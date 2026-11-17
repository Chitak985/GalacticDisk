#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using GalacticDisk;
using UnityEditor;
using UnityEngine;

public static class OpacityChecks
{
    private static int checks;
    public static void Run()
    {
        try
        {
            KSPUtil.ApplicationRootPath = Path.GetFullPath("TestGame");
            Configuration(); Geometry(); Atmosphere(); Rendering();
            Directory.CreateDirectory("OpacityVerification");
            File.WriteAllText("OpacityVerification/Opacity.txt", "PASS - Unity " + Application.unityVersion + "; " + checks + " checks.\n" +
                "Defaults, inherited night value, ring/galaxy export, validation, preset, geometric boundaries,\n" +
                "day/night/twilight, atmospheric altitude, airless worlds, local flight/map cameras,\n" +
                "rendered particles/mesh stars/fog/full volume, tilted inside/outside views, native fallback restoration.\n" +
                "Real Unity rendering with KSP API substitutes; not a live KSP flight.\n");
            Debug.Log("GALACTICDISK_OPACITY_PASSED: " + checks);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    private static void Configuration()
    {
        var body = new GameObject("Opacity body").AddComponent<CelestialBody>(); FlightGlobals.Bodies.Add(body);
        foreach (bool ring in new[] { false, true })
        {
            var node = new ConfigNode(ring ? RingConfig.NodeName : "GalacticDiskDefinition"); node.AddValue("planet", "Opacity body");
            var c = GalaxyConfig.FromConfigNode(node);
            Check(c.insideOpacityMultiplier == 1 && c.atmosphereOpacityMultiplier == 1 && c.atmosphereNightOpacityMultiplier == 1, "Legacy defaults");
            node.AddValue("atmosphereOpacityMultiplier", 0.23f); c = GalaxyConfig.FromConfigNode(node);
            Near(c.atmosphereNightOpacityMultiplier, 0.23f, "Omitted night inherits day");
            node.AddValue("atmosphereNightOpacityMultiplier", 0.8f); node.AddValue("insideOpacityMultiplier", 0.2f); c = GalaxyConfig.FromConfigNode(node);
            string text = ring ? RingConfig.Export(c,c.planet) : (string)Invoke(new GalacticDiskToolbar(),"GenerateConfigText",c,c.planet);
            var again = GalaxyConfig.FromConfigNode(ConfigNode.Parse(text).GetNode(node.name));
            Check(again.insideOpacityMultiplier == 0.2f && again.atmosphereNightOpacityMultiplier == 0.8f, "Opacity export round trip");
            var toolbar = new GalacticDiskToolbar(); Invoke(toolbar,"PopulateFieldsFromConfig",c);
            var inputs = (System.Collections.Generic.Dictionary<string,string>)Invoke(toolbar,"CaptureFieldValues");
            var edited = ConfigEditor.ReadValues(ring,inputs,false);
            Check(edited.IsValid && ConfigEditor.Differences(c,edited.config).Count == 0,"Editor fields round trip opacity");
        }
        var preset = new GalaxyConfig(); VolumeConfig.ApplyMilkyWayPreset(preset);
        Check(preset.insideOpacityMultiplier == 0.2f && preset.atmosphereOpacityMultiplier == 0.01f && preset.atmosphereNightOpacityMultiplier == 0.65f, "Milky Way preset");
        foreach (string field in DiskOpacity.Fields)
            foreach (string invalid in new[] { "-1", "NaN" })
                Check(ConfigEditor.Read("GalacticDiskDefinition\n{\n"+field+" = "+invalid+"\n}").errors.Exists(error => error.Contains(field)), "Invalid opacity rejected: "+field);
        FlightGlobals.Bodies.Clear(); UnityEngine.Object.DestroyImmediate(body.gameObject);
    }
    private static void Geometry()
    {
        var c = new GalaxyConfig { diskThickness = 10, bulgeFraction = 0 };
        Near(DiskOpacity.InsideWeight(c,new Vector3(60,0,30)),1,"In plane away from center");
        Near(DiskOpacity.InsideWeight(c,new Vector3(60,30,0)),0,"Equal center distance above plane is outside");
        float a = DiskOpacity.InsideWeight(c,new Vector3(60,4,0)), b = DiskOpacity.InsideWeight(c,new Vector3(60,5,0)), d = DiskOpacity.InsideWeight(c,new Vector3(60,6,0));
        Check(a == 1 && a > b && b > d && d == 0,"Smooth vertical boundary");
        Check(DiskOpacity.InsideWeight(c,new Vector3(90,0,0)) > DiskOpacity.InsideWeight(c,new Vector3(100,0,0)) && DiskOpacity.InsideWeight(c,new Vector3(111,0,0)) == 0,"Smooth outer rim");
        c.isRing = true; c.innerRingRadius = 40;
        Check(DiskOpacity.InsideWeight(c,Vector3.zero) == 0 && DiskOpacity.InsideWeight(c,new Vector3(60,0,0)) == 1,"Annulus and empty hole");
        Check(DiskOpacity.InsideWeight(c,new Vector3(39,0,0)) < DiskOpacity.InsideWeight(c,new Vector3(41,0,0)),"Smooth inner rim");
        c.isRing = false; c.bulgeFraction = 0.3f; c.bulgeScaleY = 10;
        Near(DiskOpacity.InsideWeight(c,new Vector3(0,15,0)),1,"Central bulge above thin plane");
        c.bulgeFraction = 0; Near(DiskOpacity.InsideWeight(c,new Vector3(0,15,0)),0,"Disabled bulge excluded");
    }
    private static void Atmosphere()
    {
        var planet = new GameObject("Atmosphere").AddComponent<CelestialBody>(); planet.atmosphere = true; planet.Radius = 600000; planet.atmosphereDepth = 70000;
        var star = new GameObject("Sun").AddComponent<CelestialBody>(); star.isStar = true; star.position = new Vector3d(1e12,0,0);
        FlightGlobals.Bodies.Add(planet); FlightGlobals.Bodies.Add(star);
        var c = new GalaxyConfig { insideOpacityMultiplier = 0.2f, atmosphereOpacityMultiplier = 0.01f, atmosphereNightOpacityMultiplier = 0.65f };
        Near(DiskOpacity.AtmosphereMultiplier(c,new Vector3d(600000,0,0)),0.01f,"Surface day");
        Near(DiskOpacity.AtmosphereMultiplier(c,new Vector3d(-600000,0,0)),0.65f,"Surface night");
        Near(DiskOpacity.AtmosphereMultiplier(c,new Vector3d(0,600000,0)),0.33f,"Twilight blends alternatives");
        Near(DiskOpacity.AtmosphereMultiplier(c,new Vector3d(635000,0,0)),0.505f,"Altitude fades toward space");
        Near(DiskOpacity.AtmosphereMultiplier(c,new Vector3d(670000,0,0)),1,"Outside atmosphere");
        planet.atmosphere = false; Near(DiskOpacity.AtmosphereMultiplier(c,new Vector3d(600000,0,0)),1,"Airless world"); planet.atmosphere = true;
        var camera = new GameObject("Scaled camera").AddComponent<Camera>(); camera.transform.position = Vector3.right * 100;
        Near(DiskOpacity.Evaluate(c,camera,Vector3.zero),0.002f,"Atmosphere multiplies inside");
        var local = new GameObject("Local camera").AddComponent<Camera>(); local.transform.position = Vector3.left * 600000;
        FlightCamera.fetch = local.gameObject.AddComponent<FlightCamera>(); FlightCamera.fetch.mainCamera = local; HighLogic.LoadedScene = GameScenes.FLIGHT;
        Near(DiskOpacity.Evaluate(c,camera,Vector3.zero),0.13f,"Precise local flight position");
        MapView.MapIsEnabled = true; Near(DiskOpacity.Evaluate(c,camera,Vector3.zero),0.002f,"Map uses its own position");
        FlightCamera.fetch = null; MapView.MapIsEnabled = false; HighLogic.LoadedScene = GameScenes.SPACECENTER; FlightGlobals.Bodies.Clear();
        foreach (var obj in new[] { planet.gameObject,star.gameObject,camera.gameObject,local.gameObject }) UnityEngine.Object.DestroyImmediate(obj);
    }
    private static void Rendering()
    {
        var body = new GameObject("Opacity body").AddComponent<CelestialBody>(); body.scaledBody = new GameObject("Scaled body"); FlightGlobals.Bodies.Add(body);
        var root = new GameObject("Opacity disk") { layer = 10 }; root.transform.SetParent(body.scaledBody.transform,false);
        var camera = new GameObject("Render camera").AddComponent<Camera>(); camera.cullingMask = 1 << 10; camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black; camera.farClipPlane = 1000; camera.fieldOfView = 60;
        var c = new GalaxyConfig { planet = body.name, minStarSize = 0.3f,maxStarSize = 1.8f,bulgeFraction = 0,diskThickness = 10,rotationX = 65,rotationZ = 25 };
        var atmosphere = new GameObject("Render atmosphere").AddComponent<CelestialBody>(); atmosphere.Radius = 100000; atmosphere.atmosphereDepth = 20000;
        var star = new GameObject("Render star").AddComponent<CelestialBody>(); star.isStar = true;
        FlightGlobals.Bodies.Add(atmosphere); FlightGlobals.Bodies.Add(star);
        c.volume.enableDustClouds = c.volume.enableStarFormation = c.volume.enableBar = false; c.volume.cloudResolution = 64; c.volume.volumeSteps = 64;
        for (int mode = 0; mode < 4; mode++)
        {
            c.renderMode = mode == 0 ? DiskRenderMode.Particles : DiskRenderMode.Volumetric; c.volume.enableVolume = mode != 1; c.starCount = mode == 2 ? 0 : 8000;
            c.insideOpacityMultiplier = 1; GalacticDiskGenerator.RegenerateDisk(root,c); var owner = root.GetComponent<GalacticDiskObject>(); owner.LateUpdate();
            camera.transform.position = root.transform.rotation * new Vector3(60,0,-30); camera.transform.LookAt(Vector3.zero);
            long normal = Capture(camera,owner,mode+"-normal"); Check(normal > 100000,"Visible baseline "+mode);
            c.insideOpacityMultiplier = 0; Check(Capture(camera,owner,mode+"-hidden") == 0,"Whole render mode fades to zero "+mode);
            c.insideOpacityMultiplier = 0.2f; long faded = Capture(camera,owner,mode+"-faded"); Check(faded > 0 && faded < normal*0.6,"Partial fade "+mode);
            Vector3 observer = camera.transform.position;
            atmosphere.position = new Vector3d((double)observer.x*6000,(double)observer.y*6000-atmosphere.Radius,(double)observer.z*6000);
            atmosphere.atmosphere = true; c.atmosphereOpacityMultiplier = 0; c.atmosphereNightOpacityMultiplier = 1;
            star.position = new Vector3d(atmosphere.position.x,1e12,atmosphere.position.z);
            Check(Capture(camera,owner,mode+"-day") == 0,"Atmospheric daytime renders fully faded "+mode);
            star.position = new Vector3d(atmosphere.position.x,-1e12,atmosphere.position.z);
            Check(Math.Abs(Capture(camera,owner,mode+"-night")-faded) < 100,"Night restores inside-only opacity "+mode);
            atmosphere.atmosphere = false; c.atmosphereOpacityMultiplier = c.atmosphereNightOpacityMultiplier = 1;
            c.insideOpacityMultiplier = 0; camera.transform.position = root.transform.rotation * new Vector3(60,30,0); camera.transform.LookAt(Vector3.zero);
            Check(Capture(camera,owner,mode+"-above") > 100000,"Equal-distance above tilted disk restores opacity "+mode);
            if (mode == 0)
            {
                camera.transform.position = root.transform.rotation * new Vector3(60,0,-30); owner.stableParticles.enabled = false; Invoke(owner.stableParticles,"OnDisable");
                owner.psR.enabled = true; Color original = owner.material.color; Invoke(owner,"PrepareNativeOpacity",camera);
                Check(owner.material.color.a == 0,"Native fallback applies fade"); Invoke(owner,"RestoreNativeOpacity",camera);
                Check(owner.material.color == original,"Native fallback restores tint"); owner.stableParticles.enabled = true; owner.psR.enabled = false;
            }
        }
        root.GetComponent<GalacticDiskObject>().ReleaseRenderer(); FlightGlobals.Bodies.Clear();
        foreach (var obj in new[] { root,body.scaledBody,body.gameObject,camera.gameObject,atmosphere.gameObject,star.gameObject }) UnityEngine.Object.DestroyImmediate(obj);
    }
    private static long Capture(Camera camera,GalacticDiskObject owner,string name)
    {
        Directory.CreateDirectory("OpacityVerification"); var target = RenderTexture.GetTemporary(480,320,24,RenderTextureFormat.ARGB32);
        var texture = new Texture2D(480,320,TextureFormat.RGB24,false); var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target;
            if (owner.volume != null) Invoke(owner.volume,"PrepareCamera",camera);
            if (owner.stableParticles != null && owner.stableParticles.enabled) Invoke(owner.stableParticles,"PrepareCamera",camera);
            camera.Render(); RenderTexture.active = target; texture.ReadPixels(new Rect(0,0,480,320),0,0); texture.Apply();
            long light = ParticleRenderingChecks.Brightness(texture.GetPixels32()); File.WriteAllBytes("OpacityVerification/"+name+".png",texture.EncodeToPNG());
            Debug.Log("OPACITY_IMAGE: "+name+" brightness="+light); return light;
        }
        finally { camera.targetTexture = null; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(texture); }
    }
    private static object Invoke(object target,string method,params object[] args) { return target.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args); }
    private static void Near(float value,float expected,string message) { Check(Mathf.Abs(value-expected)<0.0001f,message+": "+value); }
    private static void Check(bool condition,string message) { checks++; if (!condition) throw new InvalidOperationException("Opacity check failed: "+message); }
}
#endif
