using UnityEngine;

/// <summary>
/// Shared muzzle-flash bursts for towers that fire. Same philosophy as
/// HitParticles and DustParticles: every shot emits into one shared system with
/// an explicit velocity, so a tower firing several times a second never
/// instantiates a particle object. Each shot supplies its own tint, so one pair
/// of systems serves towers of different colours.
/// </summary>
public static class MuzzleParticles
{
    private static ParticleSystem sparkSystem;
    private static ParticleSystem smokeSystem;
    private static Material sparkMaterial;
    private static Material smokeMaterial;

    /// <summary>
    /// One shot's flash: a cone of round sparks thrown along the barrel plus a
    /// slower cloud of smoke that lingers at the muzzle and fades out.
    /// </summary>
    /// <param name="position">Muzzle position, already offset out of the tower.</param>
    /// <param name="direction">Travel direction of the shot; need not be normalized.</param>
    /// <param name="sparkCount">Sparks in the cone. Zero skips the flash entirely.</param>
    /// <param name="flashColor">Tint of the sparks; the smoke takes a paler version of it.</param>
    /// <param name="coneAngle">Full width of the spark cone in degrees.</param>
    public static void EmitShot(
        Vector2 position,
        Vector2 direction,
        int sparkCount,
        Color flashColor,
        float coneAngle = 26f)
    {
        if (sparkCount <= 0 || !EnsureSystems())
        {
            return;
        }

        Vector2 forward = direction.sqrMagnitude > 0.000001f ? direction.normalized : Vector2.left;
        float halfCone = Mathf.Abs(coneAngle) * 0.5f;
        Color smokeColor = SmokeTint(flashColor);

        for (int i = 0; i < sparkCount; i++)
        {
            Vector2 sparkDirection = Rotate(forward, Random.Range(-halfCone, halfCone));
            // Started out past the smoke rather than inside it, so the dots are
            // seen against the background instead of through a puff.
            Emit(
                sparkSystem,
                position + sparkDirection * Random.Range(0.05f, 0.3f),
                sparkDirection * Random.Range(2.5f, 6f),
                flashColor);
        }

        // Enough puffs to overlap into one cloud; one per spark would bury the tower.
        int puffCount = Mathf.Clamp(sparkCount / 2, 3, 8);
        for (int i = 0; i < puffCount; i++)
        {
            Vector2 puffDirection = Rotate(forward, Random.Range(-halfCone, halfCone));
            Emit(
                smokeSystem,
                position + Random.insideUnitCircle * 0.14f,
                puffDirection * Random.Range(0.6f, 1.8f),
                smokeColor);
        }
    }

    /// <summary>
    /// Smoke keeps the shot's hue but washed out and half transparent, so the
    /// cloud reads as the same colour without competing with the sparks.
    /// </summary>
    private static Color SmokeTint(Color flashColor)
    {
        Color pale = Color.Lerp(flashColor, Color.white, 0.4f);
        // Thin enough that overlapping puffs stack into a cloud instead of one
        // flat slab of colour.
        pale.a = flashColor.a * 0.32f;
        return pale;
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

    private static void Emit(ParticleSystem system, Vector2 position, Vector2 velocity, Color color)
    {
        ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams
        {
            position = position,
            velocity = velocity,
            // The systems stay white; the tint travels with each particle so two
            // towers of different colours can share one system.
            startColor = color
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
                minLifetime: 0.22f,
                maxLifetime: 0.45f,
                minSize: 0.13f,
                maxSize: 0.28f,
                gravity: 0.25f,
                material: GetSparkMaterial(),
                // Above the smoke, or the cloud swallows the dots entirely.
                sortingOrder: 12,
                alphaRamp: SparkAlphaRamp(),
                randomRotation: false);

            // Sparks taper as they burn out rather than blinking off at full
            // size, but stay big enough to read as circles the whole way.
            ParticleSystem.SizeOverLifetimeModule sparkSize = sparkSystem.sizeOverLifetime;
            sparkSize.enabled = true;
            sparkSize.size = new ParticleSystem.MinMaxCurve(
                1f,
                AnimationCurve.EaseInOut(0f, 1f, 1f, 0.4f));

            // Air drag: the cone flares fast then settles, which looks livelier
            // than sparks coasting at their launch speed.
            ParticleSystem.LimitVelocityOverLifetimeModule sparkDrag =
                sparkSystem.limitVelocityOverLifetime;
            sparkDrag.enabled = true;
            sparkDrag.dampen = 0.25f;
            sparkDrag.limit = new ParticleSystem.MinMaxCurve(2.5f);
        }

        if (smokeSystem == null)
        {
            smokeSystem = CreateSystem(
                "Muzzle Smoke Particles",
                minLifetime: 0.7f,
                maxLifetime: 1.5f,
                minSize: 0.3f,
                maxSize: 0.6f,
                gravity: -0.1f,
                material: GetSmokeMaterial(),
                // Still in front of the tower, but under the sparks.
                sortingOrder: 9,
                alphaRamp: SmokeAlphaRamp(),
                randomRotation: true);

            // Puffs billow out as they fade, the same trick the movement dust uses.
            ParticleSystem.SizeOverLifetimeModule smokeSize = smokeSystem.sizeOverLifetime;
            smokeSize.enabled = true;
            smokeSize.size = new ParticleSystem.MinMaxCurve(
                1f,
                AnimationCurve.EaseInOut(0f, 0.55f, 1f, 2f));

            // A lazy turn keeps overlapping puffs from looking like stamped
            // copies of one circle.
            ParticleSystem.RotationOverLifetimeModule smokeSpin =
                smokeSystem.rotationOverLifetime;
            smokeSpin.enabled = true;
            smokeSpin.separateAxes = false;
            smokeSpin.z = new ParticleSystem.MinMaxCurve(-0.9f, 0.9f);

            // Heavy drag so the cloud stalls just past the muzzle and hangs there.
            ParticleSystem.LimitVelocityOverLifetimeModule smokeDrag =
                smokeSystem.limitVelocityOverLifetime;
            smokeDrag.enabled = true;
            smokeDrag.dampen = 0.8f;
            smokeDrag.limit = new ParticleSystem.MinMaxCurve(0.5f);
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
        Material material,
        int sortingOrder,
        Gradient alphaRamp,
        bool randomRotation)
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
        // White start colour: each emit tints its own particle instead.
        main.startColor = Color.white;
        main.gravityModifier = gravity;
        if (randomRotation)
        {
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        }

        // Bursts come only from EmitShot(); nothing trickles out between shots.
        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = false;

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = system.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = alphaRamp;

        ParticleSystemRenderer particleRenderer =
            systemObject.GetComponent<ParticleSystemRenderer>();
        particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        // In front of the tower that fired, matching the hit sparks.
        particleRenderer.sortingLayerName = "Foreground";
        particleRenderer.sortingOrder = sortingOrder;
        particleRenderer.sharedMaterial = material;

        return system;
    }

    /// <summary>
    /// Sparks hold full brightness through most of their life and drop off at
    /// the end, so they stay legible against the cloud behind them.
    /// </summary>
    private static Gradient SparkAlphaRamp() =>
        BuildAlphaRamp(new[]
        {
            new GradientAlphaKey(1f, 0f),
            new GradientAlphaKey(1f, 0.55f),
            new GradientAlphaKey(0f, 1f)
        });

    /// <summary>
    /// Smoke eases in from nothing and trails off slowly, which is what keeps a
    /// puff from popping into existence at full strength.
    /// </summary>
    private static Gradient SmokeAlphaRamp() =>
        BuildAlphaRamp(new[]
        {
            new GradientAlphaKey(0f, 0f),
            new GradientAlphaKey(1f, 0.15f),
            new GradientAlphaKey(0.6f, 0.55f),
            new GradientAlphaKey(0f, 1f)
        });

    /// <summary>
    /// Colour keys stay white so the per-particle tint from EmitShot survives;
    /// only the alpha ramp varies between the two systems.
    /// </summary>
    private static Gradient BuildAlphaRamp(GradientAlphaKey[] alphaKeys)
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            alphaKeys);
        return gradient;
    }

    private static Material GetSparkMaterial()
    {
        if (sparkMaterial == null)
        {
            // Nearly solid disc with a thin soft rim: a dot, not a pixel block.
            sparkMaterial = CreateParticleMaterial(
                "Shared Muzzle Spark Material",
                CreateRadialTexture(
                    "Muzzle Spark Dot",
                    64,
                    coreRadius: 0.6f,
                    falloffPower: 0.7f,
                    smoothEdge: false));
        }

        return sparkMaterial;
    }

    private static Material GetSmokeMaterial()
    {
        if (smokeMaterial == null)
        {
            // No solid core and a wide, smoothstepped falloff, so puffs blur
            // into one another as a cloud with no visible edge anywhere.
            smokeMaterial = CreateParticleMaterial(
                "Shared Muzzle Smoke Material",
                CreateRadialTexture(
                    "Muzzle Smoke Puff",
                    128,
                    coreRadius: 0f,
                    falloffPower: 1.6f,
                    smoothEdge: true));
        }

        return smokeMaterial;
    }

    private static Material CreateParticleMaterial(string name, Texture2D texture)
    {
        Shader spriteShader = Shader.Find("Sprites/Default");
        if (spriteShader == null)
        {
            return null;
        }

        return new Material(spriteShader)
        {
            name = name,
            mainTexture = texture,
            hideFlags = HideFlags.HideAndDontSave
        };
    }

    /// <summary>
    /// Builds a soft round particle sprite in code, so nothing has to be
    /// imported to keep the flash from rendering as an untextured square.
    /// </summary>
    /// <param name="coreRadius">Fraction of the radius kept fully opaque before the fade starts.</param>
    /// <param name="falloffPower">Above 1 fades early and softly; below 1 holds the edge crisp.</param>
    /// <param name="smoothEdge">Eases both ends of the fade, for smoke with no discernible rim.</param>
    private static Texture2D CreateRadialTexture(
        string name,
        int size,
        float coreRadius,
        float falloffPower,
        bool smoothEdge)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = name,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        float center = (size - 1) * 0.5f;
        float radius = size * 0.5f;
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = new Vector2(x - center, y - center).magnitude / radius;
                float edge = 1f - Mathf.InverseLerp(coreRadius, 1f, distance);
                if (smoothEdge)
                {
                    edge = Mathf.SmoothStep(0f, 1f, edge);
                }

                float alpha = Mathf.Pow(Mathf.Clamp01(edge), falloffPower);
                // White everywhere: the particle tint does the colouring.
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }
}
