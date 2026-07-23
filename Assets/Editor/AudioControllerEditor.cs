#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AudioController))]
public sealed class AudioControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();

        if (GUILayout.Button("Scan Assets/Audio Into Library"))
        {
            ((AudioController)target).ScanAudioLibrary();
            serializedObject.Update();
        }
    }
}
#endif
