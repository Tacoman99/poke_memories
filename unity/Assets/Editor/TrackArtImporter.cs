using UnityEditor;
using UnityEngine;

namespace PokeMemories.Editor
{
    /// <summary>
    /// Import settings for the generated track items and backgrounds (tools/sprites/trackart.mjs).
    /// Item sprites are scaled to the simulation's item sizes at runtime, so pixel density only
    /// matters for the backgrounds: 2048x512 tiles at 100 PPU are 20.48 x 5.12 world units.
    /// </summary>
    public class TrackArtImporter : AssetPostprocessor
    {
        public const string TrackRoot = "Assets/Art/Track/";
        public const string BackgroundRoot = "Assets/Art/Backgrounds/";

        void OnPreprocessTexture()
        {
            var isTrack = assetPath.StartsWith(TrackRoot);
            var isBackground = assetPath.StartsWith(BackgroundRoot);
            if (!isTrack && !isBackground) return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 2048;
            // The 512px ground tile is 2.56 world units; everything else is 100 PPU.
            importer.spritePixelsPerUnit = assetPath.EndsWith("/ground.png") ? 200f : 100f;
            // Gradients band badly when block-compressed; the sky strips are tiny, so keep them exact.
            importer.textureCompression = assetPath.EndsWith("/sky.png") ? TextureImporterCompression.Uncompressed : TextureImporterCompression.CompressedHQ;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
        }
    }
}
