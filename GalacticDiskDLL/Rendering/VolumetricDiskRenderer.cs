using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GalacticDisk.Rendering
{
    // Owns meshes, materials, a Texture3D and per-camera drawing commands. No ParticleSystem.
    public sealed class VolumetricDiskRenderer : MonoBehaviour
    {
        private readonly List<Mesh> meshes = new List<Mesh>();
        private readonly List<MeshRenderer> renderers = new List<MeshRenderer>();
        private sealed class CameraDraws
        {
            public readonly CommandBuffer volume = new CommandBuffer { name = "GalacticDisk volume" };
            public readonly CommandBuffer stars = new CommandBuffer { name = "GalacticDisk mesh stars" };
            public readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        }
        private readonly Dictionary<Camera, CameraDraws> cameraDraws = new Dictionary<Camera, CameraDraws>();
        private readonly List<Camera> staleCameras = new List<Camera>();
        private Material volumeMaterial;
        private Material starMaterial;
        private GalaxyCloudField cloudField;
        private GalaxySettings settings;
        private GalaxyConfig config;
        public int StarCount { get; private set; }
        public bool IsReady { get { return starMaterial != null && (volumeMaterial != null || (settings != null && !settings.enableVolume)); } }

        public void Initialize(GalaxyConfig diskConfig)
        {
            if (!SystemInfo.supports3DTextures || !SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf))
                throw new NotSupportedException("GalacticDisk volumetric rendering requires 3D textures and RGBAHalf support.");
            config = diskConfig;
            settings = VolumeConfig.SettingsFor(config);
            Shader volumeShader, starShader;
            GalaxyShaderAssets.Load(out volumeShader, out starShader);
            UpdateMatrices();
            Vector3 extents = VolumeExtents();
            cloudField = GalaxyCloudField.Build(settings, extents);
            starMaterial = new Material(starShader);
            starMaterial.SetTexture("_CloudField", cloudField.texture);
            starMaterial.SetFloat("_Brightness", settings.starBrightness);
            starMaterial.SetFloat("_Exposure", settings.volumeExposure);
            starMaterial.SetVector("_CloudExtents", extents);
            starMaterial.SetFloat("_GalaxyRadius", settings.galaxyRadius);
            starMaterial.SetFloat("_DustOpacity", settings.enableDustClouds ? settings.dustOpacity : 0f);
            starMaterial.SetColor("_DustColor", settings.dustColor);
            starMaterial.SetInt("_DustSamples", settings.starDustSamples);
            BuildStars(GalaxyStarGenerator.Generate(settings, cloudField), extents);
            if (settings.enableVolume)
            {
                GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = "GalacticDisk Volume";
                box.layer = 10;
                box.transform.SetParent(transform, false);
                box.transform.localScale = extents * 2f;
                var collider = box.GetComponent<Collider>();
                if (collider != null) { collider.enabled = false; Release(collider); }
                Mesh cube = Instantiate(box.GetComponent<MeshFilter>().sharedMesh);
                cube.name = "GalacticDisk volume bounds";
                cube.bounds = new Bounds(Vector3.zero, Vector3.one * Mathf.Max(1f, config.renderScale));
                meshes.Add(cube);
                box.GetComponent<MeshFilter>().sharedMesh = cube;
                volumeMaterial = new Material(volumeShader);
                // Galaxy fog is a background layer. Never paint its extinction over
                // pre-existing particle galaxies from other definitions/mods.
                volumeMaterial.renderQueue = 2980;
                ApplyVolumeSettings(volumeMaterial, extents);
                volumeMaterial.SetFloat("_Exposure", settings.volumeExposure);
                Configure(box.GetComponent<MeshRenderer>(), volumeMaterial);
            }
            UpdateMatrices();
            // Prepare already-existing KSP cameras before their render setup begins.
            foreach (Camera camera in Camera.allCameras) PrepareCamera(camera);
        }

        private Vector3 VolumeExtents()
        {
            if (settings.isRing) return new Vector3(settings.galaxyRadius,
                Mathf.Max(0.01f, settings.diskThickness * 1.5f), settings.galaxyRadius);
            float barExtent = settings.enableBar ? Mathf.Max(settings.barLength, settings.barWidth) * 1.25f : 0f;
            return new Vector3(Mathf.Max(settings.galaxyRadius, settings.bulgeScaleX * 4f, barExtent),
                Mathf.Max(0.01f, settings.diskThickness * 1.5f, settings.bulgeScaleY * 4f,
                    settings.enableBar ? settings.barHeight * 1.5f : 0f),
                Mathf.Max(settings.galaxyRadius, settings.bulgeScaleZ * 4f, barExtent));
        }

        private void BuildStars(GalaxyStar[] stars, Vector3 extents)
        {
            const int batchSize = 16000; // Four vertices per star: each batch fits 16-bit indices.
            StarCount = stars.Length;
            for (int first = 0; first < stars.Length; first += batchSize)
            {
                int count = Mathf.Min(batchSize, stars.Length - first);
                var positions = new Vector3[count * 4];
                var colors = new Color32[count * 4];
                var uv = new Vector2[count * 4];
                var sizes = new Vector2[count * 4];
                var indices = new int[count * 6];
                for (int i = 0; i < count; i++)
                {
                    GalaxyStar star = stars[first + i];
                    int v = i * 4, t = i * 6;
                    for (int j = 0; j < 4; j++)
                    {
                        positions[v + j] = star.position;
                        colors[v + j] = star.color;
                        sizes[v + j] = new Vector2(star.size, 0f);
                    }
                    uv[v] = Vector2.zero; uv[v + 1] = Vector2.right;
                    uv[v + 2] = Vector2.one; uv[v + 3] = Vector2.up;
                    indices[t] = v; indices[t+1] = v+1; indices[t+2] = v+2;
                    indices[t+3] = v; indices[t+4] = v+2; indices[t+5] = v+3;
                }
                var mesh = new Mesh { name = "GalacticDisk stars " + first, indexFormat = IndexFormat.UInt16 };
                meshes.Add(mesh);
                mesh.vertices = positions; mesh.colors32 = colors; mesh.uv = uv; mesh.uv2 = sizes; mesh.triangles = indices;
                mesh.bounds = new Bounds(Vector3.zero, (extents * 2f + Vector3.one * settings.maxStarSize * 2f) *
                    Mathf.Max(1f, config.renderScale));
                mesh.UploadMeshData(true);
                var batch = new GameObject("Mesh stars " + first);
                batch.layer = 10;
                batch.transform.SetParent(transform, false);
                batch.AddComponent<MeshFilter>().sharedMesh = mesh;
                Configure(batch.AddComponent<MeshRenderer>(), starMaterial);
            }
        }

        private void Configure(MeshRenderer renderer, Material material)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            // Submit explicitly per camera. A MeshRenderer under a distant scaled
            // body cannot be moved to the floating origin reliably enough for culling.
            renderer.enabled = false;
            renderers.Add(renderer);
        }

        private void OnEnable() { Camera.onPreCull += PrepareCamera; }
        private void OnDisable() { Camera.onPreCull -= PrepareCamera; ReleaseCameraBuffers(); }
        private void PrepareCamera(Camera camera)
        {
            if (!isActiveAndEnabled || !IsReady || camera == null) return;
            PruneCameraBuffers();
            CameraDraws draws;
            if ((camera.cullingMask & (1 << 10)) == 0)
            {
                if (cameraDraws.TryGetValue(camera, out draws)) { draws.volume.Clear(); draws.stars.Clear(); }
                return;
            }
            if (!cameraDraws.TryGetValue(camera, out draws))
            {
                draws = new CameraDraws();
                camera.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, draws.volume);
                camera.AddCommandBuffer(CameraEvent.AfterForwardAlpha, draws.stars);
                cameraDraws.Add(camera, draws);
            }
            // Request depth only for cameras capable of drawing scaled space.
            // No demo camera or global bloom/tonemapping effect is installed.
            bool depthWasRequested = (camera.depthTextureMode & DepthTextureMode.Depth) != 0;
            camera.depthTextureMode |= DepthTextureMode.Depth;
            // If this is a newly created camera, requesting depth during pre-cull can
            // be too late for this render. Do not sample an uninitialized fallback.
            UpdateMatrices();
            // Project in galaxy units. Squaring astronomical world-space vectors or
            // inverting their scaled matrices can overflow/underflow 32-bit GPU math.
            Transform galaxy = transform.parent;
            Quaternion inverseCamera = Quaternion.Inverse(camera.transform.rotation);
            Quaternion inverseGalaxy = Quaternion.Inverse(galaxy.rotation);
            Vector3 offset = (galaxy.position - camera.transform.position) / config.objectScale;
            // Camera transforms look along +Z; Unity projection matrices use -Z.
            Matrix4x4 galaxyToView = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) *
                Matrix4x4.TRS(inverseCamera * offset, inverseCamera * galaxy.rotation, Vector3.one);
            Vector3 cameraInGalaxy = inverseGalaxy * (-offset);
            // Each camera owns its draw state, including nested reflection cameras.
            // Identity draw matrices bypass astronomical parent transforms entirely.
            MaterialPropertyBlock properties = draws.properties;
            properties.SetMatrix("_GalaxyToView", galaxyToView);
            properties.SetVector("_GalaxyCameraPosition", cameraInGalaxy);
            properties.SetVector("_GalaxyViewDirection", inverseGalaxy * camera.transform.forward);
            properties.SetFloat("_GalaxyWorldScale", config.objectScale);
            properties.SetFloat("_UseSceneDepth", depthWasRequested ? 1f : 0f);
            draws.volume.Clear(); draws.stars.Clear();
            foreach (MeshRenderer renderer in renderers)
            {
                Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                bool isVolume = renderer.sharedMaterial == volumeMaterial;
                (isVolume ? draws.volume : draws.stars).DrawMesh(mesh, Matrix4x4.identity,
                    renderer.sharedMaterial, 0, 0, properties);
            }
        }
        private void PruneCameraBuffers()
        {
            staleCameras.Clear();
            foreach (var entry in cameraDraws) if (entry.Key == null) staleCameras.Add(entry.Key);
            foreach (Camera camera in staleCameras) ReleaseCameraBuffers(camera, cameraDraws[camera]);
        }
        private void ReleaseCameraBuffers(Camera camera, CameraDraws draws)
        {
            if (camera != null)
            {
                camera.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, draws.volume);
                camera.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, draws.stars);
            }
            draws.volume.Release(); draws.stars.Release();
            cameraDraws.Remove(camera);
        }
        private void ReleaseCameraBuffers()
        {
            foreach (var entry in cameraDraws)
            {
                if (entry.Key != null)
                {
                    entry.Key.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, entry.Value.volume);
                    entry.Key.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, entry.Value.stars);
                }
                entry.Value.volume.Release(); entry.Value.stars.Release();
            }
            cameraDraws.Clear();
        }
        internal void UpdateMatrices()
        {
            if (config == null || transform.parent == null) return;
            // Old Local-mode particles did not inherit scaledBody's radius scaling.
            // Give the volume the same physical objectScale, independently of that radius.
            Matrix4x4 parent = transform.parent.localToWorldMatrix;
            double sx = Math.Sqrt((double)parent.m00 * parent.m00 + (double)parent.m10 * parent.m10 + (double)parent.m20 * parent.m20);
            double sy = Math.Sqrt((double)parent.m01 * parent.m01 + (double)parent.m11 * parent.m11 + (double)parent.m21 * parent.m21);
            double sz = Math.Sqrt((double)parent.m02 * parent.m02 + (double)parent.m12 * parent.m12 + (double)parent.m22 * parent.m22);
            transform.localScale = new Vector3((float)(1.0 / Math.Max(sx, 1e-30)),
                (float)(1.0 / Math.Max(sy, 1e-30)), (float)(1.0 / Math.Max(sz, 1e-30)));
        }

        public void DisposeResources()
        {
            ReleaseCameraBuffers();
            foreach (var renderer in renderers) if (renderer != null) renderer.enabled = false;
            renderers.Clear();
            foreach (var mesh in meshes) Release(mesh);
            meshes.Clear();
            Release(volumeMaterial); Release(starMaterial);
            if (cloudField != null) Release(cloudField.texture);
            cloudField = null; volumeMaterial = null; starMaterial = null;
        }
        private void OnDestroy() { DisposeResources(); }
        private static void Release(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj);
        }

        private void ApplyVolumeSettings(Material material, Vector3 extents)
        {
            material.SetFloat("_IsRing", settings.isRing ? 1f : 0f);
            material.SetFloat("_InnerRingRadius", settings.innerRingRadius);
            material.SetVector("_Extents", extents);
            material.SetFloat("_GalaxyRadius", settings.galaxyRadius);
            material.SetFloat("_DiskThickness", settings.diskThickness);
            material.SetFloat("_ArmCount", settings.armCount);
            material.SetFloat("_ArmTightness", settings.armTightness);
            material.SetFloat("_ArmWidth", settings.armWidth);
            material.SetFloat("_InterarmFraction", settings.interarmFraction);
            material.SetFloat("_ArmConcentration", settings.armConcentration);
            material.SetFloat("_LogarithmicArms", settings.logarithmicArms ? 1f : 0f);
            material.SetFloat("_ArmPitchAngle", settings.armPitchAngle * Mathf.Deg2Rad);
            material.SetFloat("_ArmStartRadius", settings.armStartRadius);
            material.SetFloat("_SecondaryArmStrength", settings.secondaryArmStrength);
            material.SetFloat("_ArmIrregularity", settings.armIrregularity);
            material.SetFloat("_BarFraction", settings.enableBar ? settings.barFraction : 0f);
            material.SetVector("_BarScale", new Vector4(settings.barLength * 0.25f,
                settings.barHeight * 0.35f, settings.barWidth * 0.35f, settings.enableBar ? settings.barAngle * Mathf.Deg2Rad : 0f));
            material.SetFloat("_BulgeFraction", settings.bulgeFraction);
            material.SetVector("_BulgeScale", new Vector4(settings.bulgeScaleX,
                settings.bulgeScaleY, settings.bulgeScaleZ, 0f));
            material.SetColor("_ColorBulge", settings.colorBulge);
            material.SetColor("_ColorArms", settings.colorArms);
            material.SetColor("_ColorInterarm", settings.colorInterarm);
            material.SetColor("_ColorHighlight", settings.armHighlightColor);
            material.SetFloat("_HighlightChance", settings.armHighlightChance);
            material.SetFloat("_ScaleLength", settings.diskScaleLengthFraction);
            material.SetFloat("_Intensity", settings.volumeIntensity);
            material.SetFloat("_DustOpacity", settings.enableDustClouds ? settings.dustOpacity : 0f);
            material.SetColor("_DustColor", settings.dustColor);
            material.SetFloat("_DustScattering", settings.dustScattering);
            material.SetColor("_NebulaColor", settings.starFormingRegionColor);
            material.SetFloat("_NebulaIntensity", settings.starFormingRegionIntensity);
            material.SetColor("_YoungStarColor", settings.youngStarColor);
            material.SetFloat("_YoungStarIntensity", settings.youngStarGlowIntensity);
            if (cloudField != null) material.SetTexture("_CloudField", cloudField.texture);
            material.SetFloat("_NoiseFrequency", settings.noiseFrequency);
            material.SetFloat("_Seed", (settings.seed % 997) * 0.17f);
            material.SetInt("_Steps", settings.volumeSteps);
        }
    }
}
