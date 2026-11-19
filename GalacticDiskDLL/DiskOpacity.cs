using System;
using UnityEngine;

namespace GalacticDisk
{
    /// <summary> Helper class for disk opacity </summary>
    internal static class DiskOpacity
    {
        /// <summary> Opacity multiplier field names to verify in a <see cref="GalaxyConfig"/> </summary>
        internal static readonly string[] Fields = {
            "insideOpacityMultiplier", "atmosphereOpacityMultiplier", "atmosphereNightOpacityMultiplier"
        };

        /// <summary> Validate the opacity config values in a <see cref="GalaxyConfig"/> </summary>
        internal static void Validate(GalaxyConfig config)
        {
            foreach (string name in Fields)
            {
                float value = (float)typeof(GalaxyConfig).GetField(name).GetValue(config);
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                    throw new ArgumentException(name + " must be finite and nonnegative.");
            }
        }

        /// <summary> Return the opacity multiplier to use for a disk </summary>
        internal static float Evaluate(GalaxyConfig config, Camera camera, Vector3 cameraInGalaxy)
        {
            // Calculate the current inside multiplier and use 1.0 if the value is 1.0
            float inside = config.insideOpacityMultiplier == 1f ? 1f :
                Mathf.Lerp(1f, config.insideOpacityMultiplier, InsideWeight(config, cameraInGalaxy));

            // If no atmosphere multipliers then return the inside disk multiplier
            if (config.atmosphereOpacityMultiplier == 1f && config.atmosphereNightOpacityMultiplier == 1f) return inside;

            // Get the position of the observer in local space
            Vector3d observer = ScaledSpace.ScaledToLocalSpace(camera.transform.position);

            // Get the observer camera if in flight view and not in map view
            if (HighLogic.LoadedScene == GameScenes.FLIGHT && !MapView.MapIsEnabled &&
                FlightCamera.fetch != null && FlightCamera.fetch.mainCamera != null)
                observer = FlightCamera.fetch.mainCamera.transform.position;

            // Return the combined inside and atmosphere multipliers
            return inside * AtmosphereMultiplier(config, observer);
        }

        /// <summary> Get the weight of the inside disk opacity multiplier based on position inside the disk </summary>
        internal static float InsideWeight(GalaxyConfig c, Vector3 p)
        {
            float radius = Mathf.Max(0.0001f, c.galaxyRadius);
            float r = new Vector2(p.x, p.z).magnitude;
            float radialWidth = c.isRing ? Mathf.Min(radius * 0.1f, (radius - c.innerRingRadius) * 0.25f) : radius * 0.1f;
            radialWidth = Mathf.Max(radius * 0.000001f, radialWidth);
            float radial = 1f - Smooth(radius - radialWidth, radius + radialWidth, r);
            if (c.isRing && c.innerRingRadius > 0f)
                radial *= Smooth(c.innerRingRadius - Mathf.Min(radialWidth, c.innerRingRadius * 0.5f),
                    c.innerRingRadius + radialWidth, r);
            float halfHeight = Mathf.Max(c.diskThickness * 0.5f, radius * 0.00015f);
            float weight = radial * (1f - Smooth(halfHeight * 0.8f, halfHeight * 1.2f, Mathf.Abs(p.y)));
            if (!c.isRing && c.bulgeFraction > 0f)
            {
                bool bar = c.renderMode == DiskRenderMode.Volumetric && c.volume.enableBar;
                if (!bar || c.volume.barFraction < 1f)
                    weight = Mathf.Max(weight, Ellipsoid(p, new Vector3(c.bulgeScaleX, c.bulgeScaleY, c.bulgeScaleZ) * 3f));
                if (bar && c.volume.barFraction > 0f)
                {
                    float angle = c.volume.barAngle * Mathf.Deg2Rad;
                    Vector3 q = new Vector3(Mathf.Cos(angle) * p.x + Mathf.Sin(angle) * p.z, p.y,
                        -Mathf.Sin(angle) * p.x + Mathf.Cos(angle) * p.z);
                    weight = Mathf.Max(weight, Ellipsoid(q, new Vector3(c.volume.barLength * 0.25f,
                        c.volume.barHeight * 0.35f, c.volume.barWidth * 0.35f) * 3f));
                }
            }
            return weight;
        }

        /// <summary> Apply the atmosphere opacity multipler based on the <see cref="Vector3d"/> position of the observer </summary>
        internal static float AtmosphereMultiplier(GalaxyConfig config, Vector3d observer)
        {
            // If no bodies are loaded (somehow), don't change anything
            if (FlightGlobals.Bodies == null) return 1f;

            // Get the atmospheric body the observer is on (if it is one)
            CelestialBody atmosphere = null;
            float weight = 0f;
            foreach (CelestialBody body in FlightGlobals.Bodies)
            {
                // Make sure the body isn't null, has atmosphere, and has a valid atmosphere depth (just in case)
                if (body == null || !body.atmosphere || body.atmosphereDepth <= 0) continue;
                
                // Get the observer's position
                double altitude = (observer - body.position).magnitude - body.Radius;

                // Skip if above the body's atmosphere
                if (altitude >= body.atmosphereDepth) continue;

                // Get the body if closer to it than the currently chosen body
                float candidate = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((float)(altitude / body.atmosphereDepth)));
                if (candidate > weight)
                {
                    atmosphere = body;
                    weight = candidate;
                }
            }

            // If not at a planet with atmosphere, don't apply anything
            if (atmosphere == null) return 1f;

            // Get the star (if there is one... which there should be)
            CelestialBody star = null;
            double nearest = double.MaxValue;
            foreach (CelestialBody body in FlightGlobals.Bodies)
            {
                // Make sure the body isn't null, is a star, and isn't the same as the observer's body
                if (body == null || !body.isStar || body == atmosphere) continue;

                // Calculate the distance between the star and the observer's body
                double distance = (body.position - atmosphere.position).sqrMagnitude;

                // Set as the star to use if nearer than the current chosen star
                if (distance < nearest)
                {
                    star = body;
                    nearest = distance;
                }
            }

            // If no star found but the sun instance exists, use that instead
            if (star == null && Sun.Instance != null)
                star = Sun.Instance.sun;

            // Default to daylight if no star is found
            float daylight = 1f;
            if (star != null)
            {
                // Calculate how bright it is based on the star's and observer's position
                Vector3d up = observer - atmosphere.position, light = star.position - observer;
                double denominator = up.magnitude * light.magnitude;
                if (denominator > 0)
                    daylight = Smooth(-0.1045285f, 0.1045285f, (float)(Vector3d.Dot(up, light) / denominator));
            }

            // Apply the opacity based on multipliers and how bright it is
            return Mathf.Lerp(1f, Mathf.Lerp(config.atmosphereNightOpacityMultiplier, config.atmosphereOpacityMultiplier, daylight), weight);
        }

        /// <summary> Get the ellipsoid based on position and axes </summary>
        private static float Ellipsoid(Vector3 p, Vector3 axes)
        {
            if (axes.x <= 0f || axes.y <= 0f || axes.z <= 0f) return 0f;
            return 1f - Smooth(0.8f, 1.2f, new Vector3(p.x / axes.x, p.y / axes.y, p.z / axes.z).magnitude);
        }
        
        /// <summary> Smooth out a value </summary>
        private static float Smooth(float low, float high, float val)
            => SmoothStep(0f, 1f, Mathf.Clamp01((val - low) / (high - low)));
    }
}
