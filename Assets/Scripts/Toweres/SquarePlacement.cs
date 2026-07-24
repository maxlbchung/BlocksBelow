using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Places shop-configured towers on a square grid at the mouse cursor.
/// Cells overlapping or below the ground are unbuildable. A piece must rest on
/// the ground or sit beside an existing tower, cage, or scaffold.
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
    private float groundSurfaceY = float.NegativeInfinity;
    private GameObject ghostObject;
    private SpriteRenderer ghostRenderer;
    private GameObject ghostAimIndicator;
    private TowerShopUI.TowerOffer selectedTower;
    private int rotationSteps;

    private Quaternion CurrentRotation => Quaternion.Euler(0f, 0f, -90f * rotationSteps);

    private void Awake()
    {
        mainCamera = Camera.main;
        if (towerShop == null)
        {
            towerShop = FindFirstObjectByType<TowerShopUI>();
        }

        GameObject ground = GameObject.FindWithTag("Ground");
        if (ground != null && ground.TryGetComponent(out Collider2D groundCollider))
        {
            groundSurfaceY = groundCollider.bounds.max.y;
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

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
        {
            HandleRotationInput(cursorPosition);
        }

        if (mouse.leftButton.wasPressedThisFrame
            && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
        {
            TryPlaceAtCursor(cursorPosition);
        }
    }

    /// <summary>
    /// Rotates the directional tower under the cursor a quarter turn clockwise,
    /// or the placement ghost when no rotatable tower is hovered.
    /// </summary>
    private void HandleRotationInput(Vector2 screenPosition)
    {
        if (mainCamera == null)
        {
            return;
        }

        Vector2 cellPosition = SnapToGrid(mainCamera.ScreenToWorldPoint(screenPosition));
        if (TryRotateTowerAt(cellPosition))
        {
            return;
        }

        if (selectedTower != null && TowerShopUI.IsRotatable(selectedTower.script))
        {
            rotationSteps = (rotationSteps + 1) % 4;
            ApplyGhostRotation();
        }
    }

    private bool TryRotateTowerAt(Vector2 cellPosition)
    {
        Vector2 checkSize = Vector2.one * (cellSize * 0.9f);
        Collider2D[] hits = Physics2D.OverlapBoxAll(cellPosition, checkSize, 0f);

        foreach (Collider2D hit in hits)
        {
            Transform current = hit.transform;
            while (current != null)
            {
                if (current.CompareTag("tower"))
                {
                    if (IsCenteredOnCell(current.position, cellPosition) && IsRotatableTower(current))
                    {
                        current.Rotate(0f, 0f, -90f);
                        return true;
                    }

                    break;
                }

                current = current.parent;
            }
        }

        return false;
    }

    private static bool IsRotatableTower(Transform tower)
    {
        return tower.GetComponent<BasicTower>() != null
            || tower.GetComponent<ShotgunTower>() != null
            || tower.GetComponent<FanTower>() != null;
    }

    private void ApplyGhostRotation()
    {
        if (ghostObject != null)
        {
            ghostObject.transform.localRotation = CurrentRotation;
        }
    }

    private void CreateGhost()
    {
        if (ghostObject != null)
        {
            Destroy(ghostObject);
            ghostAimIndicator = null;
        }

        ghostObject = new GameObject("Square Placement Ghost");
        ghostObject.transform.SetParent(transform);
        ghostObject.SetActive(false);

        ghostRenderer = ghostObject.AddComponent<SpriteRenderer>();
        ghostRenderer.color = invalidGhostColor;

        ghostRenderer.sprite = ghostSprite;
        ghostRenderer.sortingOrder = 1;

        ApplyGhostRotation();
        RefreshGhostAimIndicator();
    }

    private void RefreshGhostAimIndicator()
    {
        if (ghostObject == null)
        {
            return;
        }

        bool showIndicator = selectedTower != null && TowerShopUI.IsRotatable(selectedTower.script);
        if (!showIndicator)
        {
            if (ghostAimIndicator != null)
            {
                ghostAimIndicator.SetActive(false);
            }

            return;
        }

        if (ghostAimIndicator == null)
        {
            ghostAimIndicator = TowerShopUI.CreateAimIndicator(
                ghostObject.transform,
                selectedTower.script,
                cellSize);
        }
        else
        {
            ghostAimIndicator.transform.localPosition =
                TowerShopUI.GetAimDirection(selectedTower.script) * (cellSize * 0.55f);
        }

        ghostAimIndicator.SetActive(true);
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

        if (offer == null || !TowerShopUI.IsRotatable(offer.script))
        {
            rotationSteps = 0;
        }

        ApplyGhostRotation();
        RefreshGhostAimIndicator();
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

        towerShop.CreateTower(selectedTower, cellPosition, cellSize, CurrentRotation);
    }

    private bool CanPlaceAt(Vector2 cellPosition)
    {
        bool isSupportPiece = selectedTower != null
            && (selectedTower.script == TowerShopUI.TowerScript.CageTower
                || selectedTower.script == TowerShopUI.TowerScript.Scaffolding);

        if (selectedTower == null
            || towerShop == null
            || !towerShop.CanAfford(selectedTower.price)
            || IsCellOccupied(cellPosition)
            || (!isSupportPiece && !HasCageDirectlyBelow(cellPosition)))
        {
            return false;
        }

        // A cell whose center is below the ground top would overlap or sit inside
        // the ground, so it is unbuildable. The row resting on the surface is fine.
        if (cellPosition.y < groundSurfaceY)
        {
            return false;
        }

        return HasAdjacentStructure(cellPosition) || HasGroundDirectlyBelow(cellPosition);
    }

    /// <summary>True when the cell directly below contains a Wall-layer collider (the ground).</summary>
    private bool HasGroundDirectlyBelow(Vector2 cellPosition)
    {
        int wallLayer = LayerMask.NameToLayer("Wall");
        if (wallLayer < 0)
        {
            return false;
        }

        float probeSize = Mathf.Max(cellSize * 0.1f, adjacencyTolerance * 2f);
        Collider2D[] hits = Physics2D.OverlapBoxAll(
            cellPosition + Vector2.down * cellSize,
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

        return false;
    }

    private Vector2 SnapToGrid(Vector2 worldPosition)
    {
        float x = Mathf.Round((worldPosition.x - gridOrigin.x) / cellSize) * cellSize;
        float y = Mathf.Round((worldPosition.y - gridOrigin.y) / cellSize) * cellSize;
        return gridOrigin + new Vector2(x, y);
    }

    private bool IsCellOccupied(Vector2 cellPosition)
    {
        // Scaffolding is a walk-through support piece, so the player's cell stays placeable for it.
        bool ignorePlayer = selectedTower != null
            && selectedTower.script == TowerShopUI.TowerScript.Scaffolding;

        // Slightly smaller than the cell so squares touching at their edges do not count as overlap.
        Vector2 checkSize = Vector2.one * (cellSize * 0.9f);
        Collider2D[] hits = Physics2D.OverlapBoxAll(cellPosition, checkSize, 0f);

        foreach (Collider2D hit in hits)
        {
            if (IsTowerOrCageCenteredAt(hit, cellPosition) || (!ignorePlayer && IsPlayer(hit)))
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

    private static readonly Vector2[] CardinalDirections =
    {
        Vector2.up,
        Vector2.down,
        Vector2.left,
        Vector2.right
    };

    private bool HasAdjacentStructure(Vector2 cellPosition)
    {
        float probeSize = Mathf.Max(cellSize * 0.1f, adjacencyTolerance * 2f);

        foreach (Vector2 direction in CardinalDirections)
        {
            Vector2 neighborCenter = cellPosition + direction * cellSize;
            Collider2D[] hits = Physics2D.OverlapBoxAll(
                neighborCenter,
                Vector2.one * probeSize,
                0f
            );

            foreach (Collider2D hit in hits)
            {
                // Only a tower, cage, or scaffold occupying the neighbor cell counts.
                // Children like wind funnels or orbiting saws fail the centered check.
                if (IsTowerOrCageCenteredAt(hit, neighborCenter))
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
