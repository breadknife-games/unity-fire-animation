using System.Collections.Generic;
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
            float framesPerSecond)
        {
            if (mainAsset.Animations == null || mainAsset.Animations.Length == 0)
                return;

            var animationClips = new List<AnimationClip>();
            var clipNames = new List<string>();

            foreach (var animData in mainAsset.Animations)
            {
                if (animData.Sprites == null || animData.Sprites.Length == 0)
                    continue;

                var clip = CreateAnimationClip(animData.Name, animData.Sprites, framesPerSecond);
                if (clip != null)
                {
                    string clipId = $"{animData.Name}_AnimationClip";
                    ctx.AddObjectToAsset(clipId, clip);
                    animationClips.Add(clip);
                    clipNames.Add(animData.Name);
                }
            }

            if (animationClips.Count == 0)
                return;

            CreateAnimatorController(
                ctx,
                Path.GetFileNameWithoutExtension(ctx.assetPath),
                animationClips);
        }

        private static AnimationClip CreateAnimationClip(string animationName, Sprite[] sprites, float fps)
        {
            if (sprites == null || sprites.Length == 0)
                return null;

            var clip = new AnimationClip();
            clip.name = animationName;
            clip.frameRate = fps;

            var binding = new EditorCurveBinding
            {
                path = "",
                type = typeof(SpriteRenderer),
                propertyName = "m_Sprite"
            };

            float frameTime = 1f / fps;
            var keyframes = new ObjectReferenceKeyframe[sprites.Length];

            for (int i = 0; i < sprites.Length; i++)
            {
                keyframes[i] = new ObjectReferenceKeyframe
                {
                    time = i * frameTime,
                    value = sprites[i]
                };
            }

            AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);

            var clipSettings = AnimationUtility.GetAnimationClipSettings(clip);
            clipSettings.loopTime = true;
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
    }
}
