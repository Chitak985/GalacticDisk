using KSP.UI.Screens;
using System;
using System.Collections.Generic;
using System.Globalization;
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
        // Manual persistence: KSPAddon's built-in "once" flag did not reliably survive
        // scene unloads in testing (OnDestroy still fired switching scenes), so this
        // instance is kept alive by hand via DontDestroyOnLoad below. Whenever KSP tries
        // to spin up a second copy on a later SpaceCentre load, it self-destructs in
        // Start() instead, leaving the original (and its toolbar/button state) in place.
        private static GalacticDiskGenerator instance;

        // Keep track of all disks we created so the UI can find them later
        internal static Dictionary<string, GameObject> disksByPlanet = new Dictionary<string, GameObject>();

        // The toolbar UI instance, drawn from OnGUI when the app-launcher button is toggled on.
        private GalacticDiskToolbar toolbar;

        // The stock toolbar (ApplicationLauncher) button and its icon.
        private ApplicationLauncherButton launcherButton;

        // Path (relative to GameData) to the icon texture. Swap this to your own
        // 38x38 (or 36x36) PNG once you have one — see AddLauncherButton below.
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

            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("GalacticDiskDefinition"))
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
                go.AddComponent<ParticleSystem>();  // The parameters of the particle system are set later and multiple times

                try
                {
                    ProcessObject(go, config);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[GalacticDisk] Failed to process object \"{go.name}\": {ex}");
                }

                disksByPlanet[config.planet] = go; // Remember for UI updates
            }

            toolbar = new GalacticDiskToolbar();

            GameEvents.onGUIApplicationLauncherReady.Add(AddLauncherButton);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(RemoveLauncherButton);
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

            // GetTexture looks up GameData/GalacticDisk/Icons/toolbar_icon.png (or .dds/.tga).
            // Falls back to a plain generated dot so the button always shows something.
            Texture2D icon = GameDatabase.Instance.GetTexture(IconTexturePath, false) ?? CreateDefaultParticleTexture(38);

            launcherButton = ApplicationLauncher.Instance.AddModApplication(
                onTrue: () => toolbar.SetVisible(true),
                onFalse: () => toolbar.SetVisible(false),
                onHover: null,
                onHoverOut: null,
                onEnable: null,
                onDisable: null,
                visibleInScenes: ApplicationLauncher.AppScenes.ALWAYS,
                texture: icon);

            // A freshly-created button always starts toggled off. Sync its visual
            // state to whatever the window is currently showing (it persists across
            // scenes) using the "false" makeCall overload so this doesn't re-fire
            // onTrue/onFalse and stomp on that same state.
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

            // The ApplicationLauncher itself may already be mid-teardown by the time
            // this fires, so removal can throw. Always clear our reference regardless —
            // holding on to a stale button blocks AddLauncherButton from ever running
            // again on the next scene, which is what made the icon vanish for good.
            try
            {
                ApplicationLauncher.Instance?.RemoveModApplication(launcherButton);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GalacticDisk] Failed to cleanly remove launcher button: {ex}");
            }
            finally
            {
                launcherButton = null;
            }
        }

        void OnGUI()
        {
            toolbar?.OnClick();
        }

        void ProcessObject(GameObject go, GalaxyConfig config)
        {
            ParticleSystem ps = go.GetComponent<ParticleSystem>();
            if (ps == null)  // This literally cannot happen now so maybe I should remove this (ps is added just before the function is even called)
            {
                Debug.LogWarning($"[GalacticDisk] Object \"{go.name}\" has no ParticleSystem, skipping.");
                return;
            }

            ParticleSystem.EmissionModule emission = ps.emission;
            if (ps.particleCount > 0 && !emission.enabled)
            {
                // Already generated — nothing to do. Kept as a safety net to prevent more particle generation
                Debug.Log($"[GalacticDisk] Object \"{go.name}\" already has particles generated, skipping.");
                return;
            }

            // Set up particle system so anything works
            ParticleSystem.MainModule main = ps.main;
            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();

            // Get variables
            main.maxParticles = config.starCount;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.loop = false;
            main.playOnAwake = false;
            emission.enabled = false;

            // Get image texture
            Material material = new Material(Shader.Find("Sprites/Default"));
            if (config.usesTexture)
            {
                if (File.Exists(config.texturePath))
                {
                    byte[] bytes = File.ReadAllBytes(config.texturePath);
                    Texture2D texture = new Texture2D(2, 2);
                    texture.LoadImage(bytes);
                    material.mainTexture = texture;
                }
                else
                {
                    Debug.LogWarning($"[GalacticDisk] Texture for star particles not found at '{config.texturePath}'");
                    material.mainTexture = CreateDefaultParticleTexture();
                }
            }
            else
            {
                material.mainTexture = CreateDefaultParticleTexture();
            }

            renderer.material = material;

            // Apply the object/render scale to the object's transform. The final
            // transform scale is objectScale * renderScale; the particle data itself
            // is generated pre-divided by renderScale (see GenerateGalaxy) so the
            // *visible* size of the disk still matches objectScale alone — only the
            // GameObject's bounds (and therefore KSP's distance/culling checks) see
            // the larger, renderScale-inflated size.
            go.transform.localScale = Vector3.one * (config.objectScale * config.renderScale);

            // Generate particles
            GenerateGalaxy(ps, config, material);

            Debug.Log($"[GalacticDisk] Generated {ps.particleCount} stars at \"{go.name}\".");
        }

        internal static void RegenerateDisk(GameObject go, GalaxyConfig config)
        {
            ParticleSystem ps = go.GetComponent<ParticleSystem>();
            if (ps == null) return;

            // Reset particle system to allow regeneration of new parameters.
            ParticleSystem.MainModule main = ps.main;
            main.maxParticles = 0;
            ps.SetParticles(null, 0);
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;

            // Generate fresh particles using the same logic as ProcessObject.
            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            Material material = new Material(Shader.Find("Sprites/Default"));

            if (config.usesTexture)
            {
                if (File.Exists(config.texturePath))
                {
                    byte[] bytes = File.ReadAllBytes(config.texturePath);
                    Texture2D texture = new Texture2D(2, 2);
                    texture.LoadImage(bytes);
                    material.mainTexture = texture;
                }
                else
                {
                    Debug.LogWarning($"[GalacticDisk] Texture for star particles not found at '{config.texturePath}'");
                    material.mainTexture = CreateDefaultParticleTexture();
                }
            }
            else
            {
                material.mainTexture = CreateDefaultParticleTexture();
            }

            renderer.material = material;

            // Apply the object/render scale.
            go.transform.localScale = Vector3.one * (config.objectScale * config.renderScale);

            GenerateGalaxy(ps, config, material);

            Debug.Log($"[GalacticDisk] Regenerated {ps.particleCount} stars at \"{go.name}\".");
        }

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
                return;
            }

            GameObject go = new GameObject("GalacticDisk")
            {
                layer = 10
            };
            go.transform.parent = body.scaledBody.transform;
            go.transform.localPosition = Vector3.zero;
            go.AddComponent<ParticleSystem>();

            try
            {
                instance.ProcessObject(go, config);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GalacticDisk] Failed to process object \"{go.name}\": {ex}");
            }

            disksByPlanet[body.name] = go;
        }

        internal static void RemoveDisk(string planetName)
        {
            if (string.IsNullOrEmpty(planetName)) return;

            if (disksByPlanet.TryGetValue(planetName, out GameObject go))
            {
                if (go != null) Destroy(go);
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
            go.AddComponent<ParticleSystem>();

            RegenerateDisk(go, config);

            return go;
        }

        static void GenerateGalaxy(ParticleSystem ps, GalaxyConfig c, Material m)
        {
            ParticleSystem.Particle[] particles = new ParticleSystem.Particle[c.starCount];

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

            // Get-or-add: RegenerateDisk calls this on an already-existing disk's
            // GameObject (to replace it in place), which already has this component
            // from when it was first created — AddComponent unconditionally would
            // stack a duplicate on every re-apply.
            GalacticDiskObject newGDO = ps.gameObject.GetComponent<GalacticDiskObject>() ?? ps.gameObject.AddComponent<GalacticDiskObject>();
            newGDO.particles = particles;
            newGDO.material = m;
            newGDO.config = c;
            newGDO.orbitBody = FlightGlobals.GetBodyByName(c.planet);
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

            if (UnityEngine.Random.value < c.interarmFraction)
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

    public class GalaxyConfig
    {
        public string planet;

        public int starCount = 50000;
        public float galaxyRadius = 100f;
        public float diskThickness = 2f;
        public float minStarSize = 0.015f;
        public float maxStarSize = 0.09f;

        public int armCount = 4;
        public float armTightness = 4f;
        public float armWidth = 0.25f;

        public float interarmFraction = 0.25f;
        public float armConcentration = 0.35f;

        public float bulgeFraction = 0.15f;

        // Bulge is a smooth ellipsoid with an independently-scaled extent on each
        // axis. Replaces the old single bulgeRadius (a sphere with X=Z, and Y
        // implicitly at 0.15x). Defaults reproduce roughly that old proportion.
        public float bulgeScaleX = 15f;
        public float bulgeScaleY = 2.25f;
        public float bulgeScaleZ = 15f;

        // Separate colors for each population, replacing the old color1 (inner)/
        // color2 (outer) radial blend. Defaults mirror the old defaults: bulge and
        // arms took after color1 (warm), interarm took after color2 (cool/blue).
        public Color colorBulge = new Color(1.0f, 0.92f, 0.65f);
        public Color colorArms = new Color(1.0f, 0.92f, 0.65f);
        public Color colorInterarm = new Color(0.45f, 0.65f, 1.0f);

        // Optional highlight color sprinkled onto arm stars only (not bulge, not
        // interarm) to simulate HII regions/star clusters. Chance defaults to 0
        // so existing configs render identically unless they opt in.
        public Color armHighlightColor = new Color(1.0f, 0.55f, 0.6f);
        public float armHighlightChance = 0f;

        // Fixed WORLD rotation (Euler degrees) the disk is held at every frame,
        // overriding whatever rotation it would otherwise inherit from its parent
        // scaled-space body. Defaults to 0,0,0, which matches the disk's previous
        // (unintentional) behavior of just sitting at identity rotation — so an
        // existing config with a non-spinning parent body sees no change.
        public float rotationX = 0f;
        public float rotationY = 0f;
        public float rotationZ = 0f;

        // When true, ignore rotationX/rotationZ and instead compute the disk's
        // tilt directly from the orbit of the body it's attached to (config.planet),
        // so the disk plane is guaranteed coplanar with that orbit regardless of
        // how inclination/LAN were set up. rotationY still applies as a free spin
        // around the resulting plane normal. Defaults to false — manual rotationX/
        // Y/Z behave exactly as before unless this is turned on.
        public bool alignRotationToOrbit = false;

        // Seconds for the disk to complete one full rotation about its own plane
        // normal (added on top of rotationY / alignRotationToOrbit). 0 (default)
        // disables this entirely — no extra spin, matching old behavior. Uses
        // Planetarium.GetUniversalTime() rather than accumulating per-frame deltas,
        // so it's exact at any timewarp rate and never drifts.
        public float rotationPeriod = 0f;

        // Fraction of galaxyRadius used as the exponential falloff scale length
        // for star density (was hardcoded to 0.25 previously). Lower = fades out
        // faster near the center; higher = a more gradual, extended-looking disk.
        // Default matches the old hardcoded behavior exactly.
        public float diskScaleLengthFraction = 0.25f;

        public bool usesTexture = false;
        public string texturePath = "";

        // Final Transform.localScale applied to the object is objectScale * renderScale.
        // Both support scientific notation via GetFloat's NumberStyles.Float parsing.
        public float objectScale = 1f;

        // Additional scale applied only to the Transform (not to the visible size of
        // the disk) so KSP sees a much larger object and doesn't cull/hide it from far
        // away. Particle positions/sizes are generated pre-divided by this value to
        // cancel it back out visually.
        public float renderScale = 1f;

        public static GalaxyConfig FromConfigNode(ConfigNode node)
        {
            GalaxyConfig c = new GalaxyConfig();

            c.planet = GetString(node, "planet", c.planet);

            c.starCount = GetInt(node, "starCount", c.starCount);
            c.galaxyRadius = GetFloat(node, "galaxyRadius", c.galaxyRadius);
            c.diskThickness = GetFloat(node, "diskThickness", c.diskThickness);
            c.minStarSize = GetFloat(node, "minStarSize", c.minStarSize);
            c.maxStarSize = GetFloat(node, "maxStarSize", c.maxStarSize);

            c.armCount = GetInt(node, "armCount", c.armCount);
            c.armTightness = GetFloat(node, "armTightness", c.armTightness);
            c.armWidth = GetFloat(node, "armWidth", c.armWidth);

            c.interarmFraction = GetFloat(node, "interarmFraction", c.interarmFraction);
            c.armConcentration = GetFloat(node, "armConcentration", c.armConcentration);

            c.bulgeFraction = GetFloat(node, "bulgeFraction", c.bulgeFraction);

            if (node.HasValue("bulgeRadius"))
            {
                float legacyRadius = GetFloat(node, "bulgeRadius", c.bulgeScaleX);
                c.bulgeScaleX = legacyRadius;
                c.bulgeScaleY = legacyRadius;
                c.bulgeScaleZ = legacyRadius;
                Debug.LogWarning("[GalacticDisk] Config field 'bulgeRadius' is obsolete — use bulgeScaleX/bulgeScaleY/bulgeScaleZ instead. Setting all three to the given value for now.");
            }
            c.bulgeScaleX = GetFloat(node, "bulgeScaleX", c.bulgeScaleX);
            c.bulgeScaleY = GetFloat(node, "bulgeScaleY", c.bulgeScaleY);
            c.bulgeScaleZ = GetFloat(node, "bulgeScaleZ", c.bulgeScaleZ);

            if (node.HasValue("color1"))
            {
                Color legacyColor1 = GetColor(node, "color1", c.colorBulge);
                c.colorBulge = legacyColor1;
                c.colorArms = legacyColor1;
                Debug.LogWarning("[GalacticDisk] Config field 'color1' is obsolete — use colorBulge/colorArms instead. Using it as the default for both for now.");
            }
            if (node.HasValue("color2"))
            {
                c.colorInterarm = GetColor(node, "color2", c.colorInterarm);
                Debug.LogWarning("[GalacticDisk] Config field 'color2' is obsolete — use colorInterarm instead. Using it as the default for now.");
            }
            c.colorBulge = GetColor(node, "colorBulge", c.colorBulge);
            c.colorArms = GetColor(node, "colorArms", c.colorArms);
            c.colorInterarm = GetColor(node, "colorInterarm", c.colorInterarm);

            c.armHighlightColor = GetColor(node, "armHighlightColor", c.armHighlightColor);
            c.armHighlightChance = GetFloat(node, "armHighlightChance", c.armHighlightChance);

            c.rotationX = GetFloat(node, "rotationX", c.rotationX);
            c.rotationY = GetFloat(node, "rotationY", c.rotationY);
            c.rotationZ = GetFloat(node, "rotationZ", c.rotationZ);
            c.alignRotationToOrbit = GetBool(node, "alignRotationToOrbit", c.alignRotationToOrbit);
            c.rotationPeriod = GetFloat(node, "rotationPeriod", c.rotationPeriod);
            c.diskScaleLengthFraction = GetFloat(node, "diskScaleLengthFraction", c.diskScaleLengthFraction);

            c.usesTexture = GetBool(node, "usesTexture", c.usesTexture);
            string rawPath = GetString(node, "texturePath", "");
            if (!string.IsNullOrEmpty(rawPath))
                c.texturePath = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", rawPath);
            else
            {
                c.texturePath = "";  // Let GenerateGalaxy handle default case
                if (c.usesTexture)
                {
                    c.usesTexture = false;
                    Debug.LogWarning("[GalacticDisk] No texturePath but uses a custom texture, using default texture instead.");
                }
            }

            c.objectScale = GetFloat(node, "objectScale", c.objectScale);
            c.renderScale = GetFloat(node, "renderScale", c.renderScale);

            return c;
        }

        static string GetString(ConfigNode node, string key, string fallback)
        {
            return node.HasValue(key) ? node.GetValue(key) : fallback;
        }

        static int GetInt(ConfigNode node, string key, int fallback)
        {
            if (node.HasValue(key) && int.TryParse(node.GetValue(key), out int result))
                return result;
            return fallback;
        }

        static float GetFloat(ConfigNode node, string key, float fallback)
        {
            if (node.HasValue(key) &&
                float.TryParse(node.GetValue(key), NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
                return result;
            return fallback;
        }

        static Color GetColor(ConfigNode node, string key, Color fallback)
        {
            if (!node.HasValue(key))
            {
                Debug.LogWarning("[GalacticDisk] Key " + key + " in node " + node.name + " does not exist, using fallback.");
                return fallback;
            }

            string[] parts = node.GetValue(key).Split(',');
            if (parts.Length < 3)
                return fallback;

            try
            {
                float r = float.Parse(parts[0].Trim(), CultureInfo.InvariantCulture);
                float g = float.Parse(parts[1].Trim(), CultureInfo.InvariantCulture);
                float b = float.Parse(parts[2].Trim(), CultureInfo.InvariantCulture);
                float a = parts.Length >= 4 ? float.Parse(parts[3].Trim(), CultureInfo.InvariantCulture) : 1f;

                return new Color(r, g, b, a);
            }
            catch
            {
                return fallback;
            }
        }
        static bool GetBool(ConfigNode node, string key, bool fallback)
        {
            if (node.HasValue(key) && bool.TryParse(node.GetValue(key), out bool result))
                return result;
            return fallback;
        }
    }

    // Updates the disk on runtime when it gets cleared by KSP
    // Runs after every default-order component's LateUpdate, including whatever
    // updates the parent celestial body's own rotation each frame. Without this,
    // Transform.rotation below computes a local rotation relative to the parent's
    // rotation AT THAT INSTANT — if the parent's own rotation script then runs
    // afterward in the same frame (unguaranteed ordering otherwise), the parent
    // keeps moving after we've already locked in our local offset, leaving a
    // visible per-frame drift that scales with the parent's spin rate — i.e.
    // exactly the timewarp-scaled spinning being reported. Running last removes
    // the race entirely: by the time we set our rotation, the parent has already
    // finished moving for this frame.
    [DefaultExecutionOrder(32000)]
    public class GalacticDiskObject : MonoBehaviour
    {
        public ParticleSystem.Particle[] particles;
        public ParticleSystem ps;
        public ParticleSystemRenderer psR;
        public Material material;
        public GalaxyConfig config;

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
            // Get the ParticleSystem that renders the disk
            if (ps == null)
                ps = GetComponent<ParticleSystem>();
            // Get the ParticleSystemRenderer that actually renders the disk
            if (psR == null)
                psR = GetComponent<ParticleSystemRenderer>();

            if (particles == null)
                return;

            framesSinceCheck++;
            if (framesSinceCheck < checkIntervalFrames)
                return;
            framesSinceCheck = 0;

            // If particle set is added and the disk was cleared, spawn the particles
            ParticleSystem.EmissionModule emission = ps.emission;
            if (ps.particleCount == 0 || emission.enabled)
            {
                UpdateDisk();
            }
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
                transform.rotation = align * Quaternion.Euler(0f, config.rotationY + spinDegrees, 0f);
            }
            else
            {
                transform.rotation = Quaternion.Euler(config.rotationX, config.rotationY + spinDegrees, config.rotationZ);
            }
        }

        public void UpdateDisk()  // It is not recommended to call this since it doesn't reset the particles, better change values and reset the particle system
        {
            ParticleSystem.EmissionModule emission = ps.emission;
            ParticleSystem.MainModule main = ps.main;

            psR.material = material;
            main.maxParticles = config.starCount;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.loop = false;
            main.playOnAwake = false;
            emission.enabled = false;

            // KSP appears to reset the transform scale along with clearing the
            // particle system, so it's reapplied here alongside the particles.
            transform.localScale = Vector3.one * (config.objectScale * config.renderScale);

            ps.SetParticles(particles);
            Debug.Log($"[GalacticDisk] Re-generated {ps.particleCount} stars at \"{ps.gameObject.name}\".");
        }
    }

    // Simple GUI toolbar window that lets the user edit the GalaxyConfig values
    // and apply/preview the changes on all generated disks.
    public class GalacticDiskToolbar
    {
        private bool showWindow = false;
        private Rect windowRect = new Rect(20, 20, 460, 560);

        // Index into FlightGlobals.Bodies of the currently selected preview/remove target.
        // Not sorted in any way — just whatever order the game currently has them loaded in.
        private int selectedBodyIndex = 0;

        // The text shown by "Show config", and its scroll position.
        private string configText = "";
        private Vector2 configScroll = Vector2.zero;

        // Scroll position for the collapsible field area, so the window itself
        // can stay a fixed, small size regardless of how many sections are open.
        private Vector2 fieldsScroll = Vector2.zero;

        // Editable fields (kept as plain primitives so GUILayout.TextField parsing works).
        private string starCount = "50000";
        private string galaxyRadius = "100";
        private string diskThickness = "2";
        private string minStarSize = "0.015";
        private string maxStarSize = "0.09";

        private string armCount = "4";
        private string armTightness = "4";
        private string armWidth = "0.25";
        private string interarmFraction = "0.25";
        private string armConcentration = "0.35";

        private string bulgeFraction = "0.15";
        private string bulgeScaleX = "15", bulgeScaleY = "2.25", bulgeScaleZ = "15";

        private string colorBulgeR = "1.0", colorBulgeG = "0.92", colorBulgeB = "0.65";
        private string colorArmsR = "1.0", colorArmsG = "0.92", colorArmsB = "0.65";
        private string colorInterarmR = "0.45", colorInterarmG = "0.65", colorInterarmB = "1.0";

        private string armHighlightColorR = "1.0", armHighlightColorG = "0.55", armHighlightColorB = "0.6";
        private string armHighlightChance = "0";

        private string rotationX = "0", rotationY = "0", rotationZ = "0";
        private bool alignRotationToOrbit = false;
        private string rotationPeriod = "0";
        private string diskScaleLengthFraction = "0.25";

        private string objectScale = "1";
        private string renderScale = "1";

        // Whether each section is expanded, so the window can be collapsed down
        // to fit smaller screens instead of always showing every field at once.
        private bool showBasic = true;
        private bool showArms = false;
        private bool showBulge = false;
        private bool showColors = false;
        private bool showRotation = false;
        private bool showScale = false;

        // Called from GalacticDiskGenerator.OnGUI() every frame; only draws the
        // window while the toolbar button has it toggled on.
        public void OnClick()
        {
            if (!showWindow) return;

            windowRect = GUILayout.Window(985123, windowRect, DrawWindow, "Galactic Disk Config");
        }

        public bool IsVisible => showWindow;

        public void SetVisible(bool visible)
        {
            showWindow = visible;
        }

        private static bool IsUsableScene()
        {
            return HighLogic.LoadedScene == GameScenes.SPACECENTER
                || HighLogic.LoadedScene == GameScenes.FLIGHT
                || HighLogic.LoadedScene == GameScenes.TRACKSTATION;
        }

        private const float LabelWidth = 140f;

        private static string LabeledField(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(LabelWidth));
            string result = GUILayout.TextField(value);
            GUILayout.EndHorizontal();
            return result;
        }

        // A clickable section header that toggles a bool — used to fold groups
        // of fields away so the window doesn't have to show everything at once.
        private static bool Foldout(string label, bool expanded)
        {
            string arrow = expanded ? "\u25bc " : "\u25b6 ";
            if (GUILayout.Button(arrow + label, GUILayout.Height(22f)))
                expanded = !expanded;
            return expanded;
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();

            if (!IsUsableScene())
            {
                GUILayout.Label("Galactic Disk config is only available in the Space Center, Flight, or Tracking Station scenes.");
                GUILayout.EndVertical();
                GUI.DragWindow();
                return;
            }

            GUILayout.Label("Galaxy Config (edit values here)", GUILayout.Height(20f));

            GUILayout.Space(10f);
            GUILayout.Label("Various useful information to know while using this tool:");

            GUILayout.Space(5f);
            GUILayout.Label("You will need to save this config manually, it does not autosave!");
            GUILayout.Label("\"Load\" reads the selected body's current disk into the fields below.");
            GUILayout.Label("\"Apply\" replaces that disk in place with the current field values");
            GUILayout.Label("(or creates one if the body doesn't have one yet). Click a section");
            GUILayout.Label("header below to expand/collapse it.");

            fieldsScroll = GUILayout.BeginScrollView(fieldsScroll, GUILayout.Height(320f));

            GUILayout.Space(10f);
            showBasic = Foldout("Basic", showBasic);
            if (showBasic)
            {
                starCount = LabeledField("Star count", starCount);
                galaxyRadius = LabeledField("Galaxy radius", galaxyRadius);
                diskThickness = LabeledField("Disk thickness", diskThickness);
                minStarSize = LabeledField("Min star size", minStarSize);
                maxStarSize = LabeledField("Max star size", maxStarSize);
                diskScaleLengthFraction = LabeledField("Falloff fraction", diskScaleLengthFraction);
            }

            showArms = Foldout("Arms", showArms);
            if (showArms)
            {
                armCount = LabeledField("Arm count", armCount);
                armTightness = LabeledField("Arm tightness", armTightness);
                armWidth = LabeledField("Arm width", armWidth);
                interarmFraction = LabeledField("Interarm fraction", interarmFraction);
                armConcentration = LabeledField("Arm concentration", armConcentration);
            }

            showBulge = Foldout("Bulge", showBulge);
            if (showBulge)
            {
                bulgeFraction = LabeledField("Bulge fraction", bulgeFraction);
                bulgeScaleX = LabeledField("Bulge scale X", bulgeScaleX);
                bulgeScaleY = LabeledField("Bulge scale Y", bulgeScaleY);
                bulgeScaleZ = LabeledField("Bulge scale Z", bulgeScaleZ);
            }

            showColors = Foldout("Colors", showColors);
            if (showColors)
            {
                colorBulgeR = LabeledField("Bulge color R", colorBulgeR);
                colorBulgeG = LabeledField("Bulge color G", colorBulgeG);
                colorBulgeB = LabeledField("Bulge color B", colorBulgeB);

                GUILayout.Space(6f);
                colorArmsR = LabeledField("Arms color R", colorArmsR);
                colorArmsG = LabeledField("Arms color G", colorArmsG);
                colorArmsB = LabeledField("Arms color B", colorArmsB);

                GUILayout.Space(6f);
                colorInterarmR = LabeledField("Interarm color R", colorInterarmR);
                colorInterarmG = LabeledField("Interarm color G", colorInterarmG);
                colorInterarmB = LabeledField("Interarm color B", colorInterarmB);

                GUILayout.Space(6f);
                armHighlightColorR = LabeledField("Highlight color R", armHighlightColorR);
                armHighlightColorG = LabeledField("Highlight color G", armHighlightColorG);
                armHighlightColorB = LabeledField("Highlight color B", armHighlightColorB);
                armHighlightChance = LabeledField("Highlight chance", armHighlightChance);
            }

            showRotation = Foldout("Rotation", showRotation);
            if (showRotation)
            {
                alignRotationToOrbit = GUILayout.Toggle(alignRotationToOrbit, " Align rotation to planet's orbit plane");

                GUI.enabled = !alignRotationToOrbit;
                rotationX = LabeledField("Rotation X", rotationX);
                GUI.enabled = true;
                rotationY = LabeledField("Rotation Y", rotationY);
                GUI.enabled = !alignRotationToOrbit;
                rotationZ = LabeledField("Rotation Z", rotationZ);
                GUI.enabled = true;
                if (alignRotationToOrbit)
                    GUILayout.Label("(X/Z ignored while aligned; Y still applies as spin)");

                GUILayout.Space(6f);
                rotationPeriod = LabeledField("Rotation period (s)", rotationPeriod);
                GUILayout.Label("(Seconds per full spin, on top of Rotation Y. 0 = no spin. Supports");
                GUILayout.Label("scientific notation, e.g. 6.3E+7 for a year.)");
            }

            showScale = Foldout("Scale", showScale);
            if (showScale)
            {
                objectScale = LabeledField("Object scale", objectScale);
                renderScale = LabeledField("Render scale", renderScale);
            }

            GUILayout.EndScrollView();

            GUILayout.Space(10f);

            // Body selector — cycles through whatever bodies are currently loaded,
            // in whatever order FlightGlobals.Bodies happens to have them in.
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(30f)))
            {
                CycleBody(-1);
            }
            GUILayout.Label(GetSelectedBody()?.name ?? "(no bodies loaded)");
            if (GUILayout.Button(">", GUILayout.Width(30f)))
            {
                CycleBody(1);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Show config"))
            {
                ShowConfig();
            }
            if (GUILayout.Button("Load"))
            {
                LoadSelectedDisk();
            }
            if (GUILayout.Button("Apply"))
            {
                Apply();
            }
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Remove disks on selected body"))
            {
                RemoveDisks();
            }

            if (!string.IsNullOrEmpty(configText))
            {
                GUILayout.Space(10f);
                configScroll = GUILayout.BeginScrollView(configScroll, GUILayout.Height(160f));
                GUILayout.TextArea(configText);
                GUILayout.EndScrollView();
            }

            GUILayout.EndVertical();

            GUI.DragWindow();
        }

        private CelestialBody GetSelectedBody()
        {
            List<CelestialBody> bodies = FlightGlobals.Bodies;
            if (bodies == null || bodies.Count == 0) return null;

            if (selectedBodyIndex < 0) selectedBodyIndex = 0;
            if (selectedBodyIndex >= bodies.Count) selectedBodyIndex = bodies.Count - 1;

            return bodies[selectedBodyIndex];
        }

        private void CycleBody(int direction)
        {
            List<CelestialBody> bodies = FlightGlobals.Bodies;
            if (bodies == null || bodies.Count == 0) return;

            selectedBodyIndex = (selectedBodyIndex + direction + bodies.Count) % bodies.Count;
        }

        private GalaxyConfig BuildConfigFromFields()
        {
            GalaxyConfig cfg = new GalaxyConfig();

            int.TryParse(starCount, out cfg.starCount);
            float.TryParse(galaxyRadius, NumberStyles.Float, CultureInfo.InvariantCulture, out cfg.galaxyRadius);
            float.TryParse(diskThickness, NumberStyles.Float, CultureInfo.InvariantCulture, out cfg.diskThickness);
            float.TryParse(minStarSize, NumberStyles.Float, CultureInfo.InvariantCulture, out cfg.minStarSize);
            float.TryParse(maxStarSize, NumberStyles.Float, CultureInfo.InvariantCulture, out cfg.maxStarSize);

            int.TryParse(armCount, out cfg.armCount);
            float.TryParse(armTightness, NumberStyles.Float, CultureInfo.InvariantCulture, out cfg.armTightness);
            float.TryParse(armWidth, NumberStyles.Float, CultureInfo.InvariantCulture, out cfg.armWidth);
            float.TryParse(interarmFraction, NumberStyles.Float, CultureInfo.InvariantCulture, out cfg.interarmFraction);
            float.TryParse(armConcentration, NumberStyles.Float, CultureInfo.InvariantCulture, out cfg.armConcentration);

            float.TryParse(bulgeFraction, NumberStyles.Float, CultureInfo.InvariantCulture, out cfg.bulgeFraction);
            float.TryParse(bulgeScaleX, NumberStyles.Float, CultureInfo.InvariantCulture, out cfg.bulgeScaleX);
            float.TryParse(bulgeScaleY, NumberStyles.Float, CultureInfo.InvariantCulture, out cfg.bulgeScaleY);
            float.TryParse(bulgeScaleZ, NumberStyles.Float, CultureInfo.InvariantCulture, out cfg.bulgeScaleZ);

            float.TryParse(colorBulgeR, NumberStyles.Float, CultureInfo.InvariantCulture, out float cbr);
            float.TryParse(colorBulgeG, NumberStyles.Float, CultureInfo.InvariantCulture, out float cbg);
            float.TryParse(colorBulgeB, NumberStyles.Float, CultureInfo.InvariantCulture, out float cbb);
            cfg.colorBulge = new Color(cbr, cbg, cbb);

            float.TryParse(colorArmsR, NumberStyles.Float, CultureInfo.InvariantCulture, out float car);
            float.TryParse(colorArmsG, NumberStyles.Float, CultureInfo.InvariantCulture, out float cag);
            float.TryParse(colorArmsB, NumberStyles.Float, CultureInfo.InvariantCulture, out float cab);
            cfg.colorArms = new Color(car, cag, cab);

            float.TryParse(colorInterarmR, NumberStyles.Float, CultureInfo.InvariantCulture, out float cir);
            float.TryParse(colorInterarmG, NumberStyles.Float, CultureInfo.InvariantCulture, out float cig);
            float.TryParse(colorInterarmB, NumberStyles.Float, CultureInfo.InvariantCulture, out float cib);
            cfg.colorInterarm = new Color(cir, cig, cib);

            float.TryParse(armHighlightColorR, NumberStyles.Float, CultureInfo.InvariantCulture, out float ahr);
            float.TryParse(armHighlightColorG, NumberStyles.Float, CultureInfo.InvariantCulture, out float ahg);
            float.TryParse(armHighlightColorB, NumberStyles.Float, CultureInfo.InvariantCulture, out float ahb);
            cfg.armHighlightColor = new Color(ahr, ahg, ahb);
            float.TryParse(armHighlightChance, NumberStyles.Float, CultureInfo.InvariantCulture, out cfg.armHighlightChance);

            float.TryParse(rotationX, NumberStyles.Float, CultureInfo.InvariantCulture, out cfg.rotationX);
            float.TryParse(rotationY, NumberStyles.Float, CultureInfo.InvariantCulture, out cfg.rotationY);
            float.TryParse(rotationZ, NumberStyles.Float, CultureInfo.InvariantCulture, out cfg.rotationZ);
            cfg.alignRotationToOrbit = alignRotationToOrbit;
            float.TryParse(rotationPeriod, NumberStyles.Float, CultureInfo.InvariantCulture, out cfg.rotationPeriod);
            float.TryParse(diskScaleLengthFraction, NumberStyles.Float, CultureInfo.InvariantCulture, out cfg.diskScaleLengthFraction);

            float.TryParse(objectScale, NumberStyles.Float, CultureInfo.InvariantCulture, out cfg.objectScale);
            float.TryParse(renderScale, NumberStyles.Float, CultureInfo.InvariantCulture, out cfg.renderScale);

            return cfg;
        }

        private void ShowConfig()
        {
            CelestialBody body = GetSelectedBody();
            GalaxyConfig cfg = BuildConfigFromFields();
            configText = GenerateConfigText(cfg, body?.name ?? "");
        }

        private string GenerateConfigText(GalaxyConfig cfg, string planetName)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;

            return
                "GalacticDiskDefinition\n" +
                "{\n" +
                $"    planet = {planetName}\n" +
                $"    starCount = {cfg.starCount.ToString(ci)}\n" +
                $"    galaxyRadius = {cfg.galaxyRadius.ToString(ci)}\n" +
                $"    diskThickness = {cfg.diskThickness.ToString(ci)}\n" +
                $"    minStarSize = {cfg.minStarSize.ToString(ci)}\n" +
                $"    maxStarSize = {cfg.maxStarSize.ToString(ci)}\n" +
                $"    armCount = {cfg.armCount.ToString(ci)}\n" +
                $"    armTightness = {cfg.armTightness.ToString(ci)}\n" +
                $"    armWidth = {cfg.armWidth.ToString(ci)}\n" +
                $"    interarmFraction = {cfg.interarmFraction.ToString(ci)}\n" +
                $"    armConcentration = {cfg.armConcentration.ToString(ci)}\n" +
                $"    bulgeFraction = {cfg.bulgeFraction.ToString(ci)}\n" +
                $"    bulgeScaleX = {cfg.bulgeScaleX.ToString(ci)}\n" +
                $"    bulgeScaleY = {cfg.bulgeScaleY.ToString(ci)}\n" +
                $"    bulgeScaleZ = {cfg.bulgeScaleZ.ToString(ci)}\n" +
                $"    colorBulge = {cfg.colorBulge.r.ToString(ci)}, {cfg.colorBulge.g.ToString(ci)}, {cfg.colorBulge.b.ToString(ci)}\n" +
                $"    colorArms = {cfg.colorArms.r.ToString(ci)}, {cfg.colorArms.g.ToString(ci)}, {cfg.colorArms.b.ToString(ci)}\n" +
                $"    colorInterarm = {cfg.colorInterarm.r.ToString(ci)}, {cfg.colorInterarm.g.ToString(ci)}, {cfg.colorInterarm.b.ToString(ci)}\n" +
                $"    armHighlightColor = {cfg.armHighlightColor.r.ToString(ci)}, {cfg.armHighlightColor.g.ToString(ci)}, {cfg.armHighlightColor.b.ToString(ci)}\n" +
                $"    armHighlightChance = {cfg.armHighlightChance.ToString(ci)}\n" +
                $"    rotationX = {cfg.rotationX.ToString(ci)}\n" +
                $"    rotationY = {cfg.rotationY.ToString(ci)}\n" +
                $"    rotationZ = {cfg.rotationZ.ToString(ci)}\n" +
                $"    alignRotationToOrbit = {cfg.alignRotationToOrbit}\n" +
                $"    rotationPeriod = {cfg.rotationPeriod.ToString(ci)}\n" +
                $"    diskScaleLengthFraction = {cfg.diskScaleLengthFraction.ToString(ci)}\n" +
                $"    objectScale = {cfg.objectScale.ToString(ci)}\n" +
                $"    renderScale = {cfg.renderScale.ToString(ci)}\n" +
                "}";
        }

        // Fills every editable field from an existing config — the reverse of
        // BuildConfigFromFields — so "Load" can pull a disk's current values in
        // instead of the user re-typing everything from scratch.
        private void PopulateFieldsFromConfig(GalaxyConfig cfg)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;

            starCount = cfg.starCount.ToString(ci);
            galaxyRadius = cfg.galaxyRadius.ToString(ci);
            diskThickness = cfg.diskThickness.ToString(ci);
            minStarSize = cfg.minStarSize.ToString(ci);
            maxStarSize = cfg.maxStarSize.ToString(ci);

            armCount = cfg.armCount.ToString(ci);
            armTightness = cfg.armTightness.ToString(ci);
            armWidth = cfg.armWidth.ToString(ci);
            interarmFraction = cfg.interarmFraction.ToString(ci);
            armConcentration = cfg.armConcentration.ToString(ci);

            bulgeFraction = cfg.bulgeFraction.ToString(ci);
            bulgeScaleX = cfg.bulgeScaleX.ToString(ci);
            bulgeScaleY = cfg.bulgeScaleY.ToString(ci);
            bulgeScaleZ = cfg.bulgeScaleZ.ToString(ci);

            colorBulgeR = cfg.colorBulge.r.ToString(ci);
            colorBulgeG = cfg.colorBulge.g.ToString(ci);
            colorBulgeB = cfg.colorBulge.b.ToString(ci);

            colorArmsR = cfg.colorArms.r.ToString(ci);
            colorArmsG = cfg.colorArms.g.ToString(ci);
            colorArmsB = cfg.colorArms.b.ToString(ci);

            colorInterarmR = cfg.colorInterarm.r.ToString(ci);
            colorInterarmG = cfg.colorInterarm.g.ToString(ci);
            colorInterarmB = cfg.colorInterarm.b.ToString(ci);

            armHighlightColorR = cfg.armHighlightColor.r.ToString(ci);
            armHighlightColorG = cfg.armHighlightColor.g.ToString(ci);
            armHighlightColorB = cfg.armHighlightColor.b.ToString(ci);
            armHighlightChance = cfg.armHighlightChance.ToString(ci);

            rotationX = cfg.rotationX.ToString(ci);
            rotationY = cfg.rotationY.ToString(ci);
            rotationZ = cfg.rotationZ.ToString(ci);
            alignRotationToOrbit = cfg.alignRotationToOrbit;
            rotationPeriod = cfg.rotationPeriod.ToString(ci);
            diskScaleLengthFraction = cfg.diskScaleLengthFraction.ToString(ci);

            objectScale = cfg.objectScale.ToString(ci);
            renderScale = cfg.renderScale.ToString(ci);
        }

        // Reads the selected body's currently-applied disk config, if it has one,
        // into the fields. Does nothing (and leaves fields untouched) if the body
        // has no disk yet — there's nothing to load in that case.
        private void LoadSelectedDisk()
        {
            CelestialBody body = GetSelectedBody();
            if (body == null) return;

            GalaxyConfig cfg = GalacticDiskGenerator.GetConfig(body.name);
            if (cfg == null)
            {
                configText = $"({body.name} has no disk to load.)";
                return;
            }

            PopulateFieldsFromConfig(cfg);
            configText = "";
        }

        // Replaces the selected body's disk in place with the current field
        // values — creates one if it doesn't have one yet. No more temporary
        // preview objects; this is the real disk, updated live.
        private void Apply()
        {
            CelestialBody body = GetSelectedBody();
            if (body == null) return;

            GalaxyConfig cfg = BuildConfigFromFields();
            GalacticDiskGenerator.ApplyDisk(body, cfg);
        }

        private void RemoveDisks()
        {
            CelestialBody body = GetSelectedBody();
            if (body == null) return;

            GalacticDiskGenerator.RemoveDisk(body.name);
        }
    }
}