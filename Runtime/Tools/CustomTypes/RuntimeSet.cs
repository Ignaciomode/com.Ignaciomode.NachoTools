using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "RuntimeSet", menuName = "Scriptable Objects/RuntimeSet")]
public abstract class RuntimeSet<T> : ScriptableObject, IEnumerable<T>
{
    private List<T> items = new();

    public void Add(T item)
    {
        if(!items.Contains(item))items.Add(item);
    }

    public void Remove(T item)
    {
        if(items.Contains(item)) items.Remove(item);
    }

    public void Clear()
    {
        items.Clear();
    }

    public T this[int i]
    {
        get { return items[i];}
        set { items[i] = value; }
    }

    public int Count()
    {
        return items.Count;
    }

    public List<T> Clone()
    {
        return items;
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < items.Count; i++)
        {
            yield return items[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
