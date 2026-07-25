using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds one prefab per tower type and points the shop's offers at them.
///
/// The specs below hold the values the shop used to carry in its own inspector fields,
/// captured from testGame before those fields were removed. Re-running rebuilds every
/// prefab in place, so scene references survive.
///
/// Gameplay numbers (damage, fire rate, ...) live in each spec's Stats block; edit
/// them there and re-run so tuning survives a rebuild instead of being reset to the
/// script defaults.
/// </summary>
public static class TowerPrefabBuilder
{
    // A folder of its own: Assets/Prefabs/Towers already holds hand-made prefabs
    // (Sawblade is the saw projectile, not the tower) that must not be overwritten.
    private const string PrefabFolder = "Assets/Prefabs/Towers/Shop";

    private enum TowerKind { Basic, Shotgun, SawBlade, Fan, Energy, Cage, Scaffolding, Tesla }

    private readonly struct AssetRef
    {
        public readonly string Guid;
        public readonly long FileId;

        public AssetRef(string guid, long fileId)
        {
            Guid = guid;
            FileId = fileId;
        }

        public bool IsSet => !string.IsNullOrEmpty(Guid);
    }

    private readonly struct StatValue
    {
        public readonly string Field;
        public readonly float Value;

        public StatValue(string field, float value)
        {
            Field = field;
            Value = value;
        }
    }

    private sealed class TowerSpec
    {
        public string OfferName;
        public TowerKind Kind;
        public AssetRef Sprite;
        public bool Rotatable;
        public Vector2 AimDirection = Vector2.left;
        public bool SupportPiece;
        public bool WalkThrough;
        // Shown in the shop's description box while this piece is selected.
        public string Description = string.Empty;
        public AssetRef Projectile;
        public AssetRef SawBlade;
        public AssetRef BrokenSprite;
        public AssetRef ShootSfx;
        public AssetRef[] Frames = new AssetRef[0];
        public float FrameDuration = 0.05f;
        // Gameplay numbers baked into the prefab, keyed by the serialized field
        // name on the tower behaviour. Any [SerializeField] number works here.
        public StatValue[] Stats = new StatValue[0];
    }

    private static StatValue Stat(string field, float value) => new StatValue(field, value);

    private static readonly AssetRef ProjectilePrefab =
        new AssetRef("121536fd1d8cd56489a1e86a61272bac", 6591939630860976102L);

    private static readonly TowerSpec[] Specs =
    {
        new TowerSpec
        {
            OfferName = "Basic",
            Kind = TowerKind.Basic,
            Sprite = new AssetRef("576297a92d40ef5429eda20a5509643b", 3100948675293211672L),
            Rotatable = true,
            AimDirection = Vector2.left,
            Description = "Fires a single bolt straight ahead once a round starts. "
                + "Every full cage below adds one shot per second, so a taller stack fires faster.",
            Projectile = ProjectilePrefab,
            ShootSfx = new AssetRef("c4bd30de22dc6b04e917a497093819e9", 8300000L),
            Frames = new[]
            {
                new AssetRef("576297a92d40ef5429eda20a5509643b", 3100948675293211672L),
                new AssetRef("ae6c10f0cecee6e44a66d3bc68046ab8", -3892938746899902262L),
                new AssetRef("5768fc3bb791c974c84ac22597fe5e09", -3140887939690939534L),
            },
            Stats = new[]
            {
                Stat("damage", 1f),
                Stat("fireRatePerPower", 1f),
            },
        },
        new TowerSpec
        {
            OfferName = "Shotgun",
            Kind = TowerKind.Shotgun,
            Sprite = new AssetRef("49c7fb3c5d83ad24999f7b8f4430c6a3", -5720788984432554497L),
            Rotatable = true,
            AimDirection = Vector2.left,
            Description = "Fires a wide fan of pellets in one blast. One pellet per full "
                + "cage below, so the stack decides how much of the spread is covered.",
            Projectile = ProjectilePrefab,
            ShootSfx = new AssetRef("ba8ba2a767922f143baee7fbb577a988", 8300000L),
            Frames = new[]
            {
                new AssetRef("49c7fb3c5d83ad24999f7b8f4430c6a3", -5720788984432554497L),
                new AssetRef("2525a5f2a7376b147a139b2610464a1d", -6105952530712206989L),
                new AssetRef("c56d7a226c8b5084c9b4695c214cd9bb", 1819619556249390510L),
            },
            Stats = new[]
            {
                Stat("damage", 1f),
                Stat("fireRate", 1f),
                Stat("spread", 30f),
            },
        },
        new TowerSpec
        {
            OfferName = "Sawblade",
            Kind = TowerKind.SawBlade,
            Sprite = new AssetRef("7cd10c01d0f42934f8755a341ad0ad97", -5897110159133509205L),
            SawBlade = new AssetRef("2a7857f5961cfcc4f927c40ae9a8b8f6", 6163622273882598567L),
            Description = "Spins saw blades on a wide orbit, cutting anything they pass "
                + "through. One blade per full cage below.",
            Stats = new[]
            {
                Stat("damage", 1f),
                Stat("orbitRadius", 3f),
                Stat("orbitSpeed", 90f),
            },
        },
        new TowerSpec
        {
            OfferName = "Fan",
            Kind = TowerKind.Fan,
            Sprite = new AssetRef("38cc522e0b7c13a46bedc7f4647d7939", -4743906769504856869L),
            Rotatable = true,
            AimDirection = Vector2.right,
            Description = "Blows a cone of wind that shoves enemies back and can carry "
                + "the player. Each full cage below makes the gust stronger.",
            Stats = new[]
            {
                Stat("forcePerPowerLevel", 3f),
                Stat("playerForceMultiplier", 2.5f),
            },
        },
        new TowerSpec
        {
            OfferName = "Energy Producer",
            Kind = TowerKind.Energy,
            Sprite = new AssetRef("0cce292fa3659e94ea2060b87b0cd5ac", 2244537257139846633L),
            Description = "Pays out energy at the end of every round. The payout scales with "
                + "the number of full cages below it, and nothing is earned without one.",
            Stats = new[]
            {
                Stat("energyPerPower", 100f),
            },
        },
        new TowerSpec
        {
            OfferName = "Cage",
            Kind = TowerKind.Cage,
            Sprite = new AssetRef("8ed378ed21fe6ea4fb3b2358aa60f9c3", 5817013890515777238L),
            SupportPiece = true,
            Description = "Traps an enemy that walks into it. A full cage powers every tower "
                + "stacked above it, and can be repaired once an enemy breaks out.",
            // Sprites/towers/IMG_1180 - the same cage with its bars bent apart.
            BrokenSprite = new AssetRef("617ba9ddc793cc441b1e0bb2ae2e3dee", -4290933271646154427L),
        },
        new TowerSpec
        {
            OfferName = "Scaffolding",
            Kind = TowerKind.Scaffolding,
            Sprite = new AssetRef("b1eafcaffeb124f4f8003011b7b09507", 7032804871889132505L),
            SupportPiece = true,
            WalkThrough = true,
            Description = "Cheap footing to build on and climb. It carries no power of its "
                + "own, and the player can walk straight through it.",
        },
        new TowerSpec
        {
            OfferName = "Tesla",
            Kind = TowerKind.Tesla,
            Sprite = new AssetRef("e9c8c57590f4ced44ac77b2685496b0f", 2659575502240807182L),
            Description = "Zaps the nearest enemy and chains onward from there. Power N hits "
                + "exactly N enemies, so every extra full cage below adds one more target.",
            Stats = new[]
            {
                Stat("damage", 3f),
                Stat("zapInterval", 1f),
                Stat("initialTargetRadius", 5f),
                Stat("chainRadius", 5f),
            },
        },
    };

    private const float CageCaptureRadius = 0.75f;

    [MenuItem("Tools/Towers/Build Tower Prefabs")]
    public static void BuildTowerPrefabs()
    {
        EnsureFolder();

        var report = new StringBuilder();
        var built = new Dictionary<string, GameObject>();

        foreach (TowerSpec spec in Specs)
        {
            GameObject prefab = BuildPrefab(spec, report);
            if (prefab != null)
            {
                built[spec.OfferName] = prefab;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        int assigned = AssignToShop(built, report);

        Debug.Log($"Tower prefabs: {built.Count} built, {assigned} offers wired.\n{report}");
        EditorUtility.DisplayDialog(
            "Build Tower Prefabs",
            $"{built.Count} prefab(s) built in {PrefabFolder}.\n" +
            $"{assigned} shop offer(s) wired up.\n\n" +
            "Save the scene to keep the offer assignments.\n\nSee the Console for details.",
            "OK");
    }

    private static GameObject BuildPrefab(TowerSpec spec, StringBuilder report)
    {
        Sprite sprite = Load<Sprite>(spec.Sprite);
        if (sprite == null)
        {
            report.AppendLine($"skipped  {spec.OfferName}: sprite {spec.Sprite.Guid} not found");
            return null;
        }

        var tower = new GameObject(spec.OfferName);

        try
        {
            tower.tag = spec.Kind == TowerKind.Cage ? "cage" : "tower";

            SpriteRenderer renderer = tower.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingLayerName = "Towers";
            renderer.sortingOrder = 1;
            if (spec.Kind == TowerKind.Scaffolding)
            {
                Color faded = renderer.color;
                faded.a = 0.5f;
                renderer.color = faded;
            }

            BoxCollider2D box = tower.AddComponent<BoxCollider2D>();
            box.size = Vector2.one;
            box.isTrigger = spec.Kind == TowerKind.Scaffolding;

            AddBehaviour(tower, spec);
            AddPlacementInfo(tower, spec);

            // Cages hold enemies rather than standing on them.
            if (spec.Kind != TowerKind.Cage)
            {
                tower.AddComponent<TowerCageStack>();
            }

            if (spec.Kind == TowerKind.Scaffolding)
            {
                AddOneWayPlatforms(tower.transform);
            }

            SetLayerRecursively(tower, LayerMask.NameToLayer("Wall"));

            string path = $"{PrefabFolder}/{spec.OfferName}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(tower, path, out bool success);
            if (!success || prefab == null)
            {
                report.AppendLine($"skipped  {spec.OfferName}: could not write {path}");
                return null;
            }

            report.AppendLine($"built    {spec.OfferName}  ->  {path}");
            return prefab;
        }
        finally
        {
            Object.DestroyImmediate(tower);
        }
    }

    private static void AddBehaviour(GameObject tower, TowerSpec spec)
    {
        MonoBehaviour behaviour = null;
        switch (spec.Kind)
        {
            case TowerKind.Basic:
                behaviour = tower.AddComponent<BasicTower>();
                Apply(behaviour, settings =>
                {
                    settings.FindProperty("projectilePrefab").objectReferenceValue =
                        Load<Projectile>(spec.Projectile);
                    settings.FindProperty("shootSfx").objectReferenceValue = Load<AudioClip>(spec.ShootSfx);
                });
                AddShootAnimation(tower, spec);
                break;

            case TowerKind.Shotgun:
                behaviour = tower.AddComponent<ShotgunTower>();
                Apply(behaviour, settings =>
                {
                    settings.FindProperty("projectilePrefab").objectReferenceValue =
                        Load<Projectile>(spec.Projectile);
                    settings.FindProperty("shootSfx").objectReferenceValue = Load<AudioClip>(spec.ShootSfx);
                });
                AddShootAnimation(tower, spec);
                break;

            case TowerKind.SawBlade:
                behaviour = tower.AddComponent<SawBladeTower>();
                Apply(behaviour, settings =>
                    settings.FindProperty("sawPrefab").objectReferenceValue = Load<GameObject>(spec.SawBlade));
                break;

            case TowerKind.Fan:
                behaviour = tower.AddComponent<FanTower>();
                break;

            case TowerKind.Energy:
                behaviour = tower.AddComponent<EnergyTower>();
                break;

            case TowerKind.Cage:
                behaviour = tower.AddComponent<CageTower>();
                Apply(behaviour, settings =>
                {
                    settings.FindProperty("intactSprite").objectReferenceValue = Load<Sprite>(spec.Sprite);
                    settings.FindProperty("brokenSprite").objectReferenceValue = Load<Sprite>(spec.BrokenSprite);
                    settings.FindProperty("captureRadius").floatValue = CageCaptureRadius;
                });

                CircleCollider2D captureTrigger = tower.AddComponent<CircleCollider2D>();
                captureTrigger.isTrigger = true;
                captureTrigger.radius = CageCaptureRadius;
                break;

            case TowerKind.Scaffolding:
                // Scaffolding intentionally has no behaviour of its own.
                break;

            case TowerKind.Tesla:
                behaviour = tower.AddComponent<TeslaTower>();
                break;
        }

        if (behaviour != null && spec.Stats.Length > 0)
        {
            Apply(behaviour, settings => ApplyStats(settings, spec));
        }
    }

    private static void ApplyStats(SerializedObject settings, TowerSpec spec)
    {
        foreach (StatValue stat in spec.Stats)
        {
            SerializedProperty property = settings.FindProperty(stat.Field);
            if (property == null)
            {
                Debug.LogWarning(
                    $"{spec.OfferName}: no serialized field '{stat.Field}' on " +
                    $"{settings.targetObject.GetType().Name}, stat skipped.");
                continue;
            }

            if (property.propertyType == SerializedPropertyType.Integer)
            {
                property.intValue = Mathf.RoundToInt(stat.Value);
            }
            else
            {
                property.floatValue = stat.Value;
            }
        }
    }

    private static void AddShootAnimation(GameObject tower, TowerSpec spec)
    {
        var frames = new List<Sprite>();
        foreach (AssetRef frame in spec.Frames)
        {
            Sprite loaded = Load<Sprite>(frame);
            if (loaded != null)
            {
                frames.Add(loaded);
            }
        }

        if (frames.Count == 0)
        {
            return;
        }

        Apply(tower.AddComponent<TowerShootAnimation>(), settings =>
        {
            SerializedProperty frameList = settings.FindProperty("frames");
            frameList.arraySize = frames.Count;
            for (int i = 0; i < frames.Count; i++)
            {
                frameList.GetArrayElementAtIndex(i).objectReferenceValue = frames[i];
            }

            settings.FindProperty("frameDuration").floatValue = spec.FrameDuration;
            settings.FindProperty("sortFramesByName").boolValue = true;
        });
    }

    private static void AddPlacementInfo(GameObject tower, TowerSpec spec)
    {
        Apply(tower.AddComponent<TowerPlacementInfo>(), settings =>
        {
            settings.FindProperty("rotatable").boolValue = spec.Rotatable;
            settings.FindProperty("aimDirection").vector2Value = spec.AimDirection;
            settings.FindProperty("supportPiece").boolValue = spec.SupportPiece;
            settings.FindProperty("walkThrough").boolValue = spec.WalkThrough;
            settings.FindProperty("description").stringValue = spec.Description ?? string.Empty;
        });
    }

    private static void AddOneWayPlatforms(Transform parent)
    {
        CreateOneWayEdge(parent, "Top Platform", 0.5f);
        CreateOneWayEdge(parent, "Bottom Platform", -0.5f);
    }

    private static void CreateOneWayEdge(Transform parent, string objectName, float localY)
    {
        var platform = new GameObject(objectName);
        platform.transform.SetParent(parent, false);
        platform.transform.localPosition = new Vector3(0f, localY, 0f);

        EdgeCollider2D edge = platform.AddComponent<EdgeCollider2D>();
        edge.points = new[] { new Vector2(-0.5f, 0f), new Vector2(0.5f, 0f) };
        edge.usedByEffector = true;

        PlatformEffector2D effector = platform.AddComponent<PlatformEffector2D>();
        effector.useOneWay = true;
        effector.useSideFriction = false;
        effector.surfaceArc = 180f;
    }

    /// <summary>Points the open scene's shop offers at the freshly built prefabs, matched by name.</summary>
    private static int AssignToShop(Dictionary<string, GameObject> built, StringBuilder report)
    {
        TowerShopUI shop = Object.FindFirstObjectByType<TowerShopUI>(FindObjectsInactive.Include);
        if (shop == null)
        {
            report.AppendLine("no TowerShopUI in the open scene, so no offers were wired");
            return 0;
        }

        int assigned = 0;
        foreach (TowerShopUI.TowerOffer offer in shop.Towers)
        {
            if (offer == null)
            {
                continue;
            }

            if (built.TryGetValue(offer.displayName, out GameObject prefab))
            {
                offer.prefab = prefab;
                assigned++;
            }
            else
            {
                report.AppendLine($"unwired  offer '{offer.displayName}' has no prefab of that name");
            }
        }

        if (assigned > 0)
        {
            EditorUtility.SetDirty(shop);
            EditorSceneManager.MarkSceneDirty(shop.gameObject.scene);
        }

        return assigned;
    }

    private static void Apply(Object target, System.Action<SerializedObject> configure)
    {
        var settings = new SerializedObject(target);
        configure(settings);
        settings.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// Resolves an asset by GUID and local file id, so a specific sprite inside a
    /// multi-sprite texture is picked rather than whichever one happens to be first.
    /// </summary>
    private static T Load<T>(AssetRef reference) where T : Object
    {
        if (!reference.IsSet)
        {
            return null;
        }

        string path = AssetDatabase.GUIDToAssetPath(reference.Guid);
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset is T typed
                && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out _, out long fileId)
                && fileId == reference.FileId)
            {
                return typed;
            }
        }

        return AssetDatabase.LoadAssetAtPath<T>(path);
    }

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        if (target == null || layer < 0)
        {
            return;
        }

        target.layer = layer;
        foreach (Transform child in target.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    private static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
        {
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        }

        if (!AssetDatabase.IsValidFolder("Assets/Prefabs/Towers"))
        {
            AssetDatabase.CreateFolder("Assets/Prefabs", "Towers");
        }

        if (!AssetDatabase.IsValidFolder(PrefabFolder))
        {
            AssetDatabase.CreateFolder("Assets/Prefabs/Towers", "Shop");
        }
    }
}
