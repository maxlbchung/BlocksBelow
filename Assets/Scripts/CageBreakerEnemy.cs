using System.Collections.Generic;
using TMPro;
using UnityEngine;

public sealed class CageBreakerEnemy : Enemy
{
    public enum BreakerState
    {
        /// <summary>On the field with no cage worth attacking yet, holding position until one appears.</summary>
        Waiting,
        Sneaking,
        Breaking
    }

    [Header("Sneaking")]
    [SerializeField, Min(0f)] private float moveSpeed = 4f;
    [SerializeField, Range(0f, 1f)] private float sneakingOpacity = 0.25f;
    [SerializeField, Min(0.1f)] private float spawnRadius = 12f;
    [SerializeField, Min(0f), Tooltip("How far past the cage, on the side away from the player, "
        + "the breaker plants itself before starting its countdown. Held inside the explosion "
        + "radius, so the cage is still taken by the blast.")]
    private float farSideStandoff = 1.5f;
    [SerializeField] private bool takesDamageInSneakingState;

    [Header("Breaking")]
    [SerializeField, Min(0f)] private float breakCountdown = 5f;
    [SerializeField, Min(0f)] private float explosionRadius = 3f;
    [SerializeField] private bool takesDamageInBreakingState = true;
    [SerializeField] private Vector2 countdownOffset = new Vector2(0f, 1.2f);
    [SerializeField, Min(1f)] private float countdownFontSize = 10f;
    [SerializeField] private Sprite countdownBackgroundSprite;
    [SerializeField, Min(0f)] private float countdownScreenEdgeInset = 48f;
    [SerializeField] private TextMeshPro countdownText;
    [SerializeField] private SpriteRenderer countdownBackground;
    [SerializeField] private float startExplosionAnimationTime = 0.5f;

    [Header("Explosion Effect")]
    [SerializeField, Min(0f), Tooltip("How long the white blast takes to swell to the explosion radius and fade. 0 turns it off.")]
    private float explosionFlashDuration = 0.3f;
    [SerializeField, Min(0), Tooltip("Shards flung straight outward at speed by the blast. 0 turns them off.")]
    private int explosionShardCount = 28;
    [SerializeField, Min(0), Tooltip("Slower debris that arcs down after the blast. 0 turns it off.")]
    private int explosionSparkCount = 32;
    [SerializeField, AudioClipDropdown, Tooltip("Played once when the breaker detonates.")]
    private AudioClip explosionSfx;

    // Shown instead of the explosion when the player runs the breaker down before its
    // countdown ends: a tight puff where the blast would have been, so the two outcomes
    // never look alike.
    [Header("Defeat Effect")]
    [SerializeField, Min(0f), Tooltip("How wide the puff opens, in world units. 0 turns it off.")]
    private float defeatFlashRadius = 0.7f;
    [SerializeField, Min(0f), Tooltip("How long the puff takes to open and fade. 0 turns it off.")]
    private float defeatFlashDuration = 0.16f;
    [SerializeField, Min(0), Tooltip("Debris left by the defeat. 0 turns it off.")]
    private int defeatSparkCount = 14;
    [SerializeField, AudioClipDropdown, Tooltip("Played once when the player runs the breaker down.")]
    private AudioClip defeatSfx;

    // Shard speed lives here rather than on the prefab: the burst is one system shared by
    // every breaker, and EmitParams can override a particle's size and lifetime but not
    // its speed, so a per-breaker value could not actually be honoured.
    private const float ShardMinSpeed = 14f;
    private const float ShardMaxSpeed = 26f;

    // Ceiling on the standoff as a share of the explosion radius, leaving room for the
    // overshoot at the end of the approach to still land inside the blast.
    private const float MaximumStandoffShareOfBlast = 0.75f;

    // One shared blast system for every breaker, the same way HitParticles pools its
    // bursts: the effect has to outlive the breaker, which is released to the pool in
    // the same frame it explodes.
    private static ParticleSystem flashSystem;
    private static ParticleSystem shardSystem;
    private static Material flashMaterial;

    private readonly List<CageTower> cagesInExplosion = new List<CageTower>(16);
    private SpriteRenderer[] spriteRenderers;
    private CageTower targetCage;
    private BreakerState state;
    private float countdownRemaining;
    private Animator animator;

    public BreakerState State => state;
    public CageTower TargetCage => targetCage;
    // Waiting looks the same as sneaking - faded out, no countdown - so it follows the
    // sneaking damage rule rather than the breaking one.
    public override bool CanTakeDamage =>
        state == BreakerState.Breaking
            ? takesDamageInBreakingState
            : takesDamageInSneakingState;

    /// <summary>
    /// A waiting breaker has nothing to do and may well be invincible, so the round is
    /// not held open for it. It still spawned, which is what the wave's count promises.
    /// </summary>
    public override bool BlocksWaveCompletion => state != BreakerState.Waiting;

    internal override bool UsesSeparation => false;

    protected override void Awake()
    {
        base.Awake();
        spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        animator = GetComponent<Animator>();
        EnsureCountdownText();
    }

    protected override void OnEnable()
    {
        base.OnEnable();

        // Spawning is unconditional: a wave that asks for three breakers gets three, even
        // with nothing to break yet. Without a cage the breaker waits on the field instead
        // of despawning, and picks one up as soon as the player fills a cage.
        if (TryClaimTarget())
        {
            PositionForAmbush();
            EnterSneakingState();
        }
        else
        {
            EnterWaitingState();
        }
    }

    protected override void OnDisable()
    {
        ReleaseTarget();
        base.OnDisable();
    }

    private void Update()
    {
        if (state != BreakerState.Breaking)
        {
            return;
        }

        countdownRemaining -= Time.deltaTime;
        UpdateCountdownText();
        if (countdownRemaining <= startExplosionAnimationTime)
            animator.SetBool("DoExplosionEffect", true);
        if (countdownRemaining < 0f)
        {
            Explode();
        }
    }

    private void LateUpdate()
    {
        if (state == BreakerState.Breaking)
        {
            UpdateCountdownPosition();
        }
    }

    protected override Vector2 CalculateDesiredVelocity(Transform player, float elapsed)
    {
        // Waiting breakers fall through to zero here, so they hover where they spawned.
        if (state != BreakerState.Sneaking || !IsValidTarget(targetCage))
        {
            return Vector2.zero;
        }

        Vector2 position = Position;
        Vector2 cagePosition = targetCage.transform.position;
        Vector2 farSide = ResolveFarSideDirection(player, cagePosition);

        // Held inside the blast: a standoff set further out than the explosion reaches
        // would leave the breaker unable to satisfy both halves of the arm check below, so
        // it would circle the cage forever and hold the round open.
        float standoff = Mathf.Min(
            farSideStandoff,
            Mathf.Max(0f, explosionRadius) * MaximumStandoffShareOfBlast);

        // Armed once it is a clear standoff past the cage on the side away from the player,
        // and still near enough to take the cage with it. The far side is measured as a
        // projection rather than a distance to the standoff point: the approach sweeps
        // through that point rather than settling on it, and a decision tick can step over
        // a tolerance around a point, but not over a half-space it has already crossed.
        Vector2 fromCage = position - cagePosition;
        if (Vector2.Dot(fromCage, farSide) >= standoff
            && fromCage.sqrMagnitude <= explosionRadius * explosionRadius)
        {
            EnterBreakingState();
            return Vector2.zero;
        }

        // Aimed past the cage rather than at it, which is what carries the breaker around
        // to that side. The player moving shifts the point, so the arm check above is what
        // ends the approach - the breaker need never come to rest on it exactly.
        Vector2 toStandoff = cagePosition + farSide * standoff - position;
        float distanceSquared = toStandoff.sqrMagnitude;
        if (distanceSquared <= 0.000001f)
        {
            EnterBreakingState();
            return Vector2.zero;
        }

        return toStandoff * (Mathf.Max(0f, moveSpeed) / Mathf.Sqrt(distanceSquared));
    }

    /// <summary>
    /// Unit direction from the player through the cage - the side of it the breaker arms
    /// on. Never points below the cage: the far side of one the player is standing on top
    /// of is inside the island, which the breaker cannot reach, and it would circle there
    /// forever instead of arming. Levelling the direction out still keeps the cage between
    /// the two of them.
    /// </summary>
    private Vector2 ResolveFarSideDirection(Transform player, Vector2 cagePosition)
    {
        Vector2 fromPlayer = player != null
            ? cagePosition - (Vector2)player.position
            : Position - cagePosition;
        if (fromPlayer.y < 0f)
        {
            fromPlayer.y = 0f;
        }

        if (fromPlayer.sqrMagnitude > 0.0001f)
        {
            return fromPlayer.normalized;
        }

        // Player level with the cage and on top of it, so there is no far side to speak
        // of: hold the side the breaker is already coming in on.
        Vector2 fromCage = new Vector2(Position.x - cagePosition.x, 0f);
        return fromCage.sqrMagnitude > 0.0001f ? fromCage.normalized : Vector2.right;
    }

    protected override void OnStrategicTick(Transform player, float elapsed)
    {
        // A countdown already running is committed; it explodes wherever it stands.
        if (state == BreakerState.Breaking
            || (state == BreakerState.Sneaking && IsValidTarget(targetCage)))
        {
            return;
        }

        // Losing a cage mid-flight drops the breaker back to waiting rather than
        // despawning it, so it can pick up the next cage the player fills.
        if (TryClaimTarget())
        {
            EnterSneakingState();
        }
        else
        {
            EnterWaitingState();
        }
    }

    protected override void ResetEnemyState()
    {
        ReleaseTarget();
        state = BreakerState.Waiting;
        countdownRemaining = 0f;
        SetSpriteOpacity(1f);

        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(false);
        }

        if (countdownBackground != null)
        {
            countdownBackground.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Picks the cage the fewest other breakers are already going for, and the farthest
    /// from the player among those. Breakers spread out while there are cages to go round
    /// and double up on one only once every cage is spoken for.
    /// </summary>
    private bool TryClaimTarget()
    {
        ReleaseTarget();

        Transform player = EnemySimulationManager.Instance.Player;
        Vector2 playerPosition = player != null ? player.position : Position;
        int fewestClaims = int.MaxValue;
        float farthestDistanceSquared = float.NegativeInfinity;
        CageTower bestCage = null;
        CageTower[] cages = FindObjectsByType<CageTower>(FindObjectsSortMode.None);
        CageBreakerEnemy[] breakers =
            FindObjectsByType<CageBreakerEnemy>(FindObjectsSortMode.None);

        for (int i = 0; i < cages.Length; i++)
        {
            CageTower cage = cages[i];
            if (!IsValidTarget(cage))
            {
                continue;
            }

            int claims = CountClaims(cage, breakers);
            float distanceSquared =
                ((Vector2)cage.transform.position - playerPosition).sqrMagnitude;

            // Fewer claims always wins; distance only breaks ties within the same tier.
            if (claims > fewestClaims
                || (claims == fewestClaims && distanceSquared <= farthestDistanceSquared))
            {
                continue;
            }

            fewestClaims = claims;
            farthestDistanceSquared = distanceSquared;
            bestCage = cage;
        }

        if (bestCage == null)
        {
            return false;
        }

        targetCage = bestCage;
        return true;
    }

    /// <summary>
    /// How many other live breakers are heading for <paramref name="cage"/>. Counted from
    /// the breakers themselves rather than a claim table, so a breaker that dies, despawns
    /// or is pooled mid-run cannot leave a phantom claim behind.
    /// </summary>
    private int CountClaims(CageTower cage, CageBreakerEnemy[] breakers)
    {
        int claims = 0;
        for (int i = 0; i < breakers.Length; i++)
        {
            CageBreakerEnemy breaker = breakers[i];
            if (breaker != null
                && breaker != this
                && breaker.isActiveAndEnabled
                && breaker.targetCage == cage)
            {
                claims++;
            }
        }

        return claims;
    }

    private void PositionForAmbush()
    {
        Transform player = EnemySimulationManager.Instance.Player;
        if (player == null || targetCage == null)
        {
            return;
        }

        Vector2 playerPosition = player.position;
        Vector2 targetDirection = (Vector2)targetCage.transform.position - playerPosition;
        if (targetDirection.sqrMagnitude <= 0.000001f)
        {
            targetDirection = Position - playerPosition;
        }

        if (targetDirection.sqrMagnitude <= 0.000001f)
        {
            targetDirection = Vector2.right;
        }

        // The ambush point is mirrored through the player, so a cage above the player puts
        // it below - and that can land inside the island. Lift it back out.
        Vector2 spawnPosition = ClampAboveGround(
            playerPosition - targetDirection.normalized * Mathf.Max(0.1f, spawnRadius),
            GroundClearance);
        rb.position = spawnPosition;
        transform.position = spawnPosition;
    }

    private void EnterSneakingState()
    {
        EnterHiddenState(BreakerState.Sneaking);
    }

    private void EnterWaitingState()
    {
        EnterHiddenState(BreakerState.Waiting);
    }

    /// <summary>
    /// Shared setup for the two pre-explosion states. They look identical - faded out with
    /// no countdown - and differ only in whether a cage has been claimed yet.
    /// </summary>
    private void EnterHiddenState(BreakerState hiddenState)
    {
        state = hiddenState;
        countdownRemaining = 0f;
        SetSpriteOpacity(sneakingOpacity);
        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(false);
        }

        if (countdownBackground != null)
        {
            countdownBackground.gameObject.SetActive(false);
        }
    }

    private void EnterBreakingState()
    {
        if (state == BreakerState.Breaking)
        {
            return;
        }

        state = BreakerState.Breaking;
        countdownRemaining = Mathf.Max(0f, breakCountdown);
        rb.linearVelocity = Vector2.zero;
        SetSpriteOpacity(1f);
        countdownText.gameObject.SetActive(true);
        if (countdownBackground != null)
        {
            countdownBackground.gameObject.SetActive(
                countdownBackgroundSprite != null);
        }

        UpdateCountdownText();
        UpdateCountdownPosition();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (state == BreakerState.Breaking && IsPlayerCollider(other))
        {
            PlayDefeatEffect();
            ReleaseOrDestroy();
        }
    }

    private void Explode()
    {
        PlayExplosionEffect();

        cagesInExplosion.Clear();
        CageTower[] cages = FindObjectsByType<CageTower>(FindObjectsSortMode.None);
        float radiusSquared = Mathf.Max(0f, explosionRadius);
        radiusSquared *= radiusSquared;

        for (int i = 0; i < cages.Length; i++)
        {
            CageTower cage = cages[i];
            if (cage != null
                && ((Vector2)cage.transform.position - Position).sqrMagnitude
                    <= radiusSquared)
            {
                cagesInExplosion.Add(cage);
            }
        }

        for (int i = 0; i < cagesInExplosion.Count; i++)
        {
            cagesInExplosion[i].BreakCage();
        }

        ReleaseOrDestroy();
    }

    /// <summary>
    /// The white blast, in three layers: a disc that swells out to the explosion radius,
    /// shards that leave it at speed, and slower debris that arcs down behind them. The
    /// disc is sized off the radius, so it shows exactly which cages the breaker took.
    /// </summary>
    private void PlayExplosionEffect()
    {
        if (explosionSfx != null)
        {
            AudioController.Play(explosionSfx);
        }

        EmitShards(Position, explosionShardCount);
        HitParticles.EmitDeathBurst(Position, explosionSparkCount);
        EmitFlash(Position, explosionRadius, explosionFlashDuration);
    }

    /// <summary>
    /// The breaker going down to the player instead of detonating. Deliberately the small
    /// version of the blast - a puff and a little debris, no shards and nothing the width
    /// of the explosion radius - so a defused breaker never reads as one that went off.
    /// </summary>
    private void PlayDefeatEffect()
    {
        if (defeatSfx != null)
        {
            AudioController.Play(defeatSfx);
        }

        HitParticles.EmitDeathBurst(Position, defeatSparkCount);
        EmitFlash(Position, defeatFlashRadius, defeatFlashDuration);
    }

    private static void EmitFlash(Vector2 position, float radius, float duration)
    {
        if (radius <= 0f || duration <= 0f)
        {
            return;
        }

        // A scene change destroys the system; the next one of these just rebuilds it.
        if (flashSystem == null)
        {
            flashSystem = CreateFlashSystem();
        }

        ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams
        {
            position = position,
            applyShapeToPosition = true,
            startSize = radius * 2f,
            startLifetime = duration
        };
        flashSystem.Emit(emitParams, 1);
    }

    private static void EmitShards(Vector2 position, int count)
    {
        if (count <= 0)
        {
            return;
        }

        if (shardSystem == null)
        {
            shardSystem = CreateShardSystem();
        }

        ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams
        {
            position = position,
            applyShapeToPosition = true
        };
        shardSystem.Emit(emitParams, count);
    }

    private static ParticleSystem CreateShardSystem()
    {
        GameObject systemObject = new GameObject("Cage Break Shards");
        ParticleSystem system = systemObject.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = system.main;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.45f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(ShardMinSpeed, ShardMaxSpeed);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
        main.startColor = Color.white;
        // Barely any fall: these read as the blast throwing them, not as debris dropping.
        // That is what the slower HitParticles burst underneath is for.
        main.gravityModifier = 0.25f;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = false;

        // Emitted off the rim rather than across the whole disc, so every shard leaves on
        // a clean outward heading instead of crawling out of the middle.
        ParticleSystem.ShapeModule shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.15f;
        shape.radiusThickness = 0f;
        shape.randomDirectionAmount = 0f;

        // Bleeds the speed off across the flight, so the shards punch out and coast to a
        // stop rather than holding full pace right up to the moment they vanish.
        ParticleSystem.LimitVelocityOverLifetimeModule limitVelocity =
            system.limitVelocityOverLifetime;
        limitVelocity.enabled = true;
        limitVelocity.limit = new ParticleSystem.MinMaxCurve(3f);
        limitVelocity.dampen = 0.1f;

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = system.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = CreateWhiteFadeGradient(1f, 0.6f);

        ParticleSystemRenderer particleRenderer =
            systemObject.GetComponent<ParticleSystemRenderer>();
        // Stretched along their own velocity, so the speed reads as a streak.
        particleRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        particleRenderer.lengthScale = 1.4f;
        particleRenderer.velocityScale = 0.05f;
        particleRenderer.sortingLayerName = "Foreground";
        // Above the flash and the debris, since the shards lead the explosion.
        particleRenderer.sortingOrder = 11;
        particleRenderer.sharedMaterial = GetFlashMaterial();

        return system;
    }

    private static ParticleSystem CreateFlashSystem()
    {
        GameObject systemObject = new GameObject("Cage Break Flash");
        ParticleSystem system = systemObject.AddComponent<ParticleSystem>();

        // Size and lifetime come from the emitting breaker, so one system serves breakers
        // with different explosion radii.
        ParticleSystem.MainModule main = system.main;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;
        main.startColor = Color.white;
        main.gravityModifier = 0f;

        // Bursts come only from Emit(); nothing trickles out between explosions.
        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = false;

        // The disc is placed by EmitParams alone; the shape is only here to carry the
        // position through, so it has no spread of its own.
        ParticleSystem.ShapeModule shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.0001f;

        // Snaps most of the way open on the first frames and eases into the full radius,
        // which reads as a blast rather than a circle growing.
        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = system.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
            1f,
            new AnimationCurve(
                new Keyframe(0f, 0.25f, 4f, 4f),
                new Keyframe(1f, 1f, 0f, 0f)));

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = system.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = CreateWhiteFadeGradient(1f, 0.3f);

        ParticleSystemRenderer particleRenderer =
            systemObject.GetComponent<ParticleSystemRenderer>();
        particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        particleRenderer.sortingLayerName = "Foreground";
        // One under the hit sparks, so the debris reads on top of the flash.
        particleRenderer.sortingOrder = 9;
        particleRenderer.sharedMaterial = GetFlashMaterial();

        return system;
    }

    /// <summary>
    /// Stays fully white at <paramref name="alpha"/> until <paramref name="holdUntil"/> of the
    /// particle's life, then fades out. Both layers of the blast stay white the whole way
    /// down; only the opacity moves.
    /// </summary>
    private static Gradient CreateWhiteFadeGradient(float alpha, float holdUntil)
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new[]
            {
                new GradientAlphaKey(alpha, 0f),
                new GradientAlphaKey(alpha, holdUntil),
                new GradientAlphaKey(0f, 1f)
            });

        return gradient;
    }

    /// <summary>A white disc with a soft rim, so the blast has an edge instead of a hard cut.</summary>
    private static Material GetFlashMaterial()
    {
        if (flashMaterial != null)
        {
            return flashMaterial;
        }

        Shader spriteShader = Shader.Find("Sprites/Default");
        if (spriteShader == null)
        {
            return null;
        }

        const int Resolution = 64;
        Texture2D texture = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false)
        {
            name = "Cage Break Flash Texture",
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        Color32[] pixels = new Color32[Resolution * Resolution];
        float centre = (Resolution - 1) * 0.5f;
        for (int y = 0; y < Resolution; y++)
        {
            for (int x = 0; x < Resolution; x++)
            {
                float distance = new Vector2(x - centre, y - centre).magnitude / centre;
                // Square-rooting the falloff holds the disc near solid white across most
                // of its width and spends the fade on the outer rim.
                float alpha = Mathf.Sqrt(Mathf.Clamp01(1f - distance));
                pixels[y * Resolution + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        flashMaterial = new Material(spriteShader)
        {
            name = "Shared Cage Break Flash Material",
            mainTexture = texture,
            hideFlags = HideFlags.HideAndDontSave
        };

        return flashMaterial;
    }

    private void EnsureCountdownText()
    {
        if (countdownText == null)
        {
            countdownText = GetComponentInChildren<TextMeshPro>(true);
        }

        if (countdownText == null)
        {
            GameObject textObject = new GameObject("Break Countdown");
            textObject.transform.SetParent(transform, false);
            countdownText = textObject.AddComponent<TextMeshPro>();
            countdownText.rectTransform.sizeDelta = new Vector2(20f, 5f);
            countdownText.transform.localScale = Vector3.one * 0.1f;
        }

        countdownText.transform.localPosition = countdownOffset;
        countdownText.alignment = TextAlignmentOptions.Center;
        countdownText.fontSize = Mathf.Max(1f, countdownFontSize);
        countdownText.textWrappingMode = TextWrappingModes.NoWrap;
        countdownText.sortingOrder = 100;
        countdownText.gameObject.SetActive(false);

        EnsureCountdownBackground();
    }

    private void EnsureCountdownBackground()
    {
        if (countdownBackground == null)
        {
            Transform existingBackground = transform.Find("Break Countdown Background");
            if (existingBackground != null)
            {
                countdownBackground = existingBackground.GetComponent<SpriteRenderer>();
            }
        }

        if (countdownBackground == null)
        {
            GameObject backgroundObject = new GameObject("Break Countdown Background");
            backgroundObject.transform.SetParent(transform, false);
            countdownBackground = backgroundObject.AddComponent<SpriteRenderer>();
        }

        countdownBackground.sprite = countdownBackgroundSprite;
        countdownBackground.sortingLayerID = countdownText.sortingLayerID;
        countdownBackground.sortingOrder = countdownText.sortingOrder - 1;
        countdownBackground.gameObject.SetActive(false);
    }

    private void UpdateCountdownText()
    {
        if (countdownText != null)
        {
            countdownText.text =
                Mathf.Max(0, Mathf.CeilToInt(countdownRemaining)).ToString();
        }
    }

    private void UpdateCountdownPosition()
    {
        if (countdownText == null)
        {
            return;
        }

        Vector3 normalWorldPosition = transform.position + (Vector3)countdownOffset;
        Vector3 displayWorldPosition = normalWorldPosition;
        Camera worldCamera = Camera.main;

        if (worldCamera != null)
        {
            Vector3 breakerViewportPosition =
                worldCamera.WorldToViewportPoint(transform.position);
            bool breakerIsOnScreen =
                breakerViewportPosition.z > 0f
                && breakerViewportPosition.x >= 0f
                && breakerViewportPosition.x <= 1f
                && breakerViewportPosition.y >= 0f
                && breakerViewportPosition.y <= 1f;

            if (!breakerIsOnScreen)
            {
                Vector3 breakerScreenPosition =
                    worldCamera.WorldToScreenPoint(transform.position);
                Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                Vector2 direction =
                    (Vector2)breakerScreenPosition - screenCenter;

                if (breakerScreenPosition.z < 0f)
                {
                    direction = -direction;
                }

                if (direction.sqrMagnitude <= 0.0001f)
                {
                    direction = Vector2.up;
                }

                float inset = Mathf.Clamp(
                    countdownScreenEdgeInset,
                    0f,
                    Mathf.Min(Screen.width, Screen.height) * 0.5f);
                Vector2 halfBounds = new Vector2(
                    Mathf.Max(0f, Screen.width * 0.5f - inset),
                    Mathf.Max(0f, Screen.height * 0.5f - inset));
                float scaleToEdge = Mathf.Min(
                    direction.x == 0f
                        ? float.PositiveInfinity
                        : halfBounds.x / Mathf.Abs(direction.x),
                    direction.y == 0f
                        ? float.PositiveInfinity
                        : halfBounds.y / Mathf.Abs(direction.y));
                Vector2 clampedScreenPosition =
                    screenCenter + direction * scaleToEdge;
                Vector3 screenPosition = new Vector3(
                    clampedScreenPosition.x,
                    clampedScreenPosition.y,
                    worldCamera.WorldToScreenPoint(normalWorldPosition).z);
                displayWorldPosition =
                    worldCamera.ScreenToWorldPoint(screenPosition);
            }
        }

        countdownText.transform.position = displayWorldPosition;
        if (countdownBackground != null)
        {
            countdownBackground.transform.position = displayWorldPosition;
        }
    }

    private void SetSpriteOpacity(float opacity)
    {
        if (spriteRenderers == null)
        {
            return;
        }

        float alpha = Mathf.Clamp01(opacity);
        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            SpriteRenderer spriteRenderer = spriteRenderers[i];
            if (spriteRenderer == null)
            {
                continue;
            }

            Color color = spriteRenderer.color;
            color.a = alpha;
            spriteRenderer.color = color;
        }
    }

    private static bool IsPlayerCollider(Collider2D other)
    {
        Transform current = other.attachedRigidbody != null
            ? other.attachedRigidbody.transform
            : other.transform;

        while (current != null)
        {
            if (current.CompareTag("Player"))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private void ReleaseTarget()
    {
        targetCage = null;
    }

    private static bool IsValidTarget(CageTower cage)
    {
        return cage != null
            && cage.State == CageTower.CageState.Full
            && cage.CapturedEnemy != null;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.35f, 0.1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, Mathf.Max(0f, explosionRadius));
    }
}
