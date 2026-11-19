using System;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace GalacticDisk
{
    // A shared four-vertex proxy; all decisions and draw properties belong to the camera.
    internal sealed class DistantGalaxy : IDisposable
    {
        internal static readonly string[] Fields = { "enableDistantDots", "distantDotStartPixels", "distantDotEndPixels",
            "distantDotSize", "distantDotBrightness", "distantFadeDistance" };
        internal sealed class View
        {
            internal double pixels = -1;
            internal float detail = 1, fade = 1, diameter;
            internal Vector3 position;
        }
        internal struct DrawData
        {
            internal Vector3 position;
            internal Vector2 size;
            internal Color light;
        }
        internal static Material SharedMaterial { get { return material; } }
        private static Mesh quad;
        private static Material material;
        private static int users;
        private readonly GalaxyConfig config;
        private bool acquired;
        private double red, green, blue, alpha, area;
        internal static int Users { get { return users; } }

        internal DistantGalaxy(GalaxyConfig config)
        {
            this.config = config;
            if (!config.enableDistantDots) return;
            if (material == null)
            {
                material = new Material(GalaxyShaderAssets.LoadDistant()) { name = "GalacticDisk shared distant dots" };
                quad = new Mesh { name = "GalacticDisk shared distant quad",
                    vertices = new[] { new Vector3(-1,-1,0), new Vector3(1,-1,0), new Vector3(1,1,0), new Vector3(-1,1,0) },
                    uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up }, triangles = new[] { 0,1,2,0,2,3 } };
                quad.UploadMeshData(true);
            }
            users++; acquired = true;
        }
        internal static void Validate(GalaxyConfig c)
        {
            foreach (string name in Fields)
            {
                if (name == "enableDistantDots") continue;
                float value = (float)typeof(GalaxyConfig).GetField(name).GetValue(c);
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0)
                    throw new ArgumentException(name + " must be finite and nonnegative.");
            }
            if (c.distantDotEndPixels <= 0 || c.distantDotStartPixels <= c.distantDotEndPixels)
                throw new ArgumentException("distantDotStartPixels must exceed distantDotEndPixels, which must be positive.");
            if (c.distantDotSize < 2 || c.distantDotSize > 16)
                throw new ArgumentException("distantDotSize must be between 2 and 16 pixels.");
        }
        internal static string Export(GalaxyConfig c)
        {
            var text = new StringBuilder();
            foreach (string name in Fields) text.Append("    ").Append(name).Append(" = ")
                .Append(VolumeConfig.Format(typeof(GalaxyConfig).GetField(name).GetValue(c))).Append('\n');
            return text.ToString();
        }
        // Subtract and divide in double precision before converting to Unity vectors.
        internal static Vector3 CameraOffset(Camera camera, Transform root, float scale)
        {
            Vector3 p = root.position, eye = camera.transform.position;
            return new Vector3((float)(((double)p.x-eye.x)/scale), (float)(((double)p.y-eye.y)/scale), (float)(((double)p.z-eye.z)/scale));
        }
        internal void AddStar(Color color, float size)
        {
            if (!acquired) return;
            double weight = (double)size * size;
            area += weight; alpha += weight * color.a;
            red += weight * color.r * color.a; green += weight * color.g * color.a; blue += weight * color.b * color.a;
        }
        private Color Appearance()
        {
            Color light = area > 0 ? new Color((float)(red/area), (float)(green/area), (float)(blue/area), (float)(alpha/area)) : Color.clear;
            if (config.renderMode == DiskRenderMode.Volumetric)
            {
                light *= config.volume.starBrightness;
                if (config.volume.enableVolume)
                {
                    float bulge = Mathf.Clamp01(config.bulgeFraction);
                    float arms = config.armCount > 0 ? 1-Mathf.Clamp01(config.interarmFraction) : 0;
                    Color disk = Premultiply(config.colorBulge)*bulge +
                        (Premultiply(config.colorArms)*arms + Premultiply(config.colorInterarm)*(1-arms))*(1-bulge);
                    float emission = 1-Mathf.Exp(-config.volume.volumeIntensity*0.1f);
                    light = light*0.25f + disk*emission;
                    if (config.volume.enableStarFormation && config.volume.starFormingRegionCount > 0)
                        light += Premultiply(config.volume.starFormingRegionColor)*config.volume.starFormingRegionIntensity*0.03f +
                            Premultiply(config.volume.youngStarColor)*config.volume.youngStarGlowIntensity*0.03f;
                    if (config.volume.enableDustClouds && config.volume.dustCloudCount > 0)
                        light += Premultiply(config.volume.dustColor)*config.volume.dustScattering*0.03f;
                }
                light *= config.volume.volumeExposure;
            }
            return light;
        }
        private static Color Premultiply(Color c) { return new Color(c.r*c.a,c.g*c.a,c.b*c.a,c.a); }

        internal void Evaluate(Camera camera, Transform root, Vector3 extents, View view)
        {
            view.detail = 1; view.fade = 1;
            if (!acquired) return;
            Vector3 p = root.position, eye = camera.transform.position;
            double x = ((double)p.x-eye.x)/config.objectScale, y = ((double)p.y-eye.y)/config.objectScale, z = ((double)p.z-eye.z)/config.objectScale;
            double distance = Math.Sqrt(x*x+y*y+z*z);
            double radius = Math.Sqrt((double)extents.x*extents.x+(double)extents.y*extents.y+(double)extents.z*extents.z);
            if (distance <= radius || radius <= 0) { view.pixels = -1; return; }
            Vector3 right = camera.transform.right, up = camera.transform.up, forward = camera.transform.forward;
            double vx = x*right.x+y*right.y+z*right.z, vy = x*up.x+y*up.y+z*up.z, vz = -(x*forward.x+y*forward.y+z*forward.z);
            if (-vz <= radius) { view.pixels = -1; return; }
            Matrix4x4 m = camera.projectionMatrix;
            double w = m.m30*vx+m.m31*vy+m.m32*vz+m.m33/(double)config.objectScale;
            double cx = m.m00*vx+m.m01*vy+m.m02*vz+m.m03/(double)config.objectScale;
            double cy = m.m10*vx+m.m11*vy+m.m12*vz+m.m13/(double)config.objectScale;
            double denominator = Math.Abs(w)-radius*Math.Sqrt((double)m.m30*m.m30+(double)m.m31*m.m31+(double)m.m32*m.m32);
            if (denominator <= 0) { view.pixels = -1; return; }
            double pixels = radius*Math.Max(Math.Abs(m.m00)*camera.pixelWidth,Math.Abs(m.m11)*camera.pixelHeight)/denominator;
            // A tiny pixel-space dead band prevents threshold chatter without time lag during zoom.
            if (view.pixels < 0 || Math.Abs(pixels-view.pixels) > 0.02) view.pixels = pixels;
            float t = Mathf.Clamp01((float)((view.pixels-config.distantDotEndPixels)/(config.distantDotStartPixels-config.distantDotEndPixels)));
            view.detail = t*t*(3-2*t);
            view.diameter = (float)Math.Max(config.distantDotSize,Math.Min(view.pixels,config.distantDotStartPixels));
            view.fade = (float)Math.Min(1,pixels*pixels/(config.distantDotSize*config.distantDotSize));
            if (config.distantFadeDistance > 0)
            {
                double ratio = distance/(radius*config.distantFadeDistance);
                view.fade *= (float)(1/(1+ratio*ratio));
            }
            view.position = new Vector3((float)(cx/w),(float)(cy/w),(float)Math.Min(-vz*config.objectScale,camera.farClipPlane*0.99999));
        }
        internal void Draw(CommandBuffer commands, MaterialPropertyBlock properties, Camera camera, View view, float opacity)
        {
            DrawData data;
            if (!TryGetDraw(camera,view,opacity,out data)) return;
            properties.SetVector("_DotPosition",new Vector4(data.position.x,data.position.y,data.position.z,camera.projectionMatrix.m11));
            properties.SetVector("_DotSize",data.size);
            properties.SetColor("_DotColor",data.light);
            properties.SetFloat("_DotOpacity",1f);
            commands.DrawMesh(quad,Matrix4x4.identity,material,0,0,properties);
        }
        internal bool TryGetDraw(Camera camera, View view, float opacity, out DrawData data)
        {
            data = new DrawData();
            if (!acquired || view.detail >= 1 || view.fade <= 0 || opacity <= 0) return false;
            Color light = Appearance()*(config.distantDotBrightness*opacity*(1-view.detail)*view.fade);
            if (light.maxColorComponent <= 0) return false;
            data.position = view.position;
            data.size = new Vector2(view.diameter/Math.Max(1,camera.pixelWidth),view.diameter/Math.Max(1,camera.pixelHeight));
            data.light = light;
            return true;
        }
        public void Dispose()
        {
            if (!acquired) return;
            acquired = false;
            if (--users != 0) return;
            GalacticDiskObject.Release(quad); GalacticDiskObject.Release(material); quad = null; material = null;
        }
    }
}
