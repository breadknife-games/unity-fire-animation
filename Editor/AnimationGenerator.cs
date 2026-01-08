using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEditor.Animations;
using UnityEngine;

namespace FireAnimation
{
    internal static class AnimationGenerator
    {
        public static void GenerateUnityAnimations(
            AssetImportContext ctx,
            FireAnimationAsset mainAsset,
            List<AnimationSettings> animationSettings,
            float defaultFps)
        {
            if (mainAsset.Animations == null || mainAsset.Animations.Length == 0)
                return;

            var settingsDict = new Dictionary<string, AnimationSettings>();
            if (animationSettings != null)
            {
                foreach (var setting in animationSettings)
                {
                    if (!string.IsNullOrEmpty(setting.AnimationName))
                        settingsDict[setting.AnimationName] = setting;
                }
            }

            // Group animations by color
            var animationsByColor = mainAsset.Animations
                .GroupBy(a => a.Color)
                .ToList();

            var baseName = Path.GetFileNameWithoutExtension(ctx.assetPath);

            foreach (var group in animationsByColor)
            {
                var animations = group.ToList();
                var name = animations.First().Name.Split("_").Last();
                var controllerName = animationsByColor.Count > 1
                    ? $"{baseName}_{name}"
                    : baseName;

                CreateAssetsForGroup(ctx, controllerName, animations, settingsDict, defaultFps);
            }
        }

        private static void CreateAssetsForGroup(
            AssetImportContext ctx,
            string controllerName,
            List<FireAnimationAsset.AnimationData> animations,
            Dictionary<string, AnimationSettings> settingsDict,
            float defaultFps)
        {
            var clips = new List<AnimationClip>();
            Sprite firstSprite = null;

            foreach (var animData in animations)
            {
                if (animData.Sprites == null || animData.Sprites.Length == 0)
                    continue;

                if (firstSprite == null)
                    firstSprite = animData.Sprites[0];

                if (!settingsDict.TryGetValue(animData.Name, out var settings))
                {
                    settings = new AnimationSettings
                    {
                        AnimationName = animData.Name,
                        FramesPerSecond = -1f,
                        LoopTime = true
                    };
                }

                var fpsToUse = settings.FramesPerSecond >= 0f ? settings.FramesPerSecond : defaultFps;
                var clip = CreateAnimationClip(animData.Name, animData.Sprites, fpsToUse, settings.LoopTime);
                if (clip != null)
                {
                    ctx.AddObjectToAsset($"{animData.Name}_AnimationClip", clip);
                    clips.Add(clip);
                }
            }

            if (clips.Count > 0 && firstSprite != null)
            {
                var controller = CreateAnimatorController(ctx, controllerName, clips);
                if (controller != null)
                {
                    CreatePrefab(ctx, controllerName, controller, firstSprite);
                }
            }
        }

        private static AnimationClip CreateAnimationClip(string animationName, Sprite[] sprites, float fps,
            bool loopTime)
        {
            if (sprites == null || sprites.Length == 0)
                return null;

            var clip = new AnimationClip
            {
                name = animationName,
                frameRate = fps
            };

            var binding = new EditorCurveBinding
            {
                path = "",
                type = typeof(SpriteRenderer),
                propertyName = "m_Sprite"
            };

            var frameTime = 1f / fps;
            var keyframes = new ObjectReferenceKeyframe[sprites.Length];

            for (var i = 0; i < sprites.Length; i++)
            {
                keyframes[i] = new ObjectReferenceKeyframe
                {
                    time = i * frameTime,
                    value = sprites[i]
                };
            }

            AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);

            var clipSettings = AnimationUtility.GetAnimationClipSettings(clip);
            clipSettings.loopTime = loopTime;
            AnimationUtility.SetAnimationClipSettings(clip, clipSettings);

            return clip;
        }

        private static AnimatorController CreateAnimatorController(
            AssetImportContext ctx,
            string controllerName,
            List<AnimationClip> clips)
        {
            if (clips == null || clips.Count == 0)
                return null;

            var controller = new AnimatorController();
            controller.name = $"{controllerName}_Controller";

            if (controller.layers.Length == 0)
                controller.AddLayer("Base Layer");

            foreach (var clip in clips)
                controller.AddMotion(clip);

            var layer = controller.layers[0];
            var stateMachine = layer.stateMachine;
            stateMachine.name = "Base Layer";

            ctx.AddObjectToAsset(stateMachine.name + "_StateMachine", stateMachine);

            if (stateMachine.states.Length > 0)
            {
                stateMachine.defaultState = stateMachine.states[0].state;

                foreach (var childState in stateMachine.states)
                {
                    var state = childState.state;
                    state.speed = 1.0f;
                    ctx.AddObjectToAsset(state.name + "_State", state);
                }
            }

            ctx.AddObjectToAsset(controller.name + "_Controller", controller);
            return controller;
        }

        private static void CreatePrefab(AssetImportContext ctx,
            string prefabName,
            AnimatorController controller,
            Sprite defaultSprite)
        {
            var prefab = new GameObject(prefabName);

            var spriteRenderer = prefab.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = defaultSprite;

            var animator = prefab.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;

            ctx.AddObjectToAsset($"{prefabName}_Prefab", prefab);
        }
    }
}
