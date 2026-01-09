using System.Collections.Generic;
using System.Linq;
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
        private SerializedProperty _edgeInset;
        private SerializedProperty _animationSettings;
        private SerializedProperty _groupPartSettings;

        private readonly Dictionary<string, bool> _foldoutStates = new Dictionary<string, bool>();
        private readonly Dictionary<string, Sprite[]> _spriteCache = new Dictionary<string, Sprite[]>();
        private readonly Dictionary<string, Texture2D> _normalTextureCache = new Dictionary<string, Texture2D>();
        private string _cachedAssetPath;

        private const float _thumbnailSize = 32f;
        private const float _expandedPreviewSize = 64f;
        private const float _maxPreviewRowWidth = 320f;

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
            _edgeInset = serializedObject.FindProperty("_edgeInset");
            _animationSettings = serializedObject.FindProperty("_animationSettings");
            _groupPartSettings = serializedObject.FindProperty("_groupPartSettings");

            RefreshSpriteCache();
        }

        private void RefreshSpriteCache()
        {
            var importer = (PsdImporter)target;
            var assetPath = importer.assetPath;

            if (_cachedAssetPath == assetPath && _spriteCache.Count > 0)
                return;

            _spriteCache.Clear();
            _normalTextureCache.Clear();
            _cachedAssetPath = assetPath;

            if (string.IsNullOrEmpty(assetPath))
                return;

            var allAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            var sprites = allAssets.OfType<Sprite>().ToArray();
            var textures = allAssets.OfType<Texture2D>().ToArray();

            // Group sprites by animation name (format: AnimationName_Albedo_0, AnimationName_Albedo_1, etc.)
            var spriteGroups = new Dictionary<string, List<Sprite>>();
            foreach (var sprite in sprites)
            {
                var nameParts = sprite.name.Split('_');
                if (nameParts.Length >= 3 && nameParts[^2] == "Albedo")
                {
                    // Reconstruct animation name (everything before _Albedo_N)
                    var animName = string.Join("_", nameParts.Take(nameParts.Length - 2));
                    if (!spriteGroups.ContainsKey(animName))
                        spriteGroups[animName] = new List<Sprite>();
                    spriteGroups[animName].Add(sprite);
                }
            }

            // Sort sprites by frame index and cache
            foreach (var kvp in spriteGroups)
            {
                var sorted = kvp.Value
                    .OrderBy(s =>
                    {
                        var parts = s.name.Split('_');
                        return int.TryParse(parts[^1], out var idx) ? idx : 0;
                    })
                    .ToArray();
                _spriteCache[kvp.Key] = sorted;
            }

            // Cache normal textures (format: AnimationName_Normal)
            foreach (var texture in textures)
            {
                if (!texture.name.EndsWith("_Normal"))
                    continue;

                var animName = texture.name.Substring(0, texture.name.Length - "_Normal".Length);
                _normalTextureCache[animName] = texture;
            }
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
            EditorGUILayout.PropertyField(_edgeInset,
                new GUIContent("Edge Inset", "Distance inward from edge where normals start (reduces fringing)"));

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
            public List<string> UsedInAnimations; // Which animations use this part
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
            public List<AnimationInfo> Animations;
            public List<PartInfo> UniqueParts; // All unique parts across all animations
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
                    Animations = new List<AnimationInfo>(),
                    UniqueParts = new List<PartInfo>()
                };

                // Track which animations use each part
                var partUsage = new Dictionary<string, List<string>>();

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

                    // Track part usage
                    foreach (var part in anim.Parts)
                    {
                        if (!partUsage.ContainsKey(part.Name))
                            partUsage[part.Name] = new List<string>();
                        partUsage[part.Name].Add(anim.Name);
                    }

                    groupInfo.Animations.Add(new AnimationInfo
                    {
                        Name = anim.Name,
                        FrameCount = frameCount,
                        TextureTypes = textureTypes
                    });
                }

                // Build unique parts list
                foreach (var kvp in partUsage)
                {
                    groupInfo.UniqueParts.Add(new PartInfo
                    {
                        Name = kvp.Key,
                        UsedInAnimations = kvp.Value
                    });
                }

                groupInfos.Add(groupInfo);
            }

            return groupInfos;
        }

        private void DrawGroupsList(List<GroupInfo> groupInfos)
        {
            // Build animation settings lookup
            var animSettingsDict = new Dictionary<string, SerializedProperty>();
            if (_animationSettings is { isArray: true })
            {
                for (var i = 0; i < _animationSettings.arraySize; i++)
                {
                    var setting = _animationSettings.GetArrayElementAtIndex(i);
                    var nameProp = setting.FindPropertyRelative("AnimationName");
                    if (nameProp != null && !string.IsNullOrEmpty(nameProp.stringValue))
                    {
                        animSettingsDict[nameProp.stringValue] = setting;
                    }
                }
            }

            // Build group part settings lookup
            var groupPartSettingsDict = new Dictionary<string, SerializedProperty>();
            if (_groupPartSettings is { isArray: true })
            {
                for (var i = 0; i < _groupPartSettings.arraySize; i++)
                {
                    var setting = _groupPartSettings.GetArrayElementAtIndex(i);
                    var nameProp = setting.FindPropertyRelative("GroupName");
                    if (nameProp != null && !string.IsNullOrEmpty(nameProp.stringValue))
                    {
                        groupPartSettingsDict[nameProp.stringValue] = setting;
                    }
                }
            }

            foreach (var groupInfo in groupInfos)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(groupInfo.Name, EditorStyles.boldLabel);

                EditorGUI.indentLevel++;

                // Draw animations
                foreach (var animInfo in groupInfo.Animations)
                {
                    DrawAnimationInfo(animInfo, animSettingsDict);
                }

                // Draw sprite parts section (after all animations)
                if (groupInfo.UniqueParts is { Count: > 0 })
                {
                    EditorGUILayout.Space(5);
                    DrawGroupPartsSection(groupInfo, groupPartSettingsDict);
                }

                EditorGUI.indentLevel--;

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2);
            }
        }

        private void DrawAnimationInfo(AnimationInfo animInfo, Dictionary<string, SerializedProperty> settingsDict)
        {
            _foldoutStates.TryAdd(animInfo.Name, false);

            // Get cached sprites for this animation
            _spriteCache.TryGetValue(animInfo.Name, out var sprites);
            var hasPreview = sprites is { Length: > 0 };

            EditorGUILayout.BeginHorizontal();
            var isExpanded = EditorGUILayout.Foldout(_foldoutStates[animInfo.Name], animInfo.Name, true);
            _foldoutStates[animInfo.Name] = isExpanded;

            // Show small thumbnail when collapsed
            if (!isExpanded && hasPreview)
            {
                GUILayout.FlexibleSpace();
                DrawSpriteThumbnail(sprites[0], _thumbnailSize);
            }

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

            // Show expanded sprite preview row
            if (hasPreview)
            {
                DrawExpandedSpritePreview(sprites);
                EditorGUILayout.Space(5);
            }

            // Show normal map preview
            if (_normalTextureCache.TryGetValue(animInfo.Name, out var normalTexture) && normalTexture != null)
            {
                DrawNormalMapPreview(normalTexture, sprites?.Length ?? animInfo.FrameCount);
                EditorGUILayout.Space(5);
            }

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

            EditorGUILayout.EndVertical();
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(2);
        }

        private void DrawGroupPartsSection(GroupInfo groupInfo,
            Dictionary<string, SerializedProperty> groupPartSettingsDict)
        {
            var partsFoldoutKey = $"group_parts_{groupInfo.Name}";
            _foldoutStates.TryAdd(partsFoldoutKey, false);

            EditorGUILayout.BeginHorizontal();
            var isExpanded = EditorGUILayout.Foldout(_foldoutStates[partsFoldoutKey], "Sprite Parts", true);
            _foldoutStates[partsFoldoutKey] = isExpanded;

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"{groupInfo.UniqueParts.Count} parts", EditorStyles.miniLabel,
                GUILayout.Width(60));
            EditorGUILayout.EndHorizontal();

            if (!isExpanded)
                return;

            EditorGUI.indentLevel++;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Get or create group part settings
            if (!groupPartSettingsDict.TryGetValue(groupInfo.Name, out var groupPartSetting))
            {
                _groupPartSettings.arraySize++;
                groupPartSetting = _groupPartSettings.GetArrayElementAtIndex(_groupPartSettings.arraySize - 1);
                groupPartSetting.FindPropertyRelative("GroupName").stringValue = groupInfo.Name;
                groupPartSettingsDict[groupInfo.Name] = groupPartSetting;
            }

            var partSettingsProp = groupPartSetting.FindPropertyRelative("PartSettings");

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

            foreach (var part in groupInfo.UniqueParts)
            {
                var partFoldoutKey = $"part_{groupInfo.Name}_{part.Name}";
                _foldoutStates.TryAdd(partFoldoutKey, false);

                EditorGUILayout.BeginHorizontal();
                var isPartExpanded = EditorGUILayout.Foldout(_foldoutStates[partFoldoutKey], part.Name, true);
                _foldoutStates[partFoldoutKey] = isPartExpanded;

                GUILayout.FlexibleSpace();

                // Show which animations use this part
                var usageText = part.UsedInAnimations.Count == groupInfo.Animations.Count
                    ? "all animations"
                    : $"{part.UsedInAnimations.Count} animation{(part.UsedInAnimations.Count != 1 ? "s" : "")}";
                EditorGUILayout.LabelField(usageText, EditorStyles.miniLabel, GUILayout.Width(100));
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

                // Show which animations use this part (detailed)
                if (part.UsedInAnimations.Count < groupInfo.Animations.Count)
                {
                    var animList = string.Join(", ", part.UsedInAnimations);
                    EditorGUILayout.LabelField($"Used in: {animList}", EditorStyles.miniLabel);
                }

                DrawPartSettings(partSetting);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
            EditorGUI.indentLevel--;
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

        private void DrawSpriteThumbnail(Sprite sprite, float size)
        {
            if (sprite == null || sprite.texture == null)
                return;

            var rect = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
            DrawSpriteInRect(rect, sprite);
        }

        private void DrawExpandedSpritePreview(Sprite[] sprites)
        {
            if (sprites == null || sprites.Length == 0)
                return;

            var previewSize = _expandedPreviewSize;
            var spritesPerRow = Mathf.Max(1, Mathf.FloorToInt(_maxPreviewRowWidth / (previewSize + 4)));
            var rowCount = Mathf.CeilToInt((float)sprites.Length / spritesPerRow);

            EditorGUILayout.LabelField("Albedo Frames", EditorStyles.miniLabel);

            for (var row = 0; row < rowCount; row++)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(EditorGUI.indentLevel * 15);

                for (var col = 0; col < spritesPerRow; col++)
                {
                    var idx = row * spritesPerRow + col;
                    if (idx >= sprites.Length)
                        break;

                    var sprite = sprites[idx];
                    var rect = GUILayoutUtility.GetRect(previewSize, previewSize,
                        GUILayout.Width(previewSize), GUILayout.Height(previewSize));

                    // Draw background
                    EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f, 1f));

                    // Draw sprite
                    DrawSpriteInRect(rect, sprite);

                    // Draw frame number
                    var labelRect = new Rect(rect.x, rect.yMax - 14, rect.width, 14);
                    var labelStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.LowerRight,
                        normal = { textColor = new Color(1f, 1f, 1f, 0.7f) }
                    };
                    GUI.Label(labelRect, idx.ToString(), labelStyle);

                    GUILayout.Space(2);
                }

                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawNormalMapPreview(Texture2D normalTexture, int frameCount)
        {
            if (normalTexture == null || frameCount <= 0)
                return;

            // Use same size as albedo preview for consistency
            var previewSize = _expandedPreviewSize;
            var spritesPerRow = Mathf.Max(1, Mathf.FloorToInt(_maxPreviewRowWidth / (previewSize + 4)));
            var rowCount = Mathf.CeilToInt((float)frameCount / spritesPerRow);

            // Calculate frame dimensions from the atlas
            var frameWidth = normalTexture.width / frameCount;
            var frameHeight = normalTexture.height;
            var frameAspect = (float)frameWidth / frameHeight;

            EditorGUILayout.LabelField("Normal Map", EditorStyles.miniLabel);

            for (var row = 0; row < rowCount; row++)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(EditorGUI.indentLevel * 15);

                for (var col = 0; col < spritesPerRow; col++)
                {
                    var idx = row * spritesPerRow + col;
                    if (idx >= frameCount)
                        break;

                    var rect = GUILayoutUtility.GetRect(previewSize, previewSize,
                        GUILayout.Width(previewSize), GUILayout.Height(previewSize));

                    // Draw background
                    EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 1f, 0.3f));

                    // Calculate draw rect maintaining aspect ratio (same as sprites)
                    Rect drawRect;
                    if (frameAspect > 1f)
                    {
                        // Frame is wider - fit to width
                        var height = rect.width / frameAspect;
                        drawRect = new Rect(rect.x, rect.y + (rect.height - height) / 2, rect.width, height);
                    }
                    else
                    {
                        // Frame is taller - fit to height
                        var width = rect.height * frameAspect;
                        drawRect = new Rect(rect.x + (rect.width - width) / 2, rect.y, width, rect.height);
                    }

                    // Calculate UV for this frame
                    var uvRect = new Rect(
                        (float)idx / frameCount,
                        0f,
                        1f / frameCount,
                        1f);

                    GUI.DrawTextureWithTexCoords(drawRect, normalTexture, uvRect);

                    // Draw frame number
                    var labelRect = new Rect(rect.x, rect.yMax - 14, rect.width, 14);
                    var labelStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.LowerRight,
                        normal = { textColor = new Color(0.2f, 0.2f, 0.2f, 0.9f) }
                    };
                    GUI.Label(labelRect, idx.ToString(), labelStyle);

                    GUILayout.Space(2);
                }

                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
        }

        private static void DrawSpriteInRect(Rect rect, Sprite sprite)
        {
            if (sprite == null || sprite.texture == null)
                return;

            var spriteRect = sprite.rect;
            var tex = sprite.texture;

            // Calculate UV coordinates
            var uvRect = new Rect(
                spriteRect.x / tex.width,
                spriteRect.y / tex.height,
                spriteRect.width / tex.width,
                spriteRect.height / tex.height);

            // Maintain aspect ratio
            var spriteAspect = spriteRect.width / spriteRect.height;
            var rectAspect = rect.width / rect.height;

            Rect drawRect;
            if (spriteAspect > rectAspect)
            {
                // Sprite is wider - fit to width
                var height = rect.width / spriteAspect;
                drawRect = new Rect(rect.x, rect.y + (rect.height - height) / 2, rect.width, height);
            }
            else
            {
                // Sprite is taller - fit to height
                var width = rect.height * spriteAspect;
                drawRect = new Rect(rect.x + (rect.width - width) / 2, rect.y, width, rect.height);
            }

            GUI.DrawTextureWithTexCoords(drawRect, tex, uvRect);
        }
    }
}
