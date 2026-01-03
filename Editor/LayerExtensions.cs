using System;
using PhotoshopFile;

namespace FireAnimation
{
    internal static class LayerExtensions
    {
        internal static LayerColor GetColor(this Layer layer)
        {
            foreach (var info in layer.AdditionalInfo)
            {
                if (info.Key == "lclr")
                {
                    var rawInfo = (RawLayerInfo)info;
                    var color = BitConverter.ToInt32(rawInfo.Data, 0);
                    return (LayerColor)color;
                }
            }

            return LayerColor.Default;
        }
    }
}