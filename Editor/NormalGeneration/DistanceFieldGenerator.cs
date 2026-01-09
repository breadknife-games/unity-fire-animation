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
        /// <param name="edgeInset">Starting distance for edge pixels (pushes bevel inward)</param>
        public static void ComputeDistanceField(
            LightingRegion region,
            NativeArray<Color32> sourcePixels,
            int sourceWidth,
            int sourceHeight,
            float maxDistance = float.MaxValue,
            float edgeInset = 0f)
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
                        region.DistanceField[localIndex] = edgeInset;
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
        /// An edge is defined as a transition to non-fully-opaque (alpha less than 255) or out of bounds.
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

            foreach (var dir in directions)
            {
                var nx = globalX + dir.x;
                var ny = globalY + dir.y;

                // Out of bounds counts as edge (exterior)
                if (nx < 0 || nx >= sourceWidth || ny < 0 || ny >= sourceHeight)
                    return true;

                var neighborIndex = ny * sourceWidth + nx;
                var neighborPixel = sourcePixels[neighborIndex];

                // Adjacent to non-fully-opaque pixel = edge
                if (neighborPixel.a < 255)
                    return true;
            }

            return false;
        }
    }
}
