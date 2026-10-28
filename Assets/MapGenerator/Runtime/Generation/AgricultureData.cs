using System;
using System.Collections.Generic;
using UnityEngine;

namespace TacticalMaps.Generation
{
    [Serializable]
    internal struct FarmField
    {
        public Vector2 center, halfSize;
        public float angle, height, variation;
        // 0 fallow, 1 green rice, 2 ripe rice. Fields are always dry.
        public int crop;
        // Rectangle in the parcel's local axes, shared by plant clipping and collision queries.
        public Vector4 cropExclusion;
        public int groupId;
        internal Vector2 Axis => new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        internal Vector2 Point(float x, float z) { var a = Axis; return center + a * x + new Vector2(-a.y, a.x) * z; }
        internal bool Contains(Vector2 p, float margin = 0)
        {
            return SignedDistance(p) <= margin;
        }
        internal float CornerRadius => Mathf.Min(3, Mathf.Max(2.2f, Mathf.Min(halfSize.x, halfSize.y) * .12f));
        internal bool ExcludesCrops(Vector2 p, float margin = 0)
        {
            if(cropExclusion.z<=0 || cropExclusion.w<=0)return false;
            var d=p-center;var axis=Axis;var side=new Vector2(-axis.y,axis.x);
            return Mathf.Abs(Vector2.Dot(d,axis)-cropExclusion.x)<=cropExclusion.z+margin && Mathf.Abs(Vector2.Dot(d,side)-cropExclusion.y)<=cropExclusion.w+margin;
        }
        internal void ReserveYard(Farmstead farm)
        {
            var delta=farm.center-center;var axis=Axis;var side=new Vector2(-axis.y,axis.x);
            // Extend outside the parcel far enough that inflated road collision tests can cross its edge.
            // Only the intersection with the parcel affects plants or terrain shading.
            float x=Vector2.Dot(delta,axis),y=Vector2.Dot(delta,side);
            float top=y+farm.yardHalfSize.x+1.2f,bottom=-halfSize.y-16;
            cropExclusion=new Vector4(x,(bottom+top)*.5f,farm.yardHalfSize.y+1.2f,(top-bottom)*.5f);
        }
        internal float SignedDistance(Vector2 p)
        { var a = Axis; var d = p - center; var q = new Vector2(Mathf.Abs(Vector2.Dot(d, a)), Mathf.Abs(Vector2.Dot(d, new Vector2(-a.y, a.x)))) - halfSize + Vector2.one * CornerRadius; return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0) - CornerRadius; }
        internal Bounds Bounds(float margin = 0)
        {
            var a = Axis; var e = new Vector2(Mathf.Abs(a.x) * halfSize.x + Mathf.Abs(a.y) * halfSize.y,
                Mathf.Abs(a.y) * halfSize.x + Mathf.Abs(a.x) * halfSize.y) + Vector2.one * margin;
            return new Bounds(new Vector3(center.x, height, center.y), new Vector3(e.x * 2, 20000, e.y * 2));
        }
    }
    [Serializable]
    internal struct Farmstead
    {
        public Vector2 center;
        public float angle;
        public int variant, groupId;
        public float houseHeight, storeHeight;
        public Vector2 halfFootprint;
        public FarmBuilding[] buildings;
        public Vector2 yardHalfSize;
        public int fenceStyle;
        public bool embedded;
        public Vector2 parcelCenter, parcelHalfSize;
        public float parcelAngle, gateHalfWidth, gateLead;
        internal float GateLead => gateLead>0?gateLead:8;
        internal Vector2 Axis => new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        internal float GateHalfWidth => gateHalfWidth > 0 ? gateHalfWidth : 4.5f;
        internal Vector2 Local(Vector2 p) { var d=p-center;var a=Axis;return new Vector2(Vector2.Dot(d,a),Vector2.Dot(d,new Vector2(-a.y,a.x))); }
        internal bool ContainsYard(Vector2 p,float margin=0) { var q=Local(p);return Mathf.Abs(q.x)<=yardHalfSize.x+margin && Mathf.Abs(q.y)<=yardHalfSize.y+margin; }
        internal bool GateCorridor(Vector2 p,float margin=0) {var q=Local(p);return q.x>=yardHalfSize.x-4-margin && q.x<=yardHalfSize.x+GateLead+1+margin && Mathf.Abs(q.y)<=GateHalfWidth-margin;}
        internal bool IntersectsYard(Vector2 start,Vector2 end,float margin)
        {
            var a=Local(start);var d=Local(end)-a;var half=yardHalfSize+Vector2.one*margin;float lo=0,hi=1;
            for(int i=0;i<2;i++){if(Mathf.Abs(d[i])<.00001f){if(Mathf.Abs(a[i])>half[i])return false;}else{float u=(-half[i]-a[i])/d[i],v=(half[i]-a[i])/d[i];if(u>v){float t=u;u=v;v=t;}lo=Mathf.Max(lo,u);hi=Mathf.Min(hi,v);if(lo>hi)return false;}}return true;
        }
        internal Vector2 Gate => center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (yardHalfSize.x > 0 ? yardHalfSize.x + GateLead : 25);
        internal FarmBuilding[] Buildings => buildings ?? new[] {
            new FarmBuilding { center = center, angle = angle, variant = variant, height = houseHeight, halfSize = halfFootprint, scale = 1 },
            new FarmBuilding { center = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 20, angle = angle + Mathf.PI / 2, variant = variant, height = storeHeight, halfSize = halfFootprint * .65f, scale = .65f } };
    }
    [Serializable]
    internal struct FarmBuilding
    {
        public Vector2 center, halfSize;
        public float angle, height, scale;
        public int variant;
        internal bool Contains(Vector2 p, float margin)
        { var d = p - center; float c = Mathf.Cos(angle), s = Mathf.Sin(angle); return Mathf.Abs(d.x*c+d.y*s) <= halfSize.x+margin && Mathf.Abs(-d.x*s+d.y*c) <= halfSize.y+margin; }
    }
    [Serializable]
    internal struct FarmTrack
    {
        public Vector2 a, b, controlA, controlB;
        public int ownerGroup;
        internal Vector2 Point(float t)
        {
            if (controlA == Vector2.zero && controlB == Vector2.zero) return Vector2.Lerp(a, b, t);
            float s = 1 - t; return s * s * s * a + 3 * s * s * t * controlA + 3 * s * t * t * controlB + t * t * t * b;
        }
        internal float Distance(Vector2 p)
        { int n = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(a, b) / 12)); float d = float.MaxValue; var start = a; for (int i = 1; i <= n; i++) { var end = Point((float)i / n); d = Mathf.Min(d, RoadData.Distance(p, start, end)); start = end; } return d; }
    }
    internal sealed class AgricultureData
    {
        internal readonly FarmField[] Fields;
        internal readonly Farmstead[] Farms;
        internal readonly FarmTrack[] Tracks;
        internal Color32[][] Masks;
        internal int[] Resolutions;
        internal AgricultureSurfaceData Surface;
        internal double PlanningMilliseconds, SurfaceMilliseconds;
        internal int AccessRequested, AccessConnected;
        internal RoadData CombinedRoads;
        private readonly Dictionary<Vector2Int, List<int>> bins = new Dictionary<Vector2Int, List<int>>();
        internal AgricultureData(FarmField[] fields, Farmstead[] farms, Color32[][] masks, int[] resolutions, FarmTrack[] tracks = null)
        {
            Fields = fields; Farms = farms; Masks = masks; Resolutions = resolutions;
            Tracks = tracks ?? Array.Empty<FarmTrack>();
            for (int i = 0; i < fields.Length; i++)
            {
                var b = fields[i].Bounds(8);
                for (int z = Mathf.FloorToInt(b.min.z / 128); z <= Mathf.FloorToInt(b.max.z / 128); z++)
                    for (int x = Mathf.FloorToInt(b.min.x / 128); x <= Mathf.FloorToInt(b.max.x / 128); x++)
                    {
                        var key = new Vector2Int(x, z);
                        if (!bins.TryGetValue(key, out var list)) bins.Add(key, list = new List<int>());
                        list.Add(i);
                    }
            }
            for (int i = 0; i < Tracks.Length; i++)
            {
                var lo = Vector2.Min(Tracks[i].a, Tracks[i].b) - Vector2.one * 24; var hi = Vector2.Max(Tracks[i].a, Tracks[i].b) + Vector2.one * 24;
                for (int z = Mathf.FloorToInt(lo.y / 128); z <= Mathf.FloorToInt(hi.y / 128); z++) for (int x = Mathf.FloorToInt(lo.x / 128); x <= Mathf.FloorToInt(hi.x / 128); x++)
                { var key = new Vector2Int(x, z); if (!bins.TryGetValue(key, out var list)) bins.Add(key, list = new List<int>()); list.Add(-Farms.Length - i - 1); }
            }
            for (int i = 0; i < farms.Length; i++)
            {
                float radius = farms[i].yardHalfSize.magnitude + 4;
                if (radius < 35) radius = 35;
                for (int z = Mathf.FloorToInt((farms[i].center.y - radius) / 128); z <= Mathf.FloorToInt((farms[i].center.y + radius) / 128); z++)
                    for (int x = Mathf.FloorToInt((farms[i].center.x - radius) / 128); x <= Mathf.FloorToInt((farms[i].center.x + radius) / 128); x++)
                    { var key = new Vector2Int(x, z); if (!bins.TryGetValue(key, out var list)) bins.Add(key, list = new List<int>()); list.Add(-i - 1); }
            }
        }
        internal bool Blocks(Vector2 p, float margin = 0, bool buildings = true)
        {
            for (int z = Mathf.FloorToInt((p.y - margin) / 128); z <= Mathf.FloorToInt((p.y + margin) / 128); z++)
                for (int x = Mathf.FloorToInt((p.x - margin) / 128); x <= Mathf.FloorToInt((p.x + margin) / 128); x++)
                    if (bins.TryGetValue(new Vector2Int(x, z), out var list))
                        foreach (int i in list)
                        {
                            if (i >= 0) { if (Fields[i].Contains(p, margin) && !Fields[i].ExcludesCrops(p,-margin)) return true; }
                            else
                            {
                                int index = -i - 1;
                                if (index < Farms.Length) { if (buildings) foreach (var building in Farms[index].Buildings) if (building.Contains(p, margin + .35f)) return true; }
                                else if (Tracks[index - Farms.Length].Distance(p) <= 1.8f + margin) return true;
                            }
                        }
            return false;
        }
        internal bool YardsBlockSegment(Vector2 a,Vector2 b,float margin,int? ignoreGroup=null)
        {
            var lo=Vector2.Min(a,b)-Vector2.one*margin;var hi=Vector2.Max(a,b)+Vector2.one*margin;
            for(int y=Mathf.FloorToInt(lo.y/128);y<=Mathf.FloorToInt(hi.y/128);y++)for(int x=Mathf.FloorToInt(lo.x/128);x<=Mathf.FloorToInt(hi.x/128);x++)
                if(bins.TryGetValue(new Vector2Int(x,y),out var list))foreach(int item in list)
                {int farm=-item-1;if(item<0 && farm<Farms.Length && (!ignoreGroup.HasValue||Farms[farm].groupId!=ignoreGroup.Value) && Farms[farm].IntersectsYard(a,b,margin))return true;}
            return false;
        }
        internal void BuildSurfaces(Vector2[] sizes, System.Threading.CancellationToken token)
        {
            var primitives = AgricultureSurfaceData.Describe(Fields, Farms, CombinedRoads != null ? Array.Empty<FarmTrack>() : Tracks);
            Surface = AgricultureSurfaceData.Build(primitives, sizes[0], token);
            if (Fields.Length == 0 && Farms.Length == 0) Resolutions = new[] { 32, 32, 32 };
            Masks = new Color32[3][];
            for (int l = 0; l < 3; l++) Masks[l] = AgricultureRaster.FarMask(primitives, sizes[l], Resolutions[l], token);
        }
    }
}
