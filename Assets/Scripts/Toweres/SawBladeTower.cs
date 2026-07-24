using UnityEngine;
using System.Collections.Generic;

public class SawBladeTower : MonoBehaviour
{
    [SerializeField] private GameObject sawPrefab;
    [SerializeField, Min(0)] private int numberOfSaws;
    [SerializeField, Min(0f)] private float orbitRadius = 3f;
    [SerializeField] private float orbitSpeed = 90f;
    [Header("Saw Tethers")]
    [SerializeField, Min(0.001f)] private float lineWidth = 0.05f;
    [SerializeField] private Color lineColor = Color.gray;
    [SerializeField] private AudioClip hitSfx;

    private Transform sawOrbit;
    private TowerCageStack cageStack;
    private int activeSawCount = -1;
    private readonly List<Transform> saws = new List<Transform>();
    private readonly List<LineRenderer> sawLines = new List<LineRenderer>();

    private static Material sharedTetherMaterial;

    public void Configure(GameObject newSawPrefab, AudioClip newHitSfx = null)
    {
        sawPrefab = newSawPrefab;
        hitSfx = newHitSfx;
    }

    private void Start()
    {
        cageStack = GetComponent<TowerCageStack>();
        RefreshSaws();
    }

    private void Update()
    {
        numberOfSaws = cageStack != null ? cageStack.PowerLevel : 0;
        if (numberOfSaws != activeSawCount)
        {
            RefreshSaws();
        }

        if (sawOrbit != null)
        {
            sawOrbit.Rotate(0f, 0f, orbitSpeed * Time.deltaTime);
            UpdateSawLines();
        }
    }

    private void RefreshSaws()
    {
        if (sawOrbit != null)
        {
            Destroy(sawOrbit.gameObject);
            sawOrbit = null;
        }

        foreach (LineRenderer line in sawLines)
        {
            if (line != null)
            {
                Destroy(line.gameObject);
            }
        }

        saws.Clear();
        sawLines.Clear();
        activeSawCount = Mathf.Max(0, numberOfSaws);

        if (activeSawCount > 0)
        {
            CreateSaws();
        }
    }

    private void CreateSaws()
    {
        if (sawPrefab == null)
        {
            Debug.LogWarning($"{name} needs a saw prefab assigned.", this);
            return;
        }

        int sawCount = activeSawCount;
        if (sawCount == 0)
        {
            return;
        }

        GameObject orbitObject = new GameObject("Saw Orbit");
        sawOrbit = orbitObject.transform;
        sawOrbit.SetParent(transform, false);

        float angleStep = 360f / sawCount;
        for (int i = 0; i < sawCount; i++)
        {
            float angle = angleStep * i * Mathf.Deg2Rad;
            Vector3 localPosition = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * orbitRadius;
            GameObject saw = Instantiate(sawPrefab, sawOrbit);
            saw.name = $"Saw {i + 1}";
            saw.transform.localPosition = localPosition;
            saw.transform.localRotation = Quaternion.identity;
            SawBlade blade = saw.GetComponent<SawBlade>();
            if (blade != null)
            {
                blade.ConfigureSfx(hitSfx);
            }

            saws.Add(saw.transform);
            sawLines.Add(CreateSawLine(i + 1));
        }

        UpdateSawLines();
    }

    private LineRenderer CreateSawLine(int sawNumber)
    {
        GameObject lineObject = new GameObject($"Saw Tether {sawNumber}");
        lineObject.transform.SetParent(transform, false);

        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.startWidth = lineWidth;
        line.endWidth = lineWidth;
        line.startColor = lineColor;
        line.endColor = lineColor;
        line.numCapVertices = 2;
        line.sortingOrder = -1;
        // Per-tether color comes from the LineRenderer's vertex colors, so every tether can
        // share one material. Avoids a Material clone per saw (draw-call batching + no leak).
        line.sharedMaterial = GetSharedTetherMaterial();

        return line;
    }

    private static Material GetSharedTetherMaterial()
    {
        if (sharedTetherMaterial != null)
        {
            return sharedTetherMaterial;
        }

        Shader spriteShader = Shader.Find("Sprites/Default");
        if (spriteShader != null)
        {
            sharedTetherMaterial = new Material(spriteShader)
            {
                name = "Shared Saw Tether Material",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        return sharedTetherMaterial;
    }

    private void UpdateSawLines()
    {
        int count = Mathf.Min(saws.Count, sawLines.Count);
        for (int i = 0; i < count; i++)
        {
            if (saws[i] == null || sawLines[i] == null)
            {
                continue;
            }

            sawLines[i].SetPosition(0, transform.position);
            sawLines[i].SetPosition(1, saws[i].position);
        }
    }
}
