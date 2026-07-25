using UnityEngine;

public class TeslaTower : MonoBehaviour
{
    [Header("Combat")]
    [SerializeField] private float damage = 3f;
    [SerializeField, Min(0.01f)] private float zapInterval = 1f;

    [Header("Targeting")]
    [SerializeField, Min(0.1f)] private float initialTargetRadius = 5f;
    [SerializeField, Min(0.1f)] private float chainRadius = 5f;

    [Header("Lightning")]
    [SerializeField, Min(0.01f)] private float lightningDuration = 0.6f;
    [SerializeField, Min(2)] private int pointsPerBolt = 7;
    [SerializeField, Min(0f)] private float jitterAmount = 0.12f;
    [SerializeField, Min(0.01f), Tooltip("Seconds between re-rolls of the bolt's zigzag shape.")]
    private float reshapeInterval = 0.055f;
    [SerializeField, Min(0.001f)] private float lineWidth = 0.4f;
    [SerializeField] private Color lightningColor = new Color(1f, 0.9f, 0.3f, 1f);
    [SerializeField] private AudioClip zapSfx;

    [Header("Sparks")]
    [SerializeField, Min(0)] private int sparksPerHit = 10;

    [Header("Orb")]
    [SerializeField, Min(0.05f)] private float orbSize = 0.6f;
    [SerializeField, Min(0.01f)] private float orbFlareDuration = 0.35f;

    private static Material sharedLightningMaterial;
    private static Material sharedSparkMaterial;
    private static readonly int OrbIntensityId = Shader.PropertyToID("_Intensity");

    private float nextZapTime;
    private int chainCount;
    private TowerCageStack cageStack;
    private Enemy[] hitEnemies;
    private LineRenderer[] boltLines;
    private Transform[] boltStarts;
    private Transform[] boltEnds;
    private Vector2[] boltStartPositions;
    private Vector2[] boltEndPositions;
    private float[] boltElapsed;
    private bool[] boltActive;
    private float[][] boltJitter;
    private float[] boltNextReshape;
    private ParticleSystem sparks;
    private Material orbMaterial;
    private float orbFlare;

    public int ChainCount => chainCount;

    private void Awake()
    {
        EnsureCapacity(Mathf.Max(1, chainCount + 1));
        CreateSparkSystem();
        CreateOrb();
    }

    private void Start()
    {
        cageStack = GetComponent<TowerCageStack>();
        nextZapTime = Time.time + zapInterval;
    }

    private void Update()
    {
        int powerLevel = cageStack != null ? cageStack.PowerLevel : 0;
        // The first cage powers the zap itself; each cage beyond it adds a chain,
        // so power N hits exactly N enemies. Only damage is tunable.
        chainCount = Mathf.Max(0, powerLevel - 1);

        // Bolts keep fading even outside a wave; only new zaps are gated.
        UpdateBolts(Time.deltaTime);
        UpdateOrb(powerLevel, Time.deltaTime);

        if (powerLevel <= 0 || !WaveSpawner.IsWaveActive || Time.time < nextZapTime)
        {
            return;
        }

        Zap();
        nextZapTime = Time.time + Mathf.Max(0.01f, zapInterval);
    }

    public void Zap()
    {
        EnsureCapacity(Mathf.Max(1, chainCount + 1));
        EnemySimulationManager simulation = EnemySimulationManager.Instance;
        Enemy firstEnemy = simulation.FindClosestEnemy(
            transform.position,
            initialTargetRadius);
        if (firstEnemy == null)
        {
            return;
        }

        int hitCount = 1;
        hitEnemies[0] = firstEnemy;
        orbFlare = 1f;
        if (zapSfx != null)
        {
            AudioController.Play(zapSfx);
        }

        ShowBolt(0, transform, firstEnemy.transform);
        firstEnemy.TryTakeDamage(damage);
        Enemy currentEnemy = firstEnemy;
        for (int i = 0; i < chainCount; i++)
        {
            Enemy nextEnemy = simulation.FindClosestEnemy(
                currentEnemy.Position,
                chainRadius,
                hitEnemies,
                hitCount);
            if (nextEnemy == null)
            {
                break;
            }

            ShowBolt(i + 1, currentEnemy.transform, nextEnemy.transform);
            nextEnemy.TryTakeDamage(damage);
            hitEnemies[hitCount++] = nextEnemy;
            currentEnemy = nextEnemy;
        }

        for (int i = hitCount; i < hitEnemies.Length; i++)
        {
            hitEnemies[i] = null;
        }
    }

    private void EnsureCapacity(int required)
    {
        if (hitEnemies != null && hitEnemies.Length >= required)
        {
            return;
        }

        int oldLength = hitEnemies != null ? hitEnemies.Length : 0;
        int capacity = Mathf.NextPowerOfTwo(Mathf.Max(1, required));
        Enemy[] newHitEnemies = new Enemy[capacity];
        LineRenderer[] newBoltLines = new LineRenderer[capacity];
        Transform[] newBoltStarts = new Transform[capacity];
        Transform[] newBoltEnds = new Transform[capacity];
        Vector2[] newBoltStartPositions = new Vector2[capacity];
        Vector2[] newBoltEndPositions = new Vector2[capacity];
        float[] newBoltElapsed = new float[capacity];
        bool[] newBoltActive = new bool[capacity];
        float[][] newBoltJitter = new float[capacity][];
        float[] newBoltNextReshape = new float[capacity];

        for (int i = 0; i < oldLength; i++)
        {
            newHitEnemies[i] = hitEnemies[i];
            newBoltLines[i] = boltLines[i];
            newBoltStarts[i] = boltStarts[i];
            newBoltEnds[i] = boltEnds[i];
            newBoltStartPositions[i] = boltStartPositions[i];
            newBoltEndPositions[i] = boltEndPositions[i];
            newBoltElapsed[i] = boltElapsed[i];
            newBoltActive[i] = boltActive[i];
            newBoltJitter[i] = boltJitter[i];
            newBoltNextReshape[i] = boltNextReshape[i];
        }

        hitEnemies = newHitEnemies;
        boltLines = newBoltLines;
        boltStarts = newBoltStarts;
        boltEnds = newBoltEnds;
        boltStartPositions = newBoltStartPositions;
        boltEndPositions = newBoltEndPositions;
        boltElapsed = newBoltElapsed;
        boltActive = newBoltActive;
        boltJitter = newBoltJitter;
        boltNextReshape = newBoltNextReshape;

        for (int i = oldLength; i < capacity; i++)
        {
            boltLines[i] = CreateBoltLine(i);
            boltJitter[i] = new float[Mathf.Max(2, pointsPerBolt)];
        }
    }

    private LineRenderer CreateBoltLine(int index)
    {
        GameObject boltObject = new GameObject($"Tesla Lightning {index + 1}");
        boltObject.transform.SetParent(transform, false);
        LineRenderer line = boltObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = Mathf.Max(2, pointsPerBolt);
        line.startWidth = lineWidth;
        line.endWidth = lineWidth * 0.6f;
        line.numCapVertices = 2;
        line.sortingLayerName = "Towers";
        line.sortingOrder = 3;
        line.sharedMaterial = GetSharedLightningMaterial();
        line.enabled = false;
        return line;
    }

    private static Material GetSharedLightningMaterial()
    {
        if (sharedLightningMaterial != null)
        {
            return sharedLightningMaterial;
        }

        Shader lightningShader = Shader.Find("TowerDefense/TeslaLightning");
        if (lightningShader == null)
        {
            Debug.LogWarning("The TowerDefense/TeslaLightning shader could not be found.");
            lightningShader = Shader.Find("Sprites/Default");
        }

        if (lightningShader != null)
        {
            sharedLightningMaterial = new Material(lightningShader)
            {
                name = "Shared Tesla Lightning Material",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        return sharedLightningMaterial;
    }

    private void ShowBolt(int index, Transform startTarget, Transform endTarget)
    {
        if ((uint)index >= (uint)boltLines.Length)
        {
            return;
        }

        boltStarts[index] = startTarget;
        boltEnds[index] = endTarget;
        boltStartPositions[index] = startTarget.position;
        boltEndPositions[index] = endTarget.position;
        boltElapsed[index] = 0f;
        boltNextReshape[index] = reshapeInterval;
        boltActive[index] = true;
        boltLines[index].enabled = true;
        RollBoltShape(index);
        EmitSparks(endTarget.position);
        UpdateBolt(index);
    }

    private void UpdateBolts(float deltaTime)
    {
        for (int i = 0; i < boltActive.Length; i++)
        {
            if (!boltActive[i])
            {
                continue;
            }

            boltElapsed[i] += deltaTime;
            if (boltElapsed[i] >= lightningDuration)
            {
                boltActive[i] = false;
                boltLines[i].enabled = false;
                boltStarts[i] = null;
                boltEnds[i] = null;
                continue;
            }

            // Follow targets while they live. A killed enemy no longer cuts
            // the bolt short; it keeps fading at the victim's last position.
            Transform startTarget = boltStarts[i];
            if (startTarget != null && startTarget.gameObject.activeInHierarchy)
            {
                boltStartPositions[i] = startTarget.position;
            }
            else
            {
                boltStarts[i] = null;
            }

            Transform endTarget = boltEnds[i];
            if (endTarget != null && endTarget.gameObject.activeInHierarchy)
            {
                boltEndPositions[i] = endTarget.position;
            }
            else
            {
                boltEnds[i] = null;
            }

            // The zigzag holds each shape briefly before re-rolling, so the
            // arc visibly writhes; re-rolling every frame read as frozen fuzz.
            if (boltElapsed[i] >= boltNextReshape[i])
            {
                RollBoltShape(i);
                boltNextReshape[i] += reshapeInterval;
            }

            UpdateBolt(i);
        }
    }

    private void RollBoltShape(int index)
    {
        float[] offsets = boltJitter[index];
        for (int i = 0; i < offsets.Length; i++)
        {
            offsets[i] = Random.Range(-jitterAmount, jitterAmount);
        }
    }

    private void UpdateBolt(int index)
    {
        LineRenderer line = boltLines[index];
        if (line == null)
        {
            return;
        }

        float progress = Mathf.Clamp01(
            boltElapsed[index] / Mathf.Max(0.01f, lightningDuration));
        // Hold full brightness for most of the strike, then ease out; a
        // linear fade from birth made bolts read as vanishing instantly.
        float life = 1f - Mathf.SmoothStep(
            0f, 1f, Mathf.InverseLerp(0.45f, 1f, progress));
        line.startColor = new Color(1f, 1f, 1f, life);
        line.endColor = new Color(
            lightningColor.r,
            lightningColor.g,
            lightningColor.b,
            lightningColor.a * life);

        Vector2 start = boltStartPositions[index];
        Vector2 end = boltEndPositions[index];
        Vector2 direction = end - start;
        float directionLengthSquared = direction.sqrMagnitude;
        Vector2 perpendicular = directionLengthSquared > 0.000001f
            ? new Vector2(-direction.y, direction.x) / Mathf.Sqrt(directionLengthSquared)
            : Vector2.up;

        int pointCount = line.positionCount;
        float[] offsets = boltJitter[index];
        for (int i = 0; i < pointCount; i++)
        {
            float alongBolt = i / (pointCount - 1f);
            Vector2 point = Vector2.Lerp(start, end, alongBolt);
            if (i > 0 && i < pointCount - 1)
            {
                float taper = Mathf.Sin(alongBolt * Mathf.PI);
                point += perpendicular * (offsets[i] * taper);
            }

            line.SetPosition(i, point);
        }
    }

    private void CreateSparkSystem()
    {
        GameObject sparkObject = new GameObject("Tesla Sparks");
        sparkObject.transform.SetParent(transform, false);
        sparks = sparkObject.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = sparks.main;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
        main.startColor = new ParticleSystem.MinMaxGradient(Color.white, lightningColor);
        main.gravityModifier = 0.5f;

        // Bursts come only from Emit(); nothing trickles out between zaps.
        ParticleSystem.EmissionModule emission = sparks.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = sparks.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.05f;

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = sparks.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(lightningColor, 0.35f),
                new GradientColorKey(lightningColor, 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 0.4f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = gradient;

        // Stretched along their velocity, the particles read as flying
        // sparks rather than dots.
        ParticleSystemRenderer sparkRenderer = sparkObject.GetComponent<ParticleSystemRenderer>();
        sparkRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        sparkRenderer.lengthScale = 3.5f;
        sparkRenderer.sortingLayerName = "Towers";
        sparkRenderer.sortingOrder = 4;
        sparkRenderer.sharedMaterial = GetSharedSparkMaterial();
    }

    private void EmitSparks(Vector3 position)
    {
        if (sparks == null || sparksPerHit <= 0)
        {
            return;
        }

        ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams
        {
            position = position,
            applyShapeToPosition = true
        };
        sparks.Emit(emitParams, sparksPerHit);
    }

    private void CreateOrb()
    {
        Shader orbShader = Shader.Find("TowerDefense/TeslaOrb");
        if (orbShader == null)
        {
            Debug.LogWarning("The TowerDefense/TeslaOrb shader could not be found.", this);
            return;
        }

        GameObject orbObject = new GameObject("Tesla Orb");
        orbObject.transform.SetParent(transform, false);
        orbObject.transform.localScale = new Vector3(orbSize, orbSize, 1f);

        Mesh quad = new Mesh { name = "Tesla Orb Quad" };
        quad.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f),
            new Vector3(0.5f, -0.5f),
            new Vector3(-0.5f, 0.5f),
            new Vector3(0.5f, 0.5f)
        };
        quad.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f)
        };
        quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };

        orbObject.AddComponent<MeshFilter>().sharedMesh = quad;
        MeshRenderer orbRenderer = orbObject.AddComponent<MeshRenderer>();
        orbRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        orbRenderer.receiveShadows = false;
        orbRenderer.sortingLayerName = "Towers";
        orbRenderer.sortingOrder = 4;

        orbMaterial = new Material(orbShader) { name = "Tesla Orb Material" };
        orbRenderer.sharedMaterial = orbMaterial;
    }

    private void UpdateOrb(int powerLevel, float deltaTime)
    {
        if (orbMaterial == null)
        {
            return;
        }

        orbFlare = Mathf.MoveTowards(
            orbFlare, 0f, deltaTime / Mathf.Max(0.01f, orbFlareDuration));
        // A powered orb idles at a visible glow and flares on each zap; an
        // unpowered one is a barely-lit ember.
        float idle = powerLevel > 0 ? 0.55f : 0.12f;
        orbMaterial.SetFloat(OrbIntensityId, idle + orbFlare * 0.7f);
    }

    private static Material GetSharedSparkMaterial()
    {
        if (sharedSparkMaterial != null)
        {
            return sharedSparkMaterial;
        }

        Shader spriteShader = Shader.Find("Sprites/Default");
        if (spriteShader != null)
        {
            sharedSparkMaterial = new Material(spriteShader)
            {
                name = "Shared Tesla Spark Material",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        return sharedSparkMaterial;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = lightningColor;
        Gizmos.DrawWireSphere(transform.position, initialTargetRadius);
    }
}
