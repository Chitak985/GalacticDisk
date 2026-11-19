#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using GalacticDisk;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class PerformanceChecks
{
    private static int checks;
    private static readonly string[] oldHashes = {
        "58C4378C6CF7D6F4115B8422B59D2E9767C6F0E7869DF52345C30E546A7C1300",
        "2A7219D5383310ABA33C5C7A14CC0C678642D3B62E260B15C0082BB973861A1A",
        "17B61662A94CFF84F91616B85C5B0A7C20A7561E0F040D320CBFC0071C47F5E0",
        "93BFA0AD82887BD927D6322D4ABD31D0AC09BE3A009193643705EF4E7D98941D"
    };
    public static void Run()
    {
        try
        {
            KSPUtil.ApplicationRootPath = Path.GetFullPath("TestGame");
            Directory.CreateDirectory("PerformanceVerification");
            var extents = new Vector3(100,14,100);
            for(int i=0;i<4;i++)
            {
                var s = Settings(i); var field = new GalaxyCloudField();
                Color[] pixels = GalaxyCloudField.GeneratePixels(s,extents,field);
                Check(Hash(pixels) == oldHashes[i],"Density pixels match the original algorithm exactly, seed "+s.seed);
                var regions = GalaxyCloudField.GenerateRegions(s);
                Check(regions.Length == field.regions.Length,"Deferred region count");
                for(int r=0;r<regions.Length;r++) Check(regions[r].Equals(field.regions[r]),"Deferred metadata preserves nebula and young-star placement");
                if(i == 0)
                    Check(Hash(GalaxyCloudField.GeneratePixels(s,extents,new GalaxyCloudField(),false)) == oldHashes[i],"Serial and parallel results agree");
            }
            var warm = Settings(0); warm.cloudResolution = 64;
            var texture = GalaxyCloudField.Build(warm,extents); UnityEngine.Object.DestroyImmediate(texture.texture);
            double unique = 0;
            for(int i=0;i<4;i++)
            {
                var timer = Stopwatch.StartNew(); texture = GalaxyCloudField.Build(Settings(i),extents); timer.Stop(); unique += timer.Elapsed.TotalMilliseconds;
                UnityEngine.Object.DestroyImmediate(texture.texture);
            }
            double shared = CacheChecks(extents);
            VisibilityChecks();
            double registry = RegistrationBenchmark();
            RenderVisibilityChecks();
            double population = PopulationBenchmark();
            string report = "PASS - Unity "+Application.unityVersion+"; "+checks+" checks.\n" +
                "Original density fingerprints preserved for four seeds; serial/parallel equivalence.\n" +
                "Shared field ownership, seed/resolution invalidation, external texture loss, cleanup,\n" +
                "conservative perspective/orthographic culling with astronomical coordinates and far-plane support.\n" +
                "Four unique 192x32x192 volumes: "+Ms(unique)+" ms\n" +
                "Acquire 2000 matching fields (including one cold build): "+Ms(shared)+" ms; one 9 MiB texture.\n" +
                "Register 2000 sources and sort once: "+Ms(registry)+" ms\n" +
                "Build 2000 volumetric galaxies x 2000 stars, shared density settings: "+Ms(population)+" ms\n" +
                "KSP APIs are substitutes. Timings are local measurements, not a prediction for 2000 unique fields in KSP.\n";
            File.WriteAllText("PerformanceVerification/Performance.txt",report);
            UnityEngine.Debug.Log(report); UnityEngine.Debug.Log("GALACTICDISK_PERFORMANCE_PASSED: "+checks);
        }
        catch(Exception e) { UnityEngine.Debug.LogException(e); EditorApplication.Exit(1); }
    }
    private static GalaxySettings Settings(int seed)
    {
        var s = new GalaxySettings { seed = 985123+seed,starCount = 2000 }; s.Validate(); return s;
    }
    private static double CacheChecks(Vector3 extents)
    {
        int builds = GalaxyCloudCache.BuildCount; var s = Settings(0); var fields = new GalaxyCloudField[2000];
        var timer = Stopwatch.StartNew();
        for(int i=0;i<fields.Length;i++)
        {
            s.starCount = 2000+i; s.colorArms = new Color(i/2000f,0.5f,1,1); s.volumeSteps = 32+i%100;
            fields[i] = GalaxyCloudCache.Acquire(s,extents);
            Check(ReferenceEquals(fields[i],fields[0]),"Colors/star count/steps do not duplicate density textures");
        }
        timer.Stop();
        Check(GalaxyCloudCache.BuildCount-builds == 1 && GalaxyCloudCache.ActiveCount == 1 && GalaxyCloudCache.TextureBytes == 192L*32*192*8,"2000 matching fields use one texture");
        var otherSeed = Settings(1); var other = GalaxyCloudCache.Acquire(otherSeed,extents);
        Check(!ReferenceEquals(other,fields[0]),"Distinct seeds retain distinct fields"); GalaxyCloudCache.Release(other);
        otherSeed.cloudResolution = 64; other = GalaxyCloudCache.Acquire(otherSeed,extents);
        Check(other.texture.width == 64,"Resolution invalidates the field key"); GalaxyCloudCache.Release(other);
        for(int i=0;i<fields.Length-1;i++) GalaxyCloudCache.Release(fields[i]);
        Check(fields[1999].texture != null,"Releasing one renderer cannot destroy another renderer's field");
        GalaxyCloudCache.Release(fields[1999]);
        Check(GalaxyCloudCache.ActiveCount == 0 && GalaxyCloudCache.TextureBytes == 0,"Last release frees the cache entry");
        var lost = GalaxyCloudCache.Acquire(s,extents); UnityEngine.Object.DestroyImmediate(lost.texture);
        var recovered = GalaxyCloudCache.Acquire(s,extents); Check(recovered.texture != null && !ReferenceEquals(lost,recovered),"Externally destroyed textures rebuild safely");
        GalaxyCloudCache.Release(lost); Check(recovered.texture != null,"Old release cannot destroy the replacement"); GalaxyCloudCache.Release(recovered);
        Check(GalaxyCloudCache.ActiveCount == 0 && GalaxyCloudCache.TextureBytes == 0,"Recovery releases all entries");
        var deferredSettings = Settings(7); deferredSettings.cloudResolution = 64;
        var originalSettings = deferredSettings.Snapshot();
        var deferred = GalaxyCloudCache.Acquire(deferredSettings,extents,true);
        var sharedDeferred = GalaxyCloudCache.Acquire(deferredSettings,extents,true);
        Check(ReferenceEquals(deferred,sharedDeferred) && deferred.texture == null && GalaxyCloudCache.PendingCount == 1 && GalaxyCloudCache.TextureBytes == 0,"Unseen fields share metadata without allocating textures");
        deferredSettings.seed++;
        Check(GalaxyCloudCache.EnsureTexture(deferred),"Deferred texture materializes on demand");
        var reference = new GalaxyCloudField(); GalaxyCloudField.GeneratePixels(originalSettings,extents,reference,false);
        Check(deferred.dustMass.Equals(reference.dustMass) && deferred.nebulaMass.Equals(reference.nebulaMass),"Deferred generation uses an immutable settings snapshot");
        GalaxyCloudCache.Release(deferred); Check(sharedDeferred.texture != null,"Deferred texture remains alive for another renderer"); GalaxyCloudCache.Release(sharedDeferred);
        deferred = GalaxyCloudCache.Acquire(originalSettings,extents,true); GalaxyCloudCache.Release(deferred);
        Check(GalaxyCloudCache.ActiveCount == 0 && GalaxyCloudCache.PendingCount == 0 && GalaxyCloudCache.TextureBytes == 0,"Unseen and materialized fields both clean up completely");
        return timer.Elapsed.TotalMilliseconds;
    }
    private static void VisibilityChecks()
    {
        var camera = new GameObject("Visibility camera").AddComponent<Camera>(); camera.farClipPlane = 10; camera.fieldOfView = 60; camera.aspect = 1;
        var root = new GameObject("Visibility root");
        root.transform.position = Vector3.forward*100; Check(DiskVisibility.IsVisible(camera,root.transform,Vector3.one,1),"Far plane does not cull galaxies");
        root.transform.position = Vector3.back*100; Check(!DiskVisibility.IsVisible(camera,root.transform,Vector3.one,1),"Behind camera is culled");
        root.transform.position = new Vector3(1000,0,100); Check(!DiskVisibility.IsVisible(camera,root.transform,Vector3.one,1),"Offscreen galaxy is culled");
        root.transform.position = new Vector3(60,0,100); Check(DiskVisibility.IsVisible(camera,root.transform,Vector3.one*10,1),"Intersecting screen edge retained");
        root.transform.position = Vector3.zero; Check(DiskVisibility.IsVisible(camera,root.transform,Vector3.one*100,1),"Camera inside volume retained");
        camera.orthographic = true; camera.orthographicSize = 5;
        root.transform.position = new Vector3(1,1,100); Check(DiskVisibility.IsVisible(camera,root.transform,Vector3.one,1),"Orthographic visible volume");
        root.transform.position = new Vector3(100,0,100); Check(!DiskVisibility.IsVisible(camera,root.transform,Vector3.one,1),"Orthographic offscreen volume");
        camera.orthographic = false; root.transform.position = Vector3.forward*1e24f;
        Check(DiskVisibility.IsVisible(camera,root.transform,Vector3.one*100,1e20f),"Astronomical visible coordinates remain finite");
        root.transform.position = new Vector3(1e25f,0,1e24f); Check(!DiskVisibility.IsVisible(camera,root.transform,Vector3.one*100,1e20f),"Astronomical offscreen volume");
        UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(camera.gameObject);
    }
    private static double RegistrationBenchmark()
    {
        var camera = new GameObject("Registration camera").AddComponent<Camera>(); camera.enabled = false;
        var roots = new GameObject[2000]; var buffers = new CommandBuffer[2000]; var timer = Stopwatch.StartNew();
        for(int i=0;i<roots.Length;i++)
        {
            roots[i] = new GameObject("Registry root "+i); roots[i].transform.position = Vector3.forward*(2000-i);
            var child = new GameObject("Registry source"); child.transform.SetParent(roots[i].transform,false);
            buffers[i] = new CommandBuffer(); DiskRenderOrder.Submit(camera,child.AddComponent<PerformanceSource>(),buffers[i]);
        }
        typeof(DiskRenderOrder).GetMethod("SortCamera",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[] { camera }); timer.Stop();
        Check(camera.GetCommandBuffers(DiskRenderOrder.DrawEvent).Length == 2000,"Deferred registration still attaches all disks");
        for(int i=0;i<roots.Length;i++) { DiskRenderOrder.Remove(camera,buffers[i]); buffers[i].Release(); UnityEngine.Object.DestroyImmediate(roots[i]); }
        Check(camera.GetCommandBuffers(DiskRenderOrder.DrawEvent).Length == 0,"Registry cleanup remains complete"); UnityEngine.Object.DestroyImmediate(camera.gameObject);
        return timer.Elapsed.TotalMilliseconds;
    }
    private static double PopulationBenchmark()
    {
        var body = new GameObject("Population body").AddComponent<CelestialBody>(); body.scaledBody = new GameObject("Population parent"); FlightGlobals.Bodies.Add(body);
        var roots = new GameObject[2000]; int initial = GalaxyCloudCache.BuildCount;
        var timer = Stopwatch.StartNew();
        for(int i=0;i<roots.Length;i++)
        {
            roots[i] = new GameObject("Population "+i) { layer = 10 }; roots[i].transform.SetParent(body.scaledBody.transform,false);
            var c = new GalaxyConfig { planet = body.name,renderMode = DiskRenderMode.Volumetric,starCount = 2000 };
            c.colorArms = new Color(i/2000f,0.6f,1,1); GalacticDiskGenerator.RegenerateDisk(roots[i],c);
            Check(roots[i].GetComponent<GalacticDiskObject>().volume.StarCount == 2000,"Population preserves the configured stars");
        }
        Check(roots[0].GetComponent<GalacticDiskObject>().volume.WarmCloudField(),"Shared population materializes its field before drawing");
        timer.Stop(); Check(GalaxyCloudCache.BuildCount-initial == 1 && GalaxyCloudCache.ActiveCount == 1,"Full 2000-galaxy population shares only its immutable density field");
        for(int i=0;i<roots.Length;i++) { roots[i].GetComponent<GalacticDiskObject>().ReleaseRenderer(); UnityEngine.Object.DestroyImmediate(roots[i]); }
        Check(GalaxyCloudCache.ActiveCount == 0 && GalaxyCloudCache.TextureBytes == 0,"Population cleanup frees shared GPU fields");
        FlightGlobals.Bodies.Clear(); UnityEngine.Object.DestroyImmediate(body.scaledBody); UnityEngine.Object.DestroyImmediate(body.gameObject);
        return timer.Elapsed.TotalMilliseconds;
    }
    private static void RenderVisibilityChecks()
    {
        var body = new GameObject("Visibility render body").AddComponent<CelestialBody>(); body.scaledBody = new GameObject("Visibility render parent");
        body.scaledBody.transform.position = Vector3.forward*100; FlightGlobals.Bodies.Add(body);
        var root = new GameObject("Visibility render disk") { layer = 10 }; root.transform.SetParent(body.scaledBody.transform,false);
        var c = new GalaxyConfig { planet = body.name,renderMode = DiskRenderMode.Volumetric,galaxyRadius = 10,starCount = 2000,bulgeFraction = 0,
            bulgeScaleX = 0,bulgeScaleY = 0,bulgeScaleZ = 0 };
        c.volume.enableBar = c.volume.enableDustClouds = c.volume.enableStarFormation = false; c.volume.cloudResolution = 64;
        GalacticDiskGenerator.RegenerateDisk(root,c); var owner = root.GetComponent<GalacticDiskObject>(); owner.LateUpdate();
        var camera = new GameObject("Visibility render camera").AddComponent<Camera>(); camera.cullingMask = 1<<10;
        camera.farClipPlane = 50; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        Check(VisibilityImage(camera,owner) > 0 && camera.GetCommandBuffers(DiskRenderOrder.DrawEvent).Length == 1,"Visible disk renders beyond the far plane");
        camera.transform.rotation = Quaternion.Euler(0,180,0);
        Check(VisibilityImage(camera,owner) == 0 && camera.GetCommandBuffers(DiskRenderOrder.DrawEvent).Length == 0,"Turning away hides existing camera buffers");
        camera.transform.rotation = Quaternion.identity;
        Check(VisibilityImage(camera,owner) > 0 && camera.GetCommandBuffers(DiskRenderOrder.DrawEvent).Length == 1,"Turning back restores drawing without rebuilding");
        owner.ReleaseRenderer(); FlightGlobals.Bodies.Clear();
        foreach(var obj in new[] { root,body.scaledBody,body.gameObject,camera.gameObject }) UnityEngine.Object.DestroyImmediate(obj);
        Check(GalaxyCloudCache.ActiveCount == 0,"Visibility test releases its shared field");
    }
    private static long VisibilityImage(Camera camera,GalacticDiskObject owner)
    {
        var target = RenderTexture.GetTemporary(160,120,24,RenderTextureFormat.ARGB32); var previous = RenderTexture.active;
        var image = new Texture2D(160,120,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture = target;
            typeof(VolumetricDiskRenderer).GetMethod("PrepareCamera",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(owner.volume,new object[] { camera });
            camera.Render(); RenderTexture.active = target; image.ReadPixels(new Rect(0,0,160,120),0,0); image.Apply();
            return ParticleRenderingChecks.Brightness(image.GetPixels32());
        }
        finally { camera.targetTexture = null; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(image); }
    }
    private static string Hash(Color[] pixels)
    {
        var values = new float[pixels.Length*4]; for(int i=0;i<pixels.Length;i++) { values[i*4]=pixels[i].r; values[i*4+1]=pixels[i].g; values[i*4+2]=pixels[i].b; values[i*4+3]=pixels[i].a; }
        var bytes = new byte[values.Length*4]; Buffer.BlockCopy(values,0,bytes,0,bytes.Length);
        using(var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","");
    }
    private static string Ms(double value) { return value.ToString("F1",CultureInfo.InvariantCulture); }
    private static void Check(bool condition,string message) { checks++; if(!condition) throw new InvalidOperationException("Performance check failed: "+message); }
}
public class PerformanceSource : MonoBehaviour { }
#endif
