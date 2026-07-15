using System;
using UnityEngine;

public class ForceInterfaceAttribute : PropertyAttribute
{
    public readonly Type interfaceType;

    public ForceInterfaceAttribute(Type interfaceType)
    {
        this.interfaceType = interfaceType;
    }
}
