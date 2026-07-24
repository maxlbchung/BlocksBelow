using System.Collections.Generic;
using UnityEngine;

public class FanTower : MonoBehaviour
{
    [SerializeField, Min(0f)] private float pushForce;
    [SerializeField, Min(0f)] private float forcePerPowerLevel = 3f;
    // Tuned against the player's fallAcceleration (20 at mass 1): with
    // forcePerPowerLevel 3, power 1 gives 7.5 (a floaty, barely-there push)
    // and power 3 gives 22.5, the point where an updraft overrides gravity.
    [SerializeField, Min(0f)] private float playerForceMultiplier = 2.5f;
    [SerializeField, Min(0f)] private float spinUpTime = 1.5f;
    [SerializeField, Min(0f)] private float spinDownTime = 0.4f;
    [SerializeField, Min(0.1f)] private float windRange = 5f;
    [SerializeField, Min(0.1f)] private float windWidth = 2f;
    [SerializeField] private Color windColor = new Color(0.94f, 0.98f, 1f, 0.3f);

    private static readonly int StrengthId = Shader.PropertyToID("_Strength");

    private Material windMaterial;
    private TowerCageStack cageStack;
    private Vector2 windForce;
    private float activation;
    private const int FunnelSegments = 16;

    private void Start()
    {
        cageStack = GetComponent<TowerCageStack>();
        CreateWindArea();
    }

    private void Update()
    {
        pushForce = (cageStack != null ? cageStack.PowerLevel : 0f) * forcePerPowerLevel;

        // The fan winds down for the building phase or when unpowered, and
        // spins back up smoothly once the round starts.
        float targetActivation = WaveSpawner.IsWaveActive && pushForce > 0f ? 1f : 0f;
        float rampTime = targetActivation > activation ? spinUpTime : spinDownTime;
        activation = rampTime > 0f
            ? Mathf.MoveTowards(activation, targetActivation, Time.deltaTime / rampTime)
            : targetActivation;

        if (windMaterial != null)
        {
            windMaterial.SetFloat(StrengthId, activation);
        }
    }

    private void FixedUpdate()
    {
        // Cache the wind force once per physics step. ApplyWind runs from OnTriggerStay2D for
        // every enemy inside the zone each step, so this avoids recomputing it per enemy.
        windForce = (Vector2)transform.right * (pushForce * activation);
    }

    public void ApplyWind(Collider2D other)
    {
        if (activation <= 0f)
        {
            return;
        }

        if (other.CompareTag("Enemy"))
        {
            Rigidbody2D enemyBody = other.attachedRigidbody;
            if (enemyBody != null)
            {
                enemyBody.AddForce(windForce, ForceMode2D.Force);
            }

            return;
        }

        if (other.CompareTag("Player"))
        {
            // ApplyWindForce integrates the push into the player's movement model
            // without the control lock ApplyKnockback would impose every step.
            Rigidbody2D playerBody = other.attachedRigidbody;
            if (playerBody != null
                && playerBody.TryGetComponent(out PlayerController player))
            {
                player.ApplyWindForce(windForce * playerForceMultiplier);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(transform.position, transform.right);

        Matrix4x4 previousMatrix = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Vector3 previousTop = new Vector3(0f, GetWindHalfWidth(0f), 0f);
        Vector3 previousBottom = new Vector3(0f, -GetWindHalfWidth(0f), 0f);

        for (int i = 1; i <= FunnelSegments; i++)
        {
            float t = i / (float)FunnelSegments;
            float x = windRange * t;
            float halfWidth = GetWindHalfWidth(t);
            Vector3 top = new Vector3(x, halfWidth, 0f);
            Vector3 bottom = new Vector3(x, -halfWidth, 0f);
            Gizmos.DrawLine(previousTop, top);
            Gizmos.DrawLine(previousBottom, bottom);
            previousTop = top;
            previousBottom = bottom;
        }

        Gizmos.DrawLine(
            new Vector3(0f, -GetWindHalfWidth(0f), 0f),
            new Vector3(0f, GetWindHalfWidth(0f), 0f)
        );
        Gizmos.DrawLine(previousBottom, previousTop);
        Gizmos.matrix = previousMatrix;
    }

    private void CreateWindArea()
    {
        GameObject visual = new GameObject("Wind Area");
        visual.transform.SetParent(transform, false);
        visual.tag = "Untagged";

        PolygonCollider2D windTrigger = visual.AddComponent<PolygonCollider2D>();
        windTrigger.isTrigger = true;

        List<Vector2> colliderPoints = new List<Vector2>();
        for (int i = 0; i <= FunnelSegments; i++)
        {
            float t = i / (float)FunnelSegments;
            colliderPoints.Add(new Vector2(windRange * t, GetWindHalfWidth(t)));
        }

        for (int i = FunnelSegments; i >= 0; i--)
        {
            float t = i / (float)FunnelSegments;
            colliderPoints.Add(new Vector2(windRange * t, -GetWindHalfWidth(t)));
        }

        windTrigger.points = colliderPoints.ToArray();
        visual.AddComponent<FanWindTrigger>().Configure(this);

        MeshFilter meshFilter = visual.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = visual.AddComponent<MeshRenderer>();
        meshRenderer.sortingLayerName = "Towers";
        meshRenderer.sortingOrder = 0;

        Mesh mesh = new Mesh { name = "Fan Wind Area Mesh" };
        Vector3[] vertices = new Vector3[(FunnelSegments + 1) * 2];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[FunnelSegments * 6];

        for (int i = 0; i <= FunnelSegments; i++)
        {
            float t = i / (float)FunnelSegments;
            float x = windRange * t;
            float halfWidth = GetWindHalfWidth(t);
            int vertexIndex = i * 2;
            vertices[vertexIndex] = new Vector3(x, -halfWidth, 0f);
            vertices[vertexIndex + 1] = new Vector3(x, halfWidth, 0f);
            uvs[vertexIndex] = new Vector2(t, 0f);
            uvs[vertexIndex + 1] = new Vector2(t, 1f);
        }

        for (int i = 0; i < FunnelSegments; i++)
        {
            int vertexIndex = i * 2;
            int triangleIndex = i * 6;
            triangles[triangleIndex] = vertexIndex;
            triangles[triangleIndex + 1] = vertexIndex + 1;
            triangles[triangleIndex + 2] = vertexIndex + 2;
            triangles[triangleIndex + 3] = vertexIndex + 1;
            triangles[triangleIndex + 4] = vertexIndex + 3;
            triangles[triangleIndex + 5] = vertexIndex + 2;
        }

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        meshFilter.sharedMesh = mesh;

        Shader windShader = Shader.Find("TowerDefense/WindArea");
        if (windShader == null)
        {
            Debug.LogWarning("The TowerDefense/WindArea shader could not be found.", this);
            return;
        }

        windMaterial = new Material(windShader);
        windMaterial.SetColor("_WindColor", windColor);
        windMaterial.SetFloat(StrengthId, activation);
        meshRenderer.sharedMaterial = windMaterial;
    }

    private float GetWindHalfWidth(float normalizedDistance)
    {
        const float flareEnd = 0.3f;
        float flareProgress = Mathf.Clamp01(normalizedDistance / flareEnd);
        float easedFlare = 1f - Mathf.Pow(1f - flareProgress, 3f);
        float startWidth = windWidth * 0.015f;
        return Mathf.Lerp(startWidth, windWidth * 0.5f, easedFlare);
    }

    private void OnDestroy()
    {
        if (windMaterial != null)
        {
            Destroy(windMaterial);
        }
    }
}

public class FanWindTrigger : MonoBehaviour
{
    private FanTower owner;

    public void Configure(FanTower fanTower)
    {
        owner = fanTower;
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        if (owner != null)
        {
            owner.ApplyWind(other);
        }
    }
}
