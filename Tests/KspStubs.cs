// Test-only KSP API substitutes. The production DLL is built against the actual KSP assemblies.
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public class KSPAddon : Attribute
{
    public enum Startup { SpaceCentre }
    public KSPAddon(Startup startup, bool once) { }
}
public enum GameScenes { MAINMENU, SPACECENTER, FLIGHT, TRACKSTATION }
public static class HighLogic { public static GameScenes LoadedScene = GameScenes.SPACECENTER; }
public static class KSPUtil { public static string ApplicationRootPath; }
public static class Planetarium { public static double time; public static double GetUniversalTime() { return time; } }
public class Orbit { public Vector3 normal = Vector3.forward; public Vector3 GetOrbitNormal() { return normal; } }
public class CelestialBody : MonoBehaviour { public GameObject scaledBody; public Orbit orbit = new Orbit(); }
public static class FlightGlobals
{
    public static List<CelestialBody> Bodies = new List<CelestialBody>();
    public static CelestialBody GetBodyByName(string name) { return Bodies.Find(body => body.name == name); }
}
public class ConfigNode
{
    public string name;
    private readonly Dictionary<string, string> values = new Dictionary<string, string>();
    private readonly List<ConfigNode> nodes = new List<ConfigNode>();
    public ConfigNode(string name = "") { this.name = name; }
    public void AddValue(string key, object value) { values[key] = Convert.ToString(value, CultureInfo.InvariantCulture); }
    public bool HasValue(string key) { return values.ContainsKey(key); }
    public string GetValue(string key) { string value; return values.TryGetValue(key, out value) ? value : null; }
    public ConfigNode GetNode(string key) { return nodes.Find(node => node.name == key); }
    public static ConfigNode Parse(string text)
    {
        var root = new ConfigNode();
        var stack = new Stack<ConfigNode>(); stack.Push(root);
        string pending = "";
        foreach (string original in text.Split('\n'))
        {
            string line = original.Split(new[] { "//" }, StringSplitOptions.None)[0].Trim();
            if (line.Length == 0) continue;
            if (line == "{") { var child = new ConfigNode(pending); stack.Peek().nodes.Add(child); stack.Push(child); }
            else if (line == "}") stack.Pop();
            else if (line.Contains("=")) { int i = line.IndexOf('='); stack.Peek().AddValue(line.Substring(0,i).Trim(), line.Substring(i+1).Trim()); }
            else pending = line;
        }
        return root;
    }
}
public class GameDatabase
{
    public static GameDatabase Instance = new GameDatabase();
    public readonly List<ConfigNode> nodes = new List<ConfigNode>();
    public ConfigNode[] GetConfigNodes(string name) { return nodes.FindAll(node => node.name == name).ToArray(); }
    public Texture2D GetTexture(string path, bool normal) { return null; }
}
public class TestEvent { public void Add(Action callback) { } public void Remove(Action callback) { } }
public static class GameEvents
{
    public static TestEvent onGUIApplicationLauncherReady = new TestEvent();
    public static TestEvent onGUIApplicationLauncherDestroyed = new TestEvent();
}
namespace KSP.UI.Screens
{
    public class ApplicationLauncherButton { public void SetTrue(bool call) { } public void SetFalse(bool call) { } }
    public class ApplicationLauncher
    {
        public enum AppScenes { ALWAYS }
        public static ApplicationLauncher Instance = new ApplicationLauncher();
        public ApplicationLauncherButton AddModApplication(Action onTrue, Action onFalse, Action onHover, Action onHoverOut,
            Action onEnable, Action onDisable, AppScenes visibleInScenes, Texture texture) { return new ApplicationLauncherButton(); }
        public void RemoveModApplication(ApplicationLauncherButton button) { }
    }
}
