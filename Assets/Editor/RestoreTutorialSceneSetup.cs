using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-time recovery for tutorial objects found in Unity's 0 (4).unity recovery
/// scene. It runs when MainGame is open and only fills objects/references that are
/// currently missing.
/// </summary>
[InitializeOnLoad]
internal static class RestoreTutorialSceneSetup
{
    private const string MainGamePath = "Assets/Scenes/MainGame.unity";

    static RestoreTutorialSceneSetup()
    {
        EditorApplication.delayCall += RestoreIfNeeded;
    }

    [MenuItem("Tools/Restore Tutorial Scene Setup")]
    private static void RestoreIfNeeded()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != MainGamePath)
        {
            return;
        }

        tutorialManager manager = Object.FindFirstObjectByType<tutorialManager>();
        Transform delayedLocation = FindRoot(scene, "DelayedMessage");
        GameObject ghostDefense = FindRoot(scene, "GhostDefense")?.gameObject;
        bool changed = false;

        if (delayedLocation == null)
        {
            GameObject delayed = CreateRoot(scene, "DelayedMessage");
            delayed.transform.position = new Vector3(1.88f, -1.9f, 0f);
            delayedLocation = delayed.transform;
            changed = true;
        }

        if (ghostDefense == null)
        {
            ghostDefense = CreateRoot(scene, "GhostDefense");
            ghostDefense.transform.position = new Vector3(-2.613f, 0.188f, 0f);
            ghostDefense.AddComponent<GhostTower>();

            CreateGhostSprite(
                ghostDefense.transform,
                "shotgun",
                "Assets/Sprites/shotgun.png",
                new Vector3(-2.38428f, -1.19742f, 0f),
                180f);
            CreateGhostSprite(
                ghostDefense.transform,
                "Cage1",
                "Assets/Sprites/towers/IMG_1079.png",
                new Vector3(-2.38428f, -2.196f, 0f),
                0f);
            CreateGhostSprite(
                ghostDefense.transform,
                "Cage2",
                "Assets/Sprites/towers/IMG_1079.png",
                new Vector3(-2.38428f, -3.197f, 0f),
                0f);
            changed = true;
        }

        if (manager == null)
        {
            GameObject managerObject = CreateRoot(scene, "TutorialManager");
            managerObject.transform.position = new Vector3(0.09447f, 0.27824f, 0f);
            manager = managerObject.AddComponent<tutorialManager>();
            changed = true;
        }

        SerializedObject serializedManager = new SerializedObject(manager);
        changed |= AssignIfEmpty(
            serializedManager.FindProperty("delayedMessageLocation"),
            delayedLocation);
        changed |= AssignIfEmpty(
            serializedManager.FindProperty("buildingGhostTower"),
            ghostDefense);

        SerializedProperty delayedText = serializedManager.FindProperty("delayedMessage");
        if (delayedText != null && string.IsNullOrWhiteSpace(delayedText.stringValue))
        {
            delayedText.stringValue = "Capture the birds by leading them into the cages";
            changed = true;
        }

        SerializedProperty ghostText = serializedManager.FindProperty("buildingMessage");
        if (ghostText != null && string.IsNullOrWhiteSpace(ghostText.stringValue))
        {
            ghostText.stringValue =
                "Use your energy to build defenses. Like this.\n\n"
                + "Cages underneath a tower power it once you capture a bird in it.";
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        serializedManager.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log(
            "Restored the recoverable TutorialManager scene objects and references. "
            + "Save MainGame to keep them.");
    }

    private static bool AssignIfEmpty(SerializedProperty property, Object value)
    {
        if (property == null || property.objectReferenceValue != null || value == null)
        {
            return false;
        }

        property.objectReferenceValue = value;
        return true;
    }

    private static GameObject CreateRoot(Scene scene, string objectName)
    {
        GameObject created = new GameObject(objectName);
        SceneManager.MoveGameObjectToScene(created, scene);
        Undo.RegisterCreatedObjectUndo(created, "Restore tutorial scene setup");
        return created;
    }

    private static void CreateGhostSprite(
        Transform parent,
        string objectName,
        string spritePath,
        Vector3 localPosition,
        float zRotation)
    {
        GameObject child = new GameObject(objectName, typeof(SpriteRenderer));
        Undo.RegisterCreatedObjectUndo(child, "Restore tutorial ghost");
        child.transform.SetParent(parent, false);
        child.transform.localPosition = localPosition;
        child.transform.localRotation = Quaternion.Euler(0f, 0f, zRotation);
        child.GetComponent<SpriteRenderer>().sprite =
            AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
    }

    private static Transform FindRoot(Scene scene, string objectName)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i].name == objectName)
            {
                return roots[i].transform;
            }
        }

        return null;
    }
}
