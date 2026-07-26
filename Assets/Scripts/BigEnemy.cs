using System.Collections;
using UnityEngine;

/// <summary>
/// The oversized basic enemy. It pursues exactly like one; everything added here is its
/// death, which is loud enough to be worth the wait: it locks in place, whites out,
/// shakes itself apart while throwing streaks of light, brightens past the camera's
/// bloom threshold, and detonates.
///
/// The sequence has to run while the enemy is still nominally alive, because
/// <see cref="Enemy.ApplySimulationStep"/> releases anything at or below zero health on
/// the next physics step. So the killing blow parks health just above zero and the
/// sequence writes a real zero at the end, handing the release - and the kill it records
/// - back to the base class untouched.
///
/// Every sound is synthesised here rather than authored as an asset, matching the way
/// the rest of the project builds its sprites, materials and particles at runtime.
/// </summary>
public sealed class BigEnemy : BasicEnemy
{
    [Header("Death - Timing")]
    [SerializeField, Min(0.05f), Tooltip("Seconds of shaking and brightening before the blast.")]
    private float chargeDuration = 1.4f;
    [SerializeField, Min(0f), Tooltip("Seconds the blast-white is held before the body is taken off screen.")]
    private float flashDuration = 0.07f;

    [Header("Death - Shake")]
    [SerializeField, Min(0f)] private float shakeAmplitude = 0.35f;
    [SerializeField, Min(0f)] private float shakeFrequency = 40f;

    [Header("Death - Brightness")]
    [SerializeField, Min(1f), Tooltip("Peak HDR value driven into the flash shader. The scene's "
        + "bloom threshold is 0.9, so anything past 1 blooms - the higher this goes, the wider "
        + "the halo it tears open before it blows.")]
    private float peakBrightness = 9f;

    [Header("Death - Streaks")]
    [SerializeField, Min(0)] private int streaksPerSecond = 45;
    [SerializeField] private Color streakColor = new Color(1f, 0.95f, 0.6f, 1f);

    [Header("Death - Explosion")]
    [SerializeField, Min(0)] private int explosionParticleCount = 90;
    [SerializeField] private Color explosionColor = new Color(1f, 0.75f, 0.35f, 1f);

    [Header("Death - Audio")]
    [SerializeField, Range(0f, 1f)] private float chargeVolume = 0.45f;
    [SerializeField, Range(0f, 1f)] private float crackleVolume = 0.35f;
    [SerializeField, Range(0f, 1f)] private float explosionVolume = 0.9f;
    [SerializeField, Min(0.02f), Tooltip("Seconds between the crackles that ride the streaks.")]
    private float crackleInterval = 0.16f;

    private const int SampleRate = 44100;
    private const float SurvivingHealth = 0.0001f;

    private static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");

    // One streak system, one explosion system and one set of clips serve every big enemy.
    // They live at the scene root so a burst outlives the body being pooled a frame later.
    private static ParticleSystem streaks;
    private static ParticleSystem explosion;
    private static Material particleMaterial;
    private static AudioClip chargeClip;
    private static float chargeClipDuration = -1f;
    private static AudioClip crackleClip;
    private static AudioClip explosionClip;

    private SpriteRenderer bodyRenderer;
    private EnemyHealthBar healthBar;
    private Material deathMaterial;
    private Coroutine deathRoutine;
    private bool isDying;

    // A hit landing during the sequence would restart the base class's own white flash
    // and fight this one for the sprite's material.
    public override bool CanTakeDamage => !isDying;

    // A corpse mid-detonation should not still be shouldering the player for damage.
    public override int ContactDamage => isDying ? 0 : base.ContactDamage;

    protected override void Awake()
    {
        base.Awake();
        bodyRenderer = GetComponent<SpriteRenderer>();
        healthBar = GetComponent<EnemyHealthBar>();
    }

    protected override Vector2 CalculateDesiredVelocity(Transform player, float elapsed)
    {
        return isDying ? Vector2.zero : base.CalculateDesiredVelocity(player, elapsed);
    }

    public override bool TryTakeDamage(float damageAmount)
    {
        bool landed = base.TryTakeDamage(damageAmount);
        if (landed && health <= 0f && !isDying)
        {
            BeginDeath();
        }

        return landed;
    }

    protected override void ResetEnemyState()
    {
        base.ResetEnemyState();

        isDying = false;
        deathRoutine = null;

        // Awake has not necessarily run yet: the pool acquires an instance before the
        // activation that wakes it, so these are still null on the very first acquire.
        if (bodyRenderer != null)
        {
            bodyRenderer.enabled = true;
        }

        if (healthBar != null)
        {
            healthBar.SetHidden(false);
        }
    }

    private void OnDestroy()
    {
        if (deathMaterial != null)
        {
            Destroy(deathMaterial);
        }
    }

    private void BeginDeath()
    {
        isDying = true;
        PlayDeathSfxOnce();

        // Held just above zero so the simulation step does not release the body out from
        // under the sequence. The real zero is written when the blast lands.
        health = SurvivingHealth;

        // Stops the ordinary hit flash and puts the original material back, so the
        // death material below is not overwritten when that coroutine next ticks.
        ResetHitFeedback();

        if (Body != null)
        {
            Body.linearVelocity = Vector2.zero;
            Body.angularVelocity = 0f;
            // Kinematic makes ApplySimulationStep skip its force application, which is
            // what leaves the shake below in sole control of where the body sits.
            Body.bodyType = RigidbodyType2D.Kinematic;
        }

        if (healthBar != null)
        {
            healthBar.SetHidden(true);
        }

        deathRoutine = StartCoroutine(DeathRoutine());
    }

    private IEnumerator DeathRoutine()
    {
        Material material = GetDeathMaterial();
        if (bodyRenderer != null && material != null)
        {
            bodyRenderer.sharedMaterial = material;
        }

        AudioController.Play(GetChargeClip(chargeDuration), chargeVolume);

        Vector3 anchor = transform.position;
        float shakeSeed = Random.value * 100f;
        float elapsed = 0f;
        float streakBacklog = 0f;
        float crackleTimer = 0f;

        while (elapsed < chargeDuration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / chargeDuration);

            // Perlin rather than white noise, so it reads as a body vibrating itself
            // apart instead of a sprite teleporting around a point.
            float amplitude = shakeAmplitude * Mathf.Lerp(0.2f, 1f, progress);
            float shakeTime = Time.time * shakeFrequency;
            Vector3 offset = new Vector3(
                Mathf.PerlinNoise(shakeSeed, shakeTime) - 0.5f,
                Mathf.PerlinNoise(shakeSeed + 31.7f, shakeTime) - 0.5f,
                0f) * (amplitude * 2f);
            transform.position = anchor + offset;

            // Squared so it idles dim for most of the charge and then runs away at the
            // end, which is where the bloom suddenly opens up.
            SetBrightness(Mathf.Lerp(1f, peakBrightness, progress * progress));

            streakBacklog += streaksPerSecond * Mathf.Lerp(0.3f, 1f, progress) * Time.deltaTime;
            int streaksThisFrame = Mathf.FloorToInt(streakBacklog);
            if (streaksThisFrame > 0)
            {
                streakBacklog -= streaksThisFrame;
                EmitStreaks(transform.position, streaksThisFrame);
            }

            crackleTimer -= Time.deltaTime;
            if (crackleTimer <= 0f)
            {
                crackleTimer = crackleInterval;
                AudioController.Play(
                    GetCrackleClip(),
                    crackleVolume,
                    Mathf.Lerp(0.75f, 1.7f, progress));
            }

            yield return null;
        }

        yield return Detonate(anchor);
    }

    private IEnumerator Detonate(Vector3 anchor)
    {
        transform.position = anchor;

        // Overdriven past the charge's peak for the instant of the blast itself.
        SetBrightness(peakBrightness * 1.6f);
        EmitExplosion(anchor);
        AudioController.Play(GetExplosionClip(), explosionVolume);

        if (flashDuration > 0f)
        {
            yield return new WaitForSeconds(flashDuration);
        }

        // Off screen before the release, so the white body cannot linger for the frame
        // or two between here and the next simulation step.
        if (bodyRenderer != null)
        {
            bodyRenderer.enabled = false;
        }

        deathRoutine = null;

        // Handing the kill back to the base class: it records the defeat and returns
        // the body to the pool on its next step, exactly as an ordinary death would.
        health = 0f;
    }

    private void SetBrightness(float brightness)
    {
        if (deathMaterial == null)
        {
            return;
        }

        deathMaterial.SetColor(FlashColorId, new Color(brightness, brightness, brightness, 1f));
    }

    private Material GetDeathMaterial()
    {
        if (deathMaterial != null)
        {
            return deathMaterial;
        }

        Shader flashShader = Shader.Find("TowerDefense/SpriteFlash");
        if (flashShader == null)
        {
            Debug.LogWarning("The TowerDefense/SpriteFlash shader could not be found.", this);
            return null;
        }

        // Per instance, not shared: two big enemies dying at once are at different points
        // in their brightness ramp and cannot share one material to say so.
        deathMaterial = new Material(flashShader)
        {
            name = "Big Enemy Death Flash Material",
            hideFlags = HideFlags.HideAndDontSave
        };
        return deathMaterial;
    }

    private void EmitStreaks(Vector3 position, int count)
    {
        if (count <= 0)
        {
            return;
        }

        if (streaks == null)
        {
            streaks = BuildStreakSystem(streakColor);
        }

        ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams
        {
            position = position,
            applyShapeToPosition = true
        };
        streaks.Emit(emitParams, count);
    }

    private void EmitExplosion(Vector3 position)
    {
        if (explosionParticleCount <= 0)
        {
            return;
        }

        if (explosion == null)
        {
            explosion = BuildExplosionSystem(explosionColor);
        }

        ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams
        {
            position = position,
            applyShapeToPosition = true
        };
        explosion.Emit(emitParams, explosionParticleCount);
    }

    private static ParticleSystem BuildStreakSystem(Color color)
    {
        ParticleSystem system = CreateBurstSystem("Big Enemy Death Streaks", 24);

        ParticleSystem.MainModule main = system.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 15f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
        main.startColor = new ParticleSystem.MinMaxGradient(Color.white, color);
        main.gravityModifier = 0f;

        ParticleSystem.ShapeModule shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.7f;
        // Emitted off the rim rather than through the disc, so every streak leaves
        // radially and none of them crawls out of the middle.
        shape.radiusThickness = 0f;

        ApplyFadeOut(system, color);

        ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
        renderer.lengthScale = 6f;
        renderer.sortingOrder = 20;

        return system;
    }

    private static ParticleSystem BuildExplosionSystem(Color color)
    {
        ParticleSystem system = CreateBurstSystem("Big Enemy Death Explosion", 25);

        ParticleSystem.MainModule main = system.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(8f, 22f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
        main.startColor = new ParticleSystem.MinMaxGradient(Color.white, color);
        main.gravityModifier = 0.4f;

        ParticleSystem.ShapeModule shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.9f;
        shape.radiusThickness = 0f;

        ApplyFadeOut(system, color);

        ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
        renderer.lengthScale = 3f;
        renderer.sortingOrder = 21;

        return system;
    }

    /// <summary>
    /// A world-space system that only ever fires from <c>Emit</c>. It is left looping on
    /// purpose: a stopped system swallows the burst, so it runs forever with nothing to
    /// show and simulates only what is pushed into it.
    /// </summary>
    private static ParticleSystem CreateBurstSystem(string systemName, int sortingOrder)
    {
        GameObject systemObject = new GameObject(systemName);
        ParticleSystem system = systemObject.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = system.main;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = false;

        ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.sortingLayerName = "Foreground";
        renderer.sortingOrder = sortingOrder;
        renderer.sharedMaterial = GetParticleMaterial();

        return system;
    }

    private static void ApplyFadeOut(ParticleSystem system, Color color)
    {
        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = system.colorOverLifetime;
        colorOverLifetime.enabled = true;

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(color, 0.35f),
                new GradientColorKey(color, 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 0.5f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = gradient;
    }

    private static Material GetParticleMaterial()
    {
        if (particleMaterial != null)
        {
            return particleMaterial;
        }

        Shader spriteShader = Shader.Find("Sprites/Default");
        if (spriteShader != null)
        {
            particleMaterial = new Material(spriteShader)
            {
                name = "Big Enemy Death Particle Material",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        return particleMaterial;
    }

    /// <summary>
    /// The charge: two detuned voices sweeping upward with an accelerating curve, so the
    /// pitch climbs fastest over the last stretch where the body is brightest. Rebuilt if
    /// the tuned duration changes, since the clip has to end exactly on the blast.
    /// </summary>
    private static AudioClip GetChargeClip(float duration)
    {
        if (chargeClip != null && Mathf.Approximately(chargeClipDuration, duration))
        {
            return chargeClip;
        }

        int sampleCount = Mathf.Max(1, Mathf.RoundToInt(duration * SampleRate));
        float[] samples = new float[sampleCount];
        float phase = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float progress = (float)i / sampleCount;
            float frequency = Mathf.Lerp(140f, 1500f, progress * progress);
            phase += 2f * Mathf.PI * frequency / SampleRate;

            // The fifth above turns a clean tone into a whine with some grit in it.
            float tone = Mathf.Sin(phase) * 0.7f + Mathf.Sin(phase * 1.5f) * 0.3f;

            // Quick fade in so it does not click, then a swell into the detonation.
            float envelope = Mathf.Clamp01(progress * 12f) * Mathf.Lerp(0.25f, 1f, progress);
            samples[i] = Mathf.Clamp(tone * envelope * 0.7f, -1f, 1f);
        }

        chargeClip = CreateClip("Big Enemy Death Charge", samples);
        chargeClipDuration = duration;
        return chargeClip;
    }

    /// <summary>A short bright zap, one per streak burst, pitched up as the charge runs on.</summary>
    private static AudioClip GetCrackleClip()
    {
        if (crackleClip != null)
        {
            return crackleClip;
        }

        int sampleCount = Mathf.RoundToInt(0.12f * SampleRate);
        float[] samples = new float[sampleCount];
        uint noiseState = 0x9E3779B9u;
        float phase = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float progress = (float)i / sampleCount;
            float decay = Mathf.Exp(-progress * 18f);
            float frequency = Mathf.Lerp(1200f, 400f, progress);
            phase += 2f * Mathf.PI * frequency / SampleRate;

            float value = NextNoise(ref noiseState) * 0.6f + Mathf.Sin(phase) * 0.4f;
            samples[i] = Mathf.Clamp(value * decay * 0.8f, -1f, 1f);
        }

        crackleClip = CreateClip("Big Enemy Death Crackle", samples);
        return crackleClip;
    }

    /// <summary>
    /// The blast: a sine dropping from a thump to a rumble under noise pushed through a
    /// one-pole low pass whose cutoff closes as it decays, which is what makes the tail
    /// darken the way a real explosion does instead of hissing out.
    /// </summary>
    private static AudioClip GetExplosionClip()
    {
        if (explosionClip != null)
        {
            return explosionClip;
        }

        int sampleCount = Mathf.RoundToInt(0.9f * SampleRate);
        float[] samples = new float[sampleCount];
        uint noiseState = 0x85EBCA6Bu;
        float phase = 0f;
        float filtered = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float progress = (float)i / sampleCount;

            float frequency = Mathf.Lerp(120f, 35f, Mathf.Sqrt(progress));
            phase += 2f * Mathf.PI * frequency / SampleRate;
            float thump = Mathf.Sin(phase);

            float cutoff = Mathf.Lerp(0.45f, 0.02f, progress);
            filtered += (NextNoise(ref noiseState) - filtered) * cutoff;

            // Near-instant attack, long exponential tail.
            float envelope = Mathf.Clamp01(progress * 400f) * Mathf.Exp(-progress * 4.5f);
            float value = thump * 0.6f + filtered * 1.6f;
            samples[i] = Mathf.Clamp(value * envelope, -1f, 1f);
        }

        explosionClip = CreateClip("Big Enemy Death Explosion", samples);
        return explosionClip;
    }

    private static AudioClip CreateClip(string clipName, float[] samples)
    {
        AudioClip clip = AudioClip.Create(clipName, samples.Length, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>
    /// White noise from a fixed seed, so a clip is byte-identical every run rather than
    /// re-rolled per session.
    /// </summary>
    private static float NextNoise(ref uint state)
    {
        state = state * 1664525u + 1013904223u;
        return (state >> 8) * (2f / 16777216f) - 1f;
    }
}
