namespace FireAnimation
{
    internal static class TextureTypeHelper
    {
        public static string GetDisplayName(TextureType type)
        {
            return type switch
            {
                TextureType.Normal => "Normal",
                TextureType.Albedo => "Albedo",
                TextureType.LightingRegion => "LightingRegion",
                _ => type.ToString()
            };
        }

        public static string GetShaderPropertyName(TextureType type)
        {
            return type switch
            {
                TextureType.Normal => "_NormalMap",
                TextureType.Albedo => "_MainTex",
                TextureType.LightingRegion => "_NormalMap", // LightingRegion generates normals
                _ => $"_{type}Map"
            };
        }
    }
}
