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
            _pixelsPerUnit = serializedObject.FindProperty("_pixelsPerUnit");
            _filterMode = serializedObject.FindProperty("_filterMode");
            _spriteMeshType = serializedObject.FindProperty("_spriteMeshType");
            _wrapMode = serializedObject.FindProperty("_wrapMode");
            _framesPerSecond = serializedObject.FindProperty("_framesPerSecond");
            _animationSettings = serializedObject.FindProperty("_animationSettings");
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
            EditorGUILayout.LabelField("GameObject Groups", EditorStyles.boldLabel);

            var importer = (PsdImporter)target;
            var groupInfos = GetGroupInfos(importer);
            if (groupInfos is { Count: > 0 })
                DrawGroupsList(groupInfos);
            else
                EditorGUILayout.HelpBox("No groups found. Import the PSD file to see groups.",
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
            public List<string> TextureTypes;
        }

        private class GroupInfo
        {
            public string Name;
            public LayerColor Color;
            public List<AnimationInfo> Animations;
        }

        private List<GroupInfo> GetGroupInfos(PsdImporter importer)
        {
            if (importer.Metadata is not { Groups: { Count: > 0 } }) return null;

            var groupInfos = new List<GroupInfo>();
            foreach (var group in importer.Metadata.Groups)
            {
                var groupInfo = new GroupInfo
                {
                    Name = group.Name,
                    Color = group.Color,
                    Animations = new List<AnimationInfo>()
                };

                foreach (var anim in group.Animations)
                {
                    var frameCount = 0;
                    var textureTypes = new List<string>();

                    foreach (var texture in anim.Textures)
                    {
                        if (texture.Type == TextureType.Albedo)
                        {
                            frameCount = texture.Frames?.Count ?? 0;
                        }

                        textureTypes.Add(TextureTypeHelper.GetDisplayName(texture.Type));
                    }

                    groupInfo.Animations.Add(new AnimationInfo
                    {
                        Name = anim.Name,
                        FrameCount = frameCount,
                        TextureTypes = textureTypes
                    });
                }

                groupInfos.Add(groupInfo);
            }

            return groupInfos;
        }

        private void DrawGroupsList(List<GroupInfo> groupInfos)
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

            foreach (var groupInfo in groupInfos)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(groupInfo.Name, EditorStyles.boldLabel);

                EditorGUI.indentLevel++;
                foreach (var animInfo in groupInfo.Animations)
                {
                    DrawAnimationInfo(animInfo, settingsDict);
                }

                EditorGUI.indentLevel--;

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2);
            }
        }

        private void DrawAnimationInfo(AnimationInfo animInfo, Dictionary<string, SerializedProperty> settingsDict)
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

                EditorGUILayout.LabelField(
                    animInfo.TextureTypes is { Count: > 0 }
                        ? $"Textures: {string.Join(", ", animInfo.TextureTypes)}"
                        : "Textures: None");

                EditorGUILayout.Space(5);

                if (!settingsDict.TryGetValue(animInfo.Name, out var animSetting))
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
    }
}
