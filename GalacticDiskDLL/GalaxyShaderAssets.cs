using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GalacticDisk
{
    internal static class GalaxyShaderAssets
    {
        private static Shader volume;
        private static Shader stars;
        private static Shader particles;
        internal const string ResourcePrefix = "GalacticDisk.Shaders.";

        public static void Load(out Shader volumeShader, out Shader starShader)
        {
            if (volume == null || stars == null)
            {
                Shader[] shaders = LoadShaders(new[] { "galaxyvolume", "galaxymeshstars" });
                volume = shaders[0]; stars = shaders[1];
            }
            volumeShader = volume; starShader = stars;
        }

        public static Shader LoadParticles()
        {
            if (particles == null) particles = LoadShaders(new[] { "galaxyparticles" })[0];
            return particles;
        }

        internal static string CurrentPlatform
        {
            get
            {
                return Application.platform == RuntimePlatform.LinuxPlayer || Application.platform == RuntimePlatform.LinuxEditor ? "linux" :
                    Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.OSXEditor ? "macos" : "windows";
            }
        }

        private static Shader[] LoadShaders(string[] names)
        {
            var assembly = typeof(GalaxyShaderAssets).Assembly;
            return LoadShaders(names, CurrentPlatform, string.IsNullOrEmpty(assembly.Location) ? null : Path.GetDirectoryName(assembly.Location),
                KSPUtil.ApplicationRootPath, assembly.GetManifestResourceStream);
        }

        internal static List<string> CandidatePaths(string platform, string assemblyDirectory, string gameRoot)
        {
            string filename = "galacticdisk-" + platform + ".bundle";
            var paths = new List<string>();
            Action<string> add = candidate =>
            {
                try
                {
                    string full = Path.GetFullPath(candidate);
                    if (!paths.Contains(full)) paths.Add(full);
                }
                catch (ArgumentException) { }
                catch (NotSupportedException) { }
                catch (PathTooLongException) { }
            };
            if (!string.IsNullOrEmpty(assemblyDirectory))
            {
                add(Path.Combine(assemblyDirectory, "Assets", filename));
                add(Path.Combine(assemblyDirectory, "..", "Assets", filename)); // Plugins/GalacticDisk.dll
            }
            if (!string.IsNullOrEmpty(gameRoot))
                add(Path.Combine(gameRoot, "GameData", "GalacticDisk", "Assets", filename));
            return paths;
        }

        internal static Shader[] LoadShaders(string[] names, string platform, string assemblyDirectory, string gameRoot,
            Func<string, Stream> openEmbedded)
        {
            var paths = CandidatePaths(platform, assemblyDirectory, gameRoot);
            var failures = new List<string>();
            foreach (string path in paths)
            {
                if (!File.Exists(path)) continue;
                try { return ReadShaders(AssetBundle.LoadFromFile(path), names); }
                catch (InvalidOperationException error) { failures.Add(path + ": " + error.Message); }
            }
            string resource = ResourcePrefix + "galacticdisk-" + platform + ".bundle";
            using (Stream stream = openEmbedded(resource))
            {
                if (stream != null)
                {
                    using (var data = new MemoryStream())
                    {
                        stream.CopyTo(data);
                        try { return ReadShaders(AssetBundle.LoadFromMemory(data.ToArray()), names); }
                        catch (InvalidOperationException error) { failures.Add("Embedded " + resource + ": " + error.Message); }
                    }
                }
            }
            string detail = failures.Count == 0 ? "" : "\n" + string.Join("\n", failures.ToArray());
            if (failures.Count > 0)
                throw new InvalidOperationException("GalacticDisk shaders could not be loaded for " + platform +
                    ". Install the matching DLL and shader bundle, and check graphics-device support." + detail);
            throw new FileNotFoundException("GalacticDisk shader bundle for " + platform +
                " is missing. Install the complete mod package or a DLL with embedded shaders. Searched:\n" +
                string.Join("\n", paths.ToArray()));
        }

        private static Shader[] ReadShaders(AssetBundle bundle, string[] names)
        {
            if (bundle == null) throw new InvalidOperationException("Cannot read the shader bundle.");
            try
            {
                var shaders = new Shader[names.Length];
                for (int i = 0; i < names.Length; i++)
                {
                    shaders[i] = bundle.LoadAsset<Shader>("assets/galacticdisk/shaders/" + names[i] + ".shader");
                    if (shaders[i] == null || !shaders[i].isSupported)
                        throw new InvalidOperationException("Shader " + names[i] + " is missing or unsupported by this graphics device.");
                }
                return shaders;
            }
            finally { bundle.Unload(false); }
        }
    }
}
