#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using GalacticDisk;
using GalacticDisk.Rendering;
using UnityEditor;
using UnityEngine;

public static class RingChecks
{
    private static int checks;
    public static void Run()
    {
        try { Execute(); Debug.Log("GALACTICDISK_RINGS_PASSED: " + checks); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    private static void Execute()
    {
        KSPUtil.ApplicationRootPath = Path.GetFullPath("TestGame");
        var config = GalaxyConfig.FromConfigNode(ConfigNode.Parse(File.ReadAllText("RingExample.txt")).GetNode(RingConfig.NodeName));
        Check(config.isRing && config.renderMode == DiskRenderMode.Volumetric, "Ring node selects shape and mode");
        Check(config.starCount == 140000 && config.galaxyRadius == 100f && config.innerRingRadius == 50f && config.diskThickness == 5.5f,
            "User example count and dimensions");
        Check(config.minStarSize == 0.055f && config.maxStarSize == 0.19f && config.volume.starBrightness == 1.2f && config.volume.seed == 985123,
            "Renamed size/brightness and shared seed");
        var rgb = new ConfigNode(RingConfig.NodeName); rgb.AddValue("color", "1, 0.5, 0.5");
        var rgbConfig = GalaxyConfig.FromConfigNode(rgb);
        Check(rgbConfig.colorInterarm.a == 1f && rgbConfig.renderMode == DiskRenderMode.Particles, "RGB alpha and default mode compatibility");
        var settings = VolumeConfig.SettingsFor(config);
        Check(settings.isRing && settings.innerRingRadius == 50f && settings.armCount == 0 && settings.bulgeFraction == 0f &&
            !settings.enableBar && !settings.enableStarFormation, "No galaxy populations leak into rings");
        settings.starCount = 4096;
        var stars = GalaxyStarGenerator.Generate(settings, null);
        var repeated = GalaxyStarGenerator.Generate(settings, null);
        Color32 expected = settings.colorInterarm;
        for (int i = 0; i < stars.Length; i++)
        {
            float radius = new Vector2(stars[i].position.x, stars[i].position.z).magnitude;
            Check(radius >= 49.999f && radius <= 100.001f, "Particle centers remain in annulus");
            Check(stars[i].color.Equals(expected), "One exact RGBA color without highlight or RGB jitter");
            Check(stars[i].position == repeated[i].position && stars[i].size == repeated[i].size, "Seed is deterministic");
            Check(stars[i].size >= settings.minStarSize && stars[i].size <= settings.maxStarSize, "Particle size bounds");
        }
        var clouds = GalaxyCloudField.Build(settings, new Vector3(100f, 8.25f, 100f));
        Check(clouds.dustMass > 0f && clouds.nebulaMass == 0f && clouds.youngGlowMass == 0f && clouds.regions.Length == 0,
            "Ring contains dust without extra emission populations");
        UnityEngine.Object.DestroyImmediate(clouds.texture);
        settings.innerRingRadius = 99.999f;
        foreach (var star in GalaxyStarGenerator.Generate(settings, null))
            Check(new Vector2(star.position.x, star.position.z).magnitude >= 99.998f, "Thin rings terminate and respect their inner radius");

        var toolbar = new GalacticDiskToolbar(); Invoke(toolbar, "PopulateFieldsFromConfig", config);
        var fromFields = (GalaxyConfig)Invoke(toolbar, "BuildConfigFromFields");
        string exported = (string)Invoke(toolbar, "GenerateConfigText", fromFields, "RingBody");
        Check(exported.StartsWith(RingConfig.NodeName) && exported.Contains("particleBrightness = 1.2") &&
            !exported.Contains("starCount =") && !exported.Contains("galaxyRadius =") && !exported.Contains("armCount =") &&
            !exported.Contains("enableBar =") && !exported.Contains("starFormingRegion"), "Export uses only the ring schema");
        var loaded = GalaxyConfig.FromConfigNode(ConfigNode.Parse(exported).GetNode(RingConfig.NodeName));
        Check(loaded.isRing && loaded.innerRingRadius == 50f && loaded.colorInterarm == config.colorInterarm && loaded.starCount == config.starCount,
            "Toolbar and config round-trip preserve shape, hole, color and count");
        foreach (var field in VolumeConfig.OptionFields)
            if (RingConfig.IsVolumeOption(field.Name))
                Check(VolumeConfig.Format(field.GetValue(loaded.volume)) == VolumeConfig.Format(field.GetValue(config.volume)),
                    "Ring option round-trip: " + field.Name);

        var body = new GameObject("RingBody").AddComponent<CelestialBody>();
        body.scaledBody = new GameObject("Ring scaled parent"); FlightGlobals.Bodies.Add(body);
        var root = new GameObject("Ring test") { layer = 10 }; root.transform.SetParent(body.scaledBody.transform, false);
        config.planet = body.name; config.starCount = 8192; config.objectScale = 1f; config.volume.cloudResolution = 64;
        config.volume.dustCloudCount = 30; config.renderMode = DiskRenderMode.Particles;
        GalacticDiskGenerator.RegenerateDisk(root, config);
        var owner = root.GetComponent<GalacticDiskObject>(); owner.LateUpdate();
        Check(owner.ps != null && owner.ps.particleCount == 8192, "Particle mode creates exact ring count");
        Check(Mathf.Abs(owner.material.color.r - 1.2f) < 0.001f, "Particle brightness applies in legacy renderer");
        foreach (var particle in owner.particles)
            Check(particle.startColor.Equals(expected) && new Vector2(particle.position.x, particle.position.z).magnitude * config.renderScale >= 49.999f,
                "Legacy particles use the same annulus and single color");
        foreach (float inner in new[] { -1f, 100f, float.NaN, float.PositiveInfinity })
        {
            config.innerRingRadius = inner; bool rejected = false;
            try { GalacticDiskGenerator.RegenerateDisk(root, config); } catch (ArgumentException) { rejected = true; }
            Check(rejected && owner.ps != null && owner.ps.particleCount == 8192, "Invalid radius preserves existing renderer");
        }
        config.innerRingRadius = 50f;
        var camera = new GameObject("Ring camera").AddComponent<Camera>();
        camera.cullingMask = 1 << 10; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        camera.transform.position = Vector3.up * 260f; camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
        camera.fieldOfView = 48f; camera.farClipPlane = 1000f;
        owner.ps.Simulate(0.01f, true, false, false); owner.ps.Pause(true);
        Capture(camera, "Particles.png", true);
        config.starCount = 0; config.renderMode = DiskRenderMode.Volumetric; config.volume.enableDustClouds = false;
        GalacticDiskGenerator.RegenerateDisk(root, config); owner.LateUpdate();
        Check(root.GetComponentsInChildren<ParticleSystem>(true).Length == 0 && owner.volume.StarCount == 0, "Volumetric ring needs no particles or mesh stars");
        Capture(camera, "Fog-only.png", true);
        config.volume.enableDustClouds = true;
        GalacticDiskGenerator.RegenerateDisk(root, config); owner.LateUpdate();
        Capture(camera, "Fog-and-dust.png", true);
        config.colorInterarm.a = 0f; config.volume.enableDustClouds = false;
        GalacticDiskGenerator.RegenerateDisk(root, config); owner.LateUpdate();
        Capture(camera, "Transparent.png", false);
        config.innerRingRadius = 0f; config.colorInterarm.a = 0.8f;
        GalacticDiskGenerator.RegenerateDisk(root, config); owner.LateUpdate();
        Capture(camera, "Full-disk.png", true, false);
        owner.ReleaseRenderer(); UnityEngine.Object.DestroyImmediate(root);

        // Exercise the real startup enumeration with both definition node types.
        config.innerRingRadius = 50f;
        GameDatabase.Instance.nodes.Add(ConfigNode.Parse(RingConfig.Export(config, body.name)).GetNode(RingConfig.NodeName));
        var galaxy = new ConfigNode("GalacticDiskDefinition"); galaxy.AddValue("planet", body.name); galaxy.AddValue("starCount", 128);
        GameDatabase.Instance.nodes.Add(galaxy);
        var generator = new GameObject("Ring loader").AddComponent<GalacticDiskGenerator>(); generator.LoadDefinitions();
        var owners = body.scaledBody.GetComponentsInChildren<GalacticDiskObject>();
        Check(owners.Length == 2 && GalacticDiskGenerator.GetConfig(body.name).isRing, "Startup loads galaxy and ring on the same body");
        bool legacyFound = false, ringFound = false;
        foreach (var disk in owners)
        {
            legacyFound |= disk.ps != null && disk.ps.particleCount == 128;
            ringFound |= disk.config.isRing && disk.volume != null;
            disk.ReleaseRenderer(); UnityEngine.Object.DestroyImmediate(disk.gameObject);
        }
        Check(legacyFound && ringFound, "Ring does not replace an existing galaxy definition");
        generator.OnDestroy(); UnityEngine.Object.DestroyImmediate(generator.gameObject);
        UnityEngine.Object.DestroyImmediate(camera.gameObject); UnityEngine.Object.DestroyImmediate(body.scaledBody); UnityEngine.Object.DestroyImmediate(body.gameObject);
        Directory.CreateDirectory("RingVerification");
        File.WriteAllText("RingVerification/Rings.txt", "PASS - Unity " + Application.unityVersion + "\n" + checks + " assertions.\n" +
            "User example schema, RGB/RGBA, deterministic annulus positions, single color, thin/invalid radii,\n" +
            "toolbar/config round-trips, particle brightness, galaxy/ring coexistence, dust without nebulae,\n" +
            "and rendered particle/fog/dust/transparent/zero-inner-radius cases.\n" +
            "KSP APIs are substitutes. The production DLL is separately built against KSP.\n");
    }
    private static void Capture(Camera camera, string filename, bool visible, bool hole = true)
    {
        Directory.CreateDirectory("RingVerification");
        var target = RenderTexture.GetTemporary(640, 640, 24, RenderTextureFormat.ARGB32);
        var texture = new Texture2D(640, 640, TextureFormat.RGB24, false); var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target;
            foreach (var renderer in UnityEngine.Object.FindObjectsOfType<VolumetricDiskRenderer>()) Invoke(renderer, "PrepareCamera", camera);
            camera.Render(); RenderTexture.active = target; texture.ReadPixels(new Rect(0,0,640,640),0,0); texture.Apply();
            long brightness = 0; foreach (Color32 p in texture.GetPixels32()) brightness += p.r + p.g + p.b;
            Debug.Log("RING_IMAGE: " + filename + " brightness=" + brightness + " center=" + texture.GetPixel(320,320));
            Check(visible ? brightness > 1000 : brightness == 0, "Image visibility: " + filename);
            if (visible && hole) Check(texture.GetPixel(320,320).maxColorComponent < 0.005f, "Central opening is empty: " + filename);
            if (visible && !hole) Check(texture.GetPixel(320,320).maxColorComponent > 0.05f, "Zero inner radius fills center");
            File.WriteAllBytes("RingVerification/" + filename, texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(texture);
        }
    }
    private static object Invoke(object target, string name, params object[] args)
    {
        try { return target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args); }
        catch (TargetInvocationException error) { throw error.InnerException; }
    }
    private static void Check(bool condition, string message)
    {
        checks++; if (!condition) throw new InvalidOperationException("Ring check failed: " + message);
    }
}
#endif
