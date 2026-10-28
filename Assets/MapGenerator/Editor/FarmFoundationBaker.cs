using System;
using System.Collections.Generic;
using UnityEngine;
namespace TacticalMaps.Editor
{
    internal static class FarmFoundationBaker
    {
        internal static Vector3[] Anchors(GameObject prefab,Vector3 pivot,Bounds overall)
        {
            var floors=new List<Bounds>();
            foreach(var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if(filter.sharedMesh==null||filter.sharedMesh.name.IndexOf("Floor",StringComparison.OrdinalIgnoreCase)<0)continue;
                var source=filter.sharedMesh.bounds;var matrix=prefab.transform.worldToLocalMatrix*filter.transform.localToWorldMatrix;Bounds box=default;bool first=true;
                for(int k=0;k<8;k++){var p=matrix.MultiplyPoint3x4(source.center+Vector3.Scale(source.extents,new Vector3((k&1)==0?-1:1,(k&2)==0?-1:1,(k&4)==0?-1:1)));if(first){box=new Bounds(p,Vector3.zero);first=false;}else box.Encapsulate(p);}
                if(box.size.y<.7f&&box.size.x>.8f&&box.size.z>.8f)floors.Add(box);
            }
            var anchors=new List<Vector3>();
            if(floors.Count>0)
            {
                float level=float.MaxValue;foreach(var f in floors)level=Mathf.Min(level,f.min.y);
                Bounds platform=default;bool first=true;foreach(var f in floors)if(f.min.y<=level+.25f){if(first){platform=f;first=false;}else platform.Encapsulate(f);}
                int nx=Mathf.Max(1,Mathf.CeilToInt((platform.size.x-.5f)/3)),nz=Mathf.Max(1,Mathf.CeilToInt((platform.size.z-.5f)/3));
                for(int z=0;z<=nz;z++)for(int x=0;x<=nx;x++)
                {
                    var p=new Vector3(Mathf.Lerp(platform.min.x+.24f,platform.max.x-.24f,x/(float)nx),level,Mathf.Lerp(platform.min.z+.24f,platform.max.z-.24f,z/(float)nz));
                    foreach(var f in floors)if(f.min.y<=level+.25f && p.x>=f.min.x+.04f && p.x<=f.max.x-.04f && p.z>=f.min.z+.04f && p.z<=f.max.z-.04f){Add(p-pivot);break;}
                }
                foreach(var f in floors)if(f.min.y<=level+.25f)for(int z=-1;z<=1;z+=2)for(int x=-1;x<=1;x+=2)
                    Add(new Vector3(f.center.x+x*Mathf.Max(.1f,f.extents.x-.25f),f.min.y,f.center.z+z*Mathf.Max(.1f,f.extents.z-.25f))-pivot);
            }
            if(anchors.Count<4)
            {
                // Explicit four-corner structural fallback, never accept a single detected leg.
                anchors.Clear();float y=Mathf.Max(0,overall.min.y-pivot.y);
                for(int z=-1;z<=1;z+=2)for(int x=-1;x<=1;x+=2)anchors.Add(new Vector3(x*overall.extents.x*.7f,y,z*overall.extents.z*.7f));
            }
            return anchors.ToArray();
            void Add(Vector3 p){foreach(var a in anchors)if(new Vector2(a.x-p.x,a.z-p.z).sqrMagnitude<.7f*.7f)return;anchors.Add(p);}
        }
    }
}
