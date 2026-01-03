using System;
using System.Collections.Generic;
using System.IO;
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
        [SerializeField] private int pixelsPerUnit = 100;
        [SerializeField] private FilterMode filterMode = FilterMode.Point;
        [SerializeField] private SpriteMeshType spriteMeshType = SpriteMeshType.FullRect;
        [SerializeField] private TextureWrapMode wrapMode = TextureWrapMode.Clamp;
        [SerializeField] private float framesPerSecond = 12f;

        [SerializeField] internal ImportMetadata Metadata;
        [SerializeField] private List<AnimationSettings> animationSettings = new List<AnimationSettings>();

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
                var animations = parser.ParsePsd(psdFile, psdFileName);
                parser.ResolveLayersFromDocument(animations, document);

                if (Metadata == null)
                    Metadata = new ImportMetadata();
                Metadata.Animations = animations;
                InitializeAnimationSettings(animations);
                GenerateAnimationAssets(ctx, animations, document.width, document.height);
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

        private void GenerateAnimationAssets(
            AssetImportContext ctx,
            List<SpriteAnimation> animations,
            int documentWidth,
            int documentHeight)
        {
            var mainAsset = ScriptableObject.CreateInstance<FireAnimationAsset>();
            mainAsset.name = Path.GetFileNameWithoutExtension(ctx.assetPath);

            var allAnimationData = new List<FireAnimationAsset.AnimationData>();

            foreach (var animation in animations)
            {
                var animData = GenerateAnimationWithTextures(
                    ctx,
                    animation,
                    documentWidth,
                    documentHeight);

                if (animData != null)
                    allAnimationData.Add(animData);
            }

            mainAsset.Animations = allAnimationData.ToArray();
            ctx.AddObjectToAsset("main", mainAsset);
            ctx.SetMainObject(mainAsset);

            IconHelper.SetPsdIcon(mainAsset);
            AnimationGenerator.GenerateUnityAnimations(ctx, mainAsset, animationSettings, framesPerSecond);
        }

        private void InitializeAnimationSettings(List<SpriteAnimation> animations)
        {
            var existingSettings = new Dictionary<string, AnimationSettings>();
            foreach (var setting in animationSettings)
            {
                if (!string.IsNullOrEmpty(setting.AnimationName))
                    existingSettings[setting.AnimationName] = setting;
            }

            var newSettings = new List<AnimationSettings>();
            foreach (var animation in animations)
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
                        FramesPerSecond = -1f, // Use default
                        LoopTime = true
                    });
                }
            }

            animationSettings = newSettings;
        }

        private FireAnimationAsset.AnimationData GenerateAnimationWithTextures(
            AssetImportContext ctx,
            SpriteAnimation animation,
            int documentWidth,
            int documentHeight)
        {
            AnimationTexture albedoTexture = null;
            var secondaryTextures = new List<AnimationTexture>();

            foreach (var texture in animation.Textures)
            {
                if (texture.Frames.Count == 0)
                    continue;

                if (texture.Type == TextureType.Albedo)
                    albedoTexture = texture;
                else
                    secondaryTextures.Add(texture);
            }

            if (albedoTexture == null)
            {
                if (secondaryTextures.Count > 0)
                    ctx.LogImportWarning($"Animation '{animation.Name}' has secondary textures but no Albedo texture. Skipping.");
                return null;
            }

            int albedoFrameCount = albedoTexture.Frames.Count;
            foreach (var secTexture in secondaryTextures)
            {
                if (secTexture.Frames.Count != albedoFrameCount)
                {
                    ctx.LogImportWarning(
                        $"Animation '{animation.Name}': Secondary texture '{secTexture.Name}' ({secTexture.Type}) " +
                        $"has {secTexture.Frames.Count} frames, but Albedo texture has {albedoFrameCount} frames. " +
                        $"Frame count mismatch may cause issues.");
                }
            }

            var unifiedDimensions = TextureAtlasGenerator.CalculateUnifiedDimensions(
                albedoTexture,
                secondaryTextures,
                documentWidth,
                documentHeight);

            if (unifiedDimensions.MaxWidth == 0 || unifiedDimensions.MaxHeight == 0)
                return null;

            var albedoResult = TextureAtlasGenerator.GenerateTextureAtlas(
                ctx,
                animation.Name,
                albedoTexture,
                unifiedDimensions,
                documentWidth,
                documentHeight,
                filterMode,
                wrapMode);

            if (albedoResult.Texture == null)
                return null;

            var secondaryTextureDataList = new List<FireAnimationAsset.SecondaryTextureData>();
            var secondarySpriteTextures = new List<SecondarySpriteTexture>();

            foreach (var secTexture in secondaryTextures)
            {
                var secResult = TextureAtlasGenerator.GenerateTextureAtlas(
                    ctx,
                    animation.Name,
                    secTexture,
                    unifiedDimensions,
                    documentWidth,
                    documentHeight,
                    filterMode,
                    wrapMode);

                if (secResult.Texture != null)
                {
                    string shaderPropertyName = TextureTypeHelper.GetShaderPropertyName(secTexture.Type);

                    secondaryTextureDataList.Add(new FireAnimationAsset.SecondaryTextureData
                    {
                        Name = shaderPropertyName,
                        Texture = secResult.Texture
                    });

                    secondarySpriteTextures.Add(new SecondarySpriteTexture
                    {
                        name = shaderPropertyName,
                        texture = secResult.Texture
                    });
                }
            }

            var sprites = new Sprite[albedoResult.FrameCount];
            for (int i = 0; i < albedoResult.FrameCount; i++)
            {
                var rect = new Rect(i * unifiedDimensions.MaxWidth, 0, unifiedDimensions.MaxWidth, unifiedDimensions.MaxHeight);
                var pivot = new Vector2(0.5f, 0.5f);

                Sprite sprite;
                if (secondarySpriteTextures.Count > 0)
                {
                    sprite = Sprite.Create(
                        albedoResult.Texture,
                        rect,
                        pivot,
                        pixelsPerUnit,
                        0,
                        spriteMeshType,
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
                        pixelsPerUnit,
                        0,
                        spriteMeshType);
                }

                string textureTypeName = TextureTypeHelper.GetDisplayName(albedoTexture.Type);
                sprite.name = $"{animation.Name}_{textureTypeName}_{i}";
                string spriteId = $"{animation.Name}_{textureTypeName}_Sprite_{i}";
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

    }
}
