using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace TacticalMaps.Generation
{
    // CPU-only immutable snapshot, safe on the generation worker.
    internal sealed class BiomeField
    {
        internal Vector2 TextureOrigin { get; }
        private readonly ushort[][] maps;
        private readonly Vector2Int[] resolutions;
        private readonly Vector2[] sizes;
        private readonly float height, sea;
        private readonly BiomeSettings settings;

        internal BiomeField(Texture2D[] textures, Vector2[] sizes, float height, float sea, Vector2Int point, int radius, BiomeSettings settings)
        {
            settings.Validate(); this.settings = settings; this.sizes = (Vector2[])sizes.Clone(); this.height = height; this.sea = sea;
            TextureOrigin = new Vector2(point.x * sizes[2].x / (radius * 2), point.y * sizes[2].y / (radius * 2));
            maps = new ushort[3][]; resolutions = new Vector2Int[3];
            for (int i = 0; i < 3; i++) { maps[i] = textures[i].GetPixelData<ushort>(0).ToArray(); resolutions[i] = new Vector2Int(textures[i].width, textures[i].height); }
        }
        internal float[][,,] Render(CancellationToken cancellation)
        {
            var result = new float[3][,,];
            for (int level = 0; level < 3; level++)
            {
                int n = level == 0 ? settings.MainResolution : settings.BackgroundResolution;
                var weights = new float[n, n, 6]; result[level] = weights;
                Parallel.For(0, n, new ParallelOptions { CancellationToken = cancellation, MaxDegreeOfParallelism = System.Math.Max(1, System.Math.Min(4, System.Environment.ProcessorCount - 2)) }, () => new float[6], (z, loop, values) =>
                {
                    cancellation.ThrowIfCancellationRequested();
                    for (int x = 0; x < n; x++)
                    {
                        float wx = ((x + 0.5f) / n - 0.5f) * sizes[level].x;
                        float wz = ((z + 0.5f) / n - 0.5f) * sizes[level].y;
                        Evaluate(wx, wz, values);
                        for (int i = 0; i < 6; i++) weights[z, x, i] = values[i];
                    }
                    return values;
                }, values => { });
            }
            return result;
        }
        private void Evaluate(float x, float z, float[] w)
        {
            float h = Height(x, z), above = h - sea;
            float step = 8;
            float gx = (Height(x + step, z) - Height(x - step, z)) / (2 * step);
            float gz = (Height(x, z + step) - Height(x, z - step)) / (2 * step);
            float slope = Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
            float r = settings.ValleyRadius;
            float surrounding = (Height(x + r, z) + Height(x - r, z) + Height(x, z + r) + Height(x, z - r)) * 0.25f;
            float valley = Mathf.Clamp((surrounding - h) / Mathf.Max(1, r * 0.12f), -1, 1);
            float wet = settings.Moisture + Mathf.Exp(-Mathf.Max(0, above) / settings.CoastalMoistureHeight) * 0.22f + valley * settings.ValleyMoisture;
            wet -= Mathf.Clamp01(above / settings.DryHighlandHeight) * 0.3f + Mathf.Clamp(gz, -0.3f, 0.3f) * 0.25f;
            wet += (Climate(x + TextureOrigin.x, z + TextureOrigin.y) - 0.5f) * settings.ClimateVariation;
            wet = Mathf.Clamp01(wet);
            float stone = Smooth(settings.RockSlopeStart, settings.RockSlopeEnd, slope);
            float soil = Smooth(settings.SoilSlopeStart, settings.SoilSlopeEnd, slope) * (1 - stone) * 0.65f;
            float beach = (1 - Smooth(0, settings.BeachHeight, Mathf.Max(0, above))) * (1 - stone);
            float mud = Smooth(0.2f, settings.SiltDepth, -above);
            float land = (1 - beach) * (1 - mud);
            w[0] = land * (1 - stone - soil) * wet;
            w[1] = land * (1 - stone - soil) * (1 - wet);
            w[2] = land * soil;
            w[3] = land * stone;
            w[4] = beach * (1 - mud);
            w[5] = mud;
            float sum = 0; for (int i = 0; i < 6; i++) sum += w[i];
            for (int i = 0; i < 6; i++) w[i] /= Mathf.Max(0.0001f, sum);
        }
        private float Height(float x, float z)
        {
            int level = 2;
            for (int i = 0; i < 2; i++) if (Mathf.Abs(x) <= sizes[i].x * 0.5f && Mathf.Abs(z) <= sizes[i].y * 0.5f) { level = i; break; }
            int width = resolutions[level].x, h = resolutions[level].y;
            float px = Mathf.Clamp01(x / sizes[level].x + 0.5f) * (width - 1), pz = Mathf.Clamp01(z / sizes[level].y + 0.5f) * (h - 1);
            int ix = Mathf.Min((int)px, width - 2), iz = Mathf.Min((int)pz, h - 2), p = iz * width + ix;
            var a = maps[level];
            return Mathf.Lerp(Mathf.Lerp(a[p], a[p + 1], px - ix), Mathf.Lerp(a[p + width], a[p + width + 1], px - ix), pz - iz) / 65535f * height;
        }
        private float Climate(float x, float z)
        {
            // Interpolated regional climate is a subtle bias; terrain and water decide coverage.
            x /= settings.ClimateRegionMeters; z /= settings.ClimateRegionMeters;
            int ix = Mathf.FloorToInt(x), iz = Mathf.FloorToInt(z);
            float u = Smooth(0, 1, x - ix), v = Smooth(0, 1, z - iz);
            return Mathf.Lerp(Mathf.Lerp(Hash(ix, iz), Hash(ix + 1, iz), u), Mathf.Lerp(Hash(ix, iz + 1), Hash(ix + 1, iz + 1), u), v);
        }
        private float Hash(int x, int z)
        {
            unchecked { uint h = (uint)(x * 374761393 + z * 668265263 + settings.Seed * 144269); h = (h ^ (h >> 13)) * 1274126177; return (h ^ (h >> 16)) / (float)uint.MaxValue; }
        }
        private static float Smooth(float a, float b, float value) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, value));
    }
}
