// PathFindingThetaStar.cs
//
// Jobified, Burst-compiled A* / Theta* pathfinding.
//
// OWNERSHIP: this class does NOT build, rebuild, or dispose graph data - it
// only checks out a reference to NodeManager's current graph/visibility
// generation via RetainGraph()/RetainVisibility(), uses it for one search,
// then releases it. This is what makes it safe for NodeManager to rebuild
// the graph while a search elsewhere is still in flight: a search keeps
// using the exact generation it checked out for its whole lifetime, even
// if NodeManager has already published a newer one in the meantime. See
// the ownership comment at the top of NodeManager.cs for the full picture
// of why this only actually matters for AStarAsync().
//
// WHY IT LOOKS DIFFERENT FROM A PLAIN A* SCRIPT:
// The Unity Jobs System (and Burst) can only operate on blittable structs
// and NativeContainers - it cannot touch managed classes, Transform,
// List<T>, or Dictionary<T,K>. NodeManager bakes the Node graph into flat
// NativeArrays; this class runs the search as a background job using
// integer node indices, then maps results back to Node objects so the
// public API still returns List<Node>.
//
// REQUIRED PACKAGES (Package Manager): Collections, Burst, Jobs (built in).
//
// USAGE:
//   var pathfinder = new PathFindingThetaStar(nodeManager);
//
//   // Drop-in synchronous call - fine for agents that request paths at
//   // random, unpredictable times (most common case):
//   List<Node> path = pathfinder.AStar(start, goal);
//
//   // Any-angle version - fewer/straighter waypoints. Needs
//   // nodeManager.BakeVisibility() to have been called once first:
//   List<Node> path = pathfinder.ThetaStar(start, goal);
//
//   // Or pick per call:
//   List<Node> path = pathfinder.FindPath(start, goal, useTheta: true);
//
//   // Time-sliced version - spreads the wait across frames instead of
//   // blocking. Worth it only if a single search is big enough to cause
//   // a visible stutter:
//   StartCoroutine(pathfinder.AStarAsync(start, goal, result => { path = result; }));
//
//   // Multi-agent version - schedules ALL requests before completing any
//   // of them, so they run concurrently across worker threads. Only pays
//   // off if requests actually arrive in clusters/same frame:
//   List<List<Node>> paths = pathfinder.AStarBatch(requests);

using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public class PathFindingThetaStar
{
    private readonly NodeManager nodeManager;

    public PathFindingThetaStar(NodeManager nodeManager)
    {
        if (nodeManager == null)
            throw new ArgumentNullException(nameof(nodeManager), "PathFindingThetaStar needs a live NodeManager reference - check where this is constructed and make sure that reference is assigned before this runs.");

        this.nodeManager = nodeManager;
    }

    // ---------------------------------------------------------------
    // SYNCHRONOUS A* - the default choice for agents requesting paths at
    // unpredictable times. Burst-fast, zero GC from the old
    // Dictionary/List/PriorityQueue approach, but blocks the calling
    // frame until the search completes.
    // ---------------------------------------------------------------
    public List<Node> AStar(Node start, Node goal)
    {
        var graph = nodeManager.RetainGraph();
        try
        {
            RequireGraphReady(graph);
            int startIndex = RequireNodeIndex(graph, start);
            int goalIndex = RequireNodeIndex(graph, goal);

            var graphData = graph.Data.GraphData;
            var resultPath = new NativeList<int>(Allocator.TempJob);

            var job = new AStarJob
            {
                positions = graphData.positions,
                cost = graphData.cost,
                blocked = graphData.blocked,
                neighbourStart = graphData.neighbourStart,
                neighbourCount = graphData.neighbourCount,
                neighbourIndices = graphData.neighbourIndices,
                startIndex = startIndex,
                goalIndex = goalIndex,
                resultPath = resultPath
            };

            JobHandle handle = job.Schedule();
            handle.Complete();

            var path = BuildPathFromIndices(graph, resultPath);
            resultPath.Dispose();
            return path;
        }
        finally
        {
            nodeManager.ReleaseGraph(graph);
        }
    }

    // ---------------------------------------------------------------
    // TIME-SLICED A* - schedules the job, then yields frame-by-frame
    // checking IsCompleted instead of blocking. This is the one method
    // where retaining the graph generation actually matters: it yields
    // across frames, so a NodeManager rebuild genuinely can happen while
    // this is still waiting. Holding "graph" here (rather than re-reading
    // nodeManager's current generation after the yield) guarantees this
    // search finishes against the exact data it started with, and that
    // data can't be disposed until the finally block below releases it.
    // ---------------------------------------------------------------
    public IEnumerator AStarAsync(Node start, Node goal, Action<List<Node>> onComplete)
    {
        var graph = nodeManager.RetainGraph();
        try
        {
            RequireGraphReady(graph);
            int startIndex = RequireNodeIndex(graph, start);
            int goalIndex = RequireNodeIndex(graph, goal);

            var graphData = graph.Data.GraphData;

            // Persistent, not TempJob: TempJob allocations are only valid
            // for a few frames and will throw safety errors if a search
            // takes longer.
            var resultPath = new NativeList<int>(Allocator.Persistent);

            var job = new AStarJob
            {
                positions = graphData.positions,
                cost = graphData.cost,
                blocked = graphData.blocked,
                neighbourStart = graphData.neighbourStart,
                neighbourCount = graphData.neighbourCount,
                neighbourIndices = graphData.neighbourIndices,
                startIndex = startIndex,
                goalIndex = goalIndex,
                resultPath = resultPath
            };

            JobHandle handle = job.Schedule();

            while (!handle.IsCompleted)
                yield return null;

            handle.Complete(); // near-instant, job already finished

            var path = BuildPathFromIndices(graph, resultPath);
            resultPath.Dispose();
            onComplete?.Invoke(path);
        }
        finally
        {
            nodeManager.ReleaseGraph(graph);
        }
    }

    // ---------------------------------------------------------------
    // MULTI-AGENT BATCH - schedules ONE search per request as a single
    // IJobParallelFor call, so worker threads can genuinely run multiple
    // agents' searches concurrently. Only pays off if requests actually
    // arrive together (same frame); if agents request paths at scattered,
    // unpredictable times, there's nothing to batch and AStar() is the
    // right tool instead.
    // ---------------------------------------------------------------
    public List<List<Node>> AStarBatch(List<(Node start, Node goal)> requests, int batchSize = 2)
    {
        var graph = nodeManager.RetainGraph();
        try
        {
            RequireGraphReady(graph);

            var graphData = graph.Data.GraphData;
            int n = graphData.positions.Length;
            int requestCount = requests.Count;

            var startIndices = new NativeArray<int>(requestCount, Allocator.TempJob);
            var goalIndices = new NativeArray<int>(requestCount, Allocator.TempJob);

            for (int i = 0; i < requestCount; i++)
            {
                startIndices[i] = RequireNodeIndex(graph, requests[i].start);
                goalIndices[i] = RequireNodeIndex(graph, requests[i].goal);
            }

            // A path can never contain more nodes than exist in the
            // graph, so n is a safe (if slightly generous) upper bound
            // per request.
            var resultPaths = new NativeArray<int>(requestCount * n, Allocator.TempJob);
            var resultLengths = new NativeArray<int>(requestCount, Allocator.TempJob);

            var job = new AStarBatchJob
            {
                positions = graphData.positions,
                cost = graphData.cost,
                blocked = graphData.blocked,
                neighbourStart = graphData.neighbourStart,
                neighbourCount = graphData.neighbourCount,
                neighbourIndices = graphData.neighbourIndices,
                startIndices = startIndices,
                goalIndices = goalIndices,
                maxPathLength = n,
                resultPaths = resultPaths,
                resultLengths = resultLengths
            };

            JobHandle handle = job.Schedule(requestCount, batchSize);
            handle.Complete();

            var allPaths = new List<List<Node>>(requestCount);
            for (int r = 0; r < requestCount; r++)
            {
                int length = resultLengths[r];
                int baseOffset = r * n;

                var path = new List<Node>(length);
                for (int i = 0; i < length; i++)
                    path.Add(graph.Data.NodeById[resultPaths[baseOffset + i]]);

                allPaths.Add(path);
            }

            startIndices.Dispose();
            goalIndices.Dispose();
            resultPaths.Dispose();
            resultLengths.Dispose();

            return allPaths; // same order as requests
        }
        finally
        {
            nodeManager.ReleaseGraph(graph);
        }
    }

    // ---------------------------------------------------------------
    // THETA* - any-angle version, needs nodeManager.BakeVisibility() to
    // have been called once first. Retains both the graph and the
    // visibility matrix for the search's duration, since it needs both
    // to stay consistent with each other (visibility indices are only
    // meaningful against the exact graph generation they were baked from).
    // ---------------------------------------------------------------
    public List<Node> ThetaStar(Node start, Node goal)
    {
        var graph = nodeManager.RetainGraph();
        var visibility = nodeManager.RetainVisibility();
        try
        {
            RequireGraphReady(graph);
            if (visibility == null)
                throw new InvalidOperationException("Call nodeManager.BakeVisibility() before ThetaStar().");

            int startIndex = RequireNodeIndex(graph, start);
            int goalIndex = RequireNodeIndex(graph, goal);

            var graphData = graph.Data.GraphData;
            var resultPath = new NativeList<int>(Allocator.TempJob);

            var job = new ThetaStarJob
            {
                positions = graphData.positions,
                cost = graphData.cost,
                blocked = graphData.blocked,
                neighbourStart = graphData.neighbourStart,
                neighbourCount = graphData.neighbourCount,
                neighbourIndices = graphData.neighbourIndices,
                visibility = visibility.Data,
                startIndex = startIndex,
                goalIndex = goalIndex,
                resultPath = resultPath
            };

            JobHandle handle = job.Schedule();
            handle.Complete();

            var path = BuildPathFromIndices(graph, resultPath);
            resultPath.Dispose();
            return path;
        }
        finally
        {
            nodeManager.ReleaseGraph(graph);
            nodeManager.ReleaseVisibility(visibility);
        }
    }

    // Convenience: pick the algorithm per call without the caller needing
    // to know both method names.
    public List<Node> FindPath(Node start, Node goal, bool useTheta = false)
    {
        return useTheta ? ThetaStar(start, goal) : AStar(start, goal);
    }

    private List<Node> BuildPathFromIndices(RefCounted<GraphSnapshot> graph, NativeList<int> resultPath)
    {
        var path = new List<Node>(resultPath.Length);
        for (int i = 0; i < resultPath.Length; i++)
            path.Add(graph.Data.NodeById[resultPath[i]]);
        return path;
    }

    private void RequireGraphReady(RefCounted<GraphSnapshot> graph)
    {
        if (graph == null)
            throw new InvalidOperationException("NodeManager hasn't built the graph yet - wait for onGraphUpdate before pathfinding.");
    }

    private int RequireNodeIndex(RefCounted<GraphSnapshot> graph, Node node)
    {
        if (graph == null || !graph.Data.IdByNode.TryGetValue(node, out int index))
            throw new ArgumentException($"Node '{node?.name}' isn't part of the current graph.");
        return index;
    }
}

[BurstCompile]
public struct AStarJob : IJob
{
    [ReadOnly] public NativeArray<float3> positions;
    [ReadOnly] public NativeArray<int> cost;
    [ReadOnly] public NativeArray<bool> blocked;
    [ReadOnly] public NativeArray<int> neighbourStart;
    [ReadOnly] public NativeArray<int> neighbourCount;
    [ReadOnly] public NativeArray<int> neighbourIndices;

    public int startIndex;
    public int goalIndex;

    // Output: node indices from start to goal (exclusive of start),
    // in order. Empty if no path was found.
    public NativeList<int> resultPath;

    public void Execute()
    {
        int n = positions.Length;

        var costSoFar = new NativeArray<int>(n, Allocator.Temp);
        var cameFrom = new NativeArray<int>(n, Allocator.Temp);
        var visited = new NativeArray<bool>(n, Allocator.Temp);

        for (int i = 0; i < n; i++)
        {
            costSoFar[i] = int.MaxValue;
            cameFrom[i] = -1;
        }

        var heap = new NativeMinHeap(64, Allocator.Temp);

        costSoFar[startIndex] = 0;
        heap.Push(startIndex, 0f);

        int current = -1;
        bool found = false;

        while (heap.Count > 0)
        {
            current = heap.Pop();

            // Lazy deletion: this index may have been pushed multiple
            // times with different priorities before being relaxed again.
            if (visited[current]) continue;
            visited[current] = true;

            if (current == goalIndex)
            {
                found = true;
                break;
            }

            int nStart = neighbourStart[current];
            int nCount = neighbourCount[current];

            for (int k = 0; k < nCount; k++)
            {
                int next = neighbourIndices[nStart + k];
                if (blocked[next]) continue;

                int newCost = costSoFar[current] + cost[next];

                // Fixed vs original: compare against costSoFar[next], not
                // costSoFar[current].
                if (newCost < costSoFar[next])
                {
                    costSoFar[next] = newCost;
                    cameFrom[next] = current;
                    float priority = newCost + math.distance(positions[next], positions[goalIndex]);
                    heap.Push(next, priority);
                }
            }
        }

        resultPath.Clear();

        if (found)
        {
            int node = goalIndex;
            while (node != startIndex)
            {
                resultPath.Add(node);
                node = cameFrom[node];
            }

            // Reverse in place.
            int lo = 0, hi = resultPath.Length - 1;
            while (lo < hi)
            {
                int tmp = resultPath[lo];
                resultPath[lo] = resultPath[hi];
                resultPath[hi] = tmp;
                lo++;
                hi--;
            }
        }

        costSoFar.Dispose();
        cameFrom.Dispose();
        visited.Dispose();
        heap.Dispose();
    }
}

// ---------------------------------------------------------------
// Theta*: same frontier-expansion structure as AStarJob, but each
// relaxation step checks whether current's parent has a clear line of
// sight to the neighbour. If so, it connects straight from that parent
// (skipping "current" as a waypoint entirely) using real-world distance
// as the cost instead of the graph edge cost. Otherwise it falls back
// to a normal graph-edge step, identical to A*.
// ---------------------------------------------------------------
[BurstCompile]
public struct ThetaStarJob : IJob
{
    [ReadOnly] public NativeArray<float3> positions;
    [ReadOnly] public NativeArray<int> cost;
    [ReadOnly] public NativeArray<bool> blocked;
    [ReadOnly] public NativeArray<int> neighbourStart;
    [ReadOnly] public NativeArray<int> neighbourCount;
    [ReadOnly] public NativeArray<int> neighbourIndices;
    [ReadOnly] public NativeArray<bool> visibility; // n*n, see BakeVisibility

    public int startIndex;
    public int goalIndex;

    public NativeList<int> resultPath;

    public void Execute()
    {
        int n = positions.Length;

        var costSoFar = new NativeArray<float>(n, Allocator.Temp);
        var cameFrom = new NativeArray<int>(n, Allocator.Temp);
        var visited = new NativeArray<bool>(n, Allocator.Temp);

        for (int i = 0; i < n; i++)
        {
            costSoFar[i] = float.MaxValue;
            cameFrom[i] = -1;
        }

        var heap = new NativeMinHeap(64, Allocator.Temp);

        costSoFar[startIndex] = 0f;
        cameFrom[startIndex] = startIndex; // self-parent sentinel, so LOS checks work from the very first expansion
        heap.Push(startIndex, 0f);

        int current = -1;
        bool found = false;

        while (heap.Count > 0)
        {
            current = heap.Pop();

            if (visited[current]) continue;
            visited[current] = true;

            if (current == goalIndex)
            {
                found = true;
                break;
            }

            int parentOfCurrent = cameFrom[current];

            int nStart = neighbourStart[current];
            int nCount = neighbourCount[current];

            for (int k = 0; k < nCount; k++)
            {
                int next = neighbourIndices[nStart + k];
                if (blocked[next] || visited[next]) continue;

                bool hasLineOfSight = visibility[parentOfCurrent * n + next];

                float candidateCost;
                int candidateParent;

                if (hasLineOfSight)
                {
                    // Path 2 (Theta*'s shortcut): connect straight from the
                    // grandparent, bypassing "current" as a waypoint.
                    candidateCost = costSoFar[parentOfCurrent] + math.distance(positions[parentOfCurrent], positions[next]);
                    candidateParent = parentOfCurrent;
                }
                else
                {
                    // Path 1: same as plain A*, step along the graph edge.
                    candidateCost = costSoFar[current] + cost[next];
                    candidateParent = current;
                }

                if (candidateCost < costSoFar[next])
                {
                    costSoFar[next] = candidateCost;
                    cameFrom[next] = candidateParent;
                    float priority = candidateCost + math.distance(positions[next], positions[goalIndex]);
                    heap.Push(next, priority);
                }
            }
        }

        resultPath.Clear();

        if (found)
        {
            int node = goalIndex;
            while (node != startIndex)
            {
                resultPath.Add(node);
                node = cameFrom[node];
            }

            int lo = 0, hi = resultPath.Length - 1;
            while (lo < hi)
            {
                int tmp = resultPath[lo];
                resultPath[lo] = resultPath[hi];
                resultPath[hi] = tmp;
                lo++;
                hi--;
            }
        }

        costSoFar.Dispose();
        cameFrom.Dispose();
        visited.Dispose();
        heap.Dispose();
    }
}

// ---------------------------------------------------------------
// One search per agent, run as a data-parallel job. Execute(requestIndex)
// is the same A* logic as AStarJob, just addressing its own slice of a
// shared output buffer instead of a NativeList. Each requestIndex only
// ever touches resultPaths[requestIndex * maxPathLength .. +maxPathLength),
// so the writes never overlap between threads - that's what makes
// [NativeDisableParallelForRestriction] safe here.
// ---------------------------------------------------------------
[BurstCompile]
public struct AStarBatchJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float3> positions;
    [ReadOnly] public NativeArray<int> cost;
    [ReadOnly] public NativeArray<bool> blocked;
    [ReadOnly] public NativeArray<int> neighbourStart;
    [ReadOnly] public NativeArray<int> neighbourCount;
    [ReadOnly] public NativeArray<int> neighbourIndices;

    [ReadOnly] public NativeArray<int> startIndices;
    [ReadOnly] public NativeArray<int> goalIndices;

    public int maxPathLength;

    [NativeDisableParallelForRestriction] public NativeArray<int> resultPaths;
    [NativeDisableParallelForRestriction] public NativeArray<int> resultLengths;

    public void Execute(int requestIndex)
    {
        int n = positions.Length;
        int startIndex = startIndices[requestIndex];
        int goalIndex = goalIndices[requestIndex];

        var costSoFar = new NativeArray<int>(n, Allocator.Temp);
        var cameFrom = new NativeArray<int>(n, Allocator.Temp);
        var visited = new NativeArray<bool>(n, Allocator.Temp);

        for (int i = 0; i < n; i++)
        {
            costSoFar[i] = int.MaxValue;
            cameFrom[i] = -1;
        }

        var heap = new NativeMinHeap(64, Allocator.Temp);
        costSoFar[startIndex] = 0;
        heap.Push(startIndex, 0f);

        int current = -1;
        bool found = false;

        while (heap.Count > 0)
        {
            current = heap.Pop();
            if (visited[current]) continue;
            visited[current] = true;

            if (current == goalIndex)
            {
                found = true;
                break;
            }

            int nStart = neighbourStart[current];
            int nCount = neighbourCount[current];

            for (int k = 0; k < nCount; k++)
            {
                int next = neighbourIndices[nStart + k];
                if (blocked[next]) continue;

                int newCost = costSoFar[current] + cost[next];
                if (newCost < costSoFar[next])
                {
                    costSoFar[next] = newCost;
                    cameFrom[next] = current;
                    float priority = newCost + math.distance(positions[next], positions[goalIndex]);
                    heap.Push(next, priority);
                }
            }
        }

        int baseOffset = requestIndex * maxPathLength;
        int length = 0;

        if (found)
        {
            int node = goalIndex;
            while (node != startIndex)
            {
                resultPaths[baseOffset + length] = node;
                length++;
                node = cameFrom[node];
            }

            int lo = 0, hi = length - 1;
            while (lo < hi)
            {
                int tmp = resultPaths[baseOffset + lo];
                resultPaths[baseOffset + lo] = resultPaths[baseOffset + hi];
                resultPaths[baseOffset + hi] = tmp;
                lo++;
                hi--;
            }
        }

        resultLengths[requestIndex] = length;

        costSoFar.Dispose();
        cameFrom.Dispose();
        visited.Dispose();
        heap.Dispose();
    }
}

// ---------------------------------------------------------------
// Minimal binary min-heap over NativeList, keyed by float priority.
// Replaces your PriorityQueue<Node> inside the job (jobs can't use
// managed generic collections).
// ---------------------------------------------------------------
internal struct NativeMinHeap : IDisposable
{
    private NativeList<int> items;
    private NativeList<float> priorities;

    public int Count => items.Length;

    public NativeMinHeap(int initialCapacity, Allocator allocator)
    {
        items = new NativeList<int>(initialCapacity, allocator);
        priorities = new NativeList<float>(initialCapacity, allocator);
    }

    public void Push(int item, float priority)
    {
        items.Add(item);
        priorities.Add(priority);

        int i = items.Length - 1;
        while (i > 0)
        {
            int parent = (i - 1) / 2;
            if (priorities[parent] <= priorities[i]) break;
            Swap(parent, i);
            i = parent;
        }
    }

    public int Pop()
    {
        int result = items[0];
        int last = items.Length - 1;

        items[0] = items[last];
        priorities[0] = priorities[last];
        items.RemoveAt(last);
        priorities.RemoveAt(last);

        int i = 0;
        int n = items.Length;
        while (true)
        {
            int left = 2 * i + 1;
            int right = 2 * i + 2;
            int smallest = i;

            if (left < n && priorities[left] < priorities[smallest]) smallest = left;
            if (right < n && priorities[right] < priorities[smallest]) smallest = right;
            if (smallest == i) break;

            Swap(i, smallest);
            i = smallest;
        }

        return result;
    }

    private void Swap(int a, int b)
    {
        int itemTmp = items[a];
        items[a] = items[b];
        items[b] = itemTmp;

        float prioTmp = priorities[a];
        priorities[a] = priorities[b];
        priorities[b] = prioTmp;
    }

    public void Dispose()
    {
        if (items.IsCreated) items.Dispose();
        if (priorities.IsCreated) priorities.Dispose();
    }
}

// ---------------------------------------------------------------
// WHERE TO GO NEXT
//
// 1. AStarBatch() is synchronous (Complete() called immediately) - if
//    a big batch of agents all requesting long paths in the same frame
//    starts costing noticeable frame time, the same IsCompleted-polling
//    pattern from AStarAsync() applies here too: schedule the batch,
//    yield across frames until handle.IsCompleted, then read results.
//    Ask if you want an AStarBatchAsync() built out.
//
// 2. resultPaths is sized requestCount * n as a safe upper bound on
//    path length. Fine for typical node counts/agent counts; if you
//    have a very large graph AND many simultaneous agents, that
//    buffer can get large - worth revisiting with a tighter bound
//    (e.g. a smaller max hop count) if memory becomes a concern.
//
// 3. ThetaStar() has no async/batch variant yet - it's the synchronous
//    drop-in, same tradeoffs as AStar(). If you need an AStarAsync()- or
//    AStarBatch()-style version of Theta*, it's the same pattern applied
//    to ThetaStarJob instead of AStarJob - ask if you want it built.
//
// 4. nodeManager.BakeVisibility() assumes wall geometry is static for the
//    level (matches what you said about the graph not changing at
//    runtime). If walls can move/destruct mid-level, visibility would
//    need re-baking too - worth flagging if that ever becomes a
//    requirement.