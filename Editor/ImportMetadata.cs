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
    /// A single animation (e.g., run, walk, idle) containing textures and sprite parts.
    /// </summary>
    [Serializable]
    public class SpriteAnimation
    {
        public string Name;
        public List<AnimationTexture> Textures = new List<AnimationTexture>();
        public List<SpritePart> Parts = new List<SpritePart>();
    }

    public enum TextureType
    {
        Unknown,
        Albedo,
        Normal,
        LightBlock
    }

    /// <summary>
    /// A texture of a specific type (Albedo, Normal, etc.) for an animation.
    /// Represents final/merged texture data.
    /// </summary>
    [Serializable]
    public class AnimationTexture
    {
        public TextureType Type;
        public List<AnimationFrame> Frames = new List<AnimationFrame>();
    }

    /// <summary>
    /// A distinct piece of the sprite, parsed from a gray layer group.
    /// Each part generates its own normal map which is then merged.
    /// </summary>
    [Serializable]
    public class SpritePart
    {
        public string Name;
        public List<AnimationFrame> Frames = new List<AnimationFrame>();
    }

    /// <summary>
    /// A single frame containing layer data to be merged.
    /// </summary>
    [Serializable]
    public class AnimationFrame
    {
        public List<int> LayerIDs = new List<int>();

        [NonSerialized] internal List<BitmapLayer> BitmapLayers;
    }

    /// <summary>
    /// Per-part settings for normal generation (bevel, smoothness).
    /// </summary>
    [Serializable]
    public class SpritePartSettings
    {
        public string PartName;
        public float BevelWidth = -1f; // -1 = use default
        public float Smoothness = -1f; // -1 = use default
    }

    [Serializable]
    public class AnimationSettings
    {
        public string AnimationName;
        public float FramesPerSecond = -1f;
        public bool LoopTime = true;
    }

    /// <summary>
    /// Part settings for an entire group. Parts with the same name share settings.
    /// </summary>
    [Serializable]
    public class GroupPartSettings
    {
        public string GroupName;
        public List<SpritePartSettings> PartSettings = new List<SpritePartSettings>();
    }
}
