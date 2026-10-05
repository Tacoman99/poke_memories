using UnityEditor;
using UnityEngine;

namespace PokeMemories.Editor
{
    /// <summary>
    /// Import settings for the skater frames produced by tools/sprites/pack.mjs.
    /// Every frame is 512x512 with the wheel-contact point 12px above the bottom centre,
    /// so all frames share one pivot and her feet stay on the simulation's foot position.
    /// </summary>
    public class SkaterSpriteImporter : AssetPostprocessor
    {
        public const string SkaterArtRoot = "Assets/Art/Skater/";
        public const float PixelsPerUnit = 256f;
        static readonly Vector2 ContactPivot = new Vector2(0.5f, 12f / 512f);

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(SkaterArtRoot)) return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 512;
            importer.spritePixelsPerUnit = PixelsPerUnit;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = ContactPivot;
            settings.spriteMeshType = SpriteMeshType.Tight;
            importer.SetTextureSettings(settings);
        }
    }
}
