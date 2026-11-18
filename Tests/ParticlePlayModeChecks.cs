#if UNITY_EDITOR
using System;
using System.IO;
using GalacticDisk;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

[InitializeOnLoad]
public static class ParticlePlayModeChecks
{
    private const string Key = "GalacticDisk.ParticlePlayCheck";
    private static GalacticDiskObject owner;
    private static Camera camera;
    private static int started, captured, lastFrame = -1;
    private static bool destroying;
    static ParticlePlayModeChecks()
    {
        EditorApplication.playModeStateChanged += StateChanged;
        EditorApplication.update += Tick;
    }
    public static void Begin()
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
                var body = new GameObject("Play particle body").AddComponent<CelestialBody>();
                body.scaledBody = new GameObject("Distant scaled parent");
                body.scaledBody.transform.position = new Vector3(1f, -0.37f, 0.13f) * (2.57e20f / 6000f);
                body.scaledBody.transform.localScale = Vector3.one * 1e6f; FlightGlobals.Bodies.Add(body);
                var root = new GameObject("Play particles") { layer = 10 }; root.transform.SetParent(body.scaledBody.transform, false);
                var config = new GalaxyConfig { planet = body.name, starCount = 4096, objectScale = 1.5e15f,
                    renderScale = 1e8f, minStarSize = 0.3f, maxStarSize = 1.8f };
                GalacticDiskGenerator.RegenerateDisk(root, config); owner = root.GetComponent<GalacticDiskObject>();
                camera = new GameObject("Particle play camera").AddComponent<Camera>();
                camera.cullingMask = 1 << 10; camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black; camera.fieldOfView = 48f; camera.farClipPlane = 1e6f;
                camera.transform.rotation = Quaternion.LookRotation(new Vector3(1f, -0.37f, 0.13f).normalized);
                started = Time.frameCount;
            }
            catch (Exception error) { Fail(error); }
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            string error = SessionState.GetString(Key + ".error", ""); SessionState.SetBool(Key, false);
            Directory.CreateDirectory("ParticleVerification");
            File.WriteAllText("ParticleVerification/ParticlePlayMode.txt", error.Length == 0 ?
                "PASS - Unity " + Application.unityVersion + "\n20 consecutive real Play-mode frames with a rotating distant parent,\n" +
                "floating-origin camera, short far plane, repeated particle clears, and automatic camera callbacks.\n" +
                "Inside opacity changes reach automatic camera drawing without rebuilding the disk.\n" +
                "Disable/re-enable/destruction remove and restore camera drawing correctly.\nKSP APIs are substitutes.\n" : "FAIL - " + error);
            if (error.Length == 0) Debug.Log("GALACTICDISK_PARTICLE_PLAYMODE_PASSED"); else Debug.LogError(error);
            EditorApplication.Exit(error.Length == 0 ? 0 : 1);
        }
    }
    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || camera == null || Time.frameCount == lastFrame) return;
        try
        {
            if (destroying)
            {
                Require(camera.GetCommandBuffers(DiskRenderOrder.DrawEvent).Length == 0, "Destroy removes particle commands");
                EditorApplication.isPlaying = false; return;
            }
            if (owner == null || Time.frameCount - started < 20) return;
            lastFrame = Time.frameCount;
            Require(owner.stableParticles != null && owner.stableParticles.IsReady, "Stable particle drawing persists in Play mode");
            owner.orbitBody.scaledBody.transform.rotation = Quaternion.Euler(17f, 31f + captured * 0.4f, 11f); owner.LateUpdate();
            if (captured % 3 == 0) owner.ps.Clear();
            Require(ParticleRenderingChecks.Brightness(ParticleRenderingChecks.Capture(camera, "Play-" + captured + ".png", owner)) > 100000,
                "Every real particle frame remains visible during native clears");
            if (++captured < 20) return;
            camera.transform.position = owner.transform.position + owner.transform.rotation * new Vector3(60,0,-30) * owner.config.objectScale;
            camera.transform.rotation = Quaternion.LookRotation((owner.transform.position-camera.transform.position)/owner.config.objectScale);
            owner.config.insideOpacityMultiplier = 0;
            Require(ParticleRenderingChecks.Brightness(ParticleRenderingChecks.Capture(camera,"Play-opacity-zero.png",owner)) == 0,"Automatic inside fade reaches zero");
            owner.config.insideOpacityMultiplier = 0.2f;
            Require(ParticleRenderingChecks.Brightness(ParticleRenderingChecks.Capture(camera,"Play-opacity-partial.png",owner)) > 0,"Automatic inside fade restores partial opacity");
            owner.config.insideOpacityMultiplier = 1;
            owner.stableParticles.enabled = false;
            Require(camera.GetCommandBuffers(DiskRenderOrder.DrawEvent).Length == 0, "OnDisable removes commands");
            owner.stableParticles.enabled = true;
            Require(ParticleRenderingChecks.Brightness(ParticleRenderingChecks.Capture(camera, "Play-reenabled.png", owner)) > 100000, "OnEnable restores drawing");
            UnityEngine.Object.Destroy(owner.gameObject); destroying = true;
        }
        catch (Exception error) { Fail(error); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException("Particle Play-mode check failed: " + message); }
    private static void Fail(Exception error) { SessionState.SetString(Key + ".error", error.ToString()); EditorApplication.isPlaying = false; }
}
#endif
