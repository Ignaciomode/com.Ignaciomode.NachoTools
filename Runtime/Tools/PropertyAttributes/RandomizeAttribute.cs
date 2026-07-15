using UnityEngine;

public class RandomizeAttribute: PropertyAttribute
{
    public readonly float minValue, maxValue;

    public RandomizeAttribute(float minValue, float maxValue)
    {
        this.minValue = minValue;
        this.maxValue = maxValue;
    }
}
