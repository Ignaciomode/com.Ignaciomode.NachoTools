using UnityEngine;

public class UseConstantValue : PropertyAttribute
{
    public bool choice;
    public float constantValue;
    
    public UseConstantValue(bool choice, float constantValue)
    {
        this.choice = choice;
        this.constantValue = constantValue;
    }
}
