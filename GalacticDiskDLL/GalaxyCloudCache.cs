using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace GalacticDisk
{
    // Only immutable density data is shared. Each renderer keeps its own colors,
    // materials, stars, transforms and per-camera opacity.
    internal static class GalaxyCloudCache
    {
        private sealed class Entry
        {
            internal string key;
            internal GalaxyCloudField field;
            internal int users;
            internal long bytes;
            internal GalaxySettings settings;
            internal Vector3 extents;
            internal float retryAfter;
        }
        private static readonly Dictionary<string, Entry> byKey = new Dictionary<string, Entry>();
        private static readonly Dictionary<GalaxyCloudField, Entry> byField = new Dictionary<GalaxyCloudField, Entry>();
        // Keep this list in step with GeneratePixels, SampleArmPoint and ArmAngle.
        private static readonly string[] densityNames = {
            "isRing", "innerRingRadius", "galaxyRadius", "diskThickness", "cloudResolution", "seed", "noiseFrequency",
            "armCount", "armTightness", "armWidth", "armConcentration", "logarithmicArms", "armPitchAngle", "armStartRadius",
            "armIrregularity", "enableBar", "barAngle", "enableDustClouds", "dustCloudCount", "minDustCloudSize",
            "maxDustCloudSize", "dustCloudThickness", "dustCloudClumpiness", "dustArmBias", "dustLaneStrength", "dustLaneWidth",
            "enableStarFormation", "starFormingRegionCount", "minStarFormingRegionSize", "maxStarFormingRegionSize",
            "starFormationInnerRadius", "starFormationOuterRadius", "starFormationArmBias", "starFormingRegionClumpiness"
        };
        private static readonly FieldInfo[] densityFields = GetFields();
        internal static int BuildCount { get; private set; }
        internal static int HitCount { get; private set; }
        internal static int ActiveCount { get { return byKey.Count; } }
        internal static long TextureBytes { get; private set; }
        internal static int PendingCount { get; private set; }

        internal static bool IsAnalyticBulge(GalaxySettings s)
        {
            return !s.isRing && s.bulgeFraction == 1f && !s.enableDustClouds &&
                (!s.enableStarFormation || s.starFormingRegionCount == 0);
        }

        private static FieldInfo[] GetFields()
        {
            var fields = new FieldInfo[densityNames.Length];
            for (int i = 0; i < fields.Length; i++) fields[i] = typeof(GalaxySettings).GetField(densityNames[i]);
            return fields;
        }
        private static string Key(GalaxySettings s, Vector3 extents)
        {
            var text = new StringBuilder(512);
            text.Append(VolumeConfig.Format(extents.x)).Append('|').Append(VolumeConfig.Format(extents.y)).Append('|').Append(VolumeConfig.Format(extents.z));
            foreach (FieldInfo field in densityFields) text.Append('|').Append(VolumeConfig.Format(field.GetValue(s)));
            if (!s.isRing && s.rendererVersion == 2)
                text.Append("|v2|").Append(VolumeConfig.Format(s.armVariation)).Append('|').Append(VolumeConfig.Format(s.armBranchStrength));
            return text.ToString();
        }
        internal static GalaxyCloudField Acquire(GalaxySettings s, Vector3 extents, bool deferTexture = false)
        {
            bool analytic = IsAnalyticBulge(s);
            string key = analytic ? "analytic-bulge" : Key(s, extents);
            Entry entry;
            if (byKey.TryGetValue(key, out entry))
            {
                if (entry.field.texture != null || entry.field.pending)
                {
                    if (!deferTexture && !EnsureTexture(entry.field)) throw new System.InvalidOperationException("Cloud texture generation failed; see KSP.log.");
                    entry.users++; HitCount++; return entry.field;
                }
                // Handle an externally destroyed Unity texture without retaining a dead entry.
                byKey.Remove(key); byField.Remove(entry.field); TextureBytes -= entry.bytes;
            }
            GalaxyCloudField field = analytic ? AnalyticField() : deferTexture ?
                new GalaxyCloudField { regions = GalaxyCloudField.GenerateRegions(s), pending = true } : GalaxyCloudField.Build(s, extents);
            entry = new Entry { key = key, field = field, users = 1,
                settings = field.pending ? s.Snapshot() : null, extents = extents,
                bytes = field.pending ? 0 : (long)field.texture.width * field.texture.height * field.texture.depth * 8 };
            byKey.Add(key, entry); byField.Add(field, entry);
            if (field.pending) PendingCount++; else BuildCount++;
            TextureBytes += entry.bytes;
            return field;
        }
        internal static bool EnsureTexture(GalaxyCloudField field)
        {
            if (field.texture != null) return true;
            Entry entry;
            if (!byField.TryGetValue(field, out entry) || !field.pending || entry.retryAfter > Time.realtimeSinceStartup) return false;
            try
            {
                var built = GalaxyCloudField.Build(entry.settings, entry.extents);
                field.texture = built.texture; field.regions = built.regions; field.dustCloudCount = built.dustCloudCount;
                field.dustMass = built.dustMass; field.nebulaMass = built.nebulaMass; field.youngGlowMass = built.youngGlowMass;
                field.pending = false; entry.settings = null;
                entry.bytes = (long)field.texture.width * field.texture.height * field.texture.depth * 8;
                TextureBytes += entry.bytes; PendingCount--; BuildCount++;
                return true;
            }
            catch (System.Exception error)
            {
                entry.retryAfter = Time.realtimeSinceStartup + 5f;
                Debug.LogError("[GalacticDisk] Deferred cloud generation failed; retrying after five seconds: " + error);
                return false;
            }
        }
        private static GalaxyCloudField AnalyticField()
        {
            var texture = new Texture3D(1, 1, 1, TextureFormat.RGBAHalf, false)
            {
                name = "GalacticDisk analytic bulge placeholder",
                hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear
            };
            texture.SetPixels(new[] { Color.clear }); texture.Apply(false, true);
            return new GalaxyCloudField { texture = texture, regions = new Vector4[0] };
        }
        internal static void Release(GalaxyCloudField field)
        {
            if (field == null) return;
            Entry entry;
            if (!byField.TryGetValue(field, out entry) || --entry.users > 0) return;
            byField.Remove(field); byKey.Remove(entry.key); TextureBytes -= entry.bytes;
            if (field.pending) PendingCount--;
            if (field.texture != null)
            {
                if (Application.isPlaying) Object.Destroy(field.texture); else Object.DestroyImmediate(field.texture);
            }
        }
    }
}
