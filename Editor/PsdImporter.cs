using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FireAnimation.NormalGeneration;
using PaintDotNet.Data.PhotoshopFileType;
using PDNWrapper;
using PhotoshopFile;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace FireAnimation
{
    [ScriptedImporter(100, new string[] { }, new[] { ".psd" }, AllowCaching = true)]
    public class PsdImporter : ScriptedImporter
    {
        [SerializeField] private int _pixelsPerUnit = 100;
        [SerializeField] private FilterMode _filterMode = FilterMode.Point;
        [SerializeField] private SpriteMeshType _spriteMeshType = SpriteMeshType.FullRect;
        [SerializeField] private TextureWrapMode _wrapMode = TextureWrapMode.Clamp;
        [SerializeField] private float _framesPerSecond = 12f;

        [SerializeField] internal ImportMetadata Metadata;
        [SerializeField] private List<AnimationSettings> _animationSettings = new List<AnimationSettings>();

        public override void OnImportAsset(AssetImportContext ctx)
        {
            Document document = null;
            PsdFile psdFile = null;

            try
            {
                using var stream = new FileStream(ctx.assetPath, FileMode.Open, FileAccess.Read);
                document = PsdLoad.Load(stream);

                using var stream2 = new FileStream(ctx.assetPath, FileMode.Open, FileAccess.Read);
                var loadContext = new DocumentLoadContext();
                psdFile = new PsdFile(stream2, loadContext);

                var parser = new PsdParser();
                var psdFileName = Path.GetFileNameWithoutExtension(ctx.assetPath);
                var groups = parser.ParsePsd(psdFile, psdFileName);
                parser.ResolveLayersFromDocument(groups, document);

                Metadata ??= new ImportMetadata();
                Metadata.Groups = groups;

                InitializeAnimationSettings(groups);
                GenerateAssets(ctx, groups, document.width, document.height);
            }
            catch (Exception e)
            {
                ctx.LogImportError($"Failed to import PSD: {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                psdFile?.Cleanup();
                document?.Dispose();
            }
        }

        private void GenerateAssets(
            AssetImportContext ctx,
            List<GameObjectGroup> groups,
            int documentWidth,
            int documentHeight)
        {
            var mainAsset = ScriptableObject.CreateInstance<FireAnimationAsset>();
            mainAsset.name = Path.GetFileNameWithoutExtension(ctx.assetPath);

            var allGroups = new List<FireAnimationAsset.GroupData>();

            foreach (var group in groups)
            {
                var groupData = GenerateGroupAssets(ctx, group, documentWidth, documentHeight);
                if (groupData != null)
                    allGroups.Add(groupData);
            }

            mainAsset.Groups = allGroups.ToArray();
            ctx.AddObjectToAsset("main", mainAsset);
            ctx.SetMainObject(mainAsset);

            IconHelper.SetPsdIcon(mainAsset);
            AnimationGenerator.GenerateUnityAnimations(ctx, mainAsset, _animationSettings, _framesPerSecond);
        }

        private FireAnimationAsset.GroupData GenerateGroupAssets(
            AssetImportContext ctx,
            GameObjectGroup group,
            int documentWidth,
            int documentHeight)
        {
            var animationDataList = new List<FireAnimationAsset.AnimationData>();

            foreach (var animation in group.Animations)
            {
                var animData = GenerateAnimationAssets(ctx, animation, documentWidth, documentHeight);
                if (animData != null)
                    animationDataList.Add(animData);
            }

            if (animationDataList.Count == 0)
                return null;

            return new FireAnimationAsset.GroupData
            {
                Name = group.Name,
                Color = group.Color,
                Animations = animationDataList.ToArray()
            };
        }

        private FireAnimationAsset.AnimationData GenerateAnimationAssets(
            AssetImportContext ctx,
            SpriteAnimation animation,
            int documentWidth,
            int documentHeight)
        {
            // Albedo is primary, everything else is secondary
            var albedoTexture = animation.Textures.FirstOrDefault(t => t.Type == TextureType.Albedo);
            var secondaryTextures = animation.Textures.Where(t => t.Type != TextureType.Albedo).ToList();

            if (albedoTexture == null || albedoTexture.Frames.Count == 0)
            {
                ctx.LogImportWarning($"Animation '{animation.Name}' has no valid albedo texture. Skipping.");
                return null;
            }

            // Calculate unified dimensions across all textures
            var dimensions = TextureAtlasGenerator.CalculateUnifiedDimensions(
                animation.Textures,
                documentWidth,
                documentHeight);

            if (dimensions.MaxWidth == 0 || dimensions.MaxHeight == 0)
                return null;

            // Generate albedo atlas
            var albedoResult = TextureAtlasGenerator.GenerateTextureAtlas(
                ctx,
                animation.Name,
                albedoTexture,
                dimensions,
                documentWidth,
                documentHeight,
                _filterMode,
                _wrapMode);

            if (albedoResult.Texture == null)
                return null;

            var secondaryTextureDataList = new List<FireAnimationAsset.SecondaryTextureData>();
            var secondarySpriteTextures = new List<SecondarySpriteTexture>();

            // Generate atlases for all secondary textures
            foreach (var texture in secondaryTextures)
            {
                if (texture.Frames.Count == 0)
                    continue;

                // LightingRegion requires special processing
                if (texture.Type == TextureType.LightingRegion)
                {
                    ProcessLightingRegionTexture(
                        ctx,
                        animation.Name,
                        texture,
                        dimensions.FrameCount,
                        documentWidth,
                        documentHeight,
                        secondaryTextureDataList,
                        secondarySpriteTextures);
                    continue;
                }

                var result = TextureAtlasGenerator.GenerateTextureAtlas(
                    ctx,
                    animation.Name,
                    texture,
                    dimensions,
                    documentWidth,
                    documentHeight,
                    _filterMode,
                    _wrapMode);

                if (result.Texture != null)
                {
                    var shaderPropertyName = TextureTypeHelper.GetShaderPropertyName(texture.Type);
                    secondaryTextureDataList.Add(new FireAnimationAsset.SecondaryTextureData
                    {
                        Name = shaderPropertyName,
                        Texture = result.Texture
                    });
                    secondarySpriteTextures.Add(new SecondarySpriteTexture
                    {
                        name = shaderPropertyName,
                        texture = result.Texture
                    });
                }
            }

            // Create sprites from the albedo atlas
            var sprites = new Sprite[albedoResult.FrameCount];
            for (var i = 0; i < albedoResult.FrameCount; i++)
            {
                var rect = new Rect(
                    i * dimensions.MaxWidth,
                    0,
                    dimensions.MaxWidth,
                    dimensions.MaxHeight);
                var pivot = new Vector2(0.5f, 0.5f);

                Sprite sprite;
                if (secondarySpriteTextures.Count > 0)
                {
                    sprite = Sprite.Create(
                        albedoResult.Texture,
                        rect,
                        pivot,
                        _pixelsPerUnit,
                        0,
                        _spriteMeshType,
                        Vector4.zero,
                        false,
                        secondarySpriteTextures.ToArray());
                }
                else
                {
                    sprite = Sprite.Create(
                        albedoResult.Texture,
                        rect,
                        pivot,
                        _pixelsPerUnit,
                        0,
                        _spriteMeshType);
                }

                sprite.name = $"{animation.Name}_Albedo_{i}";
                var spriteId = $"{animation.Name}_Albedo_Sprite_{i}";
                ctx.AddObjectToAsset(spriteId, sprite);
                sprites[i] = sprite;
            }

            return new FireAnimationAsset.AnimationData
            {
                Name = animation.Name,
                Texture = albedoResult.Texture,
                Sprites = sprites,
                SecondaryTextures = secondaryTextureDataList.ToArray()
            };
        }

        private void InitializeAnimationSettings(List<GameObjectGroup> groups)
        {
            var existingSettings = new Dictionary<string, AnimationSettings>();
            foreach (var setting in _animationSettings)
            {
                if (!string.IsNullOrEmpty(setting.AnimationName))
                    existingSettings[setting.AnimationName] = setting;
            }

            var newSettings = new List<AnimationSettings>();
            foreach (var group in groups)
            {
                foreach (var animation in group.Animations)
                {
                    if (existingSettings.TryGetValue(animation.Name, out var existing))
                    {
                        newSettings.Add(existing);
                    }
                    else
                    {
                        newSettings.Add(new AnimationSettings
                        {
                            AnimationName = animation.Name,
                            FramesPerSecond = -1f,
                            LoopTime = true
                        });
                    }
                }
            }

            _animationSettings = newSettings;
        }

        private void ProcessLightingRegionTexture(
            AssetImportContext ctx,
            string animationName,
            AnimationTexture lightingRegionTexture,
            int frameCount,
            int documentWidth,
            int documentHeight,
            List<FireAnimationAsset.SecondaryTextureData> secondaryTextureDataList,
            List<SecondarySpriteTexture> secondarySpriteTextures)
        {
            var textures = new List<AnimationTexture> { lightingRegionTexture };

            // Process each frame
            for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                var debugTexture = LightingRegionProcessor.ProcessLightingRegions(
                    ctx,
                    animationName,
                    textures,
                    documentWidth,
                    documentHeight,
                    frameIndex);

                if (debugTexture != null)
                {
                    // For now, add as a debug texture
                    // Later this will be replaced with proper normal map generation
                    // and added to the secondary textures for sprite rendering
                    ctx.LogImportWarning(
                        $"Generated debug distance field texture for {animationName} frame {frameIndex}: " +
                        $"{debugTexture.width}x{debugTexture.height}");
                }
            }
        }
    }
}
