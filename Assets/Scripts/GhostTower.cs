using UnityEngine;

public class GhostTower : MonoBehaviour
{
    [Header("Opacity Settings")]
    [Range(0f, 1f)]
    [Tooltip("0 = Fully Transparent, 1 = Fully Opaque")]
    [SerializeField] private float opacity = 0.5f;

    [Tooltip("If checked, changes opacity for this parent object too. If unchecked, only children are changed.")]
    [SerializeField] private bool includeParent = false;

    private void Awake()
    {
        ApplyOpacity();
    }

    public void ApplyOpacity()
    {
        // Get all SpriteRenderers in children (including inactive ones)
        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);

        foreach (SpriteRenderer sprite in renderers)
        {
            // Skip the parent object if includeParent is false
            if (!includeParent && sprite.gameObject == gameObject)
            {
                continue;
            }

            // Copy color, modify alpha (a), and reassign back to the sprite
            Color color = sprite.color;
            color.a = opacity;
            sprite.color = color;
        }
    }
}