using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Places shop-configured towers on a square grid at the mouse cursor.
/// A tower can only be placed in an empty cell beside a Collider2D tagged "tower".
/// </summary>
public class SquarePlacement : MonoBehaviour
{
    [Header("Placement")]
    [SerializeField, Min(0.01f)] private float cellSize = 1f;
    [SerializeField] private Vector2 gridOrigin;
    [SerializeField] private TowerShopUI towerShop;

    [Header("Ghost")]
    [Tooltip("Runtime preview sprite supplied by the selected shop entry.")]
    [SerializeField] private Sprite ghostSprite;
    [SerializeField] private Color validGhostColor = new Color(1f, 1f, 1f, 0.5f);
    [SerializeField] private Color invalidGhostColor = new Color(1f, 0f, 0f, 0.5f);

    [Header("Collision")]
    [Tooltip("The starting base. It may be a BoxCollider2D of any width and does not need the tower tag.")]
    [SerializeField] private Collider2D placementBase;
    [SerializeField, Min(0.001f)] private float adjacencyTolerance = 0.05f;

    private Camera mainCamera;
    private GameObject ghostObject;
    private SpriteRenderer ghostRenderer;
    private TowerShopUI.TowerOffer selectedTower;

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

        ghostRenderer.sprite = ghostSprite;
        ghostRenderer.sortingOrder = 1;
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
    /// Changes the sprite used by the cursor ghost.
    /// </summary>
    public void SetGhostSprite(Sprite sprite)
    {
        ghostSprite = sprite;

        if (ghostRenderer == null)
        {
            CreateGhost();
        }

        ghostRenderer.sprite = sprite;
    }

    /// <summary>Selects the shop entry used for future placements.</summary>
    public void SetSelectedTower(TowerShopUI.TowerOffer offer)
    {
        selectedTower = offer;
        SetGhostSprite(offer != null ? offer.sprite : null);
    }

    public void SetTowerShop(TowerShopUI shop)
    {
        towerShop = shop;
    }

    private void TryPlaceAtCursor(Vector2 screenPosition)
    {
        if (selectedTower == null || towerShop == null)
        {
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

        if (!towerShop.TrySpend(selectedTower.price))
        {
            return;
        }

        towerShop.CreateTower(selectedTower, cellPosition, cellSize);
    }

    private bool CanPlaceAt(Vector2 cellPosition)
    {
        bool isSupportPiece = selectedTower != null
            && (selectedTower.script == TowerShopUI.TowerScript.CageTower
                || selectedTower.script == TowerShopUI.TowerScript.Scaffolding);

        return selectedTower != null
            && towerShop != null
            && towerShop.CanAfford(selectedTower.price)
            && !IsCellOccupied(cellPosition)
            && HasAdjacentSquare(cellPosition)
            && (isSupportPiece || HasCageDirectlyBelow(cellPosition));
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
        Collider2D[] hits = Physics2D.OverlapBoxAll(cellPosition, checkSize, 0f);

        foreach (Collider2D hit in hits)
        {
            if (IsTowerOrCageCenteredAt(hit, cellPosition) || IsPlayer(hit))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasCageDirectlyBelow(Vector2 cellPosition)
    {
        Vector2 belowCenter = cellPosition + Vector2.down * cellSize;
        float probeSize = Mathf.Max(cellSize * 0.1f, adjacencyTolerance * 2f);
        Collider2D[] hits = Physics2D.OverlapBoxAll(
            belowCenter,
            Vector2.one * probeSize,
            0f
        );

        foreach (Collider2D hit in hits)
        {
            CageTower cage = !hit.isTrigger ? hit.GetComponentInParent<CageTower>() : null;
            if (cage != null && IsCenteredOnCell(cage.transform.position, belowCenter))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasAdjacentSquare(Vector2 cellPosition)
    {
        int wallLayer = LayerMask.NameToLayer("Wall");
        if (wallLayer < 0)
        {
            return false;
        }

        Vector2[] cardinalDirections =
        {
            Vector2.up,
            Vector2.down,
            Vector2.left,
            Vector2.right
        };

        float probeSize = Mathf.Max(cellSize * 0.1f, adjacencyTolerance * 2f);

        foreach (Vector2 direction in cardinalDirections)
        {
            Vector2 neighborCenter = cellPosition + direction * cellSize;
            Collider2D[] hits = Physics2D.OverlapBoxAll(
                neighborCenter,
                Vector2.one * probeSize,
                0f
            );

            foreach (Collider2D hit in hits)
            {
                if (hit.gameObject.layer == wallLayer)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsTowerOrCageCenteredAt(Collider2D hit, Vector2 cellCenter)
    {
        Transform current = hit.transform;
        while (current != null)
        {
            if (current.CompareTag("tower") || current.CompareTag("cage"))
            {
                return IsCenteredOnCell(current.position, cellCenter);
            }

            current = current.parent;
        }

        return false;
    }

    private bool IsCenteredOnCell(Vector2 objectPosition, Vector2 cellCenter)
    {
        float centerTolerance = Mathf.Max(0.001f, adjacencyTolerance);
        return (objectPosition - cellCenter).sqrMagnitude
            <= centerTolerance * centerTolerance;
    }

    private static bool IsPlayer(Collider2D hit)
    {
        Transform current = hit.transform;

        while (current != null)
        {
            if (current.CompareTag("Player"))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(gridOrigin, Vector3.one * cellSize);
    }

    private void OnDisable()
    {
        if (ghostObject != null)
            Destroy(ghostObject);
    }
}
