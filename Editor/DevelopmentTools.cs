using UnityEditor;
using UnityEngine;

public static class DevelopmentTools
{
    [MenuItem("Tools/Delete PlayerPrefs")]
    public static void DeletePlayerPrefs()
    {
        PlayerPrefs.DeleteAll();
    }
}
