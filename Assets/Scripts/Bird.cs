using Unity.VisualScripting;
using UnityEngine;

public class Bird : Enemy
{
    public enum BirdState
    {
        Sneaking,
        Attacking
    }

    /// <summary>Which half of the strafing run the bird is on: the dive in, or the climb out.</summary>
    private enum DiveState
    {
        Diving,
        PullingUp
    }

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 8f;
    [SerializeField] private float acceleration = 10f;
    [SerializeField, Min(0.1f), Tooltip("How sharply the bird snaps onto a new heading, in "
        + "multiples per second, and the dial that decides whether this reads as a fly-by. "
        + "Other enemies run at 1, which takes a full second to reverse - the bird coasts "
        + "roughly its own speed in units past the player before it can turn, and cannot get "
        + "back up to speed for the next run. Too high is just as wrong: past about 5 it turns "
        + "inside its own body length and corners like a robot instead of arcing through.")]
    private float steeringResponse = 1.6f;

    [Header("Dive")]
    [SerializeField, Min(0f), Tooltip("Distance from the player at which the bird locks its dive "
        + "heading. Inside this it stops steering, so it flies through the player instead of "
        + "curving in and hovering on them.")]
    private float diveCommitDistance = 2.5f;
    [SerializeField, Min(0f), Tooltip("How long the bird climbs away after a pass before turning "
        + "back for the next dive. Together with the speed it holds, this is what sets how far "
        + "past the player it gets.")]
    private float pullUpDuration = 0.4f;
    [SerializeField, Min(0f), Tooltip("Speed the bird bleeds down towards during that climb. A "
        + "short pull-up never quite reaches it, which is what makes the climb read as a "
        + "slowdown rather than a stop.")]
    private float pullUpSpeed = 4f;
    [SerializeField, Min(0f), Tooltip("How quickly speed comes off during the climb.")]
    private float pullUpDeceleration = 2f;
    [SerializeField, Range(0f, 90f), Tooltip("How steeply the bird climbs away after a pass, in "
        + "degrees above horizontal. Shallow runs flat and wide; steep climbs high and stays "
        + "over the player. Height gained is roughly the pull-up distance times the sine of "
        + "this.")]
    private float pullUpClimbAngle = 55f;

    [Header("States")]
    [SerializeField, Min(0f), Tooltip("The bird attacks once it is this close to the player.")]
    private float attackDistance = 4f;
    [SerializeField, Min(0f), Tooltip("How long the bird remains attacking after leaving attack distance.")]
    private float attackExitDelay = 1f;
    [SerializeField, Range(0f, 1f)] private float sneakingOpacity = 0.25f;

    [Header("Countdown")]
    [SerializeField, Min(0f)] private float countdownDuration = 10f;
    [SerializeField, Min(0f)] private float escapeSpeed = 14f;
    [SerializeField, Min(0.01f), Tooltip("Maximum time spent rushing upward before despawning.")]
    private float escapeDuration = 2f;

    [Header("Timer From Damage")]
    [SerializeField] private bool damageIncreasesTimer = true;
    [SerializeField, Min(0.01f)] private float firstDamageThreshold = 1f;
    [SerializeField, Min(1.01f)] private float damageThresholdMultiplier = 2f;
    [SerializeField, Min(0f)] private float secondsAddedPerThreshold = 1f;

    [Header("Cage")]
    [SerializeField, Min(0f)] private float uncageableDurationAfterRelease = 2f;

    [Header("Stealth Puff")]
    [Tooltip("Burst fired where the bird drops into stealth and where it breaks back out "
        + "of it. Left empty, a puff of smoke is built in code. A system assigned here "
        + "should be a child of the bird, since it is only told when to emit.")]
    [SerializeField] private ParticleSystem stealthPuff;
    [Tooltip("Smoke blobs per burst. Zero turns the effect off.")]
    [SerializeField, Min(0)] private int stealthPuffParticles = 12;
    [Tooltip("Colour and size of the built-in puff. Ignored when a system is assigned above.")]
    [SerializeField] private Color stealthPuffColor = new Color(0.86f, 0.86f, 0.89f, 0.8f);
    [SerializeField, Min(0.01f)] private float stealthPuffRadius = 0.35f;

    [Header("Contact Damage")]
    [SerializeField, Min(0), Tooltip("Damage dealt to the player on contact. The player's own "
        + "invincibility frames decide how often a bird pressed against them can land a hit.")]
    private int contactDamage = 1;

    public float currentSpeed;

    private float countdownRemaining;
    private float damageTowardThreshold;
    private float nextDamageThreshold;
    private float cageableAgainTime;
    private float escapeTimeRemaining;
    private float attackExitTimeRemaining;
    private SpriteRenderer[] spriteRenderers;
    private BirdState state;
    private DiveState diveState;
    private Vector2 diveHeading = Vector2.right;
    private Vector2 pullUpDirection = Vector2.up;
    private bool diveHeadingLocked;
    private float pullUpTimeRemaining;
    private bool escaping;
    private const int PuffTextureSize = 64;
    private static Material puffMaterial;
    private static Texture2D puffTexture;

    public BirdState State => state;
    public float CountdownRemaining => countdownRemaining;
    public override bool CanBeCaged =>
        base.CanBeCaged
        && state == BirdState.Attacking
        && Time.time >= cageableAgainTime
        && !escaping;

    // A diver has to be able to reverse in its own body length or the pull-up coasts it
    // halfway across the arena, so it turns far harder than the drifting default.
    protected override float SteeringResponse => steeringResponse;

    // A bird rushing for the sky is done with the player, so its escape run is harmless.
    public override int ContactDamage =>
        !escaping && state == BirdState.Attacking ? contactDamage : 0;

    protected override void Awake()
    {
        base.Awake();
        spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        EnsureStealthPuff();
        ResetBirdState();
    }

    /// <summary>
    /// Coming back on means being let out of a cage, where the opacity below was forced to
    /// full. The state the bird returns in decides what it should look like: the Enter*State
    /// methods only repaint on a change, so a bird released while still sneaking would
    /// otherwise keep wearing its caged look until it happened to switch states.
    /// </summary>
    protected override void OnEnable()
    {
        base.OnEnable();
        SetSpriteOpacity(state == BirdState.Attacking ? 1f : sneakingOpacity);
    }

    /// <summary>
    /// Being caged is nothing more than this script being switched off - the only notice the
    /// bird gets that it has been caught - and a caught bird has to read through the bars at
    /// full strength rather than at its sneaking fade. A bird taken mid-dive is already
    /// opaque, but one a round retry puts back in its cage comes out of the pool sneaking and
    /// is frozen before it can ever look at the player, so without this it sits there nearly
    /// invisible. Despawning trips this too, harmlessly: it is already out of sight, and the
    /// pool sets the opacity again on the way back out.
    /// </summary>
    protected override void OnDisable()
    {
        base.OnDisable();
        SetSpriteOpacity(1f);
    }

    private void Update()
    {
        if (escaping)
        {
            escapeTimeRemaining -= Time.deltaTime;
            Camera mainCamera = Camera.main;
            bool isAboveScreen = mainCamera != null
                && mainCamera.WorldToViewportPoint(transform.position).y > 1.1f;
            if (isAboveScreen || escapeTimeRemaining <= 0f)
            {
                ReleaseOrDestroy();
            }

            return;
        }

        if (!HasValidCage())
        {
            BeginEscape();
            return;
        }

        UpdateDiveSpeed(Time.deltaTime);
        countdownRemaining = Mathf.Max(0f, countdownRemaining - Time.deltaTime);
        UpdateCombatState();

        if (countdownRemaining <= 0f)
        {
            BeginEscape();
        }
    }

    /// <summary>
    /// Birds only stay while there is a whole, occupied cage they can be caught in.
    /// This deliberately uses the cage breaker's target rule so both enemies agree on
    /// whether a cage is still valid.
    /// </summary>
    private static bool HasValidCage()
    {
        CageTower[] cages = FindObjectsByType<CageTower>(FindObjectsSortMode.None);
        for (int i = 0; i < cages.Length; i++)
        {
            CageTower cage = cages[i];
            if (cage != null
            && cage.State == CageTower.CageState.Empty)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A strafing run rather than a chase. The bird tracks the player only up to its commit
    /// distance, then holds that heading straight through them at full speed, and afterwards
    /// climbs away shedding speed before turning back in for the next dive. Tracking the
    /// player the whole way is what used to make it slow to a hover on their face: the
    /// heading has to stop turning before the bird arrives, or the steering bends the last
    /// stretch of the dive into an orbit.
    /// </summary>
    protected override Vector2 CalculateDesiredVelocity(Transform player, float elapsed)
    {
        if (escaping)
        {
            return Vector2.up * escapeSpeed;
        }

        if (player == null)
        {
            return Vector2.zero;
        }

        Vector2 toPlayer = (Vector2)player.position - Position;
        if (diveState == DiveState.PullingUp)
        {
            pullUpTimeRemaining -= elapsed;
            if (pullUpTimeRemaining > 0f)
            {
                return pullUpDirection * currentSpeed;
            }

            BeginDive();
        }

        float distanceSquared = toPlayer.sqrMagnitude;
        if (!diveHeadingLocked)
        {
            if (distanceSquared <= 0.000001f)
            {
                return Vector2.zero;
            }

            // Steered right up to the commit point, and on rails from there in.
            diveHeading = toPlayer / Mathf.Sqrt(distanceSquared);
            diveHeadingLocked = distanceSquared <= diveCommitDistance * diveCommitDistance;
        }
        else if (Vector2.Dot(toPlayer, diveHeading) < 0f)
        {
            // The player is behind the bird, so the pass is spent. Tested as a half-space
            // rather than a distance: once crossed it stays crossed, so however fast the
            // bird is going, it cannot fly past between two decision ticks unnoticed.
            BeginPullUp();
            return pullUpDirection * currentSpeed;
        }

        return diveHeading * currentSpeed;
    }

    private void BeginDive()
    {
        diveState = DiveState.Diving;
        diveHeadingLocked = false;
    }

    /// <summary>
    /// Turns the bird up and away after a pass. It keeps running the way the dive was
    /// already going and only tilts up by the climb angle, so it sweeps out and over
    /// instead of braking to turn - and, being a sharp turner, it holds that line rather
    /// than drifting wide the way a heavier enemy would.
    /// </summary>
    private void BeginPullUp()
    {
        diveState = DiveState.PullingUp;
        pullUpTimeRemaining = pullUpDuration;
        diveHeadingLocked = false;

        float radians = pullUpClimbAngle * Mathf.Deg2Rad;
        float horizontalSign = diveHeading.x >= 0f ? 1f : -1f;
        pullUpDirection = new Vector2(
            horizontalSign * Mathf.Cos(radians),
            Mathf.Sin(radians));
    }

    /// <summary>
    /// Speed only comes off on the way out. The dive itself runs all the way in at the
    /// bird's top pace, so it never brakes in front of the player - it is past them before
    /// it starts slowing, and sheds the speed into the climb.
    /// </summary>
    private void UpdateDiveSpeed(float deltaTime)
    {
        bool pullingUp = diveState == DiveState.PullingUp;
        currentSpeed = Mathf.MoveTowards(
            currentSpeed,
            pullingUp ? pullUpSpeed : moveSpeed,
            (pullingUp ? pullUpDeceleration : acceleration) * deltaTime);
    }

    public override bool TryTakeDamage(float damage)
    {
        // Sneaking birds do not catch projectiles. Returning false lets Projectile keep
        // flying through them; attacking birds still register hits for the countdown
        // mechanic, but never lose health.
        if (state != BirdState.Attacking || escaping || IsInvincible || damage <= 0f)
        {
            return false;
        }

        PlayHitFeedback(Position);
        if (!damageIncreasesTimer || escaping)
        {
            return true;
        }

        damageTowardThreshold += damage;
        while (damageTowardThreshold >= nextDamageThreshold)
        {
            damageTowardThreshold -= nextDamageThreshold;
            countdownRemaining += secondsAddedPerThreshold;
            nextDamageThreshold *= damageThresholdMultiplier;
        }

        return true;
    }

    public override void OnReleasedFromCage()
    {
        cageableAgainTime = Time.time + uncageableDurationAfterRelease;
    }

    protected override void ResetEnemyState()
    {
        ResetBirdState();
    }

    private void ResetBirdState()
    {
        currentSpeed = 0f;
        countdownRemaining = Mathf.Max(0f, countdownDuration);
        damageTowardThreshold = 0f;
        nextDamageThreshold = Mathf.Max(0.01f, firstDamageThreshold);
        cageableAgainTime = 0f;
        escapeTimeRemaining = 0f;
        attackExitTimeRemaining = 0f;
        state = BirdState.Sneaking;
        diveState = DiveState.Diving;
        diveHeading = Vector2.right;
        pullUpDirection = Vector2.up;
        diveHeadingLocked = false;
        pullUpTimeRemaining = 0f;
        escaping = false;
        SetSpriteOpacity(sneakingOpacity);
    }

    private void BeginEscape()
    {
        escaping = true;
        escapeTimeRemaining = Mathf.Max(0.01f, escapeDuration);
        currentSpeed = 0f;
    }

    private void UpdateCombatState()
    {
        Transform player = EnemySimulationManager.InstanceOrNull?.Player;
        if (player == null)
        {
            EnterSneakingState();
            return;
        }

        float range = Mathf.Max(0f, attackDistance);
        bool playerInRange =
            ((Vector2)player.position - Position).sqrMagnitude <= range * range;
        if (playerInRange)
        {
            attackExitTimeRemaining = Mathf.Max(0f, attackExitDelay);
            EnterAttackingState();
            return;
        }

        if (state != BirdState.Attacking)
        {
            return;
        }

        attackExitTimeRemaining -= Time.deltaTime;
        if (attackExitTimeRemaining <= 0f)
        {
            EnterSneakingState();
        }
    }

    private void EnterSneakingState()
    {
        if (state == BirdState.Sneaking)
        {
            return;
        }

        state = BirdState.Sneaking;
        attackExitTimeRemaining = 0f;
        SetSpriteOpacity(sneakingOpacity);
        PlayStealthPuff();
    }

    private void EnterAttackingState()
    {
        if (state == BirdState.Attacking)
        {
            return;
        }

        state = BirdState.Attacking;
        SetSpriteOpacity(1f);
        PlayStealthPuff();
    }

    /// <summary>
    /// The burst that covers the bird fading out of sight and snapping back into it. Both
    /// transitions use the same puff, so the two read as one effect running either way.
    /// Only fired from the Enter*State methods, which return early when the bird is
    /// already in that state - the puff cannot repeat while the state holds.
    /// </summary>
    private void PlayStealthPuff()
    {
        if (stealthPuff != null && stealthPuffParticles > 0)
        {
            stealthPuff.Emit(stealthPuffParticles);
        }
    }

    /// <summary>
    /// Builds the puff in code so the bird needs no particle asset of its own, the way its
    /// countdown text is built rather than authored. Simulated in world space, so a burst
    /// hangs where the bird changed state instead of being dragged along behind it.
    /// </summary>
    private void EnsureStealthPuff()
    {
        if (stealthPuff != null)
        {
            return;
        }

        GameObject puffObject = new GameObject("Stealth Puff");
        puffObject.transform.SetParent(transform, false);
        stealthPuff = puffObject.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = stealthPuff.main;
        // Looping keeps the system running with nothing to show, which is what lets a
        // later Emit() simulate. A one-shot system stops itself and swallows the burst.
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        // Smoke lingers: it lasts far longer than it travels, so the cloud is still
        // thinning out well after it has stopped moving.
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.3f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.45f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = stealthPuffColor;
        // Negative gravity: the cloud creeps upward as it thins, the way warm smoke does.
        main.gravityModifier = -0.12f;
        // Sized in world units off this object's own scale, ignoring the bird's. Enemies
        // flip by negating localScale.y to face the other way, and a puff measured through
        // that would turn itself inside out every time the bird changed direction.
        main.scalingMode = ParticleSystemScalingMode.Local;

        // Bursts come only from Emit(); nothing trickles out between transitions.
        ParticleSystem.EmissionModule emission = stealthPuff.emission;
        emission.enabled = false;

        // Filled circle rather than its rim, so the blobs overlap into one cloud sitting
        // over the bird instead of an expanding ring with a hole in the middle.
        ParticleSystem.ShapeModule shape = stealthPuff.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = Mathf.Max(0.01f, stealthPuffRadius);
        shape.radiusThickness = 1f;

        // Billowing: each blob roughly triples, most of it in the first third of its life,
        // so the cloud boils outward and then coasts while it fades.
        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = stealthPuff.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
            1f,
            new AnimationCurve(
                new Keyframe(0f, 0.45f),
                new Keyframe(0.35f, 1.3f),
                new Keyframe(1f, 1.8f)));

        // Slow tumble in either direction. With a lumpy blob texture that churns the
        // silhouette of the cloud, which is most of what separates smoke from a soft glow.
        ParticleSystem.RotationOverLifetimeModule roll = stealthPuff.rotationOverLifetime;
        roll.enabled = true;
        roll.z = new ParticleSystem.MinMaxCurve(-0.9f, 0.9f);

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = stealthPuff.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new[]
            {
                // Fading in over the first frames stops the cloud popping into existence
                // at full strength; the long tail is it dissipating.
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.12f),
                new GradientAlphaKey(0.5f, 0.5f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = fade;

        // Heavy drag: the blobs push out for a moment and then all but stall, which is
        // what makes it read as a puff of smoke rather than a burst flying apart.
        ParticleSystem.LimitVelocityOverLifetimeModule drag =
            stealthPuff.limitVelocityOverLifetime;
        drag.enabled = true;
        drag.limit = new ParticleSystem.MinMaxCurve(0.15f);
        drag.dampen = 0.7f;

        ParticleSystemRenderer puffRenderer = puffObject.GetComponent<ParticleSystemRenderer>();
        puffRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        puffRenderer.sharedMaterial = GetPuffMaterial();

        // Drawn with the bird rather than on a layer of its own, so the puff cannot end up
        // behind the terrain the bird is flying over.
        SpriteRenderer birdRenderer =
            spriteRenderers != null && spriteRenderers.Length > 0 ? spriteRenderers[0] : null;
        if (birdRenderer != null)
        {
            puffRenderer.sortingLayerID = birdRenderer.sortingLayerID;
            puffRenderer.sortingOrder = birdRenderer.sortingOrder + 1;
        }
    }

    /// <summary>
    /// The sprite shader carrying the soft blob texture below. The hit and death bursts
    /// leave their quads untextured, but a hard-edged square cannot read as smoke.
    /// One material serves every bird, since none of them ever changes it.
    /// </summary>
    private static Material GetPuffMaterial()
    {
        if (puffMaterial == null)
        {
            Shader spriteShader = Shader.Find("Sprites/Default");
            if (spriteShader != null)
            {
                puffMaterial = new Material(spriteShader)
                {
                    name = "Shared Stealth Puff Material",
                    hideFlags = HideFlags.HideAndDontSave,
                    mainTexture = GetPuffTexture()
                };
            }
        }

        return puffMaterial;
    }

    /// <summary>
    /// A soft round blob, drawn in code so the bird still needs no art asset. Alpha falls
    /// off toward the rim and the rim itself is knocked in and out by noise, so a handful
    /// of these overlapping look like one clump of smoke rather than a row of dots.
    /// </summary>
    private static Texture2D GetPuffTexture()
    {
        if (puffTexture != null)
        {
            return puffTexture;
        }

        puffTexture = new Texture2D(PuffTextureSize, PuffTextureSize, TextureFormat.RGBA32, false)
        {
            name = "Shared Stealth Puff Texture",
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp
        };

        Color[] pixels = new Color[PuffTextureSize * PuffTextureSize];
        float center = (PuffTextureSize - 1) * 0.5f;
        for (int y = 0; y < PuffTextureSize; y++)
        {
            for (int x = 0; x < PuffTextureSize; x++)
            {
                float offsetX = (x - center) / center;
                float offsetY = (y - center) / center;
                float distance = Mathf.Sqrt(offsetX * offsetX + offsetY * offsetY);

                // Noise sampled around a circle rather than off the angle directly, so the
                // lumps meet up where the sweep wraps instead of leaving a seam.
                float angle = Mathf.Atan2(offsetY, offsetX);
                float lump = Mathf.PerlinNoise(
                    2f + Mathf.Cos(angle) * 1.7f, 2f + Mathf.Sin(angle) * 1.7f);
                float edge = Mathf.Lerp(0.7f, 1f, lump);

                // Squared falloff: solid through the middle, feathering out to nothing at
                // the rim. Left linear the blobs have a visible circular outline.
                float alpha = Mathf.Clamp01(1f - distance / edge);
                pixels[y * PuffTextureSize + x] = new Color(1f, 1f, 1f, alpha * alpha);
            }
        }

        puffTexture.SetPixels(pixels);
        puffTexture.Apply();
        return puffTexture;
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

}
