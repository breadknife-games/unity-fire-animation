using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace FireAnimation.NormalGeneration
{
    /// <summary>
    /// Generates distance fields for lighting regions.
    /// 
    /// The distance field represents, for each pixel in a region, the shortest distance
    /// to the nearest valid bevel-emitting edge. Black pixels block propagation.
    /// 
    /// Algorithm:
    /// 1. Find edge pixels (region pixels adjacent to transparent exterior OR different color)
    /// 2. Filter edges blocked by black pixels
    /// 3. BFS flood fill from valid edges, tracking distance
    /// 4. Black pixels are impassable - distance does not propagate through them
    /// </summary>
    public static class DistanceFieldGenerator
    {
        /// <summary>
        /// Represents infinite distance (unreachable pixel).
        /// </summary>
        public const float Infinity = float.MaxValue;

        /// <summary>
        /// Compute the distance field for a region using Euclidean distance.
        /// Also populates the EdgeMask on the region.
        /// 
        /// Uses a modified BFS that tracks the nearest edge position for each pixel,
        /// allowing accurate Euclidean distance calculation.
        /// </summary>
        /// <param name="region">The region to process</param>
        /// <param name="sourcePixels">Original full texture pixels (for transparency check)</param>
        /// <param name="sourceWidth">Full texture width</param>
        /// <param name="sourceHeight">Full texture height</param>
        /// <param name="maxDistance">Maximum distance to propagate (bevel width clamp)</param>
        public static void ComputeDistanceField(
            LightingRegion region,
            NativeArray<Color32> sourcePixels,
            int sourceWidth,
            int sourceHeight,
            float maxDistance = float.MaxValue)
        {
            var width = region.Width;
            var height = region.Height;
            var bounds = region.Bounds;

            // Initialize distance field and edge mask
            region.DistanceField = new float[width * height];
            region.EdgeMask = new bool[width * height];

            // Track nearest edge position for each pixel (for Euclidean distance)
            var nearestEdge = new Vector2Int[width * height];

            for (var i = 0; i < region.DistanceField.Length; i++)
            {
                region.DistanceField[i] = Infinity;
                nearestEdge[i] = new Vector2Int(-1, -1); // Invalid
            }

            // Phase 1: Find valid edge pixels
            var edgeSources = new List<Vector2Int>();

            for (var localY = 0; localY < height; localY++)
            {
                for (var localX = 0; localX < width; localX++)
                {
                    var localIndex = localY * width + localX;

                    // Skip if not part of region
                    if (!region.RegionMask[localIndex])
                        continue;

                    // Skip if this pixel is black
                    if (region.BlackMask[localIndex])
                        continue;

                    // Check if adjacent to transparent (exterior) or different color
                    var globalX = bounds.x + localX;
                    var globalY = bounds.y + localY;

                    if (IsAdjacentToEdge(globalX, globalY, sourcePixels, sourceWidth, sourceHeight))
                    {
                        region.EdgeMask[localIndex] = true;
                        region.DistanceField[localIndex] = 0f;
                        nearestEdge[localIndex] = new Vector2Int(localX, localY);
                        edgeSources.Add(new Vector2Int(localX, localY));
                    }
                }
            }

            // Phase 2: BFS flood fill, propagating nearest edge position
            // We use BFS order but compute Euclidean distance from the propagated edge position
            var queue = new Queue<Vector2Int>();
            foreach (var edge in edgeSources)
            {
                queue.Enqueue(edge);
            }

            // 8-directional neighbors for smoother propagation
            var directions = new[]
            {
                new Vector2Int(1, 0),
                new Vector2Int(-1, 0),
                new Vector2Int(0, 1),
                new Vector2Int(0, -1),
                new Vector2Int(1, 1),
                new Vector2Int(-1, 1),
                new Vector2Int(1, -1),
                new Vector2Int(-1, -1)
            };

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var currentIndex = current.y * width + current.x;
                var currentNearest = nearestEdge[currentIndex];

                foreach (var dir in directions)
                {
                    var nx = current.x + dir.x;
                    var ny = current.y + dir.y;

                    if (!region.IsValidLocal(nx, ny))
                        continue;

                    var neighborIndex = ny * width + nx;

                    // Skip if not part of region
                    if (!region.RegionMask[neighborIndex])
                        continue;

                    // Calculate Euclidean distance from neighbor to the current pixel's nearest edge
                    var dx = nx - currentNearest.x;
                    var dy = ny - currentNearest.y;
                    var newDist = Mathf.Sqrt(dx * dx + dy * dy);

                    // Stop if exceeding max distance
                    if (newDist >= maxDistance)
                        continue;

                    // Only update if this path is shorter
                    if (newDist < region.DistanceField[neighborIndex])
                    {
                        region.DistanceField[neighborIndex] = newDist;
                        nearestEdge[neighborIndex] = currentNearest;
                        queue.Enqueue(new Vector2Int(nx, ny));
                    }
                }
            }
        }

        /// <summary>
        /// Check if a global pixel position is adjacent to an edge.
        /// An edge is defined as a transition to transparent OR a transition to a different color.
        /// Black pixels do not count as edges - they are interior blockers, not boundaries.
        /// </summary>
        private static bool IsAdjacentToEdge(
            int globalX,
            int globalY,
            NativeArray<Color32> sourcePixels,
            int sourceWidth,
            int sourceHeight)
        {
            var directions = new[]
            {
                new Vector2Int(1, 0),
                new Vector2Int(-1, 0),
                new Vector2Int(0, 1),
                new Vector2Int(0, -1)
            };

            var currentIndex = globalY * sourceWidth + globalX;
            var currentPixel = sourcePixels[currentIndex];

            foreach (var dir in directions)
            {
                var nx = globalX + dir.x;
                var ny = globalY + dir.y;

                // Out of bounds counts as edge (exterior)
                if (nx < 0 || nx >= sourceWidth || ny < 0 || ny >= sourceHeight)
                {
                    return true;
                }

                var neighborIndex = ny * sourceWidth + nx;
                var neighborPixel = sourcePixels[neighborIndex];

                // Adjacent to transparent pixel = edge
                if (neighborPixel.a == 0)
                {
                    return true;
                }

                // Skip color comparison if neighbor is black - black pixels are not color boundaries
                if (IsBlackPixel(neighborPixel))
                {
                    continue;
                }

                // Adjacent to different color = edge (but not if current pixel is black)
                if (!IsBlackPixel(currentPixel) &&
                    (currentPixel.r != neighborPixel.r ||
                     currentPixel.g != neighborPixel.g ||
                     currentPixel.b != neighborPixel.b))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Check if a pixel is strict black (0, 0, 0).
        /// Must be non-transparent to count as black.
        /// </summary>
        private static bool IsBlackPixel(Color32 pixel)
        {
            return pixel.a > 0 && pixel.r == 0 && pixel.g == 0 && pixel.b == 0;
        }

        /// <summary>
        /// Generate a debug texture visualizing the distance field as grayscale.
        /// </summary>
        /// <param name="region">Region with computed distance field</param>
        /// <param name="maxDisplayDistance">Distance that maps to black (0). 0 = white.</param>
        /// <returns>Grayscale texture showing distance field</returns>
        public static Texture2D GenerateDebugTexture(LightingRegion region, float maxDisplayDistance = 20f)
        {
            var width = region.Width;
            var height = region.Height;

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[width * height];

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;

                    if (!region.RegionMask[index])
                    {
                        // Not part of region - transparent
                        pixels[index] = new Color32(0, 0, 0, 0);
                    }
                    else if (region.BlackMask[index])
                    {
                        // Black pixel - show as red for visibility
                        pixels[index] = new Color32(255, 0, 0, 255);
                    }
                    else if (region.EdgeMask != null && region.EdgeMask[index])
                    {
                        // Edge pixel - show as green
                        pixels[index] = new Color32(0, 255, 0, 255);
                    }
                    else
                    {
                        // Interior pixel - grayscale based on distance
                        var dist = region.DistanceField[index];
                        if (dist >= Infinity)
                        {
                            // Unreachable - show as magenta
                            pixels[index] = new Color32(255, 0, 255, 255);
                        }
                        else
                        {
                            // Map distance to grayscale (0 = white, maxDisplayDistance = black)
                            var t = Mathf.Clamp01(dist / maxDisplayDistance);
                            var gray = (byte)(255 * (1f - t));
                            pixels[index] = new Color32(gray, gray, gray, 255);
                        }
                    }
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// Generate a combined debug texture for an entire LightingRegionMap,
        /// showing all regions in their correct positions.
        /// </summary>
        public static Texture2D GenerateDebugTexture(LightingRegionMap map)
        {
            var texture = new Texture2D(map.Width, map.Height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[map.Width * map.Height];

            // Initialize to transparent
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(0, 0, 0, 0);
            }

            // First pass: find the maximum distance across all regions for proper scaling
            var maxDistance = 1f;
            foreach (var region in map.Regions)
            {
                if (region.DistanceField == null) continue;
                for (var i = 0; i < region.DistanceField.Length; i++)
                {
                    if (region.RegionMask[i] && region.DistanceField[i] < Infinity)
                    {
                        maxDistance = Mathf.Max(maxDistance, region.DistanceField[i]);
                    }
                }
            }

            // Draw each region
            foreach (var region in map.Regions)
            {
                for (var localY = 0; localY < region.Height; localY++)
                {
                    for (var localX = 0; localX < region.Width; localX++)
                    {
                        var localIndex = localY * region.Width + localX;

                        if (!region.RegionMask[localIndex])
                            continue;

                        var globalX = region.Bounds.x + localX;
                        var globalY = region.Bounds.y + localY;

                        // Proper bounds check
                        if (globalX < 0 || globalX >= map.Width ||
                            globalY < 0 || globalY >= map.Height)
                            continue;

                        var globalIndex = globalY * map.Width + globalX;

                        if (region.BlackMask[localIndex])
                        {
                            pixels[globalIndex] = new Color32(255, 0, 0, 255);
                        }
                        else if (region.EdgeMask != null && region.EdgeMask[localIndex])
                        {
                            pixels[globalIndex] = new Color32(0, 255, 0, 255);
                        }
                        else if (region.DistanceField != null)
                        {
                            var dist = region.DistanceField[localIndex];
                            if (dist >= Infinity)
                            {
                                pixels[globalIndex] = new Color32(255, 0, 255, 255);
                            }
                            else
                            {
                                // Scale to max distance so full gradient is visible
                                var t = Mathf.Clamp01(dist / maxDistance);
                                var gray = (byte)(255 * (1f - t));
                                pixels[globalIndex] = new Color32(gray, gray, gray, 255);
                            }
                        }
                        else
                        {
                            // No distance field yet - show region color
                            pixels[globalIndex] = region.Color;
                        }
                    }
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
