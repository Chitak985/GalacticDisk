using System;
using UnityEngine;
using System.Collections.Generic;

namespace GalacticDisk
{
    /// <summary>
    /// This <see cref="MonoBehaviour"/> is attached to <see cref="GameObject"/>s that are created for GalacticDisk objects.
    /// </summary>
    [DefaultExecutionOrder(32000)]
    public class GalacticDiskObject : MonoBehaviour
    {
        /// <summary> Particles used for the disk </summary>
        public ParticleSystem.Particle[] particles;
        /// <summary> Particle system used by the disk </summary>
        public ParticleSystem ps;
        /// <summary> Particle system renderer used by the disk </summary>
        public ParticleSystemRenderer psR;
        /// <summary> Particle system <see cref="Material"/> used by the disk </summary>
        public Material material;
        /// <summary> <see cref="GalaxyConfig"/> defining the disk </summary>
        public GalaxyConfig config;
        /// <summary> Root of the disk usde for rendering </summary>
        public GameObject renderRoot;
        /// <summary> The volumetric renderer used for the volumetric part of the disk </summary>
        public VolumetricDiskRenderer volume;
        public StableParticleRenderer stableParticles;
        /// <summary> Texture used by disk particles </summary>
        public Texture2D ownedTexture;

        /// <summary>
        /// Celestial body the disk is attached to.
        /// The inclination of this body's Orbit also defines how the disk tilts with alignRotationToOrbit
        /// </summary>
        public CelestialBody orbitBody;
        private Transform sceneAnchor;
        private bool persistentBinding;
        internal bool SceneBindingMissing
        {
            get { return persistentBinding && (sceneAnchor == null || orbitBody == null || orbitBody.scaledBody == null || sceneAnchor != orbitBody.scaledBody.transform); }
        }
        internal void BindSceneBody(CelestialBody body)
        {
            persistentBinding = true; orbitBody = body;
            sceneAnchor = body != null && body.scaledBody != null ? body.scaledBody.transform : null;
            if (sceneAnchor != null) transform.position = sceneAnchor.position;
            transform.localScale = Vector3.one;
        }
        internal void ClearSceneBody() { sceneAnchor = null; orbitBody = null; }
        internal bool SyncScenePosition()
        {
            if (!persistentBinding) return true;
            if (!DiskSceneCache.SceneReady || SceneBindingMissing)
            {
                if (gameObject.activeSelf) gameObject.SetActive(false);
                DiskSceneCache.RequestBinding(); return false;
            }
            Vector3 position = sceneAnchor.position, current = transform.position;
            if (current.x != position.x || current.y != position.y || current.z != position.z) transform.position = position;
            return true;
        }

        /// <summary> Frames since the last check, used for timing the refresh check </summary>
        private int framesSinceCheck = 0;
        /// <summary> Frames to wait before checking the disk again, faster will affect performance </summary>
        private const int checkIntervalFrames = 30; // ~2x/sec at 60fps
        private readonly Dictionary<Camera, Color> nativeCameraColors = new Dictionary<Camera, Color>();
        private Color nativeBaseColor;

        private void OnEnable()
        {
            Camera.onPreCull += PrepareNativeOpacity;
            Camera.onPostRender += RestoreNativeOpacity;
        }
        private void OnDisable()
        {
            Camera.onPreCull -= PrepareNativeOpacity;
            Camera.onPostRender -= RestoreNativeOpacity;
            if (nativeCameraColors.Count > 0 && material != null) material.color = nativeBaseColor;
            nativeCameraColors.Clear();
        }
        private void PrepareNativeOpacity(Camera camera)
        {
            if (!isActiveAndEnabled || config == null || config.renderMode != DiskRenderMode.Particles ||
                material == null || psR == null || !psR.enabled || (camera.cullingMask & (1 << 10)) == 0 ||
                (stableParticles != null && stableParticles.enabled)) return;
            if (!SyncScenePosition()) return;
            Color tint = material.color;
            if (nativeCameraColors.Count == 0) nativeBaseColor = tint;
            nativeCameraColors[camera] = tint;
            Vector3 local = Quaternion.Inverse(transform.rotation) * ((camera.transform.position - transform.position) / config.objectScale);
            tint.a *= DiskOpacity.Evaluate(config, camera, local);
            material.color = tint;
        }
        private void RestoreNativeOpacity(Camera camera)
        {
            Color tint;
            if (!nativeCameraColors.TryGetValue(camera, out tint)) return;
            if (material != null) material.color = tint;
            nativeCameraColors.Remove(camera);
        }

        /// <summary> Release the disk renderer and free up any used data </summary>
        public void ReleaseRenderer()
        {   
            nativeCameraColors.Clear();
            if (renderRoot != null) Debug.Log($"[GalacticDisk] Releasing disk renderer \"{renderRoot.name}\".");
            
            // Disable the entire disk object
            if (renderRoot != null) renderRoot.SetActive(false);
            
            // Dispose of all volumetric resources
            volume?.DisposeResources();
            stableParticles?.DisposeResources();
            
            // Release object, material, and texture
            Release(renderRoot); Release(material); Release(ownedTexture);
            
            // Null everything else for garbage collector
            renderRoot = null; volume = null; stableParticles = null; ps = null; psR = null;
            material = null; ownedTexture = null; particles = null;
        }
        /// <summary> Release the entire disk by destroying the object </summary>
        public static void Release(UnityEngine.Object obj)
        {
            // Do nothing if no object in the first place
            if (obj == null) return;
            Debug.Log($"[GalacticDisk] Releasing disk object \"{obj.name}\".");

            // Destroy object if game is running, do it immediately if it isn't
            if (Application.isPlaying)
                Destroy(obj);
            else
                DestroyImmediate(obj);
        }
        
        /// <summary> Release everything when object is destroyed (renderer releases everything else) </summary>
        private void OnDestroy() { ReleaseRenderer(); }

        /// <summary> Refreshes the disk to make sure its still there </summary>
        public void Update()
        {
            // Make sure disk has config before doing anything
            if (config == null) return;

            // Throttle disk check
            framesSinceCheck++;
            if (framesSinceCheck < checkIntervalFrames) return;
            framesSinceCheck = 0;

            // Try to regenerate the disk if...
            // - Root is gone
            // - It is not active
            // - Uses volumetric rendering but its not enabled/working
            if (renderRoot == null || !renderRoot.activeSelf ||
                (config.renderMode == DiskRenderMode.Volumetric && (volume == null || !volume.IsReady)))
            {
                try
                {
                    GalacticDiskGenerator.RegenerateDisk(gameObject, config);
                }
                catch (Exception ex)
                {
                    Debug.LogError("[GalacticDisk] Renderer recovery failed: " + ex);
                }
                return;
            }

            // Don't do particle system updates if only using volumetric mode
            if (config.renderMode == DiskRenderMode.Volumetric) return;
            if ((stableParticles != null && !stableParticles.IsReady) || (stableParticles == null && psR != null && !psR.enabled))
            {
                GalacticDiskGenerator.RegenerateDisk(gameObject, config);
                return;
            }

            // Regenerate disk if the particle system is gone
            if (ps == null || psR == null)
            {
                GalacticDiskGenerator.RegenerateDisk(gameObject, config);
                return;
            }

            // Update disk if the particles are gone but they should be there
            ParticleSystem.EmissionModule emission = ps.emission;
            if ((config.starCount > 0 && ps.particleCount == 0) || emission.enabled)
                UpdateDisk();
        }

        /// <summary> Updates scale, rotation, and volumetic matrices </summary>
        public void LateUpdate()
        {
            if (config == null) return;
            if (!SyncScenePosition()) return;
            
            if (transform.localScale != Vector3.one) transform.localScale = Vector3.one;
            if (config.renderMode == DiskRenderMode.Particles && renderRoot != null)
            {
                Vector3 scale = Vector3.one * (config.objectScale * config.renderScale);
                if (renderRoot.transform.localScale != scale) renderRoot.transform.localScale = scale;
            }
                
            // Disk rotation logic uses Planetarium.GetUniversalTime() to avoid time warp time issues
            float spinDegrees = 0f;
            if (config.rotationPeriod > 0f)
            {
                double phase = (Planetarium.GetUniversalTime() / config.rotationPeriod) % 1.0;
                spinDegrees = (float)(phase * 360.0);
            }

            Quaternion rotation;
            if (config.alignRotationToOrbit && orbitBody != null && orbitBody.orbit != null)
            {
                // Swap orbit vectors to work with the actual coordinate system
                Vector3 normal = orbitBody.orbit.GetOrbitNormal();
                normal = new Vector3(normal.x, normal.z, normal.y).normalized;

                // Disk particles are in the local XZ plane (Y is the thickness of the disk)
                Quaternion align = Quaternion.FromToRotation(Vector3.up, normal);
                rotation = config.spinAroundDiskAxis ?
                    Quaternion.AngleAxis(config.rotationY, Vector3.up) * align * Quaternion.AngleAxis(spinDegrees, Vector3.up) :
                    align * Quaternion.Euler(0f, config.rotationY + spinDegrees, 0f);
            }
            else
            {
                rotation = config.spinAroundDiskAxis ?
                    Quaternion.Euler(config.rotationX, config.rotationY, config.rotationZ) * Quaternion.AngleAxis(spinDegrees, Vector3.up) :
                    Quaternion.Euler(config.rotationX, config.rotationY + spinDegrees, config.rotationZ);
            }
            
            Quaternion current = transform.rotation;
            if (current.x != rotation.x || current.y != rotation.y || current.z != rotation.z || current.w != rotation.w)
                transform.rotation = rotation;
            // Detailed camera draws update volume matrices. Dot-only views need no proxy transforms.
        }

        /// <summary>
        /// Updates the disk particles.
        /// This doesn't reset the particles and is only really used for restoring the disk after KSP resets it.
        /// </summary>
        public void UpdateDisk()
        {
            // Make sure everything needed can be accessed and uses particles
            if (config == null || config.renderMode != DiskRenderMode.Particles || ps == null || psR == null) return;
           
            // Get modules that are configured later
            ParticleSystem.EmissionModule emission = ps.emission;
            ParticleSystem.MainModule main = ps.main;

            // Set up the particle system
            psR.sharedMaterial = material;
            main.maxParticles = config.starCount;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.loop = false;
            main.playOnAwake = false;
            emission.enabled = false;

            // Fix/update scale because KSP resets it
            transform.localScale = Vector3.one;
            renderRoot.transform.localScale = Vector3.one * (config.objectScale * config.renderScale);

            // Play and instantly pause the particle system to let load but become frozen in place
            ps.Play(true); ps.Pause(true);
            // Set the disk particles (also KSP resets them too)
            ps.SetParticles(particles, particles.Length);
            
            Debug.Log($"[GalacticDisk] Updated {ps.particleCount} stars for disk \"{ps.gameObject.name}\".");
        }
    }
}
