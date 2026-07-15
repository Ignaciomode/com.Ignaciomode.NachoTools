using System;
using UnityEngine;

[Serializable]
[CreateAssetMenu(fileName = "ScriptableVariable", menuName = "Scriptable Objects/ScriptableVariable")]
public abstract class ScriptableVariable<T> : ScriptableObject
{
    public T value;
    [SerializeField] private T startingValue;

    public void Reset()
    {
        value = startingValue;
    }
}
