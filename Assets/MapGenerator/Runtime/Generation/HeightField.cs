using System;
using System.Threading;
using UnityEngine;

namespace TacticalMaps.Generation
{
    // Pure CPU data: no Unity objects are accessed by the background worker.
    internal sealed class HeightField
    {
        internal int Side => side;
        internal float Compression => compression;
        private readonly int side;
        private readonly float[] original, playable, seaBaseline;
        private readonly MapSettings settings;
        private readonly float compression;

        internal HeightField(float[] sourceHeights, byte[] land, int width, int height, Vector2Int point, MapSettings settings)
        {
            settings.Validate(width, height);
            this.settings = settings.Copy();
            side = settings.SourceRadius * 2 + 1;
            original = new float[side * side];
            var mask = new bool[original.Length];
            for (int y = 0; y < side; y++)
                for (int x = 0; x < side; x++)
                {
                    int sx = point.x + x - settings.SourceRadius;
                    int sy = point.y + y - settings.SourceRadius;
                    int p = y * side + x;
                    // Outside the source image is water; the selection never silently shifts.
                    if (sx < 0 || sy < 0 || sx >= width || sy >= height) continue;
                    int src = sy * width + sx;
                    mask[p] = land[src] >= 128;
                    // Source values encode a fraction of the land range above sea level.
                    original[p] = mask[p] ? Mathf.Clamp01(sourceHeights[src]) * (1 - settings.SeaLevelMeters / settings.TerrainHeightMeters) : 0;
                }
            float[] shoreDistance = DistanceToWater(mask, side);
            var inverseMask = new bool[mask.Length];
            for (int i = 0; i < mask.Length; i++) inverseMask[i] = !mask[i];
            float[] waterDistance = DistanceToWater(inverseMask, side);
            playable = Blur(original, side, settings.SmoothingPixels);
            seaBaseline = new float[original.Length];
            Vector2 outer = settings.Levels[2].Size;
            Vector2 inner = settings.Levels[0].Size;
            float dx = outer.x / (side - 1), dy = outer.y / (side - 1);
            float sea = settings.SeaLevelMeters / settings.TerrainHeightMeters;
            float slopeLimit = Mathf.Tan(settings.MaxSlope * Mathf.Deg2Rad);
            // A sufficiently wide coastal baseline leaves slope budget for the land relief.
            float playableCoast = settings.CoastPixels;
            float seabedWidth = Mathf.Max(settings.SeabedPixels, 4 * settings.SeaLevelMeters / (slopeLimit * Mathf.Min(dx, dy)));
            for (int i = 0; i < original.Length; i++)
            {
                float coast = Smooth(Mathf.Clamp01((shoreDistance[i] - 0.5f) / settings.CoastPixels));
                float waterDepth = mask[i] ? 0 : Smooth(Mathf.Clamp01((waterDistance[i] - 0.5f) / seabedWidth));
                // The same seabed reference is used by all levels, including their transition.
                seaBaseline[i] = sea * (1 - waterDepth);
                original[i] = original[i] * coast + seaBaseline[i];
                float playableWeight = Smooth(Mathf.Clamp01((shoreDistance[i] - 0.5f) / playableCoast));
                playable[i] *= playableWeight;
            }
            float maxGradient = 0, maxSeaGradient = 0;
            // Bilinear interpolation has bounded derivatives in each source cell.
            // Include every cell intersecting the main terrain, including narrow selections.
            for (int y = 0; y < side - 1; y++)
                for (int x = 0; x < side - 1; x++)
                {
                    float wx = x * dx - outer.x * 0.5f, wy = y * dy - outer.y * 0.5f;
                    if (wx > inner.x * 0.5f || wx + dx < -inner.x * 0.5f ||
                        wy > inner.y * 0.5f || wy + dy < -inner.y * 0.5f) continue;
                    int p = y * side + x;
                    float gx = Mathf.Max(Mathf.Abs(playable[p + 1] - playable[p]), Mathf.Abs(playable[p + side + 1] - playable[p + side])) / dx;
                    float gy = Mathf.Max(Mathf.Abs(playable[p + side] - playable[p]), Mathf.Abs(playable[p + side + 1] - playable[p + 1])) / dy;
                    maxGradient = Mathf.Max(maxGradient, Mathf.Sqrt(gx * gx + gy * gy) * settings.TerrainHeightMeters);
                    float sx = Mathf.Max(Mathf.Abs(seaBaseline[p + 1] - seaBaseline[p]), Mathf.Abs(seaBaseline[p + side + 1] - seaBaseline[p + side])) / dx;
                    float sy = Mathf.Max(Mathf.Abs(seaBaseline[p + side] - seaBaseline[p]), Mathf.Abs(seaBaseline[p + side + 1] - seaBaseline[p + 1])) / dy;
                    maxSeaGradient = Mathf.Max(maxSeaGradient, Mathf.Sqrt(sx * sx + sy * sy) * settings.TerrainHeightMeters);
                }
            compression = Mathf.Min(1, Mathf.Max(0, slopeLimit - maxSeaGradient) / Mathf.Max(0.000001f, maxGradient));
        }

        internal float Sample(float worldX, float worldY)
        {
            var inner = settings.Levels[0].Size;
            var middle = settings.Levels[1].Size;
            var outer = settings.Levels[2].Size;
            float u = worldX / outer.x + 0.5f, v = worldY / outer.y + 0.5f;
            float distance = Mathf.Max(Mathf.Abs(worldX) - inner.x * 0.5f, Mathf.Abs(worldY) - inner.y * 0.5f);
            // Finish the transition before reaching the middle terrain boundary.
            float blendWidth = Mathf.Min(settings.TransitionMeters, Mathf.Min(middle.x - inner.x, middle.y - inner.y) * 0.45f);
            float blend = Smooth(Mathf.Clamp01(distance / Mathf.Max(0.01f, blendWidth)));
            return Mathf.Lerp(Bilinear(seaBaseline, side, u, v) + Bilinear(playable, side, u, v) * compression, Bilinear(original, side, u, v), blend);
        }

        internal ushort[] Render(int level, CancellationToken cancellation)
        {
            MapLevel l = settings.Levels[level];
            var pixels = new ushort[l.Resolution.x * l.Resolution.y];
            for (int y = 0; y < l.Resolution.y; y++)
            {
                cancellation.ThrowIfCancellationRequested();
                float wy = ((float)y / (l.Resolution.y - 1) - 0.5f) * l.Size.y;
                for (int x = 0; x < l.Resolution.x; x++)
                {
                    float wx = ((float)x / (l.Resolution.x - 1) - 0.5f) * l.Size.x;
                    pixels[y * l.Resolution.x + x] = (ushort)Mathf.RoundToInt(Mathf.Clamp01(Sample(wx, wy)) * 65535);
                }
            }
            return pixels;
        }

        private static float Bilinear(float[] data, int side, float u, float v)
        {
            float px = Mathf.Clamp01(u) * (side - 1), py = Mathf.Clamp01(v) * (side - 1);
            int x = Mathf.Min((int)px, side - 2), y = Mathf.Min((int)py, side - 2);
            int p = y * side + x;
            return Mathf.Lerp(Mathf.Lerp(data[p], data[p + 1], px - x), Mathf.Lerp(data[p + side], data[p + side + 1], px - x), py - y);
        }

        private static float Smooth(float t) => t * t * t * (t * (t * 6 - 15) + 10);

        private static float[] Blur(float[] data, int n, float sigma)
        {
            int radius = Mathf.CeilToInt(sigma * 3);
            float[] weights = new float[radius * 2 + 1];
            float sum = 0;
            for (int k = -radius; k <= radius; k++) { weights[k + radius] = Mathf.Exp(-k * k / (2 * sigma * sigma)); sum += weights[k + radius]; }
            for (int k = 0; k < weights.Length; k++) weights[k] /= sum;
            var temp = new float[data.Length]; var result = new float[data.Length];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                for (int k = -radius; k <= radius; k++) temp[y * n + x] += data[y * n + Mathf.Clamp(x + k, 0, n - 1)] * weights[k + radius];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                for (int k = -radius; k <= radius; k++) result[y * n + x] += temp[Mathf.Clamp(y + k, 0, n - 1) * n + x] * weights[k + radius];
            return result;
        }

        private static float[] DistanceToWater(bool[] mask, int n)
        {
            var d = new float[mask.Length];
            for (int i = 0; i < d.Length; i++) d[i] = mask[i] ? 1000000 : 0;
            const float diagonal = 1.41421356f;
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                int p = y * n + x;
                if (x > 0) d[p] = Mathf.Min(d[p], d[p - 1] + 1);
                if (y > 0) d[p] = Mathf.Min(d[p], d[p - n] + 1);
                if (x > 0 && y > 0) d[p] = Mathf.Min(d[p], d[p - n - 1] + diagonal);
                if (x + 1 < n && y > 0) d[p] = Mathf.Min(d[p], d[p - n + 1] + diagonal);
            }
            for (int y = n - 1; y >= 0; y--) for (int x = n - 1; x >= 0; x--)
            {
                int p = y * n + x;
                if (x + 1 < n) d[p] = Mathf.Min(d[p], d[p + 1] + 1);
                if (y + 1 < n) d[p] = Mathf.Min(d[p], d[p + n] + 1);
                if (x + 1 < n && y + 1 < n) d[p] = Mathf.Min(d[p], d[p + n + 1] + diagonal);
                if (x > 0 && y + 1 < n) d[p] = Mathf.Min(d[p], d[p + n - 1] + diagonal);
            }
            return d;
        }
    }
}
