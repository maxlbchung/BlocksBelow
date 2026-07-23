using UnityEngine;

public class SawBladeTower : MonoBehaviour
{
    [SerializeField] private GameObject sawPrefab;
    [SerializeField, Min(0)] private int numberOfSaws = 3;
    [SerializeField, Min(0f)] private float orbitRadius = 1f;
    [SerializeField] private float orbitSpeed = 90f;

    private Transform sawOrbit;

    private void Start()
    {
        CreateSaws();
    }

    private void Update()
    {
        if (sawOrbit != null)
        {
            sawOrbit.Rotate(0f, 0f, orbitSpeed * Time.deltaTime);
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
        }
    }
}
