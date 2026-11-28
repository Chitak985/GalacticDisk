#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GalacticDisk;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class MainMenuChecks
{
    private const string Key = "GalacticDisk.MainMenuChecks";
    private static int checks, stage, waitUntil, lastFrame = -1;
    private static Scene menuScene;
    private static AsyncOperation unloading;
    private static MainMenuGalaxy menu;
    private static GameObject oldRoot, oldRenderer;
    private static Camera landscape, ui;
    private static Camera backgroundCamera;
    private static Renderer skyboxCube, disabledSkyboxCube, scenery;
    private static GameObject namedSkybox, inactiveNamedSkybox;
    private static int cornerBrightness;
    private static Material skyMaterial;
    private static readonly Color originalBackground = new Color(0.1f,0.2f,0.3f,1);
    private static readonly Vector3 position = new Vector3(12,-3,7), rotation = new Vector3(25,30,5);
    static MainMenuChecks()
    {
        EditorApplication.playModeStateChanged += StateChanged;
        EditorApplication.update += Tick;
    }
    public static void Begin()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        SessionState.SetBool(Key,true); SessionState.SetString(Key+".error",""); EditorApplication.isPlaying = true;
    }
    private static void StateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key,false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            try
            {
                KSPUtil.ApplicationRootPath = Path.GetFullPath("TestGame");
                HighLogic.LoadedScene = GameScenes.MAINMENU; FlightGlobals.Bodies = null;
                ConfigChecks();
                menuScene = SceneManager.CreateScene("First main menu"); SceneManager.SetActiveScene(menuScene);
                Definitions(true); Cameras(); menu = AddMenu("Menu addon");
                waitUntil = Time.frameCount+4;
            }
            catch (Exception error) { Fail(error); }
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            string error = SessionState.GetString(Key+".error",""); SessionState.SetBool(Key,false);
            Directory.CreateDirectory("MainMenuVerification");
            File.WriteAllText("MainMenuVerification/MainMenu.txt",error.Length == 0 ?
                "PASS - Unity "+Application.unityVersion+"; "+checks+" checks.\n"+
                "Galaxy-only defaults, finite XYZ parsing, editor field/text/export round trips, ring exclusion;\n"+
                "first menu before bodies exist, only selected definition generated, independent position/rotation/scale;\n"+
                "real volume and particle camera rendering, UI camera isolation, scene unload and camera-mask cleanup;\n"+
                "Unity skybox and stock star-cube suppression only after successful generation, original states restored;\n"+
                "untyped GalaxyCube/MainMenuGalaxy hierarchy, disabled original objects, detached addon and black rendered background;\n"+
                "empty eligibility and invalid candidate fallback. KSP APIs are test substitutes.\n" : "FAIL - "+error);
            if (error.Length == 0) Debug.Log("GALACTICDISK_MAIN_MENU_PASSED: "+checks); else Debug.LogError(error);
            EditorApplication.Exit(error.Length == 0 ? 0 : 1);
        }
    }
    private static void Tick()
    {
        if (!SessionState.GetBool(Key,false) || !EditorApplication.isPlaying || lastFrame == Time.frameCount || Time.frameCount < waitUntil) return;
        lastFrame = Time.frameCount;
        try
        {
            if (stage == 0)
            {
                CheckMenu(true); oldRoot = menu.DiskRoot; oldRenderer = oldRoot.GetComponent<GalacticDiskObject>().renderRoot;
                var next = SceneManager.CreateScene("Between menu visits"); SceneManager.SetActiveScene(next);
                HighLogic.LoadedScene = GameScenes.SPACECENTER;
                unloading = SceneManager.UnloadSceneAsync(menuScene); stage = 1; return;
            }
            if (stage == 1)
            {
                if (!unloading.isDone) return;
                Check(menu == null && oldRoot == null && oldRenderer == null && MainMenuGalaxy.Active == null,"Scene unload releases the menu visual and addon");
                Check(GalaxyCloudCache.ActiveCount == 0,"Scene unload releases menu cloud data");
                HighLogic.LoadedScene = GameScenes.MAINMENU;
                Definitions(false); Cameras(); menu = AddMenu("Second menu addon");
                waitUntil = Time.frameCount+3; stage = 2; return;
            }
            if (stage == 2)
            {
                CheckMenu(false); oldRoot = menu.DiskRoot;
                UnityEngine.Object.Destroy(menu.gameObject); stage = 3; waitUntil = Time.frameCount+3; return;
            }
            if (stage == 3)
            {
                Check(oldRoot == null && MainMenuGalaxy.Active == null,"Addon removal cleans up the independently owned root");
                Check((landscape.cullingMask&(1<<10)) == 0,"Menu camera mask is restored");
                Check(landscape.GetCommandBuffers(DiskRenderOrder.DrawEvent).Length == 0,"Menu camera commands are released");
                CheckSkyboxRestored();
                GameDatabase.Instance.nodes.Clear();
                var disabled = Definition(false); disabled.AddValue("randomMainMenuDisk",false); GameDatabase.Instance.nodes.Add(disabled);
                var ring = new ConfigNode(RingConfig.NodeName); ring.AddValue("randomMainMenuDisk",true); GameDatabase.Instance.nodes.Add(ring);
                menu = new GameObject("No eligible galaxies").AddComponent<MainMenuGalaxy>();
                stage = 4; waitUntil = Time.frameCount+2; return;
            }
            if (stage == 4)
            {
                Check(menu.DiskRoot == null,"Default installation adds no main-menu galaxy");
                CheckSkyboxRestored();
                Check(Capture(landscape,"NoEligible-Skybox.png") > 100000,"No eligible galaxies keep the original menu background visible");
                UnityEngine.Object.DestroyImmediate(menu.gameObject);
                var invalid = Definition(false); invalid.AddValue("mainMenuScale",0); GameDatabase.Instance.nodes.Add(invalid);
                menu = new GameObject("Invalid candidate").AddComponent<MainMenuGalaxy>();
                stage = 5; waitUntil = Time.frameCount+2; return;
            }
            if (stage == 5)
            {
                Check(menu.DiskRoot == null && DiskSceneCache.Count == 0,"Invalid candidate leaves no partial disk");
                CheckSkyboxRestored();
                UnityEngine.Object.DestroyImmediate(menu.gameObject);
                GameDatabase.Instance.nodes.Add(Definition(false)); menu = new GameObject("Fallback candidates").AddComponent<MainMenuGalaxy>();
                stage = 6; waitUntil = Time.frameCount+2; return;
            }
            if (stage == 6)
            {
                Check(menu.DiskRoot != null && menu.SelectedConfig.mainMenuScale > 0,"A valid candidate is available even with an invalid eligible definition");
                UnityEngine.Object.DestroyImmediate(menu.gameObject); FlightGlobals.Bodies = new List<CelestialBody>();
                EditorApplication.isPlaying = false;
            }
        }
        catch (Exception error) { Fail(error); }
    }
    private static void ConfigChecks()
    {
        var defaults = GalaxyConfig.FromConfigNode(new ConfigNode("GalacticDiskDefinition"));
        Check(!defaults.randomMainMenuDisk && defaults.mainMenuScale == 1 && defaults.mainMenuPosition == Vector3.zero && defaults.mainMenuRotation == Vector3.zero,"Backward-compatible menu defaults");
        string text = "GalacticDiskDefinition\n{\nplanet = NotLoaded\nrandomMainMenuDisk = True\nmainMenuScale = 2e-1\nmainMenuPosition = 12, -3, 7\nmainMenuRotation = 25, 30, 5\n}";
        var parsed = ReadMenuText(text);
        Check(parsed.IsValid && parsed.config.randomMainMenuDisk && parsed.config.mainMenuPosition == position && parsed.config.mainMenuRotation == rotation,"Galaxy menu parsing without a loaded body");
        foreach (string bad in new[] { "1, 2", "1, 2, 3, 4", "1, NaN, 3", "Infinity, 2, 3" })
            Check(!ReadMenuText(text.Replace("12, -3, 7",bad)).IsValid,"Invalid menu triple rejected: "+bad);
        Check(!ReadMenuText(text.Replace("2e-1","0")).IsValid,"Nonpositive menu scale rejected");
        Check(!ConfigEditor.Schema(true).ContainsKey("randomMainMenuDisk") && !RingConfig.Export(new GalaxyConfig { isRing = true },"Ring").Contains("mainMenu"),"Rings do not expose or export menu settings");
        var toolbar = new GalacticDiskToolbar(); Invoke(toolbar,"PopulateFieldsFromConfig",parsed.config);
        var fields = (Dictionary<string,string>)Invoke(toolbar,"CaptureFieldValues");
        var fromFields = ConfigEditor.ReadValues(false,fields,false);
        Check(fromFields.IsValid && fromFields.config.mainMenuPosition == position && fromFields.config.randomMainMenuDisk,"Toolbar menu field round trip");
        string exported = (string)Invoke(toolbar,"GenerateConfigText",fromFields.config,"NotLoaded");
        var reloaded = ReadMenuText(exported);
        Check(reloaded.IsValid && reloaded.config.mainMenuScale == 0.2f && reloaded.config.mainMenuRotation == rotation,"Menu export round trip");
        var source = GalaxyConfig.FromConfigNode(Definition(true)); var copy = MainMenuGalaxy.PresentationConfig(source);
        Check(source.objectScale == 1e15f && source.rotationPeriod == 1000 && source.inheritBodyRotation && source.insideOpacityMultiplier == 0,"Menu presentation preserves gameplay settings");
        Check(!ReferenceEquals(source.volume,copy.volume) && !copy.enableDistantDots && copy.atmosphereOpacityMultiplier == 1 && copy.rotationPeriod == 0,"Independent menu volume snapshot and fixed presentation");
        var probe = new GameObject("Camera initialization probe").AddComponent<MainMenuGalaxy>(); probe.enabled = false;
        var fallback = new GameObject("Fallback camera").AddComponent<Camera>(); fallback.tag = "MainCamera";
        Check(probe.TargetCamera == fallback,"Menu supports an early fallback camera");
        var environment = new GameObject("Late environment").AddComponent<MainMenuEnvLogic>();
        var actual = new GameObject("Late landscape camera").AddComponent<Camera>(); environment.landscapeCamera = actual;
        Check(probe.TargetCamera == actual,"Environment camera supersedes an early fallback when ready");
        UnityEngine.Object.DestroyImmediate(probe.gameObject); UnityEngine.Object.DestroyImmediate(environment.gameObject);
        UnityEngine.Object.DestroyImmediate(fallback.gameObject); UnityEngine.Object.DestroyImmediate(actual.gameObject);
    }
    private static ConfigEditorResult ReadMenuText(string text)
    {
        var values = new Dictionary<string,string>();
        foreach (string line in text.Split('\n'))
        {
            int equals = line.IndexOf('=');
            if (equals > 0) values.Add(line.Substring(0,equals).Trim(),line.Substring(equals+1).Trim());
        }
        return ConfigEditor.ReadValues(false,values,false);
    }
    private static ConfigNode Definition(bool volume)
    {
        var node = new ConfigNode("GalacticDiskDefinition"); node.AddValue("planet","NotLoaded"); node.AddValue("randomMainMenuDisk",true);
        node.AddValue("mainMenuScale",0.8f); node.AddValue("mainMenuPosition","12, -3, 7"); node.AddValue("mainMenuRotation","25, 30, 5");
        node.AddValue("renderMode",volume?"Volumetric":"Particles"); node.AddValue("starCount",1024); node.AddValue("cloudResolution",64);
        node.AddValue("minStarSize",0.5f); node.AddValue("maxStarSize",1f); node.AddValue("seed",12345);
        node.AddValue("enableDustClouds",false); node.AddValue("enableStarFormation",false); node.AddValue("objectScale",1e15f); node.AddValue("renderScale",1e10f);
        node.AddValue("rotationPeriod",1000); node.AddValue("inheritBodyRotation",true); node.AddValue("alignRotationToOrbit",true); node.AddValue("insideOpacityMultiplier",0);
        node.AddValue("atmosphereOpacityMultiplier",0); node.AddValue("atmosphereNightOpacityMultiplier",0); return node;
    }
    private static void Definitions(bool volume)
    {
        GameDatabase.Instance.nodes.Clear(); GameDatabase.Instance.nodes.Add(Definition(volume));
        var disabled = Definition(!volume); disabled.AddValue("randomMainMenuDisk",false); disabled.AddValue("mainMenuScale",0); GameDatabase.Instance.nodes.Add(disabled);
        var omitted = new ConfigNode("GalacticDiskDefinition"); GameDatabase.Instance.nodes.Add(omitted);
        var ring = new ConfigNode(RingConfig.NodeName); ring.AddValue("randomMainMenuDisk",true); GameDatabase.Instance.nodes.Add(ring);
        Check(MainMenuGalaxy.EligibleDefinitions().Count == 1,"Only explicitly enabled galaxy definitions are eligible");
    }
    private static void Cameras()
    {
        landscape = new GameObject("Menu landscape").AddComponent<Camera>(); landscape.cullingMask = 1;
        landscape.transform.position = position+new Vector3(-90,120,-170); landscape.transform.LookAt(position);
        landscape.farClipPlane = 1000; landscape.backgroundColor = originalBackground; landscape.clearFlags = CameraClearFlags.Skybox;
        skyMaterial = new Material(Shader.Find("Skybox/6 Sided"));
        var skyTexture = new Texture2D(1,1); skyTexture.SetPixel(0,0,new Color(0.1f,0.3f,0.8f)); skyTexture.Apply();
        foreach (string side in new[] { "_FrontTex","_BackTex","_LeftTex","_RightTex","_UpTex","_DownTex" }) skyMaterial.SetTexture(side,skyTexture);
        RenderSettings.skybox = skyMaterial;
        var cube = new GameObject("Stock skybox cube").AddComponent<GalaxyCubeControl>();
        var face = GameObject.CreatePrimitive(PrimitiveType.Quad); face.transform.SetParent(cube.transform);
        face.transform.position = landscape.transform.position+landscape.transform.forward*50;
        face.transform.rotation = landscape.transform.rotation; face.transform.localScale = Vector3.one*300;
        skyboxCube = face.GetComponent<Renderer>(); skyboxCube.sharedMaterial = new Material(Shader.Find("Unlit/Color")) { color = Color.green };
        disabledSkyboxCube = new GameObject("Already hidden cube face").AddComponent<MeshRenderer>();
        disabledSkyboxCube.transform.SetParent(cube.transform); disabledSkyboxCube.enabled = false;
        scenery = new GameObject("Unrelated foreground scenery").AddComponent<MeshRenderer>();
        namedSkybox = NamedSkybox("GalaxyCube");
        inactiveNamedSkybox = NamedSkybox("GalaxyCube(Clone)"); inactiveNamedSkybox.SetActive(false);
        backgroundCamera = new GameObject("Stock galaxy camera").AddComponent<Camera>();
        backgroundCamera.gameObject.AddComponent<GalaxyCameraControl>(); backgroundCamera.cullingMask = 0;
        backgroundCamera.clearFlags = CameraClearFlags.Depth; backgroundCamera.backgroundColor = originalBackground;
        new GameObject("Main menu environment").AddComponent<MainMenuEnvLogic>().landscapeCamera = landscape;
        ui = new GameObject("Menu UI").AddComponent<Camera>(); ui.tag = "MainCamera"; ui.cullingMask = 1<<10;
        ui.transform.SetPositionAndRotation(landscape.transform.position,landscape.transform.rotation);
        ui.farClipPlane = 1000; ui.backgroundColor = Color.black; ui.clearFlags = CameraClearFlags.SolidColor;
    }
    private static MainMenuGalaxy AddMenu(string name)
    {
        var addon = new GameObject(name); addon.transform.SetParent(namedSkybox.transform);
        return addon.AddComponent<MainMenuGalaxy>();
    }
    private static GameObject NamedSkybox(string name)
    {
        // Match the reported menu hierarchy without a GalaxyCubeControl component.
        var root = new GameObject(name);
        var model = GameObject.CreatePrimitive(PrimitiveType.Quad); model.name = "MainMenuGalaxy"; model.transform.SetParent(root.transform);
        model.transform.position = landscape.transform.position+landscape.transform.forward*40;
        model.transform.rotation = landscape.transform.rotation; model.transform.localScale = Vector3.one*300;
        model.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Unlit/Color")) { color = Color.blue };
        return root;
    }
    private static void CheckMenu(bool volume)
    {
        Check(menu.DiskRoot != null && menu.SelectedConfig.planet == "NotLoaded","Menu works before any celestial bodies are loaded");
        var owner = menu.DiskRoot.GetComponent<GalacticDiskObject>();
        Check(UnityEngine.Object.FindObjectsOfType<GalacticDiskObject>().Length == 1 && DiskSceneCache.Count == 0,"Only the selected menu galaxy is generated outside the gameplay cache");
        Check(owner.orbitBody == null && menu.DiskRoot.transform.position == position && Quaternion.Angle(menu.DiskRoot.transform.rotation,Quaternion.Euler(rotation)) < 0.01f,"Independent menu position and rotation");
        Check(owner.config.objectScale == 0.8f && owner.config.renderScale == 1 && (owner.volume != null) == volume,"Menu scale and configured render mode");
        Check(menu.TargetCamera == landscape,"Environment camera takes precedence over the tagged UI camera");
        Check(!namedSkybox.activeSelf && !inactiveNamedSkybox.activeSelf,"The named GalaxyCube is disabled without enabling an originally inactive cube");
        Check(menu.isActiveAndEnabled && !menu.transform.IsChildOf(namedSkybox.transform),"The mod addon keeps running when originally parented under GalaxyCube");
        Check(Capture(landscape,volume?"Volumetric.png":"Particles.png") > 1000,"Menu galaxy renders on the landscape camera");
        Check(cornerBrightness == 0,"The rendered background is black outside the selected galaxy");
        Check(landscape.GetCommandBuffers(DiskRenderOrder.DrawEvent).Length > 0,"Detailed menu renderer submits camera commands");
        Check(Capture(ui,"UI.png") == 0 && ui.GetCommandBuffers(DiskRenderOrder.DrawEvent).Length == 0,"Other cameras do not draw the menu galaxy");
        Check((landscape.cullingMask&(1<<10)) != 0,"Menu layer is enabled for a camera that originally excluded it");
        Check(landscape.clearFlags == CameraClearFlags.SolidColor && landscape.backgroundColor == Color.black,"Unity menu skybox is replaced by black");
        Check(backgroundCamera.clearFlags == CameraClearFlags.Depth && backgroundCamera.backgroundColor == Color.black,"Separate KSP star camera retains depth-only stacking and a black background");
        Check(!skyboxCube.enabled && !disabledSkyboxCube.enabled && scenery.enabled,"Only stock skybox renderers are hidden");
        Check(RenderSettings.skybox == skyMaterial,"Shared sky material remains intact for other scenes");
        var lateCube = NamedSkybox("GalaxyCube(Clone)"); Invoke(menu,"LateUpdate");
        Check(!lateCube.activeSelf,"A GalaxyCube hierarchy created after selection is also hidden");
        namedSkybox.SetActive(true); Capture(landscape,"Reactivated-Skybox.png");
        Check(!namedSkybox.activeSelf && cornerBrightness == 0,"Background reactivation is suppressed before camera rendering");
        UnityEngine.Object.DestroyImmediate(lateCube);
        var lateCamera = new GameObject("Late skybox camera").AddComponent<Camera>(); lateCamera.clearFlags = CameraClearFlags.Skybox;
        lateCamera.cullingMask = 0; lateCamera.backgroundColor = originalBackground;
        Check(Capture(lateCamera,"Hidden-Skybox.png") == 0,"A camera created after selection also renders no skybox");
        UnityEngine.Object.DestroyImmediate(lateCamera.gameObject);
    }
    private static void CheckSkyboxRestored()
    {
        Check(landscape.clearFlags == CameraClearFlags.Skybox && landscape.backgroundColor == originalBackground,"Unity skybox camera flags and color are preserved without a menu disk");
        Check(backgroundCamera.clearFlags == CameraClearFlags.Depth && backgroundCamera.backgroundColor == originalBackground,"Stock sky camera flags and color are restored");
        Check(skyboxCube.enabled && !disabledSkyboxCube.enabled && scenery.enabled,"Original renderer states are restored without affecting scenery");
        Check(namedSkybox.activeSelf && !inactiveNamedSkybox.activeSelf,"Named skybox objects regain their original active states");
    }
    private static long Capture(Camera camera,string name)
    {
        Directory.CreateDirectory("MainMenuVerification"); var target = RenderTexture.GetTemporary(480,320,24,RenderTextureFormat.ARGB32);
        var image = new Texture2D(480,320,TextureFormat.RGB24,false); var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target; image.ReadPixels(new Rect(0,0,480,320),0,0); image.Apply();
            Color32 corner = image.GetPixel(8,8); cornerBrightness = corner.r+corner.g+corner.b;
            File.WriteAllBytes("MainMenuVerification/"+name,image.EncodeToPNG()); return ParticleRenderingChecks.Brightness(image.GetPixels32());
        }
        finally { camera.targetTexture = null; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(image); }
    }
    private static object Invoke(object target,string name,params object[] args) { return target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args); }
    private static void Check(bool condition,string message) { checks++; if (!condition) throw new InvalidOperationException("Main menu check failed: "+message); }
    private static void Fail(Exception error) { SessionState.SetString(Key+".error",error.ToString()); EditorApplication.isPlaying = false; }
}
#endif
