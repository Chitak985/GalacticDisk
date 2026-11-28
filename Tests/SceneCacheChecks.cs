#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GalacticDisk;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class SceneCacheChecks
{
    private const string Key = "GalacticDisk.SceneCacheChecks";
    private static int stage, checks, lastFrame = -1, waitUntil, builds;
    private static Scene firstScene, secondScene;
    private static AsyncOperation unloading;
    private static GalacticDiskGenerator manager;
    private static CelestialBody body;
    private static Camera camera;
    private static GalacticDiskObject[] disks;
    private static readonly List<UnityEngine.Object> resources = new List<UnityEngine.Object>();
    private static readonly List<int> ids = new List<int>();
    private static Color32[] reference;
    private static readonly Vector3 viewOffset = new Vector3(-70,140,-190);
    static SceneCacheChecks()
    {
        EditorApplication.playModeStateChanged += StateChanged; EditorApplication.update += Tick;
    }
    public static void Begin()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        SessionState.SetBool(Key,true); SessionState.SetString(Key+".error",""); EditorApplication.isPlaying = true;
    }
    private static void StateChanged(PlayModeStateChange state)
    {
        if(!SessionState.GetBool(Key,false)) return;
        if(state == PlayModeStateChange.EnteredPlayMode)
        {
            try
            {
                KSPUtil.ApplicationRootPath = Path.GetFullPath("TestGame"); HighLogic.LoadedScene = GameScenes.SPACECENTER;
                firstScene = SceneManager.CreateScene("GalacticDisk cache scene A"); SceneManager.SetActiveScene(firstScene);
                body = Body(new Vector3(12,3,4)); AddDefinitions(body.name);
                manager = new GameObject("Scene cache manager").AddComponent<GalacticDiskGenerator>();
                waitUntil = Time.frameCount+4;
            }
            catch(Exception error) { Fail(error); }
        }
        if(state == PlayModeStateChange.EnteredEditMode)
        {
            string error = SessionState.GetString(Key+".error",""); SessionState.SetBool(Key,false);
            Directory.CreateDirectory("SceneCacheVerification");
            File.WriteAllText("SceneCacheVerification/SceneCache.txt",error.Length == 0 ?
                "PASS - Unity "+Application.unityVersion+"; "+checks+" checks.\n" +
                "All fields eager before any rendering camera exists. Galaxy, particle and ring layers\n" +
                "retain the same root, renderer, mesh, material and texture instances across real scene unloads\n" +
                "and Resources.UnloadUnusedAssets. Missing/replaced bodies rebind without regeneration.\n" +
                "Images remain equivalent, anchors sync before rendering, non-game scenes hide disks,\n" +
                "explicit edits affect only the selected disk and remove clears all body layers.\n" +
                "KSP scene events/bodies are test substitutes; Unity scene/asset unloads are real.\n" : "FAIL - "+error);
            if(error.Length == 0) Debug.Log("GALACTICDISK_SCENE_CACHE_PASSED: "+checks); else Debug.LogError(error);
            EditorApplication.Exit(error.Length == 0 ? 0 : 1);
        }
    }
    private static void Tick()
    {
        if(!SessionState.GetBool(Key,false) || !EditorApplication.isPlaying || lastFrame == Time.frameCount || Time.frameCount < waitUntil) return;
        lastFrame = Time.frameCount;
        try
        {
            if(stage == 0)
            {
                disks = DiskSceneCache.GetDisks(body.name); Check(disks.Length == 3,"Multiple definitions stay distinct");
                Check(GalaxyCloudCache.PendingCount == 0 && GalaxyCloudCache.ActiveCount == 2,"All volumetric fields generated without a camera");
                builds = GalaxyCloudCache.BuildCount;
                Save((UnityEngine.Object)typeof(GalaxyDetailNoise).GetField("texture",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null));
                Check(GalaxyDetailNoise.Users == 1,"Version 2 shared detail is retained by the cached galaxy");
                Check(DistantGalaxy.Users == 2,"Galaxy dot resources are shared; rings remain detailed");
                Save((UnityEngine.Object)typeof(DistantGalaxy).GetField("quad",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null));
                Save((UnityEngine.Object)typeof(DistantGalaxy).GetField("material",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null));
                foreach(var disk in disks)
                {
                    Check(disk.gameObject.scene.name == "DontDestroyOnLoad","Disk belongs to the persistent scene");
                    Save(disk.gameObject); Save(disk.renderRoot);
                    foreach(var filter in disk.GetComponentsInChildren<MeshFilter>(true)) Save(filter.sharedMesh);
                    foreach(var renderer in disk.GetComponentsInChildren<Renderer>(true)) Save(renderer.sharedMaterial);
                    if(disk.volume != null) Save(((GalaxyCloudField)Get(disk.volume,"cloudField")).texture);
                    Save(disk.ownedTexture);
                }
                camera = CameraAt(body); reference = Capture(camera,"Before.png");
                Check(camera.GetCommandBuffers(DiskRenderOrder.DrawEvent).Length == 3,"All layers draw before the transition");
                GameEvents.onGameSceneLoadRequested.Fire(GameScenes.FLIGHT); HighLogic.LoadedScene = GameScenes.FLIGHT;
                Check(camera.GetCommandBuffers(DiskRenderOrder.DrawEvent).Length == 0,"Transition releases old camera bindings");
                secondScene = SceneManager.CreateScene("GalacticDisk cache scene B"); SceneManager.SetActiveScene(secondScene);
                unloading = SceneManager.UnloadSceneAsync(firstScene); stage = 1; return;
            }
            if(stage == 1)
            {
                if(!unloading.isDone) return;
                Check(body == null && camera == null,"Old scene-owned body and camera were destroyed");
                Alive(); unloading = Resources.UnloadUnusedAssets(); stage = 2; return;
            }
            if(stage == 2)
            {
                if(!unloading.isDone) return;
                Alive(); FlightGlobals.Bodies.Clear(); GameEvents.onLevelWasLoaded.Fire(GameScenes.FLIGHT);
                foreach(var disk in disks) Check(!disk.gameObject.activeInHierarchy,"Missing body hides the cached disk");
                waitUntil = Time.frameCount+3; stage = 3; return;
            }
            if(stage == 3)
            {
                body = Body(new Vector3(10000,500,-7000)); DiskSceneCache.Tick();
                GameEvents.onLevelWasLoadedGUIReady.Fire(GameScenes.FLIGHT);
                foreach(var disk in disks) { disk.LateUpdate(); Check(disk.gameObject.activeInHierarchy && disk.orbitBody == body,"Cached layer rebinds to the replacement body"); }
                Alive(); Check(GalaxyCloudCache.BuildCount == builds,"Scene rebind did not regenerate textures");
                foreach(var disk in disks) if(disk.ps != null) Check(disk.ps.particleCount == disk.config.starCount,"Cached native particles are restored before rendering");
                camera = CameraAt(body); var after = Capture(camera,"After.png");
                Check(Difference(reference,after) < Math.Max(10,ParticleRenderingChecks.Brightness(reference)/10000),"Rebound rendering matches the original view");
                body.scaledBody.transform.position += new Vector3(17,23,-9); camera.transform.position = body.scaledBody.transform.position+viewOffset;
                Capture(camera,"MovedAnchor.png");
                foreach(var disk in disks) Check(disk.transform.position == body.scaledBody.transform.position,"Pre-cull anchor synchronization avoids a stale position");
                GameEvents.onGameSceneLoadRequested.Fire(GameScenes.MAINMENU); HighLogic.LoadedScene = GameScenes.MAINMENU;
                GameEvents.onLevelWasLoaded.Fire(GameScenes.MAINMENU); stage = 4; waitUntil = Time.frameCount+2; return;
            }
            if(stage == 4)
            {
                Alive(); foreach(var disk in disks) Check(!disk.gameObject.activeInHierarchy,"Non-game scenes hide cached disks");
                GameEvents.onGameSceneLoadRequested.Fire(GameScenes.TRACKSTATION); HighLogic.LoadedScene = GameScenes.TRACKSTATION;
                GameEvents.onLevelWasLoaded.Fire(GameScenes.TRACKSTATION);
                Alive(); Check(GalaxyCloudCache.BuildCount == builds,"Returning to gameplay uses the same textures");
                var selected = GalacticDiskGenerator.disksByPlanet[body.name].GetComponent<GalacticDiskObject>();
                var other = disks[0] == selected ? disks[1] : disks[0]; GameObject otherRenderer = other.renderRoot;
                var edit = GalaxyConfig.FromConfigNode(ConfigNode.Parse(RingConfig.Export(selected.config,body.name)).GetNode(RingConfig.NodeName));
                edit.colorInterarm = new Color(0.3f,0.6f,1,0.8f); GalacticDiskGenerator.ApplyDisk(body,edit);
                Check(other.renderRoot == otherRenderer,"Explicit Apply leaves other cached layers intact");
                GalacticDiskGenerator.RemoveDisk(body.name);
                Check(DiskSceneCache.Count == 0 && GalaxyCloudCache.ActiveCount == 0 && GalaxyCloudCache.PendingCount == 0,"Remove releases every cached layer for the body");
                Check(GalaxyDetailNoise.Users == 0,"Remove releases cached version 2 detail");
                Check(camera.GetCommandBuffers(DiskRenderOrder.DrawEvent).Length == 0,"Remove releases camera commands");
                UnityEngine.Object.Destroy(manager.gameObject); stage = 5; waitUntil = Time.frameCount+2; return;
            }
            if(stage == 5) EditorApplication.isPlaying = false;
        }
        catch(Exception error) { Fail(error); }
    }
    private static CelestialBody Body(Vector3 position)
    {
        var body = new GameObject("SceneBody").AddComponent<CelestialBody>(); body.scaledBody = new GameObject("Scene scaled body");
        body.scaledBody.transform.position = position; body.scaledBody.transform.localScale = Vector3.one*1e6f; FlightGlobals.Bodies.Add(body); return body;
    }
    private static void AddDefinitions(string name)
    {
        GameDatabase.Instance.nodes.Clear();
        foreach(bool volume in new[] { true,false })
        {
            var node = new ConfigNode("GalacticDiskDefinition"); node.AddValue("planet",name); node.AddValue("renderMode",volume?"Volumetric":"Particles");
            node.AddValue("starCount",512); node.AddValue("cloudResolution",64); node.AddValue("enableDustClouds",false); node.AddValue("enableStarFormation",false);
            if (volume) { node.AddValue("rendererVersion",2); node.AddValue("enableDustClouds",true); node.AddValue("dustCloudCount",12); }
            node.AddValue("renderScale",1e5f); GameDatabase.Instance.nodes.Add(node);
        }
        var ring = new ConfigNode(RingConfig.NodeName); ring.AddValue("planet",name); ring.AddValue("renderMode","Volumetric"); ring.AddValue("particleCount",256);
        ring.AddValue("innerRingRadius",30); ring.AddValue("ringRadius",90); ring.AddValue("cloudResolution",64); ring.AddValue("enableDustClouds",false);
        GameDatabase.Instance.nodes.Add(ring);
    }
    private static Camera CameraAt(CelestialBody body)
    {
        var camera = new GameObject("Scene scaled camera").AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black; camera.cullingMask = 1<<10; camera.farClipPlane = 1000; camera.fieldOfView = 48;
        camera.transform.position = body.scaledBody.transform.position+viewOffset; camera.transform.rotation = Quaternion.LookRotation(-viewOffset); return camera;
    }
    private static Color32[] Capture(Camera camera,string name)
    {
        Directory.CreateDirectory("SceneCacheVerification"); var target = RenderTexture.GetTemporary(480,320,24,RenderTextureFormat.ARGB32);
        var image = new Texture2D(480,320,TextureFormat.RGB24,false); var previous = RenderTexture.active;
        try { camera.targetTexture = target; camera.Render(); RenderTexture.active = target; image.ReadPixels(new Rect(0,0,480,320),0,0); image.Apply();
            File.WriteAllBytes("SceneCacheVerification/"+name,image.EncodeToPNG()); return image.GetPixels32(); }
        finally { camera.targetTexture = null; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(image); }
    }
    private static void Save(UnityEngine.Object value) { if(value == null) return; resources.Add(value); ids.Add(value.GetInstanceID()); }
    private static void Alive() { for(int i=0;i<resources.Count;i++) Check(resources[i] != null && resources[i].GetInstanceID() == ids[i],"Generated resource survives with the same identity"); }
    private static long Difference(Color32[] a,Color32[] b) { long sum=0; for(int i=0;i<a.Length;i++) sum+=Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b); return sum; }
    private static object Get(object target,string name) { return target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target); }
    private static void Check(bool condition,string message) { checks++; if(!condition) throw new InvalidOperationException("Scene cache check failed: "+message); }
    private static void Fail(Exception error) { SessionState.SetString(Key+".error",error.ToString()); EditorApplication.isPlaying = false; }
}
#endif
