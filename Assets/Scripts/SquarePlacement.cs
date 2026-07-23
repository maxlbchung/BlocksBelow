using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Places Block prefabs on a square grid at the mouse cursor.
/// A block can only be placed in an empty cell beside an existing Block.
/// </summary>
public class SquarePlacement : MonoBehaviour
{
    [Header("Placement")]
    [SerializeField] private GameObject squarePrefab;
    [SerializeField, Min(0.01f)] private float cellSize = 1f;
    [SerializeField] private Vector2 gridOrigin;

    [Header("Collision")]
    [Tooltip("Layers that prevent a square from being placed in a cell.")]
    [SerializeField] private LayerMask blockingLayers = ~0;
    [Tooltip("Layers containing existing squares. Their objects must also have a Block component.")]
    [SerializeField] private LayerMask squareLayers = ~0;

    private Camera mainCamera;

    private void Awake()
    {
        mainCamera = Camera.main;
    }

    private void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
        {
            return;
        }

        TryPlaceAtCursor(mouse.position.ReadValue());
    }

    private void TryPlaceAtCursor(Vector2 screenPosition)
    {
        if (squarePrefab == null)
        {
            Debug.LogError("Square Placement needs a square prefab.", this);
            return;
        }

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null)
            {
                Debug.LogError("Square Placement could not find a Main Camera.", this);
                return;
            }
        }

        Vector3 worldPosition = mainCamera.ScreenToWorldPoint(screenPosition);
        Vector2 cellPosition = SnapToGrid(worldPosition);

        if (IsCellOccupied(cellPosition) || !HasAdjacentSquare(cellPosition))
        {
            return;
        }

        Instantiate(squarePrefab, cellPosition, Quaternion.identity);
    }

    private Vector2 SnapToGrid(Vector2 worldPosition)
    {
        float x = Mathf.Round((worldPosition.x - gridOrigin.x) / cellSize) * cellSize;
        float y = Mathf.Round((worldPosition.y - gridOrigin.y) / cellSize) * cellSize;
        return gridOrigin + new Vector2(x, y);
    }

    private bool IsCellOccupied(Vector2 cellPosition)
    {
        // Slightly smaller than the cell so squares touching at their edges do not count as overlap.
        Vector2 checkSize = Vector2.one * (cellSize * 0.9f);
        return Physics2D.OverlapBox(cellPosition, checkSize, 0f, blockingLayers) != null;
    }

    private bool HasAdjacentSquare(Vector2 cellPosition)
    {
        Vector2[] directions =
        {
            Vector2.up,
            Vector2.down,
            Vector2.left,
            Vector2.right
        };

        // A small check at each neighboring cell's center avoids treating diagonal blocks as adjacent.
        Vector2 checkSize = Vector2.one * (cellSize * 0.2f);

        foreach (Vector2 direction in directions)
        {
            Vector2 neighborPosition = cellPosition + direction * cellSize;
            Collider2D[] hits = Physics2D.OverlapBoxAll(
                neighborPosition,
                checkSize,
                0f,
                squareLayers
            );

            foreach (Collider2D hit in hits)
            {
                if (hit.GetComponentInParent<Block>() != null)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(gridOrigin, Vector3.one * cellSize);
    }
}
