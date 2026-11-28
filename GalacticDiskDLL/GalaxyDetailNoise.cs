using UnityEngine;

namespace GalacticDisk
{
    // One seamless, mipmapped detail volume shared by all version 2 galaxies.
    internal static class GalaxyDetailNoise
    {
        private static Texture3D texture;
        internal static int Users { get; private set; }
        internal static Texture3D Acquire()
        {
            if (texture == null)
            {
                const int size=64;
                var pixels=new Color[size*size*size];
                uint state=0x94d049bb;
                for(int i=0;i<pixels.Length;i++) pixels[i]=new Color(Next(ref state),Next(ref state),Next(ref state),Next(ref state));
                var staged=new Texture3D(size,size,size,TextureFormat.RGBA32,true)
                { name="GalacticDisk shared seamless detail", hideFlags=HideFlags.HideAndDontSave,
                    wrapMode=TextureWrapMode.Repeat, filterMode=FilterMode.Trilinear };
                try { staged.SetPixels(pixels); staged.Apply(true,true); texture=staged; }
                catch { GalacticDiskObject.Release(staged); throw; }
            }
            Users++; return texture;
        }
        private static float Next(ref uint state)
        {
            unchecked { state^=state<<13; state^=state>>17; state^=state<<5; return (state&65535)/65535f; }
        }
        internal static void Release()
        {
            if (Users == 0 || --Users > 0) return;
            GalacticDiskObject.Release(texture); texture=null;
        }
    }
}
