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
        }
        /// <summary> Camera state to use for each <see cref="Camera"/>. </summary>
        private sealed class CameraState
        {
            internal readonly List<Entry> entries = new List<Entry>();
            internal readonly List<Entry> visible = new List<Entry>();
            internal readonly List<Entry> attached = new List<Entry>();
        }
        /// <summary> List of <see cref="Camera"/>s and their <see cref="CameraState"/>s. </summary>
        private static readonly Dictionary<Camera, CameraState> cameras = new Dictionary<Camera, CameraState>();
        /// <summary> List of stale <see cref="Camera"/>s to update. </summary>
        private static readonly List<Camera> stale = new List<Camera>();
        private static bool subscribed;

        /// <summary> Submit a <see cref="Camera"/> to update. </summary>
        internal static void Submit(Camera camera, MonoBehaviour renderer, CommandBuffer commands)
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
            
            foreach (Entry entry in state.entries)
                if (ReferenceEquals(entry.commands, commands))
                    return;
            
            state.entries.Add(
                new Entry
                {
                    renderer = renderer,
                    root = renderer.transform.parent,
                    commands = commands
                }
            );
            SortCamera(camera);
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
                if (entry.renderer == null || entry.root == null ||
                    !entry.renderer.isActiveAndEnabled ||
                    (camera.cullingMask & (1 << 10)) == 0)
                    continue;
                Vector3 position = entry.root.position, eye = camera.transform.position;
                // Cast before subtraction and squaring because float distances will likely overflow
                double x = (double)position.x - eye.x, y = (double)position.y - eye.y, z = (double)position.z - eye.z;
                entry.distance = x*x + y*y + z*z;
                state.visible.Add(entry);
            }
            state.visible.Sort(Compare);
            bool changed = state.visible.Count != state.attached.Count;
            for (int i = 0; !changed && i < state.visible.Count; i++)
                changed = !ReferenceEquals(state.visible[i], state.attached[i]);
            if (!changed) return;
            foreach (Entry entry in state.attached)
                camera.RemoveCommandBuffer(DrawEvent, entry.commands);
            state.attached.Clear();
            foreach (Entry entry in state.visible)
            {
                camera.AddCommandBuffer(DrawEvent, entry.commands);
                state.attached.Add(entry);
            }
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
            for (int i = state.entries.Count - 1; i >= 0; i--)
            {
                Entry entry = state.entries[i];
                if (!ReferenceEquals(entry.commands, commands)) continue;
                if (camera != null) camera.RemoveCommandBuffer(DrawEvent, commands);
                state.attached.Remove(entry); state.entries.RemoveAt(i);
            }
            if (state.entries.Count == 0) cameras.Remove(camera);
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
                cameras.Remove(camera);

            // If no cameras and subscribed to ordering, unsubscribe 
            if (cameras.Count == 0 && subscribed)
            {
                Camera.onPreRender -= SortCamera;
                subscribed = false;
            }
        }
    }
}
