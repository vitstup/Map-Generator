using System;
using System.Threading;
using TacticalMaps.Generation;
using UnityEditor;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class BiomeChecks
    {
        [MenuItem("Tools/Tactical Maps/Verify Biome Generation")]
        private static void Run()
        {
            var textures = new Texture2D[3];
            var sizes = new[] { new Vector2(1000, 600), new Vector2(4000, 2400), new Vector2(10000, 6000) };
            try
            {
                var settings = new BiomeSettings();
                JsonUtility.FromJsonOverwrite("{\"mainResolution\":64,\"backgroundResolution\":32}", settings);
                Fill(textures, sizes, (x, z) => 75);
                var field = new BiomeField(textures, sizes, 2000, 50, new Vector2Int(2200, 1700), 50, settings);
                var maps = field.Render(CancellationToken.None);
                Check(maps[0].GetLength(0) == 64 && maps[1].GetLength(0) == 32 && maps[0].GetLength(2) == 6, "Independent splat resolutions and six surfaces");
                Check(maps[0][32, 32, 0] + maps[0][32, 32, 1] > 0.99f, "Flat inland supports meadow and dry grass");
                foreach (var map in maps) for (int z = 0; z < map.GetLength(0); z++) for (int x = 0; x < map.GetLength(1); x++)
                {
                    float sum = 0;
                    for (int i = 0; i < 6; i++) { Check(map[z, x, i] >= 0 && !float.IsNaN(map[z, x, i]), "Finite nonnegative weights"); sum += map[z, x, i]; }
                    Check(Mathf.Abs(sum - 1) < 0.00001f, "Normalized weights");
                }
                var repeated = field.Render(CancellationToken.None);
                Check(maps[0][21, 12, 0] == repeated[0][21, 12, 0], "Deterministic climate and coverage");
                using (var cancel = new CancellationTokenSource())
                {
                    cancel.Cancel(); bool canceled = false;
                    try { field.Render(cancel.Token); } catch (OperationCanceledException) { canceled = true; }
                    Check(canceled, "Canceled surface work stops");
                }
                Fill(textures, sizes, (x, z) => 0);
                maps = new BiomeField(textures, sizes, 2000, 50, Vector2Int.zero, 50, settings).Render(CancellationToken.None);
                Check(maps[0][32, 32, 5] > 0.99f, "Submerged bed selects silt regardless of climate");
                Fill(textures, sizes, (x, z) => 50.5f);
                maps = new BiomeField(textures, sizes, 2000, 50, Vector2Int.zero, 50, settings).Render(CancellationToken.None);
                Check(maps[0][32, 32, 4] > 0.8f, "Flat near-sea surface selects shore sand");
                Fill(textures, sizes, (x, z) => Mathf.Clamp(700 + x, 0, 2000));
                maps = new BiomeField(textures, sizes, 2000, 50, Vector2Int.zero, 50, settings).Render(CancellationToken.None);
                Check(maps[0][32, 32, 3] > 0.95f, "Steep inland surface exposes rock");
                System.IO.File.WriteAllText("Temp/biome-checks.txt", "PASS: six surfaces; normalized finite weights; terrain-dependent meadow, shore, silt and rock; deterministic climate; cancellation; rectangular world sizes.");
            }
            catch (Exception e) { System.IO.File.WriteAllText("Temp/biome-checks.txt", e.ToString()); Debug.LogException(e); }
            finally { foreach (var texture in textures) if (texture != null) UnityEngine.Object.DestroyImmediate(texture); }
        }
        private static void Fill(Texture2D[] textures, Vector2[] sizes, Func<float, float, float> height)
        {
            for (int level = 0; level < 3; level++)
            {
                if (textures[level] == null) textures[level] = new Texture2D(129, 65, TextureFormat.R16, false, true);
                var pixels = new ushort[129 * 65];
                for (int z = 0; z < 65; z++) for (int x = 0; x < 129; x++)
                    pixels[z * 129 + x] = (ushort)Mathf.RoundToInt(Mathf.Clamp01(height((x / 128f - 0.5f) * sizes[level].x, (z / 64f - 0.5f) * sizes[level].y) / 2000) * 65535);
                textures[level].SetPixelData(pixels, 0); textures[level].Apply();
            }
        }
        private static void Check(bool condition, string message) { if (!condition) throw new Exception("Biome check failed: " + message); }
    }
}
