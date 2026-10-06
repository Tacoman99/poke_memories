// [UNITY-SKILL:SPRITEATLAS]
// [TYPE:PREBUILD]
// Builds the sprite atlases (built-in delivery) before every player build so item sprites and
// skater frames batch instead of breaking draw calls. Call Generate() to build them in the Editor.
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.U2D;
using UnityEngine;

public class SpriteAtlasPrebuildGenerator : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report) => Generate();

    public static void Generate()
    {
        EditorSettings.spritePackerMode = SpritePackerMode.SpriteAtlasV2;

        // Track items. ground.png is excluded: it is drawn tiled and needs its own texture to repeat cleanly.
        // Backgrounds are 2048x512 seamless tiles for the same reason and stay un-atlased.
        GenerateAtlas("Assets/Art/Track", "Assets/Atlases/Track.spriteatlasv2", path => !path.EndsWith("/ground.png"));
        foreach (var clipDir in Directory.GetDirectories("Assets/Art/Skater"))
        {
            var clip = Path.GetFileName(clipDir);
            GenerateAtlas("Assets/Art/Skater/" + clip, $"Assets/Atlases/Skater_{clip}.spriteatlasv2", _ => true);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[SpriteAtlas] Sprite packer mode: " + EditorSettings.spritePackerMode);
    }

    static void GenerateAtlas(string folder, string atlasPath, System.Func<string, bool> include)
    {
        var sprites = AssetDatabase.FindAssets("t:Sprite", new[] { folder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.StartsWith("Assets/") && include(p))
            .Select(AssetDatabase.LoadAssetAtPath<Sprite>)
            .Where(s => s != null)
            .Cast<Object>()
            .ToArray();
        if (sprites.Length == 0) return;

        Directory.CreateDirectory(Path.GetDirectoryName(atlasPath));
        var asset = File.Exists(atlasPath) ? SpriteAtlasAsset.Load(atlasPath) : new SpriteAtlasAsset();
        if (File.Exists(atlasPath))
        {
            var runtimeAtlas = AssetDatabase.LoadAssetAtPath<UnityEngine.U2D.SpriteAtlas>(atlasPath);
            var existing = runtimeAtlas != null ? runtimeAtlas.GetPackables() : null;
            if (existing != null && existing.Length > 0) asset.Remove(existing);
        }
        asset.Add(sprites);
        SpriteAtlasAsset.Save(asset, atlasPath);
        AssetDatabase.ImportAsset(atlasPath);

        var importer = (SpriteAtlasImporter)AssetImporter.GetAtPath(atlasPath);
        var texture = importer.textureSettings;
        texture.filterMode = FilterMode.Bilinear;
        texture.generateMipMaps = false;
        importer.textureSettings = texture;
        var packing = importer.packingSettings;
        packing.padding = 4;
        packing.enableAlphaDilation = true;
        packing.enableTightPacking = false;
        importer.packingSettings = packing;
        importer.includeInBuild = true;
        importer.SaveAndReimport();
        Debug.Log($"[SpriteAtlas] {atlasPath}: {sprites.Length} sprites");
    }
}
