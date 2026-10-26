using System;
using System.Collections.Generic;
using UnityEngine;

namespace TacticalMaps.Generation
{
    // CPU result; no textures or scene objects are owned here.
    internal sealed class RoadData
    {
        internal const int TileSize = 128, TilePixels = 130;
        internal readonly RoadSegment[] Segments;
        internal readonly Vector2 Origin;
        internal readonly int Columns, Rows;
        internal readonly float Shoulder, Clearance;
        internal readonly Vector3[] TypeTransitions;
        internal readonly Dictionary<int, List<int>> Bins = new Dictionary<int, List<int>>();
        internal readonly List<int> TileKeys = new List<int>();
        internal readonly List<Color32[]> Tiles = new List<Color32[]>();
        internal readonly Color32[] FarCoverage;
        internal readonly int FarWidth, FarHeight;
        internal double RoutingMilliseconds, RasterMilliseconds, CaptureMilliseconds, TopologyMilliseconds;
        internal string Timings => $"capture {CaptureMilliseconds:F0}; topology wait {TopologyMilliseconds:F0}; routing {RoutingMilliseconds:F0}; masks {RasterMilliseconds:F0} ms";
        internal int RejectedSections;

        internal RoadData(RoadSegment[] segments, Vector2 size, float shoulder, float clearance)
        {
            Shoulder = shoulder; Clearance = clearance;
            var nodes=new Dictionary<Vector2Int,Vector4>();
            foreach(var segment in segments)
            {
                foreach(var p in new[]{segment.A,segment.B})
                {
                    var key=new Vector2Int(Mathf.RoundToInt(p.x*100),Mathf.RoundToInt(p.y*100));nodes.TryGetValue(key,out var node);
                    if(segment.Type==1)node.x++;else{node.y++;node.z=segment.HalfWidth;}nodes[key]=node;
                }
            }
            var transitions=new List<Vector3>();
            foreach(var pair in nodes)if(pair.Value.x==1&&pair.Value.y>0)transitions.Add(new Vector3(pair.Key.x*.01f,pair.Key.y*.01f,pair.Value.z));
            TypeTransitions=transitions.ToArray();
            for(int i=0;i<segments.Length;i++)
            {
                var s=segments[i];if(s.Type!=1)continue;float a=s.HalfWidthA,b=s.HalfWidthB;
                foreach(var node in TypeTransitions)
                {
                    var p=new Vector2(node.x,node.y);
                    a=Mathf.Min(a,Mathf.Lerp(node.z,a,Mathf.SmoothStep(0,1,Vector2.Distance(s.A,p)/28)));
                    b=Mathf.Min(b,Mathf.Lerp(node.z,b,Mathf.SmoothStep(0,1,Vector2.Distance(s.B,p)/28)));
                }
                segments[i]=new RoadSegment(s.A,s.B,a*2,b*2,s.Type);
            }
            Segments = segments;
            Origin = new Vector2(Mathf.Floor(-size.x / 2 / TileSize) * TileSize, Mathf.Floor(-size.y / 2 / TileSize) * TileSize);
            Columns = Mathf.CeilToInt((size.x / 2 - Origin.x) / TileSize);
            Rows = Mathf.CeilToInt((size.y / 2 - Origin.y) / TileSize);
            FarWidth=Columns*4;FarHeight=Rows*4;
            FarCoverage=new Color32[FarWidth*FarHeight];
            for (int i = 0; i < segments.Length; i++)
            {
                var s = segments[i]; float margin = s.HalfWidth + shoulder + 18;
                var lo = Vector2.Min(s.A, s.B) - Vector2.one * margin;
                var hi = Vector2.Max(s.A, s.B) + Vector2.one * margin;
                int x0 = Mathf.Max(0, Mathf.FloorToInt((lo.x - Origin.x) / TileSize));
                int y0 = Mathf.Max(0, Mathf.FloorToInt((lo.y - Origin.y) / TileSize));
                int x1 = Mathf.Min(Columns - 1, Mathf.FloorToInt((hi.x - Origin.x) / TileSize));
                int y1 = Mathf.Min(Rows - 1, Mathf.FloorToInt((hi.y - Origin.y) / TileSize));
                for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                { int key = y * Columns + x; if (!Bins.TryGetValue(key, out var list)) Bins.Add(key, list = new List<int>()); list.Add(i); }
            }
        }

        internal bool Blocks(Vector2 point, float radius = 0)
        {
            float margin = radius + Clearance + Shoulder;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((point.x - margin - Origin.x) / TileSize));
            int y0 = Mathf.Max(0, Mathf.FloorToInt((point.y - margin - Origin.y) / TileSize));
            int x1 = Mathf.Min(Columns - 1, Mathf.FloorToInt((point.x + margin - Origin.x) / TileSize));
            int y1 = Mathf.Min(Rows - 1, Mathf.FloorToInt((point.y + margin - Origin.y) / TileSize));
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                if (Bins.TryGetValue(y * Columns + x, out var list)) foreach (int i in list)
                { var s = Segments[i]; if (Distance(point, s.A, s.B) <= s.HalfWidth + margin) return true; }
            return false;
        }
        internal static float Distance(Vector2 p, Vector2 a, Vector2 b)
        { var v = b - a; return Vector2.Distance(p, a + v * Mathf.Clamp01(Vector2.Dot(p - a, v) / Mathf.Max(.00001f, v.sqrMagnitude))); }
    }

    internal readonly struct RoadSegment
    {
        internal readonly Vector2 A, B;
        internal readonly float HalfWidth,HalfWidthA,HalfWidthB;
        internal readonly byte Type;
        internal RoadSegment(Vector2 a, Vector2 b, float width, byte type) : this(a,b,width,width,type) { }
        internal RoadSegment(Vector2 a,Vector2 b,float widthA,float widthB,byte type){A=a;B=b;HalfWidthA=widthA/2;HalfWidthB=widthB/2;HalfWidth=Mathf.Max(HalfWidthA,HalfWidthB);Type=type;}
    }
}
