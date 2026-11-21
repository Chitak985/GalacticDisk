using UnityEngine;

namespace GalacticDisk
{
    internal static class DiskCamera
    {
        // Cubemap faces and custom view matrices need not match the Transform rotation.
        internal static Matrix4x4 Rotation(Camera camera)
        {
            Matrix4x4 view = camera.worldToCameraMatrix;
            view.m03 = view.m13 = view.m23 = 0; view.m30 = view.m31 = view.m32 = 0; view.m33 = 1;
            return view;
        }
        internal static void Axes(Camera camera, out Vector3 right, out Vector3 up, out Vector3 back)
        {
            Matrix4x4 view = camera.worldToCameraMatrix;
            right = new Vector3(view.m00,view.m01,view.m02);
            up = new Vector3(view.m10,view.m11,view.m12);
            back = new Vector3(view.m20,view.m21,view.m22);
        }
        internal static Vector3 Forward(Camera camera)
        {
            Matrix4x4 view = camera.worldToCameraMatrix; return new Vector3(-view.m20,-view.m21,-view.m22);
        }
    }
}
