using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ObjectPool : MonoBehaviour
{
    public List<GameObject> objectPoolCollection = new List<GameObject>();
    [SerializeField] private int objectPoolAmount = default(int);
    [SerializeField] private GameObject objectPrefab = default(GameObject);

    private void Awake()
    {
        objectPoolCollection.Clear();
        InstantiateObjects();
    }

    private void InstantiateObjects()
    {
        GameObject newObject = null;
        Transform targetParent = transform;

        for (int i = 0; i < objectPoolAmount; i++)
        {
            if (newObject == null)
            {
                newObject = Instantiate(objectPrefab);
                newObject.name = $"{objectPrefab.name}-{i}";
                newObject.transform.SetParent(targetParent);
            }
            else
            {
                newObject = Instantiate(newObject, targetParent);
                newObject.name = $"{objectPrefab.name}-{i}";
            }
            objectPoolCollection.Add(newObject);
            newObject.SetActive(false);
        }
    }
}
