using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Draws the edges of its RectTransform and nothing inside them, so a panel reads as a
/// wireframe box with the game visible through it instead of a filled sprite.
///
/// Each edge is a plain UI Image anchored to one side of this rect. Anchoring rather than
/// generating a mesh means the lines follow the box for free whenever a layout group
/// resizes it, and each one can be inspected in the hierarchy while the game runs.
///
/// Sides are switched on individually: the shop's tabs leave their bottom edge off, so
/// the top edge of the box below closes them and the two read as one shape.
/// </summary>
public class UIWireframeBox : MonoBehaviour
{
    private const int TopEdge = 0;
    private const int BottomEdge = 1;
    private const int LeftEdge = 2;
    private const int RightEdge = 3;
    private static readonly string[] EdgeNames = { "Top", "Bottom", "Left", "Right" };

    [SerializeField] private Color lineColor = Color.white;
    [SerializeField, Min(0.5f)] private float thickness = 3f;
    [SerializeField] private bool drawTop = true;
    [SerializeField] private bool drawBottom = true;
    [SerializeField] private bool drawLeft = true;
    [SerializeField] private bool drawRight = true;

    private readonly Image[] edges = new Image[4];

    public Color Color
    {
        get => lineColor;
        set
        {
            lineColor = value;
            Rebuild();
        }
    }

    public float Thickness
    {
        get => thickness;
        set
        {
            thickness = Mathf.Max(0.5f, value);
            Rebuild();
        }
    }

    /// <summary>Chooses which edges are drawn. Any side left off stays open.</summary>
    public void SetSides(bool left, bool right, bool top, bool bottom)
    {
        drawLeft = left;
        drawRight = right;
        drawTop = top;
        drawBottom = bottom;
        Rebuild();
    }

    private void Awake()
    {
        Rebuild();
    }

    private void Rebuild()
    {
        float edge = Mathf.Max(0.5f, thickness);

        // The verticals stop where the horizontals begin, so no two lines overlap and a
        // translucent outline stays one even shade at the corners.
        float bottomInset = drawBottom ? edge : 0f;
        float topInset = drawTop ? edge : 0f;

        ConfigureEdge(
            TopEdge, drawTop,
            new Vector2(0f, 1f), Vector2.one,
            new Vector2(0f, -edge), Vector2.zero);
        ConfigureEdge(
            BottomEdge, drawBottom,
            Vector2.zero, new Vector2(1f, 0f),
            Vector2.zero, new Vector2(0f, edge));
        ConfigureEdge(
            LeftEdge, drawLeft,
            Vector2.zero, new Vector2(0f, 1f),
            new Vector2(0f, bottomInset), new Vector2(edge, -topInset));
        ConfigureEdge(
            RightEdge, drawRight,
            new Vector2(1f, 0f), Vector2.one,
            new Vector2(-edge, bottomInset), new Vector2(0f, -topInset));
    }

    private void ConfigureEdge(
        int index,
        bool visible,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax)
    {
        Image edge = edges[index];
        if (edge == null)
        {
            GameObject edgeObject = new GameObject(EdgeNames[index] + " Edge", typeof(RectTransform));
            edgeObject.transform.SetParent(transform, false);

            edge = edgeObject.AddComponent<Image>();
            edge.raycastTarget = false;
            edges[index] = edge;
        }

        edge.enabled = visible;
        edge.color = lineColor;

        RectTransform edgeRect = edge.rectTransform;
        edgeRect.anchorMin = anchorMin;
        edgeRect.anchorMax = anchorMax;
        edgeRect.offsetMin = offsetMin;
        edgeRect.offsetMax = offsetMax;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        thickness = Mathf.Max(0.5f, thickness);
        if (Application.isPlaying)
        {
            Rebuild();
        }
    }
#endif
}
