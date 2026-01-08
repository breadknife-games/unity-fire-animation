using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace FireAnimation
{
    internal struct UnifiedDimensions
    {
        public int MaxWidth;
        public int MaxHeight;
        public int FrameCount;
    }

    internal struct TextureAtlasResult
    {
        public Texture2D Texture;
        public int FrameCount;
    }

    internal struct FrameBufferData
    {
        public NativeArray<Color32> Buffer;
        public int Width;
        public int Height;
        public Vector2 Pivot;
    }

    internal static class TextureAtlasGenerator
    {
        public static UnifiedDimensions CalculateUnifiedDimensions(
            AnimationTexture albedoTexture,
            List<AnimationTexture> secondaryTextures,
            int documentWidth,
            int documentHeight)
        {
            var maxWidth = 0;
            var maxHeight = 0;
            var frameCount = 0;

            void CheckFrameDimensions(AnimationFrame frame)
            {
                if (frame.BitmapLayers == null || frame.BitmapLayers.Count == 0)
                    return;

                var bounds = LayerMerger.CalculateMergedBounds(frame.BitmapLayers);
                maxWidth = Math.Max(maxWidth, bounds.width);
                maxHeight = Math.Max(maxHeight, bounds.height);
            }

            if (albedoTexture != null && albedoTexture.Frames.Count > 0)
            {
                frameCount = albedoTexture.Frames.Count;
                foreach (var frame in albedoTexture.Frames)
                    CheckFrameDimensions(frame);
            }

            foreach (var texture in secondaryTextures)
            {
                if (texture.Frames.Count != frameCount && frameCount > 0)
                    continue;

                if (frameCount == 0)
                    frameCount = texture.Frames.Count;

                foreach (var frame in texture.Frames)
                    CheckFrameDimensions(frame);
            }

            return new UnifiedDimensions
            {
                MaxWidth = maxWidth,
                MaxHeight = maxHeight,
                FrameCount = frameCount
            };
        }

        public static TextureAtlasResult GenerateTextureAtlas(
            AssetImportContext ctx,
            string animationName,
            AnimationTexture animTexture,
            UnifiedDimensions dimensions,
            int documentWidth,
            int documentHeight,
            FilterMode filterMode,
            TextureWrapMode wrapMode)
        {
            var frames = animTexture.Frames;
            if (frames.Count == 0 || frames[0].BitmapLayers == null)
                return default;

            var frameData = new List<FrameBufferData>();
            try
            {
                foreach (var frame in frames)
                {
                    if (frame.BitmapLayers == null || frame.BitmapLayers.Count == 0)
                    {
                        frameData.Add(new FrameBufferData
                        {
                            Buffer = new NativeArray<Color32>(0, Allocator.Temp),
                            Width = 0,
                            Height = 0,
                            Pivot = Vector2.zero
                        });
                        continue;
                    }

                    var buffer = LayerMerger.MergeLayersCropped(
                        frame.BitmapLayers,
                        documentWidth,
                        documentHeight,
                        out var width,
                        out var height,
                        out var pivotOffset);

                    frameData.Add(new FrameBufferData
                    {
                        Buffer = buffer,
                        Width = width,
                        Height = height,
                        Pivot = pivotOffset
                    });
                }

                var atlasWidth = dimensions.MaxWidth * dimensions.FrameCount;
                var atlasHeight = dimensions.MaxHeight;
                var atlasBuffer = new NativeArray<Color32>(atlasWidth * atlasHeight, Allocator.Temp);

                try
                {
                    unsafe
                    {
                        var ptr = (Color32*)atlasBuffer.GetUnsafePtr();
                        for (var i = 0; i < atlasBuffer.Length; i++)
                            ptr[i] = new Color32(0, 0, 0, 0);
                    }

                    for (var frameIndex = 0; frameIndex < frameData.Count; frameIndex++)
                    {
                        var data = frameData[frameIndex];
                        if (data.Width == 0 || data.Height == 0)
                            continue;

                        var offsetX = frameIndex * dimensions.MaxWidth;
                        var padX = (dimensions.MaxWidth - data.Width) / 2;
                        var padY = (dimensions.MaxHeight - data.Height) / 2;

                        unsafe
                        {
                            var srcPtr = (Color32*)data.Buffer.GetUnsafeReadOnlyPtr();
                            var dstPtr = (Color32*)atlasBuffer.GetUnsafePtr();

                            for (var y = 0; y < data.Height; y++)
                            {
                                for (var x = 0; x < data.Width; x++)
                                {
                                    var srcIndex = y * data.Width + x;
                                    var dstX = offsetX + padX + x;
                                    var dstY = padY + y;
                                    var dstIndex = dstY * atlasWidth + dstX;
                                    dstPtr[dstIndex] = srcPtr[srcIndex];
                                }
                            }
                        }
                    }

                    var textureTypeName = TextureTypeHelper.GetDisplayName(animTexture.Type);
                    var texture = new Texture2D(atlasWidth, atlasHeight, TextureFormat.RGBA32, false)
                    {
                        name = $"{animationName}_{textureTypeName}",
                        filterMode = filterMode,
                        wrapMode = wrapMode
                    };
                    texture.SetPixelData(atlasBuffer, 0);
                    texture.Apply(false, true);

                    var textureId = $"{animationName}_{textureTypeName}_Texture";
                    ctx.AddObjectToAsset(textureId, texture);

                    return new TextureAtlasResult
                    {
                        Texture = texture,
                        FrameCount = frames.Count
                    };
                }
                finally
                {
                    if (atlasBuffer.IsCreated)
                        atlasBuffer.Dispose();
                }
            }
            finally
            {
                for (var i = 0; i < frameData.Count; i++)
                {
                    if (frameData[i].Buffer.IsCreated)
                        frameData[i].Buffer.Dispose();
                }
            }
        }
    }
}
