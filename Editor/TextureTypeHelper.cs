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
                _ => type.ToString()
            };
        }

        public static string GetShaderPropertyName(TextureType type)
        {
            return type switch
            {
                TextureType.Normal => "_NormalMap",
                TextureType.Albedo => "_MainTex",
                _ => $"_{type}Map"
            };
        }
    }
}
