using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace TacticalMaps.Generation
{
    [DisallowMultipleComponent]
    internal sealed class AgricultureGenerator : MonoBehaviour
    {
        [SerializeField] private bool generateAgriculture = true;
        [SerializeField] private Texture2D densitySource;
        [SerializeField, Range(0, 1)] private float coverage = .72f;
        [SerializeField, Min(120)] private float districtSize = 300;
        [SerializeField, Range(0, 20)] private float maximumSlope = 9;
        [SerializeField, Range(0, 1)] private float farmProbability = .32f;
        [SerializeField, Min(1)] private int maximumFields = 16000;
        [SerializeField] private int seed = 1467;
        private Texture2D cachedSource;
        private ushort[] cachedDensity;
        private Hash128 cachedHash;

        internal AgricultureData Generate(MapData map, int radius, CancellationToken token) => Capture(map, radius)?.Render(token);
        internal async Task<AgricultureData> GenerateAsync(MapData map, int radius, CancellationToken token)
        {
            var input = Capture(map, radius);
            if (input == null) return null;
            return await Task.Run(() => input.Render(token), token);
        }
        private AgricultureInput Capture(MapData map, int radius)
        {
            if (!generateAgriculture) return null;
            if (densitySource == null || !densitySource.isReadable || densitySource.format != TextureFormat.R16)
                throw new InvalidOperationException("Agriculture requires a readable linear R16 density map.");
            if (districtSize < 120 || maximumFields < 1 || coverage < 0 || coverage > 1 || maximumSlope < 0 || maximumSlope > 20 || farmProbability < 0 || farmProbability > 1)
                throw new InvalidOperationException("Invalid agriculture settings.");
            if (cachedSource != densitySource || cachedDensity == null || cachedHash != densitySource.imageContentsHash)
            { cachedDensity = densitySource.GetPixelData<ushort>(0).ToArray(); cachedSource = densitySource; cachedHash = densitySource.imageContentsHash; }
            var input = new AgricultureInput { Density = cachedDensity, SourceWidth = densitySource.width, SourceHeight = densitySource.height,
                SourcePoint = map.SourcePoint, Radius = radius, Height = map.TerrainHeightMeters, Sea = map.SeaLevelMeters,
                Coverage = coverage, District = districtSize, Slope = maximumSlope, FarmProbability = farmProbability, Limit = maximumFields, Seed = seed, Roads = map.Roads };
            for (int i = 0; i < 3; i++)
            { input.Pixels[i] = map.Heightmaps[i].GetPixelData<ushort>(0).ToArray(); input.Widths[i] = map.Heightmaps[i].width; input.Heights[i] = map.Heightmaps[i].height; input.Sizes[i] = map.Sizes[i]; }
            return input;
        }
    }

    // Captured on the main thread; all planning/raster work below uses managed arrays only.
    internal sealed class AgricultureInput
    {
        internal ushort[] Density;
        internal int SourceWidth, SourceHeight, Radius, Limit, Seed;
        internal Vector2Int SourcePoint;
        internal readonly ushort[][] Pixels = new ushort[3][];
        internal readonly int[] Widths = new int[3], Heights = new int[3];
        internal readonly Vector2[] Sizes = new Vector2[3];
        internal float Height, Sea, Coverage, District, Slope, FarmProbability;
        internal RoadData Roads;
        internal float Sample(Vector2 p)
        {
            int l = Mathf.Abs(p.x) < Sizes[0].x / 2 && Mathf.Abs(p.y) < Sizes[0].y / 2 ? 0 : Mathf.Abs(p.x) < Sizes[1].x / 2 && Mathf.Abs(p.y) < Sizes[1].y / 2 ? 1 : 2;
            return Bilinear(Pixels[l], Widths[l], Heights[l], p.x / Sizes[l].x + .5f, p.y / Sizes[l].y + .5f) * Height;
        }
        private float DensityAt(Vector2 p)
        {
            float x = SourcePoint.x + p.x / Sizes[2].x * (2 * Radius), y = SourcePoint.y + p.y / Sizes[2].y * (2 * Radius);
            if (x < 0 || y < 0 || x >= SourceWidth || y >= SourceHeight) return 0;
            return Bilinear(Density, SourceWidth, SourceHeight, x / (SourceWidth - 1), y / (SourceHeight - 1));
        }
        private static float Bilinear(ushort[] data, int w, int h, float u, float v)
        {
            float x = Mathf.Clamp01(u) * (w - 1), y = Mathf.Clamp01(v) * (h - 1);
            int ix = Mathf.Min((int)x, w - 2), iy = Mathf.Min((int)y, h - 2);
            return Mathf.Lerp(Mathf.Lerp(data[iy * w + ix], data[iy * w + ix + 1], x - ix), Mathf.Lerp(data[(iy + 1) * w + ix], data[(iy + 1) * w + ix + 1], x - ix), y - iy) / 65535f;
        }
        private float Hash(int x, int z, int salt)
        {
            unchecked { uint h = (uint)(x * 73856093 ^ z * 19349663 ^ Seed * 83492791 ^ salt * 1640531513); h ^= h >> 16; h *= 0x7feb352d; h ^= h >> 15; h *= 0x846ca68b; h ^= h >> 16; return (h & 0xffffff) / 16777216f; }
        }
        internal AgricultureData Render(CancellationToken token)
        {
            var fields = new List<FarmField>(); var farms = new List<Farmstead>();
            // The source-to-world transform anchors districts independently of the selected source point.
            Vector2 origin = new Vector2(SourcePoint.x * Sizes[2].x / (2 * Radius), SourcePoint.y * Sizes[2].y / (2 * Radius));
            int x0 = Mathf.FloorToInt((origin.x - Sizes[2].x / 2) / District), x1 = Mathf.CeilToInt((origin.x + Sizes[2].x / 2) / District);
            int z0 = Mathf.FloorToInt((origin.y - Sizes[2].y / 2) / District), z1 = Mathf.CeilToInt((origin.y + Sizes[2].y / 2) / District);
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
            {
                token.ThrowIfCancellationRequested();
                var center = new Vector2((x + .5f) * District, (z + .5f) * District) - origin;
                float angle = (Hash(x, z, 1) - .5f) * .7f;
                var axis = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)); var side = new Vector2(-axis.y, axis.x);
                float step = District * .245f;
                int accepted = 0;
                for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++)
                {
                    int salt = (j + 1) * 3 + i + 1;
                    var p = center + axis * (i * step) + side * (j * step);
                    if (Mathf.Abs(p.x) + step > Sizes[2].x / 2 || Mathf.Abs(p.y) + step > Sizes[2].y / 2) continue;
                    float density = DensityAt(p);
                    if (Hash(x, z, 30 + salt) > density * Coverage) continue;
                    var f = new FarmField { center = p, halfSize = new Vector2(step * Mathf.Lerp(.34f, .46f, Hash(x, z, 50 + salt)), step * Mathf.Lerp(.32f, .46f, Hash(x, z, 70 + salt))), angle = angle,
                        variation = Hash(x, z, 90 + salt), crop = Hash(x, z, 110 + salt) < .16f ? 0 : Hash(x, z, 120 + salt) < .55f ? 1 : 2 };
                    float lo = float.MaxValue, hi = float.MinValue; bool valid = true;
                    for (int b = -2; b <= 2 && valid; b++) for (int a = -2; a <= 2; a++)
                    {
                        var q = f.Point(a * f.halfSize.x / 2, b * f.halfSize.y / 2);
                        float h = Sample(q); lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                        float gx = (Sample(q + Vector2.right * 3) - Sample(q - Vector2.right * 3)) / 6;
                        float gz = (Sample(q + Vector2.up * 3) - Sample(q - Vector2.up * 3)) / 6;
                        if (h < Sea + 1.2f || Mathf.Sqrt(gx * gx + gz * gz) > Mathf.Tan(Slope * Mathf.Deg2Rad) || Roads != null && Roads.Blocks(q, 5)) { valid = false; break; }
                    }
                    // Use a conservative enclosing disk for road exclusion, including the unsampled interior.
                    if (!valid || Roads != null && Roads.Blocks(p, f.halfSize.magnitude + 2)) continue;
                    f.height = lo;
                    if (f.crop != 0 && hi - lo < .22f) f.crop = 3;
                    if (fields.Count >= Limit) throw new InvalidOperationException("Agriculture field limit exceeded; increase district size or lower coverage.");
                    fields.Add(f); accepted++;
                }
                if (accepted < 2 || Hash(x, z, 200) > FarmProbability) continue;
                float best = float.MaxValue, homeAngle = angle; Vector2 home = default; bool found = false;
                for (int k = 0; k < 4; k++)
                {
                    var direction = k == 0 ? axis : k == 1 ? -axis : k == 2 ? side : -side;
                    var candidate = center + direction * (step * 1.65f);
                    if (Mathf.Abs(candidate.x) + 24 >= Sizes[2].x / 2 || Mathf.Abs(candidate.y) + 24 >= Sizes[2].y / 2 || Sample(candidate) < Sea + 2 || Roads != null && Roads.Blocks(candidate, 24)) continue;
                    bool occupied = false;
                    for (int f = fields.Count - accepted; f < fields.Count; f++) if (fields[f].Contains(candidate, 11)) { occupied = true; break; }
                    foreach (var farm in farms) if ((farm.center - candidate).sqrMagnitude < 60 * 60) { occupied = true; break; }
                    if (occupied) continue;
                    float hmin = float.MaxValue, hmax = float.MinValue;
                    for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++) { float h = Sample(candidate + new Vector2(i * 20, j * 20)); hmin = Mathf.Min(hmin, h); hmax = Mathf.Max(hmax, h); }
                    if (hmin <= Sea + 1.2f || hmax - hmin > 2.8f) continue;
                    float distance = District * 8 + Hash(x, z, 201 + k) * District;
                    if (Roads != null) foreach (var road in Roads.Segments) distance = Mathf.Min(distance, RoadData.Distance(candidate, road.A, road.B));
                    if (distance < best) { best = distance; home = candidate; homeAngle = Mathf.Atan2(direction.y, direction.x); found = true; }
                }
                if (found) farms.Add(new Farmstead { center = home, angle = homeAngle, variant = (int)(Hash(x, z, 202) * 5) });
            }
            var tracks = new List<FarmTrack>();
            var fieldIndex = Roads != null ? new AgricultureData(fields.ToArray(), Array.Empty<Farmstead>(), null, null) : null;
            if (Roads != null)
                foreach (var farm in farms)
                {
                    token.ThrowIfCancellationRequested(); float nearest = 220; Vector2 target = default;
                    foreach (var road in Roads.Segments)
                    {
                        var ab = road.B - road.A; var point = road.A + ab * Mathf.Clamp01(Vector2.Dot(farm.center - road.A, ab) / Mathf.Max(.001f, ab.sqrMagnitude));
                        float distance = Vector2.Distance(farm.center, point); if (distance < nearest) { nearest = distance; target = point; }
                    }
                    if (nearest >= 220 || nearest < 15) continue;
                    int steps = Mathf.CeilToInt(nearest / 5); bool valid = true; float previous = Sample(farm.center);
                    for (int s = 0; s <= steps && valid; s++)
                    {
                        var p = Vector2.Lerp(farm.center, target, (float)s / steps); float height = Sample(p);
                        if (height <= Sea + .5f || Mathf.Abs(height - previous) > 1.1f) { valid = false; break; } previous = height;
                        if (fieldIndex.Blocks(p, 3)) valid = false;
                    }
                    if (valid) tracks.Add(new FarmTrack { a = farm.center, b = target });
                }
            int[] resolutions = { 2048, 2048, 2048 }; var masks = new Color32[3][];
            for (int l = 0; l < 3; l++)
            {
                int n = resolutions[l]; masks[l] = new Color32[n * n];
                foreach (var f in fields)
                {
                    token.ThrowIfCancellationRequested(); var bounds = f.Bounds(3);
                    int xa = Mathf.Clamp(Mathf.FloorToInt((bounds.min.x / Sizes[l].x + .5f) * n), 0, n - 1), xb = Mathf.Clamp(Mathf.CeilToInt((bounds.max.x / Sizes[l].x + .5f) * n), 0, n - 1);
                    int za = Mathf.Clamp(Mathf.FloorToInt((bounds.min.z / Sizes[l].y + .5f) * n), 0, n - 1), zb = Mathf.Clamp(Mathf.CeilToInt((bounds.max.z / Sizes[l].y + .5f) * n), 0, n - 1);
                    for (int b = za; b <= zb; b++) for (int a = xa; a <= xb; a++)
                    {
                        var p = new Vector2(((a + .5f) / n - .5f) * Sizes[l].x, ((b + .5f) / n - .5f) * Sizes[l].y);
                        if (!f.Contains(p, 1.5f)) continue;
                        masks[l][b * n + a] = new Color32(255, (byte)(Mathf.Repeat(f.angle / (Mathf.PI * 2), 1) * 255), (byte)(f.crop * 64), (byte)(f.variation * 255));
                    }
                }
                foreach (var farm in farms)
                {
                    token.ThrowIfCancellationRequested();
                    int xa = Mathf.Clamp(Mathf.FloorToInt(((farm.center.x - 34) / Sizes[l].x + .5f) * n), 0, n - 1), xb = Mathf.Clamp(Mathf.CeilToInt(((farm.center.x + 34) / Sizes[l].x + .5f) * n), 0, n - 1);
                    int za = Mathf.Clamp(Mathf.FloorToInt(((farm.center.y - 34) / Sizes[l].y + .5f) * n), 0, n - 1), zb = Mathf.Clamp(Mathf.CeilToInt(((farm.center.y + 34) / Sizes[l].y + .5f) * n), 0, n - 1);
                    var axis = new Vector2(Mathf.Cos(farm.angle), Mathf.Sin(farm.angle)); var side = new Vector2(-axis.y, axis.x);
                    for (int b = za; b <= zb; b++) for (int a = xa; a <= xb; a++)
                    {
                        var p = new Vector2(((a + .5f) / n - .5f) * Sizes[l].x, ((b + .5f) / n - .5f) * Sizes[l].y);
                        var d = p - farm.center - axis * 8;
                        var q = new Vector2(Mathf.Abs(Vector2.Dot(d, axis)) - 20, Mathf.Abs(Vector2.Dot(d, side)) - 12);
                        float distance = Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0) - 3;
                        float amount = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-.5f, 1.5f, distance));
                        if (amount > 0 && masks[l][b * n + a].r == 0) masks[l][b * n + a] = new Color32((byte)(amount * 255), 0, 255, 100);
                    }
                }
                foreach (var track in tracks)
                {
                    token.ThrowIfCancellationRequested(); var lo = Vector2.Min(track.a, track.b) - Vector2.one * 3; var hi = Vector2.Max(track.a, track.b) + Vector2.one * 3;
                    int xa = Mathf.Clamp(Mathf.FloorToInt((lo.x / Sizes[l].x + .5f) * n), 0, n - 1), xb = Mathf.Clamp(Mathf.CeilToInt((hi.x / Sizes[l].x + .5f) * n), 0, n - 1);
                    int za = Mathf.Clamp(Mathf.FloorToInt((lo.y / Sizes[l].y + .5f) * n), 0, n - 1), zb = Mathf.Clamp(Mathf.CeilToInt((hi.y / Sizes[l].y + .5f) * n), 0, n - 1);
                    for (int b = za; b <= zb; b++) for (int a = xa; a <= xb; a++)
                    {
                        var p = new Vector2(((a + .5f) / n - .5f) * Sizes[l].x, ((b + .5f) / n - .5f) * Sizes[l].y);
                        float amount = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1, 2, RoadData.Distance(p, track.a, track.b)));
                        if (amount * 255 > masks[l][b * n + a].r) masks[l][b * n + a] = new Color32((byte)(amount * 255), 0, 255, 140);
                    }
                }
            }
            return new AgricultureData(fields.ToArray(), farms.ToArray(), masks, resolutions, tracks.ToArray());
        }
    }
}
