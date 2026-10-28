using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace TacticalMaps.Generation
{
    internal struct FarmSurfacePrimitive
    {
        internal Vector4 Shape, Style, Detail, Exclusion;
        internal Vector2 Center => new Vector2(Shape.x, Shape.y);
        internal Vector2 HalfSize => new Vector2(Shape.z, Shape.w);
        internal float Distance(Vector2 p)
        {
            float dx = p.x - Shape.x, dz = p.y - Shape.y, r = Detail.x;
            float qx = Mathf.Abs(dx * Style.x + dz * Style.y) - Shape.z + r, qz = Mathf.Abs(-dx * Style.y + dz * Style.x) - Shape.w + r;
            float ox = Mathf.Max(qx, 0), oz = Mathf.Max(qz, 0);
            float distance=Mathf.Sqrt(ox*ox+oz*oz)+Mathf.Min(Mathf.Max(qx,qz),0)-r;
            if(Exclusion.z>0 && Exclusion.w>0)
            {
                float ex=Mathf.Abs(dx*Style.x+dz*Style.y-Exclusion.x)-Exclusion.z;
                float ez=Mathf.Abs(-dx*Style.y+dz*Style.x-Exclusion.y)-Exclusion.w;
                float hole=Mathf.Sqrt(Mathf.Max(ex,0)*Mathf.Max(ex,0)+Mathf.Max(ez,0)*Mathf.Max(ez,0))+Mathf.Min(Mathf.Max(ex,ez),0);
                distance=Mathf.Max(distance,-hole);
            }
            return distance;
        }
        internal Bounds Bounds(float margin)
        {
            var e = new Vector2(Mathf.Abs(Style.x) * Shape.z + Mathf.Abs(Style.y) * Shape.w, Mathf.Abs(Style.y) * Shape.z + Mathf.Abs(Style.x) * Shape.w) + Vector2.one * margin;
            return new Bounds(new Vector3(Shape.x, 0, Shape.y), new Vector3(e.x * 2, 1, e.y * 2));
        }
    }
    // Distance and categorical metadata are separate. The distance is bilinearly filtered;
    // a nearest ID addresses exact full-precision parcel parameters, never blended angles/types.
    internal sealed class AgricultureSurfaceData
    {
        internal const int TileSize = 128, TilePixels = 130;
        internal Vector2 Origin;
        internal int Columns, Rows;
        internal FarmSurfacePrimitive[] Primitives;
        internal int[] Keys;
        internal Color32[][] Tiles;
        internal static float DecodeDistance(Color32 pixel) => (pixel.r * 256f + pixel.g) * (64f / 65535f) - 32;
        internal static FarmSurfacePrimitive[] Describe(FarmField[] fields, Farmstead[] farms, FarmTrack[] tracks)
        {
            var list = new List<FarmSurfacePrimitive>();
            foreach (var f in fields) list.Add(new FarmSurfacePrimitive { Shape = new Vector4(f.center.x, f.center.y, f.halfSize.x, f.halfSize.y), Style = new Vector4(Mathf.Cos(f.angle), Mathf.Sin(f.angle), f.crop, f.variation), Detail = new Vector4(f.CornerRadius, 0, 0, 0), Exclusion=f.cropExclusion });
            foreach (var track in tracks)
            {
                int n = Mathf.CeilToInt(Vector2.Distance(track.a, track.b) / 10); var start = track.a;
                for (int i = 1; i <= n; i++)
                {
                    var end = track.Point((float)i / n); var direction = (end - start).normalized; var p = (end + start) / 2;
                    float width = 1.55f + .18f * Mathf.Sin(p.x * .017f + p.y * .023f);
                    list.Add(new FarmSurfacePrimitive { Shape = new Vector4(p.x, p.y, Vector2.Distance(start, end) / 2 + width, width), Style = new Vector4(direction.x, direction.y, 5, .65f), Detail = new Vector4(width, 0, 0, 0) }); start = end;
                }
            }
            return list.ToArray();
        }
        internal static AgricultureSurfaceData Build(FarmSurfacePrimitive[] primitives, Vector2 mainSize, CancellationToken token)
        {
            var result = new AgricultureSurfaceData { Primitives = primitives };
            result.Origin = new Vector2(Mathf.Floor((-mainSize.x / 2 - 600) / TileSize) * TileSize, Mathf.Floor((-mainSize.y / 2 - 600) / TileSize) * TileSize);
            result.Columns = Mathf.CeilToInt((mainSize.x / 2 + 600 - result.Origin.x) / TileSize); result.Rows = Mathf.CeilToInt((mainSize.y / 2 + 600 - result.Origin.y) / TileSize);
            var bins = new SortedDictionary<int, List<int>>();
            for (int i = 0; i < primitives.Length; i++)
            {
                var b = primitives[i].Bounds(12);
                int x0 = Mathf.Max(0, Mathf.FloorToInt((b.min.x - result.Origin.x) / TileSize)), x1 = Mathf.Min(result.Columns - 1, Mathf.FloorToInt((b.max.x - result.Origin.x) / TileSize));
                int z0 = Mathf.Max(0, Mathf.FloorToInt((b.min.z - result.Origin.y) / TileSize)), z1 = Mathf.Min(result.Rows - 1, Mathf.FloorToInt((b.max.z - result.Origin.y) / TileSize));
                for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) { int key = z * result.Columns + x; if (!bins.TryGetValue(key, out var list)) bins.Add(key, list = new List<int>()); list.Add(i); }
            }
            if (bins.Count > 2048) throw new InvalidOperationException("Agriculture detail tile budget exceeded.");
            result.Keys = new int[bins.Count]; var candidates = new List<int>[bins.Count]; int at = 0;
            foreach (var pair in bins) { result.Keys[at] = pair.Key; candidates[at++] = pair.Value; }
            if (primitives.Length >= 65535) throw new InvalidOperationException("Agriculture profile index budget exceeded.");
            result.Tiles = new Color32[bins.Count][];
            Parallel.For(0, bins.Count, new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, tile =>
            {
                int key = result.Keys[tile]; float ox = result.Origin.x + key % result.Columns * TileSize, oz = result.Origin.y + key / result.Columns * TileSize;
                var pixels = new Color32[TilePixels * TilePixels];
                for (int z = 0; z < TilePixels; z++)
                {
                    token.ThrowIfCancellationRequested();
                    for (int x = 0; x < TilePixels; x++)
                    {
                        var p = new Vector2(ox + x - .5f, oz + z - .5f); float best = 32; int id = 0;
                        var list = candidates[tile];
                        for (int i = 0; i < list.Count; i++) { int index = list[i]; float d = primitives[index].Distance(p); if (d < best) { best = d; id = index + 1; } if (best <= -32) break; }
                        int distance = Mathf.Clamp(Mathf.RoundToInt((best + 32) * (65535f / 64)), 0, 65535);
                        pixels[z * TilePixels + x] = new Color32((byte)(distance >> 8), (byte)distance, (byte)(id >> 8), (byte)id);
                    }
                }
                result.Tiles[tile] = pixels;
            });
            return result;
        }
    }
    internal sealed partial class AgricultureRaster
    {
        internal static Color ColorFor(FarmSurfacePrimitive p)
        {
            float v = p.Style.w; int crop = Mathf.RoundToInt(p.Style.z);
            Color soil = Color.Lerp(new Color(.29f, .225f, .145f).linear, new Color(.4f, .31f, .2f).linear, v);
            return soil;
        }
        internal static Color32[] FarMask(FarmSurfacePrimitive[] primitives, Vector2 size, int n, CancellationToken token)
        {
            var colors = new Color32[n * n]; float pixel = Mathf.Max(size.x, size.y) / n;
            foreach (var primitive in primitives)
            {
                token.ThrowIfCancellationRequested(); var b = primitive.Bounds(pixel); var color = ColorFor(primitive);
                int x0 = Mathf.Clamp(Mathf.FloorToInt((b.min.x / size.x + .5f) * n), 0, n - 1), x1 = Mathf.Clamp(Mathf.CeilToInt((b.max.x / size.x + .5f) * n), 0, n - 1);
                int z0 = Mathf.Clamp(Mathf.FloorToInt((b.min.z / size.y + .5f) * n), 0, n - 1), z1 = Mathf.Clamp(Mathf.CeilToInt((b.max.z / size.y + .5f) * n), 0, n - 1);
                for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2(((x + .5f) / n - .5f) * size.x, ((z + .5f) / n - .5f) * size.y);
                    float amount = Mathf.Clamp01(.5f - primitive.Distance(p) / pixel);
                    if (amount <= colors[z * n + x].a / 255f) continue;
                    colors[z * n + x] = new Color(color.r * amount, color.g * amount, color.b * amount, amount);
                }
            }
            return colors;
        }
    }
}
