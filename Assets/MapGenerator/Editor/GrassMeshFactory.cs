using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class GrassMeshFactory
    {
        // UV0 holds signed width and curve position; UV1 selects blade and petal topology.
        // The GPU evaluates different widths, curves, roots and families for every plant.
        internal static Mesh Ensure(string path,bool detailed)
        {
            var vertices=new List<Vector3>(); var uv=new List<Vector2>(); var meta=new List<Vector2>(); var triangles=new List<int>();
            int blades=detailed?12:32;
            for(int blade=0;blade<blades;blade++)
            {
                int start=vertices.Count;
                Add(-1,0,blade,0); Add(1,0,blade,0);
                if(detailed)
                {
                    Add(-1,.52f,blade,0); Add(1,.52f,blade,0); Add(0,1,blade,0);
                    triangles.AddRange(new[]{start,start+2,start+1,start+1,start+2,start+3,start+2,start+4,start+3});
                }
                else { Add(0,1,blade,0); triangles.AddRange(new[]{start,start+2,start+1}); }
            }
            if(detailed) for(int petal=0;petal<5;petal++)
            {
                int start=vertices.Count; Add(-1,0,12+petal,1); Add(1,0,12+petal,1); Add(0,1,12+petal,1);
                triangles.AddRange(new[]{start,start+2,start+1});
            }
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            var mesh=existing!=null?existing:new Mesh(); mesh.Clear();
            mesh.name=detailed?"ParametricMeadowPlants":"LowMeadowPatch";
            mesh.SetVertices(vertices); mesh.SetUVs(0,uv); mesh.SetUVs(1,meta); mesh.SetTriangles(triangles,0);
            mesh.bounds=new Bounds(Vector3.up*.6f,new Vector3(7,3,7));
            mesh.UploadMeshData(false);
            if(existing!=null) { EditorUtility.SetDirty(existing); return existing; }
            AssetDatabase.CreateAsset(mesh,path); return mesh;
            void Add(float side,float t,int blade,int part)
            { vertices.Add(new Vector3(side*.1f,t,blade*.001f)); uv.Add(new Vector2(side,t)); meta.Add(new Vector2(blade,part)); }
        }
    }
}
