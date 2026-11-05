using KSP.UI.Screens;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GalacticDisk
{
    /// <summary>
    /// Generates every single loaded GalacticDisk from configs in GameDatabase.
    /// This happens in the Space Center as it is the first scene where a disk
    /// can be seen, and it is the first scene where all configs have been loaded
    /// and patched by ModuleManager.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public class GalacticDiskGenerator : MonoBehaviour
    {
        private static GalacticDiskGenerator instance;

        /// <summary>
        /// List of all GalacticDisk disks currently loaded.
        /// </summary>
        internal static Dictionary<string, GameObject> disksByPlanet = new Dictionary<string, GameObject>();
        /// <summary>
        /// List of all GalacticDisk <see cref="GalaxyConfig"/> currently loaded.
        /// </summary>
        private static readonly Dictionary<string, GalaxyConfig> appliedConfigs = new Dictionary<string, GalaxyConfig>();
        /// <summary>
        /// Frames since the last time all GalacticDisks were refreshed.
        /// </summary>
        private int recoveryFrames;

        /// <summary>
        /// <see cref="GalacticDiskToolbar"/> instance for the toolbar window.
        /// </summary>
        private GalacticDiskToolbar toolbar;

        /// <summary>
        /// Toolbar button for the GalacticDisk window.
        /// </summary>
        private ApplicationLauncherButton launcherButton;

        /// <summary>
        /// Path from GameData to the GalacticDisk icon.
        /// </summary>
        private const string IconTexturePath = "GalacticDisk/Icons/toolbar_icon";

        public void Start()
        {
            if (instance != null)
            {
                Debug.Log($"[GalacticDisk] Duplicate instance {GetInstanceID()} in scene {HighLogic.LoadedScene}, destroying it; keeping original {instance.GetInstanceID()}");
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);

            Debug.Log($"[GalacticDisk] Start() running in scene {HighLogic.LoadedScene}, instance id {GetInstanceID()}");

            LoadDefinitions();
            toolbar = new GalacticDiskToolbar();
            GameEvents.onGUIApplicationLauncherReady.Add(AddLauncherButton);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(RemoveLauncherButton);
        }

        internal void LoadDefinitions()
        {
            List<ConfigNode> definitions = new List<ConfigNode>(GameDatabase.Instance.GetConfigNodes("GalacticDiskDefinition"));
            definitions.AddRange(GameDatabase.Instance.GetConfigNodes(RingConfig.NodeName));
            foreach (ConfigNode node in definitions)
            {
                GalaxyConfig config = GalaxyConfig.FromConfigNode(node);

                if (string.IsNullOrEmpty(config.planet))
                {
                    Debug.LogWarning($"[GalacticDisk] GalacticDiskDefinition node missing planet, skipping.");
                    continue;
                }

                CelestialBody cBody = FlightGlobals.GetBodyByName(config.planet);

                if (cBody?.scaledBody == null)
                {
                    Debug.LogWarning("[GalacticDisk] Planet '" + config.planet + "' has no scaled body yet, not initializing disk.");
                    continue;
                }

                GameObject go = new GameObject
                {
                    name = "GalacticDisk",
                    layer = 10
                };
                go.transform.parent = cBody.scaledBody.transform;
                go.transform.localPosition = Vector3.zero;
                try
                {
                    ProcessObject(go, config);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[GalacticDisk] Failed to process object \"{go.name}\": {ex}");
                    Destroy(go);
                    continue;
                }

                disksByPlanet[config.planet] = go; // Remember for UI updates
                appliedConfigs[config.planet] = config;
            }

        }

        public void OnDestroy()
        {
            Debug.Log($"[GalacticDisk] OnDestroy() called on instance id {GetInstanceID()}, scene {HighLogic.LoadedScene}");

            if (instance == this)
                instance = null;

            GameEvents.onGUIApplicationLauncherReady.Remove(AddLauncherButton);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(RemoveLauncherButton);
            RemoveLauncherButton();
        }

        void AddLauncherButton()
        {
            Debug.Log($"[GalacticDisk] AddLauncherButton() fired in scene {HighLogic.LoadedScene}, launcherButton already set = {launcherButton != null}, window visible = {toolbar?.IsVisible}");

            if (launcherButton != null) return;

            // Load the toolbar texture or fall back to a generated dot
            Texture2D icon = GameDatabase.Instance.GetTexture(IconTexturePath, false) ?? CreateDefaultParticleTexture(38);

            // Create the launcher button
            launcherButton = ApplicationLauncher.Instance.AddModApplication(
                onTrue: () => toolbar.SetVisible(true),
                onFalse: () => toolbar.SetVisible(false),
                onHover: null,
                onHoverOut: null,
                onEnable: null,
                onDisable: null,
                visibleInScenes: ApplicationLauncher.AppScenes.ALWAYS,
                texture: icon);

            // Make sure the toolbar button is in the correct state
            if (toolbar.IsVisible)
                launcherButton.SetTrue(false);
            else
                launcherButton.SetFalse(false);

            Debug.Log("[GalacticDisk] Launcher button created.");
        }

        void RemoveLauncherButton()
        {
            Debug.Log($"[GalacticDisk] RemoveLauncherButton() fired in scene {HighLogic.LoadedScene}, had button = {launcherButton != null}");

            if (launcherButton == null) return;

            try
            {
                ApplicationLauncher.Instance?.RemoveModApplication(launcherButton);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GalacticDisk] Failed to remove launcher button: {ex}");
            }
            finally
            {
                launcherButton = null;
            }
        }

        public void OnGUI()
        {
            toolbar?.OnClick();
        }

        public void Update()
        {
            if (++recoveryFrames < 60) return;
            recoveryFrames = 0;
            if (HighLogic.LoadedScene != GameScenes.SPACECENTER &&
                HighLogic.LoadedScene != GameScenes.FLIGHT &&
                HighLogic.LoadedScene != GameScenes.TRACKSTATION) return;
            foreach (KeyValuePair<string, GalaxyConfig> entry
                in new List<KeyValuePair<string, GalaxyConfig>>(appliedConfigs))
            {
                if (disksByPlanet.TryGetValue(entry.Key, out GameObject disk) && disk != null) continue;
                CelestialBody body = FlightGlobals.GetBodyByName(entry.Key);
                if (body?.scaledBody == null) continue;
                try
                {
                    ApplyDisk(body, entry.Value);
                }
                catch (Exception ex)
                {
                    Debug.LogError("[GalacticDisk] Scene recovery failed: " + ex);
                }
            }
        }

        public void ProcessObject(GameObject go, GalaxyConfig config)
        {
            RegenerateDisk(go, config);
        }

        /// <summary>
        /// Regenerates a GalacticDisk of the provided <see cref="GameObject"/> with the provided <see cref="GalaxyConfig"/>.
        /// If this fails no changes are applied and the original disk is left the same.
        /// </summary>
        internal static void RegenerateDisk(GameObject go, GalaxyConfig config)
        {
            if (config == null ||
                config.starCount < 0 || config.starCount > 2000000 ||
                !IsPositiveFinite(config.galaxyRadius) ||
                !IsPositiveFinite(config.objectScale) ||
                !IsPositiveFinite(config.renderScale) ||
                !IsPositiveFinite(config.objectScale * config.renderScale))
                throw new ArgumentException("Radius/scales must be finite and positive, starCount must be between 0 and 2000000, and/or config is null.");
            if (config.isRing) VolumeConfig.SettingsFor(config); // Validate before replacing a working renderer
            GameObject staged = new GameObject("GalacticDisk Renderer")
            {
                layer = 10
            };
            go.transform.localScale = Vector3.one;
            staged.transform.SetParent(go.transform, false);
            Material material = null;
            Texture2D texture = null;
            VolumetricDiskRenderer volume = null;
            try
            {
                ParticleSystem ps = null;
                ParticleSystemRenderer renderer = null;
                ParticleSystem.Particle[] particles = null;
                if (config.renderMode == DiskRenderMode.Volumetric)
                {
                    volume = staged.AddComponent<VolumetricDiskRenderer>();
                    volume.Initialize(config);
                }
                else
                {
                    // Unity's default Local mode ignores ancestor scale. Keep the
                    // original emitter's scale on the emitter itself, not its parent.
                    staged.transform.localScale = Vector3.one * (config.objectScale * config.renderScale);
                    ps = staged.AddComponent<ParticleSystem>();
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ParticleSystem.MainModule main = ps.main;
                    main.maxParticles = Mathf.Max(1, config.starCount);
                    main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                    main.simulationSpace = ParticleSystemSimulationSpace.Local;
                    main.scalingMode = ParticleSystemScalingMode.Local;
                    main.loop = false; main.playOnAwake = false; main.startSpeed = 0f;
                    ParticleSystem.EmissionModule emission = ps.emission; emission.enabled = false;
                    ParticleSystem.ShapeModule shape = ps.shape; shape.enabled = false;
                    texture = CreateDefaultParticleTexture();
                    if (config.usesTexture && File.Exists(config.texturePath)) texture.LoadImage(File.ReadAllBytes(config.texturePath));
                    else if (config.usesTexture) Debug.LogWarning("[GalacticDisk] Particle texture not found: " + config.texturePath);
                    material = new Material(Shader.Find("Sprites/Default"))
                    {
                        mainTexture = texture
                    };
                    if (config.isRing)
                    {
                        float brightness = VolumeConfig.SettingsFor(config).starBrightness;
                        material.color = new Color(brightness, brightness, brightness, 1f);
                    }
                    renderer = ps.GetComponent<ParticleSystemRenderer>();
                    renderer.sharedMaterial = material;
                    ps.Simulate(0f, true, true); ps.Play(true); ps.Pause(true);
                    particles = GenerateGalaxy(ps, config);
                }
                GalacticDiskObject owner = go.GetComponent<GalacticDiskObject>() ?? go.AddComponent<GalacticDiskObject>();
                owner.ReleaseRenderer();
                owner.config = config;
                owner.orbitBody = FlightGlobals.GetBodyByName(config.planet);
                owner.renderRoot = staged; owner.volume = volume;
                owner.ps = ps; owner.psR = renderer; owner.particles = particles;
                owner.material = material; owner.ownedTexture = texture;
                go.transform.localScale = Vector3.one;
                volume?.UpdateMatrices();
                Debug.Log("[GalacticDisk] Applied " + config.renderMode + " renderer to " + config.planet +
                    " (" + config.starCount + " stars; objectScale=" + config.objectScale + "; renderScale=" + config.renderScale + ").");
            }
            catch
            {
                staged.SetActive(false);
                volume?.DisposeResources();
                GalacticDiskObject.Release(staged); GalacticDiskObject.Release(material); GalacticDiskObject.Release(texture);
                throw;
            }
        }

        private static bool IsPositiveFinite(float value) { return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value); }

        // Reads back the config currently applied to a body's disk, if any — used
        // by the toolbar's "Load" button so editing an existing disk doesn't
        // require re-typing every field from scratch.
        internal static GalaxyConfig GetConfig(string planetName)
        {
            if (string.IsNullOrEmpty(planetName)) return null;
            if (!disksByPlanet.TryGetValue(planetName, out GameObject go) || go == null) return null;

            return go.GetComponent<GalacticDiskObject>()?.config;
        }

        // Creates a disk on the given body if none exists yet, or regenerates the
        // existing one IN PLACE with the new config otherwise — this replaces the
        // old separate "temporary preview object" flow entirely.
        internal static void ApplyDisk(CelestialBody body, GalaxyConfig config)
        {
            if (body?.scaledBody == null || instance == null) return;

            config.planet = body.name;

            if (disksByPlanet.TryGetValue(body.name, out GameObject existing) && existing != null)
            {
                RegenerateDisk(existing, config);
                appliedConfigs[body.name] = config;
                return;
            }

            GameObject go = new GameObject("GalacticDisk")
            {
                layer = 10
            };
            go.transform.parent = body.scaledBody.transform;
            go.transform.localPosition = Vector3.zero;
            try
            {
                instance.ProcessObject(go, config);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GalacticDisk] Failed to process object \"{go.name}\": {ex}");
                Destroy(go);
                throw;
            }

            disksByPlanet[body.name] = go;
            appliedConfigs[body.name] = config;
        }

        internal static void RemoveDisk(string planetName)
        {
            if (string.IsNullOrEmpty(planetName)) return;
            appliedConfigs.Remove(planetName);

            if (disksByPlanet.TryGetValue(planetName, out GameObject go))
            {
                if (go != null) GalacticDiskObject.Release(go);
                disksByPlanet.Remove(planetName);
            }
        }

        // Builds a brand new, untracked disk GameObject parented to the given body's
        // scaled space transform, for previewing a config without touching disksByPlanet.
        internal static GameObject CreatePreviewDisk(CelestialBody body, GalaxyConfig config)
        {
            if (body?.scaledBody == null) return null;

            GameObject go = new GameObject("GalacticDisk_Preview")
            {
                layer = 10
            };
            go.transform.parent = body.scaledBody.transform;
            go.transform.localPosition = Vector3.zero;
            RegenerateDisk(go, config);

            return go;
        }

        static ParticleSystem.Particle[] GenerateGalaxy(ParticleSystem ps, GalaxyConfig c)
        {
            ParticleSystem.Particle[] particles = new ParticleSystem.Particle[c.starCount];
            if (c.isRing)
            {
                GalaxyStar[] ring = GalaxyStarGenerator.Generate(VolumeConfig.SettingsFor(c), null);
                for (int i = 0; i < ring.Length; i++)
                {
                    particles[i].position = ring[i].position / c.renderScale;
                    particles[i].startSize = ring[i].size / c.renderScale;
                    particles[i].startColor = ring[i].color;
                    particles[i].remainingLifetime = particles[i].startLifetime = float.MaxValue;
                }
                ps.SetParticles(particles, particles.Length);
                return particles;
            }

            // Particle positions/sizes are divided by renderScale so that once the
            // transform's localScale (objectScale * renderScale) is applied on top,
            // the disk still visually reads as objectScale in size.
            float invRenderScale = 1f / c.renderScale;

            for (int i = 0; i < c.starCount; i++)
            {
                bool isInterarm = false;
                bool isBulge = UnityEngine.Random.value < c.bulgeFraction;
                Vector3 pos = isBulge ? GenerateBulgeStar(c) : GenerateDiskStar(c, out isInterarm);
                bool isArmStar = !isBulge && !isInterarm;

                float radius = new Vector2(pos.x, pos.z).magnitude;
                float t = Mathf.Clamp01(radius / c.galaxyRadius);

                particles[i].position = pos * invRenderScale;

                float size =
                    Mathf.Lerp(0.09f, 0.025f, t)
                    * UnityEngine.Random.Range(0.8f, 1.2f);

                size = Mathf.Clamp(size, c.minStarSize, c.maxStarSize);
                particles[i].startSize = size * invRenderScale;

                particles[i].startColor = RandomStarColor(c, isBulge, isArmStar);

                particles[i].remainingLifetime = float.MaxValue;
                particles[i].startLifetime = float.MaxValue;
            }

            ps.SetParticles(particles, particles.Length);

            return particles;
        }

        // Re-declared here because GenerateDiskStar needs an out param and the
        // interarm blend happens where color is computed, below.
        static Vector3 GenerateBulgeStar(GalaxyConfig c)
        {
            // Each axis is an independent Gaussian, so the bulge is a smooth,
            // no-hard-edge ellipsoid whose extent on each axis is controlled
            // separately by bulgeScaleX/Y/Z.
            float x = Gaussian(c.bulgeScaleX);
            float y = Gaussian(c.bulgeScaleY);
            float z = Gaussian(c.bulgeScaleZ);

            return new Vector3(x, y, z);
        }

        static Vector3 GenerateDiskStar(GalaxyConfig c, out bool isInterarm)
        {
            isInterarm = false;
            float scaleLength = c.galaxyRadius * c.diskScaleLengthFraction;

            float r;
            while (true)
            {
                r = -scaleLength * Mathf.Log(1f - UnityEngine.Random.value);
                if (r <= c.galaxyRadius)
                    break;
            }

            float angle;

            if (c.armCount <= 0 || UnityEngine.Random.value < c.interarmFraction)
            {
                isInterarm = true;
                angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            }
            else
            {
                int arm = UnityEngine.Random.Range(0, c.armCount);

                angle = arm * Mathf.PI * 2f / c.armCount;
                angle += r / c.galaxyRadius * c.armTightness * Mathf.PI * 2f;

                float sigma = c.armWidth * Mathf.Lerp(0.6f, 0.05f, c.armConcentration);
                float armOffset = Gaussian(sigma);
                armOffset = Mathf.Clamp(armOffset, -c.armWidth, c.armWidth);
                angle += armOffset;
            }

            r += UnityEngine.Random.Range(-2f, 2f);

            float x = Mathf.Cos(angle) * r;
            float z = Mathf.Sin(angle) * r;
            float y = Gaussian(c.diskThickness * 0.22f);

            return new Vector3(x, y, z);
        }

        static float Gaussian(float sigma)
        {
            float u1 = Mathf.Max(UnityEngine.Random.value, 0.0001f);
            float u2 = UnityEngine.Random.value;

            float rand =
                Mathf.Sqrt(-2f * Mathf.Log(u1))
                * Mathf.Cos(2f * Mathf.PI * u2);

            return rand * sigma;
        }

        static Color RandomStarColor(GalaxyConfig c, bool isBulge, bool isArmStar)
        {
            Color color = isBulge ? c.colorBulge : (isArmStar ? c.colorArms : c.colorInterarm);

            if (isArmStar && c.armHighlightChance > 0f && UnityEngine.Random.value < c.armHighlightChance)
                color = c.armHighlightColor;

            color.r += UnityEngine.Random.Range(-0.03f, 0.03f);
            color.g += UnityEngine.Random.Range(-0.03f, 0.03f);
            color.b += UnityEngine.Random.Range(-0.03f, 0.03f);

            color.r = Mathf.Clamp01(color.r);
            color.g = Mathf.Clamp01(color.g);
            color.b = Mathf.Clamp01(color.b);

            return color;
        }

        static Texture2D CreateDefaultParticleTexture(int size = 64)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - size / 2f;
                    float dy = y - size / 2f;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy) / (size / 2f);

                    float alpha = Mathf.Clamp01(1f - dist);

                    tex.SetPixel(x, y, new Color(1, 1, 1, alpha));
                }
            }

            tex.Apply();
            return tex;
        }
    }
}
