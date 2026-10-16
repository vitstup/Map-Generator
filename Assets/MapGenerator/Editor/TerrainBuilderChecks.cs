using TacticalMaps.Generation;
using System;
using System.Linq;
using System.Reflection;
using TacticalMaps.Construction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class MapBuilderChecks
    {
        [MenuItem("Tools/Tactical Maps/Verify Terrain Construction")]
        private static void Run()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("MapBuilderCheck");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var maps = new Texture2D[3];
            try
            {
                var builder = root.AddComponent<MapBuilder>();
                ConstructionSetup.Configure(builder);
                var terrainConfig = new SerializedObject(root.GetComponent<TerrainGenerator>());
                terrainConfig.FindProperty("terrainLayer").intValue = 3; terrainConfig.ApplyModifiedPropertiesWithoutUndo();
                var waterConfig = new SerializedObject(root.GetComponent<WaterGenerator>());
                waterConfig.FindProperty("waterLayer").intValue = 4; waterConfig.ApplyModifiedPropertiesWithoutUndo();
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
                builder.Build(new MapData(maps, sizes, 4000, 0, 4000, Vector2Int.zero));
                var terrains = root.GetComponentsInChildren<Terrain>();
                Check(terrains.Length == 3, "Three terrains");
                for (int i = 0; i < 3; i++)
                {
                    var terrain = terrains[i]; var data = terrain.terrainData;
                    Check(terrain.gameObject.layer == 3, "Terrain layer applied to every level");
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
                var water = root.GetComponentInChildren<MeshFilter>();
                Check(water != null && water.gameObject.layer == 4 && water.GetComponent<Collider>() == null, "Water layer without gameplay collider");
                Check(root.GetComponentsInChildren<MeshRenderer>().Length == 1, "Single water surface renderer");
                var mesh = water.sharedMesh; var material = water.GetComponent<MeshRenderer>().sharedMaterial;
                Check(mesh.vertexCount == 31329 && mesh.triangles.Length / 3 == 61952, "Bounded water geometry budget");
                Check(mesh.bounds.size.x == sizes[2].x && mesh.bounds.size.z == sizes[2].y, "Water covers outer map");
                Check(material.shader.name == "TacticalMaps/LowPolyWater" && material.GetFloat("_WaveAmplitude") > 0, "Animated water shader");
                var depth = material.GetTexture("_Depth0");
                Check(depth != maps[0] && depth != null, "Water owns independent seabed snapshots");
                Check(!water.GetComponent<MeshRenderer>().shadowCastingMode.Equals(UnityEngine.Rendering.ShadowCastingMode.On), "Water has no shadow pass");
                var oldData = terrains.Select(t => t.terrainData).ToArray();
                var invalid = (Texture2D[])maps.Clone(); invalid[1] = null; bool rejected = false;
                try { builder.Build(new MapData(invalid, sizes, 4000, 0, 4000, Vector2Int.zero)); } catch (ArgumentException) { rejected = true; }
                Check(rejected && root.GetComponentsInChildren<Terrain>().Length == 3 && oldData[0] != null, "Invalid input preserves existing build");
                var savedMaterial = waterConfig.FindProperty("waterMaterial").objectReferenceValue;
                waterConfig.FindProperty("waterMaterial").objectReferenceValue = null; waterConfig.ApplyModifiedPropertiesWithoutUndo();
                int dataCount = Resources.FindObjectsOfTypeAll<TerrainData>().Length;
                bool waterRejected = false;
                try { builder.Build(new MapData(maps, sizes, 4000, 0, 4000, Vector2Int.zero)); } catch (InvalidOperationException) { waterRejected = true; }
                waterConfig.FindProperty("waterMaterial").objectReferenceValue = savedMaterial; waterConfig.ApplyModifiedPropertiesWithoutUndo();
                Check(waterRejected && mesh != null && root.GetComponentsInChildren<Terrain>().Length == 3 && Resources.FindObjectsOfTypeAll<TerrainData>().Length == dataCount, "Failed water stage rolls back terrain resources");
                builder.Build(new MapData(maps, sizes, 4000, 0, 4000, Vector2Int.zero));
                Check(mesh == null && material == null && depth == null, "Old water resources released on rebuild");
                Check(root.GetComponentsInChildren<Terrain>().Length == 3 && oldData.All(t => t == null), "Rebuild replaces objects and disposes owned data");
                var finalDepth = (Texture2D)root.GetComponentInChildren<MeshRenderer>().sharedMaterial.GetTexture("_Depth0");
                foreach (var source in maps) UnityEngine.Object.DestroyImmediate(source);
                Check(finalDepth != null && finalDepth.GetPixelData<ushort>(0).Length > 0, "Releasing MapData textures does not invalidate water");
                var api = typeof(MapBuilder).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Where(m => m.IsPublic).ToArray();
                Check(api.Length == 1 && api[0].Name == "Build" && api[0].ReturnType == typeof(void), "Exactly one public void entry point");
                foreach (var f in typeof(MapBuilder).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    Check(f.IsPrivate, "Private construction state");
                string result = "PASS: 3 terrains; rectangular resampling; world sizes; shared center; R16 orientation; holes and lowered background centers; main collider only; invalid-input preservation; replacement and cleanup; single public Build API; configurable terrain and water layers; one 61952-triangle GPU water surface; independent depth snapshots; water resource cleanup; failed-stage rollback; source lifetime independence.";
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
