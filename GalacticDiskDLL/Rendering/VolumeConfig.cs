using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace GalacticDisk.Rendering
{
    public enum DiskRenderMode { Particles, Volumetric }

    // One field list drives parsing, toolbar editing, and export so new controls round-trip.
    public static class VolumeConfig
    {
        public static readonly FieldInfo[] OptionFields = GetOptionFields();

        private static FieldInfo[] GetOptionFields()
        {
            var fields = new List<FieldInfo>();
            foreach (var field in typeof(GalaxySettings).GetFields(BindingFlags.Instance | BindingFlags.Public))
                if (typeof(GalaxyConfig).GetField(field.Name) == null) fields.Add(field);
            fields.Sort((a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
            return fields.ToArray();
        }

        public static void Read(ConfigNode node, GalaxyConfig config)
        {
            if (node.HasValue("renderMode"))
            {
                if (Enum.TryParse(node.GetValue("renderMode"), true, out DiskRenderMode mode) && Enum.IsDefined(typeof(DiskRenderMode), mode))
                    config.renderMode = mode;
                else Debug.LogWarning("[GalacticDisk] Unknown renderMode; using Particles.");
            }
            foreach (var field in OptionFields)
            {
                if (config.isRing && !RingConfig.IsVolumeOption(field.Name)) continue;
                string key = config.isRing ? RingConfig.OptionName(field.Name) : field.Name;
                if (node.HasValue(key) && TryParse(field.FieldType, node.GetValue(key), out object value))
                    field.SetValue(config.volume, value);
            }
        }

        public static GalaxySettings SettingsFor(GalaxyConfig config)
        {
            var result = new GalaxySettings();
            foreach (var field in typeof(GalaxySettings).GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                var shared = typeof(GalaxyConfig).GetField(field.Name);
                field.SetValue(result, shared != null ? shared.GetValue(config) : field.GetValue(config.volume));
            }
            if (config.isRing) RingConfig.Configure(result);
            result.Validate();
            return result;
        }

        public static void ApplyMilkyWayPreset(GalaxyConfig config)
        {
            var preset = new GalaxySettings();
            config.renderMode = DiskRenderMode.Volumetric;
            config.volume = preset;
            foreach (var field in typeof(GalaxySettings).GetFields(BindingFlags.Instance | BindingFlags.Public))
                (typeof(GalaxyConfig).GetField(field.Name))?.SetValue(config, field.GetValue(preset));
        }

        public static string Export(GalaxyConfig config, bool includeInactive = false)
        {
            if (!includeInactive && config.renderMode != DiskRenderMode.Volumetric) return "";
            var text = new StringBuilder();
            foreach (var field in OptionFields)
                text.Append("    ").Append(field.Name).Append(" = ").Append(Format(field.GetValue(config.volume))).Append('\n');
            return text.ToString();
        }

        public static string Format(object value)
        {
            if (value is Color color)
                return string.Join(", ", color.r.ToString("R", CultureInfo.InvariantCulture), color.g.ToString("R", CultureInfo.InvariantCulture),
                    color.b.ToString("R", CultureInfo.InvariantCulture), color.a.ToString("R", CultureInfo.InvariantCulture));

            if (value is float v)
                return v.ToString("R", CultureInfo.InvariantCulture);
            if (value is bool v1)
                return v1 ? "true" : "false";
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public static bool TryParse(Type type, string text, out object value)
        {
            value = null;
            if (type == typeof(bool))
            {
                if (!bool.TryParse(text, out bool parsed))
                    return false;
                value = parsed;
            }
            else if (type == typeof(int))
            {
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                    return false;
                value = parsed;
            }
            else if (type == typeof(float))
            {
                if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) ||
                    float.IsNaN(parsed) || float.IsInfinity(parsed))
                    return false;
                value = parsed;
            }
            else if (type == typeof(Color))
            {
                if (!GalaxySettings.TryParseColor(text, out Color parsed))
                    return false;
                value = parsed;
            }
            else
                return false;
            return true;
        }
    }
}
