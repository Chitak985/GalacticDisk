#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using GalacticDisk;
using UnityEditor;
using UnityEngine;

public static class DistantGalaxyChecks
{
    private static int checks;
    public static void Run()
    {
        try
        {
            KSPUtil.ApplicationRootPath = Path.GetFullPath("TestGame");
            Configs(); Projection();
            foreach(float scale in new[] { 1f,1e20f })
                foreach(DiskRenderMode mode in new[] { DiskRenderMode.Particles,DiskRenderMode.Volumetric }) Render(scale,mode);
            Check(DistantGalaxy.Users == 0,"Shared dot assets released after the last renderer");
            Directory.CreateDirectory("DistantVerification");
            File.WriteAllText("DistantVerification/Distant.txt","PASS - Unity "+Application.unityVersion+"; "+checks+" checks.\n"+
                "Galaxy/ring defaults, config and toolbar round trips, invalid settings and rollback.\n"+
                "Perspective, orthographic, custom projections, resolution changes, camera independence,\n"+
                "subpixel and physical-distance fading, zoom transitions and inside-camera detail.\n"+
                "Actual particle/volumetric rendering at scales 1 and 1e20 beyond the far plane;\n"+
                "off-center projection, smooth blends, RGBA/atmosphere opacity, opaque depth occlusion,\n"+
                "mixed dot/detail ordering and shared resource cleanup. No live KSP save loaded.\n");
            Debug.Log("GALACTICDISK_DISTANT_PASSED: "+checks);
        }
        catch(Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    private static void Configs()
    {
        var body=new GameObject("Test").AddComponent<CelestialBody>(); FlightGlobals.Bodies.Add(body);
        Check(GalaxyConfig.FromConfigNode(new ConfigNode("GalacticDiskDefinition")).enableDistantDots,"Legacy galaxies opt in automatically");
        Check(!GalaxyConfig.FromConfigNode(new ConfigNode(RingConfig.NodeName)).enableDistantDots,"Rings retain their detailed rendering by default");
        foreach(bool ring in new[] { false,true })
        {
            var c = new GalaxyConfig { isRing=ring,enableDistantDots=false,distantDotStartPixels=18,distantDotEndPixels=5,
                distantDotSize=4,distantDotBrightness=0.7f,distantFadeDistance=1500 };
            var toolbar = new GalacticDiskToolbar(); Invoke(toolbar,"PopulateFieldsFromConfig",c);
            var values = (System.Collections.Generic.Dictionary<string,string>)Invoke(toolbar,"CaptureFieldValues");
            var fromFields = ConfigEditor.ReadValues(ring,values,false).config;
            foreach(string name in DistantGalaxy.Fields)
                Check(Equals(typeof(GalaxyConfig).GetField(name).GetValue(c),typeof(GalaxyConfig).GetField(name).GetValue(fromFields)),"Toolbar roundtrip: "+name);
            string text = (string)Invoke(toolbar,"GenerateConfigText",fromFields,"Test");
            var parsed = ConfigEditor.Read(text); Check(parsed.IsValid,"Exported dot controls are valid");
            foreach(string name in DistantGalaxy.Fields)
                Check(Equals(typeof(GalaxyConfig).GetField(name).GetValue(c),typeof(GalaxyConfig).GetField(name).GetValue(parsed.config)),"Text roundtrip: "+name);
            var invalid = ConfigEditor.Read(text.Replace("distantDotEndPixels = 5","distantDotEndPixels = 18"));
            Check(!invalid.IsValid && invalid.errors.Exists(e=>e.Contains("distantDotStartPixels")),"Invalid thresholds diagnosed above editor");
        }
        foreach(string name in DistantGalaxy.Fields)
        {
            if(name == "enableDistantDots") continue;
            var c = new GalaxyConfig(); typeof(GalaxyConfig).GetField(name).SetValue(c,float.NaN);
            bool failed=false; try { DistantGalaxy.Validate(c); } catch(ArgumentException) { failed=true; }
            Check(failed,"Nonfinite value rejected: "+name);
        }
        FlightGlobals.Bodies.Remove(body); UnityEngine.Object.DestroyImmediate(body.gameObject);
    }
    private static Camera Camera()
    {
        var c = new GameObject("Distant camera").AddComponent<Camera>(); c.cullingMask=1<<10; c.fieldOfView=60;
        c.nearClipPlane=0.1f; c.farClipPlane=1000; c.clearFlags=CameraClearFlags.SolidColor; c.backgroundColor=Color.black;
        c.targetTexture=new RenderTexture(480,320,24,RenderTextureFormat.ARGB32); c.targetTexture.Create(); return c;
    }
    private static void Projection()
    {
        var camera=Camera(); var root=new GameObject("Projection root"); var c=new GalaxyConfig(); var dot=new DistantGalaxy(c);
        var v=new DistantGalaxy.View(); var e=new Vector3(100,5,100); root.transform.position=Vector3.forward*30000;
        dot.Evaluate(camera,root.transform,e,v); Check(v.detail==0 && v.pixels>0,"Small perspective galaxy uses dot"); double first=v.pixels;
        Matrix4x4 zoom=camera.projectionMatrix; zoom.m00*=1e6f; zoom.m11*=1e6f; camera.projectionMatrix=zoom;
        dot.Evaluate(camera,root.transform,e,v); Check(v.detail==1,"Extreme custom-projection zoom restores full detail");
        camera.ResetProjectionMatrix(); dot.Evaluate(camera,root.transform,e,v);
        var independent=new DistantGalaxy.View(); camera.fieldOfView=0.01f; dot.Evaluate(camera,root.transform,e,independent);
        Check(independent.detail==1 && v.detail==0,"Camera state is independent"); camera.fieldOfView=60;
        camera.targetTexture.Release(); camera.targetTexture.width=960; camera.targetTexture.height=640; camera.targetTexture.Create();
        dot.Evaluate(camera,root.transform,e,v); Check(v.pixels>first*1.9,"Render target resolution is included");
        camera.targetTexture.Release(); camera.targetTexture.width=480; camera.targetTexture.height=320; camera.targetTexture.Create();
        root.transform.position=Vector3.forward*300000; dot.Evaluate(camera,root.transform,e,v); float fade=v.fade;
        root.transform.position*=10; dot.Evaluate(camera,root.transform,e,v); Check(v.fade<fade*0.012f,"Unresolved galaxies dim with decreasing angular area");
        camera.orthographic=true; camera.orthographicSize=20000; dot.Evaluate(camera,root.transform,e,v); first=v.pixels; fade=v.fade;
        root.transform.position*=10; dot.Evaluate(camera,root.transform,e,v); Check(Math.Abs(v.pixels-first)<0.001 && v.fade==fade,"Orthographic detail ignores distance");
        c.distantFadeDistance=1000; dot.Evaluate(camera,root.transform,e,v); Check(v.fade<fade,"Optional distance fade is independent of orthographic zoom");
        camera.orthographicSize=100; dot.Evaluate(camera,root.transform,e,v); Check(v.detail==1,"Orthographic zoom restores detail");
        camera.transform.position=root.transform.position; dot.Evaluate(camera,root.transform,e,v); Check(v.detail==1,"Observer inside galaxy retains detail");
        c.enableDistantDots=false; var disabled=new DistantGalaxy(c); camera.transform.position=Vector3.zero;
        disabled.Evaluate(camera,root.transform,e,v); Check(v.detail==1,"Explicit disable always retains detail"); disabled.Dispose();
        dot.Dispose(); DestroyCamera(camera); UnityEngine.Object.DestroyImmediate(root);
    }
    private static void Render(float scale,DiskRenderMode mode)
    {
        string label=mode+"-"+scale;
        var c=new GalaxyConfig { renderMode=mode,objectScale=scale,starCount=18000,minStarSize=0.3f,maxStarSize=1.1f,
            colorArms=new Color(0.3f,0.6f,1,0.8f),colorBulge=new Color(0.3f,0.6f,1,0.8f),colorInterarm=new Color(0.3f,0.6f,1,0.8f) };
        c.volume.enableDustClouds=false; c.volume.enableStarFormation=false; c.volume.cloudResolution=64;
        var root=new GameObject(label) { layer=10 }; root.transform.position=Vector3.forward*(30000*scale);
        GalacticDiskGenerator.RegenerateDisk(root,c); var owner=root.GetComponent<GalacticDiskObject>(); owner.LateUpdate();
        var camera=Camera(); int builds=GalaxyCloudCache.BuildCount;
        var image=Capture(camera,label+"-dot"); var state=View(owner,camera);
        Check(state.detail==0 && Light(image)>10,"Distant dot visible beyond far plane: "+label);
        int dotBytes=camera.GetCommandBuffers(DiskRenderOrder.DrawEvent)[0].sizeInBytes;
        long dotLight=Light(image);
        c.distantFadeDistance=100; Check(Light(Capture(camera,null))<dotLight*0.5,"Optional physical distance fade dims the actual dot"); c.distantFadeDistance=0;
        camera.orthographic=true; camera.orthographicSize=20000*scale;
        image=Capture(camera,label+"-orthographic-dot"); Check(View(owner,camera).detail==0 && Light(image)>5,"Orthographic dot rendering: "+label);
        camera.orthographicSize=100*scale; image=Capture(camera,label+"-orthographic-zoom");
        Check(View(owner,camera).detail==1 && Light(image)>5,"Orthographic zoom restores rendered detail: "+label); camera.orthographic=false;
        root.transform.position+=new Vector3(5000*scale,7000*scale,0); image=Capture(camera,label+"-offcenter");
        Vector3 screen=camera.WorldToScreenPoint(root.transform.position);
        Check(LocalLight(image,screen.x,screen.y)>5,"Off-center dot agrees with camera projection: "+label+" screen="+screen);
        root.transform.position=Vector3.forward*(30000*scale);
        camera.fieldOfView=1; image=Capture(camera,label+"-zoomed"); state=View(owner,camera);
        Check(state.detail==1 && Light(image)>dotLight*2,"Zoom restores actual galaxy detail: "+label);
        Check(camera.GetCommandBuffers(DiskRenderOrder.DrawEvent)[0].sizeInBytes>dotBytes,"Dot skips detailed draws: "+label);
        Check(GalaxyCloudCache.BuildCount==builds,"Zoom reuses generated clouds: "+label);
        bool blended=false; float previous=-1;
        for(int i=0;i<=16;i++)
        {
            camera.fieldOfView=60-i*3.4f; Capture(camera,null); float detail=View(owner,camera).detail;
            Check(detail>=previous,"Continuous monotonic transition: "+label); previous=detail; blended|=detail>0 && detail<1;
        }
        Check(blended,"Transition contains both representations: "+label);
        camera.fieldOfView=60; c.distantDotBrightness=0; Check(Light(Capture(camera,null))==0,"Dot brightness zero hides proxy: "+label); c.distantDotBrightness=1;
        c.atmosphereOpacityMultiplier=c.atmosphereNightOpacityMultiplier=0;
        var atmosphere=new GameObject("Distant atmosphere").AddComponent<CelestialBody>(); atmosphere.atmosphere=true;
        atmosphere.Radius=10; atmosphere.atmosphereDepth=100; atmosphere.position=new Vector3d(0,-10,0); FlightGlobals.Bodies.Add(atmosphere);
        Check(Light(Capture(camera,null))==0,"Atmosphere opacity also affects dots: "+label);
        FlightGlobals.Bodies.Remove(atmosphere); UnityEngine.Object.DestroyImmediate(atmosphere.gameObject);
        c.atmosphereOpacityMultiplier=c.atmosphereNightOpacityMultiplier=1;
        var invalid=new GalaxyConfig { distantDotStartPixels=2,distantDotEndPixels=4 }; bool failed=false;
        try { GalacticDiskGenerator.RegenerateDisk(root,invalid); } catch(ArgumentException) { failed=true; }
        Check(failed && owner.config==c,"Invalid dot settings preserve working renderer");
        if(scale==1)
        {
            var blocker=GameObject.CreatePrimitive(PrimitiveType.Cube); blocker.layer=10; blocker.transform.position=Vector3.forward*5;
            blocker.transform.localScale=Vector3.one*4; blocker.GetComponent<Renderer>().sharedMaterial=new Material(Shader.Find("Unlit/Color"));
            blocker.GetComponent<Renderer>().sharedMaterial.color=Color.black;
            Check(Light(Capture(camera,label+"-occluded"))==0,"Opaque foreground geometry occludes dot");
            UnityEngine.Object.DestroyImmediate(blocker.GetComponent<Renderer>().sharedMaterial); UnityEngine.Object.DestroyImmediate(blocker);
        }
        var far=new GameObject("Far dot") { layer=10 }; far.transform.position=root.transform.position*2;
        var farConfig=new GalaxyConfig { renderMode=mode,objectScale=scale,starCount=256 }; farConfig.volume.enableDustClouds=false;
        farConfig.volume.enableStarFormation=false; farConfig.volume.cloudResolution=64;
        GalacticDiskGenerator.RegenerateDisk(far,farConfig); far.GetComponent<GalacticDiskObject>().LateUpdate();
        camera.fieldOfView=60; Capture(camera,null); var commands=camera.GetCommandBuffers(DiskRenderOrder.DrawEvent);
        var stats=DiskRenderOrder.Stats(camera);
        Check(commands.Length==1 && stats.dotDisks==2 && stats.dotBatches==1 && stats.detailDraws==0,"Dots batch together without detailed draws: "+label);
        far.transform.position*=100;
        camera.fieldOfView=1; Capture(camera,null);
        Check(View(owner,camera).detail==1 && View(far.GetComponent<GalacticDiskObject>(),camera).detail==0,"A camera can mix detailed and dotted galaxies");
        commands=camera.GetCommandBuffers(DiskRenderOrder.DrawEvent);
        Check(commands.Length==2 && commands[0].name=="GalacticDisk distant dot batch" && commands[1].name=="GalacticDisk "+label,"Mixed dot/detail drawing preserves ordering");
        owner.ReleaseRenderer(); far.GetComponent<GalacticDiskObject>().ReleaseRenderer();
        Check(camera.GetCommandBuffers(DiskRenderOrder.DrawEvent).Length==0,"Camera commands released");
        UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(far); DestroyCamera(camera);
        // All-transparent populations must not turn into a visible dot.
        c.starCount=0; c.colorBulge=c.colorArms=c.colorInterarm=Color.clear; c.volume.enableDustClouds=false; c.volume.enableStarFormation=false;
        root=new GameObject("Transparent proxy"); root.transform.position=Vector3.forward*(30000*scale);
        GalacticDiskGenerator.RegenerateDisk(root,c); camera=Camera();
        Check(Light(Capture(camera,null))==0,"RGBA transparency retained by proxy: "+label);
        root.GetComponent<GalacticDiskObject>().ReleaseRenderer(); UnityEngine.Object.DestroyImmediate(root); DestroyCamera(camera);
    }
    private static DistantGalaxy.View View(GalacticDiskObject owner,Camera camera)
    {
        object renderer=owner.volume!=null?(object)owner.volume:owner.stableParticles;
        var map=(IDictionary)Get(renderer,owner.volume!=null?"cameraDraws":"cameras"); return (DistantGalaxy.View)Get(map[camera],"distant");
    }
    private static Color32[] Capture(Camera camera,string name)
    {
        var image=new Texture2D(480,320,TextureFormat.RGB24,false); var previous=RenderTexture.active;
        try {
            foreach(var owner in UnityEngine.Object.FindObjectsOfType<GalacticDiskObject>())
            {
                object renderer=owner.volume!=null?(object)owner.volume:owner.stableParticles;
                if(renderer!=null) Invoke(renderer,"PrepareCamera",camera);
            }
            camera.Render(); RenderTexture.active=camera.targetTexture; image.ReadPixels(new Rect(0,0,480,320),0,0); image.Apply();
            if(name!=null) { Directory.CreateDirectory("DistantVerification"); File.WriteAllBytes("DistantVerification/"+name+".png",image.EncodeToPNG()); }
            return image.GetPixels32(); }
        finally { RenderTexture.active=previous; UnityEngine.Object.DestroyImmediate(image); }
    }
    private static long LocalLight(Color32[] pixels,float x,float y)
    {
        long result=0; for(int yy=Math.Max(0,(int)y-4);yy<Math.Min(320,(int)y+5);yy++)
            for(int xx=Math.Max(0,(int)x-4);xx<Math.Min(480,(int)x+5);xx++) { Color32 p=pixels[yy*480+xx]; result+=p.r+p.g+p.b; } return result;
    }
    private static long Light(Color32[] pixels) { return ParticleRenderingChecks.Brightness(pixels); }
    private static void DestroyCamera(Camera camera) { var target=camera.targetTexture; camera.targetTexture=null; UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(camera.gameObject); }
    private static object Get(object target,string name) { return target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(target); }
    private static object Invoke(object target,string name,params object[] args) { return target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args); }
    private static void Check(bool condition,string message) { checks++; if(!condition) throw new InvalidOperationException("Distant galaxy check failed: "+message); }
}
#endif
