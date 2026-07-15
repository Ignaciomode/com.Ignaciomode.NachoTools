using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class InLOSTool
{
    public static bool InLOS(Vector3 start, Vector3 end, LayerMask mask)
    {
        Vector3 dir = end - start;
        return !Physics2D.Raycast(start, dir, dir.magnitude, mask);
    }
}
