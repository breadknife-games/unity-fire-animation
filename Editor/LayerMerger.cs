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
                for (var i = 0; i < outputBuffer.Length; i++)
                {
                    ptr[i] = new Color32(0, 0, 0, 0);
                }
            }

            for (var i = layers.Count - 1; i >= 0; i--)
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

                for (var y = 0; y < outputHeight; y++)
                {
                    var srcY = bounds.y + (outputHeight - 1 - y);
                    var dstY = y;

                    for (var x = 0; x < outputWidth; x++)
                    {
                        var srcX = bounds.x + x;
                        var srcIndex = srcY * documentWidth + srcX;
                        var dstIndex = dstY * outputWidth + x;

                        dstPtr[dstIndex] = srcPtr[srcIndex];
                    }
                }
            }

            return croppedBuffer;
        }

        public static RectInt CalculateMergedBounds(IReadOnlyList<BitmapLayer> layers)
        {
            var minX = int.MaxValue;
            var minY = int.MaxValue;
            var maxX = int.MinValue;
            var maxY = int.MinValue;

            var hasValidLayer = false;

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
            var opacity = layer.Opacity / 255f;

            var srcPtr = (Color32*)surface.color.GetUnsafeReadOnlyPtr();
            var dstPtr = (Color32*)output.GetUnsafePtr();

            var layerWidth = surface.width;
            var layerHeight = surface.height;

            for (var ly = 0; ly < layerHeight; ly++)
            {
                var docY = layerRect.Y + ly;
                if (docY < 0 || docY >= documentHeight)
                    continue;

                for (var lx = 0; lx < layerWidth; lx++)
                {
                    var docX = layerRect.X + lx;
                    if (docX < 0 || docX >= documentWidth)
                        continue;

                    var srcIndex = ly * layerWidth + lx;
                    var dstIndex = docY * documentWidth + docX;

                    var src = srcPtr[srcIndex];
                    var dst = dstPtr[dstIndex];

                    var srcAlpha = (src.a / 255f) * opacity;
                    var dstAlpha = dst.a / 255f;
                    var outAlpha = srcAlpha + dstAlpha * (1f - srcAlpha);

                    if (outAlpha > 0f)
                    {
                        var srcContrib = srcAlpha / outAlpha;
                        var dstContrib = dstAlpha * (1f - srcAlpha) / outAlpha;

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

