using System;

[Serializable]
public class ScriptableVariableReference<T>
{
    public bool useConstant = false;
    public T constantValue;
    public ScriptableVariable<T> Variable;

    public T Value
    {
        get => useConstant ? constantValue : Variable.value;
        set => Variable.value = value;
    }
}
