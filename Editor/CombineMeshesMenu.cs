using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class CombineMeshesMenu
{
    // Ctrl/Cmd + Shift + M
    [MenuItem("Tools/Combine Selected Meshes %#M")]
    public static void CombineSelectedMeshes()
    {
        var meshFilters = Selection.gameObjects
            .Select(go => go.GetComponent<MeshFilter>())
            .Where(mf => mf != null && mf.sharedMesh != null)
            .ToArray();

        if (meshFilters.Length < 2)
        {
            EditorUtility.DisplayDialog("Combine Meshes",
                "Select at least two GameObjects that have a MeshFilter with a mesh.", "OK");
            return;
        }

        // Group combine instances by material so submeshes stay mapped to their materials.
        var materials = new List<Material>();
        var perMaterial = new Dictionary<Material, List<CombineInstance>>();

        foreach (var mf in meshFilters)
        {
            var mesh = mf.sharedMesh;
            var renderer = mf.GetComponent<MeshRenderer>();
            var sharedMats = renderer != null ? renderer.sharedMaterials : new Material[0];

            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var mat = sub < sharedMats.Length ? sharedMats[sub] : null;
                if (!perMaterial.TryGetValue(mat, out var list))
                {
                    list = new List<CombineInstance>();
                    perMaterial[mat] = list;
                    materials.Add(mat);
                }

                list.Add(new CombineInstance
                {
                    mesh = mesh,
                    subMeshIndex = sub,
                    transform = mf.transform.localToWorldMatrix
                });
            }
        }

        // One combined submesh per material.
        var subMeshes = new List<Mesh>();
        foreach (var mat in materials)
        {
            var sub = new Mesh();
            sub.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            sub.CombineMeshes(perMaterial[mat].ToArray(), true, true);
            subMeshes.Add(sub);
        }

        var finalMesh = new Mesh { name = "CombinedMesh" };
        finalMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        finalMesh.CombineMeshes(
            subMeshes.Select(m => new CombineInstance { mesh = m, transform = Matrix4x4.identity }).ToArray(),
            false, false);
        finalMesh.RecalculateBounds();

        foreach (var m in subMeshes)
            Object.DestroyImmediate(m);

        var combined = new GameObject("Combined Mesh");
        Undo.RegisterCreatedObjectUndo(combined, "Combine Selected Meshes");
        combined.AddComponent<MeshFilter>().sharedMesh = finalMesh;
        combined.AddComponent<MeshRenderer>().sharedMaterials = materials.ToArray();

        Selection.activeGameObject = combined;
    }
}
