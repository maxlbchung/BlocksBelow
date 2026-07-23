using UnityEngine;
using System.Collections.Generic;

public class SawBladeTower : MonoBehaviour
{
    [SerializeField] private GameObject sawPrefab;
    [SerializeField, Min(0)] private int numberOfSaws = 1;
    [SerializeField, Min(0f)] private float orbitRadius = 3f;
    [SerializeField] private float orbitSpeed = 90f;
    [Header("Saw Tethers")]
    [SerializeField, Min(0.001f)] private float lineWidth = 0.05f;
    [SerializeField] private Color lineColor = Color.gray;

    private Transform sawOrbit;
    private readonly List<Transform> saws = new List<Transform>();
    private readonly List<LineRenderer> sawLines = new List<LineRenderer>();

    public void Configure(GameObject newSawPrefab)
    {
        sawPrefab = newSawPrefab;
    }

    private void Start()
    {
        CreateSaws();
    }

    private void Update()
    {
        if (sawOrbit != null)
        {
            sawOrbit.Rotate(0f, 0f, orbitSpeed * Time.deltaTime);
            UpdateSawLines();
        }
    }

    private void CreateSaws()
    {
        if (sawPrefab == null)
        {
            Debug.LogWarning($"{name} needs a saw prefab assigned.", this);
            return;
        }

        int sawCount = Mathf.Max(0, numberOfSaws);
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

        Shader spriteShader = Shader.Find("Sprites/Default");
        if (spriteShader != null)
        {
            line.material = new Material(spriteShader);
        }

        return line;
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
