using UnityEngine;

public class TeslaTower : MonoBehaviour
{
    [Header("Targeting")]
    [SerializeField, Min(0.1f)] private float initialTargetRadius = 5f;
    [SerializeField, Min(0.1f)] private float chainRadius = 5f;
    [SerializeField, Min(0)] private int chainCount;
    [SerializeField, Min(0.01f)] private float zapInterval = 1f;

    [Header("Lightning")]
    [SerializeField, Min(0.01f)] private float lightningDuration = 0.2f;
    [SerializeField, Min(2)] private int pointsPerBolt = 7;
    [SerializeField, Min(0f)] private float jitterAmount = 0.12f;
    [SerializeField, Min(0.001f)] private float lineWidth = 0.06f;
    [SerializeField] private Color lightningColor = new Color(0.2f, 0.85f, 1f, 1f);
    [SerializeField] private float damage = 1f;
    [SerializeField] private AudioClip zapSfx;

    private static Material sharedLightningMaterial;

    private float nextZapTime;
    private Enemy[] hitEnemies;
    private LineRenderer[] boltLines;
    private Transform[] boltStarts;
    private Transform[] boltEnds;
    private float[] boltElapsed;
    private bool[] boltActive;

    public int ChainCount => chainCount;

    public void Configure(AudioClip newZapSfx)
    {
        zapSfx = newZapSfx;
    }

    private void Awake()
    {
        EnsureCapacity(Mathf.Max(1, chainCount + 1));
    }

    private void Start()
    {
        nextZapTime = Time.time + zapInterval;
    }

    private void Update()
    {
        // Bolts keep fading even outside a wave; only new zaps are gated.
        UpdateBolts(Time.deltaTime);

        if (!WaveSpawner.IsWaveActive || Time.time < nextZapTime)
        {
            return;
        }

        Zap();
        nextZapTime = Time.time + Mathf.Max(0.01f, zapInterval);
    }

    public void IncrementChains(int amount = 1)
    {
        chainCount = Mathf.Max(0, chainCount + amount);
        EnsureCapacity(Mathf.Max(1, chainCount + 1));
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
        if (zapSfx != null)
        {
            AudioController.Play(zapSfx);
        }

        ShowBolt(0, transform, firstEnemy.transform);
        Enemy currentEnemy = firstEnemy;
        for (int i = 0; i < chainCount; i++)
        {
            currentEnemy.health -= damage;
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
        float[] newBoltElapsed = new float[capacity];
        bool[] newBoltActive = new bool[capacity];

        for (int i = 0; i < oldLength; i++)
        {
            newHitEnemies[i] = hitEnemies[i];
            newBoltLines[i] = boltLines[i];
            newBoltStarts[i] = boltStarts[i];
            newBoltEnds[i] = boltEnds[i];
            newBoltElapsed[i] = boltElapsed[i];
            newBoltActive[i] = boltActive[i];
        }

        hitEnemies = newHitEnemies;
        boltLines = newBoltLines;
        boltStarts = newBoltStarts;
        boltEnds = newBoltEnds;
        boltElapsed = newBoltElapsed;
        boltActive = newBoltActive;

        for (int i = oldLength; i < capacity; i++)
        {
            boltLines[i] = CreateBoltLine(i);
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
        line.endWidth = lineWidth * 0.35f;
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

        Shader spriteShader = Shader.Find("Sprites/Default");
        if (spriteShader != null)
        {
            sharedLightningMaterial = new Material(spriteShader)
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
        boltElapsed[index] = 0f;
        boltActive[index] = true;
        boltLines[index].enabled = true;
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

            Transform startTarget = boltStarts[i];
            Transform endTarget = boltEnds[i];
            boltElapsed[i] += deltaTime;

            if (boltElapsed[i] >= lightningDuration
                || startTarget == null
                || endTarget == null
                || !startTarget.gameObject.activeInHierarchy
                || !endTarget.gameObject.activeInHierarchy)
            {
                boltActive[i] = false;
                boltLines[i].enabled = false;
                boltStarts[i] = null;
                boltEnds[i] = null;
                continue;
            }

            UpdateBolt(i);
        }
    }

    private void UpdateBolt(int index)
    {
        LineRenderer line = boltLines[index];
        Transform startTarget = boltStarts[index];
        Transform endTarget = boltEnds[index];
        if (line == null || startTarget == null || endTarget == null)
        {
            return;
        }

        float life = 1f - Mathf.Clamp01(
            boltElapsed[index] / Mathf.Max(0.01f, lightningDuration));
        line.startColor = new Color(1f, 1f, 1f, life);
        line.endColor = new Color(
            lightningColor.r,
            lightningColor.g,
            lightningColor.b,
            lightningColor.a * life);

        Vector2 start = startTarget.position;
        Vector2 end = endTarget.position;
        Vector2 direction = end - start;
        float directionLengthSquared = direction.sqrMagnitude;
        Vector2 perpendicular = directionLengthSquared > 0.000001f
            ? new Vector2(-direction.y, direction.x) / Mathf.Sqrt(directionLengthSquared)
            : Vector2.up;

        int pointCount = line.positionCount;
        for (int i = 0; i < pointCount; i++)
        {
            float progress = i / (pointCount - 1f);
            Vector2 point = Vector2.Lerp(start, end, progress);
            if (i > 0 && i < pointCount - 1)
            {
                float taper = Mathf.Sin(progress * Mathf.PI);
                point += perpendicular * Random.Range(-jitterAmount, jitterAmount) * taper;
            }

            line.SetPosition(i, point);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = lightningColor;
        Gizmos.DrawWireSphere(transform.position, initialTargetRadius);
    }
}
