using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace PokeMemories.EditorTools
{
    public static class WebBuilder
    {
        static readonly string OutDir = System.Environment.GetEnvironmentVariable("WEB_OUT") ?? "Builds/Web";
        const string TemplateDir = "Assets/WebGLTemplates/PokeMobile";

        [MenuItem("PokeMemories/Configure WebGL Player")]
        public static void Configure()
        {
            PlayerSettings.productName = "Poke Memories";
            PlayerSettings.companyName = "tacoman99";
            // Menu and Rose Bowl suit portrait, but the Course and Endless modes are landscape-framed.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.WebGL.template = "PROJECT:PokeMobile";
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = false; // vercel.json sets Content-Encoding
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
            PlayerSettings.WebGL.initialMemorySize = 64;
            PlayerSettings.WebGL.maximumMemorySize = 512;     // iOS Safari kills tabs with large heaps
            PlayerSettings.WebGL.memoryGrowthMode = WebGLMemoryGrowthMode.Geometric;
            PlayerSettings.WebGL.threadsSupport = false;      // no SharedArrayBuffer needed
            PlayerSettings.WebGL.nameFilesAsHashes = false;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.High);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene("Assets/Scenes/Course.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/SkaterPreview.unity", true),
            };
            WriteIcons();
            AssetDatabase.SaveAssets();
        }

        static void WriteIcons()
        {
            var dir = Path.Combine(TemplateDir, "icons");
            Directory.CreateDirectory(dir);
            foreach (var (name, size) in new[] { ("apple-touch-icon.png", 180), ("icon-192.png", 192), ("icon-512.png", 512) })
                File.WriteAllBytes(Path.Combine(dir, name), MakeIcon(size).EncodeToPNG());
        }

        // Pink rounded-gradient square with a white heart.
        static Texture2D MakeIcon(int s)
        {
            var t = new Texture2D(s, s, TextureFormat.RGBA32, false);
            var top = new Color(1f, 0.60f, 0.75f);
            var bot = new Color(0.88f, 0.34f, 0.50f);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    var c = Color.Lerp(bot, top, y / (float)s);
                    float u = (x / (float)s - 0.5f) * 2.6f, v = (y / (float)s - 0.45f) * 2.6f;
                    float a = u * u + v * v - 1f;
                    if (a * a * a - u * u * v * v * v < 0f) c = Color.white; // heart curve
                    t.SetPixel(x, y, c);
                }
            t.Apply();
            return t;
        }

        [MenuItem("PokeMemories/Build WebGL")]
        public static void Build()
        {
            Configure();
            Directory.CreateDirectory(OutDir);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Course.unity", "Assets/Scenes/SkaterPreview.unity" },
                locationPathName = OutDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            });
            Debug.Log("WebGL build " + report.summary.result + " size=" + report.summary.totalSize);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
