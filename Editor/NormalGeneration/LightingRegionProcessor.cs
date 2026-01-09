using System.Collections.Generic;
using Unity.Collections;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace FireAnimation.NormalGeneration
{
    /// <summary>
    /// Main processor for Lighting Region Maps.
    /// Orchestrates the full pipeline: merge layers → discover regions → compute distance fields → generate normals.
    /// 
    /// Current implementation outputs debug distance field textures for testing.
    /// </summary>
    public static class LightingRegionProcessor
    {
        /// <summary>
        /// Process all LightingRegion textures for an animation frame.
        /// </summary>
        /// <param name="ctx">Asset import context for adding generated textures</param>
        /// <param name="animationName">Name of the parent animation</param>
        /// <param name="lightingRegionTextures">All LightingRegion texture definitions for this animation</param>
        /// <param name="documentWidth">Full PSD document width</param>
        /// <param name="documentHeight">Full PSD document height</param>
        /// <param name="frameIndex">Which frame we're processing</param>
        /// <returns>Generated normal map texture, or null if processing failed</returns>
        public static Texture2D ProcessLightingRegions(
            AssetImportContext ctx,
            string animationName,
            List<AnimationTexture> lightingRegionTextures,
            int documentWidth,
            int documentHeight,
            int frameIndex)
        {
            if (lightingRegionTextures == null || lightingRegionTextures.Count == 0)
                return null;

            Texture2D lastDebugTexture = null;

            // Process each LightingRegion texture layer
            for (var layerOrder = 0; layerOrder < lightingRegionTextures.Count; layerOrder++)
            {
                var animTexture = lightingRegionTextures[layerOrder];
                if (animTexture.Frames.Count <= frameIndex)
                    continue;

                var frame = animTexture.Frames[frameIndex];
                if (frame.BitmapLayers == null || frame.BitmapLayers.Count == 0)
                    continue;

                // Merge layers for this frame
                using var buffer = LayerMerger.MergeLayersCropped(
                    frame.BitmapLayers,
                    documentWidth,
                    documentHeight,
                    out var width,
                    out var height,
                    out _);

                if (width == 0 || height == 0)
                    continue;

                // Log buffer info for debugging
                Debug.Log(
                    $"[LightingRegion] Processing {animTexture.Name} frame {frameIndex}: buffer {width}x{height}");

                // Discover regions in this texture
                var map = RegionDiscovery.DiscoverRegions(
                    buffer,
                    width,
                    height,
                    animTexture.Name,
                    layerOrder);

                Debug.Log($"[LightingRegion] Found {map.Regions.Count} regions");

                if (map.Regions.Count == 0)
                    continue;

                // Log region details
                foreach (var region in map.Regions)
                {
                    var pixelCount = 0;
                    for (var i = 0; i < region.RegionMask.Length; i++)
                        if (region.RegionMask[i])
                            pixelCount++;

                    Debug.Log($"[LightingRegion] Region color=({region.Color.r},{region.Color.g},{region.Color.b}) " +
                              $"bounds={region.Bounds} pixels={pixelCount}");
                }

                // Compute distance field and normals for each region
                foreach (var region in map.Regions)
                {
                    // Step 1: Compute distance field
                    DistanceFieldGenerator.ComputeDistanceField(
                        region,
                        buffer,
                        width,
                        height,
                        1000);

                    // Step 2: Generate normals from distance field
                    NormalMapGenerator.GenerateNormals(region, region.BevelWidth);

                    // Log stats
                    var edgeCount = 0;
                    var reachableCount = 0;
                    for (var i = 0; i < region.DistanceField.Length; i++)
                    {
                        if (!region.RegionMask[i]) continue;
                        if (region.EdgeMask[i]) edgeCount++;
                        else if (region.DistanceField[i] < DistanceFieldGenerator.Infinity) reachableCount++;
                    }

                    Debug.Log($"[LightingRegion] Region processed: edges={edgeCount} interior={reachableCount}");
                }

                // Generate debug distance field texture
                var debugTexture = DistanceFieldGenerator.GenerateDebugTexture(map);
                debugTexture.name = $"{animationName}_DistanceField_Debug_{frameIndex}_{layerOrder}";
                ctx.AddObjectToAsset($"{animationName}_DistanceField_Debug_{frameIndex}_{layerOrder}", debugTexture);

                // Generate actual normal map texture
                var normalTexture = NormalMapGenerator.GenerateTexture(map);
                normalTexture.name = $"{animationName}_NormalMap_{frameIndex}_{layerOrder}";
                ctx.AddObjectToAsset($"{animationName}_NormalMap_{frameIndex}_{layerOrder}", normalTexture);

                lastDebugTexture = normalTexture;
            }

            return lastDebugTexture;
        }

        /// <summary>
        /// Process a single frame from merged layer data (for simpler testing).
        /// </summary>
        public static LightingRegionMap ProcessSingleFrame(
            NativeArray<Color32> pixels,
            int width,
            int height,
            string mapName,
            float bevelWidth = 10f)
        {
            var map = RegionDiscovery.DiscoverRegions(pixels, width, height, mapName, 0);

            foreach (var region in map.Regions)
            {
                region.BevelWidth = bevelWidth;
                DistanceFieldGenerator.ComputeDistanceField(region, pixels, width, height, bevelWidth);
            }

            return map;
        }
    }
}
