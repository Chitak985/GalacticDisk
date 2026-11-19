#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GalacticDisk;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class MapRenderingChecks
{
    private static int checks;
    public static void Run()
    {
        try
        {
            KSPUtil.ApplicationRootPath=Path.GetFullPath("TestGame");
            HighLogic.LoadedScene=GameScenes.TRACKSTATION; MapView.MapIsEnabled=true;
            Batches(); LargeGalaxies();
            Directory.CreateDirectory("MapRenderingVerification");
            File.WriteAllText("MapRenderingVerification/MapRendering.txt","PASS - Unity "+Application.unityVersion+"; "+checks+" checks.\n"+
                "2000 visible dot submissions render in one shared mesh draw with zero star/volume draws.\n"+
                "Batched output matches 2000 separate reference draws, with ordered detailed disks splitting batches.\n"+
                "Removal and camera/layer changes release stale geometry and buffers.\n"+
                "Milky Way-size galaxy at the supplied distance and extremely distant zoomed galaxies remain detailed.\n"+
                "Actual particle/volume rendering, including observers inside the galaxy and beyond the normal far plane.\n"+
                "Synthetic map camera and KSP API substitutes; no live KSP FPS claim.\n");
            Debug.Log("GALACTICDISK_MAP_RENDERING_PASSED: "+checks);
        }
        catch(Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    private static void Batches()
    {
        const int count=2000;
        var camera=Camera(); var config=new GalaxyConfig(); var proxy=new DistantGalaxy(config);
        proxy.AddStar(new Color(0.3f,0.6f,1,0.7f),1);
        var roots=new List<GameObject>(); var renderers=new List<StableParticleRenderer>(); var buffers=new List<CommandBuffer>();
        var views=new List<DistantGalaxy.View>(); var properties=new MaterialPropertyBlock();
        for(int i=0;i<count;i++)
        {
            var root=new GameObject("Distant root "+i); root.transform.position=Vector3.forward*(10000+i);
            var child=new GameObject("Distant draw "+i); child.transform.SetParent(root.transform,false);
            roots.Add(root); renderers.Add(child.AddComponent<StableParticleRenderer>()); buffers.Add(new CommandBuffer());
            var view=new DistantGalaxy.View { detail=0,fade=1,diameter=3,position=new Vector3((i%50-24.5f)*0.036f,(i/50-19.5f)*0.045f,900) };
            views.Add(view); proxy.Draw(buffers[i],properties,camera,view,0.6f); DiskRenderOrder.Submit(camera,renderers[i],buffers[i]);
        }
        var reference=Capture(camera,"Separate2000.png");
        Check(camera.GetCommandBuffers(DiskRenderOrder.DrawEvent).Length==count,"Reference really submits 2000 individual draw commands");
        for(int i=0;i<count;i++)
        {
            buffers[i].Clear(); DistantGalaxy.DrawData data; Check(proxy.TryGetDraw(camera,views[i],0.6f,out data),"Visible dot data");
            DiskRenderOrder.SubmitDot(camera,renderers[i],buffers[i],data);
        }
        var batched=Capture(camera,"Batched2000.png"); var stats=DiskRenderOrder.Stats(camera);
        Check(stats.dotDisks==count && stats.dotBatches==1 && stats.detailedDisks==0 && stats.detailDraws==0,"2000 dots use one draw and no detailed geometry");
        Check(camera.GetCommandBuffers(DiskRenderOrder.DrawEvent).Length==1,"One command buffer for all dots");
        long light=ParticleRenderingChecks.Brightness(reference);
        Check(light>1000 && Difference(reference,batched)<Math.Max(100,light/1000),"Batch image matches individual reference draws");
        // A detailed object between dots must split the batch instead of reordering transparency.
        var middle=new GameObject("Detailed middle"); middle.transform.position=Vector3.forward*5000;
        var childMiddle=new GameObject("Detailed middle renderer"); childMiddle.transform.SetParent(middle.transform,false);
        var middleRenderer=childMiddle.AddComponent<StableParticleRenderer>(); var middleCommands=new CommandBuffer { name="Detailed middle" };
        DiskRenderOrder.Submit(camera,middleRenderer,middleCommands,2);
        roots[0].transform.position=Vector3.forward*1000;
        Capture(camera,null); stats=DiskRenderOrder.Stats(camera); var attached=camera.GetCommandBuffers(DiskRenderOrder.DrawEvent);
        Check(stats.dotBatches==2 && stats.detailedDisks==1 && stats.detailDraws==2,"Detailed disks split dot batches");
        Check(attached.Length==3 && attached[1].name=="Detailed middle","Far dots, detail and near dot keep their exact order");
        DiskRenderOrder.Remove(camera,middleCommands); middleCommands.Release(); UnityEngine.Object.DestroyImmediate(middle);
        DiskRenderOrder.Remove(camera,buffers[0]); buffers[0].Clear(); UnityEngine.Object.DestroyImmediate(roots[0]);
        Capture(camera,null); Check(DiskRenderOrder.Stats(camera).dotDisks==count-1,"Removed dot is not retained in batch geometry");
        camera.cullingMask=0; Capture(camera,null); Check(camera.GetCommandBuffers(DiskRenderOrder.DrawEvent).Length==0,"Excluded layer detaches batched geometry");
        camera.cullingMask=1<<10; Capture(camera,null); Check(DiskRenderOrder.Stats(camera).dotDisks==count-1,"Restoring the layer restores the remaining dots");
        for(int i=1;i<count;i++) { DiskRenderOrder.Remove(camera,buffers[i]); UnityEngine.Object.DestroyImmediate(roots[i]); }
        foreach(var buffer in buffers) buffer.Release();
        Check(camera.GetCommandBuffers(DiskRenderOrder.DrawEvent).Length==0,"Batch resources and camera buffers removed after last disk");
        proxy.Dispose(); DestroyCamera(camera);
    }
    private static void LargeGalaxies()
    {
        foreach(DiskRenderMode mode in new[] { DiskRenderMode.Particles,DiskRenderMode.Volumetric })
        {
            var c=new GalaxyConfig { renderMode=mode,objectScale=8e14f,starCount=2000,minStarSize=0.2f,maxStarSize=1.2f,rotationX=40 };
            c.volume.enableDustClouds=false; c.volume.enableStarFormation=false; c.volume.cloudResolution=64;
            var root=new GameObject("Large "+mode); root.transform.position=Vector3.forward*(2.57e20f/6000);
            GalacticDiskGenerator.RegenerateDisk(root,c); var owner=root.GetComponent<GalacticDiskObject>(); owner.LateUpdate();
            var camera=Camera(); Prepare(owner,camera); var image=Capture(camera,"Large-"+mode+".png"); var stats=DiskRenderOrder.Stats(camera);
            Check(stats.detailedDisks==1 && stats.dotDisks==0 && stats.detailDraws>0,"Large galaxy always uses detailed geometry: "+mode);
            Check(ParticleRenderingChecks.Brightness(image)>1000,"Large galaxy is actually visible: "+mode);
            root.transform.position=Vector3.forward*(2.08e24f/6000); camera.fieldOfView=0.01f; Prepare(owner,camera);
            image=Capture(camera,"ExtremeZoom-"+mode+".png"); stats=DiskRenderOrder.Stats(camera);
            Check(stats.detailedDisks==1 && ParticleRenderingChecks.Brightness(image)>100,"Extremely distant zoomed galaxy remains detailed and visible: "+mode);
            camera.transform.position=root.transform.position; camera.fieldOfView=60; Prepare(owner,camera); Capture(camera,null);
            Check(DiskRenderOrder.Stats(camera).detailedDisks==1,"Inside observer keeps detail: "+mode);
            owner.ReleaseRenderer(); UnityEngine.Object.DestroyImmediate(root); DestroyCamera(camera);
        }
    }
    private static void Prepare(GalacticDiskObject owner,Camera camera)
    {
        object renderer=owner.volume!=null?(object)owner.volume:owner.stableParticles;
        renderer.GetType().GetMethod("PrepareCamera",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(renderer,new object[] { camera });
    }
    private static Camera Camera()
    {
        var camera=new GameObject("Map test camera").AddComponent<Camera>(); camera.cullingMask=1<<10; camera.fieldOfView=60;
        camera.nearClipPlane=0.1f; camera.farClipPlane=1000; camera.backgroundColor=Color.black; camera.clearFlags=CameraClearFlags.SolidColor;
        camera.targetTexture=new RenderTexture(480,320,24); camera.targetTexture.Create(); return camera;
    }
    private static Color32[] Capture(Camera camera,string name)
    {
        var texture=new Texture2D(480,320,TextureFormat.RGB24,false); var previous=RenderTexture.active;
        try { camera.Render(); RenderTexture.active=camera.targetTexture; texture.ReadPixels(new Rect(0,0,480,320),0,0); texture.Apply();
            if(name!=null) { Directory.CreateDirectory("MapRenderingVerification"); File.WriteAllBytes("MapRenderingVerification/"+name,texture.EncodeToPNG()); }
            return texture.GetPixels32(); }
        finally { RenderTexture.active=previous; UnityEngine.Object.DestroyImmediate(texture); }
    }
    private static long Difference(Color32[] a,Color32[] b) { long sum=0; for(int i=0;i<a.Length;i++) sum+=Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b); return sum; }
    private static void DestroyCamera(Camera camera) { var target=camera.targetTexture; camera.targetTexture=null; UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(camera.gameObject); }
    private static void Check(bool condition,string message) { checks++; if(!condition) throw new InvalidOperationException("Map rendering check failed: "+message); }
}
#endif
