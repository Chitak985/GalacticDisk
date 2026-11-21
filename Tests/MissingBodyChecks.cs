#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GalacticDisk;
using UnityEditor;
using UnityEngine;

public static class MissingBodyChecks
{
    private static int checks, warnings, allWarnings;
    public static void Run()
    {
        Application.logMessageReceived += Log;
        try
        {
            var generator = new GameObject("Missing-body loader test").AddComponent<GalacticDiskGenerator>();
            var body = new GameObject("AvailableBody").AddComponent<CelestialBody>();
            body.scaledBody = new GameObject("Available scaled body"); FlightGlobals.Bodies.Add(body);
            var unready = new GameObject("UnreadyBody").AddComponent<CelestialBody>(); FlightGlobals.Bodies.Add(unready);
            foreach (bool ring in new[] { false,true })
            foreach (DiskRenderMode mode in new[] { DiskRenderMode.Particles,DiskRenderMode.Volumetric })
            foreach (string flag in new[] { null,"False","True" })
            {
                var c = new GalaxyConfig { isRing=ring,renderMode=mode,starCount=1 };
                var toolbar = new GalacticDiskToolbar(); Invoke(toolbar,"PopulateFieldsFromConfig",c);
                string exported = (string)Invoke(toolbar,"GenerateConfigText",c,"MissingBody");
                string text = exported.Replace("    silenceNoBodyError = false\n",flag == null ? "" : "    silenceNoBodyError = "+flag+"\n");
                var parsed = GalaxyConfig.FromConfigNode(ConfigNode.Parse(text).GetNode(ring ? RingConfig.NodeName : "GalacticDiskDefinition"));
                bool silent = flag == "True";
                Check(parsed.silenceNoBodyError==silent,"Default/explicit flag parsing");
                Invoke(toolbar,"PopulateFieldsFromConfig",parsed);
                var values=(Dictionary<string,string>)Invoke(toolbar,"CaptureFieldValues");
                Check(values["silenceNoBodyError"]==(silent?"true":"false"),"Toolbar field preserves flag");
                string saved=(string)Invoke(toolbar,"GenerateConfigText",parsed,"MissingBody");
                Check(saved.Contains("silenceNoBodyError = "+(silent?"true":"false")),"Saved config preserves flag");
                foreach(string parent in new[] { "MissingBody","UnreadyBody","" })
                {
                    var node=ConfigNode.Parse(saved.Replace("planet = MissingBody","planet = "+parent)).GetNode(ring?RingConfig.NodeName:"GalacticDiskDefinition");
                    GameDatabase.Instance.nodes.Clear(); GameDatabase.Instance.nodes.Add(node); warnings=0;
                    generator.LoadDefinitions();
                    Check(warnings==(silent?0:1),"Missing/unready/unspecified parents are silent only when requested");
                    Check(DiskSceneCache.Count==0,"Skipped definitions allocate no disk objects/resources");
                }
            }
            foreach (string type in new[] { "GalacticDiskDefinition",RingConfig.NodeName })
            {
                var minimal=new ConfigNode(type); minimal.AddValue("planet","MissingBody"); minimal.AddValue("silenceNoBodyError",true);
                minimal.AddValue("renderMode","Invalid mode that must not be parsed for a skipped body");
                GameDatabase.Instance.nodes.Clear(); GameDatabase.Instance.nodes.Add(minimal); allWarnings=0;
                generator.LoadDefinitions(); Check(allWarnings==0,"Silent skip occurs before other parsing diagnostics");
            }
            // A skipped optional definition must not prevent later valid definitions from loading.
            GameDatabase.Instance.nodes.Clear();
            var absent=new ConfigNode("GalacticDiskDefinition"); absent.AddValue("planet","MissingBody"); absent.AddValue("silenceNoBodyError",true);
            var valid=new ConfigNode("GalacticDiskDefinition"); valid.AddValue("planet",body.name); valid.AddValue("starCount",1);
            GameDatabase.Instance.nodes.Add(absent); GameDatabase.Instance.nodes.Add(valid); warnings=0; generator.LoadDefinitions();
            Check(warnings==0 && DiskSceneCache.GetDisks(body.name).Length==1,"Silent skip continues loading later valid definitions");
            DiskSceneCache.Clear(); GalacticDiskGenerator.disksByPlanet.Clear(); GameDatabase.Instance.nodes.Clear(); FlightGlobals.Bodies.Clear();
            UnityEngine.Object.DestroyImmediate(generator.gameObject); UnityEngine.Object.DestroyImmediate(body.scaledBody);
            UnityEngine.Object.DestroyImmediate(body.gameObject); UnityEngine.Object.DestroyImmediate(unready.gameObject);
            Directory.CreateDirectory("MissingBodyVerification");
            File.WriteAllText("MissingBodyVerification/MissingBody.txt","PASS - Unity "+Application.unityVersion+"; "+checks+" checks.\n"+
                "Disk/ring, particle/volume, omitted/False/True flags; missing, unready and unspecified parents.\n"+
                "Log suppression, no allocation for skipped definitions, later valid loading and toolbar/export verified.\n");
            Debug.Log("GALACTICDISK_MISSING_BODY_PASSED: "+checks);
        }
        catch(Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        finally { Application.logMessageReceived -= Log; }
    }
    private static void Log(string condition,string stack,LogType type)
    {
        if(type!=LogType.Warning || !condition.StartsWith("[GalacticDisk]")) return;
        allWarnings++;
        if(condition.Contains("has no scaled body") || condition.Contains("node missing planet")) warnings++;
    }
    private static object Invoke(object target,string name,params object[] args)
    { return target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args); }
    private static void Check(bool value,string message) { checks++; if(!value) throw new Exception("Missing-body check failed: "+message); }
}
#endif
