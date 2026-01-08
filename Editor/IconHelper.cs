using System;
using UnityEditor;
using UnityEngine;

namespace FireAnimation
{
    internal static class IconHelper
    {
        public static void SetPsdIcon(FireAnimationAsset asset)
        {
            var icon = LoadCustomIcon();

            if (icon == null)
                icon = EditorGUIUtility.FindTexture("ScriptableObject Icon");

            if (icon != null)
                EditorGUIUtility.SetIconForObject(asset, icon);
        }

        private static Texture2D LoadCustomIcon()
        {
            var guids = AssetDatabase.FindAssets("PsdIcon t:Texture2D");
            foreach (var guid in guids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (assetPath.EndsWith("PsdIcon.png", StringComparison.OrdinalIgnoreCase))
                {
                    var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                    if (icon != null)
                        return icon;
                }
            }

            return null;
        }
    }
}
