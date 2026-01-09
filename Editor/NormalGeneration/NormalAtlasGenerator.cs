using System.Collections.Generic;
using Unity.Collections;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace FireAnimation.NormalGeneration
{
    /// <summary>
    /// Generates normal map atlases from SpriteParts.
    /// Handles the full pipeline: part processing, normal generation, and atlas packing.
    /// </summary>
    internal static class NormalAtlasGenerator
    {
        public struct NormalAtlasSettings
        {
            public float DefaultBevelWidth;
            public float DefaultSmoothness;
            public float EdgeInset;
            public FilterMode FilterMode;
            public TextureWrapMode WrapMode;
            public Dictionary<string, SpritePartSettings> PartSettings;
        }

        /// <summary>
        /// Generate a normal atlas for an animation from its parts.
        /// </summary>
        public static Texture2D GenerateNormalAtlas(
            AssetImportContext ctx,
            SpriteAnimation animation,
            UnifiedDimensions dimensions,
            int documentWidth,
            int documentHeight,
            NormalAtlasSettings settings)
        {
            if (animation.Parts.Count == 0)
                return null;

            var lightBlockTexture = animation.Textures.Find(t => t.Type == TextureType.LightBlock);

            var frameCount = dimensions.FrameCount;
            var frameWidth = dimensions.MaxWidth;
            var frameHeight = dimensions.MaxHeight;

            var atlasWidth = frameWidth * frameCount;
            var atlasHeight = frameHeight;
            var atlasPixels = new Color32[atlasWidth * atlasHeight];

            // Initialize to flat normal
            var flatNormal = new Color32(128, 128, 255, 255);
            for (var i = 0; i < atlasPixels.Length; i++)
                atlasPixels[i] = flatNormal;

            // Process each frame
            for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                var frameNormals = GenerateFrameNormals(
                    animation.Parts,
                    lightBlockTexture,
                    frameIndex,
                    documentWidth,
                    documentHeight,
                    settings);

                if (frameNormals == null)
                    continue;

                CopyFrameToAtlas(
                    frameNormals,
                    documentWidth,
                    documentHeight,
                    atlasPixels,
                    atlasWidth,
                    frameIndex * frameWidth,
                    frameWidth,
                    frameHeight);
            }

            var atlas = new Texture2D(atlasWidth, atlasHeight, TextureFormat.RGBA32, false, linear: true)
            {
                filterMode = settings.FilterMode,
                wrapMode = settings.WrapMode,
                name = $"{animation.Name}_Normal"
            };
            atlas.SetPixels32(atlasPixels);
            atlas.Apply();

            ctx.AddObjectToAsset($"{animation.Name}_Normal_Atlas", atlas);

            return atlas;
        }

        private static Color32[] GenerateFrameNormals(
            List<SpritePart> parts,
            AnimationTexture lightBlockTexture,
            int frameIndex,
            int documentWidth,
            int documentHeight,
            NormalAtlasSettings settings)
        {
            NativeArray<Color32> lightBlockPixels = default;
            try
            {
                if (lightBlockTexture != null && frameIndex < lightBlockTexture.Frames.Count)
                {
                    var lbFrame = lightBlockTexture.Frames[frameIndex];
                    if (lbFrame.BitmapLayers is { Count: > 0 })
                    {
                        lightBlockPixels = new NativeArray<Color32>(
                            documentWidth * documentHeight,
                            Allocator.Temp);
                        LayerMerger.MergeLayers(
                            lbFrame.BitmapLayers,
                            documentWidth,
                            documentHeight,
                            lightBlockPixels,
                            out _);
                    }
                }

                var partNormals = new List<Color32[]>();

                foreach (var part in parts)
                {
                    if (frameIndex >= part.Frames.Count)
                        continue;

                    var frame = part.Frames[frameIndex];
                    if (frame.BitmapLayers == null || frame.BitmapLayers.Count == 0)
                        continue;

                    var bevelWidth = settings.DefaultBevelWidth;
                    var smoothness = settings.DefaultSmoothness;

                    if (settings.PartSettings != null &&
                        settings.PartSettings.TryGetValue(part.Name, out var partSetting))
                    {
                        if (partSetting.BevelWidth >= 0)
                            bevelWidth = partSetting.BevelWidth;
                        if (partSetting.Smoothness >= 0)
                            smoothness = partSetting.Smoothness;
                    }

                    using var partPixels = new NativeArray<Color32>(
                        documentWidth * documentHeight,
                        Allocator.Temp);
                    LayerMerger.MergeLayers(
                        frame.BitmapLayers,
                        documentWidth,
                        documentHeight,
                        partPixels,
                        out _);

                    var normals = SpritePartNormalGenerator.GeneratePartNormals(
                        partPixels,
                        lightBlockPixels,
                        documentWidth,
                        documentHeight,
                        bevelWidth,
                        smoothness,
                        settings.EdgeInset,
                        out _);

                    if (normals != null)
                        partNormals.Add(normals);
                }

                if (partNormals.Count == 0)
                    return null;

                return SpritePartNormalGenerator.MergeNormalMaps(partNormals, documentWidth, documentHeight);
            }
            finally
            {
                if (lightBlockPixels.IsCreated)
                    lightBlockPixels.Dispose();
            }
        }

        /// <summary>
        /// Copy frame pixels to the atlas, centering content within the frame slot.
        /// </summary>
        private static void CopyFrameToAtlas(
            Color32[] framePixels,
            int documentWidth,
            int documentHeight,
            Color32[] atlasPixels,
            int atlasWidth,
            int atlasX,
            int targetWidth,
            int targetHeight)
        {
            // Find bounds of non-transparent pixels
            var minX = documentWidth;
            var maxX = 0;
            var minY = documentHeight;
            var maxY = 0;
            var hasPixels = false;

            for (var y = 0; y < documentHeight; y++)
            {
                for (var x = 0; x < documentWidth; x++)
                {
                    var index = y * documentWidth + x;
                    if (framePixels[index].a > 0)
                    {
                        minX = Mathf.Min(minX, x);
                        maxX = Mathf.Max(maxX, x);
                        minY = Mathf.Min(minY, y);
                        maxY = Mathf.Max(maxY, y);
                        hasPixels = true;
                    }
                }
            }

            if (!hasPixels)
                return;

            var contentWidth = maxX - minX + 1;
            var contentHeight = maxY - minY + 1;

            // Center in target frame
            var padX = (targetWidth - contentWidth) / 2;
            var padY = (targetHeight - contentHeight) / 2;

            for (var y = 0; y < contentHeight; y++)
            {
                for (var x = 0; x < contentWidth; x++)
                {
                    var srcX = minX + x;
                    var srcY = minY + y;
                    var srcIndex = srcY * documentWidth + srcX;

                    var dstX = atlasX + padX + x;
                    var dstY = padY + y;
                    var dstIndex = dstY * atlasWidth + dstX;

                    if (srcIndex >= 0 && srcIndex < framePixels.Length &&
                        dstIndex >= 0 && dstIndex < atlasPixels.Length)
                    {
                        atlasPixels[dstIndex] = framePixels[srcIndex];
                    }
                }
            }
        }
    }
}
