using System.Globalization;
using UnityEngine;
using System.IO;

namespace GalacticDisk
{
    public class GalaxyConfig
    {
        public string planet;
        public DiskRenderMode renderMode = DiskRenderMode.Particles;
        public GalaxySettings volume = new GalaxySettings();
        public bool isRing;
        public float innerRingRadius;

        public int starCount = 50000;
        public float galaxyRadius = 100f;
        public float diskThickness = 2f;
        public float minStarSize = 0.015f;
        public float maxStarSize = 0.09f;

        public int armCount = 4;
        public float armTightness = 4f;
        public float armWidth = 0.25f;

        public float interarmFraction = 0.25f;
        public float armConcentration = 0.35f;

        public float bulgeFraction = 0.15f;

        // Bulge is a smooth ellipsoid with an independently-scaled extent on each
        // axis. Replaces the old single bulgeRadius (a sphere with X=Z, and Y
        // implicitly at 0.15x). Defaults reproduce roughly that old proportion.
        public float bulgeScaleX = 15f;
        public float bulgeScaleY = 2.25f;
        public float bulgeScaleZ = 15f;

        // Separate colors for each population, replacing the old color1 (inner)/
        // color2 (outer) radial blend. Defaults mirror the old defaults: bulge and
        // arms took after color1 (warm), interarm took after color2 (cool/blue).
        public Color colorBulge = new Color(1.0f, 0.92f, 0.65f, 1.0f);
        public Color colorArms = new Color(1.0f, 0.92f, 0.65f, 1.0f);
        public Color colorInterarm = new Color(0.45f, 0.65f, 1.0f, 1.0f);

        // Optional highlight color sprinkled onto arm stars only (not bulge, not
        // interarm) to simulate HII regions/star clusters. Chance defaults to 0
        // so existing configs render identically unless they opt in.
        public Color armHighlightColor = new Color(1.0f, 0.55f, 0.6f, 1.0f);
        public float armHighlightChance = 0f;

        // Fixed WORLD rotation (Euler degrees) the disk is held at every frame,
        // overriding whatever rotation it would otherwise inherit from its parent
        // scaled-space body. Defaults to 0,0,0, which matches the disk's previous
        // (unintentional) behavior of just sitting at identity rotation — so an
        // existing config with a non-spinning parent body sees no change.
        public float rotationX = 0f;
        public float rotationY = 0f;
        public float rotationZ = 0f;

        // When true, ignore rotationX/rotationZ and instead compute the disk's
        // tilt directly from the orbit of the body it's attached to (config.planet),
        // so the disk plane is guaranteed coplanar with that orbit regardless of
        // how inclination/LAN were set up. rotationY still applies as a free spin
        // around the resulting plane normal. Defaults to false — manual rotationX/
        // Y/Z behave exactly as before unless this is turned on.
        public bool alignRotationToOrbit = false;

        // Seconds for the disk to complete one full rotation about its own plane
        // normal (added on top of rotationY / alignRotationToOrbit). 0 (default)
        // disables this entirely — no extra spin, matching old behavior. Uses
        // Planetarium.GetUniversalTime() rather than accumulating per-frame deltas,
        // so it's exact at any timewarp rate and never drifts.
        public float rotationPeriod = 0f;
        // Optional spin about the tilted disk normal. Manual rotationY remains a world-Y heading.
        public bool spinAroundDiskAxis = false;

        // Fraction of galaxyRadius used as the exponential falloff scale length
        // for star density (was hardcoded to 0.25 previously). Lower = fades out
        // faster near the center; higher = a more gradual, extended-looking disk.
        // Default matches the old hardcoded behavior exactly.
        public float diskScaleLengthFraction = 0.25f;

        public bool usesTexture = false;
        public string texturePath = "";

        // Visible size multiplier. The legacy emitter itself uses objectScale * renderScale;
        // volume shaders project in galaxy units and apply objectScale independently.
        // Both support scientific notation via GetFloat's NumberStyles.Float parsing.
        public float objectScale = 1f;

        // Additional scale applied only to the legacy emitter (not to the visible size of
        // the disk) so KSP sees a much larger object and doesn't cull/hide it from far
        // away. Particle positions/sizes are generated pre-divided by this value to
        // cancel it back out visually. Volumetric proxies handle culling independently.
        public float renderScale = 1f;

        public static GalaxyConfig FromConfigNode(ConfigNode node)
        {
            if (node.name == RingConfig.NodeName) return RingConfig.Read(node);
            GalaxyConfig c = new GalaxyConfig();

            c.planet = GetString(node, "planet", c.planet);
            VolumeConfig.Read(node, c);

            c.starCount = GetInt(node, "starCount", c.starCount);
            c.galaxyRadius = GetFloat(node, "galaxyRadius", c.galaxyRadius);
            c.diskThickness = GetFloat(node, "diskThickness", c.diskThickness);
            c.minStarSize = GetFloat(node, "minStarSize", c.minStarSize);
            c.maxStarSize = GetFloat(node, "maxStarSize", c.maxStarSize);

            c.armCount = GetInt(node, "armCount", c.armCount);
            c.armTightness = GetFloat(node, "armTightness", c.armTightness);
            c.armWidth = GetFloat(node, "armWidth", c.armWidth);

            c.interarmFraction = GetFloat(node, "interarmFraction", c.interarmFraction);
            c.armConcentration = GetFloat(node, "armConcentration", c.armConcentration);

            c.bulgeFraction = GetFloat(node, "bulgeFraction", c.bulgeFraction);

            if (node.HasValue("bulgeRadius"))
            {
                float legacyRadius = GetFloat(node, "bulgeRadius", c.bulgeScaleX);
                c.bulgeScaleX = legacyRadius;
                c.bulgeScaleY = legacyRadius;
                c.bulgeScaleZ = legacyRadius;
                Debug.LogWarning("[GalacticDisk] Config field 'bulgeRadius' is obsolete — use bulgeScaleX/bulgeScaleY/bulgeScaleZ instead. Setting all three to the given value for now.");
            }
            c.bulgeScaleX = GetFloat(node, "bulgeScaleX", c.bulgeScaleX);
            c.bulgeScaleY = GetFloat(node, "bulgeScaleY", c.bulgeScaleY);
            c.bulgeScaleZ = GetFloat(node, "bulgeScaleZ", c.bulgeScaleZ);

            if (node.HasValue("color1"))
            {
                Color legacyColor1 = GetColor(node, "color1", c.colorBulge);
                c.colorBulge = legacyColor1;
                c.colorArms = legacyColor1;
                Debug.LogWarning("[GalacticDisk] Config field 'color1' is obsolete — use colorBulge/colorArms instead. Using it as the default for both for now.");
            }
            if (node.HasValue("color2"))
            {
                c.colorInterarm = GetColor(node, "color2", c.colorInterarm);
                Debug.LogWarning("[GalacticDisk] Config field 'color2' is obsolete — use colorInterarm instead. Using it as the default for now.");
            }
            c.colorBulge = GetColor(node, "colorBulge", c.colorBulge);
            c.colorArms = GetColor(node, "colorArms", c.colorArms);
            c.colorInterarm = GetColor(node, "colorInterarm", c.colorInterarm);

            c.armHighlightColor = GetColor(node, "armHighlightColor", c.armHighlightColor);
            c.armHighlightChance = GetFloat(node, "armHighlightChance", c.armHighlightChance);

            c.rotationX = GetFloat(node, "rotationX", c.rotationX);
            c.rotationY = GetFloat(node, "rotationY", c.rotationY);
            c.rotationZ = GetFloat(node, "rotationZ", c.rotationZ);
            c.alignRotationToOrbit = GetBool(node, "alignRotationToOrbit", c.alignRotationToOrbit);
            c.rotationPeriod = GetFloat(node, "rotationPeriod", c.rotationPeriod);
            c.spinAroundDiskAxis = GetBool(node, "spinAroundDiskAxis", c.spinAroundDiskAxis);
            c.diskScaleLengthFraction = GetFloat(node, "diskScaleLengthFraction", c.diskScaleLengthFraction);

            c.usesTexture = GetBool(node, "usesTexture", c.usesTexture);
            string rawPath = GetString(node, "texturePath", "");
            if (!string.IsNullOrEmpty(rawPath))
                c.texturePath = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", rawPath);
            else
            {
                c.texturePath = "";  // Let GenerateGalaxy handle default case
                if (c.usesTexture)
                {
                    c.usesTexture = false;
                    Debug.LogWarning("[GalacticDisk] No texturePath but uses a custom texture, using default texture instead.");
                }
            }

            c.objectScale = GetFloat(node, "objectScale", c.objectScale);
            c.renderScale = GetFloat(node, "renderScale", c.renderScale);

            return c;
        }

        static string GetString(ConfigNode node, string key, string fallback)
        {
            return node.HasValue(key) ? node.GetValue(key) : fallback;
        }

        static int GetInt(ConfigNode node, string key, int fallback)
        {
            if (node.HasValue(key) && int.TryParse(node.GetValue(key), out int result))
                return result;
            return fallback;
        }

        static float GetFloat(ConfigNode node, string key, float fallback)
        {
            if (node.HasValue(key) &&
                float.TryParse(node.GetValue(key), NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
                return result;
            return fallback;
        }

        static Color GetColor(ConfigNode node, string key, Color fallback)
        {
            if (!node.HasValue(key))
            {
                Debug.LogWarning("[GalacticDisk] Key " + key + " in node " + node.name + " does not exist, using fallback.");
                return fallback;
            }

            string[] parts = node.GetValue(key).Split(',');
            if (parts.Length < 3)
                return fallback;

            try
            {
                float r = float.Parse(parts[0].Trim(), CultureInfo.InvariantCulture);
                float g = float.Parse(parts[1].Trim(), CultureInfo.InvariantCulture);
                float b = float.Parse(parts[2].Trim(), CultureInfo.InvariantCulture);
                float a = parts.Length >= 4 ? float.Parse(parts[3].Trim(), CultureInfo.InvariantCulture) : 1f;

                return new Color(r, g, b, a);
            }
            catch
            {
                return fallback;
            }
        }
        static bool GetBool(ConfigNode node, string key, bool fallback)
        {
            if (node.HasValue(key) && bool.TryParse(node.GetValue(key), out bool result))
                return result;
            return fallback;
        }
    }
}
