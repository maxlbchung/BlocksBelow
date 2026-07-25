using UnityEngine;

/// <summary>
/// Shared puff system for movement dust: running, jumping, and landing. Same
/// philosophy as HitParticles - every puff is emitted into one shared system
/// with an explicit velocity, so nothing is instantiated per event.
/// </summary>
public static class DustParticles
{
    private static readonly Color DustColor = new Color(0.85f, 0.81f, 0.74f, 0.9f);

    private static ParticleSystem system;
    private static Material dustMaterial;

    /// <summary>
    /// One puff kicked up behind the moving foot. The run cadence comes from
    /// the caller's timer, not from an emission rate.
    /// </summary>
    public static void EmitRun(Vector2 feetPosition, float moveDirection)
    {
        EmitPuffs(
            feetPosition,
            new Vector2(-moveDirection * 0.9f, 0.7f),
            spreadSpeed: 0.35f,
            count: 1);
    }

    public static void EmitJump(Vector2 feetPosition, int count)
    {
        EmitPuffs(feetPosition, new Vector2(0f, 0.6f), spreadSpeed: 1.1f, count: count);
    }

    /// <summary>
    /// Landing dust squashes outward: alternating left/right puffs that stay
    /// mostly flat along the ground.
    /// </summary>
    public static void EmitLand(Vector2 feetPosition, int count)
    {
        if (count <= 0 || !EnsureSystem())
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            float side = i % 2 == 0 ? 1f : -1f;
            Vector2 velocity = new Vector2(
                side * Random.Range(0.7f, 2.4f),
                Random.Range(0.2f, 1f));
            EmitOne(feetPosition + Random.insideUnitCircle * 0.05f, velocity);
        }
    }

    private static void EmitPuffs(
        Vector2 position,
        Vector2 baseVelocity,
        float spreadSpeed,
        int count)
    {
        if (count <= 0 || !EnsureSystem())
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            Vector2 velocity = baseVelocity + Random.insideUnitCircle * spreadSpeed;
            EmitOne(position + Random.insideUnitCircle * 0.05f, velocity);
        }
    }

    private static void EmitOne(Vector2 position, Vector2 velocity)
    {
        ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams
        {
            position = position,
            velocity = velocity
        };
        system.Emit(emitParams, 1);
    }

    private static bool EnsureSystem()
    {
        // A scene change destroys the system; the next puff rebuilds it.
        if (system != null)
        {
            return true;
        }

        GameObject systemObject = new GameObject("Dust Particles");
        system = systemObject.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = system.main;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
        // Every emit supplies its own velocity, so the system adds none.
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.13f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = DustColor;
        main.gravityModifier = 0f;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = false;

        // Puffs swell slightly as they fade, which reads as dust rather than
        // debris.
        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = system.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
            1f,
            AnimationCurve.Linear(0f, 0.7f, 1f, 1.4f));

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = system.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(DustColor, 0f),
                new GradientColorKey(DustColor, 1f)
            },
            new[]
            {
                new GradientAlphaKey(DustColor.a, 0f),
                new GradientAlphaKey(DustColor.a * 0.7f, 0.4f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = gradient;

        ParticleSystemRenderer particleRenderer =
            systemObject.GetComponent<ParticleSystemRenderer>();
        particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        // Behind the player sprite but above everything on lower layers, so
        // puffs peek out around the feet instead of covering them.
        particleRenderer.sortingLayerName = "Player";
        particleRenderer.sortingOrder = -5;
        particleRenderer.sharedMaterial = GetDustMaterial();

        return true;
    }

    private static Material GetDustMaterial()
    {
        if (dustMaterial != null)
        {
            return dustMaterial;
        }

        Shader spriteShader = Shader.Find("Sprites/Default");
        if (spriteShader != null)
        {
            dustMaterial = new Material(spriteShader)
            {
                name = "Shared Dust Particle Material",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        return dustMaterial;
    }
}
