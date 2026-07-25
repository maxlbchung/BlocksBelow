using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cell-indexed record of every tower, cage, and scaffold on the build grid.
/// <para>
/// Placement used to answer "what is in this cell?" with a <c>Physics2D.OverlapBoxAll</c> per
/// question - seven of them per frame while the ghost followed the cursor, each allocating a
/// <see cref="Collider2D"/> array. Pieces never move once placed, so the answer is worth
/// recording at placement time instead of re-deriving it from colliders every frame.
/// </para>
/// <para>
/// Pieces arrive two ways and both are covered: shop placements and pre-placed offers go
/// through <see cref="TowerShopUI.CreateTower"/>, which registers them, and anything a
/// designer drops straight into the scene is picked up by <see cref="RebuildFromScene"/>.
/// </para>
/// </summary>
public static class TowerGrid
{
    /// <summary>Which tag a piece carries. Rotation only applies to towers, not cages.</summary>
    public enum PieceKind
    {
        Tower,
        Cage
    }

    public readonly struct Occupant
    {
        /// <summary>The tagged root, i.e. the transform placement treats as "the piece".</summary>
        public readonly Transform Root;

        /// <summary>The cage component, or null when this piece is not a cage.</summary>
        public readonly CageTower Cage;

        public readonly PieceKind Kind;

        /// <summary>
        /// World position recorded at registration. Queries re-check it against the cell centre
        /// so a piece sitting off-grid is rejected, exactly as the old centred-on-cell test did.
        /// </summary>
        public readonly Vector2 Position;

        public Occupant(Transform root, CageTower cage, PieceKind kind, Vector2 position)
        {
            Root = root;
            Cage = cage;
            Kind = kind;
            Position = position;
        }
    }

    private const string TowerTag = "tower";
    private const string CageTag = "cage";

    private static readonly Dictionary<Vector2Int, Occupant> cells =
        new Dictionary<Vector2Int, Occupant>(64);

    private static float cellSize = 1f;
    private static Vector2 origin = Vector2.zero;
    private static int version;

    /// <summary>
    /// Bumped whenever the contents change. Callers cache derived answers against this so they
    /// only recompute when something was actually placed.
    /// </summary>
    public static int Version => version;

    /// <summary>
    /// Sets the grid the registry keys against and empties it. Static state outlives a scene
    /// load, so this doubles as the per-scene reset; call it before registering anything.
    /// </summary>
    public static void Configure(float gridCellSize, Vector2 gridOrigin)
    {
        cellSize = Mathf.Max(0.01f, gridCellSize);
        origin = gridOrigin;
        cells.Clear();
        version++;
    }

    public static Vector2Int ToCell(Vector2 worldPosition)
    {
        return new Vector2Int(
            Mathf.RoundToInt((worldPosition.x - origin.x) / cellSize),
            Mathf.RoundToInt((worldPosition.y - origin.y) / cellSize));
    }

    public static Vector2 ToWorld(Vector2Int cell)
    {
        return origin + new Vector2(cell.x * cellSize, cell.y * cellSize);
    }

    /// <summary>
    /// Records a placed piece. Untagged objects are ignored, which matches the old collider
    /// walk - it only ever counted transforms tagged "tower" or "cage".
    /// </summary>
    public static void Register(GameObject piece)
    {
        if (piece == null)
        {
            return;
        }

        PieceKind kind;
        if (piece.CompareTag(TowerTag))
        {
            kind = PieceKind.Tower;
        }
        else if (piece.CompareTag(CageTag))
        {
            kind = PieceKind.Cage;
        }
        else
        {
            return;
        }

        Vector2 position = piece.transform.position;
        cells[ToCell(position)] = new Occupant(
            piece.transform,
            piece.GetComponent<CageTower>(),
            kind,
            position);
        version++;
    }

    public static void Unregister(GameObject piece)
    {
        if (piece == null)
        {
            return;
        }

        Vector2Int cell = ToCell(piece.transform.position);
        if (cells.TryGetValue(cell, out Occupant occupant) && occupant.Root == piece.transform)
        {
            cells.Remove(cell);
            version++;
        }
    }

    /// <summary>
    /// Seeds the registry from towers a designer placed directly in the scene, which never
    /// pass through the shop. Runs once at startup, not per frame.
    /// </summary>
    public static void RebuildFromScene()
    {
        RegisterTagged(TowerTag);
        RegisterTagged(CageTag);
    }

    private static void RegisterTagged(string tag)
    {
        GameObject[] tagged = GameObject.FindGameObjectsWithTag(tag);
        for (int i = 0; i < tagged.Length; i++)
        {
            Register(tagged[i]);
        }
    }

    /// <summary>
    /// The piece occupying <paramref name="cell"/>, if one is centred on it within
    /// <paramref name="centreTolerance"/>. Entries whose object was destroyed are dropped.
    /// </summary>
    public static bool TryGet(Vector2Int cell, float centreTolerance, out Occupant occupant)
    {
        if (!cells.TryGetValue(cell, out occupant))
        {
            return false;
        }

        if (occupant.Root == null)
        {
            cells.Remove(cell);
            version++;
            occupant = default;
            return false;
        }

        float tolerance = Mathf.Max(0.001f, centreTolerance);
        if ((occupant.Position - ToWorld(cell)).sqrMagnitude > tolerance * tolerance)
        {
            occupant = default;
            return false;
        }

        return true;
    }

    /// <summary>The cage occupying <paramref name="cell"/>, or null when it holds no cage.</summary>
    public static CageTower GetCage(Vector2Int cell, float centreTolerance)
    {
        return TryGet(cell, centreTolerance, out Occupant occupant) ? occupant.Cage : null;
    }

    /// <summary>
    /// Fills <paramref name="results"/> with every registered cage. Order is unspecified, so
    /// callers that care about position should read it from the cage itself.
    /// </summary>
    public static void CollectCages(List<CageTower> results)
    {
        results.Clear();

        foreach (KeyValuePair<Vector2Int, Occupant> entry in cells)
        {
            if (entry.Value.Cage != null)
            {
                results.Add(entry.Value.Cage);
            }
        }
    }

    public static bool IsOccupied(Vector2Int cell, float centreTolerance)
    {
        return TryGet(cell, centreTolerance, out _);
    }
}
