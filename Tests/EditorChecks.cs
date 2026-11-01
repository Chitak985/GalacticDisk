#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GalacticDisk;
using GalacticDisk.Rendering;
using UnityEditor;
using UnityEngine;

public static class EditorChecks
{
    private static int checks;
    public static void Run()
    {
        try { Execute(); Debug.Log("GALACTICDISK_EDITOR_PASSED: " + checks); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    private static string Galaxy(string values) { return "GalacticDiskDefinition\n{\nplanet = EditorA\n" + values + "\n}"; }
    private static void Execute()
    {
        KSPUtil.ApplicationRootPath = Path.GetFullPath("TestGame");
        var a = new GameObject("EditorA").AddComponent<CelestialBody>(); a.scaledBody = new GameObject("EditorA scaled");
        var b = new GameObject("EditorB").AddComponent<CelestialBody>(); b.scaledBody = new GameObject("EditorB scaled");
        FlightGlobals.Bodies.Add(a); FlightGlobals.Bodies.Add(b);
        string valid = Galaxy("// preserved comment\nstarCount = 8\ngalaxyRadius = 100\nrotationX = 35\nrotationZ = 50\nrotationY = 17\nrotationPeriod = 100\nspinAroundDiskAxis = true\ncolorArms = 1, 0.5, 0.5\nobjectScale = 1e5");
        Check(ConfigEditor.Read(valid).IsValid && ConfigEditor.Read(valid).config.colorArms.a == 1f, "RGB, scientific notation and comments");
        Check(!ConfigEditor.Read(Galaxy("rotationX = NaN")).IsValid, "Non-finite number rejected");
        Check(!ConfigEditor.Read(Galaxy("spinAroundDiskAxis = maybe")).IsValid, "Invalid boolean rejected");
        Check(!ConfigEditor.Read(Galaxy("starCount = 1.5")).IsValid, "Fractional count rejected");
        Check(!ConfigEditor.Read(Galaxy("colorArms = 1, 2")).IsValid, "Incomplete color rejected");
        Check(!ConfigEditor.Read(valid.TrimEnd('}')).IsValid, "Missing brace rejected");
        Check(!ConfigEditor.Read(valid + "\n" + valid).IsValid, "Multiple nodes rejected");
        Check(!ConfigEditor.Read(Galaxy("starCount = 1\nstarCount = 2")).IsValid, "Duplicate values rejected");
        Check(!ConfigEditor.Read(Galaxy("SUBNODE\n{\na = 1\n}")).IsValid, "Nested nodes rejected");
        Check(!ConfigEditor.Read(Galaxy("minStarSize = 2\nmaxStarSize = 1")).IsValid, "Reversed sizes rejected");
        Check(!ConfigEditor.Read(Galaxy("objectScale = 1e30\nrenderScale = 1e30")).IsValid, "Scale product overflow rejected");
        var unknown = ConfigEditor.Read(Galaxy("unknownThing = 1"));
        Check(unknown.IsValid && unknown.warnings.Exists(message => message.Contains("unknownThing")), "Unknown key reports warning");
        var clamped = ConfigEditor.Read(Galaxy("renderMode = Volumetric\nvolumeSteps = 999"));
        Check(clamped.IsValid && clamped.warnings.Exists(message => message.Contains("volumeSteps") && message.Contains("256")), "Clamping reports input and effective limit");
        var legacy = ConfigEditor.Read(Galaxy("color1 = 0.2, 0.3, 0.4\ncolor2 = 0.8, 0.7, 0.6\nbulgeRadius = 7"));
        Check(legacy.IsValid && legacy.config.colorArms.r == 0.2f && legacy.config.colorInterarm.r == 0.8f && legacy.config.bulgeScaleX == 7f && legacy.warnings.Count >= 3,
            "Deprecated aliases preserve semantics with warnings");
        Check(ConfigEditor.Read("GalacticDiskDefinition {\nplanet = EditorA\n}").IsValid, "Opening brace on header line");

        var toolbar = new GalacticDiskToolbar();
        Invoke(toolbar, "EnsureConfigText");
        Check(!string.IsNullOrEmpty(Get<string>(toolbar, "configText")), "Config is initialized without a show button");
        Invoke(toolbar, "EditConfigText", valid);
        Check(Get<string>(toolbar, "configText") == valid, "Text edits preserve raw text and comments");
        Invoke(toolbar, "EditConfigText", Galaxy("galaxyRadius = 100.123456\ncolorArms = 0.12345678, 0.23456789, 0.3456789, 0.456789"));
        Check(!Get<List<string>>(toolbar, "editorWarnings").Exists(message => message.Contains("mismatch")), "Precise numbers and colors synchronize without rounding mismatches");
        Invoke(toolbar, "SaveToConfig");
        Check(!Get<List<string>>(toolbar, "editorWarnings").Exists(message => message.Contains("mismatch")), "Saving preserves float precision");
        Invoke(toolbar, "EditConfigText", valid);
        var fields = (GalaxyConfig)Invoke(toolbar, "BuildConfigFromFields");
        Check(fields.spinAroundDiskAxis && fields.starCount == 8 && fields.rotationX == 35f && fields.colorArms.a == 1f, "Text edits populate all corresponding fields");
        Set(toolbar, "galaxyRadius", "175"); Invoke(toolbar, "RefreshEditorDiagnostics");
        Check(Get<List<string>>(toolbar, "editorWarnings").Exists(message => message.Contains("galaxyRadius") && message.Contains("175") && message.Contains("100")),
            "Mismatch lists the field name and both values");
        Check(Get<string>(toolbar, "configText") == valid, "Field edits do not overwrite draft text");
        Check((bool)Invoke(toolbar, "SaveToConfig"), "Save copies valid fields to text");
        Check(ConfigEditor.Read(Get<string>(toolbar, "configText")).config.galaxyRadius == 175f &&
            !Get<List<string>>(toolbar, "editorWarnings").Exists(message => message.Contains("mismatch")), "Save clears differences");
        string saved = Get<string>(toolbar, "configText");
        Set(toolbar, "galaxyRadius", "bad");
        Check(!(bool)Invoke(toolbar, "SaveToConfig") && Get<string>(toolbar, "configText") == saved, "Invalid field does not overwrite text");
        Check(Get<List<string>>(toolbar, "editorErrors").Exists(message => message.Contains("galaxyRadius")), "Invalid field error identifies key");
        Set(toolbar, "galaxyRadius", "175");
        Invoke(toolbar, "RefreshEditorDiagnostics");
        Check(Get<List<string>>(toolbar, "editorErrors").Count == 0 && !Get<string>(toolbar, "operationMessage").StartsWith("Error:"), "Correcting a field clears stale errors");
        string incomplete = valid.TrimEnd('}'); Invoke(toolbar, "EditConfigText", incomplete);
        Check(Get<string>(toolbar, "galaxyRadius") == "175" && Get<string>(toolbar, "configText") == incomplete, "Invalid text retains last field values and editable draft");
        Check(Get<List<string>>(toolbar, "editorErrors").Exists(message => message.Contains("brace")), "Syntax errors reach diagnostics");
        Invoke(toolbar, "Apply");
        Check(!GalacticDiskGenerator.disksByPlanet.ContainsKey(a.name) && Get<string>(toolbar, "configText") == incomplete, "Invalid draft cannot be applied or erased");
        Check((bool)Invoke(toolbar, "SaveToConfig"), "Save repairs invalid draft using valid fields");
        Invoke(toolbar, "EditConfigText", valid.Replace("EditorA", "EditorB"));
        Check(((GalaxyConfig)Invoke(toolbar, "BuildConfigFromFields")).planet == b.name, "Text planet updates body selector");
        Invoke(toolbar, "EditConfigText", valid.Replace("EditorA", "MissingBody"));
        Check(Get<List<string>>(toolbar, "editorErrors").Exists(message => message.Contains("planet")) &&
            ((GalaxyConfig)Invoke(toolbar, "BuildConfigFromFields")).planet == b.name, "Unknown body is diagnosed without changing selection");
        string ring = "GalacticDiskRingDefinition\n{\nplanet = EditorA\ninnerRingRadius = 50\nringRadius = 100\nparticleCount = 8\ncolor = 0.2, 0.4, 0.8\nspinAroundDiskAxis = true\n}";
        Invoke(toolbar, "EditConfigText", ring);
        fields = (GalaxyConfig)Invoke(toolbar, "BuildConfigFromFields");
        Check(fields.isRing && fields.innerRingRadius == 50f && fields.spinAroundDiskAxis && fields.colorInterarm.a == 1f, "Ring schema and new flag sync to fields");
        Check((bool)Invoke(toolbar, "SaveToConfig") && Get<string>(toolbar, "configText").Contains("spinAroundDiskAxis = true"), "Ring exports new flag");
        Invoke(toolbar, "EditConfigText", ring.Replace("innerRingRadius = 50", "innerRingRadius = 110"));
        Check(Get<List<string>>(toolbar, "editorErrors").Exists(message => message.Contains("innerRingRadius")), "Invalid ring relationship diagnosed");
        Invoke(toolbar, "EditConfigText", valid);
        var generator = new GameObject("Editor generator").AddComponent<GalacticDiskGenerator>();
        typeof(GalacticDiskGenerator).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, generator);
        Set(toolbar, "objectScale", "1"); Invoke(toolbar, "SaveToConfig"); saved = Get<string>(toolbar, "configText");
        Invoke(toolbar, "Apply");
        Check(GalacticDiskGenerator.GetConfig(a.name).spinAroundDiskAxis && Get<string>(toolbar, "configText") == saved, "Apply preserves config buffer and uses validated flag");
        Invoke(toolbar, "LoadSelectedDisk"); Invoke(toolbar, "RefreshEditorDiagnostics");
        Check(Get<string>(toolbar, "configText").Length > 0 && !Get<List<string>>(toolbar, "editorWarnings").Exists(message => message.Contains("mismatch")), "Load populates fields and persistent text together");
        var disk = GalacticDiskGenerator.disksByPlanet[a.name]; disk.GetComponent<GalacticDiskObject>().ReleaseRenderer(); UnityEngine.Object.DestroyImmediate(disk);
        GalacticDiskGenerator.disksByPlanet.Clear(); generator.OnDestroy(); UnityEngine.Object.DestroyImmediate(generator.gameObject);

        var spinRoot = new GameObject("Spin test"); spinRoot.transform.SetParent(a.scaledBody.transform, false);
        var owner = spinRoot.AddComponent<GalacticDiskObject>(); owner.orbitBody = a;
        foreach (bool ringShape in new[] { false, true })
        foreach (DiskRenderMode mode in new[] { DiskRenderMode.Particles, DiskRenderMode.Volumetric })
        {
            var cfg = new GalaxyConfig { rotationX = 35f, rotationY = 17f, rotationZ = 50f, rotationPeriod = 100f,
                spinAroundDiskAxis = true, isRing = ringShape, renderMode = mode };
            owner.config = cfg;
            Vector3 normal = Quaternion.Euler(35f, 17f, 50f) * Vector3.up;
            foreach (double time in new[] { 0d, 25d, 50d, 100d, 1000025d })
            {
                Planetarium.time = time; a.scaledBody.transform.rotation = Quaternion.Euler((float)(time % 133), 70f, -11f); owner.LateUpdate();
                Check(Vector3.Angle(spinRoot.transform.up, normal) < 0.03f, "Own-axis spin preserves tilted normal across times, shapes and modes");
            }
            Planetarium.time = 25d; cfg.rotationY = 45f; owner.LateUpdate();
            Check(Vector3.Angle(spinRoot.transform.up, Quaternion.AngleAxis(28f, Vector3.up) * normal) < 0.03f, "rotationY remains a world-Y heading");
            cfg.spinAroundDiskAxis = false; owner.LateUpdate();
            Check(Quaternion.Angle(spinRoot.transform.rotation, Quaternion.Euler(35f, 135f, 50f)) < 0.03f, "False preserves legacy Euler spin");
            cfg.alignRotationToOrbit = true; cfg.spinAroundDiskAxis = true; a.orbit.normal = new Vector3(0.3f, 0.4f, 0.8f);
            Vector3 orbitNormal = new Vector3(0.3f, 0.8f, 0.4f).normalized; owner.LateUpdate();
            Check(Vector3.Angle(spinRoot.transform.up, Quaternion.AngleAxis(45f, Vector3.up) * orbitNormal) < 0.03f, "Aligned own-axis spin retains explicit world heading");
        }
        Check(!ConfigEditor.Read(Galaxy("")).config.spinAroundDiskAxis, "Omitted flag preserves backward compatibility");
        UnityEngine.Object.DestroyImmediate(spinRoot); UnityEngine.Object.DestroyImmediate(a.scaledBody); UnityEngine.Object.DestroyImmediate(b.scaledBody);
        UnityEngine.Object.DestroyImmediate(a.gameObject); UnityEngine.Object.DestroyImmediate(b.gameObject);
        Directory.CreateDirectory("EditorVerification");
        File.WriteAllText("EditorVerification/Editor.txt", "PASS - Unity " + Application.unityVersion + "\n" + checks + " assertions.\n" +
            "Own-axis spin and world-Y heading; legacy defaults; galaxy/ring and both render modes;\n" +
            "editable text/field sync, mismatches with values, strict errors and warnings, Save/Load/Apply preservation.\n" +
            "KSP APIs are substitutes; production DLL built separately against installed KSP assemblies.\n");
    }
    private static object Invoke(object target, string name, params object[] args)
    {
        try { return target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args); }
        catch (TargetInvocationException error) { throw error.InnerException; }
    }
    private static void Set(object target, string name, object value) { target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value); }
    private static T Get<T>(object target, string name) { return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target); }
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException("Editor check failed: " + message); }
}
#endif
