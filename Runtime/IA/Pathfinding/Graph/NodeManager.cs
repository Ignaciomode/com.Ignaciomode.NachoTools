#region Explnation

// NodeManager.cs
//
// Builds and rebuilds the node graph via a jobified, asynchronous pipeline
// instead of each Node independently looping over every other node calling
// Physics.Raycast one at a time on the main thread.
//
// NodeManager is also the SOLE OWNER of the flattened, job-ready graph data
// (NodeGraphData) and the optional Theta* visibility matrix. Pathfinding
// code (e.g. PathFindingThetaStar, held by agents) never builds, rebuilds,
// or disposes this data itself - it checks out a reference via
// RetainGraph()/RetainVisibility(), uses it, then calls
// ReleaseGraph()/ReleaseVisibility() when done. That keeps a single shared
// source of truth instead of every agent baking its own copy, AND makes it
// safe to rebuild the graph while a search is still mid-flight elsewhere:
//
//   Only an in-flight AStarAsync() search is actually at risk of this - it
//   yields across frames while waiting on its job, so a RebuildGraphAsync()
//   coroutine genuinely can interleave and reach its disposal step before
//   the search finishes. (The synchronous AStar()/ThetaStar()/AStarBatch()
//   calls schedule-then-Complete() in one call with no yield in between, so
//   nothing else can run mid-search there - Complete() blocks the main
//   thread outright, and Unity's coroutine scheduler can't interleave with
//   a blocking call.)
//
//   The fix: RebuildGraphAsync() never disposes the current graph/
//   visibility directly. It RETIRES them instead - if nothing is currently
//   using them (ref count already zero), they're disposed immediately;
//   otherwise, disposal is deferred until the last in-flight search
//   releases its reference. See RefCounted<T> at the bottom of this file.
//
// PIPELINE (see RebuildGraphAsync):
//   1. Burst job (parallel over nodes): filter candidate pairs by distance/
//      view range only - cheap, no raycasts yet.
//   2. Burst job (parallel over candidates): build a RaycastCommand for
//      each surviving pair.
//   3. RaycastCommand.ScheduleBatch: run those raycasts, batched across
//      worker threads, chained directly off stage 2's JobHandle.
//   4. Main thread: read results, assign each node's neighbours list, then
//      flatten that same adjacency info into a new graph generation for
//      pathfinding to consume.
//
// The coroutine yields (doesn't block a frame) while waiting on stages 1
// and 3, so a big rebuild spreads across frames instead of causing a
// hitch. Stage 1 -> stage 2 has one necessary sync point (see comment at
// that line) since RaycastCommand.ScheduleBatch needs to know the exact
// candidate count up front; it's a cheap, math-only phase so this is fine.
//
// REQUIRED PACKAGES (Package Manager): Collections, Burst, Jobs (built in).
//
// USAGE:
//   Nothing changes for existing callers of UpdateGraph() - it still just
//   triggers a rebuild. It's asynchronous internally (runs as a coroutine)
//   and safe to call again while a rebuild is already in progress - calls
//   coalesce into one follow-up rebuild rather than stacking overlapping
//   coroutines. It's now ALSO safe to call while agents have pathfinding
//   searches in flight.
//
//   Agents read the graph like this (see PathFindingThetaStar - it handles
//   the retain/release dance internally, agents never touch it directly):
//     var pathfinder = new PathFindingThetaStar(nodeManager);
//     List<Node> path = pathfinder.AStar(start, goal);
//
//   Optional, only if you use ThetaStar(): call once after the graph is
//   ready (e.g. after the first onGraphUpdate fires). Note this creates a
//   new visibility generation each time, same retire/dispose lifecycle:
//     nodeManager.BakeVisibility();

#endregion
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public class NodeManager : MonoBehaviour
{
    [SerializeField] private NodeManagerVariable reference;
    public Node[] allNodes;

    // Fires AFTER a rebuild finishes - the graph is already up to date by
    // the time subscribers hear about it. (Previously fired BEFORE, as an
    // instruction telling nodes to go recompute themselves - that flow no
    // longer exists, so if anything outside this script subscribed
    // expecting that old timing, it can just read the graph directly now.)
    public event Action onGraphUpdate = delegate { };

    [SerializeField] LayerMask wallLayer;

    private bool isRebuilding;
    private bool rebuildQueued;

    // The current graph "generation" - a flattened NodeGraphData bundled
    // with the Node<->index mapping that's only meaningful relative to
    // that exact bake. Ref-counted so a rebuild can retire the old one
    // without yanking it out from under an in-flight search.
    private RefCounted<GraphSnapshot> currentGraph;

    // Optional Theta* line-of-sight matrix, same ref-counted lifecycle.
    // Invalidated (not carried over) whenever the graph topology changes,
    // since its indices are only valid against the graph they were baked
    // from - see BakeFlattenedGraph.
    private RefCounted<NativeArray<bool>> currentVisibility;

    public bool IsGraphReady => currentGraph != null;
    public bool IsVisibilityReady => currentVisibility != null;

    // Check out the current graph generation for the duration of a search.
    // MUST be paired with ReleaseGraph() when done (PathFindingThetaStar
    // handles this internally via try/finally - agents don't call these).
    public RefCounted<GraphSnapshot> RetainGraph()
    {
        currentGraph?.Retain();
        return currentGraph;
    }

    public void ReleaseGraph(RefCounted<GraphSnapshot> graph) => graph?.Release();

    public RefCounted<NativeArray<bool>> RetainVisibility()
    {
        currentVisibility?.Retain();
        return currentVisibility;
    }

    public void ReleaseVisibility(RefCounted<NativeArray<bool>> visibility) => visibility?.Release();

    private void Awake()
    {
        reference.value = this;
        allNodes = GetComponentsInChildren<Node>();
        foreach (var node in allNodes)
        {
            node.manager = this;
        }
    }

    private void Start()
    {
        UpdateGraph(); // initial build
    }

    // Call whenever nodes are added/removed/moved and neighbours need
    // recomputing. Safe to call while a rebuild is already running, and
    // safe to call while agents have searches in flight.
    public void UpdateGraph()
    {
        if (isRebuilding)
        {
            rebuildQueued = true;
            return;
        }

        StartCoroutine(RebuildGraphAsync());
    }

    public IEnumerator RebuildGraphAsync()
    {
        isRebuilding = true;

        allNodes = GetComponentsInChildren<Node>(); // picks up added/removed nodes
        var nodes = allNodes.Where(node => node != null).ToList();
        int n = nodes.Count;

        var neighbourLists = new List<Node>[n];
        for (int i = 0; i < n; i++) neighbourLists[i] = new List<Node>();

        if (n > 0)
        {
            var positions = new NativeArray<float3>(n, Allocator.Persistent);
            var viewRanges = new NativeArray<float>(n, Allocator.Persistent);
            var wallLayerMasks = new NativeArray<int>(n, Allocator.Persistent);

            for (int i = 0; i < n; i++)
            {
                positions[i] = nodes[i].transform.position;
                viewRanges[i] = nodes[i].ViewRange;
                wallLayerMasks[i] = nodes[i].WallLayer;
            }

            // Stage 1: distance-only filter. Worst case every node sees
            // every other node, so that's the safe capacity bound for the
            // output - growing a NativeList concurrently across threads
            // isn't safe, so this has to be reserved up front.
            var candidatePairs = new NativeList<int2>(n * Mathf.Max(n - 1, 1), Allocator.Persistent);

            var filterJob = new FindCandidatePairsJob
            {
                positions = positions,
                viewRanges = viewRanges,
                candidatePairs = candidatePairs.AsParallelWriter()
            };

            JobHandle filterHandle = filterJob.Schedule(n, 8);
            while (!filterHandle.IsCompleted) yield return null;
            filterHandle.Complete();

            // We need the real candidate count as a plain int on the main
            // thread to size the raycast buffers correctly -
            // RaycastCommand.ScheduleBatch takes a NativeArray, not a
            // NativeList, so there's no way to defer this the way stage 2
            // defers into stage 3. This is the one unavoidable sync point
            // in the pipeline; stage 1 is pure math with no raycasts, so
            // it's cheap.
            int candidateCount = candidatePairs.Length;

            if (candidateCount > 0)
            {
                // Stage 2: one RaycastCommand per surviving candidate.
                var commands = new NativeArray<RaycastCommand>(candidateCount, Allocator.Persistent);

                var buildCommandsJob = new BuildRaycastCommandsJob
                {
                    positions = positions,
                    wallLayerMasks = wallLayerMasks,
                    candidatePairs = candidatePairs.AsArray(),
                    commands = commands
                };

                JobHandle buildHandle = buildCommandsJob.Schedule(candidateCount, 32);

                // Stage 3: the actual raycasts - chained straight off
                // stage 2's handle, no extra sync point needed here.
                var results = new NativeArray<RaycastHit>(candidateCount, Allocator.Persistent);
                JobHandle raycastHandle = RaycastCommand.ScheduleBatch(commands, results, 32, buildHandle);

                while (!raycastHandle.IsCompleted) yield return null;
                raycastHandle.Complete();

                for (int k = 0; k < candidateCount; k++)
                {
                    if (results[k].collider != null) continue; // blocked line of sight

                    int2 pair = candidatePairs[k];
                    neighbourLists[pair.x].Add(nodes[pair.y]);
                }

                commands.Dispose();
                results.Dispose();
            }

            positions.Dispose();
            viewRanges.Dispose();
            wallLayerMasks.Dispose();
            candidatePairs.Dispose();
        }

        for (int i = 0; i < n; i++)
            nodes[i].neighbours = neighbourLists[i];

        BakeFlattenedGraph(nodes, neighbourLists);

        isRebuilding = false;
        FinishRebuild();
    }

    // Turns the same adjacency info just computed above into the flattened
    // NativeArray form pathfinding jobs need (CSR-style: neighbourStart/
    // neighbourCount index into neighbourIndices), and publishes it as a
    // new graph generation. This is the ONLY place that builds a
    // GraphSnapshot - agents never call this directly.
    private void BakeFlattenedGraph(List<Node> nodes, List<Node>[] neighbourLists)
    {
        int n = nodes.Count;
        var nodeById = new Node[n];
        var idByNode = new Dictionary<Node, int>(n);

        for (int i = 0; i < n; i++)
        {
            nodeById[i] = nodes[i];
            idByNode[nodes[i]] = i;
        }

        var flatPositions = new NativeArray<float3>(n, Allocator.Persistent);
        var flatCost = new NativeArray<int>(n, Allocator.Persistent);
        var flatBlocked = new NativeArray<bool>(n, Allocator.Persistent);
        var neighbourStart = new NativeArray<int>(n, Allocator.Persistent);
        var neighbourCount = new NativeArray<int>(n, Allocator.Persistent);

        var flatNeighbours = new List<int>();

        for (int i = 0; i < n; i++)
        {
            flatPositions[i] = nodes[i].transform.position;
            flatCost[i] = nodes[i].cost;
            flatBlocked[i] = nodes[i].blocked;

            neighbourStart[i] = flatNeighbours.Count;
            foreach (var nb in neighbourLists[i])
                flatNeighbours.Add(idByNode[nb]);
            neighbourCount[i] = flatNeighbours.Count - neighbourStart[i];
        }

        var neighbourIndices = new NativeArray<int>(flatNeighbours.Count, Allocator.Persistent);
        for (int i = 0; i < flatNeighbours.Count; i++) neighbourIndices[i] = flatNeighbours[i];

        var snapshot = new GraphSnapshot
        {
            GraphData = new NodeGraphData
            {
                positions = flatPositions,
                cost = flatCost,
                blocked = flatBlocked,
                neighbourStart = neighbourStart,
                neighbourCount = neighbourCount,
                neighbourIndices = neighbourIndices
            },
            NodeById = nodeById,
            IdByNode = idByNode
        };

        // Retire (not dispose outright) the old generation - if an
        // AStarAsync() search elsewhere is still mid-flight against it,
        // it keeps working correctly and gets disposed only once that
        // search releases it.
        currentGraph?.Retire();
        currentGraph = new RefCounted<GraphSnapshot>(snapshot);

        // Any previously baked visibility matrix is indexed against the
        // OLD node ordering - it's meaningless against this new graph, so
        // invalidate it. Call BakeVisibility() again if you need
        // ThetaStar() to keep working after a topology rebuild.
        currentVisibility?.Retire();
        currentVisibility = null;
    }

    // Call whenever blocked/cost values change (dynamic obstacles etc.)
    // without needing a full topology rebuild (no raycasting involved, so
    // this is cheap - safe to call on the main thread whenever needed).
    //
    // NOTE: unlike the topology rebuild above, this writes directly into
    // the CURRENT generation's arrays rather than publishing a new one. If
    // an AStarAsync() search is reading these same arrays on a worker
    // thread at that exact moment, Unity's job safety system will throw
    // rather than silently race - if you hit that in practice, the fix is
    // the same idea as above (publish a new generation instead of writing
    // in place); ask if you need that built out too.
    public void RefreshDynamicState()
    {
        if (currentGraph == null) return;

        var nodeById = currentGraph.Data.NodeById;
        var graphData = currentGraph.Data.GraphData;

        for (int i = 0; i < nodeById.Length; i++)
        {
            graphData.blocked[i] = nodeById[i].blocked;
            graphData.cost[i] = nodeById[i].cost;
        }
    }

    // ---------------------------------------------------------------
    // Optional: only needed if you use PathFindingThetaStar.ThetaStar().
    // Precomputes node-to-node line of sight via batched RaycastCommand,
    // same reasoning as the neighbour-finding pipeline above: Theta*
    // needs LOS checks between arbitrary node pairs mid-search, and
    // Physics.Raycast can't run inside a Burst job. Call once after the
    // graph is built (e.g. after the first onGraphUpdate).
    // ---------------------------------------------------------------
    public void BakeVisibility(float maxLOSDistance = 100f)
    {
        if (currentGraph == null) throw new InvalidOperationException("Graph isn't built yet - wait for onGraphUpdate before calling BakeVisibility().");

        var nodeById = currentGraph.Data.NodeById;
        int n = nodeById.Length;

        var commands = new NativeArray<RaycastCommand>(n * n, Allocator.TempJob);
        var results = new NativeArray<RaycastHit>(n * n, Allocator.TempJob);

        for (int i = 0; i < n; i++)
        {
            Vector3 a = nodeById[i].transform.position;
            var queryParams = new QueryParameters(
                layerMask: nodeById[i].WallLayer,
                hitBackfaces: false,
                hitTriggers: QueryTriggerInteraction.Ignore);

            for (int j = 0; j < n; j++)
            {
                int idx = i * n + j;
                if (i == j)
                {
                    commands[idx] = new RaycastCommand(a, Vector3.forward, queryParams, 0f);
                    continue;
                }

                Vector3 b = nodeById[j].transform.position;
                Vector3 delta = b - a;
                float dist = delta.magnitude;
                commands[idx] = new RaycastCommand(a, delta / dist, queryParams, dist);
            }
        }

        JobHandle handle = RaycastCommand.ScheduleBatch(commands, results, 32, default);
        handle.Complete();

        var newVisibility = new NativeArray<bool>(n * n, Allocator.Persistent);

        for (int i = 0; i < n; i++)
        {
            Vector3 a = nodeById[i].transform.position;
            for (int j = 0; j < n; j++)
            {
                int idx = i * n + j;
                if (i == j) { newVisibility[idx] = true; continue; }

                float dist = Vector3.Distance(a, nodeById[j].transform.position);
                newVisibility[idx] = results[idx].collider == null && dist <= maxLOSDistance;
            }
        }

        commands.Dispose();
        results.Dispose();

        // Same retire-not-dispose lifecycle as the graph itself - a
        // ThetaStar() search holding the old matrix keeps working until
        // it releases it.
        currentVisibility?.Retire();
        currentVisibility = new RefCounted<NativeArray<bool>>(newVisibility);
    }

    private void OnDestroy()
    {
        currentGraph?.Retire();
        currentVisibility?.Retire();
    }

    private void FinishRebuild()
    {
        onGraphUpdate();
        BakeVisibility();
        if (rebuildQueued)
        {
            rebuildQueued = false;
            StartCoroutine(RebuildGraphAsync());
        }
    }

    public Node GetNearestNode(Vector3 position)
    {
        Node nearestNode = default;
        float _currentDist = Mathf.Infinity;

        foreach (Node node in allNodes)
        {

            float dist = Vector3.Distance(position, node.transform.position);
            if (dist < _currentDist)
            {
                _currentDist = dist;
                nearestNode = node;
            }
        }

        return nearestNode;
    }

    public Node GetNearestNodeInLOS(Vector3 position)
    {
        Node nearestNode = default;
        float _currentDist = Mathf.Infinity;

        foreach (Node node in allNodes)
        {
            if (!InLOSTool.InLOS(position, node.transform.position, wallLayer)) continue;
                float dist = Vector3.Distance(position, node.transform.position);
            if (dist < _currentDist)
            {
                _currentDist = dist;
                nearestNode = node;
            }
        }

        return nearestNode;
    }
}

// ---------------------------------------------------------------
// Flattened, blittable representation of the node graph (CSR-style
// adjacency list: neighbourStart/neighbourCount index into
// neighbourIndices).
// ---------------------------------------------------------------
public struct NodeGraphData : IDisposable
{
    public NativeArray<float3> positions;
    public NativeArray<int> cost;
    public NativeArray<bool> blocked;
    public NativeArray<int> neighbourStart;
    public NativeArray<int> neighbourCount;
    public NativeArray<int> neighbourIndices;

    public void Dispose()
    {
        if (positions.IsCreated) positions.Dispose();
        if (cost.IsCreated) cost.Dispose();
        if (blocked.IsCreated) blocked.Dispose();
        if (neighbourStart.IsCreated) neighbourStart.Dispose();
        if (neighbourCount.IsCreated) neighbourCount.Dispose();
        if (neighbourIndices.IsCreated) neighbourIndices.Dispose();
    }
}

// ---------------------------------------------------------------
// Bundles the flattened graph with the Node<->index mapping that's only
// meaningful relative to that exact bake - keeps a search using one
// consistent snapshot of both for its whole lifetime, even if NodeManager
// publishes a newer generation while that search is still running.
// ---------------------------------------------------------------
public class GraphSnapshot : IDisposable
{
    public NodeGraphData GraphData;
    public Node[] NodeById;
    public Dictionary<Node, int> IdByNode;

    public void Dispose() => GraphData.Dispose();
}

// ---------------------------------------------------------------
// Generic ref-counted wrapper. Lets NodeManager swap in a new graph or
// visibility bake without disposing the old one out from under a search
// that's still using it (specifically: an in-flight AStarAsync()
// coroutine - see the file header comment for why only that one is at
// risk).
//
// Only ever touched from Unity's main thread (MonoBehaviours/coroutines,
// via PathFindingThetaStar's Retain/Release calls), never from inside a
// job, so no locking is needed here.
// ---------------------------------------------------------------
public class RefCounted<T> where T : IDisposable
{
    public readonly T Data;
    private int refCount;
    private bool retired;

    public RefCounted(T data)
    {
        Data = data;
    }

    public void Retain() => refCount++;

    public void Release()
    {
        refCount--;
        TryDispose();
    }

    // Marks this generation as no longer current. Once every in-flight
    // search holding a reference has called Release(), it disposes itself
    // automatically - immediately, if nothing was holding it to begin with.
    public void Retire()
    {
        retired = true;
        TryDispose();
    }

    private void TryDispose()
    {
        if (retired && refCount <= 0) Data.Dispose();
    }
}

// ---------------------------------------------------------------
// Stage 1: for each node i, find every other node within i's view range
// (distance only - no raycasts yet). Output is variable-length per node,
// so it's written via NativeList<T>.ParallelWriter rather than a fixed
// array. Capacity is preallocated to the worst case (n*(n-1)) since
// growing a NativeList concurrently across threads isn't safe.
// ---------------------------------------------------------------
[BurstCompile]
public struct FindCandidatePairsJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float3> positions;
    [ReadOnly] public NativeArray<float> viewRanges;

    public NativeList<int2>.ParallelWriter candidatePairs;

    public void Execute(int i)
    {
        float3 posI = positions[i];
        float rangeI = viewRanges[i];
        int n = positions.Length;

        for (int j = 0; j < n; j++)
        {
            if (j == i) continue;

            if (math.distance(posI, positions[j]) <= rangeI)
            {
                candidatePairs.AddNoResize(new int2(i, j));
            }
        }
    }
}

// ---------------------------------------------------------------
// Stage 2: turn each surviving (i, j) candidate into a RaycastCommand,
// using node i's own wall layer - matches the original InLOS() behaviour,
// which always raycast using the calling node's own _wallLayer, not a
// shared/global one.
// ---------------------------------------------------------------
[BurstCompile]
public struct BuildRaycastCommandsJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float3> positions;
    [ReadOnly] public NativeArray<int> wallLayerMasks;
    [ReadOnly] public NativeArray<int2> candidatePairs;

    public NativeArray<RaycastCommand> commands;

    public void Execute(int k)
    {
        int2 pair = candidatePairs[k];
        float3 a = positions[pair.x];
        float3 b = positions[pair.y];
        float3 delta = b - a;
        float dist = math.length(delta);

        var queryParams = new QueryParameters(
            layerMask: wallLayerMasks[pair.x],
            hitBackfaces: false,
            hitTriggers: QueryTriggerInteraction.Ignore);

        commands[k] = dist > 0.0001f
            ? new RaycastCommand(a, delta / dist, queryParams, dist)
            : new RaycastCommand(a, new float3(0f, 1f, 0f), queryParams, 0f);
    }
}