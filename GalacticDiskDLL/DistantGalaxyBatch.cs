using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GalacticDisk
{
    // Consecutive dots in distance order share one draw; detailed disks split batches.
    internal sealed class DistantGalaxyBatch
    {
        internal const int Capacity = 16000;
        internal readonly CommandBuffer commands = new CommandBuffer { name = "GalacticDisk distant dot batch" };
        private readonly Mesh mesh = new Mesh { name = "GalacticDisk dynamic distant dots", indexFormat = IndexFormat.UInt16 };
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private readonly List<Vector3> positions = new List<Vector3>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<Vector2> uv = new List<Vector2>();
        private readonly List<int> indices = new List<int>();
        internal int Count { get { return positions.Count / 4; } }
        internal DistantGalaxyBatch() { mesh.MarkDynamic(); }
        internal void Clear()
        {
            positions.Clear(); colors.Clear(); uv.Clear(); indices.Clear(); commands.Clear();
        }
        internal void Add(DistantGalaxy.DrawData dot)
        {
            int start = positions.Count;
            for (int corner = 0; corner < 4; corner++)
            {
                float x = corner == 0 || corner == 3 ? -1 : 1;
                float y = corner < 2 ? -1 : 1;
                positions.Add(new Vector3(dot.position.x+x*dot.size.x,dot.position.y+y*dot.size.y,dot.position.z));
                uv.Add(new Vector2(x,y)); colors.Add(dot.light);
            }
            indices.Add(start); indices.Add(start+1); indices.Add(start+2);
            indices.Add(start); indices.Add(start+2); indices.Add(start+3);
        }
        internal void Finish(Camera camera)
        {
            mesh.Clear(false);
            mesh.SetVertices(positions); mesh.SetColors(colors); mesh.SetUVs(0,uv); mesh.SetTriangles(indices,0,false);
            mesh.bounds = new Bounds(Vector3.zero,Vector3.one*4f);
            properties.SetFloat("_DotProjectionY",camera.projectionMatrix.m11);
            commands.DrawMesh(mesh,Matrix4x4.identity,DistantGalaxy.SharedMaterial,0,1,properties);
        }
        internal void Dispose()
        {
            commands.Release(); GalacticDiskObject.Release(mesh);
        }
    }
}
