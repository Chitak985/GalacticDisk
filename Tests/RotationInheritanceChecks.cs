#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GalacticDisk;
using UnityEditor;
using UnityEngine;

public static class RotationInheritanceChecks
{
    private static int checks;
    private const string ExactConfig = @"GalacticDiskDefinition
{
    planet = Eeloo
    silenceNoBodyError = false
    renderMode = Volumetric
    starCount = 0
    galaxyRadius = 120
    diskThickness = 5
    insideOpacityMultiplier = 0.6
    atmosphereOpacityMultiplier = 0.001
    atmosphereNightOpacityMultiplier = 0.4
    minStarSize = 0.055
    maxStarSize = 0.19
    armCount = 2
    armTightness = 0.8
    armWidth = 2
    interarmFraction = 0.5
    armConcentration = 0.5
    bulgeFraction = 0.4
    bulgeScaleX = 7.5
    bulgeScaleY = 4
    bulgeScaleZ = 7.5
    colorBulge = 1, 0.76, 0.4, 1
    colorArms = 0.52, 1, 0.66, 1
    colorInterarm = 0.88, 0.82, 0.68, 1
    armHighlightColor = 1, 0.3, 0.53, 0.85
    armHighlightChance = 0.035
    rotationX = 0
    rotationY = 25
    rotationZ = 0
    alignRotationToOrbit = False
    rotationPeriod = 0
    spinAroundDiskAxis = false
    diskScaleLengthFraction = 0.25
    objectScale = 2.7E+7
    renderScale = 10000
    usesTexture = false
    texturePath =
    enableDistantDots = true
    distantDotStartPixels = 12
    distantDotEndPixels = 4
    distantDotSize = 3
    distantDotBrightness = 1
}";
    public static void Run()
    {
        try
        {
            KSPUtil.ApplicationRootPath = Path.GetFullPath("TestGame");
            var body = new GameObject("Eeloo").AddComponent<CelestialBody>(); body.scaledBody = new GameObject("Eeloo scaled");
            body.scaledBody.transform.localScale = Vector3.one*1e6f; FlightGlobals.Bodies.Add(body);
            var root = new GameObject("Rotation test disk");
            var config = GalaxyConfig.FromConfigNode(ConfigNode.Parse(ExactConfig).GetNode("GalacticDiskDefinition"));
            Check(!config.inheritBodyRotation && config.rotationPeriod==0 && config.rotationY==25 && !config.alignRotationToOrbit,"Exact config defaults to a fixed disk");
            GalacticDiskGenerator.RegenerateDisk(root,config); DiskSceneCache.Register(root,body);
            var owner = root.GetComponent<GalacticDiskObject>();
            var camera = new GameObject("Rotation test camera").AddComponent<Camera>(); camera.cullingMask = 1<<10;
            camera.transform.position = new Vector3(-150,100,-180)*config.objectScale; camera.transform.LookAt(Vector3.zero);
            camera.fieldOfView = 48; camera.farClipPlane = 1000; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.targetTexture = new RenderTexture(480,320,24); camera.targetTexture.Create();
            owner.LateUpdate(); var reference = Capture(owner,camera,"Fixed-reference.png");
            Check(ParticleRenderingChecks.Brightness(reference)>1000,"The exact starCount=0 volume config renders");
            foreach(double time in new[] { 0d,1000d,1e8d,1e12d })
            {
                Planetarium.time = time; body.scaledBody.transform.rotation = Quaternion.Euler(11,(float)(time%360),27);
                owner.LateUpdate();
                // Mimic a scene transform changing after the mod's LateUpdate.
                DiskSceneCache.Parent.rotation = Quaternion.Euler(0,63,0);
                var image = Capture(owner,camera,null);
                Check(Quaternion.Angle(root.transform.rotation,Quaternion.Euler(0,25,0))<0.03,"Render-time sync retains fixed world orientation");
                Check(Difference(reference,image)<Math.Max(10,ParticleRenderingChecks.Brightness(reference)/10000),"Zero-period rendering stays unchanged across body rotation and time warp");
                DiskSceneCache.Parent.rotation = Quaternion.identity;
            }
            config.inheritBodyRotation = true; owner.LateUpdate();
            body.scaledBody.transform.rotation = Quaternion.Euler(0,70,0); Capture(owner,camera,"Inherited.png");
            Check(Quaternion.Angle(root.transform.rotation,body.scaledBody.transform.rotation*Quaternion.Euler(0,25,0))<0.03,"Latest body rotation is inherited before rendering");
            config.inheritBodyRotation = false; owner.LateUpdate(); Capture(owner,camera,null);
            Check(Quaternion.Angle(root.transform.rotation,Quaternion.Euler(0,25,0))<0.03,"Disabling inheritance restores world-fixed rotation");
            DiskSceneCache.Clear(); GalacticDiskGenerator.disksByPlanet.Clear();
            var target = camera.targetTexture; camera.targetTexture = null; UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(camera.gameObject);
            foreach(bool ring in new[] { false,true })
            foreach(DiskRenderMode mode in new[] { DiskRenderMode.Particles,DiskRenderMode.Volumetric })
            foreach(bool ownAxis in new[] { false,true })
            {
                var node = new GameObject("Combined rotation"); node.transform.SetParent(body.scaledBody.transform,false);
                owner = node.AddComponent<GalacticDiskObject>(); owner.orbitBody = body;
                config = new GalaxyConfig { planet=body.name,isRing=ring,renderMode=mode,rotationX=35,rotationY=25,rotationZ=17,
                    rotationPeriod=100,spinAroundDiskAxis=ownAxis,inheritBodyRotation=true };
                owner.config = config;
                foreach(double time in new[] { 0d,25d,1000025d })
                {
                    Planetarium.time = time; body.scaledBody.transform.rotation = Quaternion.Euler(13,(float)(time%360),21); owner.LateUpdate();
                    float spin = (float)((time/100%1)*360);
                    Quaternion own = ownAxis ? Quaternion.Euler(35,25,17)*Quaternion.AngleAxis(spin,Vector3.up) : Quaternion.Euler(35,25+spin,17);
                    Check(Quaternion.Angle(node.transform.rotation,body.scaledBody.transform.rotation*own)<0.03,"Inherited rotation composes with existing spin, shapes and modes");
                }
                var toolbar = new GalacticDiskToolbar(); Invoke(toolbar,"PopulateFieldsFromConfig",config);
                var values = (Dictionary<string,string>)Invoke(toolbar,"CaptureFieldValues");
                Check(ConfigEditor.ReadValues(ring,values,false).config.inheritBodyRotation,"Toolbar preserves inheritance");
                string text = (string)Invoke(toolbar,"GenerateConfigText",config,body.name);
                Check(ConfigEditor.Read(text).config.inheritBodyRotation,"Text round-trip preserves inheritance");
                config.alignRotationToOrbit = true; owner.LateUpdate();
                Vector3 normal = body.orbit.GetOrbitNormal(); normal = new Vector3(normal.x,normal.z,normal.y).normalized;
                Quaternion align = Quaternion.FromToRotation(Vector3.up,normal);
                Quaternion aligned = ownAxis ? Quaternion.AngleAxis(25,Vector3.up)*align*Quaternion.AngleAxis(90,Vector3.up) : align*Quaternion.Euler(0,115,0);
                Check(Quaternion.Angle(node.transform.rotation,body.scaledBody.transform.rotation*aligned)<0.03,"Orbit alignment is composed before inherited body rotation");
                UnityEngine.Object.DestroyImmediate(node);
            }
            FlightGlobals.Bodies.Clear(); UnityEngine.Object.DestroyImmediate(body.scaledBody); UnityEngine.Object.DestroyImmediate(body.gameObject);
            File.WriteAllText("RotationVerification/Rotation.txt","PASS - Unity "+Application.unityVersion+"; "+checks+" checks.\n"+
                "Exact zero-star config remains visually fixed across time/body rotation and late scene-transform changes.\n"+
                "Body rotation, existing Euler/own-axis spin, orbit alignment, shapes/modes and editor round-trips verified.\n"+
                "Real Unity rendering; KSP APIs are substitutes. No live KSP save loaded.\n");
            Debug.Log("GALACTICDISK_ROTATION_INHERITANCE_PASSED: "+checks);
        }
        catch(Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    private static Color32[] Capture(GalacticDiskObject owner,Camera camera,string name)
    {
        object renderer = owner.volume != null ? (object)owner.volume : owner.stableParticles;
        Invoke(renderer,"PrepareCamera",camera); var image = new Texture2D(480,320,TextureFormat.RGB24,false); var previous = RenderTexture.active;
        try { camera.Render(); RenderTexture.active = camera.targetTexture; image.ReadPixels(new Rect(0,0,480,320),0,0); image.Apply();
            Directory.CreateDirectory("RotationVerification"); if(name!=null) File.WriteAllBytes("RotationVerification/"+name,image.EncodeToPNG()); return image.GetPixels32(); }
        finally { RenderTexture.active=previous; UnityEngine.Object.DestroyImmediate(image); }
    }
    private static long Difference(Color32[] a,Color32[] b) { long result=0; for(int i=0;i<a.Length;i++) result+=Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b); return result; }
    private static object Invoke(object target,string name,params object[] args) { return target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args); }
    private static void Check(bool value,string message) { checks++; if(!value) throw new Exception("Rotation inheritance check failed: "+message); }
}
#endif
