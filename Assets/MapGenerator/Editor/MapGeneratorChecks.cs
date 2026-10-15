using System;
using System.Threading;
using UnityEngine;

namespace TacticalMaps.Editor
{
    public static class MapGeneratorChecks
    {
        [UnityEditor.MenuItem("Tools/Tactical Maps/Verify Height Generation")]
        public static void Run()
        {
            const int n = 129;
            var heights = new float[n * n]; var mask = new byte[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                heights[y * n + x] = 0.35f + 0.2f * Mathf.Sin(x * 0.7f) * Mathf.Cos(y * 0.3f);
                mask[y * n + x] = x >= 64 ? (byte)255 : (byte)0;
            }
            var s = new MapSettings { center = new Vector2Int(64, 64), sourceRadius = 50 };
            s.levels[0].resolution = new Vector2Int(129, 65);
            s.levels[0].size = new Vector2(2048, 1200);
            s.levels[1].resolution = new Vector2Int(257, 129);
            s.levels[1].size = new Vector2(8096, 5000);
            s.levels[2].resolution = new Vector2Int(513, 257);
            s.levels[2].size = new Vector2(20000, 12000);
            var field = new HeightField(heights, mask, n, n, s);
            Check(field.side == 101, "Shared 101 × 101 selection");
            var maps = new ushort[3][];
            for (int i = 0; i < 3; i++)
            {
                maps[i] = field.Render(i, CancellationToken.None);
                Check(maps[i].Length == s.levels[i].resolution.x * s.levels[i].resolution.y, "Rectangular texture dimensions");
                Check(maps[i][maps[i].Length / 2] == maps[0][maps[0].Length / 2], "Shared center height");
            }
            Check(field.Sample(-800, 0) == 0, "Water overrides a nonzero global heightmap");
            float limit = Mathf.Tan(s.maxSlope * Mathf.Deg2Rad);
            float maximum = 0;
            for (int y = -500; y <= 500; y += 20) for (int x = -900; x <= 900; x += 20)
            {
                float gx = (field.Sample(x + 0.5f, y) - field.Sample(x - 0.5f, y)) * s.heightMeters;
                float gy = (field.Sample(x, y + 0.5f) - field.Sample(x, y - 0.5f)) * s.heightMeters;
                maximum = Mathf.Max(maximum, Mathf.Sqrt(gx * gx + gy * gy));
            }
            Check(maximum <= limit + 0.001f, "Main terrain slope bound");
            float seamError = 0;
            for (int i = 0; i <= 128; i++)
            {
                float y = ((float)i / 128 - 0.5f) * s.levels[0].size.y;
                float edge = s.levels[0].size.x * 0.5f;
                seamError = Mathf.Max(seamError, Mathf.Abs(field.Sample(edge - 0.01f, y) - field.Sample(edge + 0.01f, y)));
            }
            Check(seamError < 0.0001f, "Continuous I / II boundary");
            var empty = new HeightField(heights, new byte[n * n], n, n, s);
            foreach (var v in empty.Render(0, CancellationToken.None)) Check(v == 0, "All-water selection remains zero");
            s.center = Vector2Int.zero;
            var boundary = new HeightField(heights, mask, n, n, s);
            Check(boundary.Sample(-10000, -6000) == 0, "Out-of-source area is water");
            var canceled = new CancellationTokenSource(); canceled.Cancel(); bool stopped = false;
            try { field.Render(0, canceled.Token); } catch (OperationCanceledException) { stopped = true; }
            Check(stopped, "Canceled generation stops"); canceled.Dispose();
            string result = $"PASS: 101px shared crop; rectangular outputs; shared center; water override; slope {Mathf.Atan(maximum) * Mathf.Rad2Deg:F3} <= {s.maxSlope} degrees; continuous seam; all-water; edge crop; cancellation.";
            System.IO.File.WriteAllText("Temp/map-generator-checks.txt", result);
            Debug.Log(result);
        }
        static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Heightmap check failed: " + message);
        }
    }
}
