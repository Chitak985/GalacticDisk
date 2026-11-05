using System;
using UnityEngine;

namespace GalacticDisk
{
    /// <summary>
    /// This <see cref="MonoBehaviour"/> is attached to <see cref="GameObject"/>s that are created for GalacticDisk objects.
    /// </summary>
    [DefaultExecutionOrder(32000)]
    public class GalacticDiskObject : MonoBehaviour
    {
        public ParticleSystem.Particle[] particles;
        public ParticleSystem ps;
        public ParticleSystemRenderer psR;
        public Material material;
        public GalaxyConfig config;
        public GameObject renderRoot;
        public VolumetricDiskRenderer volume;
        public Texture2D ownedTexture;

        public void ReleaseRenderer()
        {
            renderRoot?.SetActive(false);
            volume?.DisposeResources();
            Release(renderRoot); Release(material); Release(ownedTexture);
            renderRoot = null; volume = null; ps = null; psR = null;
            material = null; ownedTexture = null; particles = null;
        }
        public static void Release(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj);
        }
        private void OnDestroy() { ReleaseRenderer(); }

        // The CelestialBody the disk is centered on — its orbit (around whatever
        // referenceBody its Orbit node names) defines the plane to align to when
        // config.alignRotationToOrbit is set.
        public CelestialBody orbitBody;

        // Checking for a KSP-triggered particle clear every single frame, forever,
        // is wasted work once a disk has been sitting fully spawned for a while —
        // across several million-particle disks that adds up. Throttle the check
        // instead of skipping it entirely, since KSP can still clear particles
        // later (e.g. on some scene transitions) and this needs to catch that.
        private int framesSinceCheck = 0;
        private const int checkIntervalFrames = 30; // ~2x/sec at 60fps

        public void Update()
        {
            if (config == null) return;
            framesSinceCheck++;
            if (framesSinceCheck < checkIntervalFrames) return;
            framesSinceCheck = 0;
            if (renderRoot == null || !renderRoot.activeSelf ||
                (config.renderMode == DiskRenderMode.Volumetric && (volume == null || !volume.IsReady)))
            {
                try { GalacticDiskGenerator.RegenerateDisk(gameObject, config); }
                catch (Exception ex) { Debug.LogError("[GalacticDisk] Renderer recovery failed: " + ex); }
                return;
            }
            if (config.renderMode == DiskRenderMode.Volumetric) return;
            if (ps == null || psR == null) { GalacticDiskGenerator.RegenerateDisk(gameObject, config); return; }
            ParticleSystem.EmissionModule emission = ps.emission;
            if ((config.starCount > 0 && ps.particleCount == 0) || emission.enabled) UpdateDisk();
        }

        // Runs after Update() on every component this frame, so it overrides
        // whatever rotation the disk picked up by inheriting from its parent
        // scaled-space body this frame (the parent's own spin is presumably
        // applied in its own Update()). Setting transform.rotation (not
        // localRotation) sets WORLD rotation directly, ignoring the parent's
        // current rotation entirely — so this works regardless of how fast, or
        // around which axis, the parent body happens to be spinning.
        public void LateUpdate()
        {
            if (config == null) return;
            transform.localScale = Vector3.one;
            if (config.renderMode == DiskRenderMode.Particles && renderRoot != null)
                renderRoot.transform.localScale = Vector3.one * (config.objectScale * config.renderScale);
            // Optional slow self-rotation. Using Planetarium.GetUniversalTime()
            // (KSP's authoritative simulated clock, already correctly scaled for
            // timewarp/pausing) rather than accumulating Time.deltaTime each frame
            // means this is computed fresh from absolute time every call — there's
            // nothing to drift or to desync at high warp, and no dependency on
            // frame rate.
            float spinDegrees = 0f;
            if (config.rotationPeriod > 0f)
            {
                double phase = (Planetarium.GetUniversalTime() / config.rotationPeriod) % 1.0;
                spinDegrees = (float)(phase * 360.0);
            }

            if (config.alignRotationToOrbit && orbitBody != null && orbitBody.orbit != null)
            {
                // KSP's Orbit class computes its vectors (normal, position, velocity)
                // in a coordinate frame where Unity's Y and Z axes are swapped
                // relative to Transform/world space — a known gotcha when mixing
                // Orbit math with Unity Transforms directly. Swapping here is what
                // makes the computed plane match the orbit actually drawn in-game,
                // instead of the mismatched planes seen when guessing plain XYZ
                // Euler angles from the raw orbital elements.
                Vector3 normal = orbitBody.orbit.GetOrbitNormal();
                normal = new Vector3(normal.x, normal.z, normal.y).normalized;

                // Disk particles are laid out flat in the local XZ plane (Y is the
                // disk's "thin" axis), so align local up to the orbit normal, then
                // apply rotationY (+ any rotationPeriod spin) as rotation around
                // that normal (which arm points where — doesn't affect the plane).
                Quaternion align = Quaternion.FromToRotation(Vector3.up, normal);
                transform.rotation = config.spinAroundDiskAxis ?
                    Quaternion.AngleAxis(config.rotationY, Vector3.up) * align * Quaternion.AngleAxis(spinDegrees, Vector3.up) :
                    align * Quaternion.Euler(0f, config.rotationY + spinDegrees, 0f);
            }
            else
            {
                transform.rotation = config.spinAroundDiskAxis ?
                    Quaternion.Euler(config.rotationX, config.rotationY, config.rotationZ) * Quaternion.AngleAxis(spinDegrees, Vector3.up) :
                    Quaternion.Euler(config.rotationX, config.rotationY + spinDegrees, config.rotationZ);
            }
            volume?.UpdateMatrices();
        }

        public void UpdateDisk()  // It is not recommended to call this since it doesn't reset the particles, better change values and reset the particle system
        {
            if (config == null || config.renderMode != DiskRenderMode.Particles || ps == null || psR == null) return;
            ParticleSystem.EmissionModule emission = ps.emission;
            ParticleSystem.MainModule main = ps.main;

            psR.sharedMaterial = material;
            main.maxParticles = config.starCount;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.loop = false;
            main.playOnAwake = false;
            emission.enabled = false;

            // KSP appears to reset the transform scale along with clearing the
            // particle system, so it's reapplied here alongside the particles.
            transform.localScale = Vector3.one;
            renderRoot.transform.localScale = Vector3.one * (config.objectScale * config.renderScale);

            ps.Play(true); ps.Pause(true);
            ps.SetParticles(particles, particles.Length);
            Debug.Log($"[GalacticDisk] Re-generated {ps.particleCount} stars at \"{ps.gameObject.name}\".");
        }
    }
}
