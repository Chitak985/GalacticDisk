#if UNITY_EDITOR
using System;
using System.IO;
using GalacticDisk;
using GalacticDisk.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

[InitializeOnLoad]
public static class PlayModeChecks
{
    private const string Key = "GalacticDisk.PlayCheck";
    private static GameObject root;
    private static Camera camera;
    private static int startFrame;
    private static bool primed;
    private static GameObject occluder;
    private static int originFrames;
    private static int lastOriginFrame = -1;
    private static bool waitingForCleanup;
    static PlayModeChecks()
    {
        EditorApplication.playModeStateChanged += StateChanged;
        EditorApplication.update += Tick;
    }
    public static void Begin()
    {
        SessionState.SetBool(Key + ".ring", false);
        StartChecks();
    }
    public static void BeginRing()
    {
        SessionState.SetBool(Key + ".ring", true);
        StartChecks();
    }
    private static void StartChecks()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetString(Key + ".error", ""); SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }
    private static void StateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            try
            {
                KSPUtil.ApplicationRootPath = Path.GetFullPath("TestGame");
                var body = new GameObject("PlayBody").AddComponent<CelestialBody>();
                body.scaledBody = new GameObject("Scaled body"); FlightGlobals.Bodies.Add(body);
                root = new GameObject("Play galaxy") { layer = 10 };
                root.transform.SetParent(body.scaledBody.transform, false);
                var config = new GalaxyConfig { planet = body.name };
                if (SessionState.GetBool(Key + ".ring", false))
                {
                    config = GalaxyConfig.FromConfigNode(ConfigNode.Parse(File.ReadAllText("RingExample.txt")).GetNode(RingConfig.NodeName));
                    config.planet = body.name; config.objectScale = 1f;
                }
                else VolumeConfig.ApplyMilkyWayPreset(config);
                config.starCount = 2048; config.volume.cloudResolution = 64;
                config.volume.dustCloudCount = 18; config.volume.starFormingRegionCount = 12;
                GalacticDiskGenerator.RegenerateDisk(root, config);
                camera = new GameObject("Play scaled-space camera").AddComponent<Camera>();
                camera.cullingMask = 1 << 10; camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black; camera.fieldOfView = 48f; camera.farClipPlane = 1000f;
                camera.transform.position = new Vector3(-70f, 140f, -190f); camera.transform.LookAt(Vector3.zero);
                startFrame = Time.frameCount;
            }
            catch (Exception error) { Fail(error); }
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            string error = SessionState.GetString(Key + ".error", "");
            SessionState.SetBool(Key, false);
            Directory.CreateDirectory("Verification");
            string report = SessionState.GetBool(Key + ".ring", false) ? "RingPlayMode.txt" : "PlayMode.txt";
            File.WriteAllText("Verification/" + report, error.Length == 0 ?
                "PASS - Unity " + Application.unityVersion + "\n" +
                "Actual Play-mode frames, automatic Camera.onPreCull subscription, camera depth preparation,\n" +
                "foreground opaque occlusion, persistent mesh stars, and no ParticleSystem components.\n" +
                "12 consecutive floating-origin frames with a rotating parent at 2.57e20/6000 scaled units.\n" +
                "KSP APIs are test substitutes; this is not a live KSP session.\n" : "FAIL - " + error);
            if (error.Length == 0) Debug.Log("GALACTICDISK_PLAYMODE_PASSED"); else Debug.LogError(error);
            EditorApplication.Exit(error.Length == 0 ? 0 : 1);
        }
    }
    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || camera == null) return;
        try
        {
            if (waitingForCleanup)
            {
                if (Time.frameCount <= lastOriginFrame) return;
                Require(camera.GetCommandBuffers(CameraEvent.BeforeForwardAlpha).Length == 0 &&
                    camera.GetCommandBuffers(CameraEvent.AfterForwardAlpha).Length == 0, "Destroyed renderer removes camera commands");
                EditorApplication.isPlaying = false;
                return;
            }
            if (root == null) return;
            int elapsed = Time.frameCount - startFrame;
            if (elapsed < 20) return;
            if (!primed)
            {
                var owner = root.GetComponent<GalacticDiskObject>();
                Require(owner != null && owner.volume != null && owner.volume.IsReady && owner.renderRoot.activeInHierarchy,
                    "Renderer resources persist into Play mode");
                Require(root.GetComponentsInChildren<MeshRenderer>().Length >= 2, "Mesh renderers persist into Play mode");
                IntegrationChecks.Capture(camera, "KSP-PlayMode-Renderer.png");
                Require((camera.depthTextureMode & DepthTextureMode.Depth) != 0, "Automatic camera depth request");
                var opaque = GameObject.CreatePrimitive(PrimitiveType.Sphere); occluder = opaque;
                opaque.layer = 10; opaque.transform.position = camera.transform.position * 0.5f;
                opaque.transform.localScale = Vector3.one * 18f;
                var material = new Material(Shader.Find("Standard")); material.color = Color.green;
                material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", Color.green);
                opaque.GetComponent<Renderer>().sharedMaterial = material;
                primed = true;
                return;
            }
            if (elapsed < 40) return;
            var renderer = root.GetComponent<GalacticDiskObject>().volume;
            if (originFrames > 0)
            {
                if (Time.frameCount == lastOriginFrame) return;
                lastOriginFrame = Time.frameCount;
                var owner = root.GetComponent<GalacticDiskObject>();
                owner.orbitBody.scaledBody.transform.rotation = Quaternion.Euler(17f, 31f + originFrames * 0.3f, 11f);
                owner.LateUpdate();
                Require(CameraFrameChecks.Capture(camera, "PlayOrigin-" + originFrames + ".png", renderer) > 200000,
                    "Floating-origin volume must remain visible every Play-mode frame");
                if (++originFrames <= 12) return;
                renderer.enabled = false;
                Require(camera.GetCommandBuffers(CameraEvent.BeforeForwardAlpha).Length == 0 &&
                    camera.GetCommandBuffers(CameraEvent.AfterForwardAlpha).Length == 0, "OnDisable removes camera commands");
                Require(CameraFrameChecks.Capture(camera, "PlayDisabled.png", renderer) == 0, "Disabled volume does not draw");
                renderer.enabled = true;
                Require(CameraFrameChecks.Capture(camera, "PlayReenabled.png", renderer) > 200000, "OnEnable restores camera rendering");
                UnityEngine.Object.Destroy(root);
                waitingForCleanup = true;
                return;
            }
            Color center = IntegrationChecks.Capture(camera, "KSP-PlayMode-DepthOcclusion.png");
            Require(center.g > center.r * 1.5f && center.g > center.b * 1.5f, "Play-mode opaque occlusion: " + center);
            Require(root.GetComponentsInChildren<ParticleSystem>(true).Length == 0, "No particles after 40 frames");
            Require(renderer != null && renderer.IsReady && renderer.StarCount == 2048, "Persistent volume/mesh stars");
            UnityEngine.Object.Destroy(occluder);
            var disk = root.GetComponent<GalacticDiskObject>();
            disk.config.objectScale = 1.5e15f;
            disk.orbitBody.scaledBody.transform.localScale = Vector3.one * 1e6f;
            disk.orbitBody.scaledBody.transform.position = new Vector3(1f, -0.37f, 0.13f) * (2.57e20f / 6000f);
            camera.transform.position = Vector3.zero;
            camera.transform.rotation = Quaternion.LookRotation(new Vector3(1f, -0.37f, 0.13f).normalized);
            camera.farClipPlane = 1e6f;
            originFrames = 1; lastOriginFrame = Time.frameCount;
        }
        catch (Exception error) { Fail(error); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Fail(Exception error)
    {
        SessionState.SetString(Key + ".error", error.ToString()); EditorApplication.isPlaying = false;
    }
}
#endif
