using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace FireAnimation
{
    [CustomEditor(typeof(PsdImporter))]
    // ReSharper disable once UnusedMember.Global
    public class PsdImporterEditor : ScriptedImporterEditor
    {
        private SerializedProperty _pixelsPerUnit;
        private SerializedProperty _filterMode;
        private SerializedProperty _spriteMeshType;
        private SerializedProperty _wrapMode;
        private SerializedProperty _framesPerSecond;
        private SerializedProperty _animationSettings;

        private readonly Dictionary<string, bool> _foldoutStates = new Dictionary<string, bool>();

        public override void OnEnable()
        {
            base.OnEnable();
            _pixelsPerUnit = serializedObject.FindProperty("pixelsPerUnit");
            _filterMode = serializedObject.FindProperty("filterMode");
            _spriteMeshType = serializedObject.FindProperty("spriteMeshType");
            _wrapMode = serializedObject.FindProperty("wrapMode");
            _framesPerSecond = serializedObject.FindProperty("framesPerSecond");
            _animationSettings = serializedObject.FindProperty("animationSettings");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Sprite Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_pixelsPerUnit, new GUIContent("Pixels Per Unit"));
            EditorGUILayout.PropertyField(_spriteMeshType, new GUIContent("Mesh Type"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Default Animation Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_framesPerSecond, new GUIContent("Default Frames Per Second"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Animations", EditorStyles.boldLabel);

            var importer = (PsdImporter)target;
            var animationInfos = GetAnimationInfos(importer);
            if (animationInfos is { Count: > 0 })
                DrawAnimationsList(animationInfos);
            else
                EditorGUILayout.HelpBox("No animations found. Import the PSD file to see animations.",
                    MessageType.Info);


            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Advanced", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_wrapMode, new GUIContent("Wrap Mode"));
            EditorGUILayout.PropertyField(_filterMode, new GUIContent("Filter Mode"));

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
            if (importer.Metadata is not { Animations: { Count: > 0 } }) return null;

            var infos = new List<AnimationInfo>();
            foreach (var anim in importer.Metadata.Animations)
            {
                var frameCount = 0;
                var secondaryTextures = new List<string>();

                foreach (var texture in anim.Textures)
                {
                    if (texture.Type == TextureType.Albedo)
                    {
                        frameCount = texture.Frames?.Count ?? 0;
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

        private void DrawAnimationsList(List<AnimationInfo> animationInfos)
        {
            var settingsDict = new Dictionary<string, SerializedProperty>();
            if (_animationSettings is { isArray: true })
            {
                for (var i = 0; i < _animationSettings.arraySize; i++)
                {
                    var setting = _animationSettings.GetArrayElementAtIndex(i);
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
                _foldoutStates.TryAdd(animInfo.Name, false);

                EditorGUILayout.BeginHorizontal();
                var isExpanded = EditorGUILayout.Foldout(_foldoutStates[animInfo.Name], animInfo.Name, true);
                _foldoutStates[animInfo.Name] = isExpanded;

                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField($"{animInfo.FrameCount} frames", EditorStyles.miniLabel,
                    GUILayout.Width(80));
                EditorGUILayout.EndHorizontal();

                if (isExpanded)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                    if (animInfo.SecondaryTextures != null && animInfo.SecondaryTextures.Count > 0)
                        EditorGUILayout.LabelField(
                            $"Secondary Textures: {string.Join(", ", animInfo.SecondaryTextures)}");
                    else
                        EditorGUILayout.LabelField("Secondary Textures: None");

                    EditorGUILayout.Space(5);

                    SerializedProperty animSetting;
                    if (!settingsDict.TryGetValue(animInfo.Name, out animSetting))
                    {
                        _animationSettings.arraySize++;
                        animSetting = _animationSettings.GetArrayElementAtIndex(_animationSettings.arraySize - 1);
                        animSetting.FindPropertyRelative("AnimationName").stringValue = animInfo.Name;
                        animSetting.FindPropertyRelative("FramesPerSecond").floatValue = -1f;
                        animSetting.FindPropertyRelative("LoopTime").boolValue = true;
                        settingsDict[animInfo.Name] = animSetting;
                    }

                    var fpsProp = animSetting.FindPropertyRelative("FramesPerSecond");
                    var loopProp = animSetting.FindPropertyRelative("LoopTime");

                    var currentFps = fpsProp.floatValue;
                    var hasOverride = currentFps >= 0f;
                    var defaultFps = _framesPerSecond.floatValue;

                    EditorGUILayout.BeginHorizontal();

                    EditorGUI.BeginChangeCheck();
                    var newHasOverride =
                        EditorGUILayout.Toggle("Override FPS", hasOverride, GUILayout.ExpandWidth(false));
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
                        var newFps = EditorGUILayout.FloatField(currentFps, GUILayout.Width(100));
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