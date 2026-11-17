using System;
using UnityEngine;

namespace GalacticDisk
{
    internal static class DiskOpacity
    {
        internal static readonly string[] Fields = {
            "insideOpacityMultiplier", "atmosphereOpacityMultiplier", "atmosphereNightOpacityMultiplier"
        };

        internal static void Validate(GalaxyConfig config)
        {
            foreach (string name in Fields)
            {
                float value = (float)typeof(GalaxyConfig).GetField(name).GetValue(config);
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                    throw new ArgumentException(name + " must be finite and nonnegative.");
            }
        }

        internal static float Evaluate(GalaxyConfig config, Camera camera, Vector3 cameraInGalaxy)
        {
            float inside = config.insideOpacityMultiplier == 1f ? 1f :
                Mathf.Lerp(1f, config.insideOpacityMultiplier, InsideWeight(config, cameraInGalaxy));
            if (config.atmosphereOpacityMultiplier == 1f && config.atmosphereNightOpacityMultiplier == 1f) return inside;
            Vector3d observer = ScaledSpace.ScaledToLocalSpace(camera.transform.position);
            // A local flight camera retains surface precision even when the scaled
            // camera and a distant planet have rounded to the same float position.
            if (HighLogic.LoadedScene == GameScenes.FLIGHT && !MapView.MapIsEnabled &&
                FlightCamera.fetch != null && FlightCamera.fetch.mainCamera != null)
                observer = FlightCamera.fetch.mainCamera.transform.position;
            return inside * AtmosphereMultiplier(config, observer);
        }

        internal static float InsideWeight(GalaxyConfig c, Vector3 p)
        {
            // Work in galaxy units, with independent radial and vertical boundaries.
            // The transition straddles the rim and thickness; a ring hole is outside.
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

        internal static float AtmosphereMultiplier(GalaxyConfig config, Vector3d observer)
        {
            if (FlightGlobals.Bodies == null) return 1f;
            CelestialBody atmosphere = null;
            float weight = 0f;
            foreach (CelestialBody body in FlightGlobals.Bodies)
            {
                if (body == null || !body.atmosphere || body.atmosphereDepth <= 0) continue;
                double altitude = (observer - body.position).magnitude - body.Radius;
                if (altitude >= body.atmosphereDepth) continue;
                float candidate = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((float)(altitude / body.atmosphereDepth)));
                if (candidate > weight) { atmosphere = body; weight = candidate; }
            }
            if (atmosphere == null) return 1f;
            CelestialBody star = null;
            double nearest = double.MaxValue;
            foreach (CelestialBody body in FlightGlobals.Bodies)
            {
                if (body == null || !body.isStar || body == atmosphere) continue;
                double distance = (body.position - atmosphere.position).sqrMagnitude;
                if (distance < nearest) { star = body; nearest = distance; }
            }
            if (star == null && Sun.Instance != null) star = Sun.Instance.sun;
            // No known light source: retain daytime behavior rather than guessing night.
            float daylight = 1f;
            if (star != null)
            {
                Vector3d up = observer - atmosphere.position, light = star.position - observer;
                double denominator = up.magnitude * light.magnitude;
                if (denominator > 0)
                    daylight = Smooth(-0.1045285f, 0.1045285f, (float)(Vector3d.Dot(up, light) / denominator));
            }
            float atmospheric = Mathf.Lerp(config.atmosphereNightOpacityMultiplier, config.atmosphereOpacityMultiplier, daylight);
            return Mathf.Lerp(1f, atmospheric, weight);
        }

        private static float Ellipsoid(Vector3 p, Vector3 axes)
        {
            if (axes.x <= 0f || axes.y <= 0f || axes.z <= 0f) return 0f;
            return 1f - Smooth(0.8f, 1.2f, new Vector3(p.x / axes.x, p.y / axes.y, p.z / axes.z).magnitude);
        }
        private static float Smooth(float low, float high, float value)
        {
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((value - low) / (high - low)));
        }
    }
}
