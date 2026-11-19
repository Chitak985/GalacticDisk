#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using GalacticDisk;
using UnityEditor;
using UnityEngine;

public static class DefinitionBenchmarkChecks
{
    public static void Run()
    {
        try
        {
            KSPUtil.ApplicationRootPath = Path.GetFullPath("TestGame");
            string definitions = File.ReadAllText("DefinitionBenchmark.cfg");
            var matches = Regex.Matches(definitions,@"(?ms)^\s*(GalacticDiskDefinition|GalacticDiskRingDefinition)\s*\{[^{}]*\}");
            if(matches.Count == 0) throw new InvalidOperationException("No flat disk definitions found in the benchmark snapshot.");
            var body = new GameObject("Benchmark body").AddComponent<CelestialBody>(); body.scaledBody = new GameObject("Benchmark scaled parent"); FlightGlobals.Bodies.Add(body);
            var root = new GameObject("Benchmark disk") { layer = 10 }; root.transform.SetParent(body.scaledBody.transform,false);
            int analytic = 0, built = GalaxyCloudCache.BuildCount; long stars = 0, peakBytes = 0; double generationMs = 0, cloudMs = 0;
            var elapsed = Stopwatch.StartNew();
            for(int i=0;i<matches.Count;i++)
            {
                string nodeName = matches[i].Groups[1].Value;
                var config = GalaxyConfig.FromConfigNode(ConfigNode.Parse(matches[i].Value).GetNode(nodeName));
                if(GalaxyCloudCache.IsAnalyticBulge(VolumeConfig.SettingsFor(config))) analytic++;
                // The snapshot and source files are read-only. This in-memory name
                // lets the harness isolate rendering from Kopernicus body creation.
                config.planet = body.name;
                var timer = Stopwatch.StartNew(); GalacticDiskGenerator.RegenerateDisk(root,config); timer.Stop(); generationMs += timer.Elapsed.TotalMilliseconds;
                var owner = root.GetComponent<GalacticDiskObject>(); stars += owner.volume.StarCount;
                timer.Restart(); if(!owner.volume.WarmCloudField()) throw new InvalidOperationException("A definition could not generate its field.");
                timer.Stop(); cloudMs += timer.Elapsed.TotalMilliseconds;
                peakBytes = Math.Max(peakBytes,GalaxyCloudCache.TextureBytes); owner.ReleaseRenderer();
                if((i+1)%100 == 0) UnityEngine.Debug.Log("GALACTICDISK_DEFINITION_PROGRESS: "+(i+1)+"/"+matches.Count+" elapsedSeconds="+elapsed.Elapsed.TotalSeconds);
            }
            if(GalaxyCloudCache.ActiveCount != 0 || GalaxyCloudCache.TextureBytes != 0) throw new InvalidOperationException("Definition benchmark retained cloud resources.");
            UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(body.scaledBody); UnityEngine.Object.DestroyImmediate(body.gameObject); FlightGlobals.Bodies.Clear();
            Directory.CreateDirectory("DefinitionVerification");
            string report = "PASS - Unity "+Application.unityVersion+"\n" + matches.Count+" raw definitions; "+stars+" stars; "+analytic+" analytic spheroids.\n" +
                "Cumulative renderer-generation time: "+generationMs.ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+" ms\n" +
                "Additional time to materialize every cloud field: "+cloudMs.ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+" ms\n" +
                "Sequential generation/disposal elapsed: "+elapsed.Elapsed.TotalSeconds.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+" s\n" +
                "Generated field instances: "+(GalaxyCloudCache.BuildCount-built)+"; peak active field bytes: "+peakBytes+".\n" +
                "Definitions were copied read-only into a benchmark snapshot. One renderer was retained at a time\n" +
                "to avoid allocating all unique GPU textures. Timings exclude Kopernicus, ModuleManager and body loading.\n" +
                "KSP API substitutes; this is not an end-to-end KSP load-time measurement.\n";
            File.WriteAllText("DefinitionVerification/Definitions.txt",report); UnityEngine.Debug.Log(report);
            UnityEngine.Debug.Log("GALACTICDISK_DEFINITIONS_PASSED");
        }
        catch(Exception e) { UnityEngine.Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
#endif
