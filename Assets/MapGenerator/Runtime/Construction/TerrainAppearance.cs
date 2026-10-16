using System;
using TacticalMaps.Generation;
using Unity.Collections;
using UnityEngine;

namespace TacticalMaps.Construction
{
    // Baked once per build: one unique, clamped color map per terrain, no runtime noise or tiled detail textures.
    [Serializable]
    internal sealed class TerrainAppearance
    {
        [SerializeField, Range(128, 2048)] private int mainResolution = 1024;
        [SerializeField, Range(128, 2048)] private int backgroundResolution = 1024;
        [SerializeField] private Color grass = new Color(0.43f, 0.52f, 0.22f);
        [SerializeField] private Color dryGrass = new Color(0.65f, 0.62f, 0.30f);
        [SerializeField] private Color earth = new Color(0.44f, 0.36f, 0.22f);
        [SerializeField] private Color rock = new Color(0.57f, 0.56f, 0.48f);
        [SerializeField] private Color sand = new Color(0.73f, 0.67f, 0.44f);
        [SerializeField] private Color seabed = new Color(0.39f, 0.43f, 0.30f);
        [SerializeField, Min(1)] private float patchSizeMeters = 160;
        [SerializeField, Min(1)] private float detailSizeMeters = 12;
        [SerializeField, Range(0, 0.5f)] private float colorVariation = 0.15f;
        [SerializeField, Range(0, 80)] private float rockSlopeStart = 24;
        [SerializeField, Range(1, 89)] private float rockSlopeEnd = 48;
        [SerializeField, Min(0.1f)] private float beachHeightMeters = 4;
        [SerializeField, Range(0, 1)] private float highlandStart = 0.55f;
        [SerializeField] private int seed = 137;

        internal void Apply(TerrainData data, MapData map, int level, Vector3 center)
        {
            if (mainResolution < 128 || mainResolution > 2048 || backgroundResolution < 128 || backgroundResolution > 2048 ||
                !Positive(patchSizeMeters) || !Positive(detailSizeMeters) || !Positive(beachHeightMeters) ||
                float.IsNaN(rockSlopeStart) || float.IsNaN(rockSlopeEnd) || rockSlopeEnd <= rockSlopeStart)
                throw new ArgumentException("Invalid terrain appearance settings.");
            int n = level == 0 ? mainResolution : backgroundResolution;
            var layer = new TerrainLayer { name = "Surface_" + level, tileSize = map.Sizes[level], smoothness = 0, metallic = 0, specular = Color.black };
            data.terrainLayers = new[] { layer };
            var texture = new Texture2D(n, n, TextureFormat.RGBA32, true, false)
            { name = "TerrainColors_" + level, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 2 };
            layer.diffuseTexture = texture;
            var raw = new NativeArray<ushort>[3];
            for (int i = 0; i < 3; i++) raw[i] = map.Heightmaps[i].GetPixelData<ushort>(0);
            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                // Align the end texel centres to world bounds, including non-square maps.
                float wx = ((float)x / (n - 1) - 0.5f) * map.Sizes[level].x;
                float wz = ((float)y / (n - 1) - 0.5f) * map.Sizes[level].y;
                int source = 2;
                for (int i = 0; i < 2; i++)
                    if (Mathf.Abs(wx) <= map.Sizes[i].x * 0.5f && Mathf.Abs(wz) <= map.Sizes[i].y * 0.5f) { source = i; break; }
                var t = map.Heightmaps[source]; var size = map.Sizes[source];
                float step = Mathf.Max(4, Mathf.Max(size.x / (t.width - 1), size.y / (t.height - 1)));
                float h = Height(raw[source], t, size, wx, wz) * map.TerrainHeightMeters;
                float gx = (Height(raw[source], t, size, wx + step, wz) - Height(raw[source], t, size, wx - step, wz)) * map.TerrainHeightMeters / (2 * step);
                float gz = (Height(raw[source], t, size, wx, wz + step) - Height(raw[source], t, size, wx, wz - step)) * map.TerrainHeightMeters / (2 * step);
                float slope = Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
                pixels[y * n + x] = Evaluate(wx + center.x, wz + center.z, h, slope, map.SeaLevelMeters, map.TerrainHeightMeters);
            }
            texture.SetPixels32(pixels); texture.Apply(true, false);
        }

        private Color Evaluate(float x, float z, float height, float slope, float sea, float range)
        {
            float broad = Noise(x, z, patchSizeMeters);
            float detail = Noise(x + 719, z - 235, detailSizeMeters);
            float patches = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.25f, 0.75f, broad));
            Color color = Color.Lerp(grass, dryGrass, patches);
            float high = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(highlandStart, 1, (height - sea) / Mathf.Max(1, range - sea)));
            color = Color.Lerp(color, earth, high * 0.65f + Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.57f, 0.78f, detail)) * 0.14f);
            float rocky = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(rockSlopeStart, rockSlopeEnd, slope));
            color = Color.Lerp(color, rock, rocky);
            float beach = 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01(Mathf.Max(0, height - sea) / beachHeightMeters));
            color = Color.Lerp(color, sand, beach * (1 - rocky));
            color = Color.Lerp(color, seabed, Mathf.Clamp01((sea - height) / Mathf.Max(1, sea)));
            float shade = 1 + ((broad - 0.5f) + (Mathf.Round(detail * 7) / 7 - 0.5f) * 0.7f) * colorVariation;
            color *= shade; color.a = 1;
            return color;
        }
        private float Noise(float x, float z, float scale) => Mathf.PerlinNoise(x / scale + seed * 0.137f, z / scale + seed * 0.371f);
        private static bool Positive(float x) => !float.IsNaN(x) && !float.IsInfinity(x) && x > 0;
        private static float Height(NativeArray<ushort> raw, Texture2D texture, Vector2 size, float x, float z)
        {
            float px = Mathf.Clamp01(x / size.x + 0.5f) * (texture.width - 1), py = Mathf.Clamp01(z / size.y + 0.5f) * (texture.height - 1);
            int ix = Mathf.Min((int)px, texture.width - 2), iy = Mathf.Min((int)py, texture.height - 2);
            int p = iy * texture.width + ix;
            return Mathf.Lerp(Mathf.Lerp(raw[p], raw[p + 1], px - ix), Mathf.Lerp(raw[p + texture.width], raw[p + texture.width + 1], px - ix), py - iy) / 65535f;
        }
    }
}
