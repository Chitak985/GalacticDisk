using System.Reflection;
using System.Text;
using UnityEngine;

namespace GalacticDisk.Rendering
{
    // Ring definitions map onto the shared renderer without exposing galaxy populations.
    public static class RingConfig
    {
        public const string NodeName = "GalacticDiskRingDefinition";
        private static readonly string[,] fields = {
            { "planet", "planet" }, { "innerRingRadius", "innerRingRadius" },
            { "ringRadius", "galaxyRadius" }, { "ringThickness", "diskThickness" },
            { "objectScale", "objectScale" }, { "renderScale", "renderScale" },
            { "particleCount", "starCount" }, { "minParticleSize", "minStarSize" },
            { "maxParticleSize", "maxStarSize" }, { "diskScaleLengthFraction", "diskScaleLengthFraction" },
            { "color", "colorInterarm" }, { "rotationX", "rotationX" }, { "rotationY", "rotationY" },
            { "rotationZ", "rotationZ" }, { "alignRotationToOrbit", "alignRotationToOrbit" },
            { "rotationPeriod", "rotationPeriod" }, { "spinAroundDiskAxis", "spinAroundDiskAxis" },
            { "usesTexture", "usesTexture" }, { "texturePath", "texturePath" }
        };

        public static bool IsVolumeOption(string name)
        {
            switch (name)
            {
                case "seed": case "starBrightness": case "enableDustClouds": case "dustCloudCount":
                case "minDustCloudSize": case "maxDustCloudSize": case "dustCloudThickness":
                case "dustCloudClumpiness": case "dustOpacity": case "dustColor": case "dustScattering":
                case "enableVolume": case "volumeIntensity": case "volumeExposure": case "volumeSteps":
                case "cloudResolution": case "noiseFrequency": case "starDustSamples": return true;
                default: return false;
            }
        }
        public static string OptionName(string name) { return name == "starBrightness" ? "particleBrightness" : name; }
        internal static string SharedFieldName(string key)
        {
            for (int i = 0; i < fields.GetLength(0); i++) if (fields[i, 0] == key) return fields[i, 1];
            return null;
        }
        internal static string ConfigFieldName(string shared)
        {
            for (int i = 0; i < fields.GetLength(0); i++) if (fields[i, 1] == shared) return fields[i, 0];
            return null;
        }

        public static GalaxyConfig Read(ConfigNode node)
        {
            GalaxySettings defaults = new GalaxySettings();
            GalaxyConfig config = new GalaxyConfig { isRing = true, starCount = defaults.starCount,
                diskThickness = defaults.diskThickness, minStarSize = defaults.minStarSize,
                maxStarSize = defaults.maxStarSize, diskScaleLengthFraction = defaults.diskScaleLengthFraction,
                colorInterarm = defaults.colorArms };
            for (int i = 0; i < fields.GetLength(0); i++)
            {
                string key = fields[i, 0];
                if (!node.HasValue(key)) continue;
                FieldInfo field = typeof(GalaxyConfig).GetField(fields[i, 1]);
                if (field.FieldType == typeof(string))
                    field.SetValue(config, node.GetValue(key));
                else if (VolumeConfig.TryParse(field.FieldType, node.GetValue(key), out object value))
                    field.SetValue(config, value);
            }
            VolumeConfig.Read(node, config);
            if (!string.IsNullOrEmpty(config.texturePath))
                config.texturePath = System.IO.Path.Combine(KSPUtil.ApplicationRootPath, "GameData", config.texturePath);
            else
                config.usesTexture = false;
            Normalize(config);
            return config;
        }

        public static void Normalize(GalaxyConfig config)
        {
            config.armCount = 0; config.interarmFraction = 1f; config.bulgeFraction = 0f;
            config.bulgeScaleX = config.bulgeScaleY = config.bulgeScaleZ = 0f;
            config.colorBulge = config.colorArms = config.armHighlightColor = config.colorInterarm;
            config.armHighlightChance = 0f;
            Configure(config.volume);
        }
        public static void Configure(GalaxySettings settings)
        {
            settings.armCount = 0; settings.interarmFraction = 1f; settings.bulgeFraction = 0f;
            settings.bulgeScaleX = settings.bulgeScaleY = settings.bulgeScaleZ = 0f;
            settings.enableBar = false; settings.enableStarFormation = false;
            settings.logarithmicArms = false; settings.armHighlightChance = 0f;
            settings.dustArmBias = 0f; settings.dustLaneStrength = 1f;
            settings.colorBulge = settings.colorArms = settings.armHighlightColor = settings.colorInterarm;
        }

        public static string Export(GalaxyConfig config, string planetName)
        {
            var text = new StringBuilder(NodeName + "\n{\n");
            text.Append("    renderMode = ").Append(config.renderMode).Append('\n');
            for (int i = 0; i < fields.GetLength(0); i++)
            {
                string key = fields[i, 0];
                object value = key == "planet" ? planetName : typeof(GalaxyConfig).GetField(fields[i, 1]).GetValue(config);
                if ((key == "usesTexture" || key == "texturePath") && !config.usesTexture) continue;
                text.Append("    ").Append(key).Append(" = ").Append(VolumeConfig.Format(value)).Append('\n');
            }
            foreach (var field in VolumeConfig.OptionFields)
                if (IsVolumeOption(field.Name))
                    text.Append("    ").Append(OptionName(field.Name)).Append(" = ")
                        .Append(VolumeConfig.Format(field.GetValue(config.volume))).Append('\n');
            return text.Append('}').ToString();
        }
    }
}
