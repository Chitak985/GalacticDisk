using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace GalacticDisk
{
    [KSPAddon(KSPAddon.Startup.MainMenu,false)]
    public sealed class MainMenuGalaxy : MonoBehaviour
    {
        private static readonly System.Random selectionRandom = new System.Random();
        internal static MainMenuGalaxy Active { get; private set; }
        internal GameObject DiskRoot { get; private set; }
        internal GalaxyConfig SelectedConfig { get; private set; }
        private Camera sceneCamera;
        private bool environmentCameraResolved;
        private readonly Dictionary<Camera,bool> cameraLayers = new Dictionary<Camera,bool>();
        private struct SkyboxCameraState { internal CameraClearFlags flags; internal Color color; }
        private readonly Dictionary<Camera,SkyboxCameraState> skyboxCameras = new Dictionary<Camera,SkyboxCameraState>();
        private readonly Dictionary<Renderer,bool> skyboxRenderers = new Dictionary<Renderer,bool>();
        private readonly Dictionary<GameObject,bool> skyboxObjects = new Dictionary<GameObject,bool>();
        private bool skyboxSubscribed;
        private static readonly Type environmentType = typeof(HighLogic).Assembly.GetType("MainMenuEnvLogic");
        private static readonly Type galaxyCubeType = typeof(HighLogic).Assembly.GetType("GalaxyCubeControl");
        private static readonly Type galaxyCameraType = typeof(HighLogic).Assembly.GetType("GalaxyCameraControl");
        private static readonly FieldInfo environmentCamera = environmentType == null ? null :
            environmentType.GetField("landscapeCamera",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);

        internal Camera TargetCamera
        {
            get
            {
                if (environmentCameraResolved && sceneCamera != null && sceneCamera.isActiveAndEnabled) return EnableGalaxyLayer(sceneCamera);
                if (environmentCamera != null)
                {
                    var environment = UnityEngine.Object.FindObjectOfType(environmentType);
                    var camera = environment == null ? null : environmentCamera.GetValue(environment) as Camera;
                    if (camera != null && camera.isActiveAndEnabled)
                    {
                        sceneCamera = camera; environmentCameraResolved = true;
                        return EnableGalaxyLayer(sceneCamera);
                    }
                }
                environmentCameraResolved = false;
                if (sceneCamera != null && sceneCamera.isActiveAndEnabled) return EnableGalaxyLayer(sceneCamera);
                sceneCamera = Camera.main;
                if (sceneCamera != null && sceneCamera.isActiveAndEnabled) return EnableGalaxyLayer(sceneCamera);
                sceneCamera = null;
                foreach (Camera camera in Camera.allCameras)
                    if (camera.cameraType == CameraType.Game && !camera.orthographic) { sceneCamera = camera; break; }
                return sceneCamera == null ? null : EnableGalaxyLayer(sceneCamera);
            }
        }
        private Camera EnableGalaxyLayer(Camera camera)
        {
            const int layer = 1<<10;
            if (!cameraLayers.ContainsKey(camera)) cameraLayers.Add(camera,(camera.cullingMask&layer)!=0);
            if ((camera.cullingMask&layer)==0) camera.cullingMask |= layer;
            return camera;
        }

        internal static List<ConfigNode> EligibleDefinitions()
        {
            var result = new List<ConfigNode>();
            if (GameDatabase.Instance == null) return result;
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("GalacticDiskDefinition"))
                if (bool.TryParse(node.GetValue("randomMainMenuDisk"),out bool enabled) && enabled) result.Add(node);
            return result;
        }
        internal static GalaxyConfig PresentationConfig(GalaxyConfig source)
        {
            if (source.isRing) throw new ArgumentException("Rings cannot be main-menu galaxies.");
            if (float.IsNaN(source.mainMenuScale) || float.IsInfinity(source.mainMenuScale) || source.mainMenuScale <= 0)
                throw new ArgumentException("mainMenuScale must be finite and positive.");
            GalaxyConfig copy = source.Copy();
            copy.objectScale = source.mainMenuScale; copy.renderScale = 1;
            copy.rotationX = source.mainMenuRotation.x; copy.rotationY = source.mainMenuRotation.y; copy.rotationZ = source.mainMenuRotation.z;
            copy.rotationPeriod = 0; copy.alignRotationToOrbit = false; copy.inheritBodyRotation = false;
            copy.insideOpacityMultiplier = copy.atmosphereOpacityMultiplier = copy.atmosphereNightOpacityMultiplier = 1;
            copy.enableDistantDots = false;
            return copy;
        }
        public void Start()
        {
            if (HighLogic.LoadedScene != GameScenes.MAINMENU) return;
            if (Active != null && Active != this) { Destroy(gameObject); return; }
            Active = this; ReleaseVisual();
            var candidates = EligibleDefinitions();
            while (candidates.Count > 0)
            {
                int chosen = selectionRandom.Next(candidates.Count); ConfigNode node = candidates[chosen]; candidates.RemoveAt(chosen);
                try
                {
                    var config = GalaxyConfig.FromConfigNode(node);
                    GalaxyConfig presentation = PresentationConfig(config);
                    var root = new GameObject("GalacticDisk main-menu galaxy") { layer = 10 };
                    root.transform.position = config.mainMenuPosition;
                    DiskRoot = root;
                    GalacticDiskGenerator.RegenerateDisk(root,presentation);
                    root.GetComponent<GalacticDiskObject>().LateUpdate();
                    SelectedConfig = config;
                    Camera.onPreCull += PrepareSkyboxCamera; skyboxSubscribed = true;
                    UpdateSkybox();
                    Debug.Log("[GalacticDisk] Selected main-menu galaxy '"+config.planet+"'; menu scale="+VolumeConfig.Format(config.mainMenuScale)+".");
                    return;
                }
                catch (Exception error)
                {
                    ReleaseVisual();
                    Debug.LogWarning("[GalacticDisk] Could not create a main-menu galaxy: "+error.Message);
                }
            }
        }
        private bool HidesSkybox
        {
            get { return HighLogic.LoadedScene == GameScenes.MAINMENU && DiskRoot != null && SelectedConfig != null; }
        }
        private void LateUpdate()
        {
            if (HidesSkybox) UpdateSkybox();
            else if (skyboxSubscribed) RestoreSkybox();
        }
        private void UpdateSkybox()
        {
            // Menu prefabs can use a plain GalaxyCube/MainMenuGalaxy hierarchy
            // without GalaxyCubeControl. Disable that hierarchy independently of scripts.
            HideNamedSkybox("GalaxyCube");
            HideNamedSkybox("GalaxyCube(Clone)");
            // KSP's star background is geometry rendered by its own camera.
            // Keep the controllers alive and retain each renderer's original state.
            if (galaxyCubeType != null)
                foreach (Component cube in UnityEngine.Object.FindObjectsOfType(galaxyCubeType))
                    foreach (Renderer renderer in cube.GetComponentsInChildren<Renderer>(true))
                    {
                        if (!skyboxRenderers.ContainsKey(renderer)) skyboxRenderers.Add(renderer,renderer.enabled);
                        renderer.enabled = false;
                    }
            if (galaxyCameraType != null)
                foreach (Component controller in UnityEngine.Object.FindObjectsOfType(galaxyCameraType))
                {
                    Camera camera = controller.GetComponent<Camera>();
                    if (camera != null) ClearWithoutSkybox(camera);
                }
            foreach (Camera camera in Camera.allCameras) PrepareSkyboxCamera(camera);
        }
        private void HideNamedSkybox(string name)
        {
            GameObject cube;
            while ((cube = GameObject.Find(name)) != null)
            {
                if (cube == gameObject || cube == DiskRoot) return;
                // KSP can parent the addon under the skybox. Keep our controller
                // and generated disk alive when the stock background is deactivated.
                if (transform.IsChildOf(cube.transform)) transform.SetParent(null,true);
                if (DiskRoot.transform.IsChildOf(cube.transform)) DiskRoot.transform.SetParent(null,true);
                if (!skyboxObjects.ContainsKey(cube))
                {
                    skyboxObjects.Add(cube,cube.activeSelf);
                    Debug.Log("[GalacticDisk] Disabled main-menu skybox hierarchy '"+cube.name+"'.");
                }
                cube.SetActive(false);
            }
        }
        private void PrepareSkyboxCamera(Camera camera)
        {
            if (!HidesSkybox) return;
            foreach (var entry in skyboxObjects)
                if (entry.Key != null && entry.Key.activeSelf) entry.Key.SetActive(false);
            if (camera.clearFlags == CameraClearFlags.Skybox || skyboxCameras.ContainsKey(camera))
                ClearWithoutSkybox(camera);
        }
        private void ClearWithoutSkybox(Camera camera)
        {
            if (!skyboxCameras.ContainsKey(camera))
                skyboxCameras.Add(camera,new SkyboxCameraState { flags = camera.clearFlags, color = camera.backgroundColor });
            // Preserve depth-only camera stacking so foreground scenery is not cleared.
            if (camera.clearFlags == CameraClearFlags.Skybox) camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
        }
        private void RestoreSkybox()
        {
            if (skyboxSubscribed) Camera.onPreCull -= PrepareSkyboxCamera;
            skyboxSubscribed = false;
            foreach (var entry in skyboxCameras) if (entry.Key != null)
            { entry.Key.clearFlags = entry.Value.flags; entry.Key.backgroundColor = entry.Value.color; }
            foreach (var entry in skyboxRenderers) if (entry.Key != null) entry.Key.enabled = entry.Value;
            foreach (var entry in skyboxObjects)
                if (entry.Key != null && entry.Key.scene.isLoaded) entry.Key.SetActive(entry.Value);
            skyboxCameras.Clear(); skyboxRenderers.Clear();
            skyboxObjects.Clear();
        }
        private void ReleaseVisual()
        {
            RestoreSkybox();
            if (DiskRoot != null)
            {
                var owner = DiskRoot.GetComponent<GalacticDiskObject>();
                if (owner != null) owner.ReleaseRenderer();
                DiskRoot.SetActive(false); GalacticDiskObject.Release(DiskRoot);
            }
            DiskRoot = null; SelectedConfig = null;
            foreach (var entry in cameraLayers) if (entry.Key != null && !entry.Value) entry.Key.cullingMask &= ~(1<<10);
            cameraLayers.Clear();
        }
        private void OnDestroy()
        {
            ReleaseVisual(); if (Active == this) Active = null;
        }
    }
}
