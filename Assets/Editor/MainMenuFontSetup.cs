using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Points every UI Text in the MainMenu scene at Henny Penny.
///
/// The menu is built from legacy UnityEngine.UI.Text, so the .ttf is assigned
/// directly - no TMP font asset needed. Run this with MainMenu open; it walks
/// inactive objects too, so the Settings and About panels are covered.
/// </summary>
public static class MainMenuFontSetup
{
    private const string FontPath = "Assets/Sprites/HennyPenny-Regular.ttf";
    private const string SceneName = "MainMenu";

    [MenuItem("Tools/Blocks Below/Apply Menu Font")]
    private static void ApplyMenuFont()
    {
        Font font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
        if (font == null)
        {
            Debug.LogError($"Font not found at {FontPath}.");
            return;
        }

        Scene scene = SceneManager.GetSceneByName(SceneName);
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError($"Open the {SceneName} scene before running this.");
            return;
        }

        int changed = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Text text in root.GetComponentsInChildren<Text>(true))
            {
                if (text.font == font)
                {
                    continue;
                }

                Undo.RecordObject(text, "Apply Menu Font");
                text.font = font;
                EditorUtility.SetDirty(text);
                changed++;
            }
        }

        if (changed > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
        }

        Debug.Log($"Applied {font.name} to {changed} Text component(s) in {SceneName}.");
    }
}
