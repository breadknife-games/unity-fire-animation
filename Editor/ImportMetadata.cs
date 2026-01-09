using System;
using System.Collections.Generic;
using PDNWrapper;

namespace FireAnimation
{
    [Serializable]
    public class ImportMetadata
    {
        public List<GameObjectGroup> Groups = new List<GameObjectGroup>();
    }

    /// <summary>
    /// A group of animations that belong to a single GameObject, defined by layer color.
    /// </summary>
    [Serializable]
    public class GameObjectGroup
    {
        public string Name;
        public LayerColor Color;
        public List<SpriteAnimation> Animations = new List<SpriteAnimation>();
    }

    /// <summary>
    /// A single animation (e.g., run, walk, idle) containing textures of different types.
    /// </summary>
    [Serializable]
    public class SpriteAnimation
    {
        public string Name;
        public List<AnimationTexture> Textures = new List<AnimationTexture>();
    }

    public enum TextureType
    {
        Unknown,
        Albedo,
        Normal,
        LightingRegion
    }

    /// <summary>
    /// A texture of a specific type (Albedo, Normal, etc.) for an animation.
    /// Multiple source textures of the same type are merged into one.
    /// </summary>
    [Serializable]
    public class AnimationTexture
    {
        public TextureType Type;
        public List<AnimationFrame> Frames = new List<AnimationFrame>();
    }

    /// <summary>
    /// A single frame within an animation texture, containing layer data to be merged.
    /// </summary>
    [Serializable]
    public class AnimationFrame
    {
        public List<int> LayerIDs = new List<int>();

        [NonSerialized] internal List<BitmapLayer> BitmapLayers;
    }

    [Serializable]
    public class AnimationSettings
    {
        public string AnimationName;
        public float FramesPerSecond = -1f;
        public bool LoopTime = true;
    }
}
