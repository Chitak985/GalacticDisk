using System;
using System.IO;
using UnityEngine;

namespace GalacticDisk.Rendering
{
    internal static class GalaxyShaderAssets
    {
        private static Shader volume;
        private static Shader stars;

        /// <summary>
        /// Load the volumetric <see cref="Shader"/> to use for a galaxy/ring.
        /// </summary>
        public static void Load(out Shader volumeShader, out Shader starShader)
        {
            // Ensure the shaders aren't loaded already
            if (volume == null || stars == null)
            {
                // Get the platform
                string platform = Application.platform == RuntimePlatform.LinuxPlayer || Application.platform == RuntimePlatform.LinuxEditor ? "linux" :
                    Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.OSXEditor ? "macos" : "windows";
                
                // Get the AssetBundle path
                string path = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "GalacticDisk", "Assets", "galacticdisk-" + platform + ".bundle");
                
                // Ensure it exists
                if (!File.Exists(path))
                    throw new FileNotFoundException("GalacticDisk volumetric shader bundle is missing: " + path);

                // Get the AssetBundle
                AssetBundle bundle = AssetBundle.LoadFromFile(path) ?? throw new InvalidOperationException("Cannot load GalacticDisk shader bundle: " + path);

                // Load shader assets
                try
                {
                    volume = bundle.LoadAsset<Shader>("assets/galacticdisk/shaders/galaxyvolume.shader");
                    stars = bundle.LoadAsset<Shader>("assets/galacticdisk/shaders/galaxymeshstars.shader");
                    if (volume == null || stars == null || !volume.isSupported || !stars.isSupported)
                        throw new InvalidOperationException("GalacticDisk volumetric shaders are missing or unsupported by this graphics device.");
                }
                catch { volume = null; stars = null; throw; }  // Self destruct if failed
                finally { bundle.Unload(false); }  // Ensure to unload the AssetBundle
            }
            // Set the outputs
            volumeShader = volume;
            starShader = stars;
        }
    }
}
