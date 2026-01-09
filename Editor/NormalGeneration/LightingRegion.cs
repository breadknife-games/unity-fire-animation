using UnityEngine;

namespace FireAnimation.NormalGeneration
{
    /// <summary>
    /// Represents a single connected component of pixels with the same color
    /// within a Lighting Region Map.
    /// 
    /// Example: Two separate red circles in the same texture = two LightingRegion instances.
    /// </summary>
    public class LightingRegion
    {
        /// <summary>
        /// The exact color that identifies this region.
        /// </summary>
        public Color32 Color { get; set; }

        /// <summary>
        /// Bounding box of this region within the source texture.
        /// Used to optimize processing by limiting work to relevant pixels.
        /// </summary>
        public RectInt Bounds { get; set; }

        /// <summary>
        /// Pixel mask indicating which pixels belong to this region.
        /// Indexed as [y * Width + x] relative to Bounds.
        /// True = pixel is part of this region.
        /// </summary>
        public bool[] RegionMask { get; set; }

        /// <summary>
        /// Pixel mask indicating which pixels are black (bevel blockers).
        /// Indexed as [y * Width + x] relative to Bounds.
        /// True = pixel is black and blocks bevel propagation.
        /// </summary>
        public bool[] BlackMask { get; set; }

        /// <summary>
        /// Pixel mask indicating which pixels are valid bevel-emitting edges.
        /// An edge pixel is adjacent to transparent exterior AND not blocked by black.
        /// Indexed as [y * Width + x] relative to Bounds.
        /// True = pixel is a valid edge source for distance field.
        /// </summary>
        public bool[] EdgeMask { get; set; }

        /// <summary>
        /// Computed distance field for this region.
        /// Distance from each pixel to the nearest valid edge.
        /// Indexed as [y * Width + x] relative to Bounds.
        /// </summary>
        public float[] DistanceField { get; set; }

        /// <summary>
        /// Generated normal map for this region.
        /// Indexed as [y * Width + x] relative to Bounds.
        /// Encoded as standard tangent-space normals (RGB = XYZ mapped from [-1,1] to [0,255]).
        /// </summary>
        public Color32[] NormalMap { get; set; }

        /// <summary>
        /// Bevel width parameter for this region.
        /// Controls how far the normal gradient extends inward.
        /// </summary>
        public float BevelWidth { get; set; } = 10f;

        /// <summary>
        /// Width of the region bounds.
        /// </summary>
        public int Width => Bounds.width;

        /// <summary>
        /// Height of the region bounds.
        /// </summary>
        public int Height => Bounds.height;

        /// <summary>
        /// Total pixel count within bounds.
        /// </summary>
        public int PixelCount => Width * Height;

        /// <summary>
        /// Get the local index for a position within bounds.
        /// </summary>
        public int GetLocalIndex(int localX, int localY)
        {
            return localY * Width + localX;
        }

        /// <summary>
        /// Check if a local coordinate is within bounds.
        /// </summary>
        public bool IsValidLocal(int localX, int localY)
        {
            return localX >= 0 && localX < Width && localY >= 0 && localY < Height;
        }
    }
}
