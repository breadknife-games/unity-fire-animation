using System;
using System.Collections.Generic;
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

        public AnimationData GetAnimation(string name)
        {
            if (Animations == null) return null;

            foreach (var anim in Animations)
            {
                if (anim.Name == name)
                    return anim;
            }
            return null;
        }

        public Texture2D GetSecondaryTexture(string animationName, string textureName)
        {
            var anim = GetAnimation(animationName);
            if (anim?.SecondaryTextures == null) return null;

            foreach (var secTex in anim.SecondaryTextures)
            {
                if (secTex.Name == textureName)
                    return secTex.Texture;
            }

            return null;
        }

        public SecondaryTextureData[] GetSecondaryTextures(string animationName)
        {
            var anim = GetAnimation(animationName);
            return anim?.SecondaryTextures ?? Array.Empty<SecondaryTextureData>();
        }
    }
}

