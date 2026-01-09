// ReSharper disable UnusedMember.Global

namespace FireAnimation
{
    public enum LayerColor
    {
        Default = 0,
        Red = 256,
        Orange = 512,
        Yellow = 768,
        Green = 1024,
        Seafoam = 2048,
        Blue = 1280,
        Indigo = 2304,
        Magenta = 2560,
        Fuchsia = 2816,
        Violet = 1536,
        Gray = 1792
    }

    public static class LayerColorExtensions
    {
        public static bool IsValidGroupColor(this LayerColor color)
        {
            return color != LayerColor.Default;
        }

        public static string GetDisplayName(this LayerColor color)
        {
            return color switch
            {
                LayerColor.Red => "Red",
                LayerColor.Orange => "Orange",
                LayerColor.Yellow => "Yellow",
                LayerColor.Green => "Green",
                LayerColor.Seafoam => "Seafoam",
                LayerColor.Blue => "Blue",
                LayerColor.Indigo => "Indigo",
                LayerColor.Magenta => "Magenta",
                LayerColor.Fuchsia => "Fuchsia",
                LayerColor.Violet => "Violet",
                LayerColor.Gray => "Gray",
                _ => "Default"
            };
        }
    }
}
