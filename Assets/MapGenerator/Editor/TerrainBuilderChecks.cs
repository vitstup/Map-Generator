using System;
using System.Linq;
using System.Reflection;
using TacticalMaps.Construction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class TerrainBuilderChecks
    {
        [MenuItem("Tools/Tactical Maps/Verify Terrain Construction")]
        private static void Run()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("TerrainBuilderCheck");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var maps = new Texture2D[3];
            try
            {
                var builder = root.AddComponent<TerrainBuilder>();
                var sizes = new[] { new Vector2(256, 128), new Vector2(1024, 512), new Vector2(4096, 2048) };
                for (int level = 0; level < 3; level++)
                {
                    int width = level == 1 ? 33 : 65, height = level == 0 ? 33 : 65;
                    var texture = new Texture2D(width, height, TextureFormat.R16, false, true);
                    maps[level] = texture;
                    var pixels = new ushort[width * height];
                    for (int z = 0; z < height; z++) for (int x = 0; x < width; x++)
                    {
                        float wx = ((float)x / (width - 1) - 0.5f) * sizes[level].x;
                        float wz = ((float)z / (height - 1) - 0.5f) * sizes[level].y;
                        pixels[z * width + x] = (ushort)Mathf.RoundToInt((0.25f + wx * 0.00001f + wz * 0.00002f) * 65535);
                    }
                    texture.SetPixelData(pixels, 0); texture.Apply();
                }
                builder.Build(maps, sizes, 4000);
                var terrains = root.GetComponentsInChildren<Terrain>();
                Check(terrains.Length == 3, "Three terrains");
                for (int i = 0; i < 3; i++)
                {
                    var terrain = terrains[i]; var data = terrain.terrainData;
                    Check(data.heightmapResolution == 65, "Square resampling of rectangular inputs");
                    Check(data.size == new Vector3(sizes[i].x, 4000, sizes[i].y), "World dimensions");
                    Check(Mathf.Abs(terrain.transform.position.x + sizes[i].x / 2) < 0.001f && Mathf.Abs(terrain.transform.position.z + sizes[i].y / 2) < 0.001f, "Shared center");
                    Check(terrain.GetComponent<TerrainCollider>().enabled == (i == 0), "Main collision only");
                    if (i > 0)
                    {
                        int mid = data.holesResolution / 2;
                        Check(!data.GetHoles(mid, mid, 1, 1)[0, 0], "Background center cut out");
                        Check(data.GetHoles(0, 0, 1, 1)[0, 0], "Background outer boundary retained");
                        Check(data.GetHeight(32, 32) == 0, "Hidden center lowered to minimum");
                    }
                }
                float expected = (0.25f + 128 * 0.00001f - 64 * 0.00002f) * 4000;
                Check(Mathf.Abs(terrains[0].terrainData.GetHeight(64, 0) - expected) < 0.2f, "Height orientation and shared vertical scale");
                var oldData = terrains.Select(t => t.terrainData).ToArray();
                var invalid = (Texture2D[])maps.Clone(); invalid[1] = null; bool rejected = false;
                try { builder.Build(invalid, sizes, 4000); } catch (ArgumentException) { rejected = true; }
                Check(rejected && root.GetComponentsInChildren<Terrain>().Length == 3 && oldData[0] != null, "Invalid input preserves existing build");
                builder.Build(maps, sizes, 4000);
                Check(root.GetComponentsInChildren<Terrain>().Length == 3 && oldData.All(t => t == null), "Rebuild replaces objects and disposes owned data");
                var api = typeof(TerrainBuilder).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Where(m => m.IsAssembly).ToArray();
                Check(api.Length == 1 && api[0].Name == "Build" && api[0].ReturnType == typeof(void), "Exactly one internal void entry point");
                foreach (var f in typeof(TerrainBuilder).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    Check(f.IsPrivate, "Private construction state");
                string result = "PASS: 3 terrains; rectangular resampling; world sizes; shared center; R16 orientation; holes and lowered background centers; main collider only; invalid-input preservation; replacement and cleanup; single internal void API.";
                System.IO.File.WriteAllText("Temp/terrain-builder-checks.txt", result); Debug.Log(result);
            }
            catch (Exception e) { System.IO.File.WriteAllText("Temp/terrain-builder-checks.txt", e.ToString()); Debug.LogException(e); }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                foreach (var map in maps) if (map != null) UnityEngine.Object.DestroyImmediate(map);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Terrain construction check failed: " + message);
        }
    }
}
