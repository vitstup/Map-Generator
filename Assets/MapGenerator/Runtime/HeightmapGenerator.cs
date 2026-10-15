using System;
using System.Threading;
using UnityEngine;

namespace TacticalMaps
{
    [Serializable]
    public class MapLevel
    {
        public Vector2Int resolution = new Vector2Int(2049, 2049);
        public Vector2 size = new Vector2(2048, 2048);
    }

    [Serializable]
    public class MapSettings
    {
        [Range(1, 512)] public int sourceRadius = 50;
        public Vector2Int center = new Vector2Int(2550, 2250);
        public MapLevel[] levels = {
            new MapLevel(),
            new MapLevel { size = new Vector2(8096, 8096) },
            new MapLevel { size = new Vector2(20000, 20000) }
        };
        [Min(1)] public float heightMeters = 4000;
        [Range(0.25f, 12)] public float coastPixels = 2;
        [Range(0.25f, 16)] public float smoothingPixels = 3;
        [Range(1, 40)] public float maxSlope = 12;
        [Range(10, 5000)] public float transitionMeters = 1500;

        public void Validate(int width, int height)
        {
            if (levels == null || levels.Length != 3) throw new ArgumentException("Exactly three levels are required.");
            if (sourceRadius < 1 || sourceRadius > 512) throw new ArgumentException("Source radius: 1–512 pixels.");
            if (width < 2 || height < 2) throw new ArgumentException("Source textures are too small.");
            if (!Finite(heightMeters) || heightMeters <= 0 || !Finite(coastPixels) || coastPixels <= 0 ||
                !Finite(smoothingPixels) || smoothingPixels <= 0 || !Finite(maxSlope) || maxSlope <= 0 || maxSlope >= 89 ||
                !Finite(transitionMeters) || transitionMeters <= 0) throw new ArgumentException("Invalid smoothing or height settings.");
            for (int i = 0; i < 3; i++)
            {
                var l = levels[i];
                if (l == null || l.resolution.x < 33 || l.resolution.y < 33 || l.resolution.x > 4097 || l.resolution.y > 4097)
                    throw new ArgumentException("Texture dimensions must be between 33 and 4097.");
                if (!Finite(l.size.x) || !Finite(l.size.y) || l.size.x <= 0 || l.size.y <= 0)
                    throw new ArgumentException("Terrain dimensions must be positive.");
                if (i > 0 && (l.size.x <= levels[i - 1].size.x || l.size.y <= levels[i - 1].size.y))
                    throw new ArgumentException("Each next terrain must be larger along both axes.");
            }
        }
        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
    }

    // Pure CPU data: no Unity objects are accessed by the background worker.
    public sealed class HeightField
    {
        public readonly int side;
        public readonly float[] original, playable;
        public readonly MapSettings settings;
        public readonly float compression;

        public HeightField(float[] sourceHeights, byte[] land, int width, int height, MapSettings settings)
        {
            settings.Validate(width, height);
            this.settings = settings;
            side = settings.sourceRadius * 2 + 1;
            original = new float[side * side];
            var mask = new bool[original.Length];
            for (int y = 0; y < side; y++)
                for (int x = 0; x < side; x++)
                {
                    int sx = settings.center.x + x - settings.sourceRadius;
                    int sy = settings.center.y + y - settings.sourceRadius;
                    int p = y * side + x;
                    // Outside the source image is water; the selection never silently shifts.
                    if (sx < 0 || sy < 0 || sx >= width || sy >= height) continue;
                    int src = sy * width + sx;
                    mask[p] = land[src] >= 128;
                    original[p] = mask[p] ? sourceHeights[src] : 0;
                }
            float[] shoreDistance = DistanceToWater(mask, side);
            for (int i = 0; i < original.Length; i++)
                original[i] *= Smooth(Mathf.Clamp01((shoreDistance[i] - 0.5f) / settings.coastPixels));
            playable = Blur(original, side, settings.smoothingPixels);
            // The blurred coast remains exactly zero in water, and approaches it with zero derivative.
            for (int i = 0; i < playable.Length; i++)
                playable[i] *= Smooth(Mathf.Clamp01((shoreDistance[i] - 0.5f) / settings.coastPixels));

            Vector2 outer = settings.levels[2].size;
            Vector2 inner = settings.levels[0].size;
            float dx = outer.x / (side - 1), dy = outer.y / (side - 1);
            float maxGradient = 0;
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
                    maxGradient = Mathf.Max(maxGradient, Mathf.Sqrt(gx * gx + gy * gy) * settings.heightMeters);
                }
            compression = Mathf.Min(1, Mathf.Tan(settings.maxSlope * Mathf.Deg2Rad) / Mathf.Max(0.000001f, maxGradient));
        }

        public float Sample(float worldX, float worldY)
        {
            var inner = settings.levels[0].size;
            var middle = settings.levels[1].size;
            var outer = settings.levels[2].size;
            float u = worldX / outer.x + 0.5f, v = worldY / outer.y + 0.5f;
            float distance = Mathf.Max(Mathf.Abs(worldX) - inner.x * 0.5f, Mathf.Abs(worldY) - inner.y * 0.5f);
            // Finish the transition before reaching the middle terrain boundary.
            float blendWidth = Mathf.Min(settings.transitionMeters, Mathf.Min(middle.x - inner.x, middle.y - inner.y) * 0.45f);
            float blend = Smooth(Mathf.Clamp01(distance / Mathf.Max(0.01f, blendWidth)));
            return Mathf.Lerp(Bilinear(playable, side, u, v) * compression, Bilinear(original, side, u, v), blend);
        }

        public ushort[] Render(int level, CancellationToken cancellation)
        {
            MapLevel l = settings.levels[level];
            var pixels = new ushort[l.resolution.x * l.resolution.y];
            for (int y = 0; y < l.resolution.y; y++)
            {
                cancellation.ThrowIfCancellationRequested();
                float wy = ((float)y / (l.resolution.y - 1) - 0.5f) * l.size.y;
                for (int x = 0; x < l.resolution.x; x++)
                {
                    float wx = ((float)x / (l.resolution.x - 1) - 0.5f) * l.size.x;
                    pixels[y * l.resolution.x + x] = (ushort)Mathf.RoundToInt(Mathf.Clamp01(Sample(wx, wy)) * 65535);
                }
            }
            return pixels;
        }

        public static float Bilinear(float[] data, int side, float u, float v)
        {
            float px = Mathf.Clamp01(u) * (side - 1), py = Mathf.Clamp01(v) * (side - 1);
            int x = Mathf.Min((int)px, side - 2), y = Mathf.Min((int)py, side - 2);
            int p = y * side + x;
            return Mathf.Lerp(Mathf.Lerp(data[p], data[p + 1], px - x), Mathf.Lerp(data[p + side], data[p + side + 1], px - x), py - y);
        }

        static float Smooth(float t) => t * t * t * (t * (t * 6 - 15) + 10);

        static float[] Blur(float[] data, int n, float sigma)
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

        static float[] DistanceToWater(bool[] mask, int n)
        {
            var d = new float[mask.Length];
            for (int i = 0; i < d.Length; i++) d[i] = mask[i] ? n * 2 : 0;
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
