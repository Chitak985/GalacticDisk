#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GalacticDisk;
using UnityEditor;
using UnityEngine;

// Only the plugin discovery shape is substituted. Rendering uses the installed
// Singularity shader, GalacticDisk's actual renderers and real nested cameras.
namespace Singularity
{
    public class Singularity : MonoBehaviour
    {
        public static Singularity Instance { get; set; }
        public List<SingularityObject> loadedObjects = new List<SingularityObject>();
        public Texture screenBufferFlip, stackingDepthBuffer;
    }
    public class SingularityObject : MonoBehaviour
    {
        private Material singularityMaterial;
        public float gravity=0, scaledRadius=0.00001f, enclosingMeshRadius=40;
        public SingularityCenteredCubeMap singularityCubeMap;
        public void SetMaterial(Material value) { singularityMaterial=value; }
    }
    public class SingularityCenteredCubeMap : MonoBehaviour { public Texture singularityCubemap; }
}
public sealed class SingularityCompatibilityChecks
{
    private int checks;
    private readonly List<GalacticDiskObject> disks = new List<GalacticDiskObject>();
    public static void Run()
    {
        try { var check=new SingularityCompatibilityChecks(); check.Execute(); Debug.Log("GALACTICDISK_SINGULARITY_PASSED: "+check.checks); }
        catch(Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    private void Execute()
    {
        KSPUtil.ApplicationRootPath=Path.GetFullPath("TestGame");
        var bundle=AssetBundle.LoadFromFile(Path.GetFullPath("SingularityShaders.bin"));
        Shader original=bundle.LoadAllAssets<Shader>().First(s=>s.name=="Singularity/BlackHoleAccretionDisk"); bundle.Unload(false);
        Check(original.isSupported,"Installed Singularity shader is supported");
        Camera.onPreCull+=Prepare;
        try
        {
            foreach(DiskRenderMode mode in new[] { DiskRenderMode.Volumetric,DiskRenderMode.Particles }) Reproduce(original,mode);
            CustomViews();
            CubeFaces();
        }
        finally { Camera.onPreCull-=Prepare; SingularityCompatibility.Clear(); }
        Directory.CreateDirectory("SingularityVerification");
        File.WriteAllText("SingularityVerification/Singularity.txt","PASS - Unity "+Application.unityVersion+"; "+checks+" checks.\n"+
            "Reproduced the enclosing-sphere background loss using the installed Singularity shader.\n"+
            "The compatible shader retains captured volume/particle light without inventing opaque depth.\n"+
            "Transparent capture clear/restore, keyword/texture/uniform preservation, event horizon,\n"+
            "ordinary opaque foreground, custom camera view matrices, dots and cleanup verified.\n"+
            "Real Unity rendering and Singularity shader assets; plugin discovery/KSP APIs are substitutes.\n"+
            "No live KSP save loaded.\n");
    }
    private void Reproduce(Shader original,DiskRenderMode mode)
    {
        var root=new GameObject("Galaxy for lensing");
        var c=new GalaxyConfig { renderMode=mode,starCount=5000,minStarSize=0.4f,maxStarSize=1.5f,rotationX=50 };
        c.volume.cloudResolution=64; c.volume.enableDustClouds=false; c.volume.enableStarFormation=false;
        GalacticDiskGenerator.RegenerateDisk(root,c); var owner=root.GetComponent<GalacticDiskObject>(); owner.LateUpdate(); disks.Add(owner);
        var camera=CreateCamera("Lensing main camera"); camera.transform.position=new Vector3(-70,140,-190);
        camera.transform.LookAt(Vector3.zero); camera.depthTextureMode=DepthTextureMode.Depth;
        var scene=CreateCamera("Singularity scene camera"); var captureTarget=scene.targetTexture;
        scene.CopyFrom(camera); scene.targetTexture=captureTarget; scene.transform.SetPositionAndRotation(camera.transform.position,camera.transform.rotation);
        scene.clearFlags=CameraClearFlags.Depth; scene.backgroundColor=Color.magenta;
        var sphere=GameObject.CreatePrimitive(PrimitiveType.Sphere); sphere.name="Singularity enclosing sphere"; sphere.layer=0;
        sphere.transform.localScale=Vector3.one*80;
        var material=new Material(original); sphere.GetComponent<Renderer>().sharedMaterial=material;
        var sky=new Cubemap(4,TextureFormat.RGBA32,false); var empty=new Cubemap(4,TextureFormat.RGBA32,false);
        foreach(CubemapFace face in Enum.GetValues(typeof(CubemapFace))) if((int)face>=0 && (int)face<6)
        { sky.SetPixels(Enumerable.Repeat(Color.black,16).ToArray(),face); empty.SetPixels(Enumerable.Repeat(Color.clear,16).ToArray(),face); }
        sky.Apply(); empty.Apply();
        var depth=new Texture2D(2,2,TextureFormat.RGBAFloat,false,true);
        depth.SetPixels(Enumerable.Repeat(SystemInfo.usesReversedZBuffer?Color.clear:Color.white,4).ToArray()); depth.Apply();
        material.SetTexture("CubeMap",sky); material.SetTexture("objectCubeMap",empty);
        Shader.SetGlobalTexture("SingularityScreenBuffer",scene.targetTexture); Shader.SetGlobalTexture("SingularityDepthTexture",depth);
        material.SetColor("galaxyFadeColor",Color.white);
        material.SetMatrix("cubeMapRotation",Matrix4x4.identity); material.SetFloat("gravity",0); material.SetFloat("blackHoleRadius",0.00001f);
        material.SetFloat("enclosingMeshRadius",40); material.SetFloat("useScreenBuffer",1); material.renderQueue=2501;
        material.EnableKeyword("CUSTOM_DEPTH_TEXTURE_ON"); material.EnableKeyword("GALAXYCUBEMAPONLY_OFF");
        material.EnableKeyword("ACCRETION_DISK_OFF"); material.EnableKeyword("WORMHOLE_OFF"); material.EnableKeyword("RADIAL_DISK_MAPPING_OFF");
        Capture(camera,null); var reference=Capture(camera,mode+"-Reference.png");
        Clear(scene.targetTexture,Color.magenta); Capture(scene,null); sphere.layer=10;
        var broken=Capture(camera,mode+"-Before.png");
        long referenceLight=Region(reference), error=Difference(reference,broken);
        Check(referenceLight>1000 && error>referenceLight/2,"Original sphere hides the galaxy background: "+mode+" error="+error+" light="+referenceLight);
        var plugin=new GameObject("Plugin substitute").AddComponent<Singularity.Singularity>(); Singularity.Singularity.Instance=plugin;
        plugin.screenBufferFlip=scene.targetTexture; plugin.stackingDepthBuffer=depth;
        var item=new GameObject("Singularity object substitute").AddComponent<Singularity.SingularityObject>(); item.SetMaterial(material); plugin.loadedObjects.Add(item);
        item.singularityCubeMap=item.gameObject.AddComponent<Singularity.SingularityCenteredCubeMap>(); item.singularityCubeMap.singularityCubemap=empty;
        SingularityCompatibility.Tick();
        Check(material.shader.name=="GalacticDisk/SingularityBackground","Optional shader binds to the existing material");
        Check(material.GetTexture("CubeMap")==sky && material.IsKeywordEnabled("CUSTOM_DEPTH_TEXTURE_ON"),"Textures and shader variants are preserved");
        sphere.layer=0; Clear(scene.targetTexture,Color.magenta); var capture=Capture(scene,null);
        Check(scene.clearFlags==CameraClearFlags.Depth && scene.backgroundColor==Color.magenta,"Capture clear settings are restored after rendering");
        Check(capture[0].r<5 && capture[0].b<5,"Empty capture pixels are cleared rather than retaining old colors");
        sphere.layer=10; var fixedImage=Capture(camera,mode+"-After.png"); long fixedError=Difference(reference,fixedImage);
        Debug.Log("SINGULARITY_IMAGE "+mode+" before="+error+" after="+fixedError+" reference="+referenceLight);
        Check(fixedError<error*0.12,"Captured background is preserved across the enclosing sphere: "+mode);
        material.DisableKeyword("CUSTOM_DEPTH_TEXTURE_ON"); material.EnableKeyword("CUSTOM_DEPTH_TEXTURE_OFF");
        Check(Difference(reference,Capture(camera,mode+"-Unstacked.png"))<error*0.15,"Unstacked lensing retains the transparent background: "+mode);
        material.DisableKeyword("CUSTOM_DEPTH_TEXTURE_OFF"); material.EnableKeyword("CUSTOM_DEPTH_TEXTURE_ON");
        material.SetFloat("blackHoleRadius",5); material.SetFloat("gravity",0.5f);
        var horizon=Capture(camera,mode+"-Horizon.png"); Check(Center(horizon)<Center(reference)*0.2,"The real event horizon still obscures background light");
        var diskTexture=new Texture2D(2,2,TextureFormat.RGBA32,false); diskTexture.SetPixels(Enumerable.Repeat(new Color(1,0.2f,0.01f,1),4).ToArray()); diskTexture.Apply();
        material.SetTexture("AccretionDisk",diskTexture); material.SetVector("diskNormal",Vector3.up);
        material.SetFloat("diskInnerRadius",5); material.SetFloat("diskOuterRadius",15);
        material.DisableKeyword("ACCRETION_DISK_OFF"); material.EnableKeyword("ACCRETION_DISK_ON");
        var accretion=Capture(camera,mode+"-Accretion.png");
        Check(accretion.Count(p=>p.r>70 && p.r>p.g*2 && p.r>p.b*2)>5,"The accretion disk variant still renders");
        material.DisableKeyword("ACCRETION_DISK_ON"); material.EnableKeyword("ACCRETION_DISK_OFF");
        UnityEngine.Object.DestroyImmediate(diskTexture);
        var blocker=GameObject.CreatePrimitive(PrimitiveType.Cube); blocker.layer=10;
        blocker.transform.position=camera.transform.position+camera.transform.forward*60; blocker.transform.localScale=Vector3.one*12;
        var blockerMaterial=new Material(Shader.Find("Unlit/Color")); blockerMaterial.color=Color.cyan; blocker.GetComponent<Renderer>().sharedMaterial=blockerMaterial;
        var foreground=Capture(camera,mode+"-Foreground.png"); Check(foreground[160*480+240].g>200,"Ordinary foreground geometry stays in front of the singularity");
        blocker.layer=0; material.SetFloat("blackHoleRadius",0.00001f); material.SetFloat("gravity",0); material.SetFloat("useScreenBuffer",0);
        foreach(CubemapFace face in Enum.GetValues(typeof(CubemapFace))) if((int)face>=0 && (int)face<6)
            empty.SetPixels(Enumerable.Repeat(new Color(0.02f,0.02f,0.02f,0.1f),16).ToArray(),face);
        empty.Apply(); var darkCube=Capture(camera,null);
        foreach(CubemapFace face in Enum.GetValues(typeof(CubemapFace))) if((int)face>=0 && (int)face<6)
            sky.SetPixels(Enumerable.Repeat(Color.white,16).ToArray(),face);
        sky.Apply(); var brightCube=Capture(camera,null);
        Check(Region(brightCube)>Region(darkCube)*3,"Partially transparent object cubemaps preserve the stock sky behind them");
        SingularityCompatibility.Clear(); Check(material.shader==original,"Cleanup restores the plugin's original shader"); Singularity.Singularity.Instance=null;
        disks.Remove(owner); owner.ReleaseRenderer();
        foreach(var obj in new UnityEngine.Object[] { root,sphere,blocker,blockerMaterial,plugin.gameObject,item.gameObject,material,sky,empty,depth }) UnityEngine.Object.DestroyImmediate(obj);
        DestroyCamera(camera); DestroyCamera(scene);
    }
    private void CustomViews()
    {
        var camera=CreateCamera("Cubemap face view"); camera.fieldOfView=90;
        foreach(DiskRenderMode mode in new[] { DiskRenderMode.Volumetric,DiskRenderMode.Particles })
        {
            var root=new GameObject("Face galaxy"); root.transform.position=Vector3.right*300;
            var c=new GalaxyConfig { renderMode=mode,starCount=1000,rotationX=50,minStarSize=0.4f,maxStarSize=1.2f }; c.volume.cloudResolution=64;
            c.volume.enableDustClouds=false; c.volume.enableStarFormation=false;
            GalacticDiskGenerator.RegenerateDisk(root,c); var owner=root.GetComponent<GalacticDiskObject>(); owner.LateUpdate(); disks.Add(owner);
            camera.worldToCameraMatrix=Matrix4x4.Scale(new Vector3(1,1,-1))*Matrix4x4.Rotate(Quaternion.LookRotation(Vector3.right)).inverse;
            Check(ParticleRenderingChecks.Brightness(Capture(camera,null))>1000,"Custom view rotation renders detailed galaxy: "+mode);
            camera.fieldOfView=60; root.transform.position=Vector3.right*30000;
            Check(ParticleRenderingChecks.Brightness(Capture(camera,null))>5 && DiskRenderOrder.Stats(camera).dotDisks==1,"Custom view rotation renders a distant dot: "+mode);
            disks.Remove(owner); owner.ReleaseRenderer(); UnityEngine.Object.DestroyImmediate(root);
        }
        DestroyCamera(camera);
    }
    private void CubeFaces()
    {
        var directions=new[] { Vector3.right,Vector3.left,Vector3.up,Vector3.down,Vector3.forward,Vector3.back };
        var colors=new[] { Color.red,Color.green,Color.blue,Color.yellow,Color.magenta,Color.cyan };
        var roots=new List<GameObject>(); var camera=CreateCamera("Actual cube capture"); camera.fieldOfView=90;
        for(int i=0;i<6;i++)
        {
            var root=new GameObject("Face "+i); root.transform.position=directions[i]*300; roots.Add(root);
            var c=new GalaxyConfig { starCount=500,bulgeFraction=1,bulgeScaleX=20,bulgeScaleY=20,bulgeScaleZ=20,
                colorBulge=colors[i],minStarSize=2,maxStarSize=3 };
            GalacticDiskGenerator.RegenerateDisk(root,c); var owner=root.GetComponent<GalacticDiskObject>(); owner.LateUpdate(); disks.Add(owner);
        }
        var cube=new RenderTexture(128,128,24,RenderTextureFormat.ARGB32); cube.dimension=UnityEngine.Rendering.TextureDimension.Cube; cube.Create();
        var target=camera.targetTexture; camera.targetTexture=null;
        Check(camera.RenderToCubemap(cube),"Real RenderToCubemap succeeds");
        var previous=RenderTexture.active;
        for(int i=0;i<6;i++)
        {
            Graphics.SetRenderTarget(cube,0,(CubemapFace)i);
            var image=new Texture2D(128,128,TextureFormat.RGB24,false); image.ReadPixels(new Rect(0,0,128,128),0,0); image.Apply();
            Color total=Color.clear; foreach(Color32 pixel in image.GetPixels32()) total+=(Color)pixel;
            Check(total.r+total.g+total.b>5,"Cubemap face contains its galaxy: "+i);
            Vector3 expected=new Vector3(colors[i].r,colors[i].g,colors[i].b).normalized;
            Vector3 actual=new Vector3(total.r,total.g,total.b).normalized;
            Check(Vector3.Dot(expected,actual)>0.99,"Each real cubemap face captures the correct direction: "+i+" color="+total);
            File.WriteAllBytes("SingularityVerification/Face-"+i+".png",image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image);
        }
        RenderTexture.active=previous; camera.targetTexture=target;
        foreach(var root in roots) { var owner=root.GetComponent<GalacticDiskObject>(); disks.Remove(owner); owner.ReleaseRenderer(); UnityEngine.Object.DestroyImmediate(root); }
        UnityEngine.Object.DestroyImmediate(cube); DestroyCamera(camera);
    }
    private void Prepare(Camera camera)
    {
        foreach(var owner in disks)
        {
            object renderer=owner.volume!=null?(object)owner.volume:owner.stableParticles;
            renderer.GetType().GetMethod("PrepareCamera",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(renderer,new object[] { camera });
        }
    }
    private Camera CreateCamera(string name)
    {
        var c=new GameObject(name).AddComponent<Camera>(); c.cullingMask=1<<10; c.fieldOfView=48; c.nearClipPlane=0.1f; c.farClipPlane=1000;
        c.clearFlags=CameraClearFlags.SolidColor; c.backgroundColor=Color.black;
        c.targetTexture=new RenderTexture(480,320,24,RenderTextureFormat.ARGB32); c.targetTexture.Create(); return c;
    }
    private Color32[] Capture(Camera camera,string name)
    {
        Prepare(camera);
        var texture=new Texture2D(480,320,TextureFormat.RGB24,false); var previous=RenderTexture.active;
        try { camera.Render(); RenderTexture.active=camera.targetTexture; texture.ReadPixels(new Rect(0,0,480,320),0,0); texture.Apply();
            if(name!=null) { Directory.CreateDirectory("SingularityVerification"); File.WriteAllBytes("SingularityVerification/"+name,texture.EncodeToPNG()); }
            return texture.GetPixels32(); }
        finally { RenderTexture.active=previous; UnityEngine.Object.DestroyImmediate(texture); }
    }
    private static void Clear(RenderTexture target,Color color) { var previous=RenderTexture.active; RenderTexture.active=target; GL.Clear(true,true,color); RenderTexture.active=previous; }
    private static long Region(Color32[] pixels) { long result=0; for(int y=120;y<200;y++) for(int x=200;x<280;x++) { var p=pixels[y*480+x]; result+=p.r+p.g+p.b; } return result; }
    private static long Center(Color32[] pixels) { long result=0; for(int y=158;y<162;y++) for(int x=238;x<242;x++) { var p=pixels[y*480+x]; result+=p.r+p.g+p.b; } return result; }
    private static long Difference(Color32[] a,Color32[] b) { long result=0; for(int y=120;y<200;y++) for(int x=200;x<280;x++) { int i=y*480+x; result+=Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b); } return result; }
    private static void DestroyCamera(Camera camera) { var target=camera.targetTexture; camera.targetTexture=null; UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(camera.gameObject); }
    private void Check(bool condition,string message) { checks++; if(!condition) throw new Exception("Singularity check failed: "+message); }
}
#endif
