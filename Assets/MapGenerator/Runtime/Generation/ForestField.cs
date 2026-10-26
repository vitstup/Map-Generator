using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace TacticalMaps.Generation
{
    // Pure numerical stage. Unity assets are captured by the component before entering a worker.
    internal sealed class ForestField
    {
        internal GameObject[] Prefabs = Array.Empty<GameObject>();
        internal ushort[] Pixels;
        internal int Width, Height, Seed, Limit, Level;
        internal Vector2 Size, ScaleRange, InnerSize;
        internal float SeamClearance;
        internal float VerticalSize, Sea, Coverage, CellSize, MinimumRadius, MaximumRadius, MinimumArea,
            Spacing, SpacingRatio, Slope, Shore, WaterHeight;
        internal readonly List<Bounds> Rocks = new List<Bounds>();
        internal RoadData Roads;
        internal AgricultureData Agriculture;

        internal TreeData Generate(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Pixels == null || Coverage <= 0) return new TreeData(Prefabs, Array.Empty<TreePlacement>(), Array.Empty<bool>(), 0, 0);
            int nx = Mathf.Max(1, Mathf.CeilToInt(Size.x / CellSize)), nz = Mathf.Max(1, Mathf.CeilToInt(Size.y / CellSize));
            if ((long)nx * nz > 4000000) throw new InvalidOperationException("Forest analysis grid is too large; increase Zone Cell Size.");
            float dx = Size.x / nx, dz = Size.y / nz;
            var random = new System.Random(Seed);
            float meanRadius = (MinimumRadius + MaximumRadius) * 0.5f;
            float groveStep = meanRadius * 2.1f;
            var groves = new List<Vector4>();
            int groveColumns = Mathf.CeilToInt(Size.x / groveStep), groveRows = Mathf.CeilToInt(Size.y / groveStep);
            for (int gz = 0; gz < groveRows; gz++)
                for (int gx = 0; gx < groveColumns; gx++)
                {
                    float x = gx * groveStep - Size.x * 0.5f, z = gz * groveStep - Size.y * 0.5f;
                    groves.Add(new Vector4(x + Next(random) * groveStep, z + Next(random) * groveStep,
                        Mathf.Lerp(MinimumRadius, MaximumRadius, Next(random)), Next(random) * Mathf.PI * 2));
                }

            // Ranking an irregular grove field gives an explicit union-area budget, including overlaps.
            var ranked = new List<Cell>();
            var mask = new bool[nx * nz];
            for (int z = 0; z < nz; z++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < nx; x++)
                {
                    float wx = (x + 0.5f) * dx - Size.x * 0.5f, wz = (z + 0.5f) * dz - Size.y * 0.5f;
                    if (!Suitable(wx, wz, Mathf.Max(dx, dz) * 0.71f)) continue;
                    float score = float.NegativeInfinity;
                    int groveX = (int)((wx + Size.x * 0.5f) / groveStep), groveZ = (int)((wz + Size.y * 0.5f) / groveStep);
                    int reach = Mathf.CeilToInt(MaximumRadius * 1.65f / groveStep);
                    for (int gz = Mathf.Max(0, groveZ - reach); gz <= Mathf.Min(groveRows - 1, groveZ + reach); gz++)
                    for (int gx = Mathf.Max(0, groveX - reach); gx <= Mathf.Min(groveColumns - 1, groveX + reach); gx++)
                    {
                        var grove = groves[gz * groveColumns + gx];
                        float rx = wx - grove.x, rz = wz - grove.y;
                        if (Mathf.Abs(rx) > grove.z * 1.65f || Mathf.Abs(rz) > grove.z * 1.65f) continue;
                        float c = Mathf.Cos(grove.w), s = Mathf.Sin(grove.w);
                        float a = (rx * c + rz * s) / (grove.z * 1.3f), b = (-rx * s + rz * c) / (grove.z * 0.85f);
                        score = Mathf.Max(score, 1 - Mathf.Sqrt(a * a + b * b));
                    }
                    score += (Noise(wx / 55, wz / 55) - 0.5f) * 0.38f;
                    if (score > 0) ranked.Add(new Cell(z * nx + x, score));
                }
            }
            ranked.Sort((a, b) => { int order = b.Score.CompareTo(a.Score); return order != 0 ? order : a.Index.CompareTo(b.Index); });
            double ringArea = Size.x * (double)Size.y - InnerSize.x * (double)InnerSize.y;
            int budget = Math.Min(ranked.Count, (int)Math.Floor(Coverage * ringArea / (dx * (double)dz)));
            for (int i = 0; i < budget; i++) mask[ranked[i].Index] = true;
            RemoveSmallZones(mask, nx, nz, Mathf.Max(1, Mathf.CeilToInt(MinimumArea / (dx * dz))), token);

            // A jittered grid plus spatial rejection produces irregular points with a true minimum distance.
            int px = Mathf.Max(1, Mathf.CeilToInt(Size.x / Spacing)), pz = Mathf.Max(1, Mathf.CeilToInt(Size.y / Spacing));
            if ((long)px * pz > 8000000) throw new InvalidOperationException("Too many tree candidates; increase Tree Spacing.");
            var candidates = new List<Vector3>();
            for (int z = 0; z < pz; z++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < px; x++)
                {
                    float wx = (x + 0.1f + Next(random) * 0.8f) * Size.x / px - Size.x * 0.5f;
                    float wz = (z + 0.1f + Next(random) * 0.8f) * Size.y / pz - Size.y * 0.5f;
                    int ix = Mathf.Clamp((int)((wx / Size.x + 0.5f) * nx), 0, nx - 1);
                    int iz = Mathf.Clamp((int)((wz / Size.y + 0.5f) * nz), 0, nz - 1);
                    if (!mask[iz * nx + ix] || !Suitable(wx, wz, 0)) continue;
                    // Small gaps and variable local density keep groves from looking like plantations.
                    if (Next(random) > Mathf.Lerp(0.65f, 0.97f, Noise(wx / 35 + 157, wz / 35))) continue;
                    candidates.Add(new Vector3(wx, Sample(wx, wz), wz));
                }
            }
            for (int i = candidates.Count - 1; i > 0; i--)
            { int j = random.Next(i + 1); var p = candidates[i]; candidates[i] = candidates[j]; candidates[j] = p; }
            float minimumDistance = Spacing * SpacingRatio;
            var points = new List<Vector3>(); var bins = new Dictionary<Vector2Int, List<int>>();
            foreach (var p in candidates)
            {
                token.ThrowIfCancellationRequested();
                var key = Bin(p, Spacing); bool blocked = false;
                for (int z = -1; z <= 1 && !blocked; z++) for (int x = -1; x <= 1 && !blocked; x++)
                    if (bins.TryGetValue(key + new Vector2Int(x, z), out var nearby))
                        foreach (int other in nearby) if (DistanceSquared(p, points[other]) < minimumDistance * minimumDistance) { blocked = true; break; }
                if (blocked) continue;
                if (!bins.TryGetValue(key, out var bucket)) bins[key] = bucket = new List<int>();
                bucket.Add(points.Count); points.Add(p);
                if (points.Count >= Limit) break;
            }

            // Components of actual trees (not just the zone mask) must contain at least three trees.
            var seen = new bool[points.Count]; var output = new List<TreePlacement>(); int group = 0;
            for (int start = 0; start < points.Count; start++)
            {
                token.ThrowIfCancellationRequested();
                if (seen[start]) continue;
                var members = new List<int> { start }; seen[start] = true;
                for (int head = 0; head < members.Count; head++)
                {
                    var p = points[members[head]]; var key = Bin(p, Spacing);
                    for (int z = -3; z <= 3; z++) for (int x = -3; x <= 3; x++)
                        if (bins.TryGetValue(key + new Vector2Int(x, z), out var nearby)) foreach (int other in nearby)
                            if (!seen[other] && DistanceSquared(p, points[other]) <= Spacing * Spacing * 2.2f * 2.2f)
                            { seen[other] = true; members.Add(other); }
                }
                if (members.Count < 3) continue;
                int dominant = random.Next(Prefabs.Length);
                foreach (int member in members)
                {
                    float scale = Mathf.Lerp(ScaleRange.x, ScaleRange.y, Next(random));
                    output.Add(new TreePlacement(points[member], Next(random) < 0.8f ? dominant : random.Next(Prefabs.Length), group,
                        Next(random) * Mathf.PI * 2, scale * Mathf.Lerp(0.9f, 1.1f, Next(random)), scale, Level));
                }
                group++;
            }
            return new TreeData(Prefabs, output.ToArray(), mask, nx, nz);
        }

        private bool Suitable(float x, float z, float cellMargin)
        {
            if (Roads != null && Roads.Blocks(new Vector2(x,z), cellMargin + 1.5f * ScaleRange.y)) return false;
            float edge = Mathf.Max(Shore, cellMargin);
            if (InnerSize.x > 0 && Mathf.Abs(x) < InnerSize.x * 0.5f + SeamClearance + cellMargin &&
                Mathf.Abs(z) < InnerSize.y * 0.5f + SeamClearance + cellMargin) return false;
            if (Mathf.Abs(x) + edge >= Size.x * 0.5f || Mathf.Abs(z) + edge >= Size.y * 0.5f) return false;
            float h = Sample(x, z);
            if (h <= Sea + WaterHeight) return false;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 0.25f;
                if (Sample(x + Mathf.Cos(a) * edge, z + Mathf.Sin(a) * edge) <= Sea + WaterHeight) return false;
            }
            float step = Mathf.Max(1, Mathf.Max(Size.x / (Width - 1), Size.y / (Height - 1)));
            float gx = (Sample(x + step, z) - Sample(x - step, z)) / (2 * step);
            float gz = (Sample(x, z + step) - Sample(x, z - step)) / (2 * step);
            if (Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg > Slope) return false;
            if (Agriculture != null && Agriculture.Blocks(new Vector2(x, z), cellMargin + 2)) return false;
            foreach (var rock in Rocks)
                if (x >= rock.min.x - cellMargin && x <= rock.max.x + cellMargin && z >= rock.min.z - cellMargin && z <= rock.max.z + cellMargin && h < rock.max.y) return false;
            return true;
        }
        private float Sample(float x, float z)
        {
            float u = Mathf.Clamp01(x / Size.x + 0.5f) * (Width - 1), v = Mathf.Clamp01(z / Size.y + 0.5f) * (Height - 1);
            int ix = Mathf.Min((int)u, Width - 2), iz = Mathf.Min((int)v, Height - 2);
            return Mathf.Lerp(Mathf.Lerp(Pixels[iz * Width + ix], Pixels[iz * Width + ix + 1], u - ix),
                Mathf.Lerp(Pixels[(iz + 1) * Width + ix], Pixels[(iz + 1) * Width + ix + 1], u - ix), v - iz) / 65535f * VerticalSize;
        }
        private float Noise(float x, float z)
        {
            int ix = Mathf.FloorToInt(x), iz = Mathf.FloorToInt(z);
            float u = Mathf.SmoothStep(0, 1, x - ix), v = Mathf.SmoothStep(0, 1, z - iz);
            return Mathf.Lerp(Mathf.Lerp(Hash(ix, iz), Hash(ix + 1, iz), u), Mathf.Lerp(Hash(ix, iz + 1), Hash(ix + 1, iz + 1), u), v);
        }
        private float Hash(int x, int z)
        {
            unchecked { uint h = (uint)(x * 374761393 + z * 668265263 + Seed); h = (h ^ (h >> 13)) * 1274126177; return (h ^ (h >> 16)) / (float)uint.MaxValue; }
        }
        private static float Next(System.Random random) => (float)random.NextDouble();
        private static Vector2Int Bin(Vector3 p, float size) => new Vector2Int(Mathf.FloorToInt(p.x / size), Mathf.FloorToInt(p.z / size));
        private static float DistanceSquared(Vector3 a, Vector3 b) => (a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z);
        private readonly struct Cell
        {
            internal readonly int Index; internal readonly float Score;
            internal Cell(int index, float score) { Index = index; Score = score; }
        }
        private static void RemoveSmallZones(bool[] mask, int nx, int nz, int minimum, CancellationToken token)
        {
            var seen = new bool[mask.Length];
            for (int start = 0; start < mask.Length; start++)
            {
                token.ThrowIfCancellationRequested();
                if (!mask[start] || seen[start]) continue;
                var members = new List<int> { start }; seen[start] = true;
                for (int head = 0; head < members.Count; head++)
                {
                    int cell = members[head], x = cell % nx, z = cell / nx;
                    Visit(x - 1, z); Visit(x + 1, z); Visit(x, z - 1); Visit(x, z + 1);
                }
                if (members.Count < minimum) foreach (int cell in members) mask[cell] = false;
                void Visit(int x, int z)
                {
                    if (x < 0 || z < 0 || x >= nx || z >= nz) return;
                    int cell = z * nx + x;
                    if (seen[cell] || !mask[cell]) return;
                    seen[cell] = true; members.Add(cell);
                }
            }
        }
    }
}
