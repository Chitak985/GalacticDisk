#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using GalacticDisk;
using UnityEditor;
using UnityEngine;

public static class RendererVersionChecks
{
    private static int checks;
    public static void Baseline() { Execute(true); }
    public static void Run() { Execute(false); }
    private static void Execute(bool baseline)
    {
        try
        {
            KSPUtil.ApplicationRootPath = Path.GetFullPath("TestGame"); HighLogic.LoadedScene = GameScenes.SPACECENTER;
            Directory.CreateDirectory(baseline ? "RendererV2Baseline" : "RendererV2Verification");
            foreach (string kind in new[] { "Spiral", "Bulge", "Ring", "Particles" })
            {
                var config = LegacyConfig(kind);
                UnityEngine.Random.InitState(7391);
                var root = new GameObject("Version verification "+kind) { layer = 10 };
                GalacticDiskGenerator.RegenerateDisk(root,config); var owner = root.GetComponent<GalacticDiskObject>(); owner.LateUpdate();
                var camera = new GameObject("Version camera").AddComponent<Camera>(); camera.cullingMask = 1<<10;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.farClipPlane = 1000;
                camera.transform.position = new Vector3(-70,140,-190); camera.transform.LookAt(Vector3.zero); camera.fieldOfView = 48;
                byte[] pixels = Capture(camera,480,320);
                string imagePath = (baseline?"RendererV2Baseline/":"RendererV2Verification/")+kind+".rgb";
                File.WriteAllBytes(imagePath,pixels);
                if (!baseline) Require(Equal(pixels,File.ReadAllBytes("RendererV2Baseline/"+kind+".rgb")),"Version 1 pixels match pre-change baseline: "+kind);
                if (kind == "Spiral")
                {
                    var s = VolumeConfig.SettingsFor(config); var extents = new Vector3(100,14,100);
                    var field = new GalaxyCloudField(); Color[] voxels = GalaxyCloudField.GeneratePixels(s,extents,field,false);
                    string digest;
                    using (var data = new MemoryStream())
                    using (var writer = new BinaryWriter(data))
                    {
                        foreach (Color c in voxels) { writer.Write(c.r); writer.Write(c.g); writer.Write(c.b); writer.Write(c.a); }
                        foreach (var star in GalaxyStarGenerator.Generate(s,field))
                        { writer.Write(star.position.x); writer.Write(star.position.y); writer.Write(star.position.z); writer.Write(star.size); writer.Write(star.color.r); writer.Write(star.color.g); writer.Write(star.color.b); writer.Write(star.color.a); }
                        writer.Flush(); using (var sha = SHA256.Create()) digest = Convert.ToBase64String(sha.ComputeHash(data.ToArray()));
                    }
                    if (baseline) File.WriteAllText("RendererV2Baseline/DensityAndStars.txt",digest);
                    else Require(digest == File.ReadAllText("RendererV2Baseline/DensityAndStars.txt"),"Version 1 density and star bytes match pre-change baseline");
                }
                owner.ReleaseRenderer(); UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(camera.gameObject);
            }
            if (!baseline)
            {
                VersionTwo();
                File.WriteAllText("RendererV2Verification/RendererV2.txt","PASS - Unity "+Application.unityVersion+"; "+checks+" checks.\n"+
                    "Version 1 spiral, analytic bulge, ring and particle pixels exactly match pre-change captures.\n"+
                    "Version 1 generated voxel and mesh-star bytes exactly match the pre-change hash.\n"+
                    "Version 2 parsing/editor/export, density separation, shared noise ownership, real rendered detail,\n"+
                    "repeatability and tiny camera movement, zoom filtering, dots-only draws and resource cleanup.\n"+
                    "KSP APIs are test substitutes; production DLL is built against installed KSP assemblies.\n");
            }
            Debug.Log(baseline?"GALACTICDISK_RENDERER_BASELINE_SAVED":"GALACTICDISK_RENDERER_V2_PASSED");
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    internal static GalaxyConfig LegacyConfig(string kind)
    {
        var c = new GalaxyConfig { planet = "RendererTest", starCount = 256, objectScale = 1, renderScale = 1, rotationX = 17, rotationY = 25 };
        VolumeConfig.ApplyMilkyWayPreset(c); c.starCount = 256; c.volume.cloudResolution = 64; c.volume.dustCloudCount = 30; c.volume.starFormingRegionCount = 20;
        if (kind == "Bulge") { c.bulgeFraction = 1; c.volume.enableDustClouds = false; c.volume.enableStarFormation = false; }
        if (kind == "Ring") { c.isRing = true; c.innerRingRadius = 40; c.colorInterarm = new Color(0.6f,0.8f,1,0.8f); }
        if (kind == "Particles") c.renderMode = DiskRenderMode.Particles;
        return c;
    }
    private static void VersionTwo()
    {
        Require(new GalaxySettings().rendererVersion == 1,"Omitted version preserves the old renderer");
        var values=new System.Collections.Generic.Dictionary<string,string> { {"planet","RendererTest"},{"renderMode","Volumetric"},{"rendererVersion","2"} };
        var parsed=ConfigEditor.ReadValues(false,values,false);
        Require(parsed.IsValid && parsed.config.volume.rendererVersion == 2,"Version 2 parses through the config editor");
        values["rendererVersion"]="3"; Require(!ConfigEditor.ReadValues(false,values,false).IsValid,"Unsupported versions are diagnosed");
        values["rendererVersion"]="2"; values["stellarGrainStrength"]="NaN";
        Require(!ConfigEditor.ReadValues(false,values,false).IsValid,"Nonfinite detail parameters are diagnosed");
        Require(!ConfigEditor.Schema(true).ContainsKey("rendererVersion"),"Ring configs keep the original renderer");
        var toolbar=new GalacticDiskToolbar(); Invoke(toolbar,"PopulateFieldsFromConfig",parsed.config);
        var inputs=(System.Collections.Generic.Dictionary<string,string>)Invoke(toolbar,"CaptureFieldValues");
        var fromFields=ConfigEditor.ReadValues(false,inputs,false);
        Require(fromFields.IsValid && fromFields.config.volume.rendererVersion == 2,"Version 2 editor fields round-trip");
        string exported=(string)Invoke(toolbar,"GenerateConfigText",fromFields.config,"RendererTest");
        Require(GalaxyConfig.FromConfigNode(ConfigNode.Parse(exported).GetNode("GalacticDiskDefinition")).volume.rendererVersion == 2,"Version selection is exported");

        var camera=new GameObject("V2 oblique").AddComponent<Camera>(); camera.cullingMask=1<<10;
        camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.black; camera.farClipPlane=10000; camera.fieldOfView=48;
        camera.transform.position=new Vector3(-70,140,-190); camera.transform.LookAt(Vector3.zero);
        var old=LegacyConfig("Spiral");
        var legacyRoot=new GameObject("V1 isolation") {layer=10}; GalacticDiskGenerator.RegenerateDisk(legacyRoot,old); legacyRoot.GetComponent<GalacticDiskObject>().LateUpdate();
        var legacyMaterial=VolumeMaterial(legacyRoot);
        Require(legacyMaterial.shader.name == "GalacticDisk/Volume" && GalaxyDetailNoise.Users == 0,"Old galaxies use their unchanged shader with no new noise allocation");
        byte[] legacyPixels=Capture(camera,480,320);
        old.volume.diffuseDiskIntensity=8; old.volume.armVariation=0.5f; old.volume.stellarGrainStrength=2; old.volume.dustFilamentStrength=1;
        GalacticDiskGenerator.RegenerateDisk(legacyRoot,old); legacyRoot.GetComponent<GalacticDiskObject>().LateUpdate();
        Require(Equal(legacyPixels,Capture(camera,480,320)),"Version 2 controls have no effect in version 1");
        double oldTime=Measure(camera);
        var legacyField=Field(legacyRoot);
        var c=LegacyConfig("Spiral"); c.volume.rendererVersion=2; c.starCount=1;
        var root=new GameObject("V2 galaxy") {layer=10}; GalacticDiskGenerator.RegenerateDisk(root,c); root.GetComponent<GalacticDiskObject>().LateUpdate();
        Require(!ReferenceEquals(Field(root),legacyField),"Version 2 density cannot reuse a version 1 cache entry");
        Remove(legacyRoot);
        var material=VolumeMaterial(root);
        Require(material.shader.name == "GalacticDisk/VolumeV2" && root.GetComponentsInChildren<ParticleSystem>().Length == 0,"New renderer is volumetric and uses no particle system");
        var noise=(Texture3D)material.GetTexture("_DetailNoise");
        Require(noise != null && noise.wrapMode == TextureWrapMode.Repeat && noise.mipmapCount > 1 && GalaxyDetailNoise.Users == 1,
            "Seamless shared dust noise has filtered mip levels; texture="+(noise==null?"null":noise.wrapMode+", mips="+noise.mipmapCount)+", users="+GalaxyDetailNoise.Users);
        byte[] detailed=Capture(camera,960,640);
        double newTime=Measure(camera);
        File.WriteAllText("RendererV2Verification/RenderTiming.txt","Fixed 960x640 oblique camera, five warm renders with synchronous readback.\n"+
            "Version 1: "+oldTime.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+" ms; Version 2: "+newTime.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+" ms per capture.\n"+
            "This is a local Unity render/readback comparison, not a KSP FPS benchmark. Density generation and PNG encoding are excluded.\n");
        Require(Brightness(detailed)>100000,"The new renderer draws visible light");
        Require(Equal(detailed,Capture(camera,960,640)),"Stationary views are deterministic");
        camera.transform.position+=Vector3.right*0.001f;
        Require(Difference(detailed,Capture(camera,960,640))<2,"Tiny camera movement does not cause global brightness flicker");
        camera.transform.position-=Vector3.right*0.001f;
        material.SetFloat("_GrainStrength",1); material.SetFloat("_GrainFrequency",180);
        camera.name="V2 grain"; byte[] grain=Capture(camera,960,640);
        material.SetFloat("_GrainStrength",0); camera.name="V2 without grain";
        Require(Difference(grain,Capture(camera,960,640))>0.01,"Procedural grain changes the image without increasing starCount");
        material.SetFloat("_GrainStrength",c.volume.stellarGrainStrength); material.SetFloat("_GrainFrequency",c.volume.stellarGrainFrequency);
        material.SetFloat("_FilamentStrength",0); camera.name="V2 without filaments";
        Require(Difference(detailed,Capture(camera,960,640))>0.01,"Dust filaments contribute visible detail"); material.SetFloat("_FilamentStrength",c.volume.dustFilamentStrength);
        camera.orthographic=true; camera.orthographicSize=120; camera.name="V2 orthographic";
        Require(Brightness(Capture(camera,640,480))>10000,"Orthographic map cameras render the new mode"); camera.orthographic=false;
        camera.transform.position=new Vector3(0,0.1f,3); camera.transform.rotation=Quaternion.Euler(0,20,0); camera.name="V2 inside";
        Require(Brightness(Capture(camera,480,320))>0,"Inside/grazing views remain finite and visible");
        var another=new GameObject("V2 shared field") {layer=10}; GalacticDiskGenerator.RegenerateDisk(another,c); another.GetComponent<GalacticDiskObject>().LateUpdate();
        Require(ReferenceEquals(Field(root),Field(another)) && ReferenceEquals(noise,VolumeMaterial(another).GetTexture("_DetailNoise")),"Matching new galaxies share both density and noise");
        Require(GalaxyDetailNoise.Users == 2,"Each renderer retains ownership of shared noise"); Remove(another);
        var s=VolumeConfig.SettingsFor(c); var field=Field(root); var regions=GalaxyCloudField.GenerateRegions(s);
        Require(regions.Length==field.regions.Length,"Deferred region count matches generated regions");
        for(int i=0;i<regions.Length;i++) Require(regions[i]==field.regions[i],"Mesh cluster and gas region placement agree");
        Require(Mathf.Abs(GalaxyStructureV2.Angle(s,80,0,true)-GalaxyStructureV2.Angle(s,80,0))>0.01f,"Spiral branches separate from their parent arm");
        c.enableDistantDots=true;
        // Reinitialize to acquire the regular dot state for this config.
        GalacticDiskGenerator.RegenerateDisk(root,c); root.GetComponent<GalacticDiskObject>().LateUpdate();
        camera.transform.position=new Vector3(0,1e8f,0); camera.transform.LookAt(Vector3.zero); camera.name="V2 distant dots";
        Capture(camera,480,320); var stats=DiskRenderOrder.Stats(camera);
        Require(stats.dotDisks==1 && stats.detailedDisks==0 && stats.detailDraws==0,"Dot-only views submit neither grain nor volume draws");
        Remove(root); Require(GalaxyDetailNoise.Users==0 && noise==null,"Removing the last new galaxy releases shared detail resources");
        UnityEngine.Object.DestroyImmediate(camera.gameObject);
    }
    private static Material VolumeMaterial(GameObject root) { return (Material)typeof(VolumetricDiskRenderer).GetField("volumeMaterial",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(root.GetComponent<GalacticDiskObject>().volume); }
    private static GalaxyCloudField Field(GameObject root) { return (GalaxyCloudField)typeof(VolumetricDiskRenderer).GetField("cloudField",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(root.GetComponent<GalacticDiskObject>().volume); }
    private static void Remove(GameObject root) { root.GetComponent<GalacticDiskObject>().ReleaseRenderer(); UnityEngine.Object.DestroyImmediate(root); }
    private static long Brightness(byte[] pixels) { long sum=0; foreach(byte pixel in pixels) sum+=pixel; return sum; }
    private static double Measure(Camera camera)
    {
        var target=RenderTexture.GetTemporary(960,640,24,RenderTextureFormat.ARGB32); var previous=RenderTexture.active;
        var image=new Texture2D(960,640,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture=target;
            foreach(var renderer in UnityEngine.Object.FindObjectsOfType<VolumetricDiskRenderer>()) Invoke(renderer,"PrepareCamera",camera);
            camera.Render(); RenderTexture.active=target; image.ReadPixels(new Rect(0,0,960,640),0,0);
            var timer=System.Diagnostics.Stopwatch.StartNew();
            for(int i=0;i<5;i++) { camera.Render(); RenderTexture.active=target; image.ReadPixels(new Rect(0,0,960,640),0,0); }
            timer.Stop(); return timer.Elapsed.TotalMilliseconds/5;
        }
        finally { camera.targetTexture=null; RenderTexture.active=previous; RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(image); }
    }
    private static double Difference(byte[] a,byte[] b) { long sum=0; Require(a.Length==b.Length,"Comparable image dimensions"); for(int i=0;i<a.Length;i++) sum+=Math.Abs(a[i]-b[i]); return (double)sum/a.Length; }
    internal static byte[] Capture(Camera camera,int width,int height)
    {
        var target = RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32); var previous = RenderTexture.active;
        var image = new Texture2D(width,height,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture = target;
            foreach (var renderer in UnityEngine.Object.FindObjectsOfType<VolumetricDiskRenderer>()) Invoke(renderer,"PrepareCamera",camera);
            foreach (var renderer in UnityEngine.Object.FindObjectsOfType<StableParticleRenderer>()) Invoke(renderer,"PrepareCamera",camera);
            camera.Render(); RenderTexture.active = target; image.ReadPixels(new Rect(0,0,width,height),0,0); image.Apply();
            string path = "RendererV2Verification/"+camera.name+".png";
            if (Directory.Exists("RendererV2Verification")) File.WriteAllBytes(path,image.EncodeToPNG());
            return image.GetRawTextureData();
        }
        finally { camera.targetTexture = null; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(image); }
    }
    internal static object Invoke(object target,string name,params object[] args) { return target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Invoke(target,args); }
    private static bool Equal(byte[] a,byte[] b) { if (a.Length != b.Length) return false; for (int i=0;i<a.Length;i++) if (a[i]!=b[i]) return false; return true; }
    internal static void Require(bool value,string message) { checks++; if (!value) throw new Exception("Renderer version check failed: "+message); }
}
#endif
