using System;
using System.Collections.Generic;
using PDNWrapper;
using UnityEngine;

namespace FireAnimation
{
    [Serializable]
    public class ImportMetadata
    {
        public List<SpriteAnimation> Animations { get; set; } = new();
    }

    [Serializable]
    public class SpriteAnimation
    {
        public string Name { get; set; }
        public List<AnimationTexture> Textures { get; set; } = new();
    }

    public enum TextureType
    {
        Unknown,
        Albedo,
        Normal
    }

    [Serializable]
    public class AnimationTexture
    {
        public string Name { get; set; }
        public TextureType Type { get; set; }
        public List<AnimationFrame> Frames { get; set; } = new();
    }

    [Serializable]
    public class AnimationFrame
    {
        public List<int> LayerIDs { get; set; } = new();

        [NonSerialized]
        internal List<BitmapLayer> BitmapLayers;
    }
}
