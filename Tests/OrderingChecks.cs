#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using GalacticDisk;
using UnityEditor;
using UnityEngine;

public static class OrderingChecks
{
    private static int checks;
    public static void Run()
    {
        try
        {
            KSPUtil.ApplicationRootPath = Path.GetFullPath("TestGame");
            foreach (float scale in new[] { 1f, 1e20f })
            {
                CheckPair(scale,DiskRenderMode.Volumetric,DiskRenderMode.Volumetric);
                CheckPair(scale,DiskRenderMode.Volumetric,DiskRenderMode.Particles);
                CheckPair(scale,DiskRenderMode.Particles,DiskRenderMode.Volumetric);
                CheckPair(scale,DiskRenderMode.Particles,DiskRenderMode.Particles);
            }
            Directory.CreateDirectory("OrderingVerification");
            File.WriteAllText("OrderingVerification/Ordering.txt","PASS - Unity "+Application.unityVersion+"; "+checks+" checks.\n" +
                "Near/far overlapping rings in all four particle/volume combinations, both creation orders,\n" +
                "front/back cameras, camera reversal, per-camera independence, far-star obscuration, disabled layers,\n" +
                "and buffer cleanup. Repeated at objectScale 1 and 1e20, beyond the camera far plane.\n" +
                "Real Unity rendering with KSP API substitutes; no live KSP save loaded.\n");
            Debug.Log("GALACTICDISK_ORDERING_PASSED: "+checks);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    private static void CheckPair(float scale,DiskRenderMode nearMode,DiskRenderMode farMode)
    {
        string label = scale+"-"+nearMode+"-"+farMode;
        var nearBody = new GameObject("Near body").AddComponent<CelestialBody>(); nearBody.scaledBody = new GameObject("Near parent");
        var farBody = new GameObject("Far body").AddComponent<CelestialBody>(); farBody.scaledBody = new GameObject("Far parent");
        nearBody.scaledBody.transform.position = Vector3.forward*30*scale; farBody.scaledBody.transform.position = Vector3.forward*60*scale;
        FlightGlobals.Bodies.Add(nearBody); FlightGlobals.Bodies.Add(farBody);
        var near = new GameObject("Near disk") { layer = 10 }; near.transform.SetParent(nearBody.scaledBody.transform,false);
        var far = new GameObject("Far disk") { layer = 10 }; far.transform.SetParent(farBody.scaledBody.transform,false);
        var nearConfig = Config(nearBody.name,nearMode,10,scale,Color.red);
        var farConfig = Config(farBody.name,farMode,20,scale,Color.blue);
        var front = new GameObject("Front camera").AddComponent<Camera>(); front.cullingMask = 1 << 10; front.fieldOfView = 45;
        front.farClipPlane = 1000; front.clearFlags = CameraClearFlags.SolidColor; front.backgroundColor = Color.black;
        var back = new GameObject("Back camera").AddComponent<Camera>(); back.CopyFrom(front); back.enabled = false;
        back.transform.position = Vector3.forward*90*scale; back.transform.rotation = Quaternion.Euler(0,180,0);
        Generate(near,nearConfig); Generate(far,farConfig);
        Color32[] first = Capture(front,near,far,label+"-front"); AssertOrder(front,"Far disk","Near disk");
        Color32[] opposite = Capture(back,near,far,label+"-back"); AssertOrder(back,"Near disk","Far disk");
        Color32[] again = Capture(front,near,far,label+"-front-again"); AssertOrder(front,"Far disk","Near disk");
        Check(Difference(first,again) <= Math.Max(3,Brightness(first)/100000),"Other camera preserves this camera's image within GPU rounding: "+label+" difference="+Difference(first,again));
        if (nearMode == DiskRenderMode.Volumetric)
        {
            long red,blue; CenterLight(first,out red,out blue);
            Check(red > 10000 && red > blue*3,"Foreground fog obscures distant blue fog/stars: "+label+" red="+red+" blue="+blue);
        }
        if (farMode == DiskRenderMode.Volumetric)
        {
            long red,blue; CenterLight(opposite,out red,out blue);
            Check(blue > 10000 && blue > red*3,"Reversed view puts blue volume in front: "+label);
        }
        near.GetComponent<GalacticDiskObject>().ReleaseRenderer(); far.GetComponent<GalacticDiskObject>().ReleaseRenderer();
        Generate(far,farConfig); Generate(near,nearConfig);
        Color32[] rebuilt = Capture(front,near,far,label+"-reverse-creation");
        AssertOrder(front,"Far disk","Near disk");
        Check(Difference(first,rebuilt) <= Math.Max(3,Brightness(first)/100000),"Creation and Apply order preserve the image within GPU rounding: "+label);
        front.transform.position = back.transform.position; front.transform.rotation = back.transform.rotation;
        Capture(front,near,far,label+"-moved-camera"); AssertOrder(front,"Near disk","Far disk");
        front.cullingMask = 0; Check(Brightness(Capture(front,near,far,label+"-excluded")) == 0,"Excluded layer does not draw");
        Check(front.GetCommandBuffers(DiskRenderOrder.DrawEvent).Length == 0,"Excluded layer detaches disk buffers");
        front.cullingMask = 1 << 10; Capture(front,near,far,label+"-restored"); AssertOrder(front,"Near disk","Far disk");
        near.GetComponent<GalacticDiskObject>().ReleaseRenderer(); far.GetComponent<GalacticDiskObject>().ReleaseRenderer();
        Check(front.GetCommandBuffers(DiskRenderOrder.DrawEvent).Length == 0 && back.GetCommandBuffers(DiskRenderOrder.DrawEvent).Length == 0,"All camera buffers removed on disposal");
        FlightGlobals.Bodies.Clear();
        foreach (var obj in new[] { near,far,nearBody.scaledBody,farBody.scaledBody,nearBody.gameObject,farBody.gameObject,front.gameObject,back.gameObject }) UnityEngine.Object.DestroyImmediate(obj);
    }
    private static GalaxyConfig Config(string planet,DiskRenderMode mode,float radius,float scale,Color color)
    {
        var node = new ConfigNode(RingConfig.NodeName); node.AddValue("planet",planet); node.AddValue("ringRadius",radius); node.AddValue("innerRingRadius",0);
        var c = GalaxyConfig.FromConfigNode(node); c.renderMode = mode; c.rotationX = 90; c.objectScale = scale; c.renderScale = 1e5f;
        c.diskThickness = radius*0.3f; c.starCount = mode == DiskRenderMode.Particles ? 5000 : 2048;
        c.minStarSize = radius*0.07f; c.maxStarSize = radius*0.09f;
        c.colorInterarm = c.colorArms = c.colorBulge = c.armHighlightColor = color;
        c.volume.cloudResolution = 64; c.volume.volumeSteps = 64; c.volume.volumeIntensity = 12;
        c.volume.enableDustClouds = true; c.volume.dustCloudCount = 0; c.volume.dustLaneStrength = 4; c.volume.dustCloudClumpiness = 0;
        c.volume.dustOpacity = 80; c.volume.dustScattering = 0; c.volume.dustColor = new Color(0,0,0,1);
        return c;
    }
    private static void Generate(GameObject root,GalaxyConfig c) { GalacticDiskGenerator.RegenerateDisk(root,c); root.GetComponent<GalacticDiskObject>().LateUpdate(); }
    private static void AssertOrder(Camera camera,string first,string second)
    {
        var commands = camera.GetCommandBuffers(DiskRenderOrder.DrawEvent);
        Check(commands.Length == 2 && commands[0].name == "GalacticDisk "+first && commands[1].name == "GalacticDisk "+second,"Camera sorts complete disks far to near");
    }
    private static Color32[] Capture(Camera camera,GameObject near,GameObject far,string name)
    {
        Directory.CreateDirectory("OrderingVerification"); var target = RenderTexture.GetTemporary(480,320,24,RenderTextureFormat.ARGB32);
        var texture = new Texture2D(480,320,TextureFormat.RGB24,false); var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target;
            foreach (GameObject root in new[] { near,far })
            {
                var owner = root.GetComponent<GalacticDiskObject>(); object renderer = owner.volume != null ? (object)owner.volume : owner.stableParticles;
                renderer.GetType().GetMethod("PrepareCamera",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(renderer,new object[] { camera });
            }
            camera.Render(); RenderTexture.active = target; texture.ReadPixels(new Rect(0,0,480,320),0,0); texture.Apply();
            Color32[] pixels = texture.GetPixels32(); File.WriteAllBytes("OrderingVerification/"+name+".png",texture.EncodeToPNG()); return pixels;
        }
        finally { camera.targetTexture = null; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(texture); }
    }
    private static long Difference(Color32[] a,Color32[] b) { long sum = 0; for(int i=0;i<a.Length;i++) sum+=Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b); return sum; }
    private static long Brightness(Color32[] a) { return ParticleRenderingChecks.Brightness(a); }
    private static void CenterLight(Color32[] pixels,out long red,out long blue) { red = blue = 0; for(int y=140;y<180;y++) for(int x=220;x<260;x++) { var p = pixels[y*480+x]; red+=p.r; blue+=p.b; } }
    private static void Check(bool condition,string message) { checks++; if(!condition) throw new InvalidOperationException("Ordering check failed: "+message); }
}
#endif
