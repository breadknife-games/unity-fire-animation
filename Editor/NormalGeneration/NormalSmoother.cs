using UnityEngine;

namespace FireAnimation.NormalGeneration
{
    /// <summary>
    /// Applies smoothing (blur) to normal maps.
    /// Uses a separable Gaussian blur for efficiency.
    /// Only smooths within the bevel zone, preserving the hard edge at the bevel boundary.
    /// </summary>
    public static class NormalSmoother
    {
        private static readonly Vector3 FlatNormal = Vector3.forward; // (0, 0, 1) - pointing straight out

        /// <summary>
        /// Apply Gaussian smoothing to a region's normal map.
        /// Only smooths pixels within the bevel width - flat center pixels are untouched.
        /// If the region is fully beveled (no flat center), applies standard smoothing to all region pixels.
        /// </summary>
        /// <param name="region">Region with generated normal map and distance field</param>
        /// <param name="bevelWidth">The bevel width - only pixels with distance less than this are smoothed</param>
        /// <param name="radius">Blur radius in pixels. 0 = no smoothing.</param>
        public static void SmoothNormals(LightingRegion region, float bevelWidth, float radius)
        {
            if (region?.NormalMap == null || region.DistanceField == null || radius <= 0f)
                return;

            var width = region.Width;
            var height = region.Height;

            // Check if region is fully beveled (no flat center exists)
            var isFullyBeveled = bevelWidth > 9000;

            // Create smoothing mask
            // If fully beveled: smooth ALL region pixels (standard smoothing)
            // If not: only smooth bevel pixels (edge-aware smoothing)
            var smoothMask = new bool[width * height];
            for (var i = 0; i < region.DistanceField.Length; i++)
            {
                if (isFullyBeveled)
                {
                    // Full smoothing: include all region pixels
                    smoothMask[i] = region.RegionMask[i];
                }
                else
                {
                    // Bevel-only smoothing: only pixels within bevel width
                    smoothMask[i] = region.RegionMask[i] &&
                                    region.DistanceField[i] < bevelWidth &&
                                    region.DistanceField[i] < DistanceFieldGenerator.Infinity;
                }
            }

            // Decode normals to Vector3 for proper averaging
            var normals = new Vector3[width * height];
            for (var i = 0; i < region.NormalMap.Length; i++)
            {
                normals[i] = DecodeNormal(region.NormalMap[i]);
            }

            // For fully beveled regions: pre-flatten the peak before blurring
            // This gives the blur something flat to propagate from
            if (isFullyBeveled)
            {
                FlattenPeak(region, normals, radius * 2);
            }

            // Generate Gaussian kernel
            var kernelRadius = Mathf.CeilToInt(radius * 2.5f); // 2.5 sigma covers ~99% of the distribution
            var kernel = GenerateGaussianKernel(radius, kernelRadius);

            // Separable blur: horizontal pass
            var tempNormals = new Vector3[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;

                    if (!smoothMask[index])
                    {
                        tempNormals[index] = normals[index];
                        continue;
                    }

                    tempNormals[index] = BlurPixelHorizontal(normals, smoothMask, x, y, width, kernel, kernelRadius);
                }
            }

            // Separable blur: vertical pass
            var resultNormals = new Vector3[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;

                    if (!smoothMask[index])
                    {
                        resultNormals[index] = tempNormals[index];
                        continue;
                    }

                    resultNormals[index] =
                        BlurPixelVertical(tempNormals, smoothMask, x, y, width, height, kernel, kernelRadius);
                }
            }

            // Encode back to Color32
            for (var i = 0; i < region.NormalMap.Length; i++)
            {
                if (smoothMask[i])
                {
                    region.NormalMap[i] = EncodeNormal(resultNormals[i]);
                }
            }
        }

        /// <summary>
        /// Pre-flatten the peak of a fully beveled region before blurring.
        /// Sets pixels at/near max distance to flat normal, giving the blur
        /// something to propagate from to eliminate the harsh center point.
        /// </summary>
        private static void FlattenPeak(LightingRegion region, Vector3[] normals, float fadeRadius)
        {
            // Find max distance in the region (the peak)
            var maxDistance = 0f;
            for (var i = 0; i < region.DistanceField.Length; i++)
            {
                if (!region.RegionMask[i])
                    continue;

                var dist = region.DistanceField[i];
                if (dist < DistanceFieldGenerator.Infinity && dist > maxDistance)
                {
                    maxDistance = dist;
                }
            }

            if (maxDistance <= 0f)
                return;

            // Fade toward flat for pixels near the peak
            // fadeRadius controls how far from the peak the fade extends
            for (var i = 0; i < region.DistanceField.Length; i++)
            {
                if (!region.RegionMask[i])
                    continue;

                var distance = region.DistanceField[i];
                if (distance >= DistanceFieldGenerator.Infinity)
                    continue;

                // How far from the peak is this pixel?
                var distFromPeak = maxDistance - distance;

                // Only affect pixels within fadeRadius of the peak
                if (distFromPeak > fadeRadius)
                    continue;

                // t = 0 at peak (full flat), t = 1 at fadeRadius (keep original)
                var t = distFromPeak / fadeRadius;
                t = Mathf.Clamp01(t);
                t = SmoothStep(t);

                // Blend toward flat
                normals[i] = Vector3.Lerp(FlatNormal, normals[i], t).normalized;
            }
        }

        private static Vector3 BlurPixelHorizontal(
            Vector3[] normals,
            bool[] bevelMask,
            int x, int y,
            int width,
            float[] kernel, int kernelRadius)
        {
            var sum = Vector3.zero;
            var weightSum = 0f;

            for (var k = -kernelRadius; k <= kernelRadius; k++)
            {
                var sampleX = x + k;
                if (sampleX < 0 || sampleX >= width)
                    continue;

                var sampleIndex = y * width + sampleX;

                // Only sample from other bevel pixels - don't blend with flat center
                if (!bevelMask[sampleIndex])
                    continue;

                var weight = kernel[k + kernelRadius];
                sum += normals[sampleIndex] * weight;
                weightSum += weight;
            }

            if (weightSum > 0f)
            {
                return (sum / weightSum).normalized;
            }

            return normals[y * width + x];
        }

        private static Vector3 BlurPixelVertical(
            Vector3[] normals,
            bool[] bevelMask,
            int x, int y,
            int width, int height,
            float[] kernel, int kernelRadius)
        {
            // Note: width is needed to calculate index (sampleY * width + x)
            var sum = Vector3.zero;
            var weightSum = 0f;

            for (var k = -kernelRadius; k <= kernelRadius; k++)
            {
                var sampleY = y + k;
                if (sampleY < 0 || sampleY >= height)
                    continue;

                var sampleIndex = sampleY * width + x;

                // Only sample from other bevel pixels - don't blend with flat center
                if (!bevelMask[sampleIndex])
                    continue;

                var weight = kernel[k + kernelRadius];
                sum += normals[sampleIndex] * weight;
                weightSum += weight;
            }

            if (weightSum > 0f)
            {
                return (sum / weightSum).normalized;
            }

            return normals[y * width + x];
        }

        /// <summary>
        /// Smooth the inner edge where the bevel meets the flat center.
        /// Creates a gradual transition from beveled normals to flat normals.
        /// Works by fading bevel normals toward flat as distance approaches bevelWidth.
        /// </summary>
        /// <param name="region">Region with generated normal map and distance field</param>
        /// <param name="bevelWidth">The bevel width - defines where the inner edge is</param>
        /// <param name="radius">Transition radius in pixels. Controls how far from the inner edge the fade starts.</param>
        public static void SmoothInnerEdge(LightingRegion region, float bevelWidth, float radius)
        {
            if (region?.NormalMap == null || region.DistanceField == null || radius <= 0f)
                return;

            var width = region.Width;
            var height = region.Height;

            // Process pixels within the bevel zone, fading toward flat near the inner edge
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;

                    // Skip pixels outside the region
                    if (!region.RegionMask[index])
                        continue;

                    var distance = region.DistanceField[index];

                    // Skip pixels at infinity (outside bevel zone - already flat)
                    if (distance >= DistanceFieldGenerator.Infinity)
                        continue;

                    // Skip pixels outside the bevel width (shouldn't happen, but safety check)
                    if (distance >= bevelWidth)
                        continue;

                    // Calculate how far from the inner edge we are
                    // distToInnerEdge = 0 at the inner boundary (distance == bevelWidth)
                    // distToInnerEdge = radius at the start of the fade zone
                    var distToInnerEdge = bevelWidth - distance;

                    // Only process pixels within the fade zone (near the inner edge)
                    if (distToInnerEdge > radius)
                        continue;

                    // Calculate blend factor: 
                    // t = 0 at inner edge (distance == bevelWidth), blend to flat
                    // t = 1 at start of fade zone (distToInnerEdge == radius), keep full bevel
                    var t = distToInnerEdge / radius;
                    t = Mathf.Clamp01(t);

                    // Smooth the transition with a smooth step function
                    t = SmoothStep(t);

                    // Decode current normal
                    var currentNormal = DecodeNormal(region.NormalMap[index]);

                    // Blend between flat normal (t=0) and current beveled normal (t=1)
                    var blendedNormal = Vector3.Lerp(FlatNormal, currentNormal, t).normalized;

                    region.NormalMap[index] = EncodeNormal(blendedNormal);
                }
            }
        }

        /// <summary>
        /// Hermite smooth step function for smooth interpolation.
        /// </summary>
        private static float SmoothStep(float t)
        {
            return t * t * (3f - 2f * t);
        }

        private static float[] GenerateGaussianKernel(float sigma, int radius)
        {
            var size = radius * 2 + 1;
            var kernel = new float[size];
            var sum = 0f;
            var twoSigmaSq = 2f * sigma * sigma;

            for (var i = -radius; i <= radius; i++)
            {
                var value = Mathf.Exp(-(i * i) / twoSigmaSq);
                kernel[i + radius] = value;
                sum += value;
            }

            // Normalize
            for (var i = 0; i < size; i++)
            {
                kernel[i] /= sum;
            }

            return kernel;
        }

        private static Vector3 DecodeNormal(Color32 color)
        {
            return new Vector3(
                (color.r / 255f) * 2f - 1f,
                (color.g / 255f) * 2f - 1f,
                (color.b / 255f) * 2f - 1f
            ).normalized;
        }

        private static Color32 EncodeNormal(Vector3 normal)
        {
            return new Color32(
                (byte)((normal.x * 0.5f + 0.5f) * 255f),
                (byte)((normal.y * 0.5f + 0.5f) * 255f),
                (byte)((normal.z * 0.5f + 0.5f) * 255f),
                255
            );
        }
    }
}
