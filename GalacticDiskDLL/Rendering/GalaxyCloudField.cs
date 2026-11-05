using System;
using UnityEngine;

namespace GalacticDisk.Rendering
{
    /// <summary>
    /// 3D density field used by both the ray marcher and individual stars.
    /// </summary>
    public sealed class GalaxyCloudField
    {
        public Texture3D texture;
        public Vector4[] regions;
        public int dustCloudCount;
        public float dustMass;
        public float nebulaMass;
        public float youngGlowMass;

        public static GalaxyCloudField Build(GalaxySettings s, Vector3 extents)
        {
            int width = s.cloudResolution;
            int height = Mathf.Clamp(Mathf.RoundToInt(width * extents.y / Mathf.Max(extents.x, extents.z)), 32, 128);
            int depth = width;
            Color pixels = new Color[width * height * depth];
            GalaxyCloudField result = new GalaxyCloudField();
            System.Random random = new System.Random(unchecked(s.seed + 17041));
            float radius = s.galaxyRadius;
            float sigma = s.armWidth * Mathf.Lerp(0.6f, 0.05f, s.armConcentration);
            float dustHeight = Mathf.Max(radius * 0.0015f, s.diskThickness * 0.12f);

            // R: obscuring dust, G: HII emission, B: blue association glow, A: diffuse clump noise
            for (int z = 0; z < depth; z++)
                for (int x = 0; x < width; x++)
                {
                    float px = Axis(x, width, extents.x), pz = Axis(z, depth, extents.z);
                    float r = Mathf.Sqrt(px * px + pz * pz) / radius;
                    float radial = Mathf.Exp(-r / 0.5f) * (1f - Smooth(0.88f, 1f, r));
                    if (s.isRing)
                        radial = Mathf.Exp(-(r - s.innerRingRadius / radius) / 0.5f);
                    float phase = Mathf.Atan2(pz, px) - s.ArmAngle(r * radius);
                    float laneDistance = ArmDistance(phase - sigma * 1.1f, s.armCount);
                    float laneSigma = Mathf.Max(0.01f, sigma * s.dustLaneWidth);
                    float lane = Mathf.Exp(-0.5f * laneDistance * laneDistance / (laneSigma * laneSigma));
                    float innerFade = Smooth(0.1f, 0.22f, r);
                    if (s.armCount == 0)
                        lane = 0.2f;
                    if (s.isRing)
                    {
                        lane = 1f;
                        innerFade = 1f;
                    }
                    for (int y = 0; y < height; y++)
                    {
                        Vector3 p = new Vector3(px, Axis(y, height, extents.y), pz);
                        float n = FractalNoise(p / radius * s.noiseFrequency, s.seed);
                        float dustY = p.y / dustHeight;
                        float density = s.enableDustClouds ? radial * innerFade * lane *
                            Mathf.Exp(-0.5f * dustY * dustY) * s.dustLaneStrength *
                            Mathf.Lerp(1f, 0.18f + n * n * 3f, s.dustCloudClumpiness) : 0f;
                        pixels[x + width * (y + height * z)] = new Color(density, 0f, 0f, n);
                    }
                }

            if (s.enableDustClouds)
            {
                result.dustCloudCount = s.dustCloudCount;
                for (int cloud = 0; cloud < s.dustCloudCount; cloud++)
                {
                    Vector3 center = SampleArmPoint(s, random, 0.17f, 0.96f, s.dustArmBias, sigma * 1.1f);
                    float size = Mathf.Lerp(s.minDustCloudSize, s.maxDustCloudSize, Next(random));
                    float angle = Mathf.Atan2(center.z, center.x) + 1.25f;
                    Deposit(pixels, width, height, depth, extents, center,
                        new Vector3(size * 1.6f, size * s.dustCloudThickness, size * 0.75f),
                        angle, 0, 1.6f, s.dustCloudClumpiness, s.seed + cloud * 37);
                }
            }

            int count = s.enableStarFormation ? s.starFormingRegionCount : 0;
            result.regions = new Vector4[count];
            random = new System.Random(unchecked(s.seed + 7919));
            for (int region = 0; region < count; region++)
            {
                Vector3 center = SampleArmPoint(s, random, s.starFormationInnerRadius,
                    s.starFormationOuterRadius, s.starFormationArmBias, -sigma * 0.65f);
                float size = Mathf.Lerp(s.minStarFormingRegionSize, s.maxStarFormingRegionSize,
                    Mathf.Pow(Next(random), 1.5f));
                result.regions[region] = new Vector4(center.x, center.y, center.z, size);
                float angle = Next(random) * Mathf.PI * 2f;
                Deposit(pixels, width, height, depth, extents, center,
                    new Vector3(size * 1.2f, Mathf.Max(size * 0.55f, extents.y / height * 1.4f), size), angle, 1,
                    Mathf.Lerp(0.3f, 0.8f, Next(random)), s.starFormingRegionClumpiness, s.seed + region * 53);
                // Each nebula is an irregular complex of lobes, this avoids them looking the same color
                for (int lobe = 0; lobe < 3; lobe++)
                {
                    Vector3 offset = new Vector3(Gaussian(random), Gaussian(random) * 0.18f, Gaussian(random)) * size * 0.55f;
                    Deposit(pixels, width, height, depth, extents, center + offset,
                        new Vector3(size * 0.7f, Mathf.Max(size * 0.35f, extents.y / height), size * 0.9f),
                        angle + lobe, 1, 0.35f, s.starFormingRegionClumpiness, s.seed + region * 53 + lobe * 31);
                }
                Deposit(pixels, width, height, depth, extents, center,
                    new Vector3(size * 1.8f, size * 0.8f, size * 1.5f), angle, 2,
                    0.65f, s.starFormingRegionClumpiness * 0.35f, s.seed + region * 53);
            }

            for (int z = 0; z < depth; z++)
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        int index = x + width * (y + height * z);
                        if (s.isRing)
                        {
                            float px = Axis(x, width, extents.x), pz = Axis(z, depth, extents.z);
                            float r2 = px * px + pz * pz;
                            if (r2 < s.innerRingRadius * s.innerRingRadius || r2 > radius * radius)
                                pixels[index] = Color.clear;
                        }
                        Color voxel = pixels[index];
                        result.dustMass += voxel.r;
                        result.nebulaMass += voxel.g;
                        result.youngGlowMass += voxel.b;
                    }
            
            result.texture = new Texture3D(width, height, depth, TextureFormat.RGBAHalf, false)
            {
                name = "Galaxy cloud densities (R dust, G nebulae, B young glow, A noise)",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear
            };
            result.texture.SetPixels(pixels);
            result.texture.Apply(false, true);
            return result;
        }

        private static void Deposit(Color[] pixels, int width, int height, int depth, Vector3 extents,
            Vector3 center, Vector3 size, float angle, int channel, float strength, float clumpiness, int seed)
        {
            float horizontal = Mathf.Max(size.x, size.z) * 1.6f;
            Vector3 bounds = new Vector3(horizontal, size.y * 1.6f, horizontal);
            Vector3 min = center - bounds, max = center + bounds;
            int x0 = Index(min.x, width, extents.x), x1 = Index(max.x, width, extents.x);
            int y0 = Index(min.y, height, extents.y), y1 = Index(max.y, height, extents.y);
            int z0 = Index(min.z, depth, extents.z), z1 = Index(max.z, depth, extents.z);
            float sin = Mathf.Sin(angle), cos = Mathf.Cos(angle);
            for (int z = z0; z <= z1; z++)
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        Vector3 p = new Vector3(Axis(x, width, extents.x), Axis(y, height, extents.y),
                            Axis(z, depth, extents.z)) - center;
                        Vector3 q = new Vector3((cos * p.x + sin * p.z) / size.x,
                            p.y / size.y, (-sin * p.x + cos * p.z) / size.z);
                        float distance = q.sqrMagnitude;
                        if (distance > 2.56f)
                            continue;
                        float n = Noise(q * 3.6f + Vector3.one * 11.7f, seed);
                        float grain = Mathf.Lerp(1f, 0.06f + Mathf.Pow(n * 1.6f, 3f), clumpiness);
                        float density = Mathf.Exp(-distance * 2.8f) * grain * strength;
                        int index = x + width * (y + height * z);
                        Color voxel = pixels[index];
                        if (channel == 0)
                            voxel.r += density;
                        else if (channel == 1)
                            voxel.g += density;
                        else
                            voxel.b += density;
                        pixels[index] = voxel;
                    }
        }

        public static Vector3 SampleArmPoint(GalaxySettings s, System.Random random,
            float inner, float outer, float armBias, float angularOffset)
        {
            float r = Mathf.Lerp(inner, outer, Mathf.Sqrt(Next(random))) * s.galaxyRadius;
            if (s.isRing)
                r = Mathf.Sqrt(Mathf.Lerp(s.innerRingRadius * s.innerRingRadius,
                                          s.galaxyRadius * s.galaxyRadius,
                                          Next(random)));
            float angle = Next(random) * Mathf.PI * 2f;
            if (s.armCount > 0 && Next(random) < armBias)
            {
                float sigma = s.armWidth * Mathf.Lerp(0.6f, 0.05f, s.armConcentration);
                angle = s.ArmAngle(r) + random.Next(s.armCount) * Mathf.PI * 2f / s.armCount +
                    Gaussian(random) * sigma * 0.4f + angularOffset;
            }
            return new Vector3(Mathf.Cos(angle) * r,
                               Gaussian(random) * s.diskThickness * 0.055f,
                               Mathf.Sin(angle) * r);
        }
        private static float Axis(int i, int length, float extent)
            => ((i + 0.5f) / length * 2f - 1f) * extent;
        private static int Index(float p, int length, float extent)
            => Mathf.Clamp(Mathf.FloorToInt((p / extent * 0.5f + 0.5f) * length), 0, length - 1);
        private static float ArmDistance(float angle, int count)
        {
            count = Mathf.Max(1, count);
            return Mathf.Abs(Mathf.Repeat(angle * count / (Mathf.PI * 2f) + 0.5f, 1f) - 0.5f) * Mathf.PI * 2f / count;
        }
        private static float Smooth(float min, float max, float x)
        {
            float t = Mathf.Clamp01((x - min) / (max - min));
            return t * t * (3f - 2f * t);
        }
        private static float Next(System.Random random)
            => (float)random.NextDouble();
        private static float Gaussian(System.Random random)
            => Mathf.Sqrt(-2f * Mathf.Log(Mathf.Max(0.000001f, Next(random)))) * Mathf.Cos(2f * Mathf.PI * Next(random));
        private static float FractalNoise(Vector3 p, int seed)
            => Noise(p, seed) * 0.65f + Noise(p * 2.07f + Vector3.one * 9.3f, seed) * 0.35f;
        private static float Noise(Vector3 p, int seed)
        {
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
            float fx = p.x - x, fy = p.y - y, fz = p.z - z;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy); fz = fz * fz * (3f - 2f * fz);
            return Mathf.Lerp(Mathf.Lerp(Mathf.Lerp(Hash(x,y,z,seed), Hash(x+1,y,z,seed),fx),
                Mathf.Lerp(Hash(x,y+1,z,seed), Hash(x+1,y+1,z,seed),fx),fy),
                Mathf.Lerp(Mathf.Lerp(Hash(x,y,z+1,seed), Hash(x+1,y,z+1,seed),fx),
                Mathf.Lerp(Hash(x,y+1,z+1,seed), Hash(x+1,y+1,z+1,seed),fx),fy),fz);
        }
        private static float Hash(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177 + seed);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0x00ffffffu) / 16777216f;
            }
        }
    }
}
