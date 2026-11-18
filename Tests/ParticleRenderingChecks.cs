#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using GalacticDisk;
using UnityEditor;
using UnityEngine;

public static class ParticleRenderingChecks
{
    public static void Run()
    {
        try
        {
            KSPUtil.ApplicationRootPath = Path.GetFullPath("TestGame");
            var body = new GameObject("Particle body").AddComponent<CelestialBody>();
            body.scaledBody = new GameObject("Particle scaled parent"); FlightGlobals.Bodies.Add(body);
            var root = new GameObject("Particle stability disk") { layer = 10 };
            root.transform.SetParent(body.scaledBody.transform, false);
            var config = new GalaxyConfig { planet = body.name, starCount = 12000, minStarSize = 0.3f, maxStarSize = 1.8f,
                objectScale = 1f, renderScale = 1e8f };
            UnityEngine.Random.InitState(7391); GalacticDiskGenerator.RegenerateDisk(root, config);
            var owner = root.GetComponent<GalacticDiskObject>(); owner.LateUpdate();
            Require(owner.stableParticles != null && owner.stableParticles.IsReady && owner.ps.particleCount == 12000, "Particle mode retains original system and stable drawing");
            var camera = new GameObject("Particle camera").AddComponent<Camera>();
            camera.cullingMask = 1 << 10; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.fieldOfView = 48f; camera.farClipPlane = 1000f;
            Vector3 unit = new Vector3(-70f, 140f, -190f);
            camera.transform.position = unit; camera.transform.rotation = Quaternion.LookRotation(-unit.normalized);
            SetNative(owner, true);
            Color32[] reference = Capture(camera, "Native-reference.png", owner);
            SetNative(owner, false);
            Color32[] stable = Capture(camera, "Stable-reference.png", owner);
            long errorSum = 0;
            for (int i = 0; i < reference.Length; i++) errorSum += Math.Abs(reference[i].r - stable[i].r) + Math.Abs(reference[i].g - stable[i].g) + Math.Abs(reference[i].b - stable[i].b);
            long referenceLight = Brightness(reference);
            Debug.Log("PARTICLE_REFERENCE: native=" + referenceLight + " stable=" + Brightness(stable) + " absoluteDifference=" + errorSum);
            Require(referenceLight > 100000 && errorSum < referenceLight * 0.12, "Stable drawing preserves the native particle profile and brightness");
            config.objectScale = 1.5e15f;
            body.scaledBody.transform.localScale = Vector3.one * 1e6f;
            foreach (float distance in new[] { 0f, 2.57e20f / 6000f, 2.08e24f / 6000f })
            {
                Vector3 origin = new Vector3(distance, -distance * 0.37f, distance * 0.13f);
                body.scaledBody.transform.position = origin;
                long min = long.MaxValue, max = 0;
                for (int frame = 0; frame < 12; frame++)
                {
                    body.scaledBody.transform.rotation = Quaternion.Euler(17f, 31f + frame * 0.3f, 11f); owner.LateUpdate();
                    camera.transform.position = origin + unit * config.objectScale;
                    camera.transform.rotation = Quaternion.LookRotation(((origin - camera.transform.position) / config.objectScale).normalized);
                    camera.farClipPlane = 1e6f;
                    long light = Brightness(Capture(camera, "Stable-" + distance + "-" + frame + ".png", owner));
                    min = Math.Min(min, light); max = Math.Max(max, light);
                }
                Debug.Log("PARTICLE_FRAMES: distance=" + distance + " min=" + min + " max=" + max);
                Require(min > 100000 && min > max * 0.95, "Particle frames stay visible and stable beyond far plane with rotating distant parent");
            }
            // Reproduce the origin-camera case where a native astronomical transform is ill-conditioned.
            body.scaledBody.transform.position = new Vector3(1f, -0.37f, 0.13f) * (2.57e20f / 6000f);
            camera.transform.position = Vector3.zero;
            camera.transform.rotation = Quaternion.LookRotation(new Vector3(1f, -0.37f, 0.13f).normalized);
            long originMinimum = long.MaxValue, originMaximum = 0, nativeMinimum = long.MaxValue, nativeMaximum = 0;
            for (int frame = 0; frame < 12; frame++)
            {
                body.scaledBody.transform.rotation = Quaternion.Euler(17f, 31f + frame * 0.3f, 11f); owner.LateUpdate();
                camera.farClipPlane = 1e35f;
                SetNative(owner, true);
                long nativeLight = Brightness(Capture(camera, "Native-origin-" + frame + ".png", owner));
                nativeMinimum = Math.Min(nativeMinimum, nativeLight); nativeMaximum = Math.Max(nativeMaximum, nativeLight);
                SetNative(owner, false); camera.farClipPlane = 1e6f;
                if (frame % 3 == 0) owner.ps.Clear(); // Draws remain valid during KSP particle clears.
                long light = Brightness(Capture(camera, "Stable-origin-" + frame + ".png", owner));
                originMinimum = Math.Min(originMinimum, light); originMaximum = Math.Max(originMaximum, light);
            }
            Debug.Log("PARTICLE_ORIGIN: nativeMin=" + nativeMinimum + " nativeMax=" + nativeMaximum + " stableMin=" + originMinimum + " stableMax=" + originMaximum);
            Require(originMinimum > 100000 && originMinimum > originMaximum * 0.95, "Floating-origin drawing stays stable even during native clears");
            for (int i = 0; i < 30; i++) owner.Update();
            Require(owner.ps.particleCount == 12000, "Native particle clear recovery remains compatible");
            config.objectScale = 1f; config.renderScale = 1f; body.scaledBody.transform.position = Vector3.zero;
            body.scaledBody.transform.localScale = Vector3.one; body.scaledBody.transform.rotation = Quaternion.identity;
            GalacticDiskGenerator.RegenerateDisk(root, config);
            owner.LateUpdate(); camera.transform.position = Vector3.back * 10f; camera.transform.rotation = Quaternion.identity;
            camera.farClipPlane = 1000f;
            var giant = new ParticleSystem.Particle { position = Vector3.zero, startSize = 100f / config.renderScale,
                startColor = Color.white, remainingLifetime = float.MaxValue, startLifetime = float.MaxValue };
            owner.ps.SetParticles(new[] { giant }, 1); owner.psR.maxParticleSize = 0.25f;
            owner.material.mainTexture = Texture2D.whiteTexture;
            owner.stableParticles.DisposeResources();
            owner.stableParticles.Initialize(config, new[] { giant }, Texture2D.whiteTexture, Color.white, owner.psR);
            SetNative(owner, true); var nativeCap = Capture(camera, "Native-viewport-limit.png", owner);
            SetNative(owner, false); var stableCap = Capture(camera, "Stable-viewport-limit.png", owner);
            Debug.Log("PARTICLE_VIEWPORT: nativePixels=" + VisiblePixels(nativeCap) + " stablePixels=" + VisiblePixels(stableCap));
            Require(Math.Abs(VisiblePixels(nativeCap) - VisiblePixels(stableCap)) < VisiblePixels(nativeCap) * 0.03, "Native maximum viewport size is preserved");
            owner.psR.minParticleSize = 0.1f; giant.startSize = 0.01f / config.renderScale;
            owner.ps.SetParticles(new[] { giant }, 1); owner.stableParticles.DisposeResources();
            owner.stableParticles.Initialize(config, new[] { giant }, Texture2D.whiteTexture, Color.white, owner.psR);
            SetNative(owner, true); nativeCap = Capture(camera, "Native-minimum-size.png", owner);
            SetNative(owner, false); stableCap = Capture(camera, "Stable-minimum-size.png", owner);
            Debug.Log("PARTICLE_MIN_VIEWPORT: nativePixels=" + VisiblePixels(nativeCap) + " stablePixels=" + VisiblePixels(stableCap));
            Require(Math.Abs(VisiblePixels(nativeCap) - VisiblePixels(stableCap)) < VisiblePixels(nativeCap) * 0.03, "Native minimum viewport size is preserved");
            owner.stableParticles.enabled = false; Invoke(owner.stableParticles, "OnDisable");
            Require(camera.GetCommandBuffers(DiskRenderOrder.DrawEvent).Length == 0, "Disabling removes particle camera commands");
            owner.ReleaseRenderer(); UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(camera.gameObject); UnityEngine.Object.DestroyImmediate(body.scaledBody); UnityEngine.Object.DestroyImmediate(body.gameObject);
            Directory.CreateDirectory("ParticleVerification");
            File.WriteAllText("ParticleVerification/Particles.txt", "PASS - Unity " + Application.unityVersion + "\n" +
                "Native/stable appearance comparison; 48 stable particle frames at astronomical scales, translated and rotating parents,\n" +
                "short far plane and floating origin; native clears and cleanup. Original particle data retained.\n" +
                "Native origin brightness range: " + nativeMinimum + ".." + nativeMaximum + "; stable: " + originMinimum + ".." + originMaximum + ".\n" +
                "KSP APIs are test substitutes; this does not reproduce a live third-party KSP installation.\n");
            Debug.Log("GALACTICDISK_PARTICLES_PASSED");
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    private static void SetNative(GalacticDiskObject owner, bool native)
    {
        owner.stableParticles.enabled = !native;
        if (native) Invoke(owner.stableParticles, "OnDisable");
        owner.psR.enabled = native;
        if (native) { owner.ps.Simulate(0.01f, true, false, false); owner.ps.Pause(true); }
    }
    public static Color32[] Capture(Camera camera, string name, GalacticDiskObject owner)
    {
        Directory.CreateDirectory("ParticleVerification");
        var target = RenderTexture.GetTemporary(480, 320, 24, RenderTextureFormat.ARGB32);
        var texture = new Texture2D(480, 320, TextureFormat.RGB24, false); var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target;
            if (!Application.isPlaying && owner.stableParticles != null && owner.stableParticles.enabled) Invoke(owner.stableParticles, "PrepareCamera", camera);
            camera.Render(); RenderTexture.active = target; texture.ReadPixels(new Rect(0,0,480,320),0,0); texture.Apply();
            Color32[] pixels = texture.GetPixels32(); File.WriteAllBytes("ParticleVerification/" + name, texture.EncodeToPNG()); return pixels;
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(texture);
        }
    }
    public static long Brightness(Color32[] pixels) { long total = 0; foreach (Color32 pixel in pixels) total += pixel.r + pixel.g + pixel.b; return total; }
    private static int VisiblePixels(Color32[] pixels) { int count = 0; foreach (Color32 p in pixels) if (p.r + p.g + p.b > 0) count++; return count; }
    private static object Invoke(object target, string name, params object[] args)
    {
        try { return target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args); }
        catch (TargetInvocationException error) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); return null; }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException("Particle check failed: " + message); }
}
#endif
