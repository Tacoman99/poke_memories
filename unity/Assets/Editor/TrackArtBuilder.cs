using PokeMemories.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PokeMemories.Editor
{
    /// <summary>
    /// Builds Assets/Art/TrackArt.asset from the generated PNGs and assigns it to the CourseGame in
    /// the open scene. Re-run after regenerating the art with tools/sprites/trackart.mjs.
    /// </summary>
    public static class TrackArtBuilder
    {
        const string AssetPath = "Assets/Art/TrackArt.asset";
        static readonly (string folder, string name, Color tint)[] Sets =
        {
            ("RoseWalk", "Rose Walk", new Color(1f, 0.97f, 0.97f)),
            ("Boardwalk", "Boardwalk Rush", new Color(1f, 0.88f, 0.8f)),
            ("GoldenHour", "Golden Hour", new Color(1f, 0.78f, 0.62f)),
        };

        static Sprite Load(string path)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) throw new System.Exception($"Missing sprite {path}; run tools/sprites/trackart.mjs and let Unity import it.");
            return sprite;
        }

        [MenuItem("PokeMemories/Build Track Art Set")]
        public static string Build()
        {
            var art = AssetDatabase.LoadAssetAtPath<TrackArtSet>(AssetPath);
            if (art == null)
            {
                art = ScriptableObject.CreateInstance<TrackArtSet>();
                AssetDatabase.CreateAsset(art, AssetPath);
            }

            const string track = TrackArtImporter.TrackRoot;
            art.ball = Load(track + "ball.png");
            art.cone = Load(track + "cone.png");
            art.barrier = Load(track + "barrier.png");
            art.kicker = Load(track + "kicker.png");
            art.gap = Load(track + "gap.png");
            art.railBar = Load(track + "rail_bar.png");
            art.railPost = Load(track + "rail_post.png");
            art.step = Load(track + "step.png");
            art.ground = Load(track + "ground.png");

            art.backgrounds = new TrackArtSet.BackgroundSet[Sets.Length];
            for (var i = 0; i < Sets.Length; i++)
            {
                var root = $"{TrackArtImporter.BackgroundRoot}{Sets[i].folder}/";
                art.backgrounds[i] = new TrackArtSet.BackgroundSet
                {
                    name = Sets[i].name,
                    sky = Load(root + "sky.png"),
                    far = Load(root + "far.png"),
                    mid = Load(root + "mid.png"),
                    near = Load(root + "near.png"),
                    groundTint = Sets[i].tint,
                };
            }
            EditorUtility.SetDirty(art);
            AssetDatabase.SaveAssets();

            var game = Object.FindAnyObjectByType<CourseGame>();
            if (game == null) return "Built TrackArt.asset (no CourseGame in the open scene to wire).";
            var serialized = new SerializedObject(game);
            serialized.FindProperty("art").objectReferenceValue = art;
            serialized.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(game.gameObject.scene);
            EditorSceneManager.SaveScene(game.gameObject.scene);
            return $"Built TrackArt.asset and assigned it to {game.name} in {game.gameObject.scene.name}.";
        }
    }
}
