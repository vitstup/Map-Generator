using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace TacticalMaps.Editor
{
    // Offline quadric edge collapse. Original per-corner UVs and normal islands survive;
    // open boundaries are pinned and every surviving face is checked against inversion.
    internal static class FarmMeshDecimator
    {
        private struct Quadric
        {
            internal double xx,xy,xz,xw,yy,yz,yw,zz,zw,ww;
            internal static Quadric Plane(Vector3 n,float d,float weight)=>new Quadric{xx=n.x*n.x*weight,xy=n.x*n.y*weight,xz=n.x*n.z*weight,xw=n.x*d*weight,yy=n.y*n.y*weight,yz=n.y*n.z*weight,yw=n.y*d*weight,zz=n.z*n.z*weight,zw=n.z*d*weight,ww=d*d*weight};
            public static Quadric operator +(Quadric a,Quadric b)=>new Quadric{xx=a.xx+b.xx,xy=a.xy+b.xy,xz=a.xz+b.xz,xw=a.xw+b.xw,yy=a.yy+b.yy,yz=a.yz+b.yz,yw=a.yw+b.yw,zz=a.zz+b.zz,zw=a.zw+b.zw,ww=a.ww+b.ww};
            internal double Cost(Vector3 p)=>Math.Max(0,xx*p.x*p.x+2*xy*p.x*p.y+2*xz*p.x*p.z+2*xw*p.x+yy*p.y*p.y+2*yz*p.y*p.z+2*yw*p.y+zz*p.z*p.z+2*zw*p.z+ww);
        }
        private sealed class Vertex {internal Vector3 Position;internal Quadric Q;internal HashSet<int> Faces=new HashSet<int>();internal bool Boundary,Deleted;internal int Version;}
        private sealed class Face {internal int[] V=new int[3],Original=new int[3];internal bool Deleted;}
        private struct Candidate {internal int A,B,VA,VB;internal Vector3 Position;internal double Cost;}
        internal static Mesh Simplify(Mesh source,float ratio,float maximumError)
        {
            var raw=source.vertices;var normals=source.normals;var uv=source.uv;var colors=source.colors;var indices=source.triangles;
            var vertices=new List<Vertex>();var weld=new Dictionary<Vector3Int,int>();var map=new int[raw.Length];
            for(int i=0;i<raw.Length;i++)
            {
                var key=Vector3Int.RoundToInt(raw[i]*10000);if(!weld.TryGetValue(key,out int id)){id=vertices.Count;weld[key]=id;vertices.Add(new Vertex{Position=raw[i]});}map[i]=id;
            }
            var faces=new List<Face>();var edgeCounts=new Dictionary<(int,int),int>();
            for(int i=0;i<indices.Length;i+=3)
            {
                var face=new Face();for(int j=0;j<3;j++){face.Original[j]=indices[i+j];face.V[j]=map[indices[i+j]];}if(face.V[0]==face.V[1]||face.V[1]==face.V[2]||face.V[2]==face.V[0])continue;
                var a=vertices[face.V[0]].Position;var cross=Vector3.Cross(vertices[face.V[1]].Position-a,vertices[face.V[2]].Position-a);if(cross.magnitude<.000001f)continue;
                var normal=cross.normalized;var q=Quadric.Plane(normal,-Vector3.Dot(normal,a),Mathf.Max(.001f,cross.magnitude*.5f));int id=faces.Count;faces.Add(face);
                for(int j=0;j<3;j++){var v=vertices[face.V[j]];v.Faces.Add(id);v.Q+=q;if(cross.magnitude*.5f > .08f)v.Boundary=true;var edge=Edge(face.V[j],face.V[(j+1)%3]);edgeCounts.TryGetValue(edge,out int count);edgeCounts[edge]=count+1;}
            }
            foreach(var pair in edgeCounts)if(pair.Value==1){vertices[pair.Key.Item1].Boundary=true;vertices[pair.Key.Item2].Boundary=true;}
            var heap=new Heap();foreach(var edge in edgeCounts.Keys)Enqueue(edge.Item1,edge.Item2);
            int remaining=faces.Count,target=Mathf.RoundToInt(remaining*ratio),iterations=0;
            while(remaining>target&&heap.Count>0&&iterations++<faces.Count*18)
            {
                var c=heap.Pop();var a=vertices[c.A];var b=vertices[c.B];if(a.Deleted||b.Deleted||a.Version!=c.VA||b.Version!=c.VB)continue;
                if(c.Cost>maximumError*maximumError)break;
                var common=new List<int>();foreach(int f in a.Faces)if(b.Faces.Contains(f)&&!faces[f].Deleted)common.Add(f);
                if(common.Count!=2)continue;
                var neighborsA=Neighbors(c.A);var neighborsB=Neighbors(c.B);neighborsA.IntersectWith(neighborsB);neighborsA.Remove(c.A);neighborsA.Remove(c.B);if(neighborsA.Count!=2)continue;
                var touched=new HashSet<int>(a.Faces);touched.UnionWith(b.Faces);bool valid=true;
                foreach(int id in touched)
                {
                    var f=faces[id];if(f.Deleted||common.Contains(id))continue;
                    var p0=vertices[f.V[0]].Position;var p1=vertices[f.V[1]].Position;var p2=vertices[f.V[2]].Position;var before=Vector3.Cross(p1-p0,p2-p0);
                    var after=new Vector3[3];for(int j=0;j<3;j++)after[j]=f.V[j]==c.A||f.V[j]==c.B?c.Position:vertices[f.V[j]].Position;
                    var n=Vector3.Cross(after[1]-after[0],after[2]-after[0]);
                    if(n.sqrMagnitude<.0000000001f || Vector3.Dot(n.normalized,before.normalized)<.35f){valid=false;break;}
                }
                if(!valid)continue;
                a.Position=c.Position;a.Q+=b.Q;a.Boundary|=b.Boundary;a.Version++;b.Deleted=true;b.Version++;
                foreach(int id in touched)
                {
                    var f=faces[id];if(f.Deleted)continue;
                    if(common.Contains(id)){f.Deleted=true;remaining--;for(int j=0;j<3;j++)vertices[f.V[j]].Faces.Remove(id);continue;}
                    for(int j=0;j<3;j++)if(f.V[j]==c.B)f.V[j]=c.A;
                    a.Faces.Add(id);
                }
                b.Faces.Clear();
                var neighbors=Neighbors(c.A);foreach(int n in neighbors)Enqueue(c.A,n);
            }
            var output=new Mesh{indexFormat=IndexFormat.UInt32};var ps=new List<Vector3>();var ns=new List<Vector3>();var ts=new List<Vector2>();var cs=new List<Color>();var tris=new List<int>();
            foreach(var f in faces)if(!f.Deleted)for(int j=0;j<3;j++){int original=f.Original[j];tris.Add(ps.Count);ps.Add(vertices[f.V[j]].Position);ns.Add(normals.Length==raw.Length?normals[original]:Vector3.up);ts.Add(uv.Length==raw.Length?uv[original]:Vector2.zero);cs.Add(colors.Length==raw.Length?colors[original]:Color.white);}
            output.SetVertices(ps);output.SetNormals(ns);output.SetUVs(0,ts);output.SetColors(cs);output.SetTriangles(tris,0);output.RecalculateBounds();output.RecalculateTangents();return output;
            HashSet<int> Neighbors(int v){var result=new HashSet<int>();foreach(int id in vertices[v].Faces)if(!faces[id].Deleted)foreach(int n in faces[id].V)if(n!=v)result.Add(n);return result;}
            void Enqueue(int u,int v)
            {
                if(u==v)return;var a=vertices[u];var b=vertices[v];if(a.Deleted||b.Deleted||a.Boundary&&b.Boundary)return;
                var q=a.Q+b.Q;var p=(a.Position+b.Position)*.5f;double cost=q.Cost(p);
                double ca=q.Cost(a.Position),cb=q.Cost(b.Position);if(ca<cost){p=a.Position;cost=ca;}if(cb<cost){p=b.Position;cost=cb;}
                if(a.Boundary){p=a.Position;cost=q.Cost(p);}else if(b.Boundary){p=b.Position;cost=q.Cost(p);}
                heap.Push(new Candidate{A=u,B=v,VA=a.Version,VB=b.Version,Position=p,Cost=cost});
            }
        }
        private static (int,int) Edge(int a,int b)=>a<b?(a,b):(b,a);
        private sealed class Heap
        {
            private readonly List<Candidate> values=new List<Candidate>();internal int Count=>values.Count;
            internal void Push(Candidate value){values.Add(value);int i=values.Count-1;while(i>0){int p=(i-1)/2;if(values[p].Cost<=value.Cost)break;values[i]=values[p];i=p;}values[i]=value;}
            internal Candidate Pop(){var result=values[0];var tail=values[values.Count-1];values.RemoveAt(values.Count-1);if(values.Count==0)return result;int i=0;while(i*2+1<values.Count){int c=i*2+1;if(c+1<values.Count&&values[c+1].Cost<values[c].Cost)c++;if(values[c].Cost>=tail.Cost)break;values[i]=values[c];i=c;}values[i]=tail;return result;}
        }
    }
}
