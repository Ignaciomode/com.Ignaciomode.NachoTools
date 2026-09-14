using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.IO;

public class SceneSwitcherWindow : EditorWindow
{
    [MenuItem("Tools/Scene Switcher")]
    public static void ShowWindow()
    {
        GetWindow<SceneSwitcherWindow>("Scene Switcher");
    }

    private void OnGUI()
    {
        GUILayout.Label("Scenes in Build Settings", EditorStyles.boldLabel);

        EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
        if (scenes.Length == 0)
        {
            GUILayout.Label("No scenes added to the Build Settings.");
            if (GUILayout.Button("Open Build Settings"))
            {
                GetWindow(System.Type.GetType("UnityEditor.BuildPlayerWindow,UnityEditor"));
            }
            return;
        }

        Vector2 scrollPos = Vector2.zero;
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        foreach (EditorBuildSettingsScene scene in scenes)
        {
            string sceneName = Path.GetFileNameWithoutExtension(scene.path);

            // Show a visual cue if the scene is the current active scene
            bool isActiveScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path == scene.path;

            EditorGUILayout.BeginHorizontal();

            GUI.enabled = !isActiveScene;
            if (GUILayout.Button(sceneName, GUILayout.Height(25)))
            {
                if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    EditorSceneManager.OpenScene(scene.path);
                }
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();
    }
}
