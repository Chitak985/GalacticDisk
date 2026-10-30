#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using GalacticDisk;
using GalacticDisk.Rendering;
using UnityEditor;
using UnityEngine;

public static class IntegrationChecks
{
    private static int checks;
    public static void Run()
    {
        try { Execute(); Debug.Log("GALACTICDISK_INTEGRATION_PASSED: " + checks); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    private static void Execute()
    {
        KSPUtil.ApplicationRootPath = Path.GetFullPath("TestGame");
        var bodyObject = new GameObject("TestBody");
        var body = bodyObject.AddComponent<CelestialBody>();
        body.scaledBody = new GameObject("Scaled body");
        FlightGlobals.Bodies.Add(body);
        var generatorObject = new GameObject("Test generator");
        var generator = generatorObject.AddComponent<GalacticDiskGenerator>();
        typeof(GalacticDiskGenerator).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, generator);

        var rgb = new ConfigNode("GalacticDiskDefinition");
        rgb.AddValue("planet", body.name); rgb.AddValue("colorArms", "1, 0.5, 0.5"); rgb.AddValue("starCount", 256);
        var legacy = GalaxyConfig.FromConfigNode(rgb);
        Check(legacy.renderMode == DiskRenderMode.Particles && legacy.colorArms.a == 1f, "Legacy config/RGB alpha default");
        GalacticDiskGenerator.ApplyDisk(body, legacy);
        GameObject root = GalacticDiskGenerator.disksByPlanet[body.name];
        var owner = root.GetComponent<GalacticDiskObject>();
        Check(owner.ps != null && owner.ps.particleCount == 256, "Legacy generation");
        owner.ps.Clear();
        for (int i = 0; i < 30; i++) owner.Update();
        Check(owner.ps.particleCount == 256, "Cleared particle recovery");

        var config = new GalaxyConfig { planet = body.name };
        VolumeConfig.ApplyMilkyWayPreset(config);
        config.starCount = 1024; config.volume.cloudResolution = 64;
        config.volume.dustCloudCount = 18; config.volume.starFormingRegionCount = 12;
        config.volume.seed = 123456; config.volume.dustColor = new Color(0.2f, 0.1f, 0.03f, 0.4f);
        config.volume.volumeExposure = 1.4f;
        string correctRoot = KSPUtil.ApplicationRootPath;
        KSPUtil.ApplicationRootPath = Path.GetFullPath("MissingBundleGame");
        bool failed = false;
        try { GalacticDiskGenerator.RegenerateDisk(root, config); }
        catch (FileNotFoundException) { failed = true; }
        Check(failed && owner.ps != null && owner.ps.particleCount == 256 && owner.config == legacy,
            "Missing bundle must preserve existing renderer");
        KSPUtil.ApplicationRootPath = correctRoot;

        GalacticDiskGenerator.ApplyDisk(body, config);
        Check(root.GetComponentsInChildren<ParticleSystem>(true).Length == 0, "Volumetric mode has no ParticleSystem");
        Check(root.GetComponentsInChildren<ParticleSystemRenderer>(true).Length == 0, "Volumetric mode has no ParticleSystemRenderer");
        Check(owner.volume != null && owner.volume.StarCount == 1024, "Exact mesh star count");
        Check(root.GetComponentsInChildren<MeshRenderer>().Length >= 2, "Mesh stars and volume box");
        foreach (var child in root.GetComponentsInChildren<Transform>(true)) Check(child.gameObject.layer == 10, "Scaled-space layers");
        Check(GalacticDiskGenerator.GetConfig(body.name) == config, "Shared Load lookup");

        var toolbar = new GalacticDiskToolbar();
        Invoke(toolbar, "PopulateFieldsFromConfig", config);
        var fromFields = (GalaxyConfig)Invoke(toolbar, "BuildConfigFromFields");
        Check(fromFields.renderMode == config.renderMode, "Toolbar mode round-trip");
        foreach (var field in VolumeConfig.OptionFields)
            Check(VolumeConfig.Format(field.GetValue(fromFields.volume)) == VolumeConfig.Format(field.GetValue(config.volume)),
                "Toolbar option round-trip: " + field.Name);
        string exported = (string)Invoke(toolbar, "GenerateConfigText", fromFields, body.name);
        var loaded = GalaxyConfig.FromConfigNode(ConfigNode.Parse(exported).GetNode("GalacticDiskDefinition"));
        foreach (var field in VolumeConfig.OptionFields)
            Check(VolumeConfig.Format(field.GetValue(loaded.volume)) == VolumeConfig.Format(field.GetValue(config.volume)),
                "Config export/load: " + field.Name);
        Check(loaded.volume.dustColor.a == 0.4f && loaded.colorArms.a == config.colorArms.a, "RGBA round-trip");

        var settings = VolumeConfig.SettingsFor(config);
        settings.bulgeFraction = 0f; settings.interarmFraction = 0f; settings.armHighlightChance = 0f;
        settings.enableStarFormation = false; settings.colorArms.a = 0f;
        foreach (var star in GalaxyStarGenerator.Generate(settings, null))
        {
            Check(star.color.a == 0, "Mesh alpha zero retained");
            Check(star.size >= settings.minStarSize && star.size <= settings.maxStarSize, "Mesh star size bounds");
        }

        config.objectScale = 2f; config.renderScale = 100000f; config.rotationPeriod = 100f;
        GalacticDiskGenerator.ApplyDisk(body, config);
        Check(Mathf.Abs(owner.renderRoot.transform.lossyScale.x - 1f) < 0.001f, "Numerically stable proxy scale");
        Planetarium.time = 25;
        owner.LateUpdate();
        Check(Quaternion.Angle(root.transform.rotation, Quaternion.Euler(config.rotationX, config.rotationY + 90f, config.rotationZ)) < 0.01f,
            "Shared simulated-time rotation");
        root.transform.localScale = Vector3.one * 123f; owner.LateUpdate();
        Check(root.transform.localScale == Vector3.one && Mathf.Abs(owner.renderRoot.transform.lossyScale.x - 1f) < 0.001f,
            "Neutral root and visible scale recovery");
        UnityEngine.Object.DestroyImmediate(owner.renderRoot);
        for (int i = 0; i < 30; i++) owner.Update();
        Check(owner.volume != null && owner.volume.IsReady && root.GetComponentsInChildren<ParticleSystem>(true).Length == 0,
            "Volumetric child recovery");
        UnityEngine.Object.DestroyImmediate(root);
        for (int i = 0; i < 60; i++) Invoke(generator, "Update");
        root = GalacticDiskGenerator.disksByPlanet[body.name]; owner = root.GetComponent<GalacticDiskObject>();
        Check(root != null && owner.volume != null, "Scene/root recreation");
        GalacticDiskGenerator.ApplyDisk(body, legacy);
        Check(owner.volume == null && owner.ps != null && owner.ps.particleCount == 256, "Switch back to particles");

        // Render the real KSP renderer using the built bundle, without the demo's post-process effect.
        config.objectScale = 1f; config.renderScale = 100000f; config.rotationPeriod = 0f;
        VolumeConfig.ApplyMilkyWayPreset(config);
        GalacticDiskGenerator.ApplyDisk(body, config); owner.LateUpdate();
        var cameraObject = new GameObject("Scaled-space test camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.cullingMask = 1 << 10; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        camera.fieldOfView = 48f; camera.farClipPlane = 1000f;
        camera.transform.position = new Vector3(-70f, 140f, -190f);
        camera.transform.LookAt(Vector3.zero);
        Capture(camera, "KSP-Volumetric-Renderer.png");
        var occluder = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        occluder.layer = 10;
        // Put the occluder wholly in front of the volume, so no genuine foreground
        // galactic emission can contaminate the center-pixel assertion.
        occluder.transform.position = camera.transform.position * 0.5f;
        occluder.transform.localScale = Vector3.one * 18f;
        var opaque = new Material(Shader.Find("Standard"));
        opaque.color = Color.green;
        opaque.EnableKeyword("_EMISSION"); opaque.SetColor("_EmissionColor", Color.green);
        occluder.GetComponent<Renderer>().sharedMaterial = opaque;
        Color center = Capture(camera, "KSP-Volumetric-DepthOcclusion.png");
        Check(center.g > center.r * 1.5f && center.g > center.b * 1.5f,
            "Opaque scaled-space geometry occludes the galaxy; center=" + center);
        UnityEngine.Object.DestroyImmediate(occluder); UnityEngine.Object.DestroyImmediate(opaque);
        camera.farClipPlane = 20f;
        Capture(camera, "KSP-Volumetric-BeyondFarPlane.png");
        Check(camera.GetComponents<MonoBehaviour>().Length == 0, "No full-screen effects installed on game camera");
        UnityEngine.Object.DestroyImmediate(cameraObject);
        GalacticDiskGenerator.RemoveDisk(body.name);
        Check(!GalacticDiskGenerator.disksByPlanet.ContainsKey(body.name), "Remove workflow");
        UnityEngine.Object.DestroyImmediate(generatorObject); UnityEngine.Object.DestroyImmediate(body.scaledBody);
        UnityEngine.Object.DestroyImmediate(bodyObject);
        Directory.CreateDirectory("Verification");
        File.WriteAllText("Verification/Integration.txt", "PASS - Unity " + Application.unityVersion + "\n" + checks + " assertions.\n" +
            "Actual renderer/config/toolbar sources; test-only KSP API substitutes.\n" +
            "Legacy/RGB compatibility, clear recovery, shader bundle load and rollback, no particles in volume mode, mesh stars, RGBA,\n" +
            "all option round-trips, renderScale, simulated-time rotation, renderer/root recovery, mode switching, removal, and rendered previews.\n" +
            "The production DLL is separately compiled against actual KSP assemblies. This is not a live KSP play test.\n");
    }

    private static object Invoke(object target, string name, params object[] arguments)
    {
        try { return target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, arguments); }
        catch (TargetInvocationException error) { throw error.InnerException; }
    }
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException("Integration check failed: " + message);
    }
    public static Color Capture(Camera camera, string filename)
    {
        Directory.CreateDirectory("Verification");
        var target = RenderTexture.GetTemporary(1200, 800, 24, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        var texture = new Texture2D(1200, 800, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            // Edit-mode tests call callbacks explicitly; Play-mode tests use Unity's hook.
            if (!Application.isPlaying)
                foreach (var renderer in UnityEngine.Object.FindObjectsOfType<VolumetricDiskRenderer>())
                    Invoke(renderer, "PrepareCamera", camera);
            camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0,0,1200,800), 0,0); texture.Apply();
            File.WriteAllBytes("Verification/" + filename, texture.EncodeToPNG());
            long brightness = 0;
            foreach (var pixel in texture.GetPixels32()) brightness += pixel.r + pixel.g + pixel.b;
            Check(brightness > 1000000, "Rendered galaxy is not empty: " + filename);
            return texture.GetPixel(600, 400);
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
#endif
