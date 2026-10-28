using System;
using System.Collections.Generic;
using TacticalMaps.Generation;
using UnityEngine;
using UnityEngine.Rendering;

namespace TacticalMaps.Construction
{
    internal static class FarmFenceBuilder
    {
        private sealed class Batch { internal Material Material; internal List<CombineInstance> Parts=new List<CombineInstance>(); }
        internal static void Build(Farmstead[] farms,AgricultureLibrary library,Terrain[] terrains,Vector3 center,Vector2 mainSize,Transform root,AgricultureMap owned)
        {
            if(library.fences==null || library.fences.Length==0)return;
            var batches=new Dictionary<(Vector2Int,Material),Batch>();
            foreach(var farm in farms)
            {
                if(farm.buildings==null || Mathf.Abs(farm.center.x)>mainSize.x/2+300 || Mathf.Abs(farm.center.y)>mainSize.y/2+300)continue;
                var prefab=library.fences[farm.fenceStyle%library.fences.Length];if(prefab==null)continue;
                var filters=prefab.GetComponentsInChildren<MeshFilter>();float section=prefab.GetComponent<FenceSection>().length;
                var axis=new Vector2(Mathf.Cos(farm.angle),Mathf.Sin(farm.angle));var side=new Vector2(-axis.y,axis.x);var h=farm.yardHalfSize;
                for(int edge=0;edge<4;edge++)
                {
                    if(farm.fenceStyle==2 && edge==2)continue;
                    Vector2 a,b;
                    if(edge<2){a=farm.center+axis*(edge==0?h.x:-h.x)-side*h.y;b=a+side*(2*h.y);}
                    else{a=farm.center+side*(edge==2?h.y:-h.y)-axis*h.x;b=a+axis*(2*h.x);}
                    float total=Vector2.Distance(a,b);int count=Mathf.Max(1,Mathf.RoundToInt(total/section));float length=total/count;var direction=(b-a).normalized;
                    for(int i=0;i<count;i++)
                    {
                        float along=(i+.5f)*length;
                        // Gate on the positive local X side, aligned with Farmstead.Gate.
                        if(edge==0 && Mathf.Abs(along-total*.5f)<farm.GateHalfWidth+length*.5f)continue;
                        var p=a+direction*along;var near=AgricultureBuilder.Ground(p-direction*length*.5f,terrains,center,-.045f);var far=AgricultureBuilder.Ground(p+direction*length*.5f,terrains,center,-.045f);
                        if(Mathf.Abs(far.y-near.y)>1.5f)continue;
                        var across=new Vector3(direction.x,(far.y-near.y)/length,direction.y).normalized;
                        var forward=Vector3.Cross(across,Vector3.up).normalized;var rotation=Quaternion.LookRotation(forward,Vector3.Cross(forward,across));
                        var position=(near+far)*.5f;var matrix=Matrix4x4.TRS(position,rotation,new Vector3(length/section,1,1));
                        var cell=new Vector2Int(Mathf.FloorToInt(p.x/128),Mathf.FloorToInt(p.y/128));
                        foreach(var filter in filters)
                        {
                            var renderer=filter.GetComponent<MeshRenderer>();var materials=renderer.sharedMaterials;
                            for(int sub=0;sub<filter.sharedMesh.subMeshCount && sub<materials.Length;sub++)
                            {
                                var material=materials[sub];if(material==null)continue;var key=(cell,material);
                                if(!batches.TryGetValue(key,out var batch))batches[key]=batch=new Batch{Material=material};
                                batch.Parts.Add(new CombineInstance{mesh=filter.sharedMesh,subMeshIndex=sub,transform=matrix*prefab.transform.worldToLocalMatrix*filter.transform.localToWorldMatrix});
                            }
                        }
                    }
                }
            }
            int batchIndex=0;
            foreach(var pair in batches)
            {
                var mesh=new Mesh{name="FarmFence_"+pair.Key.Item1+"_"+batchIndex++,indexFormat=IndexFormat.UInt32};mesh.CombineMeshes(pair.Value.Parts.ToArray(),true,true);mesh.RecalculateBounds();
                var go=new GameObject(mesh.name,typeof(MeshFilter),typeof(MeshRenderer),typeof(LODGroup));go.transform.SetParent(root,false);
                // Mesh positions are world coordinates; root may be translated.
                go.transform.position=Vector3.zero;
                go.GetComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.GetComponent<MeshRenderer>();renderer.sharedMaterial=pair.Value.Material;
                var lod=go.GetComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.12f,new Renderer[]{renderer})});lod.RecalculateBounds();owned.AddMesh(mesh);
            }
        }
    }
}
