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
    [SerializeField, Min(0.01f)] private float boltSize = 0.4125f;

    [Header("Impact Sparks")]
    [Tooltip("Sparks thrown off where the bullet hits the player. Zero turns the burst off.")]
    [SerializeField, Min(0)] private int sparksPerHit = 40;
    [SerializeField] private Color sparkColor = new Color(1f, 0.92f, 0.15f, 1f);

    // One bolt material, one blank sprite and one spark system serve every bullet: they
    // all come off the same prefab, so none of them ever needs its own.
    private static Material boltMaterial;
    private static Sprite boltSprite;
    private static ParticleSystem sparks;
    private static Material sparkMaterial;

    private static readonly int BoltIntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int BoltGlowStrengthId = Shader.PropertyToID("_GlowStrength");

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
        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }
    }

    public void OnPoolRelease()
    {
        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }
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

        // Driven past the shader's own defaults: a hotter core and a wider halo, so the
        // bolt still reads as incoming fire against a bright sky rather than as a mote.
        boltMaterial.SetFloat(BoltIntensityId, 1.6f);
        boltMaterial.SetFloat(BoltGlowStrengthId, 2.2f);

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
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 11f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.09f, 0.24f);
        // White at the hot end of the spread, the bolt's own yellow at the other.
        main.startColor = new ParticleSystem.MinMaxGradient(Color.white, color);
        main.gravityModifier = 0.7f;

        // Bursts come only from Emit(); nothing trickles out between hits.
        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.18f;

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

        // Stretched along their velocity, the particles read as sparks flying off the
        // hit rather than as a puff of dots.
        ParticleSystemRenderer sparkRenderer = sparkObject.GetComponent<ParticleSystemRenderer>();
        sparkRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        sparkRenderer.lengthScale = 4.5f;
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
        }

        return sparkMaterial;
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
