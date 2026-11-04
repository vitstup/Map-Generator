using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class GrassMeshFactory
    {
        // UV0 holds signed width and curve position; UV1 selects blade and petal topology.
        // The GPU evaluates different widths, curves, roots and families for every plant.
        internal static Mesh Ensure(string path,bool detailed,bool simplified=false)
        {
            var vertices=new List<Vector3>(); var uv=new List<Vector2>(); var meta=new List<Vector2>(); var triangles=new List<int>();
            int blades=detailed||simplified?12:32;
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
            int heads=detailed||simplified?1:2;
            for(int head=0;head<heads;head++)for(int petal=0;petal<9;petal++)
            {
                int start=vertices.Count;int blade=(detailed||simplified?12:32)+head*9+petal;
                int part=petal==8?2:1;
                Add(-1,0,blade,part);Add(1,0,blade,part);Add(0,1,blade,part);
                triangles.AddRange(new[]{start,start+2,start+1});
            }
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            // Do not replace native vertex buffers of an unchanged mesh used by live indirect draws.
            if(existing!=null&&Matches(existing,vertices,uv,meta,triangles)) return existing;
            var mesh=existing!=null?existing:new Mesh(); mesh.Clear();
            mesh.name=detailed?"ParametricMeadowPlants":simplified?"SimplifiedMeadowPlants":"LowMeadowPatch";
            mesh.SetVertices(vertices); mesh.SetUVs(0,uv); mesh.SetUVs(1,meta); mesh.SetTriangles(triangles,0);
            mesh.bounds=new Bounds(Vector3.up*.6f,new Vector3(7,3,7));
            mesh.UploadMeshData(false);
            if(existing!=null) { EditorUtility.SetDirty(existing); return existing; }
            AssetDatabase.CreateAsset(mesh,path); return mesh;
            void Add(float side,float t,int blade,int part)
            { vertices.Add(new Vector3(side*.1f,t,blade*.001f)); uv.Add(new Vector2(side,t)); meta.Add(new Vector2(blade,part)); }
        }
        private static bool Matches(Mesh mesh,List<Vector3> vertices,List<Vector2> uv,List<Vector2> meta,List<int> triangles)
        {
            if(mesh.vertexCount!=vertices.Count||mesh.GetIndexCount(0)!=(uint)triangles.Count) return false;
            var oldVertices=mesh.vertices;var oldUV=mesh.uv;var oldMeta=mesh.uv2;var oldTriangles=mesh.triangles;
            if(oldUV.Length!=uv.Count||oldMeta.Length!=meta.Count) return false;
            for(int i=0;i<vertices.Count;i++) if(oldVertices[i]!=vertices[i]||oldUV[i]!=uv[i]||oldMeta[i]!=meta[i]) return false;
            for(int i=0;i<triangles.Count;i++) if(oldTriangles[i]!=triangles[i]) return false;
            return true;
        }
    }
}
