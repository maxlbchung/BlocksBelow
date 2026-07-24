using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class PlayerAnimationSetup
{
    private const string AnimationFolder = "Assets/Animations";
    private const string IdleClipPath = AnimationFolder + "/PlayerIdle.anim";
    private const string WalkClipPath = AnimationFolder + "/PlayerWalk.anim";
    private const string ControllerPath = AnimationFolder + "/Player.controller";
    private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";

    private static readonly string[] IdleFramePaths =
    {
        "Assets/Sprites/PlayerIdle/Idle1.png",
        "Assets/Sprites/PlayerIdle/Idle2.png",
        "Assets/Sprites/PlayerIdle/Idle3.png"
    };

    private static readonly string[] WalkFramePaths =
    {
        "Assets/Sprites/PlayerWalkCycle/PlayerWalk1.png",
        "Assets/Sprites/PlayerWalkCycle/PlayerWalk2.png",
        "Assets/Sprites/PlayerWalkCycle/PlayerWalk3.png",
        "Assets/Sprites/PlayerWalkCycle/PlayerWalk4.png",
        "Assets/Sprites/PlayerWalkCycle/PlayerWalk5.png",
        "Assets/Sprites/PlayerWalkCycle/PlayerWalk6.png"
    };

    [InitializeOnLoadMethod]
    private static void SetUpAutomaticallyWhenNeeded()
    {
        if (AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath) == null)
        {
            EditorApplication.delayCall += Setup;
        }
    }

    [MenuItem("Tools/Blocks Below/Set Up Player Animations")]
    public static void Setup()
    {
        EnsureAnimationFolderExists();

        Sprite[] idleFrames = ImportFrames(IdleFramePaths);
        Sprite[] walkFrames = ImportFrames(WalkFramePaths);

        AnimationClip idleClip = CreateSpriteClip(IdleClipPath, idleFrames, 5f);
        AnimationClip walkClip = CreateSpriteClip(WalkClipPath, walkFrames, 12f);
        AnimatorController controller = CreateController(idleClip, walkClip);

        if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) != null)
        {
            AttachToPlayerPrefab(controller, idleFrames[0]);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("Player idle and walk animations were created and attached to the Player prefab.");
    }

    public static void ValidateGeneratedSetup()
    {
        AnimatorController controller =
            AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        AnimationClip idleClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdleClipPath);
        AnimationClip walkClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(WalkClipPath);
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);

        if (controller == null || idleClip == null || walkClip == null || playerPrefab == null)
        {
            throw new MissingReferenceException("One or more player animation assets are missing.");
        }

        Animator animator = playerPrefab.GetComponent<Animator>();
        SpriteRenderer renderer = playerPrefab.GetComponent<SpriteRenderer>();
        if (animator == null
            || animator.runtimeAnimatorController != controller
            || renderer == null
            || renderer.sprite == null)
        {
            throw new MissingReferenceException(
                "The Player prefab animation components are not connected correctly.");
        }

        Debug.Log("Player animation validation passed.");
    }

    private static void EnsureAnimationFolderExists()
    {
        if (!AssetDatabase.IsValidFolder(AnimationFolder))
        {
            AssetDatabase.CreateFolder("Assets", "Animations");
        }
    }

    private static Sprite[] ImportFrames(string[] paths)
    {
        Sprite[] sprites = new Sprite[paths.Length];

        for (int i = 0; i < paths.Length; i++)
        {
            TextureImporter importer = AssetImporter.GetAtPath(paths[i]) as TextureImporter;
            if (importer == null)
            {
                throw new FileNotFoundException("Could not find player animation frame.", paths[i]);
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 400f;
            // All source frames share a 1229x1714 canvas. Anchor them around
            // the original player's visual center so frame changes do not
            // jitter while the character stays centered on its collider.
            TextureImporterSettings textureSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(textureSettings);
            textureSettings.spriteAlignment = (int)SpriteAlignment.Custom;
            textureSettings.spritePivot = new Vector2(524.5f / 1229f, 572.5f / 1714f);
            importer.SetTextureSettings(textureSettings);
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            sprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>(paths[i]);
            if (sprites[i] == null)
            {
                throw new MissingReferenceException("Could not load imported sprite: " + paths[i]);
            }
        }

        return sprites;
    }

    private static AnimationClip CreateSpriteClip(string path, Sprite[] frames, float frameRate)
    {
        AssetDatabase.DeleteAsset(path);

        AnimationClip clip = new AnimationClip
        {
            frameRate = frameRate
        };

        EditorCurveBinding binding = new EditorCurveBinding
        {
            path = string.Empty,
            type = typeof(SpriteRenderer),
            propertyName = "m_Sprite"
        };

        ObjectReferenceKeyframe[] keyframes = new ObjectReferenceKeyframe[frames.Length];
        for (int i = 0; i < frames.Length; i++)
        {
            keyframes[i] = new ObjectReferenceKeyframe
            {
                time = i / frameRate,
                value = frames[i]
            };
        }

        AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        AssetDatabase.CreateAsset(clip, path);
        return clip;
    }

    private static AnimatorController CreateController(
        AnimationClip idleClip,
        AnimationClip walkClip)
    {
        AssetDatabase.DeleteAsset(ControllerPath);
        AnimatorController controller =
            AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("IsWalking", AnimatorControllerParameterType.Bool);

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
        AnimatorState idleState = stateMachine.AddState("Idle");
        idleState.motion = idleClip;
        AnimatorState walkState = stateMachine.AddState("Walk");
        walkState.motion = walkClip;
        stateMachine.defaultState = idleState;

        AnimatorStateTransition toWalk = idleState.AddTransition(walkState);
        ConfigureTransition(toWalk);
        toWalk.AddCondition(AnimatorConditionMode.If, 0f, "IsWalking");

        AnimatorStateTransition toIdle = walkState.AddTransition(idleState);
        ConfigureTransition(toIdle);
        toIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsWalking");

        return controller;
    }

    private static void ConfigureTransition(AnimatorStateTransition transition)
    {
        transition.hasExitTime = false;
        transition.hasFixedDuration = true;
        transition.duration = 0.05f;
    }

    private static void AttachToPlayerPrefab(
        RuntimeAnimatorController controller,
        Sprite idleSprite)
    {
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            Animator animator = prefabRoot.GetComponent<Animator>();
            if (animator == null)
            {
                animator = prefabRoot.AddComponent<Animator>();
            }

            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            SpriteRenderer renderer = prefabRoot.GetComponent<SpriteRenderer>();
            renderer.sprite = idleSprite;
            renderer.color = Color.white;

            Component playerControllerComponent = prefabRoot.GetComponent("PlayerController");
            if (playerControllerComponent == null)
            {
                throw new MissingComponentException(
                    "The Player prefab does not have a PlayerController component.");
            }

            SerializedObject playerController = new SerializedObject(playerControllerComponent);
            playerController.FindProperty("animator").objectReferenceValue = animator;
            playerController.FindProperty("spriteRenderer").objectReferenceValue = renderer;
            playerController.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, PlayerPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }
}
