using System;
using System.Collections.Generic;
using UnityEngine;

namespace GalacticDisk
{
    // Retain complete generated disks, not scene-owned body/camera objects.
    internal static class DiskSceneCache
    {
        private sealed class Record
        {
            internal GameObject root;
            internal GalacticDiskObject owner;
            internal GalaxyConfig config;
        }
        private static readonly List<Record> records = new List<Record>();
        private static readonly Dictionary<Transform, Record> byTransform = new Dictionary<Transform, Record>();
        private static GameObject container;
        private static bool subscribed, changingScene, needsBinding, clearing;
        private static GameScenes currentScene;
        private static int healthFrames, bindingFrames;
        // KSP's EventData inspects Delegate.Target.GetType(); static callbacks have no target.
        private sealed class SceneEvents
        {
            internal void Loading(GameScenes scene) { BeginSceneChange(scene); }
            internal void Loaded(GameScenes scene) { CompleteSceneChange(scene); }
        }
        private static readonly SceneEvents sceneEvents = new SceneEvents();
        internal static bool SceneReady { get; private set; }
        internal static int Count { get { return records.Count; } }

        internal static Transform Parent
        {
            get
            {
                if (container == null)
                {
                    container = new GameObject("GalacticDisk session cache") { layer = 10 };
                    if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(container);
                    currentScene = HighLogic.LoadedScene;
                    SceneReady = !changingScene && IsUsable(currentScene);
                    container.SetActive(SceneReady);
                }
                if (!subscribed)
                {
                    GameEvents.onGameSceneLoadRequested.Add(sceneEvents.Loading);
                    GameEvents.onLevelWasLoaded.Add(sceneEvents.Loaded);
                    GameEvents.onLevelWasLoadedGUIReady.Add(sceneEvents.Loaded);
                    subscribed = true;
                }
                return container.transform;
            }
        }
        private static bool IsUsable(GameScenes scene)
        {
            return scene == GameScenes.SPACECENTER || scene == GameScenes.FLIGHT || scene == GameScenes.TRACKSTATION;
        }
        internal static void Register(GameObject root, CelestialBody body)
        {
            var owner = root.GetComponent<GalacticDiskObject>();
            root.transform.SetParent(Parent, true); root.transform.localScale = Vector3.one;
            Record record;
            if (!byTransform.TryGetValue(root.transform, out record))
            {
                record = new Record { root = root }; records.Add(record); byTransform.Add(root.transform, record);
            }
            record.owner = owner; record.config = owner.config;
            owner.BindSceneBody(body);
            owner.LateUpdate();
        }
        internal static bool SyncAnchor(Transform root)
        {
            Record record;
            if (root == null) return true;
            bool managed = byTransform.TryGetValue(root,out record);
            GalacticDiskObject owner = managed ? record.owner : root.GetComponent<GalacticDiskObject>();
            if (owner == null) return !managed;
            if (!owner.SyncScenePosition()) return false;
            // KSP or another mod can rotate/reparent scene transforms after LateUpdate.
            owner.SyncRotationForRendering();
            return true;
        }
        internal static void RequestBinding() { if (!needsBinding) bindingFrames = 0; needsBinding = true; }
        internal static void BeginSceneChange(GameScenes nextScene)
        {
            changingScene = true; SceneReady = false;
            if (container != null) container.SetActive(false);
            foreach (Record record in records) if (record.owner != null) record.owner.ClearSceneBody();
        }
        internal static void CompleteSceneChange(GameScenes scene)
        {
            changingScene = false; currentScene = scene; needsBinding = true; bindingFrames = 0;
            RebindAll(scene);
        }
        internal static void Tick()
        {
            if (records.Count == 0 || changingScene) return;
            if (currentScene != HighLogic.LoadedScene) { CompleteSceneChange(HighLogic.LoadedScene); return; }
            if (!IsUsable(currentScene)) return;
            if (++healthFrames >= 30)
            {
                healthFrames = 0;
                foreach (Record record in records)
                    if (record.root == null || record.owner == null || record.owner.SceneBindingMissing) { needsBinding = true; break; }
            }
            if (needsBinding && (++bindingFrames <= 10 || bindingFrames % 30 == 0)) RebindAll(currentScene);
        }
        private static void RebindAll(GameScenes scene)
        {
            if (!IsUsable(scene))
            {
                SceneReady = false; if (container != null) container.SetActive(false); return;
            }
            Transform parent = Parent;
            var bodies = new Dictionary<string, CelestialBody>(StringComparer.Ordinal);
            if (FlightGlobals.Bodies != null)
                foreach (CelestialBody body in FlightGlobals.Bodies) if (body != null) bodies[body.name] = body;
            needsBinding = false; int rebound = 0;
            foreach (Record record in records)
            {
                CelestialBody body;
                if (!bodies.TryGetValue(record.config.planet, out body) && FlightGlobals.Bodies != null)
                    body = FlightGlobals.GetBodyByName(record.config.planet);
                if (body == null || body.scaledBody == null)
                {
                    if (record.root != null) record.root.SetActive(false);
                    if (record.owner != null) record.owner.ClearSceneBody();
                    needsBinding = true; continue;
                }
                if (record.root == null || record.owner == null)
                {
                    // Recovery is still available if another mod explicitly destroys a disk.
                    if (!ReferenceEquals(record.owner, null)) record.owner.ReleaseRenderer();
                    if (record.root == null) record.root = new GameObject("GalacticDisk") { layer = 10 };
                    record.root.transform.SetParent(parent, false);
                    record.root.transform.position = body.scaledBody.transform.position;
                    try { GalacticDiskGenerator.RegenerateDisk(record.root, record.config); }
                    catch (Exception error) { Debug.LogError("[GalacticDisk] Cached disk recovery failed: " + error); needsBinding = true; continue; }
                    record.owner = record.root.GetComponent<GalacticDiskObject>();
                    byTransform[record.root.transform] = record;
                }
                if (record.owner.SceneBindingMissing || record.owner.orbitBody != body) rebound++;
                record.owner.BindSceneBody(body); record.root.SetActive(true);
                GalacticDiskGenerator.RememberCachedDisk(record.root, record.config);
            }
            byTransform.Clear();
            foreach (Record record in records) if (record.root != null) byTransform[record.root.transform] = record;
            SceneReady = true; container.SetActive(true);
            foreach (Record record in records)
                if (record.owner != null && record.root.activeInHierarchy && record.owner.ps != null &&
                    record.config.starCount > 0 && record.owner.ps.particleCount == 0) record.owner.UpdateDisk();
            if (rebound > 0) Debug.Log("[GalacticDisk] Rebound " + rebound + " cached disks for " + scene + "; generated resources reused.");
        }
        internal static GalacticDiskObject[] GetDisks(string planet)
        {
            var found = new List<GalacticDiskObject>();
            foreach (Record record in records) if (record.config.planet == planet && record.owner != null) found.Add(record.owner);
            return found.ToArray();
        }
        internal static int RemoveBody(string planet)
        {
            int removed = 0;
            for (int i = records.Count - 1; i >= 0; i--)
            {
                Record record = records[i]; if (record.config.planet != planet) continue;
                if (record.root != null) { byTransform.Remove(record.root.transform); record.root.SetActive(false); }
                if (!ReferenceEquals(record.owner, null)) record.owner.ReleaseRenderer();
                GalacticDiskObject.Release(record.root); records.RemoveAt(i); removed++;
            }
            return removed;
        }
        internal static void Clear()
        {
            if (clearing) return;
            clearing = true; SceneReady = false;
            if (container != null) container.SetActive(false);
            foreach (Record record in records) if (!ReferenceEquals(record.owner, null)) record.owner.ReleaseRenderer();
            records.Clear(); byTransform.Clear(); GalacticDiskObject.Release(container); container = null;
            if (subscribed)
            {
                GameEvents.onGameSceneLoadRequested.Remove(sceneEvents.Loading);
                GameEvents.onLevelWasLoaded.Remove(sceneEvents.Loaded); subscribed = false;
                GameEvents.onLevelWasLoadedGUIReady.Remove(sceneEvents.Loaded);
            }
            changingScene = needsBinding = false; healthFrames = bindingFrames = 0;
            clearing = false;
        }
    }
}
