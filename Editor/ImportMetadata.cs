using System;
using System.Collections.Generic;
using PDNWrapper;
using UnityEngine;

namespace FireAnimation
{
    [Serializable]
    public class ImportMetadata
    {
        public List<SpriteAnimation> Animations = new List<SpriteAnimation>();
    }

    [Serializable]
    public class SpriteAnimation
    {
        public string Name;
        public LayerColor Color;
        public List<AnimationTexture> Textures = new List<AnimationTexture>();
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
        public string Name;
        public TextureType Type;
        public List<AnimationFrame> Frames = new List<AnimationFrame>();
    }

    [Serializable]
    public class AnimationFrame
    {
        public List<int> LayerIDs = new List<int>();

        [NonSerialized]
        internal List<BitmapLayer> BitmapLayers;
    }

    [Serializable]
    public class AnimationSettings
    {
        public string AnimationName;
        public float FramesPerSecond = -1f;
        public bool LoopTime = true;

        public bool HasFpsOverride => FramesPerSecond >= 0f;
    }
}
