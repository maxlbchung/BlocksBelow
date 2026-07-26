using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Places shop-configured towers on a square grid at the mouse cursor.
/// Cells overlapping or below the ground are unbuildable. A piece must rest on
/// the ground or sit beside an existing tower, cage, or scaffold.
/// <para>
/// Cell occupancy is read from <see cref="TowerGrid"/> rather than from physics overlaps.
/// Pieces do not move once placed, so recording them at placement time replaces the seven
/// allocating <c>OverlapBoxAll</c> calls this ran on every frame the ghost was visible.
/// </para>
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

    [Header("Repair")]
    [Tooltip("Overlay drawn on every broken cage while repair mode is active.")]
    [SerializeField] private Color repairHighlightColor = new Color(0.3f, 0.9f, 0.45f, 0.25f);
    [Tooltip("Overlay drawn on the broken cage the cursor is over, i.e. the one a click repairs.")]
    [SerializeField] private Color repairHoverColor = new Color(0.45f, 1f, 0.6f, 0.5f);
    [Tooltip("Pulses per second for the markers on the broken cages the cursor is not over.")]
    [SerializeField, Min(0f)] private float repairPulseSpeed = 1.5f;

    [Header("Collision")]
    [Tooltip("The starting base. It may be a BoxCollider2D of any width and does not need the tower tag.")]
    [SerializeField] private Collider2D placementBase;
    [SerializeField, Min(0.001f)] private float adjacencyTolerance = 0.05f;

    [Header("Pre Placed Towers")]
    [SerializeField] private Transform[] prePlacedTowersPosition;
    [SerializeField] private TowerShopUI.TowerOffer[] prePlacedTowers;

    private Camera mainCamera;
    private float groundSurfaceY = float.NegativeInfinity;
    private GameObject ghostObject;
    private SpriteRenderer ghostRenderer;
    private GameObject ghostAimIndicator;
    private SpriteRenderer ghostAimRenderer;
    private static Sprite repairHighlightSprite;
    private TowerShopUI.TowerOffer selectedTower;
    private int rotationSteps;
    private Collider2D playerCollider;

    // One marker per broken cage, so repair mode shows every cage that needs paying for
    // rather than only the one under the cursor. Markers are pooled: the count only grows
    // to the largest number of cages broken at once.
    private readonly List<SpriteRenderer> repairHighlights = new List<SpriteRenderer>(8);
    private readonly List<CageTower> cages = new List<CageTower>(16);
    private int cageCacheVersion = -1;

    // The ground probe is the one question the tower registry cannot answer: it reads world
    // geometry (terrain, the starting base) rather than placed pieces. Terrain never moves, and
    // the only Wall-layer colliders that appear at runtime are placed pieces, so the answer is
    // cached per cell and thrown away whenever the registry changes.
    private readonly Dictionary<Vector2Int, bool> groundBelowCache = new Dictionary<Vector2Int, bool>(64);
    private readonly List<Collider2D> probeResults = new List<Collider2D>(8);
    private ContactFilter2D wallFilter;
    private bool wallFilterReady;
    private int groundCacheVersion = -1;

    // The ghost's validity only changes when the hovered cell, the grid contents, the wallet,
    // or the selection changes, so it is recomputed on those rather than every frame.
    private Vector2Int lastGhostCell;
    private int lastGhostGridVersion = -1;
    private int lastGhostEnergy = -1;
    private TowerShopUI.TowerOffer lastGhostOffer;
    private bool lastGhostValid;
    private bool hasGhostResult;

    /// <summary>Width of one grid cell in world units.</summary>
    public float CellSize => cellSize;

    private bool IsRepairing => towerShop != null && towerShop.RepairMode;

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

        GameObject playerObject = GameObject.FindWithTag("Player");
        if (playerObject != null)
        {
            playerCollider = playerObject.GetComponent<Collider2D>();
        }

        // Configure clears the registry, which also resets the statics a scene reload leaves
        // behind. The scene scan then picks up towers a designer placed by hand; anything the
        // shop builds after this - including the pre-placed offers below - registers itself.
        TowerGrid.Configure(cellSize, gridOrigin);
        TowerGrid.RebuildFromScene();

        CreateGhost();

        for (int i = 0; i < prePlacedTowersPosition.Length; i++)
        {
            towerShop.CreateTower(prePlacedTowers[i], SnapToGrid(prePlacedTowersPosition[i].position), cellSize, CurrentRotation);
            if (prePlacedTowers[i] != null
                && string.Equals(
                    prePlacedTowers[i].displayName,
                    "Cage",
                    System.StringComparison.OrdinalIgnoreCase))
            {
                AudioController.Play("Whoosh");
            }
        }

        // A tower maps the cages under it as it is placed, so one listed above its own
        // cages would come up unpowered. Re-mapping once the whole list is down lets the
        // pre-placed entries be in any order.
        RemapPlacedCageStacks();
    }

    private void RemapPlacedCageStacks()
    {
        TowerCageStack[] stacks = FindObjectsByType<TowerCageStack>(FindObjectsSortMode.None);
        for (int i = 0; i < stacks.Length; i++)
        {
            stacks[i].Initialize(cellSize);
        }
    }

    private void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            SetGhostVisible(false);
            HideRepairHighlights();
            return;
        }

        Vector2 cursorPosition = mouse.position.ReadValue();
        bool repairing = IsRepairing;

        if (repairing)
        {
            SetGhostVisible(false);
            UpdateRepairHighlights(cursorPosition);
        }
        else
        {
            HideRepairHighlights();
            UpdateGhost(cursorPosition);
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
        {
            HandleRotationInput(cursorPosition);
        }

        if (mouse.leftButton.wasPressedThisFrame
            && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
        {
            if (repairing)
            {
                TryRepairAtCursor(cursorPosition);
            }
            else
            {
                TryPlaceAtCursor(cursorPosition);
            }
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

        if (TowerShopUI.IsRotatable(selectedTower))
        {
            rotationSteps = (rotationSteps + 1) % 4;
            ApplyGhostRotation();
        }
    }

    private bool TryRotateTowerAt(Vector2 cellPosition)
    {
        if (!TowerGrid.TryGet(TowerGrid.ToCell(cellPosition), adjacencyTolerance, out TowerGrid.Occupant occupant)
            || occupant.Kind != TowerGrid.PieceKind.Tower
            || !IsRotatableTower(occupant.Root))
        {
            return false;
        }

        occupant.Root.Rotate(0f, 0f, -90f);
        return true;
    }

    private static bool IsRotatableTower(Transform tower)
    {
        // Prefab-built towers declare this themselves; the component checks cover
        // towers still being assembled in code.
        TowerPlacementInfo info = tower.GetComponent<TowerPlacementInfo>();
        if (info != null)
        {
            return info.Rotatable;
        }

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
            ghostAimRenderer = null;
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

        bool showIndicator = TowerShopUI.IsRotatable(selectedTower);
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
                TowerShopUI.GetAimDirection(selectedTower),
                cellSize);
            ghostAimRenderer = ghostAimIndicator.GetComponent<SpriteRenderer>();
        }
        else
        {
            TowerShopUI.PointAimIndicator(
                ghostAimIndicator.transform,
                TowerShopUI.GetAimDirection(selectedTower),
                cellSize);
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
        ghostRenderer.color = IsGhostCellPlaceable(cellPosition) ? validGhostColor : invalidGhostColor;
        MatchAimIndicatorAlpha();
        SetGhostVisible(true);
    }

    /// <summary>
    /// The aim arrow is part of the preview, so it fades with it. Only the alpha is
    /// copied - the arrow keeps its white so it stays readable over the red the ghost
    /// turns on an unbuildable cell.
    /// </summary>
    private void MatchAimIndicatorAlpha()
    {
        if (ghostAimRenderer == null)
        {
            return;
        }

        Color arrowColor = ghostAimRenderer.color;
        arrowColor.a = ghostRenderer.color.a;
        ghostAimRenderer.color = arrowColor;
    }

    /// <summary>
    /// <see cref="CanPlaceAt"/> behind a cache. Its answer only moves when the hovered cell,
    /// the grid contents, the wallet, or the selected offer changes - not once per frame - and
    /// the player's own position, which is why that check is re-run every time.
    /// </summary>
    private bool IsGhostCellPlaceable(Vector2 cellPosition)
    {
        Vector2Int cell = TowerGrid.ToCell(cellPosition);
        int energy = towerShop != null ? towerShop.Energy : 0;

        if (hasGhostResult
            && cell == lastGhostCell
            && lastGhostGridVersion == TowerGrid.Version
            && lastGhostEnergy == energy
            && ReferenceEquals(lastGhostOffer, selectedTower))
        {
            // The player walks around while the cursor holds still, so this one stays live.
            return lastGhostValid
                && !(!TowerShopUI.IsWalkThrough(selectedTower) && IsPlayerInCell(cellPosition));
        }

        lastGhostCell = cell;
        lastGhostGridVersion = TowerGrid.Version;
        lastGhostEnergy = energy;
        lastGhostOffer = selectedTower;
        hasGhostResult = true;

        // Cached without the player term so the live check above can layer on top of it.
        lastGhostValid = CanPlaceAt(cellPosition, ignorePlayer: true);
        return lastGhostValid
            && !(!TowerShopUI.IsWalkThrough(selectedTower) && IsPlayerInCell(cellPosition));
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
        SetGhostSprite(TowerShopUI.GetOfferSprite(offer));

        if (!TowerShopUI.IsRotatable(offer))
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

        if (towerShop.CreateTower(selectedTower, cellPosition, cellSize, CurrentRotation) != null)
        {
            RunStats.RecordTowerPlaced();
        }
    }

    /// <summary>Repairs the broken cage in the cell under the cursor, if there is one.</summary>
    private void TryRepairAtCursor(Vector2 screenPosition)
    {
        if (towerShop == null)
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

        Vector2 cellPosition = SnapToGrid(mainCamera.ScreenToWorldPoint(screenPosition));
        CageTower cage = FindBrokenCageAt(cellPosition);
        if (cage != null)
        {
            towerShop.TryRepairCage(cage);
        }
    }

    private CageTower FindBrokenCageAt(Vector2 cellPosition)
    {
        CageTower cage = TowerGrid.GetCage(TowerGrid.ToCell(cellPosition), adjacencyTolerance);
        return cage != null && cage.IsBroken ? cage : null;
    }

    /// <summary>
    /// Marks every broken cage while repair mode is active, so a cage that needs paying for
    /// can be found without hunting the tower for changed artwork, and brightens the one
    /// under the cursor - the one a click would actually repair.
    /// </summary>
    private void UpdateRepairHighlights(Vector2 screenPosition)
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        if (mainCamera == null)
        {
            HideRepairHighlights();
            return;
        }

        Vector2 cellPosition = SnapToGrid(mainCamera.ScreenToWorldPoint(screenPosition));
        CageTower hoveredCage = FindBrokenCageAt(cellPosition);
        Color pulsedColor = PulseRepairColor(repairHighlightColor);
        int markerCount = 0;

        RefreshCageList();
        for (int i = 0; i < cages.Count; i++)
        {
            CageTower cage = cages[i];
            if (cage == null || !cage.IsBroken)
            {
                continue;
            }

            SpriteRenderer highlight = GetRepairHighlight(markerCount++);
            highlight.transform.position = cage.transform.position;
            highlight.color = cage == hoveredCage ? repairHoverColor : pulsedColor;
            if (!highlight.gameObject.activeSelf)
            {
                highlight.gameObject.SetActive(true);
            }
        }

        HideRepairHighlights(markerCount);
    }

    /// <summary>
    /// Which cages exist only changes when something is placed, so the list is rebuilt against
    /// the registry version instead of every frame. Whether each one is broken is read live.
    /// </summary>
    private void RefreshCageList()
    {
        if (cageCacheVersion == TowerGrid.Version)
        {
            return;
        }

        cageCacheVersion = TowerGrid.Version;
        TowerGrid.CollectCages(cages);
    }

    /// <summary>Fades the marker in and out so a broken cage reads as wanting attention.</summary>
    private Color PulseRepairColor(Color color)
    {
        float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * repairPulseSpeed * Mathf.PI * 2f);
        color.a *= Mathf.Lerp(0.4f, 1f, wave);
        return color;
    }

    private SpriteRenderer GetRepairHighlight(int index)
    {
        while (repairHighlights.Count <= index)
        {
            GameObject highlight = new GameObject("Cage Repair Highlight");
            highlight.transform.SetParent(transform);
            highlight.transform.localScale = Vector3.one * cellSize;
            highlight.SetActive(false);

            SpriteRenderer highlightRenderer = highlight.AddComponent<SpriteRenderer>();
            highlightRenderer.sprite = GetRepairHighlightSprite();
            highlightRenderer.color = repairHighlightColor;
            highlightRenderer.sortingLayerName = "Towers";
            highlightRenderer.sortingOrder = 2;
            repairHighlights.Add(highlightRenderer);
        }

        return repairHighlights[index];
    }

    /// <param name="firstIndex">Markers from here on are spares this frame did not need.</param>
    private void HideRepairHighlights(int firstIndex = 0)
    {
        for (int i = firstIndex; i < repairHighlights.Count; i++)
        {
            if (repairHighlights[i].gameObject.activeSelf)
            {
                repairHighlights[i].gameObject.SetActive(false);
            }
        }
    }

    private static Sprite GetRepairHighlightSprite()
    {
        if (repairHighlightSprite == null)
        {
            Texture2D texture = Texture2D.whiteTexture;
            repairHighlightSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                texture.width);
        }

        return repairHighlightSprite;
    }

    /// <param name="ignorePlayer">
    /// Skips the player-overlap term so the ghost can cache the static part of the answer and
    /// re-test the player separately. Placement itself always passes false.
    /// </param>
    private bool CanPlaceAt(Vector2 cellPosition, bool ignorePlayer = false)
    {
        bool isSupportPiece = TowerShopUI.IsSupportPiece(selectedTower);

        if (selectedTower == null
            || towerShop == null
            || !towerShop.CanAfford(selectedTower.price)
            || IsCellOccupied(cellPosition, ignorePlayer)
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

    /// <summary>
    /// True when the cell directly below holds something solid to rest on, which in
    /// practice means the ground or the starting base.
    /// <para>
    /// This stays a physics query because it reads world geometry, not placed pieces - the
    /// terrain and the starting base are never in the tower registry. The result is memoised
    /// per cell and dropped whenever the registry changes, since scaffolds are also on the
    /// Wall layer and so a newly placed piece can change the answer.
    /// </para>
    /// </summary>
    private bool HasGroundDirectlyBelow(Vector2 cellPosition)
    {
        if (!EnsureWallFilter())
        {
            return false;
        }

        Vector2 belowCenter = cellPosition + Vector2.down * cellSize;
        Vector2Int belowCell = TowerGrid.ToCell(belowCenter);

        if (groundCacheVersion != TowerGrid.Version)
        {
            groundBelowCache.Clear();
            groundCacheVersion = TowerGrid.Version;
        }
        else if (groundBelowCache.TryGetValue(belowCell, out bool cached))
        {
            return cached;
        }

        float probeSize = Mathf.Max(cellSize * 0.1f, adjacencyTolerance * 2f);
        Physics2D.OverlapBox(belowCenter, Vector2.one * probeSize, 0f, wallFilter, probeResults);

        bool hasGround = false;
        for (int i = 0; i < probeResults.Count; i++)
        {
            // A cage's capture radius reaches well past its own cell, and a scaffold's
            // box is walk-through. Counting either as ground let pieces be placed
            // floating a cell away from a cage, with nothing underneath them.
            if (!probeResults[i].isTrigger)
            {
                hasGround = true;
                break;
            }
        }

        groundBelowCache[belowCell] = hasGround;
        return hasGround;
    }

    /// <summary>
    /// Builds the Wall-layer filter once. The filter replaces the old per-hit layer comparison
    /// and lets the query use the non-allocating overload.
    /// </summary>
    private bool EnsureWallFilter()
    {
        if (wallFilterReady)
        {
            return wallFilter.layerMask.value != 0;
        }

        wallFilterReady = true;
        int wallLayer = LayerMask.NameToLayer("Wall");
        wallFilter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = wallLayer >= 0 ? 1 << wallLayer : 0,
            useTriggers = true
        };

        return wallLayer >= 0;
    }

    private Vector2 SnapToGrid(Vector2 worldPosition)
    {
        float x = Mathf.Round((worldPosition.x - gridOrigin.x) / cellSize) * cellSize;
        float y = Mathf.Round((worldPosition.y - gridOrigin.y) / cellSize) * cellSize;
        return gridOrigin + new Vector2(x, y);
    }

    private bool IsCellOccupied(Vector2 cellPosition, bool ignorePlayer)
    {
        if (TowerGrid.IsOccupied(TowerGrid.ToCell(cellPosition), adjacencyTolerance))
        {
            return true;
        }

        // Scaffolding is a walk-through support piece, so the player's cell stays placeable for it.
        return !ignorePlayer
            && !TowerShopUI.IsWalkThrough(selectedTower)
            && IsPlayerInCell(cellPosition);
    }

    /// <summary>
    /// True when the player's body overlaps the cell. The player is the one mover placement
    /// cares about, so it is tested directly instead of through an overlap query.
    /// </summary>
    private bool IsPlayerInCell(Vector2 cellPosition)
    {
        if (playerCollider == null)
        {
            return false;
        }

        // Slightly smaller than the cell so squares touching at their edges do not count as overlap.
        float halfExtent = cellSize * 0.45f;
        Bounds playerBounds = playerCollider.bounds;
        return Mathf.Abs(playerBounds.center.x - cellPosition.x)
                <= halfExtent + playerBounds.extents.x
            && Mathf.Abs(playerBounds.center.y - cellPosition.y)
                <= halfExtent + playerBounds.extents.y;
    }

    private bool HasCageDirectlyBelow(Vector2 cellPosition)
    {
        Vector2 belowCenter = cellPosition + Vector2.down * cellSize;
        return TowerGrid.GetCage(TowerGrid.ToCell(belowCenter), adjacencyTolerance) != null;
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
        foreach (Vector2 direction in CardinalDirections)
        {
            // Only a tower, cage, or scaffold occupying the neighbor cell counts. The registry
            // holds tagged roots only, so children like wind funnels or orbiting saws are
            // never candidates in the first place.
            Vector2 neighborCenter = cellPosition + direction * cellSize;
            if (TowerGrid.IsOccupied(TowerGrid.ToCell(neighborCenter), adjacencyTolerance))
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

    private void OnDisable()
    {
        if (ghostObject != null)
            Destroy(ghostObject);

        for (int i = 0; i < repairHighlights.Count; i++)
        {
            if (repairHighlights[i] != null)
                Destroy(repairHighlights[i].gameObject);
        }

        repairHighlights.Clear();
    }
}
