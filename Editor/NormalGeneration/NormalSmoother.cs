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

            // Create bevel mask - only pixels within bevel width participate in smoothing
            var bevelMask = new bool[width * height];
            for (var i = 0; i < region.DistanceField.Length; i++)
            {
                // Pixel is in bevel zone if it's in the region and distance < bevelWidth
                bevelMask[i] = region.RegionMask[i] &&
                               region.DistanceField[i] < bevelWidth &&
                               region.DistanceField[i] < DistanceFieldGenerator.Infinity;
            }

            // Decode normals to Vector3 for proper averaging
            var normals = new Vector3[width * height];
            for (var i = 0; i < region.NormalMap.Length; i++)
            {
                normals[i] = DecodeNormal(region.NormalMap[i]);
            }

            // Generate Gaussian kernel
            var kernelRadius = Mathf.CeilToInt(radius * 2.5f); // 2.5 sigma covers ~99% of the distribution
            var kernel = GenerateGaussianKernel(radius, kernelRadius);

            // Separable blur: horizontal pass (only on bevel pixels, only sampling bevel pixels)
            var tempNormals = new Vector3[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;

                    // Only blur pixels in the bevel zone
                    if (!bevelMask[index])
                    {
                        tempNormals[index] = normals[index];
                        continue;
                    }

                    tempNormals[index] = BlurPixelHorizontal(normals, bevelMask, x, y, width, kernel, kernelRadius);
                }
            }

            // Separable blur: vertical pass
            var resultNormals = new Vector3[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;

                    // Only blur pixels in the bevel zone
                    if (!bevelMask[index])
                    {
                        resultNormals[index] = tempNormals[index];
                        continue;
                    }

                    resultNormals[index] =
                        BlurPixelVertical(tempNormals, bevelMask, x, y, width, height, kernel, kernelRadius);
                }
            }

            // Encode back to Color32 - only update bevel pixels
            for (var i = 0; i < region.NormalMap.Length; i++)
            {
                if (bevelMask[i])
                {
                    region.NormalMap[i] = EncodeNormal(resultNormals[i]);
                }
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
