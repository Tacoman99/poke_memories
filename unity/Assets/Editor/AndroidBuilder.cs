using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace PokeMemories.EditorTools
{
    public static class AndroidBuilder
    {
        const string ApkPath = "Builds/Android/PokeMemories.apk";

        [MenuItem("PokeMemories/Configure Android Player")]
        public static void Configure()
        {
            PlayerSettings.productName = "Poke Memories";
            PlayerSettings.companyName = "tacoman99";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.tacoman99.pokememories");
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.useCustomKeystore = false; // Unity's debug keystore signs the APK
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene("Assets/Scenes/Course.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/SkaterPreview.unity", true),
            };
            AssetDatabase.SaveAssets();
        }

        [MenuItem("PokeMemories/Build Android APK")]
        public static void Build()
        {
            Configure();
            Directory.CreateDirectory(Path.GetDirectoryName(ApkPath));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Course.unity", "Assets/Scenes/SkaterPreview.unity" },
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            });
            Debug.Log("APK build " + report.summary.result + " size=" + report.summary.totalSize);
        }
    }
}
