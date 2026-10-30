using System;
using System.IO;
using UnityEngine;

namespace GalacticDisk.Rendering
{
    internal static class GalaxyShaderAssets
    {
        private static Shader volume;
        private static Shader stars;

        public static void Load(out Shader volumeShader, out Shader starShader)
        {
            if (volume == null || stars == null)
            {
                string platform = Application.platform == RuntimePlatform.LinuxPlayer || Application.platform == RuntimePlatform.LinuxEditor ? "linux" :
                    Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.OSXEditor ? "macos" : "windows";
                string path = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "GalacticDisk", "Assets", "galacticdisk-" + platform + ".bundle");
                if (!File.Exists(path)) throw new FileNotFoundException("GalacticDisk volumetric shader bundle is missing: " + path);
                AssetBundle bundle = AssetBundle.LoadFromFile(path);
                if (bundle == null) throw new InvalidOperationException("Cannot load GalacticDisk shader bundle: " + path);
                try
                {
                    volume = bundle.LoadAsset<Shader>("assets/galacticdisk/shaders/galaxyvolume.shader");
                    stars = bundle.LoadAsset<Shader>("assets/galacticdisk/shaders/galaxymeshstars.shader");
                    if (volume == null || stars == null || !volume.isSupported || !stars.isSupported)
                        throw new InvalidOperationException("GalacticDisk volumetric shaders are missing or unsupported by this graphics device.");
                }
                catch { volume = null; stars = null; throw; }
                finally { bundle.Unload(false); }
            }
            volumeShader = volume;
            starShader = stars;
        }
    }
}
