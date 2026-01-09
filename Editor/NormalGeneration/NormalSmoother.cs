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
