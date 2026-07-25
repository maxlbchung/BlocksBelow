using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws white energy climbing into a tower from the cages that power it, so a
/// powered tower reads as powered at a glance. One column is drawn, running from
/// the lowest full cage all the way up, so a deeper stack shows a longer climb
/// rather than a second stream.
/// <para>
/// <see cref="TowerCageStack"/> attaches this at runtime, so no prefab needs to
/// carry it. Adding it to a prefab by hand is still fine - the auto-attach skips
/// a tower that already has one, so the Inspector values are kept.
/// </para>
/// </summary>
[RequireComponent(typeof(TowerCageStack))]
public class TowerPowerFlow : MonoBehaviour
{
    [SerializeField] private Color energyColor = new Color(1f, 1f, 1f, 0.45f);
    [Tooltip("Grid cells the energy climbs per second.")]
    [SerializeField, Min(0f)] private float riseSpeed = 1.2f;
    [Tooltip("Streak segments per grid cell. Lower makes each streak longer.")]
    [SerializeField, Min(0.1f)] private float streakDetail = 1.2f;
    [Tooltip("Column width as a fraction of one grid cell.")]
    [SerializeField, Range(0.05f, 1.5f)] private float columnWidth = 1f;
    [SerializeField, Range(0f, 1f)] private float streakSpread = 0.5f;
    [Tooltip("How far the streaks snake left and right as they rise.")]
    [SerializeField, Min(0f)] private float waviness = 1f;
    [Tooltip("Strength of the broad haze the streaks travel through.")]
    [SerializeField, Range(0f, 1f)] private float wideGlow = 0.4f;
    [SerializeField, Min(0f)] private float fadeInTime = 0.35f;
    [SerializeField, Min(0f)] private float fadeOutTime = 0.25f;
    [SerializeField] private string sortingLayerName = "Towers";
    [SerializeField] private int sortingOrder = -11;

    private static readonly int EnergyColorId = Shader.PropertyToID("_EnergyColor");
    private static readonly int SpeedId = Shader.PropertyToID("_Speed");
    private static readonly int DetailId = Shader.PropertyToID("_Detail");
    private static readonly int SpreadId = Shader.PropertyToID("_Spread");
    private static readonly int SwayId = Shader.PropertyToID("_Sway");
    private static readonly int GlowId = Shader.PropertyToID("_Glow");
    private static readonly int ColumnFadeId = Shader.PropertyToID("_ColumnFade");
    private static readonly int StrengthId = Shader.PropertyToID("_Strength");
    private static readonly int NowId = Shader.PropertyToID("_Now");

    private static readonly Vector2[] QuadUVs =
    {
        new Vector2(0f, 0f),
        new Vector2(1f, 0f),
        new Vector2(0f, 1f),
        new Vector2(1f, 1f)
    };

    private static readonly int[] QuadTriangles = { 0, 2, 1, 2, 3, 1 };

    private readonly Vector3[] vertices = new Vector3[4];
    private readonly Vector4[] columnData = new Vector4[4];

    private TowerCageStack cageStack;
    private CageTower lowestPoweredCage;
    private Transform flowTransform;
    private MeshRenderer flowRenderer;
    private Mesh flowMesh;
    private Material flowMaterial;
    private int columnSignature;
    private bool hasColumn;
    private float strength;

    private void Start()
    {
        cageStack = GetComponent<TowerCageStack>();
        CreateFlowVisual();
    }

    private void LateUpdate()
    {
        // Runs late so the cage states this reads are the ones the towers acted on
        // this frame rather than last frame's.
        if (flowRenderer == null || cageStack == null)
        {
            return;
        }

        // Left unrotated, so a tower placed sideways still lifts its energy straight up.
        flowTransform.rotation = Quaternion.identity;

        FindLowestPoweredCage();

        float target = lowestPoweredCage != null ? 1f : 0f;
        float rampTime = target > strength ? fadeInTime : fadeOutTime;
        strength = rampTime > 0f
            ? Mathf.MoveTowards(strength, target, Time.deltaTime / rampTime)
            : target;

        RefreshColumn();

        bool visible = strength > 0f && hasColumn;
        if (flowRenderer.enabled != visible)
        {
            flowRenderer.enabled = visible;
        }

        if (!visible)
        {
            return;
        }

        flowMaterial.SetFloat(StrengthId, strength);
        // The shader animates off this instead of _Time so the column fade-in
        // measures against the same clock the mesh was stamped with.
        flowMaterial.SetFloat(NowId, Time.timeSinceLevelLoad);
    }

    /// <summary>
    /// The cages run downward from the tower, so the last full one is the deepest.
    /// That is where the climb starts; the full cages above it are already on the
    /// way up and do not each get their own column.
    /// </summary>
    private void FindLowestPoweredCage()
    {
        lowestPoweredCage = null;
        IReadOnlyList<CageTower> cages = cageStack.CagesBelow;

        for (int i = 0; i < cages.Count; i++)
        {
            CageTower cage = cages[i];
            if (cage != null && cage.State == CageTower.CageState.Full)
            {
                lowestPoweredCage = cage;
            }
        }
    }

    /// <summary>
    /// Rebuilds the mesh only when the cage the column starts from changed, so a
    /// cage filling higher up the stack does not restart the effect.
    /// </summary>
    private void RefreshColumn()
    {
        int signature = lowestPoweredCage != null ? lowestPoweredCage.GetInstanceID() : 0;
        if (signature == columnSignature)
        {
            return;
        }

        // Losing the last cage leaves nothing to build from, so the column that is
        // already on screen is kept until it has finished fading out.
        if (lowestPoweredCage == null && strength > 0f)
        {
            return;
        }

        columnSignature = signature;
        BuildColumn();
    }

    private void BuildColumn()
    {
        flowMesh.Clear();
        hasColumn = false;

        if (lowestPoweredCage == null)
        {
            return;
        }

        Vector3 towerPosition = transform.position;
        Vector3 cagePosition = lowestPoweredCage.transform.position;
        float bottom = cagePosition.y - towerPosition.y;

        // A cage level with the tower would give a zero-height quad.
        if (bottom > -0.01f)
        {
            return;
        }

        float cellSize = GetCellSize();
        float halfWidth = cellSize * columnWidth * 0.5f;
        float centreX = cagePosition.x - towerPosition.x;
        // The quad's own coordinates are world-aligned offsets from the tower,
        // which holds because the visual is kept unrotated.
        vertices[0] = new Vector3(centreX - halfWidth, bottom, 0f);
        vertices[1] = new Vector3(centreX + halfWidth, bottom, 0f);
        vertices[2] = new Vector3(centreX - halfWidth, 0f, 0f);
        vertices[3] = new Vector3(centreX + halfWidth, 0f, 0f);

        Vector4 column = new Vector4(
            -bottom / cellSize,
            ColumnSeed(cagePosition),
            Time.timeSinceLevelLoad,
            0f);

        for (int corner = 0; corner < columnData.Length; corner++)
        {
            columnData[corner] = column;
        }

        flowMesh.SetVertices(vertices);
        flowMesh.SetUVs(0, QuadUVs);
        flowMesh.SetUVs(1, columnData);
        flowMesh.SetTriangles(QuadTriangles, 0);
        flowMesh.RecalculateBounds();
        hasColumn = true;
    }

    /// <summary>
    /// The stack sits one cell apart by construction, so the nearest cage below
    /// measures the grid without the tower having to be told the cell size.
    /// </summary>
    private float GetCellSize()
    {
        IReadOnlyList<CageTower> cages = cageStack.CagesBelow;
        if (cages.Count > 0 && cages[0] != null)
        {
            float spacing = transform.position.y - cages[0].transform.position.y;
            if (spacing > 0.01f)
            {
                return spacing;
            }
        }

        return 1f;
    }

    /// <summary>Keyed off the cage's position so a column keeps its look across rebuilds.</summary>
    private static float ColumnSeed(Vector3 cagePosition)
    {
        float raw = Mathf.Sin(cagePosition.x * 12.9898f + cagePosition.y * 78.233f) * 43758.5453f;
        return raw - Mathf.Floor(raw);
    }

    private void CreateFlowVisual()
    {
        Shader flowShader = Shader.Find("TowerDefense/PowerFlow");
        if (flowShader == null)
        {
            Debug.LogWarning("The TowerDefense/PowerFlow shader could not be found.", this);
            return;
        }

        GameObject visual = new GameObject("Power Flow");
        visual.transform.SetParent(transform, false);
        flowTransform = visual.transform;

        flowMesh = new Mesh { name = "Power Flow Mesh" };
        flowMesh.MarkDynamic();
        visual.AddComponent<MeshFilter>().sharedMesh = flowMesh;

        flowRenderer = visual.AddComponent<MeshRenderer>();
        flowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        flowRenderer.receiveShadows = false;
        flowRenderer.sortingLayerName = sortingLayerName;
        flowRenderer.sortingOrder = sortingOrder;
        flowRenderer.enabled = false;

        flowMaterial = new Material(flowShader) { name = "Power Flow Material" };
        flowRenderer.sharedMaterial = flowMaterial;
        ApplyMaterialSettings();
    }

    private void ApplyMaterialSettings()
    {
        if (flowMaterial == null)
        {
            return;
        }

        flowMaterial.SetColor(EnergyColorId, energyColor);
        flowMaterial.SetFloat(SpeedId, riseSpeed);
        flowMaterial.SetFloat(DetailId, streakDetail);
        flowMaterial.SetFloat(SpreadId, streakSpread);
        flowMaterial.SetFloat(SwayId, waviness);
        flowMaterial.SetFloat(GlowId, wideGlow);
        flowMaterial.SetFloat(ColumnFadeId, fadeInTime);
        flowMaterial.SetFloat(StrengthId, strength);
    }

    private void OnValidate()
    {
        // Lets the look be dialled in while the game is running.
        ApplyMaterialSettings();

        if (flowRenderer != null)
        {
            flowRenderer.sortingLayerName = sortingLayerName;
            flowRenderer.sortingOrder = sortingOrder;
            columnSignature = 0;
        }
    }

    private void OnDestroy()
    {
        if (flowMaterial != null)
        {
            Destroy(flowMaterial);
        }

        if (flowMesh != null)
        {
            Destroy(flowMesh);
        }
    }
}
