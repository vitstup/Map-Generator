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
            var a = Axis; var d = p - center;
            return Mathf.Abs(Vector2.Dot(d, a)) <= halfSize.x + margin && Mathf.Abs(Vector2.Dot(d, new Vector2(-a.y, a.x))) <= halfSize.y + margin;
        }
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
    }
    [Serializable]
    internal struct FarmTrack { public Vector2 a, b; }
    internal sealed class AgricultureData
    {
        internal readonly FarmField[] Fields;
        internal readonly Farmstead[] Farms;
        internal readonly FarmTrack[] Tracks;
        internal readonly Color32[][] Masks;
        internal readonly int[] Resolutions;
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
                var lo = Vector2.Min(Tracks[i].a, Tracks[i].b) - Vector2.one * 2; var hi = Vector2.Max(Tracks[i].a, Tracks[i].b) + Vector2.one * 2;
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
                                else { var track = Tracks[index - Farms.Length]; if (RoadData.Distance(p, track.a, track.b) <= 1.8f + margin) return true; }
                            }
                        }
            return false;
        }
    }
}
