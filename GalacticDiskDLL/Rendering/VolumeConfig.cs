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
                DiskRenderMode mode;
                if (Enum.TryParse(node.GetValue("renderMode"), true, out mode) && Enum.IsDefined(typeof(DiskRenderMode), mode))
                    config.renderMode = mode;
                else Debug.LogWarning("[GalacticDisk] Unknown renderMode; using Particles.");
            }
            foreach (var field in OptionFields)
            {
                if (config.isRing && !RingConfig.IsVolumeOption(field.Name)) continue;
                string key = config.isRing ? RingConfig.OptionName(field.Name) : field.Name;
                object value;
                if (node.HasValue(key) && TryParse(field.FieldType, node.GetValue(key), out value))
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
            {
                var shared = typeof(GalaxyConfig).GetField(field.Name);
                if (shared != null) shared.SetValue(config, field.GetValue(preset));
            }
        }

        public static string Export(GalaxyConfig config)
        {
            if (config.renderMode != DiskRenderMode.Volumetric) return "";
            var text = new StringBuilder();
            foreach (var field in OptionFields)
                text.Append("    ").Append(field.Name).Append(" = ").Append(Format(field.GetValue(config.volume))).Append('\n');
            return text.ToString();
        }

        public static string Format(object value)
        {
            if (value is Color)
            {
                var color = (Color)value;
                return string.Join(", ", color.r.ToString(CultureInfo.InvariantCulture), color.g.ToString(CultureInfo.InvariantCulture),
                    color.b.ToString(CultureInfo.InvariantCulture), color.a.ToString(CultureInfo.InvariantCulture));
            }
            if (value is bool) return (bool)value ? "true" : "false";
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public static bool TryParse(Type type, string text, out object value)
        {
            value = null;
            if (type == typeof(bool))
            {
                bool parsed; if (!bool.TryParse(text, out parsed)) return false; value = parsed;
            }
            else if (type == typeof(int))
            {
                int parsed; if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) return false; value = parsed;
            }
            else if (type == typeof(float))
            {
                float parsed;
                if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ||
                    float.IsNaN(parsed) || float.IsInfinity(parsed)) return false;
                value = parsed;
            }
            else if (type == typeof(Color))
            {
                Color parsed; if (!GalaxySettings.TryParseColor(text, out parsed)) return false; value = parsed;
            }
            else return false;
            return true;
        }
    }
}
