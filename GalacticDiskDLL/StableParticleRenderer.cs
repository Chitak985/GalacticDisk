using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GalacticDisk
{
    // Uses the original particle positions, sizes, colors and texture. Only drawing
    // changes: giant parent matrices and native renderer bounds are bypassed.
    public sealed class StableParticleRenderer : MonoBehaviour
    {
        private readonly List<Mesh> meshes = new List<Mesh>();
        private sealed class CameraDraws
        {
            public readonly CommandBuffer commands = new CommandBuffer { name = "GalacticDisk stable particles" };
            public readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
            internal readonly DistantGalaxy.View distant = new DistantGalaxy.View();
        }
        private readonly Dictionary<Camera, CameraDraws> cameras = new Dictionary<Camera, CameraDraws>();
        private readonly List<Camera> stale = new List<Camera>();
        private GalaxyConfig config;
        private ParticleSystemRenderer nativeRenderer;
        private Material material;
        private DistantGalaxy distant;
        private Vector3 drawExtents;
        public bool IsReady { get { return material != null; } }
        public int ParticleCount { get; private set; }

        public void Initialize(GalaxyConfig diskConfig, ParticleSystem.Particle[] particles, Texture2D texture,
            Color tint, ParticleSystemRenderer originalRenderer)
        {
            config = diskConfig; nativeRenderer = originalRenderer;
            distant = new DistantGalaxy(config);
            material = new Material(GalaxyShaderAssets.LoadParticles());
            material.SetTexture("_MainTex", texture); material.SetColor("_TintColor", tint);
            material.SetVector("_ParticleSizeLimits", new Vector4(originalRenderer.minParticleSize, originalRenderer.maxParticleSize, 0f, 0f));
            ParticleCount = particles.Length;
            const int batchSize = 16000;
            for (int first = 0; first < particles.Length; first += batchSize)
            {
                int count = Mathf.Min(batchSize, particles.Length - first);
                var positions = new Vector3[count * 4]; var colors = new Color32[count * 4];
                var uv = new Vector2[count * 4]; var sizes = new Vector2[count * 4]; var triangles = new int[count * 6];
                for (int i = 0; i < count; i++)
                {
                    var particle = particles[first + i]; int v = i * 4, t = i * 6;
                    distant.AddStar((Color)particle.startColor * tint, particle.startSize * config.renderScale);
                    Vector3 extent = particle.position * config.renderScale;
                    float half = particle.startSize * config.renderScale * 0.5f;
                    drawExtents = Vector3.Max(drawExtents, new Vector3(Mathf.Abs(extent.x)+half, Mathf.Abs(extent.y)+half, Mathf.Abs(extent.z)+half));
                    for (int j = 0; j < 4; j++)
                    {
                        positions[v + j] = particle.position * config.renderScale;
                        sizes[v + j] = new Vector2(particle.startSize * config.renderScale, 0f);
                        colors[v + j] = particle.startColor;
                    }
                    uv[v] = Vector2.zero; uv[v + 1] = Vector2.right; uv[v + 2] = Vector2.one; uv[v + 3] = Vector2.up;
                    triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                    triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
                }
                var mesh = new Mesh { name = "GalacticDisk particle billboards " + first, indexFormat = IndexFormat.UInt16 };
                meshes.Add(mesh);
                mesh.vertices = positions; mesh.colors32 = colors; mesh.uv = uv; mesh.uv2 = sizes; mesh.triangles = triangles;
                mesh.UploadMeshData(true);
            }
            nativeRenderer.enabled = false;
            foreach (Camera camera in Camera.allCameras) PrepareCamera(camera);
        }

        private void OnEnable() { Camera.onPreCull += PrepareCamera; }
        private void OnDisable() { Camera.onPreCull -= PrepareCamera; ReleaseCameras(); }
        private void PrepareCamera(Camera camera)
        {
            if (!isActiveAndEnabled || !IsReady || camera == null) return;
            if (!DiskSceneCache.SyncAnchor(transform.parent)) return;
            if (nativeRenderer != null) nativeRenderer.enabled = false;
            stale.Clear();
            foreach (var entry in cameras) if (entry.Key == null) stale.Add(entry.Key);
            foreach (Camera dead in stale) { DiskRenderOrder.Remove(dead, cameras[dead].commands); cameras[dead].commands.Release(); cameras.Remove(dead); }
            CameraDraws draws;
            if (!DiskCamera.RendersDisk(camera,transform.parent) || !DiskVisibility.IsVisible(camera, transform.parent, drawExtents, config.objectScale))
            {
                if (cameras.TryGetValue(camera, out draws)) { draws.commands.Clear(); DiskRenderOrder.Hide(camera, draws.commands); }
                return;
            }
            if (!cameras.TryGetValue(camera, out draws))
            {
                draws = new CameraDraws();
                draws.commands.name = "GalacticDisk " + transform.parent.name;
                cameras.Add(camera, draws);
            }
            Transform galaxy = transform.parent;
            distant.Evaluate(camera, galaxy, drawExtents, draws.distant);
            Vector3 offset = DistantGalaxy.CameraOffset(camera, galaxy, config.objectScale);
            float opacity = DiskOpacity.Evaluate(config, camera, Quaternion.Inverse(galaxy.rotation) * (-offset));
            draws.commands.Clear();
            if (draws.distant.detail <= 0)
            {
                DistantGalaxy.DrawData data;
                if (distant.TryGetDraw(camera,draws.distant,opacity,out data)) DiskRenderOrder.SubmitDot(camera,this,draws.commands,data);
                else DiskRenderOrder.Hide(camera,draws.commands);
                return;
            }
            Matrix4x4 galaxyToView = DiskCamera.Rotation(camera) * Matrix4x4.TRS(offset,galaxy.rotation,Vector3.one);
            draws.properties.SetMatrix("_GalaxyToView", galaxyToView);
            draws.properties.SetFloat("_GalaxyWorldScale", config.objectScale);
            draws.properties.SetFloat("_OpacityMultiplier", opacity * draws.distant.detail);
            draws.commands.Clear();
            if (draws.distant.detail > 0)
                foreach (Mesh mesh in meshes) draws.commands.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0, draws.properties);
            distant.Draw(draws.commands, draws.properties, camera, draws.distant, opacity);
            DiskRenderOrder.Submit(camera, this, draws.commands, meshes.Count);
        }
        private void ReleaseCameras()
        {
            foreach (var entry in cameras)
            {
                DiskRenderOrder.Remove(entry.Key, entry.Value.commands);
                entry.Value.commands.Release();
            }
            cameras.Clear();
        }
        public void DisposeResources()
        {
            ReleaseCameras();
            foreach (Mesh mesh in meshes) Release(mesh);
            meshes.Clear(); Release(material); material = null;
            distant?.Dispose(); distant = null;
        }
        private void OnDestroy() { DisposeResources(); }
        private static void Release(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
