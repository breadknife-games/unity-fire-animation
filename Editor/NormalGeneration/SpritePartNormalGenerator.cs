using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace FireAnimation.NormalGeneration
{
    /// <summary>
    /// Generates normal maps for SpriteParts.
    /// Each part's albedo defines a region, LightBlock texture provides blocking.
    /// </summary>
    public static class SpritePartNormalGenerator
    {
        /// <summary>
        /// Generate a normal map for a single SpritePart frame.
        /// </summary>
        /// <param name="partPixels">Merged albedo pixels for this part (document-sized)</param>
        /// <param name="lightBlockPixels">LightBlock pixels for blocking (document-sized, can be empty)</param>
        /// <param name="width">Document width</param>
        /// <param name="height">Document height</param>
        /// <param name="bevelWidth">How far the bevel extends inward</param>
        /// <param name="smoothness">Blur radius for normal smoothing (0 = no smoothing)</param>
        /// <param name="outRegion">Optional: outputs the region for debug visualization</param>
        /// <returns>Normal map as Color32 array (document-sized)</returns>
        public static Color32[] GeneratePartNormals(
            NativeArray<Color32> partPixels,
            NativeArray<Color32> lightBlockPixels,
            int width,
            int height,
            float bevelWidth,
            float smoothness,
            out LightingRegion outRegion)
        {
            // Create a single region covering the entire document
            var region = CreateRegionFromPart(partPixels, lightBlockPixels, width, height);
            outRegion = region;

            if (region == null)
                return null;

            // Compute distance field
            DistanceFieldGenerator.ComputeDistanceField(
                region,
                partPixels,
                width,
                height,
                bevelWidth);

            // Generate normals
            NormalMapGenerator.GenerateNormals(region, bevelWidth);

            // Apply smoothing (per-part, before combining)
            // Only smooths within bevel zone - preserves hard edge at bevel boundary
            if (smoothness > 0f)
            {
                NormalSmoother.SmoothNormals(region, bevelWidth, smoothness);
            }

            // Convert to full document-sized array
            var result = new Color32[width * height];
            var flatNormal = new Color32(128, 128, 255, 0); // Transparent flat normal

            for (var i = 0; i < result.Length; i++)
                result[i] = flatNormal;

            // Copy region normals to correct positions
            for (var localY = 0; localY < region.Height; localY++)
            {
                for (var localX = 0; localX < region.Width; localX++)
                {
                    var localIndex = localY * region.Width + localX;

                    if (!region.RegionMask[localIndex])
                        continue;

                    var globalX = region.Bounds.x + localX;
                    var globalY = region.Bounds.y + localY;

                    if (globalX < 0 || globalX >= width || globalY < 0 || globalY >= height)
                        continue;

                    var globalIndex = globalY * width + globalX;
                    result[globalIndex] = region.NormalMap[localIndex];
                }
            }

            return result;
        }

        /// <summary>
        /// Create a LightingRegion from part pixels.
        /// Region = any pixel with alpha > 0
        /// BlackMask = pixels where LightBlock has alpha > 0
        /// </summary>
        private static LightingRegion CreateRegionFromPart(
            NativeArray<Color32> partPixels,
            NativeArray<Color32> lightBlockPixels,
            int width,
            int height)
        {
            var hasLightBlock = lightBlockPixels.IsCreated && lightBlockPixels.Length == width * height;

            // Find bounding box of non-transparent pixels
            var minX = width;
            var maxX = 0;
            var minY = height;
            var maxY = 0;
            var hasPixels = false;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;
                    if (partPixels[index].a > 0)
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
                return null;

            // Create region with bounds
            var bounds = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
            var region = new LightingRegion
            {
                Color = new Color32(255, 255, 255, 255), // Dummy color
                Bounds = bounds,
                RegionMask = new bool[bounds.width * bounds.height],
                BlackMask = new bool[bounds.width * bounds.height]
            };

            // Fill masks
            for (var localY = 0; localY < bounds.height; localY++)
            {
                for (var localX = 0; localX < bounds.width; localX++)
                {
                    var globalX = bounds.x + localX;
                    var globalY = bounds.y + localY;
                    var globalIndex = globalY * width + globalX;
                    var localIndex = localY * bounds.width + localX;

                    // Part of region if alpha > 0
                    if (partPixels[globalIndex].a > 0)
                    {
                        region.RegionMask[localIndex] = true;

                        // Check LightBlock for blocking
                        if (hasLightBlock && lightBlockPixels[globalIndex].a > 0)
                        {
                            region.BlackMask[localIndex] = true;
                        }
                    }
                }
            }

            return region;
        }

        /// <summary>
        /// Merge multiple normal maps, with earlier parts (top in PS layer panel) overwriting later ones.
        /// Parts are parsed in PS layer order (top to bottom), so we process in reverse to get correct stacking.
        /// </summary>
        /// <param name="normalMaps">List of normal maps (document-sized Color32 arrays)</param>
        /// <param name="width">Document width</param>
        /// <param name="height">Document height</param>
        /// <returns>Merged normal map</returns>
        public static Color32[] MergeNormalMaps(
            List<Color32[]> normalMaps,
            int width,
            int height)
        {
            var result = new Color32[width * height];
            var flatNormal = new Color32(128, 128, 255, 0);

            // Initialize to flat transparent
            for (var i = 0; i < result.Length; i++)
                result[i] = flatNormal;

            // Overlay in reverse order so that first part (top in PS) ends up on top
            for (var mapIndex = normalMaps.Count - 1; mapIndex >= 0; mapIndex--)
            {
                var normalMap = normalMaps[mapIndex];
                if (normalMap == null)
                    continue;

                for (var i = 0; i < normalMap.Length && i < result.Length; i++)
                {
                    // Only overwrite if this pixel has alpha (is part of the region)
                    if (normalMap[i].a > 0)
                    {
                        result[i] = normalMap[i];
                    }
                }
            }

            return result;
        }
    }
}
