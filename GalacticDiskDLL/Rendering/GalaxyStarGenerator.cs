using UnityEngine;
using System;

namespace GalacticDisk.Rendering
{
    /// <summary>
    /// A star struct used as part of a particle disk/ring.
    /// </summary>
    public struct GalaxyStar
    {
        public Vector3 position;
        public Color32 color;
        public float size;
    }

    /// <summary>
    /// Used for enerating a particle galaxy disk/ring and has various helpers for that too.
    /// </summary>
    public static class GalaxyStarGenerator
    {
        /// <summary>
        /// Generates a particle galaxy disk/ring.
        /// <returns>Array of <see cref="GalaxyStar"/> stars that make up the disk/ring.</returns>
        /// </summary>
        public static GalaxyStar[] Generate(GalaxySettings settings, GalaxyCloudField cloudField)
        {
            // Get a random generator that uses the specified seed
            Random random = new Random(settings.seed);
            // The array of stars to return when done
            GalaxyStar particles = new GalaxyStar[settings.starCount];
            float scaleLength = settings.galaxyRadius * settings.diskScaleLengthFraction;
            float truncatedCdf = 1f - Mathf.Exp(-settings.galaxyRadius / scaleLength);

            // Generate the star particles
            for (int i = 0; i < particles.Length; i++)
            {
                // Is the particle part of the bulge
                bool bulge = Next(random) < settings.bulgeFraction;
                // Is the particle part of an arm
                bool arm = false;
                // Position of the particle to use when done
                Vector3 position;
                
                if (bulge)
                {  // Bulge particle
                    if (settings.enableBar && Next(random) < settings.barFraction)
                    {  // Bar bulge particle
                        float x = BoundedGaussian(random) * settings.barLength * 0.25f;
                        float z = BoundedGaussian(random) * settings.barWidth * 0.35f;
                        float angle = settings.barAngle * Mathf.Deg2Rad;
                        position = new Vector3(
                            Mathf.Cos(angle) * x - Mathf.Sin(angle) * z,
                            BoundedGaussian(random) * settings.barHeight * 0.35f,
                            Mathf.Sin(angle) * x + Mathf.Cos(angle) * z);
                    }
                    else  // Normal bulge particle
                        position = new Vector3(
                            BoundedGaussian(random) * settings.bulgeScaleX,
                            BoundedGaussian(random) * settings.bulgeScaleY,
                            BoundedGaussian(random) * settings.bulgeScaleZ);
                }
                else
                {  // Normal star particle
                    // Inverse CDF of a truncated exponential
                    float r = settings.isRing ? settings.innerRingRadius - scaleLength * Mathf.Log(1f - Next(random) *
                        (1f - Mathf.Exp(-(settings.galaxyRadius - settings.innerRingRadius) / scaleLength))) :
                        -scaleLength * Mathf.Log(1f - Next(random) * truncatedCdf);
                    // Make particle part of an arm if there are any and its not interarm
                    arm = settings.armCount > 0 && Next(random) >= settings.interarmFraction;
                    float angle;
                    
                    if (arm)
                    {
                        int armIndex = ChooseArm(random, settings);
                        angle = armIndex * Mathf.PI * 2f / settings.armCount + settings.ArmAngle(r);
                        float sigma = settings.armWidth * Mathf.Lerp(0.6f, 0.05f, settings.armConcentration);
                        angle += Mathf.Clamp(Gaussian(random) * sigma, -settings.armWidth, settings.armWidth);
                    }
                    else
                        angle = Next(random) * Mathf.PI * 2f;

                    // Use the inner radius if generating a ring
                    if (settings.isRing)
                        r = Mathf.Clamp(r, settings.innerRingRadius, settings.galaxyRadius);
                    else
                        r = Mathf.Clamp(r + (Next(random) - 0.5f) * settings.galaxyRadius * 0.012f, 0f, settings.galaxyRadius);
                    
                    position = new Vector3(
                        Mathf.Cos(angle) * r,
                        BoundedGaussian(random) * settings.diskThickness * 0.22f,
                        Mathf.Sin(angle) * r);
                }

                Color color = bulge ? settings.colorBulge : (arm ? settings.colorArms : settings.colorInterarm);

                // Apply highlighting and nebulae if part of an arm
                if (arm)
                {
                    // Handle star forming regions (nebulae)
                    if (cloudField != null && cloudField.regions.Length > 0 && Next(random) < settings.youngStarFraction)
                    {
                        // Some position adjustments to better show the nebula
                        Vector4 region = cloudField.regions[random.Next(cloudField.regions.Length)];
                        position = new Vector3(
                            region.x + BoundedGaussian(random) * region.w * 0.28f,
                            region.y + BoundedGaussian(random) * region.w * 0.12f,
                            region.z + BoundedGaussian(random) * region.w * 0.28f);
                        // Use the young star color instead of the normal color
                        color = settings.youngStarColor;
                    }
                    // Apply random highlighting
                    if (Next(random) < settings.armHighlightChance)
                        color = settings.armHighlightColor;
                }

                // Slighly randomize galaxy disk star color (might want this for rings too...)
                if (!settings.isRing)
                {
                    color.r = Mathf.Clamp01(color.r + (Next(random) - 0.5f) * 0.06f);
                    color.g = Mathf.Clamp01(color.g + (Next(random) - 0.5f) * 0.06f);
                    color.b = Mathf.Clamp01(color.b + (Next(random) - 0.5f) * 0.06f);
                    // Ignore alpha, shouldn't be randomized
                }
                
                // Get star size
                float size = Mathf.Clamp(
                                 Mathf.Lerp(
                                     settings.maxStarSize, settings.minStarSize,
                                     new Vector2(position.x, position.z).magnitude / settings.galaxyRadius
                                 ) * Mathf.Lerp(0.7f, 1.3f, Next(random)),  // Apply some variation
                                 settings.minStarSize, settings.maxStarSize  // Ensure star is within limits
                             )

                // Create a new star particle with the finished parameters
                particles[i] = new GalaxyStar
                {
                    position = position,
                    color = color,
                    size = size
                };
            }
            return particles;
        }

        /// <summary>
        /// Choose the arm to put the star on.
        /// <returns>Chosen arm's index.</returns>
        /// </summary>
        private static int ChooseArm(Random random, GalaxySettings settings)
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

        /// <summary>
        /// Get a random float.
        /// </summary>
        private static float Next(Random random)
            => (float)random.NextDouble();
        /// <summary>
        /// Get a random Gaussian distribution.
        /// </summary>
        private static float Gaussian(Random random)
            => Mathf.Sqrt(-2f * Mathf.Log(Mathf.Max(0.000001f, Next(random)))) *
                Mathf.Cos(2f * Mathf.PI * Next(random));
        /// <summary>
        /// Get a random Gaussian distribution within [-3.5,3.5].
        /// </summary>
        private static float BoundedGaussian(Random random)
            => Mathf.Clamp(Gaussian(random), -3.5f, 3.5f);
    }
}
