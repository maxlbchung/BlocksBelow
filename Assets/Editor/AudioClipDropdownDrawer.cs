#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(AudioClipDropdownAttribute))]
public sealed class AudioClipDropdownDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.ObjectReference)
        {
            EditorGUI.PropertyField(position, property, label);
            return;
        }

        AudioClip[] clips = AssetDatabase.FindAssets("t:AudioClip")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<AudioClip>)
            .Where(clip => clip != null)
            .OrderBy(clip => clip.name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        string[] options = new[] { "None" }
            .Concat(clips.Select(clip => clip.name))
            .ToArray();

        AudioClip selected = property.objectReferenceValue as AudioClip;
        int currentIndex = selected == null ? 0 : Array.IndexOf(clips, selected) + 1;
        int newIndex = EditorGUI.Popup(position, label.text, currentIndex, options);

        property.objectReferenceValue = newIndex <= 0 ? null : clips[newIndex - 1];
    }
}
#endif
