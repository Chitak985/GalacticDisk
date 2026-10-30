using System;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

public static class BuildGalaxyShaders
{
    [MenuItem("GalacticDisk/Build Windows shaders")]
    public static void BuildWindows() { Build(BuildTarget.StandaloneWindows64, "windows"); }
    [MenuItem("GalacticDisk/Build Linux shaders")]
    public static void BuildLinux() { Build(BuildTarget.StandaloneLinux64, "linux"); }
    [MenuItem("GalacticDisk/Build macOS shaders")]
    public static void BuildMacOS() { Build(BuildTarget.StandaloneOSX, "macos"); }

    private static void Build(BuildTarget target, string platform)
    {
        string output = Path.GetFullPath("../GalacticDisk/Assets");
        Directory.CreateDirectory(output);
        string staging = Path.GetFullPath("Library/GalacticDiskBundles/" + platform);
        Directory.CreateDirectory(staging);
        var build = new AssetBundleBuild
        {
            assetBundleName = "galacticdisk-" + platform + ".bundle",
            assetNames = new[] {
                "Assets/GalacticDisk/Shaders/GalaxyVolume.shader",
                "Assets/GalacticDisk/Shaders/GalaxyMeshStars.shader"
            }
        };
        var manifest = BuildPipeline.BuildAssetBundles(staging, new[] { build },
            BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle, target);
        if (manifest == null) throw new InvalidOperationException("Shader bundle build failed for " + platform);
        foreach (string path in build.assetNames)
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            foreach (var message in ShaderUtil.GetShaderMessages(shader))
                if (message.severity == ShaderCompilerMessageSeverity.Error)
                    throw new InvalidOperationException(path + ": " + message.message);
        }
        File.Copy(Path.Combine(staging, build.assetBundleName), Path.Combine(output, build.assetBundleName), true);
        Debug.Log("GALACTICDISK_SHADERS_BUILT: " + output);
    }
}
