using TacticalMaps.Generation;
using TacticalMaps.Testing;
using TacticalMaps.Construction;
using System;
using System.Threading;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class MapGeneratorChecks
    {
        [UnityEditor.MenuItem("Tools/Tactical Maps/Verify Height Generation")]
        internal static void Run()
        {
            const int n = 129;
            var heights = new float[n * n]; var mask = new byte[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                heights[y * n + x] = 0.35f + 0.2f * Mathf.Sin(x * 0.7f) * Mathf.Cos(y * 0.3f);
                mask[y * n + x] = x >= 64 ? (byte)255 : (byte)0;
            }
            var s = new MapSettings { Center = new Vector2Int(64, 64), SourceRadius = 50 };
            s.Levels[0].Resolution = new Vector2Int(129, 65);
            s.Levels[0].Size = new Vector2(2048, 1200);
            s.Levels[1].Resolution = new Vector2Int(257, 129);
            s.Levels[1].Size = new Vector2(8096, 5000);
            s.Levels[2].Resolution = new Vector2Int(513, 257);
            s.Levels[2].Size = new Vector2(20000, 12000);
            var field = new HeightField(heights, mask, n, n, s);
            Check(field.Side == 101, "Shared 101 × 101 selection");
            var maps = new ushort[3][];
            for (int i = 0; i < 3; i++)
            {
                maps[i] = field.Render(i, CancellationToken.None);
                Check(maps[i].Length == s.Levels[i].Resolution.x * s.Levels[i].Resolution.y, "Rectangular texture dimensions");
                Check(maps[i][maps[i].Length / 2] == maps[0][maps[0].Length / 2], "Shared center height");
            }
            Check(field.Sample(-800, 0) == 0, "Water overrides a nonzero global heightmap");
            float limit = Mathf.Tan(s.MaxSlope * Mathf.Deg2Rad);
            float maximum = 0;
            for (int y = -500; y <= 500; y += 20) for (int x = -900; x <= 900; x += 20)
            {
                float gx = (field.Sample(x + 0.5f, y) - field.Sample(x - 0.5f, y)) * s.HeightMeters;
                float gy = (field.Sample(x, y + 0.5f) - field.Sample(x, y - 0.5f)) * s.HeightMeters;
                maximum = Mathf.Max(maximum, Mathf.Sqrt(gx * gx + gy * gy));
            }
            Check(maximum <= limit + 0.001f, "Main terrain slope bound");
            float seamError = 0;
            for (int i = 0; i <= 128; i++)
            {
                float y = ((float)i / 128 - 0.5f) * s.Levels[0].Size.y;
                float edge = s.Levels[0].Size.x * 0.5f;
                seamError = Mathf.Max(seamError, Mathf.Abs(field.Sample(edge - 0.01f, y) - field.Sample(edge + 0.01f, y)));
            }
            Check(seamError < 0.0001f, "Continuous I / II boundary");
            var empty = new HeightField(heights, new byte[n * n], n, n, s);
            foreach (var v in empty.Render(0, CancellationToken.None)) Check(v == 0, "All-water selection remains zero");
            s.Center = Vector2Int.zero;
            var boundary = new HeightField(heights, mask, n, n, s);
            Check(boundary.Sample(-10000, -6000) == 0, "Out-of-source area is water");
            var canceled = new CancellationTokenSource(); canceled.Cancel(); bool stopped = false;
            try { field.Render(0, canceled.Token); } catch (OperationCanceledException) { stopped = true; }
            Check(stopped, "Canceled generation stops"); canceled.Dispose();
            CheckStandaloneGenerator();
            string result = $"PASS: 101px shared crop; rectangular outputs; shared center; water override; slope {Mathf.Atan(maximum) * Mathf.Rad2Deg:F3} <= {s.MaxSlope} degrees; continuous seam; all-water; edge crop; cancellation; standalone generator without UI; private serialized configuration; no public API; defensive settings copy.";
            System.IO.File.WriteAllText("Temp/map-generator-checks.txt", result);
            Debug.Log(result);
        }
        private static void CheckStandaloneGenerator()
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly;
            foreach (var type in new[] { typeof(MapGenerator), typeof(HeightField), typeof(MapSettings), typeof(MapLevel), typeof(MapWorkbench), typeof(MapSelectionGraphic) })
            {
                Check(!type.IsPublic, type.Name + " is internal");
                foreach (var member in type.GetMembers(flags | System.Reflection.BindingFlags.Public))
                    if (!(member is System.Reflection.ConstructorInfo)) Check(false, type.Name + " has no declared public API: " + member.Name);
                foreach (var field in type.GetFields(flags | System.Reflection.BindingFlags.NonPublic))
                    if (Attribute.IsDefined(field, typeof(SerializeField))) Check(field.IsPrivate, field.Name + " is private serialized");
            }
            var go = new GameObject("StandaloneGeneratorCheck");
            var height = new Texture2D(33, 33, TextureFormat.R16, false, true);
            var land = new Texture2D(33, 33, TextureFormat.R8, false, true);
            try
            {
                var heights = new ushort[33 * 33]; var mask = new byte[heights.Length];
                for (int i = 0; i < heights.Length; i++) { heights[i] = (ushort)(10000 + i * 10); mask[i] = 255; }
                height.SetPixelData(heights, 0); height.Apply(); land.SetPixelData(mask, 0); land.Apply();
                var generator = go.AddComponent<MapGenerator>();
                var serialized = new UnityEditor.SerializedObject(generator);
                serialized.FindProperty("heightSource").objectReferenceValue = height;
                serialized.FindProperty("landSource").objectReferenceValue = land;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var settings = generator.Settings;
                settings.Center = new Vector2Int(16, 16); settings.SourceRadius = 10;
                for (int i = 0; i < 3; i++) settings.Levels[i].Resolution = new Vector2Int(33 + i * 2, 35 + i * 2);
                generator.Configure(settings);
                settings.SourceRadius = 7;
                Check(generator.Settings.SourceRadius == 10, "Configure takes a defensive copy");
                var copy = generator.Settings; copy.Levels[0].Resolution = new Vector2Int(999, 999);
                Check(generator.Settings.Levels[0].Resolution.x == 33, "Settings getter takes a deep copy");
                generator.Generate();
                for (int i = 0; i < 3; i++)
                {
                    var result = generator.GetHeightmap(i);
                    Check(result != null && result.format == TextureFormat.R16 && result.width == 33 + i * 2 && result.height == 35 + i * 2, "Generation without any Canvas dependency");
                }
                foreach (var field in typeof(MapGenerator).GetFields(flags | System.Reflection.BindingFlags.NonPublic))
                    Check(field.FieldType != typeof(MapWorkbench) && !typeof(UnityEngine.UI.Graphic).IsAssignableFrom(field.FieldType), "Generator has no UI references");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(height); UnityEngine.Object.DestroyImmediate(land);
            }
        }
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Heightmap check failed: " + message);
        }
    }
}
