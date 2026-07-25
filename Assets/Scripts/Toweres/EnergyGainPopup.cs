using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// A big number that pops into the world and stays until it is told to go - one unit of
/// power counted off a cage. Built and thrown away at runtime, so nothing has to carry it
/// on a prefab.
/// </summary>
public class EnergyGainPopup : MonoBehaviour
{
    private const float PopDuration = 0.22f;
    // How far past its final size the pop swells before settling back.
    private const float PopOvershoot = 1.3f;
    private const float FontSize = 34f;
    private const int SortingOrder = 30;

    private TextMeshPro label;
    private float baseScale = 1f;

    /// <summary>Pops <paramref name="text"/> in at <paramref name="worldPosition"/>.</summary>
    public static EnergyGainPopup Show(Vector3 worldPosition, string text, float cellUnit)
    {
        GameObject popupObject = new GameObject("Energy Gain Popup");
        popupObject.transform.position = worldPosition;

        EnergyGainPopup popup = popupObject.AddComponent<EnergyGainPopup>();
        popup.Begin(text, cellUnit);
        return popup;
    }

    /// <summary>Fades the popup away over <paramref name="duration"/> and deletes it.</summary>
    public void FadeOut(float duration)
    {
        StopAllCoroutines();
        StartCoroutine(FadeRoutine(duration));
    }

    private void Begin(string text, float cellUnit)
    {
        // TextMeshPro authors its text at a size meant for a whole screen, so the object
        // is scaled down to grid size the same way the bird's countdown is.
        baseScale = 0.1f * Mathf.Max(0.01f, cellUnit);
        transform.localScale = Vector3.zero;

        label = gameObject.AddComponent<TextMeshPro>();
        label.text = text;
        label.fontSize = FontSize;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.color = Color.white;
        label.rectTransform.sizeDelta = new Vector2(24f, 12f);
        label.sortingLayerID = SortingLayer.NameToID("Foreground");
        label.sortingOrder = SortingOrder;

        StartCoroutine(PopRoutine());
    }

    /// <summary>Swells past full size and settles back, so the number lands with weight.</summary>
    private IEnumerator PopRoutine()
    {
        const float SwellFraction = 0.6f;

        for (float elapsed = 0f; elapsed < PopDuration; elapsed += Time.deltaTime)
        {
            float t = elapsed / PopDuration;
            float scale = t < SwellFraction
                ? Mathf.SmoothStep(0f, PopOvershoot, t / SwellFraction)
                : Mathf.SmoothStep(PopOvershoot, 1f, (t - SwellFraction) / (1f - SwellFraction));

            transform.localScale = Vector3.one * (baseScale * scale);
            yield return null;
        }

        transform.localScale = Vector3.one * baseScale;
    }

    private IEnumerator FadeRoutine(float duration)
    {
        transform.localScale = Vector3.one * baseScale;

        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            label.color = new Color(1f, 1f, 1f, 1f - elapsed / duration);
            yield return null;
        }

        Destroy(gameObject);
    }
}
