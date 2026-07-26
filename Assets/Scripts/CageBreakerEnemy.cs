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
        Breaking,

        /// <summary>Knocked out by the player mid-countdown, tumbling off the bottom of the screen.</summary>
        Falling
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
    [SerializeField, Min(0f), Tooltip("How far off the breaker the countdown sits, on the side the "
        + "player is on. It is never brought nearer than this, not even when it is pushed towards "
        + "the player to stay on screen.")]
    private float countdownDistance = 1.5f;
    [SerializeField, Min(1f)] private float countdownFontSize = 10f;
    [SerializeField, Min(0f)] private float countdownTextScale = 0.1f;
    [SerializeField] private Sprite countdownBackgroundSprite;
    [SerializeField, Min(0f)] private float countdownSpriteScale = 1f;
    [SerializeField, Tooltip("Where the number sits in the timer artwork, in the artwork's own "
        + "units, turning with it. The artwork's pivot is the middle of the whole sprite, its "
        + "point included, so the number wants pushing back off the point to land in the face.")]
    private Vector2 countdownTextOffset = new Vector2(0f, -0.55f);
    [SerializeField, Tooltip("Rotation added to the countdown-to-breaker direction. Use this to match which way the timer artwork points at zero rotation.")]
    private float countdownSpriteRotationOffset = 90f;
    [SerializeField, Min(0f), Tooltip("Pixels of screen edge the countdown is kept clear of when it "
        + "is pushed towards the player to stay in view.")]
    private float countdownScreenEdgeInset = 48f;
    [SerializeField, Min(0f), Tooltip("Width of the soft disc sat under the countdown so it reads "
        + "over a busy background, in world units. 0 turns it off.")]
    private float countdownGlowSize = 1.5f;
    [SerializeField, ColorUsage(true, true), Tooltip("Colour of that disc. Held still through the "
        + "countdown, so it backs the number rather than pulling the eye off it. Past 1 it blooms, "
        + "the scene's threshold being 0.9.")]
    private Color countdownGlowColor = new Color(1f, 1f, 1f, 0.75f);
    [SerializeField] private TextMeshPro countdownText;
    [SerializeField] private SpriteRenderer countdownBackground;
    [SerializeField] private SpriteRenderer countdownGlow;
    [SerializeField] private float startExplosionAnimationTime = 0.5f;
    [SerializeField, AudioClipDropdown, Tooltip("Repeated throughout the countdown and stopped immediately before the explosion or defeat sound.")]
    private AudioClip countdownLoopSfx;

    [Header("Charge Up")]
    [SerializeField, Min(0f), Tooltip("How far the breaker rattles off the spot it planted itself, "
        + "at the moment it detonates. The shake builds up to this across the countdown. 0 turns it off.")]
    private float chargeShakeDistance = 0.12f;
    [SerializeField, Min(0f), Tooltip("How much the breaker has swollen by the moment it detonates, "
        + "as a share of its normal size. 0 turns the growth off.")]
    private float chargeGrowth = 1f;
    [SerializeField, Tooltip("Colour the breaker, its halo and the motes it pulls in are driven towards "
        + "as the countdown runs down.")]
    private Color chargeGlowColor = new Color(1f, 0.45f, 0.12f, 1f);
    [SerializeField, Min(1f), Tooltip("Peak brightness the glow colour is driven to at the moment it "
        + "detonates. The scene's bloom threshold is 0.9, so anything past 1 blooms. 1 turns the glow off.")]
    private float chargeGlowIntensity = 5f;
    [SerializeField, Min(0f), Tooltip("How wide the halo behind the breaker opens, in the breaker's own "
        + "widths, at the moment it detonates. 0 turns the halo off.")]
    private float chargeHaloSize = 2.2f;
    [SerializeField, Min(0f), Tooltip("How far out the charge-up pulls its motes in from, in world units. "
        + "0 turns them off.")]
    private float chargeInflowRadius = 3.2f;
    [SerializeField, Min(0), Tooltip("Motes drawn into the breaker per second at the moment it detonates. "
        + "0 turns them off.")]
    private int chargeInflowRate = 70;
    [SerializeField, Min(0.05f), Tooltip("Seconds a mote takes to travel from the rim into the breaker.")]
    private float chargeInflowTravel = 0.5f;

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
    [SerializeField, Min(0f), Tooltip("Upward speed the knock-out flings the breaker with, before gravity takes it back down.")]
    private float defeatFlingSpeed = 6f;
    [SerializeField, Min(0f), Tooltip("Downward pull on the flung breaker, in units per second squared.")]
    private float defeatFallGravity = 22f;
    [SerializeField, Min(0f), Tooltip("How far below the bottom of the screen, in screen heights, the breaker "
        + "falls before it is taken off the field.")]
    private float defeatFallScreenMargin = 0.25f;
    [SerializeField, Min(0.1f), Tooltip("Backstop on the length of the fall, for scenes with no camera to "
        + "measure the screen against.")]
    private float defeatFallTimeout = 8f;

    // Shard speed lives here rather than on the prefab: the burst is one system shared by
    // every breaker, and EmitParams can override a particle's size and lifetime but not
    // its speed, so a per-breaker value could not actually be honoured.
    private const float ShardMinSpeed = 14f;
    private const float ShardMaxSpeed = 26f;

    // Ceiling on the standoff as a share of the explosion radius, leaving room for the
    // overshoot at the end of the approach to still land inside the blast.
    private const float MaximumStandoffShareOfBlast = 0.75f;

    // The halo's heartbeat: it beats slowly while the countdown is long and races by the
    // end, which is what sells the charge running away with itself.
    private const float HaloPulseSlowSpeed = 6f;
    private const float HaloPulseFastSpeed = 26f;
    private const float HaloPulseDepth = 0.09f;
    private const float HaloPeakOpacity = 0.55f;

    // Kept off 1 at the start so the halo opens out of the body rather than snapping on
    // at full width the instant the countdown starts.
    private const float HaloStartShare = 0.35f;

    // The frontmost layer in the project, shared with the rest of the effects that have to
    // read over the field. The order is picked well clear of anything else on the layer.
    private const string CountdownSortingLayer = "Foreground";
    private const int CountdownSortingOrder = 1000;

    // One shared blast system for every breaker, the same way HitParticles pools its
    // bursts: the effect has to outlive the breaker, which is released to the pool in
    // the same frame it explodes.
    private static ParticleSystem flashSystem;
    private static ParticleSystem shardSystem;
    private static Material flashMaterial;

    // The soft disc the halo and the motes are both drawn with. Shared, unlike the systems
    // that use it: the charge-up effects belong to one breaker and die with it.
    private static Texture2D glowTexture;
    private static Material glowMaterial;
    private static Sprite glowSprite;

    private readonly List<CageTower> cagesInExplosion = new List<CageTower>(16);
    private SpriteRenderer[] spriteRenderers;
    private CageTower targetCage;
    private BreakerState state;
    private float countdownRemaining;
    private Animator animator;

    // The spot the breaker planted itself on. The charge-up shake is measured from here
    // rather than from where it currently stands, so the jitter cannot walk it off the cage.
    private Vector2 breakingAnchor;
    private Vector3 chargeBaseScale = Vector3.one;
    private float chargeScale = 1f;
    private Vector2 fallVelocity;
    private float fallElapsed;
    private AudioSource countdownLoopSource;

    // The prefab's own transform, restored on the way back into the pool so a breaker never
    // respawns still swollen from a charge-up or belly-up from a fall.
    private Vector3 baseScale;
    private bool baseScaleCaptured;
    private Vector3 countdownTextBaseScale = Vector3.one;
    private Vector3 countdownBackgroundBaseScale = Vector3.one;
    private Vector3 countdownGlowBaseScale = Vector3.one;

    // The charge-up's own visuals. Both hang off the breaker rather than living at the
    // scene root like the blast does: they are only ever wanted while it is still standing,
    // so being taken off the field with it is exactly the behaviour wanted.
    private SpriteRenderer chargeHalo;
    private ParticleSystem chargeInflow;
    private SpriteRenderer[] glowRenderers;
    private Color[] glowBaseColors;
    private float inflowBacklog;

    public BreakerState State => state;
    public CageTower TargetCage => targetCage;
    public override bool CanTakeDamage => state switch
    {
        // Already knocked out and on its way off the screen; there is nothing left to shoot down.
        BreakerState.Falling => false,
        BreakerState.Breaking => takesDamageInBreakingState,
        // Waiting looks the same as sneaking - faded out, no countdown - so it follows the
        // sneaking damage rule rather than the breaking one.
        _ => takesDamageInSneakingState
    };

    /// <summary>
    /// A waiting breaker has nothing to do and may well be invincible, so the round is
    /// not held open for it. It still spawned, which is what the wave's count promises.
    /// A falling one is already spent and only has its drop off the screen left to play.
    /// </summary>
    public override bool BlocksWaveCompletion =>
        state != BreakerState.Waiting && state != BreakerState.Falling;

    internal override bool UsesSeparation => false;

    protected override void Awake()
    {
        base.Awake();
        spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        animator = GetComponent<Animator>();
        CaptureBaseScale();
        EnsureCountdownText();
        BuildChargeEffects();
    }

    /// <summary>
    /// Records the prefab's scale the first time it is seen, for the same reason the base
    /// class captures its health twice: the pool builds its items under an inactive root, so
    /// the first acquire - and the reset it runs - lands before Awake does.
    /// </summary>
    private void CaptureBaseScale()
    {
        if (baseScaleCaptured)
        {
            return;
        }

        baseScale = transform.localScale;
        baseScaleCaptured = true;
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
        StopCountdownSound();
        ReleaseTarget();
        base.OnDisable();
    }

    private void Update()
    {
        if (state == BreakerState.Falling)
        {
            UpdateDefeatFall();
            return;
        }

        if (state != BreakerState.Breaking)
        {
            return;
        }

        countdownRemaining -= Time.deltaTime;
        UpdateChargeUp();
        UpdateCountdownText();
        if (countdownRemaining <= startExplosionAnimationTime)
            animator.SetBool("DoExplosionEffect", true);
        if (countdownRemaining < 0f)
        {
            Explode();
        }
    }

    /// <summary>
    /// The wind-up to the blast: the breaker swells, rattles, glows and hauls motes in out
    /// of the air around it, all harder the nearer the countdown gets to zero. The swell
    /// and the rattle run on the transform, so the wing-flapping clip - and the explosion
    /// clip that takes over for the last stretch - keep playing underneath.
    /// </summary>
    private void UpdateChargeUp()
    {
        // Planted, so nothing should be nudging it off its spot. Zeroed every frame rather
        // than once on arrival: ground recovery underneath can still hand it a velocity, and
        // a breaker with one gets turned to face its heading by the shared enemy step.
        rb.linearVelocity = Vector2.zero;

        float charge = breakCountdown > 0f
            ? Mathf.Clamp01(1f - countdownRemaining / breakCountdown)
            : 1f;

        chargeScale = 1f + chargeGrowth * charge;
        transform.localScale = chargeBaseScale * chargeScale;

        // Re-rolled every frame rather than eased along a curve: the point is a rattle that
        // cannot be followed, not a wobble.
        transform.position =
            breakingAnchor + Random.insideUnitCircle * (chargeShakeDistance * charge);

        UpdateChargeEffects(charge);
    }

    /// <summary>
    /// The glow, the halo and the inflow, all driven off the same charge. The brightness is
    /// squared so the breaker sits near its normal colour for most of the countdown and
    /// only tears open at the end, which is where the camera's bloom takes over.
    /// </summary>
    private void UpdateChargeEffects(float charge)
    {
        float glow = charge * charge;
        ApplyChargeGlow(glow);
        UpdateChargeHalo(charge, glow);
        UpdateChargeInflow(charge);
    }

    /// <summary>
    /// Drives the breaker's own sprites from their normal colour towards the glow colour at
    /// full intensity. Written as an HDR tint rather than a material swap so the ordinary
    /// hit flash - which takes the material for a couple of frames - can still play over it.
    /// Alpha is left alone: the sneak fade owns that.
    /// </summary>
    private void ApplyChargeGlow(float glow)
    {
        if (glowRenderers == null)
        {
            return;
        }

        Color hot = chargeGlowColor * Mathf.Max(1f, chargeGlowIntensity);
        for (int i = 0; i < glowRenderers.Length; i++)
        {
            SpriteRenderer glowRenderer = glowRenderers[i];
            if (glowRenderer == null)
            {
                continue;
            }

            Color color = glow > 0f
                ? Color.LerpUnclamped(glowBaseColors[i], hot, Mathf.Clamp01(glow))
                : glowBaseColors[i];
            color.a = glowRenderer.color.a;
            glowRenderer.color = color;
        }
    }

    private void UpdateChargeHalo(float charge, float glow)
    {
        if (chargeHalo == null)
        {
            return;
        }

        if (chargeHaloSize <= 0f || chargeGlowIntensity <= 1f || glow <= 0.0001f)
        {
            chargeHalo.enabled = false;
            return;
        }

        chargeHalo.enabled = true;

        float pulseSpeed = Mathf.Lerp(HaloPulseSlowSpeed, HaloPulseFastSpeed, charge);
        float pulse = 1f + HaloPulseDepth * Mathf.Sin(Time.time * pulseSpeed);
        chargeHalo.transform.localScale =
            Vector3.one * (chargeHaloSize * Mathf.Lerp(HaloStartShare, 1f, charge) * pulse);

        Color color = chargeGlowColor * chargeGlowIntensity;
        color.a = Mathf.Clamp01(glow) * HaloPeakOpacity;
        chargeHalo.color = color;
    }

    /// <summary>
    /// Meters the motes out over time rather than in bursts, ramped in so the pull starts as
    /// a trickle and is a stream by the time the countdown runs out.
    /// </summary>
    private void UpdateChargeInflow(float charge)
    {
        if (chargeInflow == null || chargeInflowRate <= 0 || chargeInflowRadius <= 0f)
        {
            return;
        }

        inflowBacklog += chargeInflowRate * Mathf.Lerp(0.15f, 1f, charge) * Time.deltaTime;
        int motesThisFrame = Mathf.FloorToInt(inflowBacklog);
        if (motesThisFrame <= 0)
        {
            return;
        }

        inflowBacklog -= motesThisFrame;
        EmitInflow(motesThisFrame, charge);
    }

    /// <summary>
    /// Each mote is placed on the rim and handed the exact velocity that carries it to the
    /// breaker as its life runs out, so they land on it instead of sailing through it or
    /// stopping short. Straight-line and constant-speed: with the shape module bypassed and
    /// no gravity on the system, nothing else touches them on the way in.
    /// </summary>
    private void EmitInflow(int count, float charge)
    {
        Vector2 centre = transform.position;
        float travel = Mathf.Max(0.05f, chargeInflowTravel);

        for (int i = 0; i < count; i++)
        {
            float angle = Random.value * Mathf.PI * 2f;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 start = centre + direction * (chargeInflowRadius * Random.Range(0.7f, 1.15f));
            float lifetime = travel * Random.Range(0.75f, 1.15f);

            ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams
            {
                position = start,
                velocity = (centre - start) / lifetime,
                startLifetime = lifetime,
                startSize = Random.Range(0.09f, 0.22f) * Mathf.Lerp(0.7f, 1.3f, charge)
            };
            chargeInflow.Emit(emitParams, 1);
        }
    }

    private void BuildChargeEffects()
    {
        CaptureGlowRenderers();
        EnsureChargeHalo();
        EnsureChargeInflow();
        ApplyChargeGlow(0f);
    }

    /// <summary>
    /// The breaker's own sprites, which the glow drives. The countdown's backdrop and glow are
    /// left out: they are a readout hanging off the breaker, and have no business glowing with it.
    /// </summary>
    private void CaptureGlowRenderers()
    {
        List<SpriteRenderer> renderers = new List<SpriteRenderer>(
            spriteRenderers != null ? spriteRenderers.Length : 0);
        for (int i = 0; spriteRenderers != null && i < spriteRenderers.Length; i++)
        {
            SpriteRenderer spriteRenderer = spriteRenderers[i];
            if (spriteRenderer != null && !IsCountdownRenderer(spriteRenderer))
            {
                renderers.Add(spriteRenderer);
            }
        }

        glowRenderers = renderers.ToArray();
        glowBaseColors = new Color[glowRenderers.Length];
        for (int i = 0; i < glowRenderers.Length; i++)
        {
            glowBaseColors[i] = glowRenderers[i].color;
        }
    }

    private void EnsureChargeHalo()
    {
        if (chargeHalo != null)
        {
            return;
        }

        GameObject haloObject = new GameObject("Charge Halo");
        haloObject.transform.SetParent(transform, false);
        chargeHalo = haloObject.AddComponent<SpriteRenderer>();
        chargeHalo.sprite = GetGlowSprite();

        // Tucked one behind the body on its own sorting layer, so the breaker is still read
        // as a silhouette against its own glow rather than washed out by it.
        SpriteRenderer body = ResolveBodyRenderer();
        if (body != null)
        {
            chargeHalo.sortingLayerID = body.sortingLayerID;
            chargeHalo.sortingOrder = body.sortingOrder - 1;
        }

        chargeHalo.enabled = false;
    }

    private void EnsureChargeInflow()
    {
        if (chargeInflow != null)
        {
            return;
        }

        GameObject systemObject = new GameObject("Charge Inflow");
        systemObject.transform.SetParent(transform, false);
        chargeInflow = systemObject.AddComponent<ParticleSystem>();

        // Left looping with emission off, the same trick the blast systems use: a stopped
        // system swallows Emit, so it runs forever and simulates only what is pushed in.
        ParticleSystem.MainModule main = chargeInflow.main;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        // Shape-only scaling: the system hangs off a breaker that doubles in size across the
        // countdown, and the motes have no business doubling with it.
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.startSpeed = 0f;
        main.startLifetime = Mathf.Max(0.05f, chargeInflowTravel);
        main.startSize = 0.15f;
        main.startColor = chargeGlowColor * Mathf.Max(1f, chargeGlowIntensity);
        main.gravityModifier = 0f;
        main.maxParticles = 500;

        ParticleSystem.EmissionModule emission = chargeInflow.emission;
        emission.enabled = false;

        // Swells on the way in and pinches out on arrival, so a mote reads as being taken
        // into the breaker rather than as one that simply stopped being drawn.
        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = chargeInflow.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
            1f,
            new AnimationCurve(
                new Keyframe(0f, 0.3f),
                new Keyframe(0.8f, 1f),
                new Keyframe(1f, 0f)));

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = chargeInflow.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.25f),
                new GradientAlphaKey(1f, 0.85f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = gradient;

        ParticleSystemRenderer particleRenderer =
            systemObject.GetComponent<ParticleSystemRenderer>();
        // Stretched along the heading, so the pull reads as a direction rather than as dots
        // drifting about.
        particleRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        particleRenderer.lengthScale = 2f;
        particleRenderer.velocityScale = 0.02f;
        particleRenderer.sharedMaterial = GetGlowMaterial();

        SpriteRenderer body = ResolveBodyRenderer();
        if (body != null)
        {
            particleRenderer.sortingLayerID = body.sortingLayerID;
            // Over the body: the last stretch of a mote's flight is across the breaker.
            particleRenderer.sortingOrder = body.sortingOrder + 1;
        }
    }

    private SpriteRenderer ResolveBodyRenderer()
    {
        for (int i = 0; spriteRenderers != null && i < spriteRenderers.Length; i++)
        {
            SpriteRenderer spriteRenderer = spriteRenderers[i];
            if (spriteRenderer != null && !IsCountdownRenderer(spriteRenderer))
            {
                return spriteRenderer;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a sprite belongs to the countdown readout rather than to the breaker. The
    /// readout is placed, coloured and sized on its own terms, so everything that sweeps the
    /// breaker's sprites has to step over it.
    /// </summary>
    private bool IsCountdownRenderer(SpriteRenderer spriteRenderer) =>
        spriteRenderer == countdownBackground || spriteRenderer == countdownGlow;

    /// <summary>
    /// Puts the breaker back to its normal colour and takes the charge-up's visuals down.
    /// Run wherever a countdown ends - knocked out, detonated or pooled - so a breaker never
    /// falls, or comes back out of the pool, still glowing with motes flying at it.
    /// </summary>
    private void StopChargeEffects()
    {
        inflowBacklog = 0f;
        ApplyChargeGlow(0f);

        if (chargeHalo != null)
        {
            chargeHalo.enabled = false;
            // Emptied as well as switched off: the pool re-enables every renderer it finds on
            // the way out, so the halo has to be invisible on its own terms too.
            chargeHalo.color = Color.clear;
        }

        if (chargeInflow != null)
        {
            // Cleared rather than left to finish: motes still in flight would converge on a
            // breaker that is no longer standing there.
            chargeInflow.Clear();
        }
    }

    /// <summary>A soft-edged disc with a hot core - the halo, and every mote drawn into it.</summary>
    private static Texture2D GetGlowTexture()
    {
        if (glowTexture != null)
        {
            return glowTexture;
        }

        const int Resolution = 128;
        glowTexture = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false)
        {
            name = "Charge Glow Texture",
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
                // Raised well past the flash disc's falloff, which holds near-solid white to
                // its rim: a glow wants most of its width spent fading out.
                float alpha = Mathf.Pow(Mathf.Clamp01(1f - distance), 2.5f);
                pixels[y * Resolution + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        glowTexture.SetPixels32(pixels);
        glowTexture.Apply();
        return glowTexture;
    }

    private static Sprite GetGlowSprite()
    {
        if (glowSprite != null)
        {
            return glowSprite;
        }

        Texture2D texture = GetGlowTexture();
        // Sized off the texture's own width, so the halo is one world unit across at scale
        // one and its serialized size reads directly in the breaker's widths.
        glowSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            texture.width);
        glowSprite.name = "Charge Glow Sprite";
        glowSprite.hideFlags = HideFlags.HideAndDontSave;
        return glowSprite;
    }

    private static Material GetGlowMaterial()
    {
        if (glowMaterial != null)
        {
            return glowMaterial;
        }

        Shader spriteShader = Shader.Find("Sprites/Default");
        if (spriteShader == null)
        {
            return null;
        }

        glowMaterial = new Material(spriteShader)
        {
            name = "Shared Charge Glow Material",
            mainTexture = GetGlowTexture(),
            hideFlags = HideFlags.HideAndDontSave
        };

        return glowMaterial;
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
        // A countdown already running is committed; it explodes wherever it stands. A
        // knocked-out breaker is done with cages altogether and just finishes its fall.
        if (state == BreakerState.Breaking
            || state == BreakerState.Falling
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
        fallVelocity = Vector2.zero;
        fallElapsed = 0f;
        SetSpriteOpacity(1f);

        // Everything the charge-up and the fall wrote straight onto the transform, put back:
        // a breaker out of the pool starts its normal size, upright and on its body, not
        // swollen, belly-up and a shake's width off it - and its normal colour, not glowing.
        StopChargeEffects();
        CaptureBaseScale();
        chargeScale = 1f;
        chargeBaseScale = baseScale;
        transform.localScale = baseScale;
        transform.rotation = Quaternion.identity;
        if (rb != null)
        {
            transform.position = rb.position;
        }

        if (enemyCollider != null)
        {
            enemyCollider.enabled = true;
        }

        HideCountdown();
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
        StopCountdownSound();
        state = hiddenState;
        countdownRemaining = 0f;
        SetSpriteOpacity(sneakingOpacity);
        HideCountdown();
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

        // Captured before the countdown display is placed, which is measured off the anchor
        // so the number holds still while the breaker underneath it rattles. The scale is
        // taken as it stands, mirrored side included, so the swell does not flip its facing.
        breakingAnchor = Position;
        chargeBaseScale = transform.localScale;
        chargeScale = 1f;
        inflowBacklog = 0f;

        // A system that is not playing swallows Emit, and being deactivated on the way into
        // the pool is exactly the sort of thing that stops one.
        if (chargeInflow != null && !chargeInflow.isPlaying)
        {
            chargeInflow.Play();
        }

        SetSpriteOpacity(1f);
        countdownText.gameObject.SetActive(true);
        if (countdownBackground != null)
        {
            countdownBackground.gameObject.SetActive(
                countdownBackgroundSprite != null);
        }

        if (countdownGlow != null)
        {
            // Re-tinted on the way in: the sneak's fade runs over every sprite hanging off the
            // breaker, and one authored on the prefab would come out of it flattened to opaque.
            countdownGlow.color = countdownGlowColor;
            countdownGlow.gameObject.SetActive(HasCountdownGlow);
        }

        UpdateCountdownText();
        UpdateCountdownPosition();
        countdownLoopSource = AudioController.PlayLoop(countdownLoopSfx, gameObject);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (state == BreakerState.Breaking && IsPlayerCollider(other))
        {
            EnterFallingState();
        }
    }

    /// <summary>
    /// The player running the breaker down. It is knocked out rather than blown up: the
    /// countdown stops, it rolls belly-up, takes a small punt upwards and is handed to
    /// gravity, which carries it off the bottom of the screen.
    /// </summary>
    private void EnterFallingState()
    {
        StopCountdownSound();
        // Ahead of the puff, so the charge-up is off the breaker in the same frame it is
        // knocked out: a corpse dropping off the screen still glowing, with motes chasing
        // it down, would read as one still about to go off.
        StopChargeEffects();
        PlayDefeatEffect();

        state = BreakerState.Falling;
        countdownRemaining = 0f;
        fallVelocity = new Vector2(0f, defeatFlingSpeed);
        fallElapsed = 0f;

        // Off physics for the drop: the shared enemy step would steer this velocity back to
        // zero and hold the body above the terrain, and neither suits something on its way
        // off the screen. A kinematic body with no velocity is also left alone by the step
        // that turns enemies to face where they are going, so the roll-over sticks.
        rb.linearVelocity = Vector2.zero;
        rb.bodyType = RigidbodyType2D.Kinematic;
        if (enemyCollider != null)
        {
            enemyCollider.enabled = false;
        }

        // Dropped back onto the anchor at its normal size, then mirrored on Y - the same
        // flip the enemy uses to face left, which upright reads as belly-up.
        transform.position = breakingAnchor;
        transform.rotation = Quaternion.identity;
        chargeScale = 1f;
        Vector3 scale = chargeBaseScale;
        scale.y = -Mathf.Abs(scale.y);
        transform.localScale = scale;

        // Back to the flapping loop. The explosion clip has no transition out of its own
        // state, so a breaker caught in the last stretch of its countdown would otherwise
        // fall as a frozen blast frame.
        if (animator != null)
        {
            animator.SetBool("DoExplosionEffect", false);
            animator.Play("Breaker", 0, 0f);
        }

        HideCountdown();
    }

    /// <summary>
    /// Integrated here rather than left to the physics body, which the shared enemy step
    /// would keep flying. Ends the moment the breaker is clear of the bottom of the screen.
    /// </summary>
    private void UpdateDefeatFall()
    {
        float deltaTime = Time.deltaTime;
        fallElapsed += deltaTime;
        fallVelocity.y -= defeatFallGravity * deltaTime;

        Vector2 position = (Vector2)transform.position + fallVelocity * deltaTime;
        transform.position = position;
        rb.position = position;

        if (fallElapsed >= defeatFallTimeout || HasFallenOffScreen(position))
        {
            ReleaseOrDestroy();
        }
    }

    /// <summary>
    /// Whether the fall has cleared the bottom of the screen by the margin, so the breaker
    /// can be taken off the field out of sight. Always false with no camera to measure
    /// against - the timeout is what ends the fall there.
    /// </summary>
    private bool HasFallenOffScreen(Vector2 position)
    {
        Camera worldCamera = Camera.main;
        return worldCamera != null
            && worldCamera.WorldToViewportPoint(position).y < -defeatFallScreenMargin;
    }

    private void Explode()
    {
        StopCountdownSound();
        // The charge collapses into the blast rather than carrying on through it: the blast
        // is its own effect, and it lives at the scene root so it outlives the body.
        StopChargeEffects();
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

    private void StopCountdownSound()
    {
        AudioController.StopLoop(countdownLoopSource);
        countdownLoopSource = null;
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
        }

        // Placed in world space every frame, so the local offset is only a starting point.
        countdownText.transform.localPosition = Vector3.zero;
        countdownText.transform.localScale =
            Vector3.one * Mathf.Max(0f, countdownTextScale);
        countdownText.alignment = TextAlignmentOptions.Center;
        countdownText.fontSize = Mathf.Max(1f, countdownFontSize);
        countdownText.textWrappingMode = TextWrappingModes.NoWrap;

        // The frontmost sorting layer the project has: the readout is the one thing on the
        // breaker that must never end up behind a tower, a cage or the blast it is counting to.
        countdownText.sortingLayerID = SortingLayer.NameToID(CountdownSortingLayer);
        countdownText.sortingOrder = CountdownSortingOrder;
        countdownText.gameObject.SetActive(false);
        countdownTextBaseScale = countdownText.transform.localScale;

        EnsureCountdownBackground();
        EnsureCountdownGlow();
    }

    /// <summary>
    /// Divides the breaker's scale back out of the countdown. All three parts of it hang off
    /// the breaker, so the charge-up's swell swells them too - which a readout pinned to the
    /// edge of the screen has no business doing - and the flip that turns the breaker to face
    /// the player mirrors them, which prints the number back to front. Taken off the whole
    /// scale, sign included, so both come out at once.
    /// </summary>
    private void ApplyCountdownScale()
    {
        Vector3 lossyScale = transform.lossyScale;
        Vector3 inverse = new Vector3(
            Mathf.Approximately(lossyScale.x, 0f) ? 1f : 1f / lossyScale.x,
            Mathf.Approximately(lossyScale.y, 0f) ? 1f : 1f / lossyScale.y,
            1f);

        countdownText.transform.localScale = Vector3.Scale(countdownTextBaseScale, inverse);
        if (countdownBackground != null)
        {
            countdownBackground.transform.localScale =
                Vector3.Scale(countdownBackgroundBaseScale, inverse);
        }

        if (countdownGlow != null)
        {
            countdownGlow.transform.localScale =
                Vector3.Scale(countdownGlowBaseScale, inverse);
        }
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
        countdownBackground.transform.localScale =
            Vector3.one * Mathf.Max(0f, countdownSpriteScale);
        countdownBackground.sortingLayerID = countdownText.sortingLayerID;
        countdownBackground.sortingOrder = countdownText.sortingOrder - 1;
        countdownBackground.gameObject.SetActive(false);
        countdownBackgroundBaseScale = countdownBackground.transform.localScale;
    }

    /// <summary>
    /// A soft disc laid under the readout - the same one the charge halo is drawn with - so
    /// the number holds up over whatever the field has put behind it. Set once and left
    /// alone: it does not beat with the charge-up, which would pull the eye off the number.
    /// </summary>
    private void EnsureCountdownGlow()
    {
        if (countdownGlow == null)
        {
            Transform existingGlow = transform.Find("Break Countdown Glow");
            if (existingGlow != null)
            {
                countdownGlow = existingGlow.GetComponent<SpriteRenderer>();
            }
        }

        if (countdownGlow == null)
        {
            GameObject glowObject = new GameObject("Break Countdown Glow");
            glowObject.transform.SetParent(transform, false);
            countdownGlow = glowObject.AddComponent<SpriteRenderer>();
        }

        countdownGlow.sprite = GetGlowSprite();
        countdownGlow.color = countdownGlowColor;
        countdownGlow.transform.localScale =
            Vector3.one * Mathf.Max(0f, countdownGlowSize);

        // Under both the number and the artwork it backs, which is the whole point of it,
        // and on their layer so it is still in front of the field they are read over.
        countdownGlow.sortingLayerID = countdownText.sortingLayerID;
        countdownGlow.sortingOrder = countdownText.sortingOrder - 2;
        countdownGlow.gameObject.SetActive(false);
        countdownGlowBaseScale = countdownGlow.transform.localScale;
    }

    /// <summary>Whether the glow has been given a width worth drawing.</summary>
    private bool HasCountdownGlow => countdownGlow != null && countdownGlowSize > 0f;

    /// <summary>
    /// Takes the whole readout - number, artwork and glow - off the field. Run wherever a
    /// countdown ends, so none of the three can be left hanging over an empty spot.
    /// </summary>
    private void HideCountdown()
    {
        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(false);
        }

        if (countdownBackground != null)
        {
            countdownBackground.gameObject.SetActive(false);
        }

        if (countdownGlow != null)
        {
            countdownGlow.gameObject.SetActive(false);
        }
    }

    private void UpdateCountdownText()
    {
        if (countdownText != null)
        {
            countdownText.text =
                Mathf.Max(0, Mathf.CeilToInt(countdownRemaining)).ToString();
        }
    }

    /// <summary>
    /// Sits the countdown a fixed step off the breaker, on the line towards the player, and
    /// slides it further along that line when the spot is off screen - so a breaker that has
    /// wandered out of view leaves its readout at the edge, pointing back at where it stands.
    /// </summary>
    private void UpdateCountdownPosition()
    {
        if (countdownText == null)
        {
            return;
        }

        // Measured off the anchor rather than the transform, and scaled back out of the
        // charge-up's swell: the countdown is a readout, so it holds still and holds its
        // size while the breaker under it rattles and grows.
        ApplyCountdownScale();

        Vector3 anchorPosition = breakingAnchor;
        Transform player = EnemySimulationManager.InstanceOrNull?.Player;

        // Up is the fallback the whole placement leans on with no player to take a side from,
        // which is what the countdown did before it took sides at all.
        Vector2 towardsPlayer = player != null
            ? (Vector2)player.position - (Vector2)anchorPosition
            : Vector2.up;
        if (towardsPlayer.sqrMagnitude <= 0.000001f)
        {
            towardsPlayer = Vector2.up;
        }

        towardsPlayer.Normalize();

        float standoff = Mathf.Max(0f, countdownDistance);
        Vector3 displayWorldPosition =
            anchorPosition + (Vector3)(towardsPlayer * standoff);

        Camera worldCamera = Camera.main;
        if (worldCamera != null && player != null)
        {
            float travel = ResolveOnScreenTravel(
                worldCamera,
                displayWorldPosition,
                player.position,
                countdownScreenEdgeInset);
            if (travel > 0f)
            {
                Vector3 towardsPlayerPosition = new Vector3(
                    player.position.x,
                    player.position.y,
                    displayWorldPosition.z);
                displayWorldPosition = Vector3.Lerp(
                    displayWorldPosition, towardsPlayerPosition, travel);

                // A player standing nearer than the standoff puts the walk back towards the
                // breaker rather than away from it, which would sit the readout on top of the
                // thing it is counting down for. The step off the breaker is the floor.
                Vector2 fromAnchor = (Vector2)displayWorldPosition - (Vector2)anchorPosition;
                if (fromAnchor.magnitude < standoff)
                {
                    displayWorldPosition =
                        anchorPosition + (Vector3)(towardsPlayer * standoff);
                }
            }
        }

        // Taken from where the readout ended up rather than from the player, so the artwork
        // holds its facing after it has been pushed along the line. It runs from the breaker
        // outwards, the artwork pointing back down the line it is given. Worked out whether or
        // not there is artwork to turn: the number is placed in this same frame.
        Vector2 awayFromBreaker =
            (Vector2)displayWorldPosition - (Vector2)anchorPosition;
        if (awayFromBreaker.sqrMagnitude <= 0.000001f)
        {
            awayFromBreaker = towardsPlayer;
        }

        float directionAngle =
            Mathf.Atan2(awayFromBreaker.y, awayFromBreaker.x) * Mathf.Rad2Deg;
        Quaternion artworkRotation = Quaternion.Euler(
            0f,
            0f,
            directionAngle + countdownSpriteRotationOffset);

        // Set inside the artwork rather than on its pivot, and turned with it: the pivot is
        // the middle of the whole sprite, point included, which leaves the number riding high
        // towards the point - and swinging out of the face entirely once the artwork turns.
        Vector3 faceWorldPosition = displayWorldPosition
            + artworkRotation
                * (Vector3)(countdownTextOffset * Mathf.Max(0f, countdownSpriteScale));

        countdownText.gameObject.SetActive(true);
        countdownText.transform.position = faceWorldPosition;
        countdownText.transform.rotation = Quaternion.identity;
        if (countdownGlow != null)
        {
            // Sat with the number rather than on the pivot, the whole point of it being to
            // back the number. Left unturned: a disc has no facing, and spinning it with the
            // artwork would only shimmer its edge as the breaker and player move about.
            countdownGlow.gameObject.SetActive(HasCountdownGlow);
            countdownGlow.transform.position = faceWorldPosition;
            countdownGlow.transform.rotation = Quaternion.identity;
        }

        if (countdownBackground != null)
        {
            countdownBackground.gameObject.SetActive(
                countdownBackgroundSprite != null);
            countdownBackground.transform.position = displayWorldPosition;
            countdownBackground.transform.rotation = artworkRotation;
        }
    }

    /// <summary>
    /// How far along the line from <paramref name="idealPosition"/> to the player the readout
    /// has to be dragged before it clears the screen edge, as a share of that line. 0 when it
    /// already shows, 1 when even the player's own spot will not do.
    /// </summary>
    private static float ResolveOnScreenTravel(
        Camera worldCamera,
        Vector3 idealPosition,
        Vector3 playerPosition,
        float screenEdgeInset)
    {
        Vector3 idealViewportPosition = worldCamera.WorldToViewportPoint(idealPosition);
        Vector3 playerViewportPosition = worldCamera.WorldToViewportPoint(playerPosition);

        // Behind the camera the viewport point mirrors through the centre and reads as
        // on-screen when it is not, so the walk is taken as far as it goes.
        if (idealViewportPosition.z <= 0f)
        {
            return 1f;
        }

        // The inset is in pixels, and everything below this is in viewport shares. Half a
        // screen is the ceiling: a wider inset would leave no strip to aim at.
        float insetX = worldCamera.pixelWidth > 0f
            ? Mathf.Clamp(screenEdgeInset / worldCamera.pixelWidth, 0f, 0.49f)
            : 0f;
        float insetY = worldCamera.pixelHeight > 0f
            ? Mathf.Clamp(screenEdgeInset / worldCamera.pixelHeight, 0f, 0.49f)
            : 0f;

        float travel = ResolveAxisEntryTravel(
            idealViewportPosition.x, playerViewportPosition.x, insetX, 1f - insetX);
        travel = Mathf.Max(
            travel,
            ResolveAxisEntryTravel(
                idealViewportPosition.y, playerViewportPosition.y, insetY, 1f - insetY));

        return Mathf.Clamp01(travel);
    }

    /// <summary>
    /// The share of the walk at which one axis first falls inside the visible strip. The
    /// larger of the two axes is where the point as a whole comes into view, the screen being
    /// a rectangle and the walk a straight line across it.
    /// </summary>
    private static float ResolveAxisEntryTravel(float from, float to, float minimum, float maximum)
    {
        if (from >= minimum && from <= maximum)
        {
            return 0f;
        }

        float span = to - from;
        if (Mathf.Abs(span) <= 0.000001f)
        {
            return 1f;
        }

        float edge = from < minimum ? minimum : maximum;
        float travel = (edge - from) / span;

        // Negative means the walk heads away from the strip on this axis, so it never enters.
        return travel <= 0f ? 1f : Mathf.Min(1f, travel);
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
            if (spriteRenderer == null || spriteRenderer == countdownGlow)
            {
                // The glow carries its own alpha, and the sneak's fade would flatten it.
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

    internal static bool IsValidTarget(CageTower cage)
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
