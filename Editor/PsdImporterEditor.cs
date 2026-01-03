using System.Collections.Generic;
using System.Linq;
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
        private SerializedProperty m_AnimationSettings;
        private SerializedProperty m_Metadata;

        private Dictionary<string, bool> m_FoldoutStates = new Dictionary<string, bool>();

        public override void OnEnable()
        {
            base.OnEnable();
            m_PixelsPerUnit = serializedObject.FindProperty("pixelsPerUnit");
            m_FilterMode = serializedObject.FindProperty("filterMode");
            m_SpriteMeshType = serializedObject.FindProperty("spriteMeshType");
            m_WrapMode = serializedObject.FindProperty("wrapMode");
            m_FramesPerSecond = serializedObject.FindProperty("framesPerSecond");
            m_AnimationSettings = serializedObject.FindProperty("animationSettings");
            m_Metadata = serializedObject.FindProperty("Metadata");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Sprite Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_PixelsPerUnit, new GUIContent("Pixels Per Unit"));
            EditorGUILayout.PropertyField(m_SpriteMeshType, new GUIContent("Mesh Type"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Default Animation Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_FramesPerSecond, new GUIContent("Default Frames Per Second"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Animations", EditorStyles.boldLabel);

            var importer = (PsdImporter)target;
            List<AnimationInfo> animationInfos = GetAnimationInfos(importer);
            if (animationInfos != null && animationInfos.Count > 0)
                DrawAnimationsList(animationInfos);
            else
                EditorGUILayout.HelpBox("No animations found. Import the PSD file to see animations.", MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Advanced", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_WrapMode, new GUIContent("Wrap Mode"));
            EditorGUILayout.PropertyField(m_FilterMode, new GUIContent("Filter Mode"));

            serializedObject.ApplyModifiedProperties();

            ApplyRevertGUI();
        }

        private class AnimationInfo
        {
            public string Name;
            public int FrameCount;
            public List<string> SecondaryTextures;
        }

        private List<AnimationInfo> GetAnimationInfos(PsdImporter importer)
        {
            if (importer == null) return null;

            if (importer.Metadata != null && importer.Metadata.Animations != null && importer.Metadata.Animations.Count > 0)
            {
                var infos = new List<AnimationInfo>();
                foreach (var anim in importer.Metadata.Animations)
                {
                    int frameCount = 0;
                    var secondaryTextures = new List<string>();

                    foreach (var texture in anim.Textures)
                    {
                        if (texture.Type == TextureType.Albedo)
                        {
                            frameCount = texture.Frames != null ? texture.Frames.Count : 0;
                        }
                        else if (texture.Type != TextureType.Unknown)
                        {
                            secondaryTextures.Add(texture.Name);
                        }
                    }

                    infos.Add(new AnimationInfo
                    {
                        Name = anim.Name,
                        FrameCount = frameCount,
                        SecondaryTextures = secondaryTextures
                    });
                }
                return infos;
            }

            return null;
        }

        private void DrawAnimationsList(List<AnimationInfo> animationInfos)
        {
            var settingsDict = new Dictionary<string, SerializedProperty>();
            if (m_AnimationSettings != null && m_AnimationSettings.isArray)
            {
                for (int i = 0; i < m_AnimationSettings.arraySize; i++)
                {
                    var setting = m_AnimationSettings.GetArrayElementAtIndex(i);
                    var nameProp = setting.FindPropertyRelative("AnimationName");
                    if (nameProp != null && !string.IsNullOrEmpty(nameProp.stringValue))
                    {
                        settingsDict[nameProp.stringValue] = setting;
                    }
                }
            }

            EditorGUI.indentLevel++;
            foreach (var animInfo in animationInfos)
            {
                if (!m_FoldoutStates.ContainsKey(animInfo.Name))
                    m_FoldoutStates[animInfo.Name] = false;

                EditorGUILayout.BeginHorizontal();
                bool isExpanded = EditorGUILayout.Foldout(m_FoldoutStates[animInfo.Name], animInfo.Name, true);
                m_FoldoutStates[animInfo.Name] = isExpanded;

                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField($"{animInfo.FrameCount} frames", EditorStyles.miniLabel, GUILayout.Width(80));
                EditorGUILayout.EndHorizontal();

                if (isExpanded)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                    if (animInfo.SecondaryTextures != null && animInfo.SecondaryTextures.Count > 0)
                        EditorGUILayout.LabelField($"Secondary Textures: {string.Join(", ", animInfo.SecondaryTextures)}");
                    else
                        EditorGUILayout.LabelField("Secondary Textures: None");

                    EditorGUILayout.Space(5);

                    SerializedProperty animSetting;
                    if (!settingsDict.TryGetValue(animInfo.Name, out animSetting))
                    {
                        m_AnimationSettings.arraySize++;
                        animSetting = m_AnimationSettings.GetArrayElementAtIndex(m_AnimationSettings.arraySize - 1);
                        animSetting.FindPropertyRelative("AnimationName").stringValue = animInfo.Name;
                        animSetting.FindPropertyRelative("FramesPerSecond").floatValue = -1f;
                        animSetting.FindPropertyRelative("LoopTime").boolValue = true;
                        settingsDict[animInfo.Name] = animSetting;
                    }

                    var fpsProp = animSetting.FindPropertyRelative("FramesPerSecond");
                    var loopProp = animSetting.FindPropertyRelative("LoopTime");

                    float currentFps = fpsProp.floatValue;
                    bool hasOverride = currentFps >= 0f;
                    float defaultFps = m_FramesPerSecond.floatValue;

                    EditorGUILayout.BeginHorizontal();

                    EditorGUI.BeginChangeCheck();
                    bool newHasOverride = EditorGUILayout.Toggle("Override FPS", hasOverride, GUILayout.ExpandWidth(false));
                    if (EditorGUI.EndChangeCheck())
                    {
                        if (newHasOverride)
                        {
                            if (currentFps < 0f)
                            {
                                fpsProp.floatValue = defaultFps;
                                currentFps = defaultFps;
                                hasOverride = true;
                            }
                        }
                        else
                        {
                            fpsProp.floatValue = -1f;
                            hasOverride = false;
                        }
                    }

                    if (hasOverride)
                    {
                        EditorGUI.BeginChangeCheck();
                        float newFps = EditorGUILayout.FloatField(currentFps, GUILayout.Width(100));
                        if (EditorGUI.EndChangeCheck())
                        {
                            if (newFps > 0f)
                            {
                                fpsProp.floatValue = newFps;
                            }
                        }
                    }
                    else
                    {
                        EditorGUI.BeginDisabledGroup(true);
                        EditorGUILayout.FloatField(defaultFps, GUILayout.Width(100));
                        EditorGUI.EndDisabledGroup();
                    }

                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.PropertyField(loopProp, new GUIContent("Loop Time"));

                    EditorGUILayout.EndVertical();
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.Space(2);
            }
            EditorGUI.indentLevel--;
        }
    }
}