using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace FireAnimation
{
    [CustomEditor(typeof(PsdImporter))]
    public class PsdImporterEditor : ScriptedImporterEditor
    {
        private SerializedProperty m_PixelsPerUnit;
        private SerializedProperty m_FilterMode;
        private SerializedProperty m_SpriteMeshType;
        private SerializedProperty m_WrapMode;
        private SerializedProperty m_FramesPerSecond;

        public override void OnEnable()
        {
            base.OnEnable();
            m_PixelsPerUnit = serializedObject.FindProperty("pixelsPerUnit");
            m_FilterMode = serializedObject.FindProperty("filterMode");
            m_SpriteMeshType = serializedObject.FindProperty("spriteMeshType");
            m_WrapMode = serializedObject.FindProperty("wrapMode");
            m_FramesPerSecond = serializedObject.FindProperty("framesPerSecond");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Sprite Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_PixelsPerUnit, new GUIContent("Pixels Per Unit"));
            EditorGUILayout.PropertyField(m_SpriteMeshType, new GUIContent("Mesh Type"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Animation Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_FramesPerSecond, new GUIContent("Frames Per Second"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Advanced", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_WrapMode, new GUIContent("Wrap Mode"));
            EditorGUILayout.PropertyField(m_FilterMode, new GUIContent("Filter Mode"));

            serializedObject.ApplyModifiedProperties();

            ApplyRevertGUI();
        }
    }
}