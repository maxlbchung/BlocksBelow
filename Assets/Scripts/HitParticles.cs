using UnityEngine;

/// <summary>
/// Shared burst systems for hit sparks and death explosions. Mirrors the
/// AudioController voice pool: effects emit into one shared system per kind
/// instead of instantiating a particle object per event.
/// </summary>
public static class HitParticles
{
    private static ParticleSystem hitSystem;
    private static ParticleSystem deathSystem;
    private static Material particleMaterial;

    public static void Emit(Vector2 position, int count)
    {
        // A scene change destroys the systems; the next hit just rebuilds them.
        if (hitSystem == null)
        {
            hitSystem = CreateSystem(
                "Hit Particles",
                minLifetime: 0.15f,
                maxLifetime: 0.35f,
                minSpeed: 1.5f,
                maxSpeed: 4f,
                minSize: 0.04f,
                maxSize: 0.1f,
                gravity: 0.6f,
                spawnRadius: 0.08f,
                randomRotation: false);
        }

        EmitInto(hitSystem, position, count);
    }

    /// <summary>
    /// A bigger, slower debris burst for something dying, distinct from the
    /// small sparks a survivable hit shows.
    /// </summary>
    public static void EmitDeathBurst(Vector2 position, int count)
    {
        if (deathSystem == null)
        {
            deathSystem = CreateSystem(
                "Death Particles",
                minLifetime: 0.35f,
                maxLifetime: 0.9f,
                minSpeed: 3f,
                maxSpeed: 8f,
                minSize: 0.08f,
                maxSize: 0.22f,
                gravity: 1.2f,
                spawnRadius: 0.2f,
                randomRotation: true);
        }

        EmitInto(deathSystem, position, count);
    }

    private static void EmitInto(ParticleSystem system, Vector2 position, int count)
    {
        if (system == null || count <= 0)
        {
            return;
        }

        ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams
        {
            position = position,
            applyShapeToPosition = true
        };
        system.Emit(emitParams, count);
    }

    private static ParticleSystem CreateSystem(
        string name,
        float minLifetime,
        float maxLifetime,
        float minSpeed,
        float maxSpeed,
        float minSize,
        float maxSize,
        float gravity,
        float spawnRadius,
        bool randomRotation)
    {
        GameObject systemObject = new GameObject(name);
        ParticleSystem system = systemObject.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = system.main;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(minLifetime, maxLifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
        main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        main.startColor = Color.white;
        main.gravityModifier = gravity;
        if (randomRotation)
        {
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        }

        // Bursts come only from Emit(); nothing trickles out between events.
        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = spawnRadius;

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = system.colorOverLifetime;
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
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 0.5f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = gradient;

        ParticleSystemRenderer particleRenderer =
            systemObject.GetComponent<ParticleSystemRenderer>();
        particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
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
                name = "Shared Hit Particle Material",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        return particleMaterial;
    }
}
