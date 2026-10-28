using TacticalMaps.Construction;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;
namespace TacticalMaps.Editor
{
    internal static class FarmPropBaker
    {
        internal static void Install(AgricultureLibrary library)
        {
            string[] names={"Props/SM_Prop_Barrel_01","Props/SM_Prop_Basket_01","Props/SM_Prop_Bucket_01","Props/SM_Prop_Wood_01","Props/SM_Prop_WinnowingBasket_01","Vehicles/SM_Veh_Cart_01","Props/SM_Prop_Well_01","Props/SM_Prop_Barrel_02","Props/SM_Prop_Basket_02","Props/SM_Prop_Basket_03","Props/SM_Prop_Pot_01","Props/SM_Prop_Pot_02","Props/SM_Prop_Stool_01"};
            library.props=new GameObject[names.Length];library.propRadii=new float[names.Length];
            for(int i=0;i<names.Length;i++)
            {
                var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Synty/PolygonSamuraiEmpire/Prefabs/"+names[i]+".prefab");if(source==null)throw new InvalidOperationException(names[i]);
                    var root=new GameObject("FarmProp_"+i);var pieces=new List<CombineInstance>();var materials=new List<Material>();
                try
                {
                    foreach(var filter in source.GetComponentsInChildren<MeshFilter>(true))
                    {
                        var renderer=filter.GetComponent<MeshRenderer>();if(renderer==null||filter.sharedMesh==null||!filter.gameObject.activeSelf)continue;
                        for(int sub=0;sub<filter.sharedMesh.subMeshCount&&sub<renderer.sharedMaterials.Length;sub++)
                        {
                            var original=renderer.sharedMaterials[sub];if(original==null)continue;
                            string materialPath=AgricultureSetup.Folder+"/Buildings/PropMaterial_"+AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(original))+".mat";
                            var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);if(material==null){material=new Material(original);AssetDatabase.CreateAsset(material,materialPath);}material.enableInstancing=true;EditorUtility.SetDirty(material);
                            pieces.Add(new CombineInstance{mesh=filter.sharedMesh,subMeshIndex=sub,transform=source.transform.worldToLocalMatrix*filter.transform.localToWorldMatrix});materials.Add(material);
                        }
                    }
                    var groups=new Dictionary<Material,List<CombineInstance>>();
                    for(int j=0;j<pieces.Count;j++){if(!groups.TryGetValue(materials[j],out var list))groups[materials[j]]=list=new List<CombineInstance>();list.Add(pieces[j]);}
                    pieces.Clear();materials.Clear();var temporary=new List<Mesh>();
                    foreach(var pair in groups){var part=new Mesh{indexFormat=IndexFormat.UInt32};part.CombineMeshes(pair.Value.ToArray(),true,true);temporary.Add(part);pieces.Add(new CombineInstance{mesh=part,transform=Matrix4x4.identity});materials.Add(pair.Key);}
                    var mesh=new Mesh{name="FarmProp"+i,indexFormat=IndexFormat.UInt32};mesh.CombineMeshes(pieces.ToArray(),false,true);foreach(var part in temporary)Object.DestroyImmediate(part);mesh.RecalculateBounds();var bounds=mesh.bounds;
                    string path=AgricultureSetup.Folder+"/Buildings/FarmProp"+i+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(old==null)AssetDatabase.CreateAsset(mesh,path);else{EditorUtility.CopySerialized(mesh,old);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(old);mesh=old;}
                    var child=new GameObject("Mesh",typeof(MeshFilter),typeof(MeshRenderer));child.transform.SetParent(root.transform,false);child.transform.localPosition=-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
                    child.GetComponent<MeshFilter>().sharedMesh=mesh;child.GetComponent<MeshRenderer>().sharedMaterials=materials.ToArray();
                    library.propRadii[i]=new Vector2(bounds.extents.x,bounds.extents.z).magnitude;
                    library.props[i]=PrefabUtility.SaveAsPrefabAsset(root,AgricultureSetup.Folder+"/Buildings/FarmProp_"+i+".prefab");
                }finally{Object.DestroyImmediate(root);}
            }
            EditorUtility.SetDirty(library);
        }
    }
}
