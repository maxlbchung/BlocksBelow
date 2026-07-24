#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(AudioClipDropdownAttribute))]
public sealed class AudioClipDropdownDrawer : PropertyDrawer
{
    // Scanning the whole AssetDatabase and loading every AudioClip is expensive, and OnGUI runs
    // on every Inspector repaint (many times per second, per field). Cache the result once and
    // rebuild only when project assets actually change, so drawing costs nothing per repaint.
    private static AudioClip[] cachedClips;
    private static string[] cachedOptions;
    private static bool cacheValid;
    private static bool hookInstalled;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.ObjectReference)
        {
            EditorGUI.PropertyField(position, property, label);
            return;
        }

        EnsureCache();

        AudioClip selected = property.objectReferenceValue as AudioClip;
        int currentIndex = selected == null ? 0 : Array.IndexOf(cachedClips, selected) + 1;

        EditorGUI.BeginChangeCheck();
        int newIndex = EditorGUI.Popup(position, label.text, currentIndex, cachedOptions);
        if (EditorGUI.EndChangeCheck())
        {
            property.objectReferenceValue = newIndex <= 0 ? null : cachedClips[newIndex - 1];
        }
    }

    private static void EnsureCache()
    {
        if (!hookInstalled)
        {
            // Invalidate the cache whenever assets are imported, deleted, or renamed.
            EditorApplication.projectChanged += InvalidateCache;
            hookInstalled = true;
        }

        if (cacheValid && cachedClips != null && cachedOptions != null)
        {
            return;
        }

        cachedClips = AssetDatabase.FindAssets("t:AudioClip")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<AudioClip>)
            .Where(clip => clip != null)
            .OrderBy(clip => clip.name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        cachedOptions = new string[cachedClips.Length + 1];
        cachedOptions[0] = "None";
        for (int i = 0; i < cachedClips.Length; i++)
        {
            cachedOptions[i + 1] = cachedClips[i].name;
        }

        cacheValid = true;
    }

    private static void InvalidateCache()
    {
        cacheValid = false;
    }
}
#endif
