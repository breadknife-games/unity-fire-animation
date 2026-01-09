using System.Collections.Generic;

namespace FireAnimation.NormalGeneration
{
    /// <summary>
    /// Represents one Lighting Region Map texture layer.
    /// Contains multiple LightingRegion instances (connected components).
    /// 
    /// Multiple LightingRegionMap instances can exist per animation frame,
    /// allowing overlapping regions with defined stacking order.
    /// </summary>
    public class LightingRegionMap
    {
        /// <summary>
        /// Name of this lighting region map (from layer name).
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Layer order for composition. Lower = further back.
        /// </summary>
        public int LayerOrder { get; set; }

        /// <summary>
        /// Width of the source texture.
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// Height of the source texture.
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// All connected regions discovered in this texture.
        /// Each region is a single connected component of one color.
        /// </summary>
        public List<LightingRegion> Regions { get; set; } = new List<LightingRegion>();
    }
}
