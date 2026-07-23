using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Places tower prefabs on a square grid at the mouse cursor.
/// A tower can only be placed in an empty cell beside a Collider2D tagged "tower".
/// </summary>
public class SquarePlacement : MonoBehaviour
{
    [Header("Placement")]
    [SerializeField] private GameObject squarePrefab;
    [SerializeField, Min(0.01f)] private float cellSize = 1f;
    [SerializeField] private Vector2 gridOrigin;
    [SerializeField] private TowerShopUI towerShop;

    [Header("Ghost")]
    [Tooltip("Leave empty to use the square prefab's sprite.")]
    [SerializeField] private Sprite ghostSprite;
    [SerializeField] private Color validGhostColor = new Color(1f, 1f, 1f, 0.5f);
    [SerializeField] private Color invalidGhostColor = new Color(1f, 0f, 0f, 0.5f);

    [Header("Collision")]
    [Tooltip("Layers that prevent a square from being placed in a cell.")]
    [SerializeField] private LayerMask blockingLayers = ~0;
    [Tooltip("The starting base. It may be a BoxCollider2D of any width and does not need the tower tag.")]
    [SerializeField] private Collider2D placementBase;
    [SerializeField, Min(0.001f)] private float adjacencyTolerance = 0.05f;

    private Camera mainCamera;
    private GameObject ghostObject;
    private SpriteRenderer ghostRenderer;
    private int selectedTowerPrice;

    private void Awake()
    {
        mainCamera = Camera.main;
        if (towerShop == null)
        {
            towerShop = FindFirstObjectByType<TowerShopUI>();
        }
        CreateGhost();
    }

    private void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            SetGhostVisible(false);
            return;
        }

        Vector2 cursorPosition = mouse.position.ReadValue();
        UpdateGhost(cursorPosition);

        if (mouse.leftButton.wasPressedThisFrame
            && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
        {
            TryPlaceAtCursor(cursorPosition);
        }
    }

    private void CreateGhost()
    {
        ghostObject = new GameObject("Square Placement Ghost");
        ghostObject.transform.SetParent(transform);
        ghostObject.SetActive(false);

        ghostRenderer = ghostObject.AddComponent<SpriteRenderer>();
        ghostRenderer.color = invalidGhostColor;

        SpriteRenderer prefabRenderer = squarePrefab != null
            ? squarePrefab.GetComponentInChildren<SpriteRenderer>()
            : null;

        ghostRenderer.sprite = ghostSprite != null
            ? ghostSprite
            : prefabRenderer != null ? prefabRenderer.sprite : null;

        if (prefabRenderer != null)
        {
            ghostRenderer.sortingLayerID = prefabRenderer.sortingLayerID;
            ghostRenderer.sortingOrder = prefabRenderer.sortingOrder + 1;
            ghostRenderer.flipX = prefabRenderer.flipX;
            ghostRenderer.flipY = prefabRenderer.flipY;
        }
    }

    private void UpdateGhost(Vector2 screenPosition)
    {
        if (ghostObject == null)
        {
            CreateGhost();
        }

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        if (mainCamera == null)
        {
            SetGhostVisible(false);
            return;
        }

        Vector3 worldPosition = mainCamera.ScreenToWorldPoint(screenPosition);
        Vector2 cellPosition = SnapToGrid(worldPosition);
        ghostObject.transform.position = new Vector3(cellPosition.x, cellPosition.y, 0f);
        ghostRenderer.color = CanPlaceAt(cellPosition) ? validGhostColor : invalidGhostColor;
        SetGhostVisible(true);
    }

    private void SetGhostVisible(bool isVisible)
    {
        if (ghostObject != null && ghostObject.activeSelf != isVisible)
        {
            ghostObject.SetActive(isVisible);
        }
    }

    /// <summary>
    /// Changes the sprite used by the cursor ghost. Passing null restores the
    /// square prefab's normal sprite.
    /// </summary>
    public void SetGhostSprite(Sprite sprite)
    {
        ghostSprite = sprite;

        if (ghostRenderer == null)
        {
            CreateGhost();
        }

        SpriteRenderer prefabRenderer = squarePrefab != null
            ? squarePrefab.GetComponentInChildren<SpriteRenderer>()
            : null;

        ghostRenderer.sprite = sprite != null
            ? sprite
            : prefabRenderer != null ? prefabRenderer.sprite : null;
    }

    /// <summary>Selects the prefab and price used for future placements.</summary>
    public void SetSelectedTower(GameObject towerPrefab, Sprite previewSprite, int price)
    {
        squarePrefab = towerPrefab;
        selectedTowerPrice = Mathf.Max(0, price);
        SetGhostSprite(previewSprite);
    }

    public void SetTowerShop(TowerShopUI shop)
    {
        towerShop = shop;
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

        if (!CanPlaceAt(cellPosition))
        {
            return;
        }

        if (towerShop != null && !towerShop.TrySpend(selectedTowerPrice))
        {
            return;
        }

        Instantiate(squarePrefab, cellPosition, Quaternion.identity);
    }

    private bool CanPlaceAt(Vector2 cellPosition)
    {
        bool canAfford = towerShop == null || towerShop.CanAfford(selectedTowerPrice);
        return squarePrefab != null
            && canAfford
            && !IsCellOccupied(cellPosition)
            && HasAdjacentSquare(cellPosition);
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
        // Expanding beyond the proposed tower's edges detects contact anywhere
        // along a wide base, rather than only at four neighboring cell centers.
        Vector2 checkSize = Vector2.one * (cellSize + adjacencyTolerance * 2f);
        Collider2D[] hits = Physics2D.OverlapBoxAll(cellPosition, checkSize, 0f);

        foreach (Collider2D hit in hits)
        {
            if (hit == placementBase || hit.CompareTag("tower"))
            {
                return true;
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
