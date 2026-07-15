using UnityEditor;
using UnityEngine;
using Unity.VisualScripting;
#if UNITY_EDITOR
public class GridPlacer : MonoBehaviour
{
    [SerializeField] private GameObject gridObject;
    public float gridSizeX, gridSizeY, xPadding, yPadding;
    [SerializeField] private bool VerticalGrid;
    [SerializeField] private Transform cellParent;

    public void CreateGrid(float xSize, float ySize, float xPadding, float yPadding)
    {
        for (int x = 0; x < xSize; x++)
        {
            for (int y = 0; y < ySize; y++)
            {
                var cell = PrefabUtility.InstantiatePrefab(gridObject).GameObject();
                cell.transform.position = cellParent.position;
                cell.transform.rotation = cellParent.rotation;
                
                if (cellParent) cell.transform.SetParent(cellParent, true);
                else cell.transform.SetParent(transform, true);
                if (VerticalGrid)
                    cell.transform.position = new Vector3(cell.transform.parent.position.x + x * xPadding,
                        cell.transform.parent.position.y + y * yPadding, 0);
                else
                    cell.transform.position = new Vector3(cell.transform.parent.position.x + x * xPadding,
                        0, cell.transform.parent.position.y + y * yPadding);
            }
        }
    }

    public void DestroyGrid()
    {
        var gridChildren = cellParent.GetComponentsInChildren<Transform>();
        
        for (int i = gridChildren.Length - 1; i < gridChildren.Length; i--)
        {
            if(gridChildren[i] != transform) DestroyImmediate(gridChildren[i].gameObject);
        }
    }
    
}


[CustomEditor(typeof(GridPlacer))]
public class GridPlacerCustomEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        GridPlacer reference = (GridPlacer)target;
        if (GUILayout.Button("Spawn Grid", GUILayout.Width(90f)))
        {
            reference.CreateGrid(reference.gridSizeX, reference.gridSizeY, reference.xPadding, reference.yPadding);
        }
        if (GUILayout.Button("Destroy Grid", GUILayout.Width(90f)))
        {
            reference.DestroyGrid();
        }
    }
}


#endif