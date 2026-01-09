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
        private SerializedProperty _defaultBevelWidth;
        private SerializedProperty _defaultSmoothness;
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
            _defaultBevelWidth = serializedObject.FindProperty("_defaultBevelWidth");
            _defaultSmoothness = serializedObject.FindProperty("_defaultSmoothness");
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
            EditorGUILayout.LabelField("Default Normal Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_defaultBevelWidth, new GUIContent("Default Bevel Width"));
            EditorGUILayout.PropertyField(_defaultSmoothness, new GUIContent("Default Smoothness"));

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

        private class PartInfo
        {
            public string Name;
            public int FrameCount;
        }

        private class AnimationInfo
        {
            public string Name;
            public int FrameCount;
            public List<string> TextureTypes;
            public List<PartInfo> Parts;
            public bool HasLightBlock;
        }

        private class GroupInfo
        {
            public string Name;
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
                    Animations = new List<AnimationInfo>()
                };

                foreach (var anim in group.Animations)
                {
                    var frameCount = 0;
                    var textureTypes = new List<string>();

                    foreach (var texture in anim.Textures)
                    {
                        if (texture.Type == TextureType.Albedo)
                            frameCount = texture.Frames?.Count ?? 0;

                        textureTypes.Add(TextureTypeHelper.GetDisplayName(texture.Type));
                    }

                    var parts = new List<PartInfo>();
                    foreach (var part in anim.Parts)
                    {
                        parts.Add(new PartInfo
                        {
                            Name = part.Name,
                            FrameCount = part.Frames?.Count ?? 0
                        });
                    }

                    groupInfo.Animations.Add(new AnimationInfo
                    {
                        Name = anim.Name,
                        FrameCount = frameCount,
                        TextureTypes = textureTypes,
                        Parts = parts,
                        HasLightBlock = anim.Textures.Exists(t => t.Type == TextureType.LightBlock)
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

            if (!isExpanded)
            {
                EditorGUILayout.Space(2);
                return;
            }

            EditorGUI.indentLevel++;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Textures info
            EditorGUILayout.LabelField(
                animInfo.TextureTypes is { Count: > 0 }
                    ? $"Textures: {string.Join(", ", animInfo.TextureTypes)}"
                    : "Textures: None");

            EditorGUILayout.Space(5);

            // Get or create animation settings
            if (!settingsDict.TryGetValue(animInfo.Name, out var animSetting))
            {
                _animationSettings.arraySize++;
                animSetting = _animationSettings.GetArrayElementAtIndex(_animationSettings.arraySize - 1);
                animSetting.FindPropertyRelative("AnimationName").stringValue = animInfo.Name;
                animSetting.FindPropertyRelative("FramesPerSecond").floatValue = -1f;
                animSetting.FindPropertyRelative("LoopTime").boolValue = true;
                settingsDict[animInfo.Name] = animSetting;
            }

            // FPS override
            DrawFpsOverride(animSetting);

            // Loop time
            var loopProp = animSetting.FindPropertyRelative("LoopTime");
            EditorGUILayout.PropertyField(loopProp, new GUIContent("Loop Time"));

            // Parts section
            if (animInfo.Parts is { Count: > 0 })
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Sprite Parts", EditorStyles.boldLabel);

                var partSettingsProp = animSetting.FindPropertyRelative("PartSettings");
                DrawPartsSection(animInfo.Parts, partSettingsProp);
            }

            EditorGUILayout.EndVertical();
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(2);
        }

        private void DrawFpsOverride(SerializedProperty animSetting)
        {
            var fpsProp = animSetting.FindPropertyRelative("FramesPerSecond");
            var currentFps = fpsProp.floatValue;
            var hasOverride = currentFps >= 0f;
            var defaultFps = _framesPerSecond.floatValue;

            EditorGUILayout.BeginHorizontal();

            EditorGUI.BeginChangeCheck();
            var newHasOverride = EditorGUILayout.Toggle("Override FPS", hasOverride, GUILayout.ExpandWidth(false));
            if (EditorGUI.EndChangeCheck())
            {
                fpsProp.floatValue = newHasOverride ? defaultFps : -1f;
                hasOverride = newHasOverride;
            }

            if (hasOverride)
            {
                EditorGUI.BeginChangeCheck();
                var newFps = EditorGUILayout.FloatField(fpsProp.floatValue, GUILayout.Width(100));
                if (EditorGUI.EndChangeCheck() && newFps > 0f)
                    fpsProp.floatValue = newFps;
            }
            else
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.FloatField(defaultFps, GUILayout.Width(100));
                EditorGUI.EndDisabledGroup();
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawPartsSection(List<PartInfo> parts, SerializedProperty partSettingsProp)
        {
            // Build lookup for existing part settings
            var partSettingsDict = new Dictionary<string, SerializedProperty>();
            if (partSettingsProp is { isArray: true })
            {
                for (var i = 0; i < partSettingsProp.arraySize; i++)
                {
                    var setting = partSettingsProp.GetArrayElementAtIndex(i);
                    var nameProp = setting.FindPropertyRelative("PartName");
                    if (nameProp != null && !string.IsNullOrEmpty(nameProp.stringValue))
                        partSettingsDict[nameProp.stringValue] = setting;
                }
            }

            foreach (var part in parts)
            {
                var partFoldoutKey = $"part_{part.Name}";
                _foldoutStates.TryAdd(partFoldoutKey, false);

                EditorGUILayout.BeginHorizontal();
                var isPartExpanded = EditorGUILayout.Foldout(_foldoutStates[partFoldoutKey], part.Name, true);
                _foldoutStates[partFoldoutKey] = isPartExpanded;

                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField($"{part.FrameCount} frames", EditorStyles.miniLabel, GUILayout.Width(80));
                EditorGUILayout.EndHorizontal();

                if (!isPartExpanded)
                    continue;

                // Get or create part settings
                if (!partSettingsDict.TryGetValue(part.Name, out var partSetting))
                {
                    partSettingsProp.arraySize++;
                    partSetting = partSettingsProp.GetArrayElementAtIndex(partSettingsProp.arraySize - 1);
                    partSetting.FindPropertyRelative("PartName").stringValue = part.Name;
                    partSetting.FindPropertyRelative("BevelWidth").floatValue = -1f;
                    partSetting.FindPropertyRelative("Smoothness").floatValue = -1f;
                    partSettingsDict[part.Name] = partSetting;
                }

                EditorGUI.indentLevel++;
                DrawPartSettings(partSetting);
                EditorGUI.indentLevel--;
            }
        }

        private const float _maxBevelWidth = 9999f;

        private void DrawPartSettings(SerializedProperty partSetting)
        {
            var defaultBevelWidth = _defaultBevelWidth.floatValue;
            var defaultSmoothness = _defaultSmoothness.floatValue;

            var bevelProp = partSetting.FindPropertyRelative("BevelWidth");
            var smoothProp = partSetting.FindPropertyRelative("Smoothness");

            DrawBevelWidthWithFullOption(bevelProp, defaultBevelWidth);
            DrawOverrideFloat("Smoothness", smoothProp, defaultSmoothness);
        }

        private void DrawOverrideFloat(string label, SerializedProperty prop, float defaultValue)
        {
            var currentValue = prop.floatValue;
            var hasOverride = currentValue >= 0f;

            EditorGUILayout.BeginHorizontal();

            EditorGUI.BeginChangeCheck();
            var newHasOverride = EditorGUILayout.Toggle($"Override {label}", hasOverride, GUILayout.ExpandWidth(false));
            if (EditorGUI.EndChangeCheck())
            {
                prop.floatValue = newHasOverride ? defaultValue : -1f;
                hasOverride = newHasOverride;
            }

            if (hasOverride)
            {
                EditorGUI.BeginChangeCheck();
                var newValue = EditorGUILayout.FloatField(prop.floatValue, GUILayout.Width(120));
                if (EditorGUI.EndChangeCheck() && newValue >= 0f)
                    prop.floatValue = newValue;
            }
            else
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.FloatField(defaultValue, GUILayout.Width(120));
                EditorGUI.EndDisabledGroup();
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawBevelWidthWithFullOption(SerializedProperty prop, float defaultValue)
        {
            var currentValue = prop.floatValue;
            var hasOverride = currentValue >= 0f;
            var isFullWidth = currentValue >= _maxBevelWidth;

            // Full Width checkbox
            EditorGUI.BeginChangeCheck();
            var newFullWidth = EditorGUILayout.Toggle("Full Width", isFullWidth);
            if (EditorGUI.EndChangeCheck())
            {
                prop.floatValue = newFullWidth ? _maxBevelWidth : -1f; // -1 = no override
            }

            // Bevel Width override (disabled when Full Width is checked)
            EditorGUI.BeginDisabledGroup(isFullWidth);

            EditorGUILayout.BeginHorizontal();

            EditorGUI.BeginChangeCheck();
            var newHasOverride =
                EditorGUILayout.Toggle("Override Bevel Width", hasOverride, GUILayout.ExpandWidth(false));
            if (EditorGUI.EndChangeCheck())
            {
                prop.floatValue = newHasOverride ? defaultValue : -1f;
                hasOverride = newHasOverride;
            }

            if (hasOverride)
            {
                EditorGUI.BeginChangeCheck();
                var newValue = EditorGUILayout.FloatField(prop.floatValue, GUILayout.Width(120));
                if (EditorGUI.EndChangeCheck() && newValue >= 0f)
                    prop.floatValue = newValue;
            }
            else
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.FloatField(defaultValue, GUILayout.Width(120));
                EditorGUI.EndDisabledGroup();
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUI.EndDisabledGroup();
        }
    }
}
