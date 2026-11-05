using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace GalacticDisk
{
    internal sealed class ConfigEditorResult
    {
        public GalaxyConfig config;
        public readonly List<string> errors = new List<string>();
        public readonly List<string> warnings = new List<string>();
        public bool IsValid
        {
            get
            {
                return config != null && errors.Count == 0;
            }
        }
    }

    /// <summary>
    /// A GUI window to edit configs in-game.
    /// </summary>
    internal static class ConfigEditor
    {
        private const string GalaxyNode = "GalacticDiskDefinition";
        private static readonly string[] legacyNames = { "color1", "color2", "bulgeRadius" };

        internal static Dictionary<string, Type> Schema(bool ring)
        {
            Dictionary<string, Type> schema = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (FieldInfo field in typeof(GalaxyConfig).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.Name == "volume" || field.Name == "isRing" || (!ring && field.Name == "innerRingRadius"))
                    continue;
                string key = ring ? RingConfig.ConfigFieldName(field.Name) : field.Name;
                if (key != null)
                    schema[key] = field.FieldType;
            }
            schema["renderMode"] = typeof(DiskRenderMode);
            foreach (FieldInfo field in VolumeConfig.OptionFields)
                if (!ring || RingConfig.IsVolumeOption(field.Name))
                    schema[ring ? RingConfig.OptionName(field.Name) : field.Name] = field.FieldType;
            if (!ring)
            {
                schema["color1"] = schema["color2"] = typeof(Color);
                schema["bulgeRadius"] = typeof(float);
            }
            return schema;
        }

        internal static ConfigEditorResult Read(string text)
        {
            ConfigEditorResult result = new ConfigEditorResult();
            List<KeyValuePair<int, string>> tokens = new List<KeyValuePair<int, string>>();
            StringBuilder segment = new StringBuilder();
            int line = 1, segmentLine = 1;
            bool quoted = false;

            void flush()
            {
                string token = segment.ToString().Trim();
                if (token.Length > 0)
                    tokens.Add(new KeyValuePair<int, string>(segmentLine, token));
                segment.Length = 0;
            }
            for (int i = 0; i < (text ?? "").Length; i++)
            {
                char c = text[i];
                if (!quoted && c == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    flush();
                    while (i < text.Length && text[i] != '\n')
                        i++;
                    if (i < text.Length)
                        line++;
                    segmentLine = line;
                    continue;
                }
                if (c == '"')
                    quoted = !quoted;
                if (c == '\n')
                {
                    flush();
                    line++;
                    segmentLine = line;
                    continue;
                }
                if (c == '\r')
                    continue;
                if (!quoted && (c == '{' || c == '}'))
                {
                    flush();
                    tokens.Add(new KeyValuePair<int,string>(line, c.ToString()));
                    segmentLine = line;
                }
                else
                {
                    if (segment.Length == 0)
                        segmentLine = line;
                    segment.Append(c);
                }
            }
            flush();
            if (quoted)
                result.errors.Add("Unclosed quotation mark.");
            if (tokens.Count < 3)
            {
                result.errors.Add("Enter one complete GalacticDiskDefinition or GalacticDiskRingDefinition node.");
                return result;
            }
            string nodeName = tokens[0].Value;
            if (nodeName != GalaxyNode && nodeName != RingConfig.NodeName)
                result.errors.Add($"Line {tokens[0].Key}: expected GalacticDiskDefinition or GalacticDiskRingDefinition.");
            if (tokens[1].Value != "{")
                result.errors.Add($"Line {tokens[1].Key}: expected an opening brace.");
            bool closed = false;
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, int> lines = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 2; i < tokens.Count; i++)
            {
                string token = tokens[i].Value;
                string prefix = "Line " + tokens[i].Key + ": ";
                if (closed)
                {
                    result.errors.Add(prefix + "only one definition is allowed in this editor.");
                    break;
                }
                if (token == "}")
                {
                    closed = true;
                    continue;
                }
                if (token == "{")
                {
                    result.errors.Add(prefix + "nested nodes are not supported here.");
                    continue;
                }
                int equals = token.IndexOf('=');
                if (equals <= 0)
                {
                    result.errors.Add(prefix + "expected key = value.");
                    continue;
                }
                string key = token.Substring(0, equals).Trim(), val = token.Substring(equals + 1).Trim();
                if (values.ContainsKey(key))
                {
                    result.errors.Add(prefix + "duplicate key '" + key + "'.");
                    continue;
                }
                if (val.Length >= 2 && val[0] == '"' && val[val.Length - 1] == '"')
                    val = val.Substring(1, val.Length - 2);
                values[key] = val;
                lines[key] = tokens[i].Key;
            }
            if (!closed) result.errors.Add("Missing closing brace.");
            if (result.errors.Count > 0) return result;
            return ReadValues(nodeName == RingConfig.NodeName, values, true, lines);
        }

        internal static ConfigEditorResult ReadValues(bool ring, Dictionary<string, string> values, bool requireBody,
            Dictionary<string, int> lines = null)
        {
            ConfigEditorResult result = new ConfigEditorResult();
            Dictionary<string, Type> schema = Schema(ring);
            ConfigNode node = new ConfigNode(ring ? RingConfig.NodeName : GalaxyNode);
            
            foreach (KeyValuePair<string,string> entry in values)
            {
                string prefix = lines != null && lines.ContainsKey(entry.Key) ? "Line " + lines[entry.Key] + ": " : "";

                if (!schema.TryGetValue(entry.Key, out Type type))
                {
                    result.warnings.Add(prefix + "unknown key '" + entry.Key + "' is ignored and will be removed by Save to config.");
                    continue;
                }

                if (!TryValue(type, entry.Value, out _))
                {
                    result.errors.Add(prefix + entry.Key + ": expected " + TypeDescription(type) + "; got '" + entry.Value + "'.");
                    continue;
                }

                node.AddValue(entry.Key, entry.Value);
            }
            if (result.errors.Count > 0) return result;
            if (values.TryGetValue("usesTexture", out string textureEnabled) && bool.TryParse(textureEnabled, out bool requestedTexture) && requestedTexture &&
                (!values.TryGetValue("texturePath", out string texturePath) || string.IsNullOrWhiteSpace(texturePath)))
                result.warnings.Add("usesTexture is true but texturePath is empty; the custom texture is disabled and the default texture is used.");

            if (!ring)
            {
                ConfigNode resultingNode = new ConfigNode(GalaxyNode);
                GalaxyConfig defaults = new GalaxyConfig();
                
                foreach (KeyValuePair<string,string> entry in values)
                    if (schema.ContainsKey(entry.Key) && Array.IndexOf(legacyNames, entry.Key) < 0)
                        resultingNode.AddValue(entry.Key, entry.Value);
                
                foreach (string key in new[] { "colorBulge", "colorArms", "colorInterarm", "armHighlightColor" })
                {
                    if (values.ContainsKey(key)) continue;
                    string alias = key == "colorInterarm" ? "color2" : key == "armHighlightColor" ? "" : "color1";
                    resultingNode.AddValue(key, values.TryGetValue(alias, out string value) ? value : VolumeConfig.Format(typeof(GalaxyConfig).GetField(key).GetValue(defaults)));
                }
                
                if (values.ContainsKey("bulgeRadius"))
                    foreach (string key in new[] { "bulgeScaleX", "bulgeScaleY", "bulgeScaleZ" })
                        if (!values.ContainsKey(key))
                            resultingNode.AddValue(key, values["bulgeRadius"]);
                
                foreach (string alias in legacyNames)
                    if (values.ContainsKey(alias))
                        result.warnings.Add(alias + " is deprecated; Save to config writes the replacement fields.");
                
                node = resultingNode;
            }
            try
            {
                result.config = GalaxyConfig.FromConfigNode(node); Validate(result, requireBody);
            }
            catch (Exception error)
            {
                result.errors.Add($"Could not read config: {error.Message}");
            }
            return result;
        }

        private static bool TryValue(Type type, string text, out object val)
        {
            val = null;
            if (type == typeof(string))
            {
                val = text;
                return true;
            }
            if (type == typeof(DiskRenderMode))
            {
                bool valid = Enum.TryParse(text, true, out DiskRenderMode mode) && Enum.IsDefined(typeof(DiskRenderMode), mode);
                val = mode; return valid;
            }
            return VolumeConfig.TryParse(type, text, out val);
        }
        private static string TypeDescription(Type type)
        {
            return type == typeof(bool) ? "true or false" : type == typeof(int) ? "a whole number" :
                type == typeof(float) ? "a finite number" : type == typeof(Color) ? "three or four finite RGB(A) values" : "Particles or Volumetric";
        }

        /// <summary>
        /// Validate the values of a <see cref="ConfigEditorResult"/>.
        /// </summary>
        private static void Validate(ConfigEditorResult result, bool requireBody)
        {
            GalaxyConfig c = result.config;
            string radius = c.isRing ? "ringRadius" : "galaxyRadius", count = c.isRing ? "particleCount" : "starCount";
            
            if (requireBody && (string.IsNullOrWhiteSpace(c.planet) || FlightGlobals.GetBodyByName(c.planet) == null))
                result.errors.Add("planet: select an existing celestial body.");
            if (c.galaxyRadius <= 0f)
                result.errors.Add(radius + " must be positive.");
            if (c.objectScale <= 0f || c.renderScale <= 0f || float.IsInfinity(c.objectScale * c.renderScale) || c.objectScale * c.renderScale <= 0f)
                result.errors.Add("objectScale and renderScale must be positive, and their product must be finite and nonzero.");
            if (c.starCount < 0 || c.starCount > 2000000)
                result.errors.Add(count + " must be between 0 and 2000000.");
            if (c.diskThickness < 0f)
                result.errors.Add((c.isRing ? "ringThickness" : "diskThickness") + " cannot be negative.");
            if (c.minStarSize <= 0f || c.maxStarSize < c.minStarSize)
                result.errors.Add(c.isRing ? "Particle sizes must be positive, with maxParticleSize >= minParticleSize." : "Star sizes must be positive, with maxStarSize >= minStarSize.");
            if (c.diskScaleLengthFraction <= 0f)
                result.errors.Add("diskScaleLengthFraction must be positive.");
            if (c.rotationPeriod < 0f)
                result.errors.Add("rotationPeriod must be zero (no spin) or positive.");
            if (c.isRing && (c.innerRingRadius < 0f || c.innerRingRadius >= c.galaxyRadius))
                result.errors.Add("innerRingRadius must be nonnegative and smaller than ringRadius.");
            if (c.alignRotationToOrbit && (c.rotationX != 0f || c.rotationZ != 0f))
                result.warnings.Add("rotationX and rotationZ are ignored while alignRotationToOrbit is true.");
            if (c.usesTexture && !System.IO.File.Exists(c.texturePath))
                result.warnings.Add("texturePath was not found; the default particle texture will be used.");
            if (c.renderMode == DiskRenderMode.Volumetric && c.usesTexture)
                result.warnings.Add("usesTexture and texturePath only affect Particles mode.");
            
            if (result.errors.Count > 0) return;
            
            GalaxySettings normalized = VolumeConfig.SettingsFor(c);
            foreach (FieldInfo field in typeof(GalaxySettings).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if ((field.Name == "isRing" || field.Name == "innerRingRadius") ||
                    (c.isRing && RingConfig.ConfigFieldName(field.Name) == null && !RingConfig.IsVolumeOption(field.Name)))
                    continue;
                FieldInfo shared = typeof(GalaxyConfig).GetField(field.Name);
                object raw = shared != null ? shared.GetValue(c) : field.GetValue(c.volume);
                object adjusted = field.GetValue(normalized);
                if (!Equals(raw, adjusted))
                    result.warnings.Add((c.isRing ? RingConfig.ConfigFieldName(field.Name) ?? RingConfig.OptionName(field.Name) : field.Name) +
                        (c.renderMode == DiskRenderMode.Particles && !c.isRing ? " would be clamped in Volumetric mode: " : " is clamped for rendering: ") +
                        Display(raw) + " -> " + Display(adjusted) + ".");
            }
        }

        internal static List<string> Differences(GalaxyConfig fields, GalaxyConfig text)
        {
            List<string> differences = new List<string>();
            
            if (fields.isRing != text.isRing)
                differences.Add("definition: fields = " + (fields.isRing ? RingConfig.NodeName : GalaxyNode) + "; config = " + (text.isRing ? RingConfig.NodeName : GalaxyNode));
           
            foreach (KeyValuePair<string,Type> entry in Schema(fields.isRing))
            {
                if (Array.IndexOf(legacyNames, entry.Key) >= 0)
                    continue;
                string sharedName = fields.isRing ? RingConfig.SharedFieldName(entry.Key) : entry.Key;
                FieldInfo shared = sharedName == null ? null : typeof(GalaxyConfig).GetField(sharedName);
                if (entry.Key == "renderMode")
                    shared = typeof(GalaxyConfig).GetField("renderMode");
                FieldInfo option = typeof(GalaxySettings).GetField(entry.Key == "particleBrightness" ? "starBrightness" : entry.Key);
                object left = shared != null ? shared.GetValue(fields) : option.GetValue(fields.volume);
                object right = shared != null ? shared.GetValue(text) : option.GetValue(text.volume);
                if (!Equals(left, right))
                    differences.Add(entry.Key + ": fields = " + Display(left) + "; config = " + Display(right));
            }
            
            return differences;
        }
        /// <summary>
        /// Get the string to use for displaying a config value.
        /// </summary>
        private static string Display(object val)
            => val is float v ? v.ToString("R", CultureInfo.InvariantCulture) : VolumeConfig.Format(val);
    }
}
