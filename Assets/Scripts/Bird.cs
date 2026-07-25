using TMPro;
using UnityEngine;

public class Bird : Enemy
{
    public enum BirdState
    {
        Sneaking,
        Attacking
    }

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 8f;
    [SerializeField] private float acceleration = 5f;

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
    [SerializeField] private Vector2 countdownOffset = new Vector2(0f, 1.2f);
    [SerializeField, Min(1f)] private float countdownFontSize = 10f;
    [SerializeField] private TextMeshPro countdownText;

    [Header("Timer From Damage")]
    [SerializeField] private bool damageIncreasesTimer = true;
    [SerializeField, Min(0.01f)] private float firstDamageThreshold = 1f;
    [SerializeField, Min(1.01f)] private float damageThresholdMultiplier = 2f;
    [SerializeField, Min(0f)] private float secondsAddedPerThreshold = 1f;

    [Header("Cage")]
    [SerializeField, Min(0f)] private float uncageableDurationAfterRelease = 2f;

    [Header("Stealth Puff")]
    [Tooltip("Burst fired where the bird drops into stealth and where it breaks back out "
        + "of it. Left empty, a ring of puffs is built in code. A system assigned here "
        + "should be a child of the bird, since it is only told when to emit.")]
    [SerializeField] private ParticleSystem stealthPuff;
    [Tooltip("Puffs per burst. Zero turns the effect off.")]
    [SerializeField, Min(0)] private int stealthPuffParticles = 16;
    [Tooltip("Colour and size of the built-in puff. Ignored when a system is assigned above.")]
    [SerializeField] private Color stealthPuffColor = new Color(1f, 0.94f, 0.7f, 0.85f);
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
    private bool escaping;
    private static Material puffMaterial;

    public BirdState State => state;
    public float CountdownRemaining => countdownRemaining;
    public override bool CanBeCaged =>
        base.CanBeCaged
        && state == BirdState.Attacking
        && Time.time >= cageableAgainTime
        && !escaping;

    // A bird rushing for the sky is done with the player, so its escape run is harmless.
    public override int ContactDamage =>
        !escaping && state == BirdState.Attacking ? contactDamage : 0;

    protected override void Awake()
    {
        base.Awake();
        spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        EnsureCountdownText();
        EnsureStealthPuff();
        ResetBirdState();
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

        currentSpeed = Mathf.Clamp(currentSpeed + acceleration * Time.deltaTime, 0f, moveSpeed);
        countdownRemaining = Mathf.Max(0f, countdownRemaining - Time.deltaTime);
        UpdateCombatState();
        UpdateCountdownText();

        if (countdownRemaining <= 0f)
        {
            BeginEscape();
        }
    }

    private void LateUpdate()
    {
        if (countdownText == null)
        {
            return;
        }

        countdownText.transform.position = transform.position + (Vector3)countdownOffset;
        countdownText.transform.rotation = Quaternion.identity;
    }

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

        Vector2 direction = (Vector2)player.position - Position;
        float distanceSquared = direction.sqrMagnitude;
        if (distanceSquared <= 0.000001f)
        {
            return Vector2.zero;
        }

        return direction * (currentSpeed / Mathf.Sqrt(distanceSquared));
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

        UpdateCountdownText();
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
        escaping = false;
        SetSpriteOpacity(sneakingOpacity);
        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(true);
        }
        UpdateCountdownText();
    }

    private void BeginEscape()
    {
        escaping = true;
        escapeTimeRemaining = Mathf.Max(0.01f, escapeDuration);
        currentSpeed = 0f;
        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(false);
        }
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
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.45f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = stealthPuffColor;
        // Puffs hang in the air rather than dropping out of it.
        main.gravityModifier = 0f;
        // Sized in world units off this object's own scale, ignoring the bird's. Enemies
        // flip by negating localScale.y to face the other way, and a puff measured through
        // that would turn itself inside out every time the bird changed direction.
        main.scalingMode = ParticleSystemScalingMode.Local;

        // Bursts come only from Emit(); nothing trickles out between transitions.
        ParticleSystem.EmissionModule emission = stealthPuff.emission;
        emission.enabled = false;

        // Spawned on the rim of a circle and pushed outward along it, so the burst opens
        // as a ring around where the bird was rather than a blob on top of it.
        ParticleSystem.ShapeModule shape = stealthPuff.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = Mathf.Max(0.01f, stealthPuffRadius);
        shape.radiusThickness = 0f;

        // Each puff swells as it drifts, so the ring thins out into nothing instead of
        // staying a tight cluster of dots to the end of its life.
        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = stealthPuff.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
            1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.6f));

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
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0.8f, 0.35f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = fade;

        // Drag: the ring pushes out quickly and then settles, which is what separates a
        // puff of air from a spark burst flying apart at full speed.
        ParticleSystem.LimitVelocityOverLifetimeModule drag =
            stealthPuff.limitVelocityOverLifetime;
        drag.enabled = true;
        drag.limit = new ParticleSystem.MinMaxCurve(0.5f);
        drag.dampen = 0.35f;

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
    /// Untextured white quads on the sprite shader, matching the hit and death bursts.
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
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
        }

        return puffMaterial;
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

    private void EnsureCountdownText()
    {
        if (countdownText == null)
        {
            countdownText = GetComponentInChildren<TextMeshPro>(true);
        }

        if (countdownText == null)
        {
            GameObject textObject = new GameObject("Bird Countdown");
            textObject.transform.SetParent(transform, false);
            countdownText = textObject.AddComponent<TextMeshPro>();
            countdownText.rectTransform.sizeDelta = new Vector2(20f, 5f);
            countdownText.transform.localScale = Vector3.one * 0.1f;
        }

        countdownText.alignment = TextAlignmentOptions.Center;
        countdownText.fontSize = Mathf.Max(1f, countdownFontSize);
        countdownText.textWrappingMode = TextWrappingModes.NoWrap;
        countdownText.sortingOrder = 100;
        countdownText.gameObject.SetActive(true);
    }

    private void UpdateCountdownText()
    {
        if (countdownText != null)
        {
            countdownText.text = Mathf.CeilToInt(countdownRemaining).ToString();
        }
    }
}
