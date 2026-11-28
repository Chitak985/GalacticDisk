using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GalacticDisk
{
    /// <summary> Controls disk render order to render them correctly. </summary>
    internal static class DiskRenderOrder
    {
        /// <summary> The <see cref="CameraEvent"/> used for rendering order. </summary>
        internal const CameraEvent DrawEvent = CameraEvent.BeforeForwardAlpha;
        /// <summary> A disk entry to use for ordering. </summary>
        private sealed class Entry
        {
            internal MonoBehaviour renderer;
            internal Transform root;
            internal CommandBuffer commands;
            internal double distance;
            internal bool visible = true;
            internal int index, detailDraws;
            internal bool dot;
            internal DistantGalaxy.DrawData dotData;
        }
        internal struct CameraStats
        {
            internal int detailedDisks, dotDisks, dotBatches, detailDraws;
        }
        /// <summary> Camera state to use for each <see cref="Camera"/>. </summary>
        private sealed class CameraState
        {
            internal readonly List<Entry> entries = new List<Entry>();
            internal readonly Dictionary<CommandBuffer, Entry> byCommands = new Dictionary<CommandBuffer, Entry>();
            internal readonly List<Entry> visible = new List<Entry>();
            internal readonly List<CommandBuffer> attached = new List<CommandBuffer>();
            internal readonly List<CommandBuffer> wanted = new List<CommandBuffer>();
            internal readonly List<DistantGalaxyBatch> batches = new List<DistantGalaxyBatch>();
            internal CameraStats stats, reported;
            internal bool hasReport, reportedMap;
            internal float nextReport;
            internal void Dispose(Camera camera)
            {
                if (camera != null) foreach (var buffer in attached) camera.RemoveCommandBuffer(DrawEvent,buffer);
                foreach (var batch in batches) batch.Dispose();
                attached.Clear(); batches.Clear();
            }
        }
        /// <summary> List of <see cref="Camera"/>s and their <see cref="CameraState"/>s. </summary>
        private static readonly Dictionary<Camera, CameraState> cameras = new Dictionary<Camera, CameraState>();
        /// <summary> List of stale <see cref="Camera"/>s to update. </summary>
        private static readonly List<Camera> stale = new List<Camera>();
        private static bool subscribed;

        /// <summary> Submit a <see cref="Camera"/> to update. </summary>
        internal static void Submit(Camera camera, MonoBehaviour renderer, CommandBuffer commands, int detailDraws = 1)
        {
            PruneCameras();
            CameraState state;
            
            if (!cameras.TryGetValue(camera, out state))
            {
                state = new CameraState();
                cameras.Add(camera, state);
                if (!subscribed)
                {
                    Camera.onPreRender += SortCamera;
                    subscribed = true;
                }
            }
            
            Entry existing;
            if (state.byCommands.TryGetValue(commands, out existing))
            { existing.visible = true; existing.dot = false; existing.detailDraws = detailDraws; return; }
            
            Entry added = new Entry
                {
                    renderer = renderer,
                    root = renderer.transform.parent,
                    commands = commands, index = state.entries.Count, detailDraws = detailDraws
                };
            state.entries.Add(added); state.byCommands.Add(commands, added);
            // Defer sorting/attachment until pre-render. Sorting on every startup
            // registration repeatedly detached and reattached the growing disk list.
        }

        internal static void SubmitDot(Camera camera, MonoBehaviour renderer, CommandBuffer commands, DistantGalaxy.DrawData data)
        {
            Submit(camera,renderer,commands,0);
            Entry entry = cameras[camera].byCommands[commands]; entry.dot = true; entry.dotData = data;
        }
        internal static CameraStats Stats(Camera camera)
        {
            CameraState state; return cameras.TryGetValue(camera,out state) ? state.stats : new CameraStats();
        }

        internal static void Hide(Camera camera, CommandBuffer commands)
        {
            CameraState state; Entry entry;
            if (cameras.TryGetValue(camera, out state) && state.byCommands.TryGetValue(commands, out entry)) entry.visible = false;
        }

        /// <summary>
        /// Sort the provided camera.
        /// Called after every renderer's pre-cull preparation and includes nested cameras.
        /// </summary>
        private static void SortCamera(Camera camera)
        {
            CameraState state;
            if (camera == null || !cameras.TryGetValue(camera, out state)) return;
            state.visible.Clear();
            foreach (Entry entry in state.entries)
            {
                if (!entry.visible || entry.renderer == null || entry.root == null ||
                    !entry.renderer.isActiveAndEnabled ||
                    !DiskCamera.RendersDisk(camera,entry.root))
                    continue;
                Vector3 position = entry.root.position, eye = camera.transform.position;
                // Cast before subtraction and squaring because float distances will likely overflow
                double x = (double)position.x - eye.x, y = (double)position.y - eye.y, z = (double)position.z - eye.z;
                entry.distance = x*x + y*y + z*z;
                state.visible.Add(entry);
            }
            state.visible.Sort(Compare);
            state.wanted.Clear(); state.stats = new CameraStats();
            DistantGalaxyBatch pending = null;
            foreach (Entry entry in state.visible)
            {
                if (!entry.dot)
                {
                    FinishBatch(camera,state,ref pending);
                    state.wanted.Add(entry.commands); state.stats.detailedDisks++; state.stats.detailDraws += entry.detailDraws;
                    continue;
                }
                if (pending != null && pending.Count == DistantGalaxyBatch.Capacity) FinishBatch(camera,state,ref pending);
                if (pending == null)
                {
                    if (state.batches.Count <= state.stats.dotBatches) state.batches.Add(new DistantGalaxyBatch());
                    pending = state.batches[state.stats.dotBatches]; pending.Clear();
                }
                pending.Add(entry.dotData); state.stats.dotDisks++;
            }
            FinishBatch(camera,state,ref pending);
            for (int i = state.stats.dotBatches; i < state.batches.Count; i++) state.batches[i].commands.Clear();
            Report(camera,state);
            bool changed = state.wanted.Count != state.attached.Count;
            for (int i = 0; !changed && i < state.wanted.Count; i++)
                changed = !ReferenceEquals(state.wanted[i], state.attached[i]);
            if (!changed) return;
            foreach (CommandBuffer buffer in state.attached)
                camera.RemoveCommandBuffer(DrawEvent, buffer);
            state.attached.Clear();
            foreach (CommandBuffer buffer in state.wanted)
            {
                camera.AddCommandBuffer(DrawEvent, buffer);
                state.attached.Add(buffer);
            }
        }
        private static void FinishBatch(Camera camera, CameraState state, ref DistantGalaxyBatch pending)
        {
            if (pending == null) return;
            pending.Finish(camera); state.wanted.Add(pending.commands); state.stats.dotBatches++; pending = null;
        }
        private static void Report(Camera camera, CameraState state)
        {
            if (!Application.isPlaying) return;
            bool map = MapView.MapIsEnabled || HighLogic.LoadedScene == GameScenes.TRACKSTATION;
            if (state.hasReport && state.reportedMap == map && Time.realtimeSinceStartup < state.nextReport) return;
            state.nextReport = Time.realtimeSinceStartup+5;
            CameraStats s = state.stats, old = state.reported;
            if (!state.hasReport || state.reportedMap != map || s.dotDisks != old.dotDisks || s.detailedDisks != old.detailedDisks || s.dotBatches != old.dotBatches)
                Debug.Log("[GalacticDisk] Camera '"+camera.name+"' ("+(map ? "map" : HighLogic.LoadedScene == GameScenes.MAINMENU ? "main menu" : "flight/space center")+"): "+s.detailedDisks+
                    " detailed disks, "+s.dotDisks+" dots in "+s.dotBatches+" batches; "+s.detailDraws+" detailed draws. Dot-only disks submit no star or volume draws.");
            state.hasReport = true; state.reportedMap = map; state.reported = s;
        }
        /// <summary> Compare to entries by their distance to determine rendering order. </summary>
        private static int Compare(Entry a, Entry b)
        {
            int distance = b.distance.CompareTo(a.distance);
            return distance != 0 ? distance : a.root.GetInstanceID().CompareTo(b.root.GetInstanceID());
        }

        /// <summary> Remove a <see cref="Camera"/> to no longer use it. </summary>
        internal static void Remove(Camera camera, CommandBuffer commands)
        {
            CameraState state;
            if (ReferenceEquals(camera, null) || !cameras.TryGetValue(camera, out state)) return;
            Entry entry;
            if (state.byCommands.TryGetValue(commands,out entry))
            {
                if (camera != null) camera.RemoveCommandBuffer(DrawEvent, commands);
                state.attached.Remove(commands);
                int last = state.entries.Count-1;
                Entry moved = state.entries[last]; state.entries[entry.index] = moved; moved.index = entry.index; state.entries.RemoveAt(last);
                state.byCommands.Remove(commands);
                // Invalidate old batched geometry immediately, without rebuilding on each scene teardown callback.
                foreach (var batch in state.batches) batch.commands.Clear();
            }
            if (state.entries.Count == 0) { state.Dispose(camera); cameras.Remove(camera); }
            PruneCameras();
        }
        /// <summary> Find any stale <see cref="Camera"/>s and remove them. </summary>
        private static void PruneCameras()
        {
            // Clear any other stale cameras
            stale.Clear();

            // Go through each camera entry and get null ones
            foreach (var entry in cameras)
                if (entry.Key == null)
                    stale.Add(entry.Key);

            // Remove all stale cameras from orderer
            foreach (Camera camera in stale)
            { cameras[camera].Dispose(camera); cameras.Remove(camera); }

            // If no cameras and subscribed to ordering, unsubscribe 
            if (cameras.Count == 0 && subscribed)
            {
                Camera.onPreRender -= SortCamera;
                subscribed = false;
            }
        }
    }
}
