using System.Collections.Generic;
using UnityEngine;

public class TowerCageStack : MonoBehaviour
{
    [SerializeField, Min(0)] private int towerValue;
    [SerializeField] private List<CageTower> cagesBelow = new List<CageTower>();

    private float cellSize = 1f;

    public int TowerValue => towerValue;
    public int PowerLevel => towerValue;
    public IReadOnlyList<CageTower> CagesBelow => cagesBelow;

    public void Initialize(float gridCellSize)
    {
        cellSize = Mathf.Max(0.01f, gridCellSize);
        Physics2D.SyncTransforms();
        FindContinuousCagesBelow();
        RefreshTowerValue();
    }

    private void Update()
    {
        RefreshTowerValue();
    }

    private void FindContinuousCagesBelow()
    {
        cagesBelow.Clear();
        float probeSize = cellSize * 0.2f;

        // A gap or any non-cage tower ends the stack immediately.
        for (int distance = 1; distance <= 1000; distance++)
        {
            Vector2 cellCenter = (Vector2)transform.position + Vector2.down * (cellSize * distance);
            Collider2D[] hits = Physics2D.OverlapBoxAll(cellCenter, Vector2.one * probeSize, 0f);
            CageTower cage = null;

            foreach (Collider2D hit in hits)
            {
                if (hit.isTrigger)
                {
                    continue;
                }

                cage = hit.GetComponent<CageTower>();
                if (cage != null)
                {
                    break;
                }
            }

            if (cage == null)
            {
                break;
            }

            cagesBelow.Add(cage);
        }
    }

    private void RefreshTowerValue()
    {
        int fullCageCount = 0;

        foreach (CageTower cage in cagesBelow)
        {
            if (cage != null && cage.State == CageTower.CageState.Full)
            {
                fullCageCount++;
            }
        }

        towerValue = fullCageCount;
    }
}
