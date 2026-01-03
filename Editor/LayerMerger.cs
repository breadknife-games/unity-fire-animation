using System;
using System.Collections.Generic;
using PDNWrapper;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace FireAnimation
{
    internal static class LayerMerger
    {
        public static void MergeLayers(
            IReadOnlyList<BitmapLayer> layers,
            int documentWidth,
            int documentHeight,
            NativeArray<Color32> outputBuffer,
            out RectInt bounds)
        {
            bounds = CalculateMergedBounds(layers);

            if (bounds.width <= 0 || bounds.height <= 0)
            {
                bounds = new RectInt(0, 0, 0, 0);
                return;
            }

            unsafe
            {
                var ptr = (Color32*)outputBuffer.GetUnsafePtr();
                for (int i = 0; i < outputBuffer.Length; i++)
                {
                    ptr[i] = new Color32(0, 0, 0, 0);
                }
            }

            for (int i = layers.Count - 1; i >= 0; i--)
            {
                var layer = layers[i];
                if (layer == null || layer.Surface == null || layer.IsEmpty)
                    continue;

                if (!layer.Visible)
                    continue;

                CompositeLayer(layer, documentWidth, documentHeight, outputBuffer);
            }
        }

        public static NativeArray<Color32> MergeLayersCropped(
            IReadOnlyList<BitmapLayer> layers,
            int documentWidth,
            int documentHeight,
            out int outputWidth,
            out int outputHeight,
            out Vector2 pivotOffset)
        {
            using var fullBuffer = new NativeArray<Color32>(documentWidth * documentHeight, Allocator.Temp);
            MergeLayers(layers, documentWidth, documentHeight, fullBuffer, out var bounds);

            if (bounds.width <= 0 || bounds.height <= 0)
            {
                outputWidth = 0;
                outputHeight = 0;
                pivotOffset = Vector2.zero;
                return new NativeArray<Color32>(0, Allocator.Persistent);
            }

            outputWidth = bounds.width;
            outputHeight = bounds.height;
            pivotOffset = new Vector2(bounds.x, documentHeight - bounds.y - bounds.height);

            var croppedBuffer = new NativeArray<Color32>(outputWidth * outputHeight, Allocator.Persistent);

            unsafe
            {
                var srcPtr = (Color32*)fullBuffer.GetUnsafeReadOnlyPtr();
                var dstPtr = (Color32*)croppedBuffer.GetUnsafePtr();

                for (int y = 0; y < outputHeight; y++)
                {
                    int srcY = bounds.y + (outputHeight - 1 - y);
                    int dstY = y;

                    for (int x = 0; x < outputWidth; x++)
                    {
                        int srcX = bounds.x + x;
                        int srcIndex = srcY * documentWidth + srcX;
                        int dstIndex = dstY * outputWidth + x;

                        dstPtr[dstIndex] = srcPtr[srcIndex];
                    }
                }
            }

            return croppedBuffer;
        }

        public static RectInt CalculateMergedBounds(IReadOnlyList<BitmapLayer> layers)
        {
            int minX = int.MaxValue;
            int minY = int.MaxValue;
            int maxX = int.MinValue;
            int maxY = int.MinValue;

            bool hasValidLayer = false;

            foreach (var layer in layers)
            {
                if (layer == null || layer.IsEmpty || !layer.Visible)
                    continue;

                var rect = layer.documentRect;
                minX = Math.Min(minX, rect.X);
                minY = Math.Min(minY, rect.Y);
                maxX = Math.Max(maxX, rect.X + rect.Width);
                maxY = Math.Max(maxY, rect.Y + rect.Height);
                hasValidLayer = true;
            }

            if (!hasValidLayer)
                return new RectInt(0, 0, 0, 0);

            return new RectInt(minX, minY, maxX - minX, maxY - minY);
        }

        private static unsafe void CompositeLayer(
            BitmapLayer layer,
            int documentWidth,
            int documentHeight,
            NativeArray<Color32> output)
        {
            var layerRect = layer.documentRect;
            var surface = layer.Surface;
            float opacity = layer.Opacity / 255f;

            var srcPtr = (Color32*)surface.color.GetUnsafeReadOnlyPtr();
            var dstPtr = (Color32*)output.GetUnsafePtr();

            int layerWidth = surface.width;
            int layerHeight = surface.height;

            for (int ly = 0; ly < layerHeight; ly++)
            {
                int docY = layerRect.Y + ly;
                if (docY < 0 || docY >= documentHeight)
                    continue;

                for (int lx = 0; lx < layerWidth; lx++)
                {
                    int docX = layerRect.X + lx;
                    if (docX < 0 || docX >= documentWidth)
                        continue;

                    int srcIndex = ly * layerWidth + lx;
                    int dstIndex = docY * documentWidth + docX;

                    Color32 src = srcPtr[srcIndex];
                    Color32 dst = dstPtr[dstIndex];

                    float srcAlpha = (src.a / 255f) * opacity;
                    float dstAlpha = dst.a / 255f;
                    float outAlpha = srcAlpha + dstAlpha * (1f - srcAlpha);

                    if (outAlpha > 0f)
                    {
                        float srcContrib = srcAlpha / outAlpha;
                        float dstContrib = dstAlpha * (1f - srcAlpha) / outAlpha;

                        dstPtr[dstIndex] = new Color32(
                            (byte)(src.r * srcContrib + dst.r * dstContrib),
                            (byte)(src.g * srcContrib + dst.g * dstContrib),
                            (byte)(src.b * srcContrib + dst.b * dstContrib),
                            (byte)(outAlpha * 255f)
                        );
                    }
                }
            }
        }
    }
}

