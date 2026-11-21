using System;
using UnityEngine;

namespace GalacticDisk
{
    internal static class DiskVisibility
    {
        // Conservative sphere test in camera space. Intentionally omit the far
        // plane: the galaxy shaders support KSP disks beyond that clipping plane.
        internal static bool IsVisible(Camera camera, Transform root, Vector3 extents, float scale)
        {
            Vector3 position = root.position, eye = camera.transform.position;
            double x = (double)position.x - eye.x, y = (double)position.y - eye.y, z = (double)position.z - eye.z;
            Vector3 right, up, back; DiskCamera.Axes(camera,out right,out up,out back);
            double vx = x*right.x + y*right.y + z*right.z;
            double vy = x*up.x + y*up.y + z*up.z;
            double vz = x*back.x + y*back.y + z*back.z;
            double radius = Math.Sqrt((double)extents.x*extents.x + (double)extents.y*extents.y + (double)extents.z*extents.z) * scale;
            if (vz > radius) return false;
            Matrix4x4 p = camera.projectionMatrix;
            return Inside(p.m30+p.m00,p.m31+p.m01,p.m32+p.m02,p.m33+p.m03,vx,vy,vz,radius) &&
                   Inside(p.m30-p.m00,p.m31-p.m01,p.m32-p.m02,p.m33-p.m03,vx,vy,vz,radius) &&
                   Inside(p.m30+p.m10,p.m31+p.m11,p.m32+p.m12,p.m33+p.m13,vx,vy,vz,radius) &&
                   Inside(p.m30-p.m10,p.m31-p.m11,p.m32-p.m12,p.m33-p.m13,vx,vy,vz,radius);
        }
        private static bool Inside(double a,double b,double c,double d,double x,double y,double z,double radius)
        {
            return a*x+b*y+c*z+d >= -radius*Math.Sqrt(a*a+b*b+c*c);
        }
    }
}
