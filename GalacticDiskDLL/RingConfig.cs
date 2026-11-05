using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
using static Targeting;

namespace GalacticDisk
{
    /// <summary>
    /// Used in <see cref="ConfigEditor"/> to create ring configs.
    /// </summary>
    public static class RingConfig
    {
        /// <summary>
        /// Node used by the ring config.
        /// </summary>
        public const string NodeName = "GalacticDiskRingDefinition";
        
        /// <summary>
        /// Array of config fields matched to shared fields used by the ring config.
        /// </summary>
        private static readonly string[,] fields = {
            { "planet", "planet" },
            { "innerRingRadius", "innerRingRadius" },
            { "ringRadius", "galaxyRadius" },
            { "ringThickness", "diskThickness" },
            { "objectScale", "objectScale" },
            { "renderScale", "renderScale" },
            { "particleCount", "starCount" },
            { "minParticleSize", "minStarSize" },
            { "maxParticleSize", "maxStarSize" },
            { "diskScaleLengthFraction", "diskScaleLengthFraction" },
            { "color", "colorInterarm" },
            { "rotationX", "rotationX" },
            { "rotationY", "rotationY" },
            { "rotationZ", "rotationZ" },
            { "alignRotationToOrbit", "alignRotationToOrbit" },
            { "rotationPeriod", "rotationPeriod" },
            { "spinAroundDiskAxis", "spinAroundDiskAxis" },
            { "usesTexture", "usesTexture" },
            { "texturePath", "texturePath" }
        };

        /// <summary>
        /// Checks if a value is a value used for volumetric rendering.
        /// </summary>
        public static bool IsVolumeOption(string name)
        {
            switch (name)
            {
                // L O N G
                case "seed":
                case "starBrightness":
                case "enableDustClouds":
                case "dustCloudCount":
                case "minDustCloudSize":
                case "maxDustCloudSize":
                case "dustCloudThickness":
                case "dustCloudClumpiness":
                case "dustOpacity":
                case "dustColor":
                case "dustScattering":
                case "enableVolume":
                case "volumeIntensity":
                case "volumeExposure":
                case "volumeSteps":
                case "cloudResolution":
                case "noiseFrequency":
                case "starDustSamples": return true;
                default: return false;
            }
        }
        
        /// <summary>
        /// Get actual name of a provided field.
        /// Currently only applies aliases for:
        /// starBrightness -> particleBrightness
        /// </summary>
        public static string OptionName(string name)
            => name == "starBrightness" ? "particleBrightness" : name;
        
        /// <summary>
        /// Get the shared field name from config field.
        /// </summary>
        internal static string SharedFieldName(string field)
        {
            for (int i = 0; i < fields.GetLength(0); i++)
                if (fields[i, 0] == field)
                    return fields[i, 1];
            return null;
        }
        /// <summary>
        /// Get the config field name from a shared field.
        /// </summary>
        internal static string ConfigFieldName(string field)
        {
            for (int i = 0; i < fields.GetLength(0); i++)
                if (fields[i, 1] == field)
                    return fields[i, 0];
            return null;
        }

        /// <summary>
        /// Read the <see cref="ConfigNode"/> of a ring and turn it into a <see cref="GalaxyConfig"/>.
        /// </summary>
        public static GalaxyConfig Read(ConfigNode node)
        {
            // Create the default galaxy settings
            GalaxySettings defaults = new GalaxySettings();
            // Create the default galaxy config
            GalaxyConfig config = new GalaxyConfig
            {
                isRing = true,
                starCount = defaults.starCount,
                diskThickness = defaults.diskThickness,
                minStarSize = defaults.minStarSize,
                maxStarSize = defaults.maxStarSize,
                diskScaleLengthFraction = defaults.diskScaleLengthFraction,
                colorInterarm = defaults.colorArms
            };

            // Load each config field
            for (int i = 0; i < fields.GetLength(0); i++)
            {
                string key = fields[i, 0];
                if (!node.HasValue(key)) continue;
                FieldInfo field = typeof(GalaxyConfig).GetField(fields[i, 1]);
                if (field.FieldType == typeof(string))
                    field.SetValue(config, node.GetValue(key));
                else if (VolumeConfig.TryParse(field.FieldType, node.GetValue(key), out object val))
                    field.SetValue(config, val);
            }
            
            // Load volumetric config fields
            VolumeConfig.Read(node, config);

            // Get the full texture path if texturePath is present, otherwise just disable textures
            if (!string.IsNullOrEmpty(config.texturePath))
                config.texturePath = System.IO.Path.Combine(KSPUtil.ApplicationRootPath, "GameData", config.texturePath);
            else
                config.usesTexture = false;

            // Finish the config by adding normal disk values in order to make it loadable
            config.armCount = 0;
            config.interarmFraction = 1f;
            config.bulgeFraction = 0f;
            config.bulgeScaleX = config.bulgeScaleY = config.bulgeScaleZ = 0f;
            config.colorBulge = config.colorArms = config.armHighlightColor = config.colorInterarm;
            config.armHighlightChance = 0f;
            Configure(config.volume);

            return config;
        }

        /// <summary>
        /// Configure volumetric parameters of a <see cref="RingConfig"/>.
        /// </summary>
        public static void Configure(GalaxySettings volume)
        {
            volume.armCount = 0;
            volume.interarmFraction = 1f;
            volume.bulgeFraction = 0f;
            volume.bulgeScaleX = volume.bulgeScaleY = volume.bulgeScaleZ = 0f;
            volume.enableBar = false;
            volume.enableStarFormation = false;
            volume.logarithmicArms = false;
            volume.armHighlightChance = 0f;
            volume.dustArmBias = 0f;
            volume.dustLaneStrength = 1f;
            volume.colorBulge = volume.colorArms = volume.armHighlightColor = volume.colorInterarm;
        }

        /// <summary>
        /// Convert a <see cref="GalaxyConfig"/> into a string to show in <see cref="ConfigEditor"/>.
        /// This treats it as a ring, not a disk.
        /// </summary>
        public static string Export(GalaxyConfig config, string planetName)
        {
            // Create the string builder and node
            StringBuilder text = new StringBuilder(NodeName + "\n{\n");
            // Add render mode
            text.Append("    renderMode = ").Append(config.renderMode).Append('\n');
            // Add all config fields
            for (int i = 0; i < fields.GetLength(0); i++)
            {
                string key = fields[i, 0];
                object val = key == "planet" ? planetName : typeof(GalaxyConfig).GetField(fields[i, 1]).GetValue(config);
                if ((key == "usesTexture" || key == "texturePath") && !config.usesTexture) continue;
                text.Append("    ").Append(key).Append(" = ").Append(VolumeConfig.Format(val)).Append('\n');
            }
            // Add volumetric config fields
            foreach (FieldInfo field in VolumeConfig.OptionFields)
                if (IsVolumeOption(field.Name))
                    text.Append("    ").Append(OptionName(field.Name)).Append(" = ")
                        .Append(VolumeConfig.Format(field.GetValue(config.volume))).Append('\n');
            // Close the node and return the string
            return text.Append('}').ToString();
        }
    }
}
