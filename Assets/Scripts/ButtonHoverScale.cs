using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Smoothly enlarges a menu button while the pointer is over it.
/// </summary>
public class ButtonHoverScale : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private MainMenuNavigation menu;
    private Vector3 normalScale;
    private bool isHovered;

    public void Initialize(MainMenuNavigation owningMenu)
    {
        menu = owningMenu;
        normalScale = transform.localScale;
    }

    private void Awake()
    {
        normalScale = transform.localScale;
    }

    private void Update()
    {
        float size = isHovered && menu != null ? menu.HoverSize : 1f;
        float speed = menu != null ? menu.X : 8f;
        Vector3 targetScale = normalScale * size;

        // Unscaled time keeps menu animation responsive even if gameplay is paused.
        transform.localScale = Vector3.Lerp(
            transform.localScale,
            targetScale,
            1f - Mathf.Exp(-speed * Time.unscaledDeltaTime));
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovered = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
    }

    private void OnDisable()
    {
        isHovered = false;
        transform.localScale = normalScale;
    }
}
