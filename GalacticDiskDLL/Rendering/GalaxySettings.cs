using System;
using System.Globalization;
using UnityEngine;

namespace GalacticDisk.Rendering
{
    [Serializable]
    public sealed class GalaxySettings
    {
        // Internal shape selection comes from the definition node, not an extra config key.
        public bool isRing;
        public float innerRingRadius;
        [Header("Stars and disk (Unity units)")]
        [Min(0)] public int starCount = 140000;
        [Min(0.01f)] public float galaxyRadius = 100f;
        [Tooltip("Vertical disk spread; stellar Y standard deviation is diskThickness * 0.22.")]
        [Min(0f)] public float diskThickness = 5.5f;
        [Min(0.001f)] public float minStarSize = 0.055f;
        [Min(0.001f)] public float maxStarSize = 0.19f;

        [Header("Spiral arms")]
        [Tooltip("Zero makes all non-bulge stars interarm stars.")]
        [Range(0, 12)] public int armCount = 4;
        [Tooltip("Winding multiplier for logarithmic arms; full turns from center to rim in legacy linear mode. Negative reverses winding.")]
        public float armTightness = 0.8f;
        [Tooltip("Maximum angular spread in radians, as in GalacticDisk.")]
        [Range(0.01f, 2f)] public float armWidth = 0.48f;
        [Tooltip("Fraction of DISK stars between arms, excluding the bulge.")]
        [Range(0f, 1f)] public float interarmFraction = 0.3f;
        [Tooltip("Higher values make the arms more concentrated within armWidth.")]
        [Range(0f, 1f)] public float armConcentration = 0.3f;

        [Header("Milky Way spiral structure")]
        [Tooltip("Logarithmic arms start near the bar ends. Off restores the original linear-radius winding law.")]
        public bool logarithmicArms = true;
        [Range(5f, 35f)] public float armPitchAngle = 13.5f;
        [Tooltip("Inner arm radius as a fraction of galaxyRadius.")]
        [Range(0.05f, 0.5f)] public float armStartRadius = 0.24f;
        [Tooltip("Strength of alternate arms: 1 makes equal arms; lower values give two dominant stellar arms out of four gas arms.")]
        [Range(0f, 1f)] public float secondaryArmStrength = 0.45f;
        [Range(0f, 0.5f)] public float armIrregularity = 0.065f;

        [Header("Central bulge")]
        [Range(0f, 1f)] public float bulgeFraction = 0.16f;
        [Tooltip("Gaussian standard deviation along X. Samples are bounded at 3.5 sigma.")]
        [Min(0f)] public float bulgeScaleX = 7.5f;
        [Min(0f)] public float bulgeScaleY = 3.5f;
        [Min(0f)] public float bulgeScaleZ = 7.5f;

        [Header("Central stellar bar")]
        public bool enableBar = true;
        [Tooltip("Fraction of the bulge population assigned to the bar; total starCount is unchanged.")]
        [Range(0f, 1f)] public float barFraction = 0.65f;
        [Tooltip("Approximate full bar length in Unity units.")]
        [Min(0.01f)] public float barLength = 54f;
        [Min(0.01f)] public float barWidth = 9f;
        [Min(0.01f)] public float barHeight = 4f;
        public float barAngle = 25f;

        [Header("Population colors (RGBA, alpha = opacity)")]
        public Color colorBulge = new Color(1f, 0.76f, 0.4f, 1f);
        public Color colorArms = new Color(0.62f, 0.76f, 1f, 0.8f);
        public Color colorInterarm = new Color(0.88f, 0.82f, 0.68f, 0.65f);
        public Color armHighlightColor = new Color(1f, 0.38f, 0.53f, 0.85f);
        [Tooltip("Chance per ARM star, excluding bulge and interarm stars.")]
        [Range(0f, 1f)] public float armHighlightChance = 0.035f;

        [Header("Dust clouds and obscuring lanes")]
        public bool enableDustClouds = true;
        [Range(0, 2000)] public int dustCloudCount = 450;
        [Min(0.05f)] public float minDustCloudSize = 1.5f;
        [Min(0.05f)] public float maxDustCloudSize = 8f;
        [Tooltip("Cloud vertical radius divided by its horizontal radius.")]
        [Range(0.05f, 1f)] public float dustCloudThickness = 0.4f;
        [Range(0f, 1f)] public float dustCloudClumpiness = 0.85f;
        [Tooltip("Fraction of clouds following spiral arms rather than the general disk.")]
        [Range(0f, 1f)] public float dustArmBias = 0.82f;
        [Range(0f, 4f)] public float dustLaneStrength = 1.6f;
        [Range(0.1f, 2f)] public float dustLaneWidth = 0.42f;
        public Color dustColor = new Color(0.23f, 0.10f, 0.045f, 0.95f);
        [Tooltip("Small reflected-light contribution. Absorption still makes dust predominantly dark.")]
        [Range(0f, 1f)] public float dustScattering = 0.08f;

        [Header("Star-forming nebulae and young clusters")]
        public bool enableStarFormation = true;
        [Range(0, 1000)] public int starFormingRegionCount = 180;
        [Min(0.05f)] public float minStarFormingRegionSize = 0.8f;
        [Min(0.05f)] public float maxStarFormingRegionSize = 2.3f;
        [Range(0f, 1f)] public float starFormationInnerRadius = 0.28f;
        [Range(0f, 1f)] public float starFormationOuterRadius = 0.9f;
        [Range(0f, 1f)] public float starFormationArmBias = 0.97f;
        [Range(0f, 1f)] public float starFormingRegionClumpiness = 0.9f;
        public Color starFormingRegionColor = new Color(1f, 0.035f, 0.14f, 0.95f);
        [Range(0f, 30f)] public float starFormingRegionIntensity = 1.8f;
        public Color youngStarColor = new Color(0.2f, 0.48f, 1f, 0.95f);
        [Range(0f, 15f)] public float youngStarGlowIntensity = 1.5f;
        [Tooltip("Fraction of arm stars clustered near nebulae. These are part of starCount, not extra stars.")]
        [Range(0f, 1f)] public float youngStarFraction = 0.12f;

        [Header("Optional appearance and quality")]
        public int seed = 985123;
        [Range(0.05f, 1f)] public float diskScaleLengthFraction = 0.32f;
        public bool enableVolume = true;
        [Range(0f, 50f)] public float volumeIntensity = 16f;
        [Range(0f, 80f)] public float dustOpacity = 58f;
        [Range(32, 256)] public int volumeSteps = 192;
        [Range(2f, 40f)] public float noiseFrequency = 24f;
        [Range(0f, 8f)] public float starBrightness = 1.2f;
        [Tooltip("XZ resolution of the generated 3D density texture. Y is sized automatically.")]
        [Range(64, 256)] public int cloudResolution = 192;
        [Tooltip("Dust samples per star vertex; zero disables individual star attenuation.")]
        [Range(0, 24)] public int starDustSamples = 12;

        public float ArmAngle(float radius)
        {
            float r = radius / galaxyRadius;
            float angle = logarithmicArms ?
                Mathf.Log(Mathf.Max(r, armStartRadius) / armStartRadius) /
                    Mathf.Tan(armPitchAngle * Mathf.Deg2Rad) * armTightness :
                r * armTightness * Mathf.PI * 2f;
            return angle + (enableBar ? barAngle * Mathf.Deg2Rad : 0f) +
                armIrregularity * (Mathf.Sin(r * 19f) + 0.5f * Mathf.Sin(r * 47f));
        }

        [Range(0.1f, 4f)] public float volumeExposure = 1.15f;

        public void Validate()
        {
            starCount = Mathf.Clamp(starCount, 0, 2000000);
            galaxyRadius = Mathf.Max(0.01f, galaxyRadius);
            if (isRing && (float.IsNaN(innerRingRadius) || float.IsInfinity(innerRingRadius) ||
                innerRingRadius < 0f || innerRingRadius >= galaxyRadius))
                throw new ArgumentException("innerRingRadius must be finite, nonnegative and smaller than ringRadius.");
            diskThickness = Mathf.Max(0f, diskThickness);
            minStarSize = Mathf.Max(0.001f, minStarSize);
            maxStarSize = Mathf.Max(minStarSize, maxStarSize);
            armCount = Mathf.Clamp(armCount, 0, 12);
            armWidth = Mathf.Clamp(armWidth, 0.01f, 2f);
            interarmFraction = Mathf.Clamp01(interarmFraction);
            armConcentration = Mathf.Clamp01(armConcentration);
            armPitchAngle = Mathf.Clamp(armPitchAngle, 5f, 35f);
            armStartRadius = Mathf.Clamp(armStartRadius, 0.05f, 0.5f);
            secondaryArmStrength = Mathf.Clamp01(secondaryArmStrength);
            armIrregularity = Mathf.Clamp(armIrregularity, 0f, 0.5f);
            bulgeFraction = Mathf.Clamp01(bulgeFraction);
            bulgeScaleX = Mathf.Max(0f, bulgeScaleX);
            bulgeScaleY = Mathf.Max(0f, bulgeScaleY);
            bulgeScaleZ = Mathf.Max(0f, bulgeScaleZ);
            barFraction = Mathf.Clamp01(barFraction);
            barLength = Mathf.Max(0.01f, barLength);
            barWidth = Mathf.Max(0.01f, barWidth);
            barHeight = Mathf.Max(0.01f, barHeight);
            armHighlightChance = Mathf.Clamp01(armHighlightChance);
            colorBulge = ClampColor(colorBulge);
            colorArms = ClampColor(colorArms);
            colorInterarm = ClampColor(colorInterarm);
            armHighlightColor = ClampColor(armHighlightColor);
            dustCloudCount = Mathf.Clamp(dustCloudCount, 0, 2000);
            minDustCloudSize = Mathf.Max(0.05f, minDustCloudSize);
            maxDustCloudSize = Mathf.Max(minDustCloudSize, maxDustCloudSize);
            dustCloudThickness = Mathf.Clamp(dustCloudThickness, 0.05f, 1f);
            dustCloudClumpiness = Mathf.Clamp01(dustCloudClumpiness);
            dustArmBias = Mathf.Clamp01(dustArmBias);
            dustLaneStrength = Mathf.Clamp(dustLaneStrength, 0f, 4f);
            dustLaneWidth = Mathf.Clamp(dustLaneWidth, 0.1f, 2f);
            dustColor = ClampColor(dustColor);
            dustScattering = Mathf.Clamp01(dustScattering);
            starFormingRegionCount = Mathf.Clamp(starFormingRegionCount, 0, 1000);
            minStarFormingRegionSize = Mathf.Max(0.05f, minStarFormingRegionSize);
            maxStarFormingRegionSize = Mathf.Max(minStarFormingRegionSize, maxStarFormingRegionSize);
            starFormationInnerRadius = Mathf.Clamp01(starFormationInnerRadius);
            starFormationOuterRadius = Mathf.Clamp(starFormationOuterRadius, starFormationInnerRadius, 1f);
            starFormationArmBias = Mathf.Clamp01(starFormationArmBias);
            starFormingRegionClumpiness = Mathf.Clamp01(starFormingRegionClumpiness);
            starFormingRegionColor = ClampColor(starFormingRegionColor);
            youngStarColor = ClampColor(youngStarColor);
            starFormingRegionIntensity = Mathf.Clamp(starFormingRegionIntensity, 0f, 30f);
            youngStarGlowIntensity = Mathf.Clamp(youngStarGlowIntensity, 0f, 15f);
            youngStarFraction = Mathf.Clamp01(youngStarFraction);
            diskScaleLengthFraction = Mathf.Clamp(diskScaleLengthFraction, 0.05f, 1f);
            volumeSteps = Mathf.Clamp(volumeSteps, 32, 256);
            noiseFrequency = Mathf.Clamp(noiseFrequency, 2f, 40f);
            volumeIntensity = Mathf.Clamp(volumeIntensity, 0f, 50f);
            dustOpacity = Mathf.Clamp(dustOpacity, 0f, 80f);
            starBrightness = Mathf.Clamp(starBrightness, 0f, 8f);
            cloudResolution = Mathf.Clamp(cloudResolution, 64, 256);
            starDustSamples = Mathf.Clamp(starDustSamples, 0, 24);
            volumeExposure = Mathf.Clamp(volumeExposure, 0.1f, 4f);
        }

        private static Color ClampColor(Color c)
        {
            return new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g),
                Mathf.Clamp01(c.b), Mathf.Clamp01(c.a));
        }

        /// <summary>Optional helper for existing GalacticDisk RGB or RGBA config values.</summary>
        public static bool TryParseColor(string text, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string[] parts = text.Split(',');
            if (parts.Length != 3 && parts.Length != 4) return false;
            float[] values = { 0f, 0f, 0f, 1f };
            for (int i = 0; i < parts.Length; i++)
            {
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out values[i]) ||
                    float.IsNaN(values[i]) || float.IsInfinity(values[i])) return false;
            }
            color = new Color(values[0], values[1], values[2], values[3]);
            return true;
        }
    }
}
