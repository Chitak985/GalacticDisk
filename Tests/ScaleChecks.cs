#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using GalacticDisk;
using GalacticDisk.Rendering;
using UnityEditor;
using UnityEngine;

public static class ScaleChecks
{
    public static void Diagnose()
    {
        try
        {
            KSPUtil.ApplicationRootPath = Path.GetFullPath("TestGame");
            var body = new GameObject("Scale body").AddComponent<CelestialBody>();
            body.scaledBody = new GameObject("Scaled parent"); FlightGlobals.Bodies.Add(body);
            var root = new GameObject("Scale disk") { layer = 10 };
            root.transform.SetParent(body.scaledBody.transform, false);
            var camera = new GameObject("Scale camera").AddComponent<Camera>();
            camera.cullingMask = 1 << 10; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black; camera.fieldOfView = 48f; camera.farClipPlane = 1000000f;
            Vector3 unit = new Vector3(-70, 140, -190);
            camera.transform.rotation = Quaternion.LookRotation(-unit.normalized);
            long referenceVolume = 0;
            foreach (float parentScale in new[] { 1f, 1e6f })
            {
                var config = new GalaxyConfig { planet = body.name, starCount = 20000, minStarSize = 0.3f, maxStarSize = 1.8f };
                config.objectScale = 1.5e15f; config.renderScale = 1e8f;
                body.scaledBody.transform.localScale = Vector3.one * parentScale;
                // Legacy Local scaling deliberately does not inherit the body's radius scale.
                camera.transform.position = unit * config.objectScale;
                config.renderMode = DiskRenderMode.Particles;
                camera.farClipPlane = 1e35f; // Legacy sprites retain their original camera clipping.
                UnityEngine.Random.InitState(7382);
                GalacticDiskGenerator.RegenerateDisk(root, config);
                var owner = root.GetComponent<GalacticDiskObject>(); owner.LateUpdate();
                owner.ps.Simulate(0.01f, true, false, false); owner.ps.Pause(true);
                Debug.Log("SCALE_DIAGNOSTIC: parent=" + parentScale + " defaultMode=" + owner.ps.main.scalingMode +
                    " rootScale=" + root.transform.localScale + " rendererScale=" + owner.ps.transform.localScale +
                    " lossy=" + owner.ps.transform.lossyScale + " bounds=" + owner.psR.bounds +
                    " inverse=" + root.transform.worldToLocalMatrix);
                Color32[] legacyPixels = Capture(camera, "Legacy-default-" + parentScale + ".png");
                Require(Brightness(legacyPixels) > 1000000, "Legacy disk visible at configured scales");
                VolumeConfig.ApplyMilkyWayPreset(config); config.starCount = 20000;
                camera.farClipPlane = 1000000f;
                config.volume.cloudResolution = 64; config.volume.dustCloudCount = 30; config.volume.starFormingRegionCount = 16;
                GalacticDiskGenerator.RegenerateDisk(root, config); owner.LateUpdate();
                Debug.Log("SCALE_DIAGNOSTIC: volume parent=" + parentScale + " lossy=" + owner.renderRoot.transform.lossyScale +
                    " inverse=" + owner.renderRoot.transform.worldToLocalMatrix);
                Color32[] volumePixels = Capture(camera, "Volume-" + parentScale + ".png");
                long volumeBrightness = Brightness(volumePixels);
                Require(volumeBrightness > 1000000, "Volume visible at configured scales beyond far plane");
                if (referenceVolume != 0) Require(volumeBrightness == referenceVolume, "Body radius scale does not change galaxy appearance");
                referenceVolume = volumeBrightness;
                Require(root.GetComponentsInChildren<ParticleSystem>(true).Length == 0, "Volume uses no particles");
                config.starCount = 0;
                GalacticDiskGenerator.RegenerateDisk(root, config); owner.LateUpdate();
                Require(Brightness(Capture(camera, "Clouds-only-" + parentScale + ".png")) > 1000000,
                    "Continuous volume visible without mesh stars");

                // Two legacy definitions may intentionally layer around the same body.
                // The new volume must preserve both and must not darken their pixels.
                root.SetActive(false);
                // Edit-mode tests simulate lifecycle dispatch explicitly; the Play-mode
                // checks verify Unity invokes this automatically when disabled.
                typeof(VolumetricDiskRenderer).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(owner.volume, null);
                camera.farClipPlane = 1e35f;
                var legacyRoots = new GameObject[2];
                for (int i = 0; i < legacyRoots.Length; i++)
                {
                    legacyRoots[i] = new GameObject("Legacy layer " + i) { layer = 10 };
                    legacyRoots[i].transform.SetParent(body.scaledBody.transform, false);
                    var legacy = new GalaxyConfig { planet = body.name, objectScale = config.objectScale,
                        renderScale = config.renderScale, starCount = 2048, minStarSize = 1f, maxStarSize = 2f,
                        colorBulge = Color.red, colorArms = Color.red, colorInterarm = Color.red, armHighlightChance = 0f };
                    GalacticDiskGenerator.RegenerateDisk(legacyRoots[i], legacy);
                    var layer = legacyRoots[i].GetComponent<GalacticDiskObject>(); layer.LateUpdate();
                    layer.ps.Simulate(0.01f, true, false, false); layer.ps.Pause(true);
                }
                Color32[] before = Capture(camera, "Legacy-layers-" + parentScale + ".png");
                root.SetActive(true);
                Color32[] after = Capture(camera, "Combined-" + parentScale + ".png");
                for (int i = 0; i < before.Length; i++)
                    Require(after[i].r + 2 >= before[i].r, "Volume preserves legacy star light");
                foreach (GameObject legacyRoot in legacyRoots)
                {
                    Require(legacyRoot.GetComponent<GalacticDiskObject>().ps.particleCount == 2048, "Legacy layer preserved");
                    UnityEngine.Object.DestroyImmediate(legacyRoot);
                }
            }
            UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(body.scaledBody);
            UnityEngine.Object.DestroyImmediate(body.gameObject); UnityEngine.Object.DestroyImmediate(camera.gameObject);
            File.WriteAllText("ScaleVerification/ScaleChecks.txt", "PASS - Unity " + Application.unityVersion + "\n" +
                "objectScale=1.5e15, renderScale=1e8; body scales 1 and 1e6; volume tested beyond a 1e6 far plane.\n" +
                "Legacy visibility, identical volume appearance at both parent scales, clouds with zero stars,\n" +
                "and two legacy layers visible alongside the volume. KSP APIs are test substitutes.\n");
            Debug.Log("GALACTICDISK_SCALE_CHECKS_PASSED");
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Scale check failed: " + message);
    }
    private static long Brightness(Color32[] pixels)
    {
        long brightness = 0; foreach (Color32 p in pixels) brightness += p.r + p.g + p.b;
        return brightness;
    }
    private static Color32[] Capture(Camera camera, string name)
    {
        Directory.CreateDirectory("ScaleVerification");
        var target = RenderTexture.GetTemporary(960, 640, 24, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        var texture = new Texture2D(960, 640, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            foreach (var renderer in UnityEngine.Object.FindObjectsOfType<VolumetricDiskRenderer>())
                typeof(VolumetricDiskRenderer).GetMethod("PrepareCamera", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(renderer, new object[] { camera });
            camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0,0,960,640),0,0); texture.Apply();
            Color32[] pixels = texture.GetPixels32();
            File.WriteAllBytes("ScaleVerification/" + name, texture.EncodeToPNG());
            Debug.Log("SCALE_IMAGE: " + name + " brightness=" + Brightness(pixels));
            return pixels;
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
#endif
