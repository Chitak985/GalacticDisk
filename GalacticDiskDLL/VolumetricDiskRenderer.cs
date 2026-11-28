using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GalacticDisk
{
    /// <summary>
    /// Renders a volumetric disk or ring using a shader.
    /// Largely created with an LLM due to the sheer complexity.
    /// </summary>
    public sealed class VolumetricDiskRenderer : MonoBehaviour
    {
        private readonly List<Mesh> meshes = new List<Mesh>();
        private readonly List<MeshRenderer> renderers = new List<MeshRenderer>();
        private sealed class CameraDraws
        {
            public readonly CommandBuffer commands = new CommandBuffer { name = "GalacticDisk volume and mesh stars" };
            public readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
            internal readonly DistantGalaxy.View distant = new DistantGalaxy.View();
        }
        private readonly Dictionary<Camera, CameraDraws> cameraDraws = new Dictionary<Camera, CameraDraws>();
        private readonly List<Camera> staleCameras = new List<Camera>();
        private Material volumeMaterial;
        private Material starMaterial;
        private GalaxyCloudField cloudField;
        private GalaxySettings settings;
        private GalaxyConfig config;
        private Vector3 drawExtents;
        private Texture3D boundField;
        private DistantGalaxy distant;
        public int StarCount { get; private set; }
        public bool IsReady
        {
            get
            {
                return starMaterial != null && cloudField != null && (cloudField.texture != null || cloudField.pending) &&
                    (volumeMaterial != null || (settings != null && !settings.enableVolume));
            }
        }

        public void Initialize(GalaxyConfig diskConfig)
        {
            if (!SystemInfo.supports3DTextures || !SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf))
                throw new NotSupportedException("GalacticDisk volumetric rendering requires 3D textures and RGBAHalf support.");
            config = diskConfig;
            settings = VolumeConfig.SettingsFor(config);
            distant = new DistantGalaxy(config);
            GalaxyShaderAssets.Load(out Shader volumeShader, out Shader starShader);
            UpdateMatrices();
            Vector3 extents = VolumeExtents();
            drawExtents = extents;
            cloudField = GalaxyCloudCache.Acquire(settings, extents);
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
                Collider collider = box.GetComponent<Collider>();
                if (collider != null) { collider.enabled = false; Release(collider); }
                Mesh cube = Instantiate(box.GetComponent<MeshFilter>().sharedMesh);
                cube.name = "GalacticDisk volume bounds";
                cube.bounds = new Bounds(Vector3.zero, Vector3.one * Mathf.Max(1f, config.renderScale));
                meshes.Add(cube);
                box.GetComponent<MeshFilter>().sharedMesh = cube;
                volumeMaterial = new Material(volumeShader)
                {
                    // Put shader far into the background
                    renderQueue = 2980
                };
                ApplyVolumeSettings(volumeMaterial, extents);
                volumeMaterial.SetFloat("_Exposure", settings.volumeExposure);
                Configure(box.GetComponent<MeshRenderer>(), volumeMaterial);
            }
            UpdateMatrices();
            // Prepare existing KSP cameras before their render setup begins
            boundField = cloudField.texture;
            foreach (Camera camera in Camera.allCameras)
                PrepareCamera(camera);
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
            const int batchSize = 16000; // Four vertices per star, each batch fits 16-bit indices
            StarCount = stars.Length;
            for (int first = 0; first < stars.Length; first += batchSize)
            {
                int count = Mathf.Min(batchSize, stars.Length - first);
                Vector3[] positions = new Vector3[count * 4];
                Color32[] colors = new Color32[count * 4];
                Vector2[] uv = new Vector2[count * 4];
                Vector2[] sizes = new Vector2[count * 4];
                int[] indices = new int[count * 6];
                for (int i = 0; i < count; i++)
                {
                    GalaxyStar star = stars[first + i];
                    distant.AddStar(star.color, star.size);
                    float halfSize = star.size * 0.5f;
                    drawExtents.x = Mathf.Max(drawExtents.x, Mathf.Abs(star.position.x) + halfSize);
                    drawExtents.y = Mathf.Max(drawExtents.y, Mathf.Abs(star.position.y) + halfSize);
                    drawExtents.z = Mathf.Max(drawExtents.z, Mathf.Abs(star.position.z) + halfSize);
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
                Mesh mesh = new Mesh
                {
                    name = "GalacticDisk stars " + first,
                    indexFormat = IndexFormat.UInt16,
                    vertices = positions,
                    colors32 = colors,
                    uv = uv,
                    uv2 = sizes,
                    triangles = indices,
                    bounds = new Bounds(Vector3.zero,
                        (extents * 2f + Vector3.one * settings.maxStarSize * 2f) * Mathf.Max(1f, config.renderScale))
                };
                meshes.Add(mesh);
                mesh.UploadMeshData(true);
                GameObject batch = new GameObject("Mesh stars " + first)
                {
                    layer = 10
                };
                batch.transform.SetParent(transform, false);
                batch.AddComponent<MeshFilter>().sharedMesh = mesh;
                Configure(batch.AddComponent<MeshRenderer>(), starMaterial);
            }
        }

        /// <summary>
        /// Configure the <see cref="MeshRenderer"/> of the volumetric disk object to function correctly.
        /// </summary>
        private void Configure(MeshRenderer renderer, Material material)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.enabled = false;
            renderers.Add(renderer);
        }

        private void OnEnable() { Camera.onPreCull += PrepareCamera; }
        private void OnDisable() { Camera.onPreCull -= PrepareCamera; ReleaseCameraBuffers(); }
        private void PrepareCamera(Camera camera)
        {
            if (!isActiveAndEnabled || !IsReady || camera == null) return;
            if (!DiskSceneCache.SyncAnchor(transform.parent)) return;
            PruneCameraBuffers();
            CameraDraws draws;
            if (!DiskCamera.RendersDisk(camera,transform.parent) || !DiskVisibility.IsVisible(camera, transform.parent, drawExtents, config.objectScale))
            {
                if (cameraDraws.TryGetValue(camera, out draws)) { draws.commands.Clear(); DiskRenderOrder.Hide(camera, draws.commands); }
                return;
            }
            if (!cameraDraws.TryGetValue(camera, out draws))
            {
                draws = new CameraDraws();
                draws.commands.name = "GalacticDisk " + transform.parent.name;
                cameraDraws.Add(camera, draws);
            }
            distant.Evaluate(camera, transform.parent, drawExtents, draws.distant);
            if (draws.distant.detail <= 0)
            {
                Vector3 local = Quaternion.Inverse(transform.parent.rotation) * -DistantGalaxy.CameraOffset(camera, transform.parent, config.objectScale);
                draws.commands.Clear();
                DistantGalaxy.DrawData data;
                if (distant.TryGetDraw(camera,draws.distant,DiskOpacity.Evaluate(config,camera,local),out data))
                    DiskRenderOrder.SubmitDot(camera,this,draws.commands,data);
                else DiskRenderOrder.Hide(camera,draws.commands);
                return;
            }
            if (draws.distant.detail > 0 && !WarmCloudField()) { draws.commands.Clear(); DiskRenderOrder.Hide(camera, draws.commands); return; }
            // Request depth only for cameras capable of drawing scaled space.
            // No demo camera or global bloom/tonemapping effect is installed.
            bool depthWasRequested = (camera.depthTextureMode & DepthTextureMode.Depth) != 0;
            if (draws.distant.detail > 0) camera.depthTextureMode |= DepthTextureMode.Depth;
            // If this is a newly created camera, requesting depth during pre-cull can
            // be too late for this render. Do not sample an uninitialized fallback.
            UpdateMatrices();
            // Project in galaxy units. Squaring astronomical world-space vectors or
            // inverting their scaled matrices can overflow/underflow 32-bit GPU math.
            Transform galaxy = transform.parent;
            Quaternion inverseGalaxy = Quaternion.Inverse(galaxy.rotation);
            Vector3 offset = DistantGalaxy.CameraOffset(camera, galaxy, config.objectScale);
            // Camera transforms look along +Z; Unity projection matrices use -Z.
            Matrix4x4 galaxyToView = DiskCamera.Rotation(camera) * Matrix4x4.TRS(offset,galaxy.rotation,Vector3.one);
            Vector3 cameraInGalaxy = inverseGalaxy * (-offset);
            // Each camera owns its draw state, including nested reflection cameras.
            // Identity draw matrices bypass astronomical parent transforms entirely.
            MaterialPropertyBlock properties = draws.properties;
            properties.SetMatrix("_GalaxyToView", galaxyToView);
            properties.SetVector("_GalaxyCameraPosition", cameraInGalaxy);
            properties.SetVector("_GalaxyViewDirection", inverseGalaxy * DiskCamera.Forward(camera));
            properties.SetFloat("_GalaxyWorldScale", config.objectScale);
            float opacity = DiskOpacity.Evaluate(config, camera, cameraInGalaxy);
            properties.SetFloat("_OpacityMultiplier", opacity * draws.distant.detail);
            properties.SetFloat("_UseSceneDepth", depthWasRequested ? 1f : 0f);
            draws.commands.Clear();
            // BuildStars runs before the volume proxy is created. Explicitly draw
            // the volume first so it cannot obscure its own cached star light.
            for (int pass = 0; pass < 2; pass++)
            foreach (MeshRenderer renderer in renderers)
            {
                if (draws.distant.detail <= 0) break;
                Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                bool isVolume = renderer.sharedMaterial == volumeMaterial;
                if (isVolume != (pass == 0)) continue;
                draws.commands.DrawMesh(mesh, Matrix4x4.identity,
                    renderer.sharedMaterial, 0, 0, properties);
            }
            DiskRenderOrder.Submit(camera, this, draws.commands, renderers.Count);
            distant.Draw(draws.commands, properties, camera, draws.distant, opacity);
        }
        
        /// <summary>
        /// Removes any stale camera buffers.
        /// </summary>
        private void PruneCameraBuffers()
        {
            staleCameras.Clear();
            foreach (KeyValuePair<Camera,CameraDraws> entry in cameraDraws)
                if (entry.Key == null)
                    staleCameras.Add(entry.Key);
            foreach (Camera camera in staleCameras)
                ReleaseCameraBuffers(camera, cameraDraws[camera]);
        }
        /// <summary>
        /// Releases a single camera's buffer.
        /// </summary>
        private void ReleaseCameraBuffers(Camera camera, CameraDraws draws)
        {
            DiskRenderOrder.Remove(camera, draws.commands);
            draws.commands.Release();
            cameraDraws.Remove(camera);
        }
        /// <summary>
        /// Releases all camera buffers.
        /// </summary>
        private void ReleaseCameraBuffers()
        {
            foreach (KeyValuePair<Camera,CameraDraws> entry in new List<KeyValuePair<Camera,CameraDraws>>(cameraDraws))
                ReleaseCameraBuffers(entry.Key, entry.Value);
            cameraDraws.Clear();
        }
        
        internal void UpdateMatrices()
        {
            if (config == null || transform.parent == null) return;
            Matrix4x4 parent = transform.parent.localToWorldMatrix;
            transform.localScale = new Vector3(
                (float)(1.0 / Math.Max(Math.Sqrt((double)parent.m00 * parent.m00 + (double)parent.m10 * parent.m10 + (double)parent.m20 * parent.m20), 1e-30)),
                (float)(1.0 / Math.Max(Math.Sqrt((double)parent.m01 * parent.m01 + (double)parent.m11 * parent.m11 + (double)parent.m21 * parent.m21), 1e-30)),
                (float)(1.0 / Math.Max(Math.Sqrt((double)parent.m02 * parent.m02 + (double)parent.m12 * parent.m12 + (double)parent.m22 * parent.m22), 1e-30))
            );
        }

        internal bool WarmCloudField()
        {
            if (!GalaxyCloudCache.EnsureTexture(cloudField)) return false;
            if (boundField != cloudField.texture)
            {
                starMaterial.SetTexture("_CloudField", cloudField.texture);
                if (volumeMaterial != null) volumeMaterial.SetTexture("_CloudField", cloudField.texture);
                boundField = cloudField.texture;
            }
            return true;
        }

        /// <summary>
        /// Removes all resources used by the renderer.
        /// </summary>
        public void DisposeResources()
        {
            ReleaseCameraBuffers();
            foreach (MeshRenderer renderer in renderers)
                if (renderer != null)
                    renderer.enabled = false;
            renderers.Clear();
            foreach (Mesh mesh in meshes) Release(mesh);
            meshes.Clear();
            Release(volumeMaterial); Release(starMaterial);
            GalaxyCloudCache.Release(cloudField);
            distant?.Dispose(); distant = null;
            cloudField = null; boundField = null; volumeMaterial = null; starMaterial = null;
        }
        
        private void OnDestroy() { DisposeResources(); }
        
        /// <summary>
        /// Release a <see cref="UnityEngine.Object"/> correctly.
        /// If application is running the object is destroyed,
        /// but if it isn't then it is destroyed immediately.
        /// </summary>
        private static void Release(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying)
                Destroy(obj);
            else
                DestroyImmediate(obj);
        }

        /// <summary>
        /// Set up the volumetric shader material using the loaded <see cref="GalaxySettings"/>.
        /// </summary>
        private void ApplyVolumeSettings(Material material, Vector3 extents)
        {
            material.SetFloat("_AnalyticBulge", GalaxyCloudCache.IsAnalyticBulge(settings) ? 1f : 0f);
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
