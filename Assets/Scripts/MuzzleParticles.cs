using UnityEngine;

/// <summary>
/// Shared muzzle-flash bursts for towers that fire. Same philosophy as
/// HitParticles and DustParticles: every shot emits into one shared system with
/// an explicit velocity, so a tower firing several times a second never
/// instantiates a particle object.
/// </summary>
public static class MuzzleParticles
{
    private static readonly Color SparkColor = new Color(1f, 0.85f, 0.42f, 1f);
    private static readonly Color SmokeColor = new Color(0.72f, 0.71f, 0.68f, 0.5f);

    private static ParticleSystem sparkSystem;
    private static ParticleSystem smokeSystem;
    private static Material particleMaterial;

    /// <summary>
    /// One shot's flash: a cone of sparks thrown along the barrel plus a slower
    /// puff of smoke that lingers at the muzzle.
    /// </summary>
    /// <param name="position">Muzzle position, already offset out of the tower.</param>
    /// <param name="direction">Travel direction of the shot; need not be normalized.</param>
    /// <param name="sparkCount">Sparks in the cone. Zero skips the flash entirely.</param>
    /// <param name="coneAngle">Full width of the spark cone in degrees.</param>
    public static void EmitShot(Vector2 position, Vector2 direction, int sparkCount, float coneAngle = 26f)
    {
        if (sparkCount <= 0 || !EnsureSystems())
        {
            return;
        }

        Vector2 forward = direction.sqrMagnitude > 0.000001f ? direction.normalized : Vector2.left;
        float halfCone = Mathf.Abs(coneAngle) * 0.5f;

        for (int i = 0; i < sparkCount; i++)
        {
            Vector2 sparkDirection = Rotate(forward, Random.Range(-halfCone, halfCone));
            Emit(
                sparkSystem,
                position + sparkDirection * Random.Range(0f, 0.12f),
                sparkDirection * Random.Range(4f, 10f));
        }

        // A couple of puffs read as smoke; one per spark would bury the tower.
        int puffCount = Mathf.Clamp(sparkCount / 4, 1, 3);
        for (int i = 0; i < puffCount; i++)
        {
            Vector2 puffDirection = Rotate(forward, Random.Range(-halfCone, halfCone));
            Emit(
                smokeSystem,
                position + Random.insideUnitCircle * 0.06f,
                puffDirection * Random.Range(0.5f, 1.6f));
        }
    }

    /// <summary>Turns a direction by an angle in degrees, in the 2D plane.</summary>
    private static Vector2 Rotate(Vector2 direction, float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);
        return new Vector2(
            direction.x * cos - direction.y * sin,
            direction.x * sin + direction.y * cos);
    }

    private static void Emit(ParticleSystem system, Vector2 position, Vector2 velocity)
    {
        ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams
        {
            position = position,
            velocity = velocity
        };
        system.Emit(emitParams, 1);
    }

    private static bool EnsureSystems()
    {
        // A scene change destroys the systems; the next shot just rebuilds them.
        if (sparkSystem != null && smokeSystem != null)
        {
            return true;
        }

        if (sparkSystem == null)
        {
            sparkSystem = CreateSystem(
                "Muzzle Spark Particles",
                minLifetime: 0.05f,
                maxLifetime: 0.16f,
                minSize: 0.05f,
                maxSize: 0.13f,
                gravity: 0.2f,
                color: SparkColor);

            // Fast sparks read as tracer streaks rather than dots when the
            // renderer stretches them along their own velocity.
            ParticleSystemRenderer sparkRenderer =
                sparkSystem.GetComponent<ParticleSystemRenderer>();
            sparkRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            sparkRenderer.velocityScale = 0.05f;
            sparkRenderer.lengthScale = 1.5f;
        }

        if (smokeSystem == null)
        {
            smokeSystem = CreateSystem(
                "Muzzle Smoke Particles",
                minLifetime: 0.18f,
                maxLifetime: 0.4f,
                minSize: 0.14f,
                maxSize: 0.3f,
                gravity: -0.15f,
                color: SmokeColor);

            // Puffs swell as they fade, the same trick the movement dust uses.
            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime =
                smokeSystem.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
                1f,
                AnimationCurve.Linear(0f, 0.6f, 1f, 1.5f));
        }

        return true;
    }

    private static ParticleSystem CreateSystem(
        string name,
        float minLifetime,
        float maxLifetime,
        float minSize,
        float maxSize,
        float gravity,
        Color color)
    {
        GameObject systemObject = new GameObject(name);
        ParticleSystem system = systemObject.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = system.main;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(minLifetime, maxLifetime);
        // Every emit supplies its own velocity, so the system adds none.
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = color;
        main.gravityModifier = gravity;

        // Bursts come only from EmitShot(); nothing trickles out between shots.
        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = false;

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = system.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(color, 0f),
                new GradientColorKey(color, 1f)
            },
            new[]
            {
                new GradientAlphaKey(color.a, 0f),
                new GradientAlphaKey(color.a * 0.7f, 0.5f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = gradient;

        ParticleSystemRenderer particleRenderer =
            systemObject.GetComponent<ParticleSystemRenderer>();
        particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        // In front of the tower that fired, matching the hit sparks.
        particleRenderer.sortingLayerName = "Foreground";
        particleRenderer.sortingOrder = 10;
        particleRenderer.sharedMaterial = GetParticleMaterial();

        return system;
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
                name = "Shared Muzzle Particle Material",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        return particleMaterial;
    }
}
