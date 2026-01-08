using System;
using UnityEngine;

namespace FireAnimation
{
    public class FireAnimationAsset : ScriptableObject
    {
        [Serializable]
        public class SecondaryTextureData
        {
            public string Name;
            public Texture2D Texture;
        }

        [Serializable]
        public class AnimationData
        {
            public string Name;
            public LayerColor Color;
            public Texture2D Texture;
            public Sprite[] Sprites;
            public SecondaryTextureData[] SecondaryTextures;
        }

        public AnimationData[] Animations;
    }
}

