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

        /// <summary> List of all GalacticDisk disks currently loaded. </summary>
        internal static Dictionary<string, GameObject> disksByPlanet = new Dictionary<string, GameObject>();
        /// <summary> List of all GalacticDisk <see cref="GalaxyConfig"/> currently loaded. </summary>
        private static readonly Dictionary<string, GalaxyConfig> appliedConfigs = new Dictionary<string, GalaxyConfig>();

        /// <summary> <see cref="GalacticDiskToolbar"/> instance for the toolbar window. </summary>
        private GalacticDiskToolbar toolbar;

        /// <summary> Toolbar button for the GalacticDisk window. </summary>
        private ApplicationLauncherButton launcherButton;

        /// <summary> Path from GameData to the GalacticDisk icon. </summary>
        private const string IconTexturePath = "GalacticDisk/Icons/toolbar_icon";

        public void Start()
        {
            // Check for duplicate instances
            if (instance != null)
            {
                Debug.Log($"[GalacticDisk] Duplicate instance {GetInstanceID()} in scene {HighLogic.LoadedScene}, self-destructing. Other instance is {instance.GetInstanceID()}");
                Destroy(gameObject);
                return;
            }

            // Set up singleton instance
            instance = this;
            DontDestroyOnLoad(gameObject);
            // Log singleton setup
            Debug.Log($"[GalacticDisk] Start() running in scene {HighLogic.LoadedScene}, instance id {GetInstanceID()}");

            // Load disks
            LoadDefinitions();
            
            // Load GUI and set it up
            toolbar = new GalacticDiskToolbar();
            GameEvents.onGUIApplicationLauncherReady.Add(AddLauncherButton);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(RemoveLauncherButton);
        }

        /// <summary> Load all disk definitions </summary>
        internal void LoadDefinitions()
        {
            var loadTimer = System.Diagnostics.Stopwatch.StartNew();
            int initialBuilds = GalaxyCloudCache.BuildCount, initialHits = GalaxyCloudCache.HitCount, loaded = 0;
            // List of definition config nodes
            List<ConfigNode> definitions = new List<ConfigNode>(GameDatabase.Instance.GetConfigNodes("GalacticDiskDefinition"));
            definitions.AddRange(GameDatabase.Instance.GetConfigNodes(RingConfig.NodeName));

            // Load each config node
            foreach (ConfigNode node in definitions)
            {
                string parent = node.GetValue("planet");
                bool.TryParse(node.GetValue("silenceNoBodyError"), out bool silentMissingBody);

                // Make sure disk has body
                if (string.IsNullOrEmpty(parent))
                {
                    if (!silentMissingBody) Debug.LogWarning("[GalacticDisk] " + node.name + " node missing planet, skipping.");
                    continue;
                }

                // Make sure the body exists
                CelestialBody cBody = FlightGlobals.GetBodyByName(parent);
                if (cBody?.scaledBody == null)
                {
                    if (!silentMissingBody) Debug.LogWarning("[GalacticDisk] Planet '" + parent + "' has no scaled body yet, not initializing disk.");
                    continue;
                }

                GalaxyConfig config = GalaxyConfig.FromConfigNode(node);

                // Create the GalacticDisk disk object
                GameObject go = new GameObject
                {
                    name = "GalacticDisk",
                    layer = 10
                };
                go.transform.SetParent(DiskSceneCache.Parent, false);
                go.transform.position = cBody.scaledBody.transform.position;

                // Process disk
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

                // Add disk to list of loaded disks
                disksByPlanet[config.planet] = go; // Remember for UI updates
                appliedConfigs[config.planet] = config;
                loaded++;
            }
            Debug.Log("[GalacticDisk] Loaded " + loaded + " disks in " + loadTimer.Elapsed.TotalSeconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) +
                " s; generated " + (GalaxyCloudCache.BuildCount - initialBuilds) + " cloud fields, reused " + (GalaxyCloudCache.HitCount - initialHits) +
                "; active cloud textures: " + (GalaxyCloudCache.TextureBytes / (1024.0 * 1024.0)).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " MiB.");
        }

        public void OnDestroy()
        {
            // Log singleton destruction
            Debug.Log($"[GalacticDisk] OnDestroy() called on instance id {GetInstanceID()}, scene {HighLogic.LoadedScene}");

            // Remove singleton instance
            if (instance == this)
            {
                instance = null; DiskSceneCache.Clear();
                SingularityCompatibility.Clear();
                disksByPlanet.Clear(); appliedConfigs.Clear();
            }

            // Remove GUI
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

            // Remove the launcher button
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
            DiskSceneCache.Tick();
            SingularityCompatibility.Tick();
        }

        public void ProcessObject(GameObject go, GalaxyConfig config)
        {
            RegenerateDisk(go, config);
            CelestialBody body = FlightGlobals.GetBodyByName(config.planet);
            if (body != null && body.scaledBody != null) DiskSceneCache.Register(go, body);
        }

        internal static void RememberCachedDisk(GameObject go, GalaxyConfig config)
        {
            disksByPlanet[config.planet] = go; appliedConfigs[config.planet] = config;
        }

        /// <summary>
        /// Regenerates a GalacticDisk of the provided <see cref="GameObject"/> with the provided <see cref="GalaxyConfig"/>.
        /// If this fails nothing happens and the original disk stays the same.
        /// </summary>
        internal static void RegenerateDisk(GameObject go, GalaxyConfig config)
        {
            // Verify config values
            if (config == null ||
                config.starCount < 0 || config.starCount > 2000000 ||
                !IsPositiveFinite(config.galaxyRadius) ||
                !IsPositiveFinite(config.objectScale) ||
                !IsPositiveFinite(config.renderScale) ||
                !IsPositiveFinite(config.objectScale * config.renderScale))
                throw new ArgumentException("Radius/scales must be finite and positive, starCount must be between 0 and 2000000, and/or config is null.");

            // Validate before replacing a working renderer
            DiskOpacity.Validate(config);
            DistantGalaxy.Validate(config);
            if (config.isRing) VolumeConfig.SettingsFor(config);

            // Set up renderer GameObject
            GameObject staged = new GameObject("GalacticDisk Renderer")
            {
                layer = 10
            };
            go.transform.localScale = Vector3.one;
            staged.transform.SetParent(go.transform, false);

            // Clear resources
            Material material = null;
            Texture2D texture = null;
            VolumetricDiskRenderer volume = null;
            StableParticleRenderer stableParticles = null;
            try
            {
                // Clear particle system values
                ParticleSystem ps = null;
                ParticleSystemRenderer renderer = null;
                ParticleSystem.Particle[] particles = null;
                
                // Set up disk
                if (config.renderMode == DiskRenderMode.Volumetric)  // Set up volumetric disk
                {
                    volume = staged.AddComponent<VolumetricDiskRenderer>();
                    volume.Initialize(config);
                }
                else  // Set up particle disk
                {
                    // Apply scale locally to let KSP control the parent scale
                    staged.transform.localScale = Vector3.one * (config.objectScale * config.renderScale);

                    // Set up particle system
                    ps = staged.AddComponent<ParticleSystem>();
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ParticleSystem.MainModule main = ps.main;
                    main.maxParticles = Mathf.Max(1, config.starCount);
                    main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                    main.simulationSpace = ParticleSystemSimulationSpace.Local;
                    main.scalingMode = ParticleSystemScalingMode.Local;
                    main.loop = false;
                    main.playOnAwake = false;
                    main.startSpeed = 0f;
                    ParticleSystem.EmissionModule emission = ps.emission;
                    emission.enabled = false;
                    ParticleSystem.ShapeModule shape = ps.shape;
                    shape.enabled = false;

                    // Apply texture if there is one and it exists
                    texture = CreateDefaultParticleTexture();
                    if (config.usesTexture && File.Exists(config.texturePath))
                        texture.LoadImage(File.ReadAllBytes(config.texturePath));
                    else if (config.usesTexture)
                        Debug.LogWarning("[GalacticDisk] Particle texture not found: " + config.texturePath);

                    // Create particle system material
                    material = CreateParticleMaterial(texture);

                    // Adjust everything if disk is a ring
                    if (config.isRing)
                    {
                        float brightness = VolumeConfig.SettingsFor(config).starBrightness;
                        material.color = new Color(brightness, brightness, brightness, 1f);
                    }

                    // Set up particle system renderer
                    renderer = ps.GetComponent<ParticleSystemRenderer>();
                    renderer.sharedMaterial = material;
                    ps.Simulate(0f, true, true);
                    ps.Play(true);
                    ps.Pause(true);

                    // Generate and add particles
                    particles = GenerateGalaxy(ps, config);
                    stableParticles = staged.AddComponent<StableParticleRenderer>();
                    try { stableParticles.Initialize(config, particles, texture, material.color, renderer); }
                    catch (Exception error) when (error is System.IO.FileNotFoundException || error is InvalidOperationException)
                    {
                        stableParticles.DisposeResources();
                        GalacticDiskObject.Release(stableParticles); stableParticles = null;
                        renderer.enabled = true;
                        Debug.LogWarning("[GalacticDisk] Using native particle drawing because the stable particle shader is unavailable: " + error.Message);
                    }
                }
                
                // Set up GalacticDisk object
                GalacticDiskObject owner = go.GetComponent<GalacticDiskObject>() ?? go.AddComponent<GalacticDiskObject>();
                owner.ReleaseRenderer();
                owner.config = config;
                owner.orbitBody = FlightGlobals.GetBodyByName(config.planet);
                owner.renderRoot = staged;
                owner.volume = volume;
                owner.stableParticles = stableParticles;
                owner.ps = ps;
                owner.psR = renderer;
                owner.particles = particles;
                owner.material = material;
                owner.ownedTexture = texture;
                go.transform.localScale = Vector3.one;

                // Update volumetric renderer if it is used
                volume?.UpdateMatrices();

                // Log creation
                Debug.Log($"[GalacticDisk] Applied {config.renderMode} disk renderer to {config.planet} with {config.starCount} stars.");
            }
            catch
            {
                // Dispose of everything if creation fails
                staged.SetActive(false);
                volume?.DisposeResources();
                stableParticles?.DisposeResources();
                GalacticDiskObject.Release(staged);
                GalacticDiskObject.Release(material);
                GalacticDiskObject.Release(texture);
                throw;
            }
        }

        private static bool IsPositiveFinite(float value)
            => value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);

        /// <summary> Read the loaded disk config for a body, used by Load in GUI </summary>
        internal static GalaxyConfig GetConfig(string planetName)
        {
            if (string.IsNullOrEmpty(planetName)) return null;
            if (!disksByPlanet.TryGetValue(planetName, out GameObject go) || go == null) return null;

            return go.GetComponent<GalacticDiskObject>()?.config;
        }

        /// <summary> Create or update disk at body with config, used by Apply in GUI </summary>
        internal static void ApplyDisk(CelestialBody body, GalaxyConfig config)
        {
            if (body?.scaledBody == null || instance == null) return;

            config.planet = body.name;

            if (disksByPlanet.TryGetValue(body.name, out GameObject existing) && existing != null)
            {
                RegenerateDisk(existing, config);
                DiskSceneCache.Register(existing, body);
                appliedConfigs[body.name] = config;
                return;
            }

            GameObject go = new GameObject("GalacticDisk")
            {
                layer = 10
            };
            go.transform.SetParent(DiskSceneCache.Parent, false);
            go.transform.position = body.scaledBody.transform.position;
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

        /// <summary> Remove the loaded disk from a body, used by Remove all disks in GUI </summary>
        internal static void RemoveDisk(string planetName)
        {
            if (string.IsNullOrEmpty(planetName)) return;
            appliedConfigs.Remove(planetName);
            int removed = DiskSceneCache.RemoveBody(planetName);

            if (disksByPlanet.TryGetValue(planetName, out GameObject go))
            {
                if (removed == 0 && go != null) GalacticDiskObject.Release(go);
                disksByPlanet.Remove(planetName);
            }
        }

        /// <summary> Generate particles for a particle system with the provided <see cref="GalaxyConfig"/> </summary>
        public static ParticleSystem.Particle[] GenerateGalaxy(ParticleSystem ps, GalaxyConfig c)
        {
            // Particles to output
            ParticleSystem.Particle[] particles = new ParticleSystem.Particle[c.starCount];
            
            // Handle ring disk
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

            // Divided by renderScale so it doesn't affect objectScale
            float invRenderScale = 1f / c.renderScale;

            // Generate all of the stars
            for (int i = 0; i < c.starCount; i++)
            {
                bool isInterarm = false;
                bool isBulge = UnityEngine.Random.value < c.bulgeFraction;

                // Get position (and if star is interarm)
                Vector3 pos = isBulge ? GenerateBulgeStar(c) : GenerateDiskStar(c, out isInterarm);
                particles[i].position = pos * invRenderScale;

                // Get star size
                particles[i].startSize = Mathf.Clamp(
                                            Mathf.Lerp(0.09f, 0.025f, Mathf.Clamp01(new Vector2(pos.x, pos.z).magnitude / c.galaxyRadius))
                                                * UnityEngine.Random.Range(0.8f, 1.2f),
                                            c.minStarSize,
                                            c.maxStarSize
                                         ) * invRenderScale;

                // Get color
                particles[i].startColor = RandomStarColor(c, isBulge, !isBulge && !isInterarm);

                // Keep particle existing forever
                particles[i].remainingLifetime = float.MaxValue;
                particles[i].startLifetime = float.MaxValue;
            }

            ps.SetParticles(particles, particles.Length);
            return particles;
        }
        
        /// <summary> Generates a bulge where each axis is an independent Gaussian, making it smooth </summary>
        public static Vector3 GenerateBulgeStar(GalaxyConfig c)
            => new Vector3(Gaussian(c.bulgeScaleX), Gaussian(c.bulgeScaleY), Gaussian(c.bulgeScaleZ));

        /// <summary> Generate a disk star's position </summary>
        public static Vector3 GenerateDiskStar(GalaxyConfig c, out bool isInterarm)
        {
            isInterarm = false;

            float r;
            while (true)
            {
                r = -(c.galaxyRadius * c.diskScaleLengthFraction) * Mathf.Log(1f - UnityEngine.Random.value);
                if (r <= c.galaxyRadius) break;
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

        /// <summary> Get the random star color used by a star depending on where it is in the disk </summary>
        public static Color RandomStarColor(GalaxyConfig c, bool isBulge, bool isArmStar)
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

        // Keep Shader.Find outside the renderer's exception filters so Shabby's
        // shader replacement transpiler can patch a simple method body.
        private static Material CreateParticleMaterial(Texture2D texture)
        {
            return new Material(Shader.Find("Sprites/Default")) { mainTexture = texture };
        }

        /// <summary> Create the default particle texture, a simple orb </summary>
        public static Texture2D CreateDefaultParticleTexture(int size = 64)
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

        /// <summary> Get Gaussian distribution for a sigma </summary>
        public static float Gaussian(float sigma)
        {
            float u1 = Mathf.Max(UnityEngine.Random.value, 0.0001f);
            float u2 = UnityEngine.Random.value;

            float rand =
                Mathf.Sqrt(-2f * Mathf.Log(u1))
                * Mathf.Cos(2f * Mathf.PI * u2);

            return rand * sigma;
        }
    }
}
