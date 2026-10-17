using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TacticalMaps.Construction;
using TacticalMaps.Generation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class TreeChecks
    {
        [MenuItem("Tools/Tactical Maps/Verify Tree Placement")]
        private static async void Run()
        {
            const string report = "Temp/tree-checks.txt";
            File.WriteAllText(report, "Running...\n");
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Tree checks"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var textures = new Texture2D[3]; MapData map = null;
            try
            {
                var generator = root.AddComponent<MapGenerator>(); var builder = root.AddComponent<MapBuilder>();
                TreeSetup.Configure(generator, builder); ConstructionSetup.Configure(builder);
                var sizes = new[] { new Vector2(1024, 768), new Vector2(2048, 1536), new Vector2(4096, 3072) };
                for (int level = 0; level < 3; level++)
                {
                    var raw = Enumerable.Repeat((ushort)16384, 129 * 129).ToArray();
                    textures[level] = new Texture2D(129, 129, TextureFormat.R16, false, true);
                    textures[level].SetPixelData(raw, 0); textures[level].Apply();
                }
                map = new MapData(textures, sizes, 400, 20, 4000, new Vector2Int(42, 71));
                var stage = root.GetComponent<TreePlacementGenerator>();
                var first = stage.Generate(map, CancellationToken.None); var second = stage.Generate(map, CancellationToken.None);
                Check(first.Placements.Count > 200, "Flat rectangular main terrain receives groves");
                Check(first.Coverage > 0.20f && first.Coverage <= 0.300001f, "Forest union area stays within 30% of full main terrain");
                Same(first, second);
                Same(first, await stage.GenerateAsync(map, CancellationToken.None));
                var groups = first.Placements.GroupBy(p => p.Group).ToArray();
                Check(groups.All(g => g.Count() >= 3), "No singleton or two-tree groups");
                foreach (var p in first.Placements)
                {
                    int x = Mathf.Min((int)((p.Position.x / sizes[0].x + 0.5f) * first.MaskWidth), first.MaskWidth - 1);
                    int z = Mathf.Min((int)((p.Position.z / sizes[0].y + 0.5f) * first.MaskHeight), first.MaskHeight - 1);
                    Check(first.ForestMask[z * first.MaskWidth + x], "Every point is inside its forest zone");
                    Check(p.Position.y > 22 && p.Prefab >= 0 && p.Prefab < first.Prefabs.Count && p.HeightScale > 0, "Valid land positions and prefab selection");
                }
                for (int i = 0; i < first.Placements.Count; i++)
                {
                    bool neighbor = false; var p = first.Placements[i].Position;
                    for (int j = 0; j < first.Placements.Count; j++)
                    {
                        if (i == j) continue;
                        var q = first.Placements[j].Position;
                        float distance = new Vector2(p.x - q.x, p.z - q.z).sqrMagnitude;
                        Check(distance >= 49 - 0.001f, "Minimum 7m trunk spacing");
                        if (distance <= 22 * 22 + 0.001f) neighbor = true;
                    }
                    Check(neighbor, "Every tree has a nearby tree");
                }
                bool canceled = false;
                try { await stage.GenerateAsync(map, new CancellationToken(true)); } catch (OperationCanceledException) { canceled = true; }
                Check(canceled, "Cancellation stops tree generation");
                var blocker = new Bounds(new Vector3(0, 120, 0), new Vector3(200, 300, 250));
                var blocked = stage.Generate(map, CancellationToken.None, new[] { blocker });
                Check(blocked.Placements.All(p => !blocker.Contains(p.Position)), "Cliff blockers exclude trees before group filtering");

                // Verify integration, normalization, actual Terrain height, replacement and asset ownership.
                var complete = new MapData(textures, sizes, 400, 20, 4000, map.SourcePoint, trees: first);
                var config = new SerializedObject(builder); config.FindProperty("center").vector3Value = new Vector3(137, 53, -219); config.ApplyModifiedPropertiesWithoutUndo();
                builder.Build(complete);
                var terrains = builder.GetComponentsInChildren<Terrain>(); var main = terrains.First(t => t.name == "Terrain_Main");
                Check(main.terrainData.treeInstanceCount == first.Placements.Count && main.terrainData.treePrototypes.Length == first.Prefabs.Count, "Native Terrain instances and prototypes");
                Check(main.treeBillboardDistance >= main.treeDistance && main.treeMaximumFullLODCount >= first.Placements.Count, "Mesh-only prefabs never disappear into unsupported billboards");
                Check(terrains.Where(t => t != main).All(t => t.terrainData.treeInstanceCount == 0), "Trees only on main terrain");
                var instances = main.terrainData.treeInstances;
                for (int i = 0; i < instances.Length; i++)
                {
                    var p = instances[i].position;
                    Check(p.x > 0 && p.x < 1 && p.z > 0 && p.z < 1, "Normalized Terrain coordinates");
                    Check(Mathf.Abs(p.y * main.terrainData.size.y - main.terrainData.GetInterpolatedHeight(p.x, p.z)) < 0.001f, "Trunks snapped to built Terrain");
                }
                builder.Build(complete);
                Check(builder.GetComponentsInChildren<Terrain>().First(t => t.name == "Terrain_Main").terrainData.treeInstanceCount == first.Placements.Count, "Rebuild replaces rather than duplicates trees");
                Check(first.Prefabs.All(p => p != null), "Rebuild preserves borrowed prefab assets");
                builder.Build(map);
                Check(builder.GetComponentsInChildren<Terrain>().All(t => t.terrainData.treeInstanceCount == 0), "Height-only maps remain supported");

                var settings = new SerializedObject(stage); settings.FindProperty("maximumCoverage").floatValue = 0; settings.ApplyModifiedPropertiesWithoutUndo();
                Check(stage.Generate(map, CancellationToken.None).Placements.Count == 0, "Zero coverage disables forest");
                settings.FindProperty("maximumCoverage").floatValue = 0.1f; settings.ApplyModifiedPropertiesWithoutUndo();
                Check(stage.Generate(map, CancellationToken.None).Coverage <= 0.100001f, "Custom 10% area budget");
                var changed = new MapData(textures, sizes, 400, 20, 4000, new Vector2Int(43, 71));
                var alternate = stage.Generate(changed, CancellationToken.None);
                Check(alternate.Placements.Count > 0 && alternate.Placements[0].Position != first.Placements[0].Position, "Source selection changes forest seed");
                foreach (float h in new[] { 0f, 40f })
                {
                    var raw = new ushort[129 * 129];
                    for (int z = 0; z < 129; z++) for (int x = 0; x < 129; x++)
                        raw[z * 129 + x] = (ushort)Mathf.RoundToInt((h == 0 ? 0 : 30 + x * 5) / 1000f * 65535);
                    textures[0].SetPixelData(raw, 0); textures[0].Apply();
                    var excluded = new MapData(textures, sizes, 1000, 20, 4000, map.SourcePoint);
                    Check(stage.Generate(excluded, CancellationToken.None).Placements.Count == 0, h == 0 ? "No underwater trees" : "No trees on steep slopes");
                }
                var source = UnityEngine.Object.FindObjectsByType<MapGenerator>(FindObjectsSortMode.None).FirstOrDefault(g => g != generator && g.gameObject.scene != scene);
                if (source != null)
                {
                    var clone = UnityEngine.Object.Instantiate(source.gameObject, root.transform).GetComponent<MapGenerator>();
                    var sourceSettings = clone.Settings;
                    foreach (var level in sourceSettings.Levels) level.Resolution = new Vector2Int(513, 513);
                    clone.Configure(sourceSettings); TreeSetup.Configure(clone, builder);
                    var workbench = UnityEngine.Object.FindAnyObjectByType<TacticalMaps.Testing.MapWorkbench>();
                    var generated = await clone.GenerateAsync(workbench != null ? workbench.SelectedPoint : new Vector2Int(2684, 2140));
                    try
                    {
                        Check(generated.Trees != null && generated.Trees.Coverage <= 0.300001f, "Full asynchronous generation facade includes forest data");
                        builder.Build(generated);
                        Check(builder.GetComponentsInChildren<Terrain>().First(t => t.name == "Terrain_Main").terrainData.treeInstanceCount == generated.Trees.Placements.Count, "Real source map constructs its generated trees");
                    }
                    finally { generated.Release(); }
                }
                File.WriteAllText(report, $"PASS: {first.Placements.Count} trees, {groups.Length} groups, {first.Coverage:P3} forest area. Determinism, async, cancellation, area budgets, spacing, groups, water, slopes, rock blockers, Terrain construction, offsets, mesh LOD, rebuild lifetime and full source-map generation.\n");
                Debug.Log("Tree placement checks passed.");
            }
            catch (Exception e) { File.WriteAllText(report, e.ToString()); Debug.LogException(e); }
            finally { UnityEngine.Object.DestroyImmediate(root); map?.Release(); EditorSceneManager.ClosePreviewScene(scene); }
        }
        private static void Same(TreeData a, TreeData b)
        {
            Check(a.Placements.Count == b.Placements.Count && a.ForestMask.SequenceEqual(b.ForestMask), "Deterministic mask and count");
            for (int i = 0; i < a.Placements.Count; i++)
            {
                var p = a.Placements[i]; var q = b.Placements[i];
                Check(p.Position == q.Position && p.Prefab == q.Prefab && p.Group == q.Group && p.Rotation == q.Rotation && p.WidthScale == q.WidthScale && p.HeightScale == q.HeightScale, "Deterministic point attributes");
            }
        }
        private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
