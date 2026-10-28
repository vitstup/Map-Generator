using System;
using System.Collections.Generic;
using System.Linq;
using TacticalMaps.Construction;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace TacticalMaps.Editor
{
    internal static class AgricultureLodBaker
    {
        private static readonly Dictionary<Shader,Shader> fadeShaders = new Dictionary<Shader,Shader>();
        internal static Shader WithCrossFade(Shader source)
        {
            if(fadeShaders.TryGetValue(source,out var cached))return cached;
            string path=AssetDatabase.GetAssetPath(source);
            if(!path.EndsWith(".shadergraph",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Expected a Shader Graph for farm LOD crossfade: "+path);
            string folder=AgricultureSetup.Folder+"/Shaders";System.IO.Directory.CreateDirectory(folder);
            string target=folder+"/Farm_"+System.IO.Path.GetFileName(path);
            if(AssetDatabase.LoadAssetAtPath<Shader>(target)==null)
            {
                var assembly=AppDomain.CurrentDomain.GetAssemblies().First(a=>a.GetType("UnityEditor.ShaderGraph.GraphData")!=null);
                var graphType=assembly.GetType("UnityEditor.ShaderGraph.GraphData");var graph=Activator.CreateInstance(graphType,true);
                var json=assembly.GetType("UnityEditor.ShaderGraph.Serialization.MultiJson");
                json.GetMethod("Deserialize").MakeGenericMethod(graphType).Invoke(null,new object[]{graph,System.IO.File.ReadAllText(path),null,false});
                graphType.GetProperty("path").SetValue(graph,"TacticalMaps/Agriculture");
                bool changed=false;
                foreach(var active in (System.Collections.IEnumerable)graphType.GetProperty("activeTargets").GetValue(graph))
                {var property=active.GetType().GetProperty("supportsLodCrossFade");if(property!=null){property.SetValue(active,true);changed=true;}}
                if(!changed)throw new InvalidOperationException("No URP crossfade target in "+path);
                string serialized=(string)json.GetMethod("Serialize").Invoke(null,new[]{graph});
                assembly.GetType("UnityEditor.ShaderGraph.GraphUtil").GetMethod("WriteToFile").Invoke(null,new object[]{target,serialized});
                AssetDatabase.ImportAsset(target,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
            }
            var shader=AssetDatabase.LoadAssetAtPath<Shader>(target);if(shader==null)throw new InvalidOperationException("Cannot import LOD shader "+target);
            fadeShaders[source]=shader;return shader;
        }
        private static Mesh Save(Mesh mesh,string path)
        {var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(old==null){AssetDatabase.CreateAsset(mesh,path);return mesh;}EditorUtility.CopySerialized(mesh,old);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(old);return old;}
        internal static Renderer[] Bake(Transform root,Transform detailed,Dictionary<Material,List<CombineInstance>> groups,int index,Vector3 pivot,bool distant)
        {
            var parent=new GameObject(distant?"Distant":"Medium");parent.transform.SetParent(root,false);parent.transform.localPosition=-pivot;
            var renderers=new List<Renderer>();int part=0;
            foreach(var pair in groups)
            {
                var pieces=pair.Value.ToArray();
                var combined=new Mesh{indexFormat=IndexFormat.UInt32};combined.CombineMeshes(pieces,true,true);
                var mesh=FarmMeshDecimator.Simplify(combined,distant?.4f:.75f,distant?.18f:.045f);Object.DestroyImmediate(combined);
                string suffix=distant?"Far":"Mid";mesh.name="House"+index+"_"+suffix+part;
                var child=new GameObject("Part_"+part,typeof(MeshFilter),typeof(MeshRenderer));child.transform.SetParent(parent.transform,false);child.GetComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=child.GetComponent<MeshRenderer>();renderer.sharedMaterial=detailed.GetChild(part).GetComponent<MeshRenderer>().sharedMaterial;
                renderer.shadowCastingMode=distant?ShadowCastingMode.Off:ShadowCastingMode.On;renderers.Add(renderer);part++;
            }
            return new[] { Merge(parent.transform, "House"+index+(distant?"_FarCombined":"_MidCombined")) };
        }
        internal static Renderer Merge(Transform parent,string name)
        {
            var children=parent.GetComponentsInChildren<MeshFilter>();var pieces=new List<CombineInstance>();var materials=new List<Material>();
            foreach(var filter in children)
            {
                var renderer=filter.GetComponent<MeshRenderer>();
                for(int sub=0;sub<filter.sharedMesh.subMeshCount;sub++)
                {pieces.Add(new CombineInstance{mesh=filter.sharedMesh,subMeshIndex=sub,transform=parent.worldToLocalMatrix*filter.transform.localToWorldMatrix});materials.Add(renderer.sharedMaterials[sub]);}
            }
            var mesh=new Mesh{indexFormat=IndexFormat.UInt32,name=name};mesh.CombineMeshes(pieces.ToArray(),false,true);mesh.RecalculateBounds();
            mesh=Save(mesh,AgricultureSetup.Folder+"/Buildings/"+name+".asset");
            foreach(var child in children){var temporary=child.sharedMesh;Object.DestroyImmediate(child.gameObject);if(!AssetDatabase.Contains(temporary))Object.DestroyImmediate(temporary);}
            var go=new GameObject("Mesh",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent,false);go.GetComponent<MeshFilter>().sharedMesh=mesh;
            var output=go.GetComponent<MeshRenderer>();output.sharedMaterials=materials.ToArray();output.shadowCastingMode=parent.name=="Distant"?ShadowCastingMode.Off:ShadowCastingMode.On;return output;
        }
        internal static GameObject Fence(int variant)
        {
            string name=variant==1?"SM_Bld_Fence_Bamboo_01":"SM_Bld_Fence_01";
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Synty/PolygonSamuraiEmpire/Prefabs/Buildings/"+name+".prefab");if(source==null)throw new InvalidOperationException("Missing fence "+name);
            var root=new GameObject("FarmFence_"+variant,typeof(FenceSection));
            try
            {
                var groups=new Dictionary<Material,List<CombineInstance>>();Bounds bounds=default;bool first=true;
                foreach(var filter in source.GetComponentsInChildren<MeshFilter>(true))
                {
                    var renderer=filter.GetComponent<MeshRenderer>();if(renderer==null||filter.sharedMesh==null)continue;
                    var matrix=source.transform.worldToLocalMatrix*filter.transform.localToWorldMatrix;
                    var b=filter.sharedMesh.bounds;for(int k=0;k<8;k++){var p=matrix.MultiplyPoint3x4(b.center+Vector3.Scale(b.extents,new Vector3((k&1)==0?-1:1,(k&2)==0?-1:1,(k&4)==0?-1:1)));if(first){bounds=new Bounds(p,Vector3.zero);first=false;}else bounds.Encapsulate(p);}
                    for(int s=0;s<filter.sharedMesh.subMeshCount&&s<renderer.sharedMaterials.Length;s++){var material=renderer.sharedMaterials[s];if(material==null)continue;if(!groups.TryGetValue(material,out var list))groups[material]=list=new List<CombineInstance>();list.Add(new CombineInstance{mesh=filter.sharedMesh,subMeshIndex=s,transform=matrix});}
                }
                bool rotate=bounds.size.z>bounds.size.x;var rotation=Matrix4x4.Rotate(Quaternion.Euler(0,rotate?90:0,0));var offset=Matrix4x4.Translate(-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z));int part=0;
                foreach(var pair in groups)
                {
                    var pieces=pair.Value.ToArray();for(int i=0;i<pieces.Length;i++)pieces[i].transform=rotation*offset*pieces[i].transform;
                    var mesh=new Mesh{indexFormat=IndexFormat.UInt32,name="Fence"+variant+"_"+part};mesh.CombineMeshes(pieces,true,true);mesh=Save(mesh,AgricultureSetup.Folder+"/Buildings/"+mesh.name+".asset");
                    var go=new GameObject("Part_"+part++,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(root.transform,false);go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterial=pair.Key;
                }
                root.GetComponent<FenceSection>().length=rotate?bounds.size.z:bounds.size.x;
                return PrefabUtility.SaveAsPrefabAsset(root,AgricultureSetup.Folder+"/Buildings/FarmFence_"+variant+".prefab");
            }finally{Object.DestroyImmediate(root);}
        }
    }
}
