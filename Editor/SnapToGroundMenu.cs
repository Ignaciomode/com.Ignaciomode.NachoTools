using System.Linq;
using UnityEditor;
using UnityEngine;

public static class SnapToGroundMenu
{
    
    [MenuItem("Tools/Snap to ground/ Snap Selected Game object to ground #P")]
    public static void SnapToGround()
    {
        //GameObject selectedGameObject = Selection.activeGameObject;

        foreach (var selectedGameObject in Selection.gameObjects)
        {
            Ray ray = new Ray(selectedGameObject.GetLowestVertex(), Vector3.down);
            if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity))
            {
                Vector3 dirY = hit.point - selectedGameObject.GetLowestVertex();
                selectedGameObject.transform.position += new Vector3(0, dirY.y , 0);
            }
        }
    }

    public static Vector3 GetLowestVertex(this GameObject target)
    {
        if (target.TryGetComponent(out MeshFilter meshFilter))
        {
            var list = meshFilter.sharedMesh.vertices.Select(target.transform.TransformPoint).OrderBy(v => v.y).ToList();
            return list[0];
        }
        else
        {
            return target.transform.position;
        }
    }

    [MenuItem("CONTEXT/GridPlacer/Debug RB")]
    public static void DebugRB(MenuCommand command)
    {
        var grid = (GridPlacer)command.context;
        Debug.Log(grid);
    }
    
}
