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
        // 0 fallow, 1 green rice, 2 ripe rice, 3 flooded young rice.
        public int crop;
        internal Vector2 Axis => new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        internal Vector2 Point(float x, float z) { var a = Axis; return center + a * x + new Vector2(-a.y, a.x) * z; }
        internal bool Contains(Vector2 p, float margin = 0)
        {
            return SignedDistance(p) <= margin;
        }
        internal float CornerRadius => Mathf.Min(3, Mathf.Max(2.2f, Mathf.Min(halfSize.x, halfSize.y) * .12f));
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
        public int variant;
        public float houseHeight, storeHeight;
        public Vector2 halfFootprint;
    }
    [Serializable]
    internal struct FarmTrack
    {
        public Vector2 a, b, controlA, controlB;
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
                for (int z = Mathf.FloorToInt((farms[i].center.y - 34) / 128); z <= Mathf.FloorToInt((farms[i].center.y + 34) / 128); z++)
                    for (int x = Mathf.FloorToInt((farms[i].center.x - 34) / 128); x <= Mathf.FloorToInt((farms[i].center.x + 34) / 128); x++)
                    {
                        var key = new Vector2Int(x, z); if (!bins.TryGetValue(key, out var list)) bins.Add(key, list = new List<int>()); list.Add(-i - 1);
                    }
        }
        internal bool Blocks(Vector2 p, float margin = 0)
        {
            for (int z = Mathf.FloorToInt((p.y - margin) / 128); z <= Mathf.FloorToInt((p.y + margin) / 128); z++)
                for (int x = Mathf.FloorToInt((p.x - margin) / 128); x <= Mathf.FloorToInt((p.x + margin) / 128); x++)
                    if (bins.TryGetValue(new Vector2Int(x, z), out var list))
                        foreach (int i in list)
                        {
                            if (i >= 0) { if (Fields[i].Contains(p, margin)) return true; }
                            else
                            {
                                int index = -i - 1;
                                if (index < Farms.Length) { if ((p - Farms[index].center).sqrMagnitude < (32 + margin) * (32 + margin)) return true; }
                                else if (Tracks[index - Farms.Length].Distance(p) <= 1.8f + margin) return true;
                            }
                        }
            return false;
        }
        internal void BuildSurfaces(Vector2[] sizes, System.Threading.CancellationToken token)
        {
            var primitives = AgricultureSurfaceData.Describe(Fields, Farms, Tracks);
            Surface = AgricultureSurfaceData.Build(primitives, sizes[0], token);
            if (Fields.Length == 0 && Farms.Length == 0) Resolutions = new[] { 32, 32, 32 };
            Masks = new Color32[3][];
            for (int l = 0; l < 3; l++) Masks[l] = AgricultureRaster.FarMask(primitives, sizes[l], Resolutions[l], token);
        }
    }
}
