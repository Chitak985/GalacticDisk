#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using GalacticDisk;
using GalacticDisk.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class CameraFrameChecks
{
    public static void Run()
    {
        try
        {
            KSPUtil.ApplicationRootPath = Path.GetFullPath("TestGame");
            var body = new GameObject("Distant body").AddComponent<CelestialBody>();
            body.scaledBody = new GameObject("Translated scaled body");
            body.scaledBody.transform.localScale = Vector3.one * 1e6f;
            body.scaledBody.transform.rotation = Quaternion.Euler(17f, 31f, 11f);
            FlightGlobals.Bodies.Add(body);
            var root = new GameObject("Distant galaxy") { layer = 10 };
            root.transform.SetParent(body.scaledBody.transform, false);
            var config = new GalaxyConfig { planet = body.name, objectScale = 1.5e15f, renderScale = 1e8f };
            VolumeConfig.ApplyMilkyWayPreset(config);
            config.starCount = 0; config.volume.cloudResolution = 64;
            config.volume.dustCloudCount = 30; config.volume.starFormingRegionCount = 16;
            GalacticDiskGenerator.RegenerateDisk(root, config);
            var owner = root.GetComponent<GalacticDiskObject>();
            var camera = new GameObject("Main scaled camera").AddComponent<Camera>();
            camera.cullingMask = 1 << 10; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black; camera.fieldOfView = 48f; camera.farClipPlane = 1e6f;
            var otherCamera = new GameObject("Other scaled camera").AddComponent<Camera>();
            otherCamera.CopyFrom(camera); otherCamera.enabled = false;
            otherCamera.transform.position = new Vector3(17f, -13f, 19f);
            Directory.CreateDirectory("FrameVerification");
            foreach (float distance in new[] { 0f, 2.57e20f / 6000f, 2.08e24f / 6000f })
            {
                Vector3 origin = new Vector3(distance, -distance * 0.37f, distance * 0.13f);
                body.scaledBody.transform.position = origin;
                long minimum = long.MaxValue, maximum = 0;
                for (int frame = 0; frame < 12; frame++)
                {
                    // Simulate moving body transforms and multiple cameras every frame.
                    body.scaledBody.transform.rotation = Quaternion.Euler(17f, 31f + frame * 0.3f, 11f);
                    owner.LateUpdate();
                    camera.transform.position = origin + new Vector3(-70f + frame * 0.001f, 140f, -190f) * config.objectScale;
                    Vector3 direction = (origin - camera.transform.position) / config.objectScale;
                    camera.transform.rotation = Quaternion.LookRotation(direction.normalized);
                    Prepare(owner.volume, otherCamera);
                    long brightness = Capture(camera, distance + "-" + frame + ".png", owner.volume);
                    minimum = Math.Min(minimum, brightness); maximum = Math.Max(maximum, brightness);
                }
                Debug.Log("FRAME_SERIES: origin=" + distance + " min=" + minimum + " max=" + maximum);
                if (minimum < 200000 || minimum < maximum * 0.8)
                    throw new InvalidOperationException("Camera frame check failed: missing/flickering volume at origin " + distance);
                if (distance == 0f) continue;
                // KSP also keeps an observing camera near the floating origin while
                // the body's scaled transform remains far away. This is different
                // from translating both camera and body together.
                config.objectScale = Mathf.Max(1.5e15f, distance / 246f);
                minimum = long.MaxValue; maximum = 0;
                for (int frame = 0; frame < 12; frame++)
                {
                    body.scaledBody.transform.rotation = Quaternion.Euler(17f, 31f + frame * 0.3f, 11f);
                    owner.LateUpdate();
                    camera.transform.position = new Vector3(frame * 0.01f, 0f, 0f);
                    camera.transform.rotation = Quaternion.LookRotation((origin / config.objectScale).normalized);
                    Prepare(owner.volume, otherCamera);
                    long brightness = Capture(camera, "OriginCamera-" + distance + "-" + frame + ".png", owner.volume);
                    minimum = Math.Min(minimum, brightness); maximum = Math.Max(maximum, brightness);
                }
                Debug.Log("ORIGIN_FRAME_SERIES: origin=" + distance + " min=" + minimum + " max=" + maximum);
                if (minimum < 200000 || minimum < maximum * 0.8)
                    throw new InvalidOperationException("Origin camera check failed: missing/flickering volume at body distance " + distance);
                config.objectScale = 1.5e15f;
            }
            owner.volume.enabled = false;
            // Edit-mode checks invoke lifecycle callbacks explicitly, just as they
            // invoke pre-cull. PlayModeChecks verifies automatic lifecycle dispatch.
            typeof(VolumetricDiskRenderer).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(owner.volume, null);
            if (camera.GetCommandBuffers(CameraEvent.BeforeForwardAlpha).Length != 0 ||
                otherCamera.GetCommandBuffers(CameraEvent.AfterForwardAlpha).Length != 0)
                throw new InvalidOperationException("Disabled renderer retained camera commands");
            if (Capture(camera, "Disabled.png", owner.volume) != 0)
                throw new InvalidOperationException("Disabled renderer still draws");
            owner.volume.enabled = true;
            camera.transform.position = body.scaledBody.transform.position + new Vector3(-70f, 140f, -190f) * config.objectScale;
            camera.transform.rotation = Quaternion.LookRotation(new Vector3(70f, -140f, 190f).normalized);
            if (Capture(camera, "Reenabled.png", owner.volume) < 200000)
                throw new InvalidOperationException("Re-enabled renderer failed to draw");
            camera.cullingMask = 0;
            if (Capture(camera, "LayerExcluded.png", owner.volume) != 0)
                throw new InvalidOperationException("Layer-excluded renderer still draws");
            camera.cullingMask = 1 << 10;
            if (Capture(camera, "LayerRestored.png", owner.volume) < 200000)
                throw new InvalidOperationException("Restored layer failed to draw");
            owner.volume.DisposeResources();
            UnityEngine.Object.DestroyImmediate(root);
            if (camera.GetCommandBuffers(CameraEvent.BeforeForwardAlpha).Length != 0 ||
                camera.GetCommandBuffers(CameraEvent.AfterForwardAlpha).Length != 0)
                throw new InvalidOperationException("Destroyed renderer retained camera commands");
            UnityEngine.Object.DestroyImmediate(body.scaledBody);
            UnityEngine.Object.DestroyImmediate(body.gameObject); UnityEngine.Object.DestroyImmediate(camera.gameObject);
            UnityEngine.Object.DestroyImmediate(otherCamera.gameObject);
            File.WriteAllText("FrameVerification/CameraFrames.txt", "PASS - Unity " + Application.unityVersion + "\n" +
                "60 rendered camera frames; rotating scaled parent; alternating cameras; zero mesh stars.\n" +
                "Body translations at 0, 2.57e20/6000 and 2.08e24/6000; objectScale=1.5e15, renderScale=1e8.\n" +
                "Additional origin-camera views at both distances; the most distant origin-camera case uses\n" +
                "objectScale=distance/246 to keep the volume resolvable in the small test image.\n" +
                "Disabled/destroyed renderer cleanup and camera layer-mask changes verified.\n" +
                "KSP APIs are substitutes; no live KSP save loaded.\n");
            Debug.Log("GALACTICDISK_CAMERA_FRAMES_PASSED");
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    private static void Prepare(VolumetricDiskRenderer renderer, Camera camera)
    {
        typeof(VolumetricDiskRenderer).GetMethod("PrepareCamera", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(renderer, new object[] { camera });
    }
    public static long Capture(Camera camera, string filename, VolumetricDiskRenderer renderer)
    {
        var target = RenderTexture.GetTemporary(480, 320, 24, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        var texture = new Texture2D(480, 320, TextureFormat.RGB24, false);
        try
        {
            Directory.CreateDirectory("FrameVerification");
            camera.targetTexture = target;
            if (!Application.isPlaying) Prepare(renderer, camera);
            camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 480, 320), 0, 0); texture.Apply();
            long brightness = 0; foreach (Color32 pixel in texture.GetPixels32()) brightness += pixel.r + pixel.g + pixel.b;
            File.WriteAllBytes("FrameVerification/" + filename, texture.EncodeToPNG());
            return brightness;
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
#endif
