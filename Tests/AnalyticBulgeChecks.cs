#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using GalacticDisk;
using UnityEditor;
using UnityEngine;

public static class AnalyticBulgeChecks
{
    private static int checks;
    public static void Run()
    {
        try
        {
            KSPUtil.ApplicationRootPath = Path.GetFullPath("TestGame");
            var settings = new GalaxySettings { bulgeFraction = 1,enableDustClouds = false,enableStarFormation = false };
            var first = GalaxyCloudCache.Acquire(settings,new Vector3(100,40,100)); settings.seed++; settings.bulgeScaleY = 30;
            var other = GalaxyCloudCache.Acquire(settings,new Vector3(200,120,200));
            Check(ReferenceEquals(first,other) && first.texture.width == 1 && GalaxyCloudCache.TextureBytes == 8,"Different pure bulges share a single 8-byte placeholder");
            GalaxyCloudCache.Release(first); Check(other.texture != null,"Shared analytic ownership"); GalaxyCloudCache.Release(other);
            settings.bulgeFraction = 0.99f; Check(!GalaxyCloudCache.IsAnalyticBulge(settings),"A remaining disk retains its noise");
            settings.bulgeFraction = 1; settings.enableDustClouds = true; Check(!GalaxyCloudCache.IsAnalyticBulge(settings),"Dust requires a full field");
            settings.enableDustClouds = false; settings.enableStarFormation = true; settings.starFormingRegionCount = 1;
            Check(!GalaxyCloudCache.IsAnalyticBulge(settings),"Nebulae require a full field");
            var body = new GameObject("Analytic body").AddComponent<CelestialBody>(); body.scaledBody = new GameObject("Analytic parent"); FlightGlobals.Bodies.Add(body);
            var root = new GameObject("Analytic disk") { layer = 10 }; root.transform.SetParent(body.scaledBody.transform,false);
            var camera = new GameObject("Analytic camera").AddComponent<Camera>(); camera.cullingMask = 1<<10; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f,0.03f,0.04f); camera.farClipPlane = 1000;
            var config = new GalaxyConfig { planet = body.name,renderMode = DiskRenderMode.Volumetric,bulgeFraction = 1,starCount = 2000,
                bulgeScaleX = 15,bulgeScaleY = 10,bulgeScaleZ = 20,rotationX = 24,rotationY = 17,rotationZ = 49 };
            config.volume.enableDustClouds = config.volume.enableStarFormation = false; config.volume.cloudResolution = 64;
            config.volume.volumeIntensity = 4;
            foreach(bool bar in new[] { false,true })
            {
                config.volume.enableBar = bar; config.volume.barFraction = 0.6f;
                GalacticDiskGenerator.RegenerateDisk(root,config); var owner = root.GetComponent<GalacticDiskObject>(); owner.LateUpdate();
                var renderer = owner.volume; var material = (Material)Get(renderer,"volumeMaterial"); var shared = (GalaxyCloudField)Get(renderer,"cloudField");
                var extents = (Vector3)Invoke(renderer,"VolumeExtents"); var full = GalaxyCloudField.Build(VolumeConfig.SettingsFor(config),extents);
                foreach(bool inside in new[] { false,true })
                {
                    camera.transform.position = inside ? Vector3.zero : new Vector3(-70,90,-180);
                    camera.transform.rotation = inside ? Quaternion.identity : Quaternion.LookRotation(-camera.transform.position);
                    material.SetTexture("_CloudField",full.texture); material.SetFloat("_AnalyticBulge",0);
                    var reference = Capture(camera,renderer,"Reference-"+bar+"-"+inside+".png");
                    material.SetTexture("_CloudField",shared.texture); material.SetFloat("_AnalyticBulge",1);
                    var analytic = Capture(camera,renderer,"Analytic-"+bar+"-"+inside+".png");
                    long difference = 0, brightness = ParticleRenderingChecks.Brightness(reference);
                    for(int i=0;i<reference.Length;i++) difference += Math.Abs(reference[i].r-analytic[i].r)+Math.Abs(reference[i].g-analytic[i].g)+Math.Abs(reference[i].b-analytic[i].b);
                    Check(brightness > 100000 && difference <= Math.Max(3,brightness/10000),"Analytic and original cloud-backed images match: bar="+bar+" inside="+inside+" difference="+difference);
                }
                UnityEngine.Object.DestroyImmediate(full.texture); owner.ReleaseRenderer();
            }
            FlightGlobals.Bodies.Clear();
            foreach(var obj in new[] { root,body.scaledBody,body.gameObject,camera.gameObject }) UnityEngine.Object.DestroyImmediate(obj);
            Check(GalaxyCloudCache.ActiveCount == 0 && GalaxyCloudCache.TextureBytes == 0,"Analytic resources release completely");
            Directory.CreateDirectory("AnalyticVerification"); File.WriteAllText("AnalyticVerification/Analytic.txt","PASS - Unity "+Application.unityVersion+"; "+checks+" checks.\n" +
                "Pure spheroids/bars skip density generation, share an 8-byte placeholder, and preserve cloud-backed\n" +
                "rendered output from inside/outside with and without a bar. Mixed/dusty/nebular systems retain full fields.\n");
            Debug.Log("GALACTICDISK_ANALYTIC_PASSED: "+checks);
        }
        catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    private static Color32[] Capture(Camera camera,VolumetricDiskRenderer renderer,string name)
    {
        Directory.CreateDirectory("AnalyticVerification"); var target = RenderTexture.GetTemporary(480,320,24,RenderTextureFormat.ARGB32);
        var image = new Texture2D(480,320,TextureFormat.RGB24,false); var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target; Invoke(renderer,"PrepareCamera",camera); camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0,0,480,320),0,0); image.Apply(); File.WriteAllBytes("AnalyticVerification/"+name,image.EncodeToPNG()); return image.GetPixels32();
        }
        finally { camera.targetTexture = null; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(image); }
    }
    private static object Get(object target,string name) { return target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(target); }
    private static object Invoke(object target,string name,params object[] args) { return target.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(target,args); }
    private static void Check(bool condition,string message) { checks++; if(!condition) throw new InvalidOperationException("Analytic check failed: "+message); }
}
#endif
