using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace GalacticDisk
{
    // Optional integration: no compile-time dependency or changes to Singularity's DLL/configs.
    internal static class SingularityCompatibility
    {
        private sealed class Binding { internal Material material; internal Shader original; internal object owner, item; }
        private struct ClearState { internal CameraClearFlags flags; internal Color color; }
        private static readonly Dictionary<Material,Binding> bindings = new Dictionary<Material,Binding>();
        private static readonly Dictionary<Camera,ClearState> captures = new Dictionary<Camera,ClearState>();
        private static readonly List<Camera> stale = new List<Camera>();
        private static Type pluginType;
        private static PropertyInfo instanceProperty;
        private static FieldInfo objectsField, materialField;
        private static object instance;
        private static int lastTick = -1, nextSearch, objectCount = -1;
        private static bool hooked, failed;
        private static Shader replacement;

        internal static void Tick()
        {
            if (lastTick == Time.frameCount) return;
            lastTick = Time.frameCount;
            try
            {
                if (pluginType == null)
                {
                    if (Time.frameCount < nextSearch) return;
                    nextSearch = Time.frameCount+300;
                    foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        pluginType = assembly.GetType("Singularity.Singularity",false);
                        if (pluginType != null) break;
                    }
                    if (pluginType == null) return;
                    instanceProperty = pluginType.GetProperty("Instance",BindingFlags.Static|BindingFlags.Public);
                    objectsField = pluginType.GetField("loadedObjects",BindingFlags.Public|BindingFlags.Instance);
                    if (instanceProperty == null || objectsField == null) { pluginType = null; return; }
                }
                object current = instanceProperty.GetValue(null,null);
                if (current is UnityEngine.Object && (UnityEngine.Object)current == null) current = null;
                if (!ReferenceEquals(current,instance)) { RestoreMaterials(); instance = current; objectCount = -1; }
                if (current == null) return;
                var objects = objectsField.GetValue(current) as IList;
                if (objects == null || (objects.Count == objectCount && Time.frameCount%60 != 0)) return;
                objectCount = objects.Count;
                if (!hooked)
                {
                    Camera.onPreCull += BeforeCapture; Camera.onPostRender += AfterCapture; hooked = true;
                }
                int changed = 0;
                foreach (object item in objects)
                {
                    if (item == null || (item is UnityEngine.Object && (UnityEngine.Object)item == null)) continue;
                    if (materialField == null || materialField.DeclaringType != item.GetType())
                        materialField = item.GetType().GetField("singularityMaterial",BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance);
                    var material = materialField == null ? null : materialField.GetValue(item) as Material;
                    if (material == null || bindings.ContainsKey(material) || material.shader == null || material.shader.name != "Singularity/BlackHoleAccretionDisk") continue;
                    if (replacement == null) replacement = GalaxyShaderAssets.LoadSingularity();
                    Shader original = material.shader;
                    SetShader(material,replacement);
                    ConfigureMaterial(material,current,item);
                    bindings.Add(material,new Binding { material = material, original = original, owner = current, item = item }); changed++;
                }
                if (changed > 0) Debug.Log("[GalacticDisk] Enabled transparent-background lensing for "+changed+" Singularity materials.");
            }
            catch (Exception error)
            {
                if (!failed) Debug.LogWarning("[GalacticDisk] Singularity compatibility could not initialize: "+error.Message);
                failed = true;
            }
        }
        private static void SetShader(Material material, Shader shader)
        {
            var snapshot = new Material(material);
            string[] keywords = material.shaderKeywords;
            material.shader = shader; material.CopyPropertiesFromMaterial(snapshot);
            material.shaderKeywords = keywords;
            if (Application.isPlaying) UnityEngine.Object.Destroy(snapshot); else UnityEngine.Object.DestroyImmediate(snapshot);
        }
        private static object Field(object owner, string name)
        {
            if (owner == null) return null;
            FieldInfo field = owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            return field == null ? null : field.GetValue(owner);
        }
        private static float Number(object owner, string name, float fallback)
        {
            object value = Field(owner,name); return value is float ? (float)value : fallback;
        }
        private static void ConfigureMaterial(Material material, object owner, object item)
        {
            // Unity 2019 does not copy shader uniforms absent from ShaderLab Properties.
            // Rehydrate the plugin-owned values from the same fields its Init/Update use.
            float gravity = Number(item,"gravity",1);
            material.SetFloat("gravity",gravity);
            material.SetFloat("blackHoleRadius",Number(item,"scaledRadius",Mathf.Sqrt(Mathf.Max(0,gravity))*5));
            material.SetFloat("enclosingMeshRadius",Number(item,"enclosingMeshRadius",Number(item,"scaleEnclosingMesh",1)*Mathf.Sqrt(Mathf.Abs(gravity))*120));
            material.SetFloat("useScreenBuffer",1); material.SetFloat("useReflectionProbeMode",0);
            material.SetFloat("diskInnerRadius",Number(item,"accretionDiskInnerRadius",1)/6000);
            material.SetFloat("diskOuterRadius",Number(item,"accretionDiskOuterRadius",5)/6000);
            material.SetFloat("rotationSpeed",Number(item,"accretionDiskRotationSpeed",60)*Mathf.PI*2/60);
            object normal = Field(item,"accretionDiskNormal"); material.SetVector("diskNormal",normal is Vector3 ? (Vector3)normal : Vector3.up);
            var control = Field(owner,"galaxyCubeControlMPB") as MaterialPropertyBlock;
            material.SetColor("galaxyFadeColor",control == null ? Color.white : control.GetColor("_Color"));
            material.SetMatrix("cubeMapRotation",Matrix4x4.Rotate((Quaternion)Planetarium.Rotation).inverse);
            var clock = owner.GetType().GetMethod("getTime",BindingFlags.Public|BindingFlags.Instance);
            if (clock != null) material.SetFloat("universalTime",Convert.ToSingle(clock.Invoke(owner,null)));
            material.SetTexture("objectCubeMap",Field(Field(item,"singularityCubeMap"),"singularityCubemap") as Texture);
            material.SetTexture("wormholeCubemap",Field(Field(item,"wormholeCubeMap"),"singularityCubemap") as Texture);
            material.SetTexture("SingularityScreenBuffer",Field(owner,"screenBufferFlip") as Texture);
            material.SetTexture("SingularityDepthTexture",Field(owner,"stackingDepthBuffer") as Texture);
        }
        private static void BeforeCapture(Camera camera)
        {
            // This camera is created with this exact name in ScaledSceneBufferRenderer.Init.
            // It deliberately excludes stock sky; clear RGBA so empty pixels remain identifiable.
            if (instance == null || camera == null || camera.name != "Singularity scene camera" || captures.ContainsKey(camera)) return;
            captures.Add(camera,new ClearState { flags = camera.clearFlags, color = camera.backgroundColor });
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
        }
        private static void AfterCapture(Camera camera)
        {
            ClearState state;
            if (!captures.TryGetValue(camera,out state)) return;
            if (camera != null) { camera.clearFlags = state.flags; camera.backgroundColor = state.color; }
            captures.Remove(camera);
        }
        private static void RestoreMaterials()
        {
            foreach (Binding binding in bindings.Values)
                if (binding.material != null && binding.material.shader == replacement && binding.original != null)
                {
                    SetShader(binding.material,binding.original);
                    bool ownerAlive = !(binding.owner is UnityEngine.Object) || (UnityEngine.Object)binding.owner != null;
                    bool itemAlive = !(binding.item is UnityEngine.Object) || (UnityEngine.Object)binding.item != null;
                    if (ownerAlive && itemAlive) ConfigureMaterial(binding.material,binding.owner,binding.item);
                }
            bindings.Clear();
        }
        internal static void Clear()
        {
            RestoreMaterials();
            stale.Clear(); foreach (var camera in captures.Keys) stale.Add(camera);
            foreach (var camera in stale) AfterCapture(camera);
            if (hooked) { Camera.onPreCull -= BeforeCapture; Camera.onPostRender -= AfterCapture; hooked = false; }
            instance = null; objectCount = -1; lastTick = -1; failed = false;
        }
    }
}
