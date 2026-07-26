using UnityEngine;

/// <summary>
/// A floating health bar for an entity whose fight lasts long enough that the player
/// wants to watch it wear down. Drop it on the prefabs that earn one; enemies without
/// it are unaffected.
///
/// Built in Start rather than Awake on purpose. Entity caches the renderers it flashes
/// white on a hit during its own Awake, so a bar created after that is left out of the
/// flash and stays readable while the body blinks.
/// </summary>
public sealed class EnemyHealthBar : MonoBehaviour
{
    [Header("Layout")]
    [SerializeField] private Vector2 barSize = new Vector2(7.2f, 0.84f);
    [SerializeField, Tooltip("World-space gap between the top of the entity's collider and the bar.")]
    private float gapAboveEntity = 0.6f;

    [Header("Colors")]
    [Tooltip("Both colors are deliberately translucent, so a bar this size does not "
        + "block the fight going on behind it.")]
    [SerializeField] private Color fillColor = new Color(0.85f, 0.22f, 0.2f, 0.6f);
    [SerializeField] private Color backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.45f);

    [Header("Visibility")]
    [SerializeField, Tooltip("Hides the bar until the entity has been hit, the way the player's own bar behaves.")]
    private bool hideWhenFull;
    [SerializeField] private int sortingOrder = 100;

    // One white pixel serves every bar in the scene; none of them ever needs its own.
    private static Sprite barSprite;

    private Entity entity;
    private Transform barRoot;
    private Transform fill;
    private float maximumHealth;
    private float worldHeight;
    private bool hidden;

    /// <summary>
    /// Forces the bar off screen, for effects that take over the entity's look for a
    /// while - a death sequence has no use for a sliver of health hanging over it.
    /// Cleared when the entity is recycled.
    /// </summary>
    public void SetHidden(bool value)
    {
        hidden = value;
        UpdateBar();
    }

    private void Start()
    {
        entity = GetComponent<Entity>();
        if (entity == null)
        {
            Debug.LogWarning($"{name} has an EnemyHealthBar but no Entity to read health from.", this);
            enabled = false;
            return;
        }

        // Health is at its prefab value here: the pool restores it on acquire, which
        // runs before the first activation that lets this Start fire.
        maximumHealth = Mathf.Max(0.0001f, entity.health);
        worldHeight = ResolveWorldHeight();
        BuildBar();
        UpdateBar();
    }

    private void LateUpdate()
    {
        UpdateBar();
    }

    private float ResolveWorldHeight()
    {
        Collider2D bodyCollider = GetComponent<Collider2D>();
        float halfHeight = bodyCollider != null
            ? bodyCollider.bounds.max.y - transform.position.y
            : Mathf.Abs(transform.lossyScale.y) * 0.5f;

        return Mathf.Max(0f, halfHeight) + gapAboveEntity;
    }

    private void BuildBar()
    {
        GameObject rootObject = new GameObject("Health Bar");
        barRoot = rootObject.transform;
        barRoot.SetParent(transform, false);

        CreateSegment("Background", backgroundColor, sortingOrder, 0f);
        fill = CreateSegment("Fill", fillColor, sortingOrder + 1, -0.01f);
    }

    private Transform CreateSegment(string segmentName, Color color, int order, float depth)
    {
        GameObject segment = new GameObject(segmentName);
        segment.transform.SetParent(barRoot, false);
        segment.transform.localPosition = new Vector3(0f, 0f, depth);
        segment.transform.localScale = new Vector3(barSize.x, barSize.y, 1f);

        SpriteRenderer renderer = segment.AddComponent<SpriteRenderer>();
        renderer.sprite = GetBarSprite();
        renderer.color = color;
        renderer.sortingLayerName = "Foreground";
        renderer.sortingOrder = order;

        return segment.transform;
    }

    private void UpdateBar()
    {
        if (barRoot == null || fill == null)
        {
            return;
        }

        // A healed or re-tuned entity should not overflow its own bar.
        if (entity.health > maximumHealth)
        {
            maximumHealth = entity.health;
        }

        float percent = Mathf.Clamp01(entity.health / maximumHealth);
        bool visible = !hidden && percent > 0f && (!hideWhenFull || percent < 1f);
        if (barRoot.gameObject.activeSelf != visible)
        {
            barRoot.gameObject.SetActive(visible);
        }

        if (!visible)
        {
            return;
        }

        // The body spins to face its velocity and mirrors on Y when it turns left, so the
        // bar is pinned in world space each frame instead of riding along. Dividing out
        // the parent's scale - sign included - keeps it level, upright and unmirrored.
        barRoot.position = transform.position + Vector3.up * worldHeight;
        barRoot.rotation = Quaternion.identity;
        Vector3 lossyScale = transform.lossyScale;
        barRoot.localScale = new Vector3(
            Mathf.Approximately(lossyScale.x, 0f) ? 1f : 1f / lossyScale.x,
            Mathf.Approximately(lossyScale.y, 0f) ? 1f : 1f / lossyScale.y,
            1f);

        // Anchored to the left edge, so the bar drains rightward instead of shrinking
        // toward its middle.
        fill.localScale = new Vector3(barSize.x * percent, barSize.y, 1f);
        fill.localPosition = new Vector3(
            -(barSize.x * (1f - percent)) * 0.5f,
            0f,
            -0.01f);
    }

    private static Sprite GetBarSprite()
    {
        if (barSprite != null)
        {
            return barSprite;
        }

        Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            name = "Enemy Health Bar Texture",
            filterMode = FilterMode.Point,
            hideFlags = HideFlags.HideAndDontSave
        };
        texture.SetPixel(0, 0, Color.white);
        texture.Apply();

        barSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, 1f, 1f),
            new Vector2(0.5f, 0.5f),
            1f);
        barSprite.name = "Enemy Health Bar";
        barSprite.hideFlags = HideFlags.HideAndDontSave;
        return barSprite;
    }
}
