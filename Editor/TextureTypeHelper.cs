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
                TextureType.LightBlock => "LightBlock",
                _ => type.ToString()
            };
        }

        /// <summary>
        /// Returns the shader property name for this texture type.
        /// Returns null if this type should not be added as a sprite secondary texture.
        /// </summary>
        public static string GetShaderPropertyName(TextureType type)
        {
            return type switch
            {
                TextureType.Normal => "_NormalMap",
                TextureType.Albedo => "_MainTex",
                TextureType.LightBlock => null, // Intermediate only, not a sprite texture
                TextureType.Unknown => null,
                _ => $"_{type}Map"
            };
        }

        /// <summary>
        /// Maps a PSD layer color to a texture type.
        /// </summary>
        public static TextureType GetTextureType(LayerColor color)
        {
            return color switch
            {
                LayerColor.Gray => TextureType.Albedo,
                LayerColor.Fuchsia => TextureType.LightBlock,
                _ => TextureType.Unknown
            };
        }
    }
}
