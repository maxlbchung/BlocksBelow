using UnityEngine;

/// <summary>
/// Builds the swamp the island sits in: a murky water surface stretching down
/// from this transform, and the fog that rises off it. This object's own
/// position is the far waterline - the horizon where the swamp meets the
/// treeline - so it lines up with the painted background rather than with the
/// island's own base. Note that the background sprite is parented to the Main
/// Camera, so its painted horizon is pinned to a screen position: the two agree
/// at the camera's resting height (y 0.49, where CameraFollow clamps it) and
/// drift apart only while the player is climbing well above the island.
///
/// The island then stands in front of that water and hides its lower half, which
/// is what sells it as sitting in the swamp. Putting the surface down at the
/// island's feet instead leaves a bright strip of animated water pinned to the
/// bottom of the screen with the painted static water still sitting above it.
///
/// The fog is split into two quads that straddle the island - a bank behind it
/// and a thin veil in front - because a single layer leaves the island looking
/// pasted onto the mist rather than standing in it. Both rise from the near
/// shore, where the water meets the island, not from the far waterline.
/// </summary>
[DisallowMultipleComponent]
public class SwampWater : MonoBehaviour
{
    [Header("Water")]
    [SerializeField, Min(1f)] private float width = 26f;
    // The shader spreads its whole shallow-to-deep gradient over this, so it wants
    // to be close to the drop from the waterline to the island's top edge. Much
    // deeper and everything on screen stays stuck in the shallows.
    [SerializeField, Min(0.5f)] private float depth = 3f;
    [SerializeField] private Color shallowColor = new Color(0.221f, 0.298f, 0.145f, 1f);
    [SerializeField] private Color deepColor = new Color(0.043f, 0.066f, 0.02f, 1f);
    [SerializeField] private Color waterlineColor = new Color(0.478f, 0.573f, 0.302f, 1f);
    [SerializeField, Range(0f, 0.25f)] private float waveHeight = 0.02f;
    [SerializeField, Min(0f)] private float waveSpeed = 0.55f;
    // Deliberately short of opaque. The background's water is hand-painted with
    // mottling and light pooling that procedural noise does not beat; letting it
    // read through means this layer tints and animates that art rather than
    // flattening it under a wash of solid green.
    [SerializeField, Range(0f, 1f)] private float waterOpacity = 0.7f;

    [Header("Fog")]
    // How far below the waterline the water meets the island. The fog beds down
    // here rather than at the far waterline, because this is the near shore - the
    // edge of the water closest to the camera.
    [SerializeField, Min(0f)] private float shoreDepth = 1.6f;
    [SerializeField, Min(0.1f)] private float fogHeight = 5f;
    [SerializeField] private Color fogColor = new Color(0.45f, 0.52f, 0.38f, 0.36f);
    // Pushed well past these and the near veil starts hazing over the island the
    // player builds on, which costs more in readability than it gains in mood.
    [SerializeField, Range(0f, 2f)] private float backFogDensity = 1.15f;
    [SerializeField, Range(0f, 2f)] private float frontFogDensity = 0.4f;
    [SerializeField, Min(0f)] private float riseSpeed = 0.11f;
    [SerializeField, Range(0f, 1f)] private float particleAmount = 0.3f;

    [Header("Sorting")]
    // In front of the painted background, behind everything else. The island
    // (Foreground, order 55) cuts off the water's near edge for us.
    [SerializeField] private string waterSortingLayer = "Background";
    [SerializeField] private int waterSortingOrder = 10;
    [SerializeField] private string backFogSortingLayer = "Background";
    [SerializeField] private int backFogSortingOrder = 20;
    [SerializeField] private string frontFogSortingLayer = "Foreground";
    [SerializeField] private int frontFogSortingOrder = 60;

    // The fog quads start below the waterline so their bottom edge is hidden
    // inside the water while the shader fades the overlap out.
    private const float FogSink = 0.2f;

    private void Start()
    {
        BuildWater();

        // The far bank is taller, denser and slower; the near veil is a low, fast
        // scrap of mist sitting a little closer to the camera. The mismatch in
        // height, speed and wisp size is what gives the two layers parallax.
        BuildFog("Swamp Fog Back", backFogSortingLayer, backFogSortingOrder,
            -shoreDepth, fogHeight, backFogDensity, riseSpeed, 2.6f, 1f,
            particleAmount);
        BuildFog("Swamp Fog Front", frontFogSortingLayer, frontFogSortingOrder,
            -shoreDepth - 0.8f, fogHeight * 0.42f, frontFogDensity, riseSpeed * 1.7f,
            1.7f, 0.35f, particleAmount * 1.4f);
    }

    private void BuildWater()
    {
        Material waterMaterial = CreateMaterial("TowerDefense/SwampWater");
        if (waterMaterial == null)
        {
            return;
        }

        waterMaterial.SetColor("_ShallowColor", shallowColor);
        waterMaterial.SetColor("_DeepColor", deepColor);
        waterMaterial.SetColor("_SurfaceColor", waterlineColor);
        waterMaterial.SetFloat("_WaveHeight", waveHeight);
        waterMaterial.SetFloat("_WaveSpeed", waveSpeed);
        waterMaterial.SetFloat("_Opacity", waterOpacity);

        // Top edge on the waterline, body hanging below it.
        CreateQuad("Swamp Water Surface", -depth, 0f,
            waterSortingLayer, waterSortingOrder, waterMaterial);
    }

    private void BuildFog(
        string quadName,
        string sortingLayer,
        int sortingOrder,
        float baseOffset,
        float height,
        float density,
        float rise,
        float wispScale,
        float plumes,
        float motes)
    {
        Material fogMaterial = CreateMaterial("TowerDefense/SwampFog");
        if (fogMaterial == null)
        {
            return;
        }

        fogMaterial.SetColor("_FogColor", fogColor);
        fogMaterial.SetFloat("_Density", density);
        fogMaterial.SetFloat("_RiseSpeed", rise);
        fogMaterial.SetFloat("_WispScale", wispScale);
        fogMaterial.SetFloat("_Plumes", plumes);
        fogMaterial.SetFloat("_MoteAmount", Mathf.Clamp01(motes));

        CreateQuad(quadName, baseOffset - FogSink, baseOffset + height,
            sortingLayer, sortingOrder, fogMaterial);
    }

    /// <summary>
    /// A single screen-facing quad spanning the swamp's width, from
    /// <paramref name="bottom"/> to <paramref name="top"/> in local space. UVs run
    /// 0-1 over the whole face so the shaders can work in normalised space.
    /// </summary>
    private void CreateQuad(
        string quadName,
        float bottom,
        float top,
        string sortingLayer,
        int sortingOrder,
        Material material)
    {
        GameObject visual = new GameObject(quadName);
        visual.transform.SetParent(transform, false);

        MeshFilter meshFilter = visual.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = visual.AddComponent<MeshRenderer>();
        meshRenderer.sortingLayerName = sortingLayer;
        meshRenderer.sortingOrder = sortingOrder;
        meshRenderer.sharedMaterial = material;

        // The shaders undo the quad's stretch with this, so their noise cells stay
        // square in world space instead of smearing across these very wide faces.
        material.SetFloat("_Aspect", width / Mathf.Max(top - bottom, 0.0001f));

        float halfWidth = width * 0.5f;
        Mesh mesh = new Mesh { name = quadName + " Mesh" };
        mesh.vertices = new[]
        {
            new Vector3(-halfWidth, bottom, 0f),
            new Vector3(halfWidth, bottom, 0f),
            new Vector3(-halfWidth, top, 0f),
            new Vector3(halfWidth, top, 0f)
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f)
        };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.RecalculateBounds();
        meshFilter.sharedMesh = mesh;
    }

    private Material CreateMaterial(string shaderName)
    {
        Shader shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogWarning($"The {shaderName} shader could not be found.", this);
            return null;
        }

        return new Material(shader);
    }

    private void OnDrawGizmosSelected()
    {
        // The quads only exist in play mode, so draw the volumes they will occupy
        // to make the waterline placeable against the island in the scene view.
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;

        Gizmos.color = new Color(0.35f, 0.7f, 0.4f, 1f);
        Gizmos.DrawLine(new Vector3(-width * 0.5f, 0f, 0f), new Vector3(width * 0.5f, 0f, 0f));
        Gizmos.DrawWireCube(
            new Vector3(0f, -depth * 0.5f, 0f),
            new Vector3(width, depth, 0f)
        );

        // The near shore, where the fog beds down against the island.
        Gizmos.color = new Color(0.75f, 0.85f, 0.6f, 0.5f);
        Gizmos.DrawWireCube(
            new Vector3(0f, -shoreDepth + fogHeight * 0.5f, 0f),
            new Vector3(width, fogHeight, 0f)
        );

        Gizmos.matrix = previousMatrix;
    }
}
