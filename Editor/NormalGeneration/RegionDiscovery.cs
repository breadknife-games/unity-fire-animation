using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace FireAnimation.NormalGeneration
{
    /// <summary>
    /// Discovers connected regions within a Lighting Region Map texture.
    /// Uses flood-fill to identify each connected component of the same color.
    /// </summary>
    public static class RegionDiscovery
    {
        /// <summary>
        /// Discover all connected regions in a texture buffer.
        /// </summary>
        /// <param name="pixels">Source pixel data</param>
        /// <param name="width">Texture width</param>
        /// <param name="height">Texture height</param>
        /// <param name="mapName">Name for the resulting map</param>
        /// <param name="layerOrder">Layer order for composition</param>
        /// <returns>A LightingRegionMap containing all discovered regions</returns>
        public static LightingRegionMap DiscoverRegions(
            NativeArray<Color32> pixels,
            int width,
            int height,
            string mapName,
            int layerOrder)
        {
            var map = new LightingRegionMap
            {
                Name = mapName,
                LayerOrder = layerOrder,
                Width = width,
                Height = height
            };

            // Track which pixels have been visited
            var visited = new bool[width * height];

            // Collect all black pixels globally (needed for edge blocking)
            var globalBlackMask = new bool[width * height];
            for (var i = 0; i < pixels.Length; i++)
            {
                globalBlackMask[i] = IsBlack(pixels[i]);
            }

            // Scan all pixels for unvisited non-transparent, non-black pixels
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;
                    if (visited[index])
                        continue;

                    var pixel = pixels[index];

                    // Skip transparent pixels
                    if (pixel.a == 0)
                    {
                        visited[index] = true;
                        continue;
                    }

                    // Skip black pixels (they're blockers, not regions)
                    if (IsBlack(pixel))
                    {
                        visited[index] = true;
                        continue;
                    }

                    // Found an unvisited colored pixel - flood fill to find connected region
                    var region = FloodFillRegion(pixels, width, height, x, y, pixel, visited, globalBlackMask);
                    if (region != null)
                    {
                        map.Regions.Add(region);
                    }
                }
            }

            return map;
        }

        /// <summary>
        /// Flood fill from a starting pixel to find all connected pixels of the same color.
        /// Also includes adjacent black pixels as part of the region (they act as one-way barriers).
        /// </summary>
        private static LightingRegion FloodFillRegion(
            NativeArray<Color32> pixels,
            int width,
            int height,
            int startX,
            int startY,
            Color32 targetColor,
            bool[] visited,
            bool[] globalBlackMask)
        {
            var regionPixels = new List<Vector2Int>();
            var blackPixelsInRegion = new List<Vector2Int>();
            var queue = new Queue<Vector2Int>();

            queue.Enqueue(new Vector2Int(startX, startY));
            visited[startY * width + startX] = true;

            // Track bounds
            var minX = startX;
            var maxX = startX;
            var minY = startY;
            var maxY = startY;

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var currentIndex = current.y * width + current.x;
                var currentIsBlack = globalBlackMask[currentIndex];

                if (currentIsBlack)
                    blackPixelsInRegion.Add(current);
                else
                    regionPixels.Add(current);

                // Update bounds
                minX = Mathf.Min(minX, current.x);
                maxX = Mathf.Max(maxX, current.x);
                minY = Mathf.Min(minY, current.y);
                maxY = Mathf.Max(maxY, current.y);

                // Check 4 neighbors
                CheckNeighbor(current.x + 1, current.y, currentIsBlack);
                CheckNeighbor(current.x - 1, current.y, currentIsBlack);
                CheckNeighbor(current.x, current.y + 1, currentIsBlack);
                CheckNeighbor(current.x, current.y - 1, currentIsBlack);

                void CheckNeighbor(int nx, int ny, bool fromBlack)
                {
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                        return;

                    var ni = ny * width + nx;
                    if (visited[ni])
                        return;

                    var neighborPixel = pixels[ni];

                    // Skip transparent
                    if (neighborPixel.a == 0)
                        return;

                    var neighborIsBlack = IsBlack(neighborPixel);

                    // If coming from black and going to colored, don't cross
                    // (This prevents black pixels from "discovering" the other side)
                    // But we still include black in the region
                    if (fromBlack && !neighborIsBlack)
                        return;

                    // For colored pixels, must match target color exactly
                    if (!neighborIsBlack && !ColorsMatch(neighborPixel, targetColor))
                        return;

                    visited[ni] = true;
                    queue.Enqueue(new Vector2Int(nx, ny));
                }
            }

            // Skip tiny regions (likely noise)
            if (regionPixels.Count < 2)
                return null;

            // Create the region with local coordinate system
            var bounds = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
            var region = new LightingRegion
            {
                Color = targetColor,
                Bounds = bounds,
                RegionMask = new bool[bounds.width * bounds.height],
                BlackMask = new bool[bounds.width * bounds.height]
            };

            // Fill region mask with colored pixels
            foreach (var pixel in regionPixels)
            {
                var localX = pixel.x - bounds.x;
                var localY = pixel.y - bounds.y;
                var localIndex = localY * bounds.width + localX;
                region.RegionMask[localIndex] = true;
            }

            // Fill both masks for black pixels (they're part of region AND marked as black)
            foreach (var pixel in blackPixelsInRegion)
            {
                var localX = pixel.x - bounds.x;
                var localY = pixel.y - bounds.y;
                var localIndex = localY * bounds.width + localX;
                region.RegionMask[localIndex] = true; // Part of region
                region.BlackMask[localIndex] = true; // But marked as black
            }

            return region;
        }

        /// <summary>
        /// Check if a color is strict black (0, 0, 0).
        /// Alpha must be non-zero for it to count as black.
        /// </summary>
        private static bool IsBlack(Color32 color)
        {
            return color.a > 0 && color.r == 0 && color.g == 0 && color.b == 0;
        }

        /// <summary>
        /// Check if two colors match exactly (RGB, ignoring alpha).
        /// </summary>
        private static bool ColorsMatch(Color32 a, Color32 b)
        {
            return a.r == b.r && a.g == b.g && a.b == b.b;
        }
    }
}
