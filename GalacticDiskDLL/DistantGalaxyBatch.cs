using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GalacticDisk
{
    /// <summary>
    /// Class used for drawing distant disks as singular dots or as a batch of dots.
    /// This greatly improves performance because hundreds of thousands of particles
    /// that are being rendered but too small to be seen become a single dot.
    /// </summary>
    internal sealed class DistantGalaxyBatch
    {
        internal const int Capacity = 16000;
        
        internal readonly CommandBuffer commands = new CommandBuffer
        {
            name = "GalacticDisk distant dot batch"
        };
        
        /// <summary> Mesh used to render the dots </summary>
        private readonly Mesh mesh = new Mesh
        {
            name = "GalacticDisk dynamic distant dots",
            indexFormat = IndexFormat.UInt16
        };
        
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        
        /// <summary> Dot positions </summary>
        private readonly List<Vector3> positions = new List<Vector3>();
        /// <summary> Dot colors, this depends on the overall disk color </summary>
        private readonly List<Color> colors = new List<Color>();
        /// <summary> Dot UVs </summary>
        private readonly List<Vector2> uv = new List<Vector2>();
        /// <summary> Dot triangle indices used for drawing the dots </summary>
        private readonly List<int> indices = new List<int>();
        
        internal int Count
        {
            get
            {
                return positions.Count / 4;
            }
        }
        
        internal DistantGalaxyBatch()
        {
            mesh.MarkDynamic();
        }
        
        /// <summary> Clear resource lists and commands </summary>
        internal void Clear()
        {
            positions.Clear(); colors.Clear(); uv.Clear(); indices.Clear(); commands.Clear();
        }
        /// <summary> Add a new dot for rendering </summary>
        internal void Add(DistantGalaxy.DrawData dot)
        {
            int start = positions.Count;
            for (int corner = 0; corner < 4; corner++)
            {
                float x = corner == 0 || corner == 3 ? -1 : 1;
                float y = corner < 2 ? -1 : 1;
                positions.Add(
                    new Vector3(
                        dot.position.x + x*dot.size.x,
                        dot.position.y + y*dot.size.y,
                        dot.position.z
                    )
                );
                uv.Add(new Vector2(x, y));
                colors.Add(dot.light);
            }
            indices.Add(start); indices.Add(start+1); indices.Add(start+2);
            indices.Add(start); indices.Add(start+2); indices.Add(start+3);
        }
        /// <summary> Uses a <see cref="Camera"/> for drawing the dots. </summary>
        internal void Finish(Camera camera)
        {
            // Clear mesh to prepare for drawing
            mesh.Clear(false);

            // Set mesh resources
            mesh.SetVertices(positions);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(indices, 0, false);

            // Reset mesh bounds
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one*4f);

            // Do something
            properties.SetFloat("_DotProjectionY", camera.projectionMatrix.m11);

            // Draw the mesh containing the disk dots
            commands.DrawMesh(mesh, Matrix4x4.identity, DistantGalaxy.SharedMaterial, 0, 1, properties);
        }
        /// <summary> Clear everything and dispose of resources </summary>
        internal void Dispose()
        {
            commands.Release();
            GalacticDiskObject.Release(mesh);
        }
    }
}
