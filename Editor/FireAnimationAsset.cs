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
            public Texture2D Texture;
            public Sprite[] Sprites;
            public SecondaryTextureData[] SecondaryTextures;
        }

        [Serializable]
        public class GroupData
        {
            public string Name;
            public LayerColor Color;
            public AnimationData[] Animations;
        }

        public GroupData[] Groups;
    }
}
