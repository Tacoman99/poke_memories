using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace PokeMemories.Editor
{
    /// <summary>
    /// Builds one AnimationClip per skater frame folder and an Animator Controller with a
    /// state per clip. Re-run after re-packing frames with tools/sprites/pack.mjs.
    /// </summary>
    public static class SkaterAnimationBuilder
    {
        const string AnimationRoot = "Assets/Animation/Skater/";
        const string ControllerPath = AnimationRoot + "Skater.controller";
        const float FramesPerSecond = 12f;

        // Clip name -> loops. Jump plays once; the simulation decides when she lands.
        static readonly (string name, bool loop)[] Clips =
        {
            ("idle", true),
            ("push", true),
            ("jump", false),
            ("grind", true),
        };

        [MenuItem("PokeMemories/Build Skater Animations")]
        public static void Build()
        {
            Directory.CreateDirectory(AnimationRoot);
            AssetDatabase.Refresh();

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var stateMachine = controller.layers[0].stateMachine;

            foreach (var (name, loop) in Clips)
            {
                var clip = BuildClip(name, loop);
                if (clip == null) continue;

                var state = stateMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == name)
                    ?? stateMachine.AddState(name);
                state.motion = clip;
                if (name == "idle") stateMachine.defaultState = state;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"Skater animations built in {AnimationRoot}");
        }

        static AnimationClip BuildClip(string name, bool loop)
        {
            var folder = SkaterSpriteImporter.SkaterArtRoot + name;
            var sprites = AssetDatabase.FindAssets("t:Sprite", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p)
                .Select(AssetDatabase.LoadAssetAtPath<Sprite>)
                .ToArray();
            if (sprites.Length == 0)
            {
                Debug.LogWarning($"No sprites found in {folder}");
                return null;
            }

            var clipPath = AnimationRoot + name + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null)
            {
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, clipPath);
            }
            clip.frameRate = FramesPerSecond;

            var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
            var keys = sprites
                .Select((sprite, i) => new ObjectReferenceKeyframe { time = i / FramesPerSecond, value = sprite })
                .ToArray();
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            return clip;
        }
    }
}
