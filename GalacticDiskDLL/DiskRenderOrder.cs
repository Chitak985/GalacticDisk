using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GalacticDisk
{
    internal static class DiskRenderOrder
    {
        internal const CameraEvent DrawEvent = CameraEvent.BeforeForwardAlpha;
        private sealed class Entry
        {
            internal MonoBehaviour renderer;
            internal Transform root;
            internal CommandBuffer commands;
            internal double distance;
        }
        private sealed class CameraState
        {
            internal readonly List<Entry> entries = new List<Entry>();
            internal readonly List<Entry> visible = new List<Entry>();
            internal readonly List<Entry> attached = new List<Entry>();
        }
        private static readonly Dictionary<Camera, CameraState> cameras = new Dictionary<Camera, CameraState>();
        private static readonly List<Camera> stale = new List<Camera>();
        private static bool subscribed;

        internal static void Submit(Camera camera, MonoBehaviour renderer, CommandBuffer commands)
        {
            PruneCameras();
            CameraState state;
            if (!cameras.TryGetValue(camera, out state))
            {
                state = new CameraState(); cameras.Add(camera, state);
                if (!subscribed) { Camera.onPreRender += SortCamera; subscribed = true; }
            }
            foreach (Entry entry in state.entries) if (ReferenceEquals(entry.commands, commands)) return;
            state.entries.Add(new Entry { renderer = renderer, root = renderer.transform.parent, commands = commands });
            SortCamera(camera);
        }

        // Called after every renderer's pre-cull preparation, including nested cameras.
        private static void SortCamera(Camera camera)
        {
            CameraState state;
            if (camera == null || !cameras.TryGetValue(camera, out state)) return;
            state.visible.Clear();
            foreach (Entry entry in state.entries)
            {
                if (entry.renderer == null || entry.root == null || !entry.renderer.isActiveAndEnabled ||
                    (camera.cullingMask & (1 << 10)) == 0) continue;
                Vector3 position = entry.root.position, eye = camera.transform.position;
                // Cast before subtraction and squaring: float distances overflow at KSP scales.
                double x = (double)position.x - eye.x, y = (double)position.y - eye.y, z = (double)position.z - eye.z;
                entry.distance = x*x + y*y + z*z; state.visible.Add(entry);
            }
            state.visible.Sort(Compare);
            bool changed = state.visible.Count != state.attached.Count;
            for (int i = 0; !changed && i < state.visible.Count; i++)
                changed = !ReferenceEquals(state.visible[i], state.attached[i]);
            if (!changed) return;
            foreach (Entry entry in state.attached) camera.RemoveCommandBuffer(DrawEvent, entry.commands);
            state.attached.Clear();
            foreach (Entry entry in state.visible)
            {
                camera.AddCommandBuffer(DrawEvent, entry.commands); state.attached.Add(entry);
            }
        }
        private static int Compare(Entry a, Entry b)
        {
            int distance = b.distance.CompareTo(a.distance);
            return distance != 0 ? distance : a.root.GetInstanceID().CompareTo(b.root.GetInstanceID());
        }

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
        private static void PruneCameras()
        {
            stale.Clear();
            foreach (var entry in cameras) if (entry.Key == null) stale.Add(entry.Key);
            foreach (Camera camera in stale) cameras.Remove(camera);
            if (cameras.Count == 0 && subscribed) { Camera.onPreRender -= SortCamera; subscribed = false; }
        }
    }
}
