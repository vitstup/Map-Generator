using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace TacticalMaps.Construction
{
    internal static class FarmSupportBuilder
    {
        internal static Mesh Build(GameObject house,FarmBuildingGrounding data,Material material,Terrain[] terrains,Vector3 center)
        {
            if(data==null || data.supports==null || data.supports.Length!=4)return null;
            var vertices=new List<Vector3>();var colors=new List<Color>();var triangles=new List<int>();
            var color=new Color(.24f,.17f,.11f).linear;float half=data.supportHalfWidth;
            foreach(var anchor in data.supports)
            {
                var p=house.transform.TransformPoint(anchor);var floor=AgricultureBuilder.Ground(new Vector2(p.x-center.x,p.z-center.z),terrains,center,-.10f);
                if(p.y<=floor.y+.025f)continue;
                float bottom=house.transform.InverseTransformPoint(floor).y,top=anchor.y+.045f;
                Vector3 P(int x,int z,float y)=>new Vector3(anchor.x+x*half,y,anchor.z+z*half);
                Quad(P(-1,-1,bottom),P(1,-1,bottom),P(1,-1,top),P(-1,-1,top));Quad(P(1,-1,bottom),P(1,1,bottom),P(1,1,top),P(1,-1,top));
                Quad(P(1,1,bottom),P(-1,1,bottom),P(-1,1,top),P(1,1,top));Quad(P(-1,1,bottom),P(-1,-1,bottom),P(-1,-1,top),P(-1,1,top));Quad(P(-1,-1,top),P(1,-1,top),P(1,1,top),P(-1,1,top));
            }
            if(vertices.Count==0)return null;
            var mesh=new Mesh{name="FarmSupports_"+house.GetInstanceID()};mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            var go=new GameObject("StructuralSupports",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(house.transform,false);go.GetComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.GetComponent<MeshRenderer>();renderer.sharedMaterial=material;
            var group=house.GetComponent<LODGroup>();if(group!=null){var lods=group.GetLODs();for(int i=0;i<lods.Length;i++){var list=new List<Renderer>(lods[i].renderers){renderer};lods[i].renderers=list.ToArray();}group.SetLODs(lods);group.RecalculateBounds();}
            return mesh;
            void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d){int n=vertices.Count;vertices.AddRange(new[]{a,b,c,d});colors.AddRange(new[]{color,color,color,color});triangles.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});}
        }
    }
}
