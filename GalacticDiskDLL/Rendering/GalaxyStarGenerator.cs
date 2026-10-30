using UnityEngine;

namespace GalacticDisk.Rendering
{
    public struct GalaxyStar
    {
        public Vector3 position;
        public Color32 color;
        public float size;
    }

    public static class GalaxyStarGenerator
    {
        public static GalaxyStar[] Generate(GalaxySettings settings, GalaxyCloudField cloudField)
        {
            // System.Random avoids disturbing other scripts' UnityEngine.Random state.
            var random = new System.Random(settings.seed);
            var particles = new GalaxyStar[settings.starCount];
            float radius = settings.galaxyRadius;
            float scaleLength = radius * settings.diskScaleLengthFraction;
            float truncatedCdf = 1f - Mathf.Exp(-radius / scaleLength);

            for (int i = 0; i < particles.Length; i++)
            {
                bool bulge = Next(random) < settings.bulgeFraction;
                bool arm = false;
                Vector3 position;
                if (bulge)
                {
                    if (settings.enableBar && Next(random) < settings.barFraction)
                    {
                        float x = BoundedGaussian(random) * settings.barLength * 0.25f;
                        float z = BoundedGaussian(random) * settings.barWidth * 0.35f;
                        float angle = settings.barAngle * Mathf.Deg2Rad;
                        position = new Vector3(Mathf.Cos(angle) * x - Mathf.Sin(angle) * z,
                            BoundedGaussian(random) * settings.barHeight * 0.35f,
                            Mathf.Sin(angle) * x + Mathf.Cos(angle) * z);
                    }
                    else position = new Vector3(BoundedGaussian(random) * settings.bulgeScaleX,
                            BoundedGaussian(random) * settings.bulgeScaleY,
                            BoundedGaussian(random) * settings.bulgeScaleZ);
                }
                else
                {
                    // Inverse CDF of a truncated exponential: no unbounded rejection loop.
                    float r = settings.isRing ? settings.innerRingRadius - scaleLength * Mathf.Log(1f - Next(random) *
                        (1f - Mathf.Exp(-(radius - settings.innerRingRadius) / scaleLength))) :
                        -scaleLength * Mathf.Log(1f - Next(random) * truncatedCdf);
                    arm = settings.armCount > 0 && Next(random) >= settings.interarmFraction;
                    float angle;
                    if (arm)
                    {
                        int armIndex = ChooseArm(random, settings);
                        angle = armIndex * Mathf.PI * 2f / settings.armCount + settings.ArmAngle(r);
                        float sigma = settings.armWidth * Mathf.Lerp(0.6f, 0.05f, settings.armConcentration);
                        angle += Mathf.Clamp(Gaussian(random) * sigma, -settings.armWidth, settings.armWidth);
                    }
                    else angle = Next(random) * Mathf.PI * 2f;
                    if (!settings.isRing) r = Mathf.Clamp(r + (Next(random) - 0.5f) * radius * 0.012f, 0f, radius);
                    else r = Mathf.Clamp(r, settings.innerRingRadius, radius);
                    position = new Vector3(Mathf.Cos(angle) * r,
                        BoundedGaussian(random) * settings.diskThickness * 0.22f,
                        Mathf.Sin(angle) * r);
                }

                Color color = bulge ? settings.colorBulge : (arm ? settings.colorArms : settings.colorInterarm);
                if (arm && cloudField != null && cloudField.regions.Length > 0 && Next(random) < settings.youngStarFraction)
                {
                    Vector4 region = cloudField.regions[random.Next(cloudField.regions.Length)];
                    position = new Vector3(region.x + BoundedGaussian(random) * region.w * 0.28f,
                        region.y + BoundedGaussian(random) * region.w * 0.12f,
                        region.z + BoundedGaussian(random) * region.w * 0.28f);
                    color = settings.youngStarColor;
                }
                if (arm && Next(random) < settings.armHighlightChance) color = settings.armHighlightColor;
                // Vary RGB only; preserve the chosen population's alpha exactly.
                if (!settings.isRing)
                {
                    color.r = Mathf.Clamp01(color.r + (Next(random) - 0.5f) * 0.06f);
                    color.g = Mathf.Clamp01(color.g + (Next(random) - 0.5f) * 0.06f);
                    color.b = Mathf.Clamp01(color.b + (Next(random) - 0.5f) * 0.06f);
                }
                float radialFraction = new Vector2(position.x, position.z).magnitude / radius;
                float size = Mathf.Lerp(settings.maxStarSize, settings.minStarSize, radialFraction);
                size *= Mathf.Lerp(0.7f, 1.3f, Next(random));

                particles[i] = new GalaxyStar
                {
                    position = position,
                    color = color,
                    size = Mathf.Clamp(size, settings.minStarSize, settings.maxStarSize)
                };
            }
            return particles;
        }

        private static int ChooseArm(System.Random random, GalaxySettings settings)
        {
            float total = 0f;
            for (int i = 0; i < settings.armCount; i++) total += (i & 1) == 0 ? 1f : settings.secondaryArmStrength;
            float pick = Next(random) * total;
            for (int i = 0; i < settings.armCount; i++)
            {
                pick -= (i & 1) == 0 ? 1f : settings.secondaryArmStrength;
                if (pick <= 0f) return i;
            }
            return 0;
        }

        private static float Next(System.Random random) { return (float)random.NextDouble(); }
        private static float Gaussian(System.Random random)
        {
            return Mathf.Sqrt(-2f * Mathf.Log(Mathf.Max(0.000001f, Next(random)))) *
                Mathf.Cos(2f * Mathf.PI * Next(random));
        }
        private static float BoundedGaussian(System.Random random)
        {
            return Mathf.Clamp(Gaussian(random), -3.5f, 3.5f);
        }
    }
}
