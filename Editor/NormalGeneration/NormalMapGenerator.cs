using UnityEngine;

namespace FireAnimation.NormalGeneration
{
    /// <summary>
    /// Generates normal maps from distance fields.
    /// 
    /// The normal at each pixel is derived from the gradient of the distance field:
    /// - Gradient direction = surface slope direction
    /// - Gradient magnitude = steepness
    /// - Combined with a Z component for the "up" facing portion
    /// </summary>
    public static class NormalMapGenerator
    {
        /// <summary>
        /// Generate normals for a single region from its distance field.
        /// Stores the result in a new NormalMap array on the region.
        /// </summary>
        /// <param name="region">Region with computed distance field</param>
        /// <param name="bevelStrength">How steep the bevel is. Higher = more pronounced normals.</param>
        public static void GenerateNormals(LightingRegion region, float bevelStrength = 1f)
        {
            if (region.DistanceField == null)
                return;

            var width = region.Width;
            var height = region.Height;

            // Allocate normal map (stored as Color32 for easy texture creation)
            region.NormalMap = new Color32[width * height];

            // Default normal (pointing straight up in tangent space)
            var flatNormal = EncodeNormal(new Vector3(0, 0, 1));

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;

                    // Skip pixels not in the region
                    if (!region.RegionMask[index])
                    {
                        region.NormalMap[index] = new Color32(128, 128, 255, 0); // Flat normal, transparent
                        continue;
                    }

                    // Check if this pixel has a valid distance (was reached by BFS)
                    var dist = region.DistanceField[index];
                    if (dist >= DistanceFieldGenerator.Infinity)
                    {
                        // Unreachable pixel - flat normal
                        region.NormalMap[index] = flatNormal;
                        continue;
                    }

                    // Compute gradient using central differences
                    // This works for both colored and black pixels that have valid distances
                    var gradient = ComputeGradient(region, x, y);

                    // Scale gradient by bevel strength
                    gradient *= bevelStrength;

                    // Construct normal from gradient
                    // The gradient points in the direction of increasing distance (away from edges)
                    // We want normals that point "outward" from the surface, which is perpendicular to the gradient
                    // In tangent space: X = right, Y = up, Z = out of surface
                    var normal = new Vector3(-gradient.x, -gradient.y, 1f).normalized;

                    region.NormalMap[index] = EncodeNormal(normal);
                }
            }
        }

        /// <summary>
        /// Compute the gradient of the distance field at a given pixel using central differences.
        /// </summary>
        private static Vector2 ComputeGradient(LightingRegion region, int x, int y)
        {
            var width = region.Width;
            var height = region.Height;

            // Sample distance at neighboring pixels
            var distCenter = GetDistanceRaw(region, x, y);
            var distLeft = GetDistanceRaw(region, x - 1, y);
            var distRight = GetDistanceRaw(region, x + 1, y);
            var distDown = GetDistanceRaw(region, x, y - 1);
            var distUp = GetDistanceRaw(region, x, y + 1);

            // For unreachable neighbors (Infinity), use center distance to create zero gradient contribution
            // This prevents false edges from appearing at black barriers
            if (distLeft >= DistanceFieldGenerator.Infinity) distLeft = distCenter;
            if (distRight >= DistanceFieldGenerator.Infinity) distRight = distCenter;
            if (distDown >= DistanceFieldGenerator.Infinity) distDown = distCenter;
            if (distUp >= DistanceFieldGenerator.Infinity) distUp = distCenter;

            // Central difference for interior pixels
            float dx, dy;

            if (x == 0)
                dx = distRight - distCenter; // Forward difference
            else if (x == width - 1)
                dx = distCenter - distLeft; // Backward difference
            else
                dx = (distRight - distLeft) * 0.5f; // Central difference

            if (y == 0)
                dy = distUp - distCenter; // Forward difference
            else if (y == height - 1)
                dy = distCenter - distDown; // Backward difference
            else
                dy = (distUp - distDown) * 0.5f; // Central difference

            return new Vector2(dx, dy);
        }

        /// <summary>
        /// Get the raw distance at a pixel, with bounds clamping.
        /// Returns 0 if out of bounds or not in region.
        /// Returns Infinity for unreachable pixels (caller must handle).
        /// </summary>
        private static float GetDistanceRaw(LightingRegion region, int x, int y)
        {
            // Clamp to bounds
            x = Mathf.Clamp(x, 0, region.Width - 1);
            y = Mathf.Clamp(y, 0, region.Height - 1);

            var index = y * region.Width + x;

            // If not in region, treat as edge (distance 0)
            // if (!region.RegionMask[index])
            {
                // return 0f;
            }

            return region.DistanceField[index];
        }

        /// <summary>
        /// Encode a normalized vector to Color32 using standard normal map encoding.
        /// Maps [-1, 1] to [0, 255].
        /// </summary>
        private static Color32 EncodeNormal(Vector3 normal)
        {
            return new Color32(
                (byte)((normal.x * 0.5f + 0.5f) * 255f),
                (byte)((normal.y * 0.5f + 0.5f) * 255f),
                (byte)((normal.z * 0.5f + 0.5f) * 255f),
                255
            );
        }

        /// <summary>
        /// Generate a normal map texture for a single region.
        /// </summary>
        public static Texture2D GenerateTexture(LightingRegion region)
        {
            if (region.NormalMap == null)
                return null;

            var texture = new Texture2D(region.Width, region.Height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            texture.SetPixels32(region.NormalMap);
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// Generate a combined normal map texture for an entire LightingRegionMap.
        /// </summary>
        public static Texture2D GenerateTexture(LightingRegionMap map)
        {
            var texture = new Texture2D(map.Width, map.Height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[map.Width * map.Height];

            // Initialize to flat normal with zero alpha (transparent)
            var flatNormal = new Color32(128, 128, 255, 0);
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = flatNormal;
            }

            // Draw each region's normals
            foreach (var region in map.Regions)
            {
                if (region.NormalMap == null)
                    continue;

                for (var localY = 0; localY < region.Height; localY++)
                {
                    for (var localX = 0; localX < region.Width; localX++)
                    {
                        var localIndex = localY * region.Width + localX;

                        if (!region.RegionMask[localIndex])
                            continue;

                        var globalX = region.Bounds.x + localX;
                        var globalY = region.Bounds.y + localY;

                        if (globalX < 0 || globalX >= map.Width ||
                            globalY < 0 || globalY >= map.Height)
                            continue;

                        var globalIndex = globalY * map.Width + globalX;
                        pixels[globalIndex] = region.NormalMap[localIndex];
                    }
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
