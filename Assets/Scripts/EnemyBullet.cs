using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(BoxCollider2D))]
public class EnemyBullet : MonoBehaviour, IPoolable
{
    private Rigidbody2D body;
    private PooledObject poolHandle;
    public int damage = 1;

    [Header("Bolt")]
    [Tooltip("World size of the electric bolt drawn in place of the bullet sprite. The "
        + "collider is left alone - this is the look only. Only true while the prefab "
        + "sits at scale 1: the bolt is sized by its sprite's pixels-per-unit, so any "
        + "transform scale multiplies on top of this and the number stops meaning world "
        + "units.")]
    [SerializeField, Min(0.01f)] private float boltSize = 0.78f;

    [Header("Impact Sparks")]
    [Tooltip("Sparks thrown off where the bullet hits the player. Kept low on purpose: a "
        + "few countable streaks read as a burst, where a crowd of them blooms together "
        + "into a single glow. Zero turns the burst off.")]
    [SerializeField, Min(0)] private int sparksPerHit = 16;
    [SerializeField] private Color sparkColor = new Color(1f, 0.92f, 0.15f, 1f);

    [Header("Sparkle Trail")]
    [Tooltip("Sparkles shed along the flight path. Zero turns the trail off.")]
    [SerializeField, Min(0)] private float sparklesPerSecond = 55f;
    [SerializeField] private Color sparkleColor = new Color(1f, 0.86f, 0.3f, 1f);

    // One bolt material, one blank sprite and one system per effect serve every bullet:
    // they all come off the same prefab, so none of them ever needs its own.
    private static Material boltMaterial;
    private static Sprite boltSprite;
    private static ParticleSystem sparks;
    private static Material sparkMaterial;
    private static ParticleSystem sparkles;
    private static Material sparkleMaterial;
    private static Texture2D sparkleTexture;

    // Fractional sparkles carried between frames, so a rate that does not divide evenly
    // into the frame time still comes out at the rate asked for instead of rounding away.
    private float sparkleBacklog;

    private static readonly int BoltIntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int BoltGlowStrengthId = Shader.PropertyToID("_GlowStrength");
    private static readonly int SpriteColorId = Shader.PropertyToID("_Color");

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        body.gravityScale = 0f;
        body.constraints = RigidbodyConstraints2D.FreezeRotation;
        body.collisionDetectionMode = CollisionDetectionMode2D.Discrete;
        ApplyBoltLook();
    }

    public static EnemyBullet Spawn(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        Vector2 velocity,
        float lifetime)
    {
        if (!CombatObjectPool.TryAcquire(
                prefab,
                position,
                rotation,
                lifetime,
                out PooledObject pooledObject)
            || pooledObject.EnemyBullet == null)
        {
            return null;
        }

        EnemyBullet bullet = pooledObject.EnemyBullet;
        // Activate first so the Rigidbody2D exists and is simulated, then set the velocity.
        // A velocity assigned to an inactive (or never-awoken pooled) body does not persist.
        CombatObjectPool.Activate(pooledObject);
        bullet.SetVelocity(velocity);
        return bullet;
    }

    public void SetVelocity(Vector2 velocity)
    {
        if (body == null)
        {
            body = GetComponent<Rigidbody2D>();
        }

        if (body != null)
        {
            body.linearVelocity = velocity;
        }
    }

    public void OnPoolAcquire()
    {
        sparkleBacklog = 0f;
        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }
    }

    public void OnPoolRelease()
    {
        sparkleBacklog = 0f;
        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }
    }

    private void Update()
    {
        EmitSparkles(Time.deltaTime);
    }

    internal void AssignPoolHandle(PooledObject handle)
    {
        poolHandle = handle;
    }

    /// <summary>
    /// Bursts into sparks and takes the bullet off the field. Called by whatever the
    /// bullet lands on, since the collision is detected on that side rather than here.
    /// </summary>
    public void Explode()
    {
        EmitSparks(transform.position);
        Release();
    }

    /// <summary>
    /// Draws the bullet as the procedural bolt instead of its sprite. The shader works
    /// off 0-1 UVs, which a sprite cut out of a sheet does not carry, so the renderer is
    /// handed a blank full-rect sprite for the material to cover.
    /// </summary>
    private void ApplyBoltLook()
    {
        SpriteRenderer spriteRenderer = GetComponent<SpriteRenderer>();
        Material bolt = GetBoltMaterial();
        if (spriteRenderer == null || bolt == null)
        {
            return;
        }

        spriteRenderer.sprite = GetBoltSprite(boltSize);
        spriteRenderer.sharedMaterial = bolt;
        spriteRenderer.color = Color.white;
    }

    private static Material GetBoltMaterial()
    {
        if (boltMaterial != null)
        {
            return boltMaterial;
        }

        Shader boltShader = Shader.Find("TowerDefense/EnemyBolt");
        if (boltShader == null)
        {
            Debug.LogWarning("The TowerDefense/EnemyBolt shader could not be found.");
            return null;
        }

        boltMaterial = new Material(boltShader)
        {
            name = "Shared Enemy Bolt Material",
            hideFlags = HideFlags.HideAndDontSave
        };

        // Driven past the shader's own defaults, and past 1: the scene renders HDR and
        // the global volume blooms above 0.9, so overbright is what buys the glow. The
        // bolt reads as incoming fire against a bright sky rather than as a mote.
        boltMaterial.SetFloat(BoltIntensityId, 2.8f);
        boltMaterial.SetFloat(BoltGlowStrengthId, 3.2f);

        return boltMaterial;
    }

    /// <summary>
    /// A blank quad for the bolt material to draw across, sized in world units by its
    /// pixels-per-unit rather than by scaling the bullet, which would drag the collider
    /// along with it.
    /// </summary>
    private static Sprite GetBoltSprite(float worldSize)
    {
        if (boltSprite != null)
        {
            return boltSprite;
        }

        const int size = 2;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Enemy Bolt Texture",
            hideFlags = HideFlags.HideAndDontSave
        };

        Color32[] pixels = new Color32[size * size];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Color32(255, 255, 255, 255);
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        boltSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            size / Mathf.Max(0.01f, worldSize));
        boltSprite.name = "Enemy Bolt";
        boltSprite.hideFlags = HideFlags.HideAndDontSave;
        return boltSprite;
    }

    private void EmitSparks(Vector2 position)
    {
        if (sparksPerHit <= 0)
        {
            return;
        }

        // A scene change destroys the system; the next hit just rebuilds it. It lives at
        // the scene root rather than under the bullet, so the burst outlives the bullet
        // being released back into the pool a line later.
        if (sparks == null)
        {
            sparks = CreateSparkSystem(sparkColor);
        }

        ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams
        {
            position = position,
            applyShapeToPosition = true
        };
        sparks.Emit(emitParams, sparksPerHit);
    }

    private static ParticleSystem CreateSparkSystem(Color color)
    {
        GameObject sparkObject = new GameObject("Enemy Bolt Sparks");
        ParticleSystem system = sparkObject.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = system.main;
        // Looping keeps the system running with nothing to show, which is what lets a
        // later Emit() simulate. A one-shot system stops itself and swallows the burst.
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        // Short-lived and quick: the speed is what throws the sparks outwards, the
        // lifetime is what keeps them from travelling far enough to read as an explosion.
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.1f, 0.24f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(4.5f, 10f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
        // White at the hot end of the spread, the bolt's own yellow at the other.
        main.startColor = new ParticleSystem.MinMaxGradient(Color.white, color);
        main.gravityModifier = 0.45f;

        // Bursts come only from Emit(); nothing trickles out between hits.
        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = false;

        // A circle barely wider than a point: the shape is here for the radial directions
        // it hands every spark, not to spread the start positions. Filling a disc is what
        // made the burst read as a blob with sparks in it rather than as sparks leaving a
        // single impact point.
        ParticleSystem.ShapeModule shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.03f;
        shape.radiusThickness = 1f;

        // Drag, so each streak shoots out hard and then stalls where it lands instead of
        // sailing on across the screen.
        ParticleSystem.LimitVelocityOverLifetimeModule limitVelocity =
            system.limitVelocityOverLifetime;
        limitVelocity.enabled = true;
        limitVelocity.dampen = 0.4f;
        limitVelocity.limit = new ParticleSystem.MinMaxCurve(2.5f);

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = system.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(color, 0.3f),
                new GradientColorKey(color, 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 0.45f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = gradient;

        // Thinning as they slow, so a streak tapers off at the end of its flight rather
        // than switching off at full width.
        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = system.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
            1f,
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.15f)));

        // Stretched along their velocity, the particles read as sparks flying off the
        // hit rather than as a puff of dots. Kept short: a long streak at this spark
        // count overlaps its neighbours and the burst fuses back into one glow.
        ParticleSystemRenderer sparkRenderer = sparkObject.GetComponent<ParticleSystemRenderer>();
        sparkRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        sparkRenderer.lengthScale = 2.4f;
        sparkRenderer.sortingLayerName = "Foreground";
        sparkRenderer.sortingOrder = 10;
        sparkRenderer.sharedMaterial = GetSparkMaterial();

        return system;
    }

    private static Material GetSparkMaterial()
    {
        if (sparkMaterial != null)
        {
            return sparkMaterial;
        }

        Shader spriteShader = Shader.Find("Sprites/Default");
        if (spriteShader != null)
        {
            sparkMaterial = new Material(spriteShader)
            {
                name = "Shared Enemy Bolt Spark Material",
                hideFlags = HideFlags.HideAndDontSave
            };

            // Tinted past white for the same reason the bolt is: the particle system's
            // own colours are packed to bytes and can never exceed 1, so the overbright
            // that the bloom threshold wants has to come from the material. Held just
            // over the threshold rather than well past it - the further past, the wider
            // the halo each spark wears, and the halos are what used to close the gaps
            // between the streaks into a solid glow.
            sparkMaterial.SetColor(SpriteColorId, new Color(1.5f, 1.32f, 0.85f, 1f));
        }

        return sparkMaterial;
    }

    /// <summary>
    /// Sheds sparkles along the path flown since the last frame. They are spaced across
    /// the segment rather than dropped at the current position, so the trail stays a
    /// continuous ribbon instead of clumping once per frame at high speed.
    /// </summary>
    private void EmitSparkles(float deltaTime)
    {
        if (sparklesPerSecond <= 0f)
        {
            return;
        }

        sparkleBacklog += sparklesPerSecond * deltaTime;
        int count = Mathf.FloorToInt(sparkleBacklog);
        if (count <= 0)
        {
            return;
        }

        sparkleBacklog -= count;

        if (sparkles == null)
        {
            sparkles = CreateSparkleSystem(sparkleColor);
        }

        Vector2 velocity = body != null ? body.linearVelocity : Vector2.zero;
        Vector3 travel = new Vector3(velocity.x, velocity.y, 0f) * deltaTime;
        // Drifting backwards off the bolt, so the trail hangs behind it rather than
        // travelling along with it.
        Vector2 drift = velocity * -0.14f;
        float jitter = boltSize * 0.28f;

        for (int i = 0; i < count; i++)
        {
            float back = (i + 1f) / count;
            Vector2 offset = Random.insideUnitCircle * jitter;

            ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams
            {
                position = transform.position - travel * back
                    + new Vector3(offset.x, offset.y, 0f),
                velocity = drift + Random.insideUnitCircle * 1.1f,
                applyShapeToPosition = false
            };
            sparkles.Emit(emitParams, 1);
        }
    }

    private static ParticleSystem CreateSparkleSystem(Color color)
    {
        GameObject sparkleObject = new GameObject("Enemy Bolt Sparkles");
        ParticleSystem system = sparkleObject.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = system.main;
        // Looping for the same reason the impact sparks are: a stopped system swallows
        // the Emit() calls that are the only thing feeding it.
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.55f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.17f);
        main.startColor = new ParticleSystem.MinMaxGradient(Color.white, color);
        main.gravityModifier = 0.18f;
        // A ceiling rather than a target: a heavy wave cannot let the trail eat the
        // frame, and dropping the oldest sparkles is invisible at these lifetimes.
        main.maxParticles = 1200;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = false;

        // Positions come from EmitParams, so the shape would only fight them.
        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = false;

        // Air drag, so a sparkle slips off the bolt and then hangs where it was left.
        ParticleSystem.LimitVelocityOverLifetimeModule limitVelocity =
            system.limitVelocityOverLifetime;
        limitVelocity.enabled = true;
        limitVelocity.dampen = 0.35f;
        limitVelocity.limit = new ParticleSystem.MinMaxCurve(1.5f);

        // The twinkle: alpha dips and comes back twice on the way out. Random lifetimes
        // put every sparkle at a different point in that cycle, so the trail glitters
        // instead of pulsing as one.
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
                new GradientAlphaKey(0.3f, 0.22f),
                new GradientAlphaKey(1f, 0.42f),
                new GradientAlphaKey(0.25f, 0.66f),
                new GradientAlphaKey(0.9f, 0.84f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = gradient;

        // Shrinking to a point as they fade keeps the tail of the trail from reading as
        // a row of dots that all switch off together.
        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = system.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve(
            new Keyframe(0f, 0.55f),
            new Keyframe(0.25f, 1f),
            new Keyframe(1f, 0f));
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        ParticleSystemRenderer sparkleRenderer =
            sparkleObject.GetComponent<ParticleSystemRenderer>();
        sparkleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        sparkleRenderer.sortingLayerName = "Foreground";
        sparkleRenderer.sortingOrder = 9;
        sparkleRenderer.sharedMaterial = GetSparkleMaterial();

        return system;
    }

    private static Material GetSparkleMaterial()
    {
        if (sparkleMaterial != null)
        {
            return sparkleMaterial;
        }

        Shader spriteShader = Shader.Find("Sprites/Default");
        if (spriteShader == null)
        {
            return null;
        }

        sparkleMaterial = new Material(spriteShader)
        {
            name = "Shared Enemy Bolt Sparkle Material",
            hideFlags = HideFlags.HideAndDontSave,
            mainTexture = GetSparkleTexture()
        };

        // Overbright, so the sparkles cross the bloom threshold and bleed light the way
        // the bolt does. Untextured they would be flat squares, hence the dot above.
        sparkleMaterial.SetColor(SpriteColorId, new Color(2.8f, 2.4f, 1.5f, 1f));
        return sparkleMaterial;
    }

    /// <summary>
    /// A soft round dot with a hot centre, built in code so the effect carries no art
    /// dependency. The falloff is steep enough that the sparkle keeps a defined point
    /// once bloom has spread a halo around it.
    /// </summary>
    private static Texture2D GetSparkleTexture()
    {
        if (sparkleTexture != null)
        {
            return sparkleTexture;
        }

        const int size = 32;
        sparkleTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Enemy Bolt Sparkle Dot",
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        Color32[] pixels = new Color32[size * size];
        const float center = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Mathf.Sqrt(
                    (x - center) * (x - center) + (y - center) * (y - center));
                float falloff = Mathf.Clamp01(1f - distance / center);
                float alpha = falloff * falloff * falloff;
                byte a = (byte)Mathf.RoundToInt(alpha * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
        }

        sparkleTexture.SetPixels32(pixels);
        sparkleTexture.Apply();
        return sparkleTexture;
    }

    public void Release()
    {
        if (poolHandle != null)
        {
            poolHandle.Release();
        }
        else if (!CombatObjectPool.Release(gameObject))
        {
            Destroy(gameObject);
        }
    }
}
