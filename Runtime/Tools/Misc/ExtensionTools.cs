using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;


namespace BaseScripts.Tools.Misc
{
    public static class ExtensionTools
    {
        public static IEnumerator DelayedCallback(float delay, Action callback)
        {
            yield return new WaitForSeconds(delay);
            callback();
        }

        public static T RandomInCollection<T>(this IList<T> collection)
        {
            return collection[Random.Range(0, collection.Count)];
        } 
        
        public static Vector3 RandomPointInBox(Vector3 center, Vector3 size) {

            return center + new Vector3(
                (Random.value - 0.5f) * size.x,
                (Random.value - 0.5f) * size.y,
                (Random.value - 0.5f) * size.z
            );
        }

        public static float Percentage(float value, float maxValue)
        {
            return (value / maxValue) * 100;
        }
        
    }
}
