#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using GalacticDisk;
using UnityEditor;
using UnityEngine;

public static class DistributionChecks
{
    public static void Run()
    {
        try
        {
            string filename = "galacticdisk-windows.bundle";
            var packaged = Assembly.Load(File.ReadAllBytes("PackagedMod.bin"));
            string resource = GalaxyShaderAssets.ResourcePrefix + filename;
            byte[] embedded;
            using (Stream stream = packaged.GetManifestResourceStream(resource))
            using (var data = new MemoryStream())
            {
                Require(stream != null, "Built DLL contains Windows shaders"); stream.CopyTo(data); embedded = data.ToArray();
            }
            using (var sha = SHA256.Create())
                Require(Convert.ToBase64String(sha.ComputeHash(embedded)) == Convert.ToBase64String(sha.ComputeHash(
                    File.ReadAllBytes("TestGame/GameData/GalacticDisk/Assets/" + filename))), "Embedded shaders match distributed bundle");
            string[] names = { "galaxyvolume", "galaxymeshstars", "galaxyparticles", "galaxydistant", "galaxysingularity" };
            var shaders = GalaxyShaderAssets.LoadShaders(names, "windows", Path.GetFullPath("DLLOnly/Plugins"),
                Path.GetFullPath("NoGameData"), packaged.GetManifestResourceStream);
            foreach (Shader shader in shaders) Require(shader != null && shader.isSupported, "DLL-only installation loads supported shaders");
            string plugins = Path.GetFullPath("Relocated Mod/Plugins"), assets = Path.GetFullPath("Relocated Mod/Assets");
            Directory.CreateDirectory(plugins); Directory.CreateDirectory(assets);
            File.WriteAllBytes(Path.Combine(assets, filename), embedded);
            shaders = GalaxyShaderAssets.LoadShaders(names, "windows", plugins, Path.GetFullPath("WrongRoot"), _ => null);
            Require(shaders.Length == 5, "Assembly-relative Assets work with a renamed mod folder and Plugins layout");
            string game = Path.GetFullPath("TestGame");
            var paths = GalaxyShaderAssets.CandidatePaths("windows", null, Path.Combine(game, "KSP_x64_Data", "..", "."));
            Require(paths.Count == 1 && paths[0] == Path.Combine(game, "GameData", "GalacticDisk", "Assets", filename), "KSP root with data-folder parent segments is normalized");
            bool failed = false;
            try { GalaxyShaderAssets.LoadShaders(names, "windows", null, Path.GetFullPath("NoAssets"), _ => null); }
            catch (FileNotFoundException error) { failed = error.Message.Contains("complete mod package"); }
            Require(failed, "Missing resources give installation instructions");
            Directory.CreateDirectory("DistributionVerification");
            File.WriteAllText("DistributionVerification/Distribution.txt", "PASS - Unity " + Application.unityVersion + "\n" +
                "Loaded all five shaders directly from the production DLL with no Assets folder.\n" +
                "Embedded/distributed bytes match; renamed folder + Plugins layout and KSP root normalization verified.\n" +
                "Explicit missing-asset diagnostics verified. Test uses actual compiled shader assets.\n");
            Debug.Log("GALACTICDISK_DISTRIBUTION_PASSED");
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException("Distribution check failed: " + message); }
}
#endif
