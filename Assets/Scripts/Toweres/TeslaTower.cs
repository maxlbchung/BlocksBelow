using System.Collections;
using System.Collections.Generic;
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

    private float nextZapTime;

    public int ChainCount => chainCount;

    private void Start()
    {
        Zap();
        nextZapTime = Time.time + zapInterval;
    }

    private void Update()
    {
        if (Time.time < nextZapTime)
        {
            return;
        }

        Zap();
        nextZapTime = Time.time + Mathf.Max(0.01f, zapInterval);
    }

    public void IncrementChains(int amount = 1)
    {
        chainCount = Mathf.Max(0, chainCount + amount);
    }

    public void Zap()
    {
        Transform firstEnemy = FindClosestEnemy(transform.position, initialTargetRadius, null);
        if (firstEnemy == null)
        {
            return;
        }

        List<Transform> hitEnemies = new List<Transform> { firstEnemy };
        StartCoroutine(DrawLightning(transform, firstEnemy));

        Transform currentEnemy = firstEnemy;
        for (int i = 0; i < chainCount; i++)
        {
            currentEnemy.gameObject.GetComponent<Enemy>().health -= damage; // Apply damage to the current enemy
            Transform nextEnemy = FindClosestEnemy(currentEnemy.position, chainRadius, hitEnemies);
            if (nextEnemy == null)
            {
                break;
            }

            StartCoroutine(DrawLightning(currentEnemy, nextEnemy));
            hitEnemies.Add(nextEnemy);
            currentEnemy = nextEnemy;
        }
    }

    private Transform FindClosestEnemy(
        Vector2 origin,
        float radius,
        List<Transform> excludedEnemies)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(origin, radius);
        Transform closest = null;
        float closestDistance = float.PositiveInfinity;

        foreach (Collider2D hit in hits)
        {
            Transform enemy = FindEnemyRoot(hit);
            if (enemy == null || (excludedEnemies != null && excludedEnemies.Contains(enemy)))
            {
                continue;
            }

            float distance = ((Vector2)enemy.position - origin).sqrMagnitude;
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = enemy;
            }
        }

        return closest;
    }

    private IEnumerator DrawLightning(Transform startTarget, Transform endTarget)
    {
        GameObject boltObject = new GameObject("Tesla Lightning");
        LineRenderer line = boltObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = Mathf.Max(2, pointsPerBolt);
        line.startWidth = lineWidth;
        line.endWidth = lineWidth * 0.35f;
        line.numCapVertices = 2;
        line.sortingLayerName = "Towers";
        line.sortingOrder = 3;

        Shader spriteShader = Shader.Find("Sprites/Default");
        if (spriteShader != null)
        {
            line.material = new Material(spriteShader);
        }

        float elapsed = 0f;
        while (elapsed < lightningDuration && startTarget != null && endTarget != null)
        {
            elapsed += Time.deltaTime;
            float life = 1f - Mathf.Clamp01(elapsed / Mathf.Max(0.01f, lightningDuration));
            Color fadedColor = new Color(
                lightningColor.r,
                lightningColor.g,
                lightningColor.b,
                lightningColor.a * life
            );
            line.startColor = Color.white * new Color(1f, 1f, 1f, life);
            line.endColor = fadedColor;

            Vector2 start = startTarget.position;
            Vector2 end = endTarget.position;
            Vector2 direction = end - start;
            Vector2 perpendicular = direction.sqrMagnitude > 0f
                ? new Vector2(-direction.y, direction.x).normalized
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

            yield return null;
        }

        Destroy(boltObject);
    }

    private static Transform FindEnemyRoot(Collider2D hit)
    {
        Transform current = hit.attachedRigidbody != null
            ? hit.attachedRigidbody.transform
            : hit.transform;

        while (current != null)
        {
            if (current.CompareTag("Enemy"))
            {
                return current;
            }

            current = current.parent;
        }

        return null;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = lightningColor;
        Gizmos.DrawWireSphere(transform.position, initialTargetRadius);
    }
}
