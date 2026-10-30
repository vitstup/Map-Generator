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
                var all = stage.Generate(map, CancellationToken.None); var second = stage.Generate(map, CancellationToken.None);
                var first = new TreeData(all.Prefabs.ToArray(), all.Placements.Where(p => p.Level == 0).ToArray(), all.Zones.ToArray());
                Check(first.Placements.Count > 200, "Flat rectangular main terrain receives groves");
                Check(first.Coverage > 0.20f && first.Coverage <= 0.300001f, "Forest union area stays within 30% of full main terrain");
                Same(all, second);
                Same(all, await stage.GenerateAsync(map, CancellationToken.None));
                var counts = new int[3];
                foreach (var p in all.Placements)
                {
                    counts[p.Level]++;
                    if (p.Level > 0) Check(Mathf.Abs(p.Position.x) > sizes[p.Level - 1].x * 0.5f || Mathf.Abs(p.Position.z) > sizes[p.Level - 1].y * 0.5f, "No duplicate trees in hidden terrain interiors");
                }
                Check(counts.All(c => c > 0), "All three terrain zones receive trees");
                Check(all.Placements.GroupBy(p => (p.Level, p.Group)).All(g => g.Count() >= 3), "Background trees also remain in groups");
                for (int level = 1; level < 3; level++)
                {
                    float ringFraction = 1 - sizes[level - 1].x * sizes[level - 1].y / (sizes[level].x * sizes[level].y);
                    Check(all.Zones[level].Coverage <= 0.300001f * ringFraction, "Background forest mask respects the ring area budget");
                }
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
                var complete = new MapData(textures, sizes, 400, 20, 4000, map.SourcePoint, trees: all);
                var config = new SerializedObject(builder); config.FindProperty("center").vector3Value = new Vector3(137, 53, -219); config.ApplyModifiedPropertiesWithoutUndo();
                builder.Build(complete);
                var terrains = builder.GetComponentsInChildren<Terrain>(); var main = terrains.First(t => t.name == "Terrain_Main");
                Check(main.terrainData.treeInstanceCount == first.Placements.Count && main.terrainData.treePrototypes.Length == first.Prefabs.Count, "Native Terrain instances and prototypes");
                Check(terrains.All(t => !t.drawTreesAndFoliage), "No duplicate native Terrain tree draws");
                Check(terrains.All(t => t.terrainData.treeInstanceCount > 0), "All levels retain their placement data");
                Check(builder.GetComponentInChildren<InstancedTreeRenderer>().TreeCount == all.Placements.Count, "GPU renderer owns all three terrain zones");
                var library = AssetDatabase.LoadAssetAtPath<TreeRenderLibrary>(TreeInstancingSetup.Folder + "/TreeRenderLibrary.asset");
                Check(library.SpeciesList.All(s => (s.originalLevels ?? s.levels).Length == s.source.GetComponentInChildren<LODGroup>().lodCount), "Every original LOD is preserved for authoring and A/B profiling");
                Check(library.ImpostorMesh.triangles.Length == 6 && library.ImpostorCulling.HasKernel("CullTrees"), "Two-triangle distant mesh and GPU culling kernel");
                Check(library.SpeciesList.All(s => s.impostorMaterial != null && s.levels.All(l => l.parts != null && l.parts.All(p => p.mesh != null && p.material != null))), "Runtime library uses direct mesh/material records");
                foreach (var species in library.SpeciesList)
                {
                    var original = species.source.GetComponentInChildren<LODGroup>().GetLODs();
                    var preserved = species.originalLevels != null && species.originalLevels.Length > 0 ? species.originalLevels : species.levels;
                    for (int i = 0; i < original.Length; i++)
                    {
                        Check(preserved[i].screenHeight == original[i].screenRelativeTransitionHeight, "Original authoring LOD thresholds preserved");
                        Check(preserved[i].prefab.GetComponentsInChildren<MeshFilter>().Select(f => f.sharedMesh).SequenceEqual(
                            original[i].renderers.Select(r => r.GetComponent<MeshFilter>().sharedMesh)), "All LOD meshes, including separate branch renderers, preserved");
                    }
                }
                foreach (var species in library.SpeciesList) foreach (var level in species.levels)
                    Check(level.parts.All(p => p.material.enableInstancing), "All runtime LOD materials enable instancing");
                Check(InstancedTreeRenderer.SelectLod(new[] { 0.5f, 0.2f, 0.01f }, 0.6f) == 0 &&
                    InstancedTreeRenderer.SelectLod(new[] { 0.5f, 0.2f, 0.01f }, 0.3f) == 1 &&
                    InstancedTreeRenderer.SelectLod(new[] { 0.5f, 0.2f, 0.01f }, 0.05f) == 2 &&
                    InstancedTreeRenderer.SelectLod(new[] { 0.5f, 0.2f, 0.01f }, 0.001f) == -1, "Screen-size LOD0/1/2 selection and small-object culling");
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
                    Check(stage.Generate(excluded, CancellationToken.None).Placements.All(p => p.Level != 0), h == 0 ? "No underwater trees" : "No trees on steep slopes");
                }
                var source = UnityEngine.Object.FindObjectsByType<MapGenerator>(FindObjectsSortMode.None).FirstOrDefault(g => g != generator && g.gameObject.scene != scene);
                if (source != null)
                {
                    // The real generation facade now also produces roads and farms. Give the
                    // isolated builder their shared resources before checking a complete map.
                    var sourceBuilder = UnityEngine.Object.FindObjectsByType<MapBuilder>(FindObjectsSortMode.None).FirstOrDefault(b => b.gameObject.scene != scene);
                    if (sourceBuilder != null)
                    {
                        var roads = sourceBuilder.GetComponent<RoadBuilder>();
                        if (roads != null) EditorUtility.CopySerialized(roads, root.AddComponent<RoadBuilder>());
                        var farms = sourceBuilder.GetComponent<AgricultureBuilder>();
                        if (farms != null) EditorUtility.CopySerialized(farms, root.AddComponent<AgricultureBuilder>());
                    }
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
                        Check(builder.GetComponentsInChildren<Terrain>().Sum(t => t.terrainData.treeInstanceCount) == generated.Trees.Placements.Count, "Real source map constructs trees on every level");
                    }
                    finally { generated.Release(); }
                }
                File.WriteAllText(report, $"PASS: trees by level {string.Join(",", counts)}; main {groups.Length} groups, {first.Coverage:P3} forest area. Determinism, async, cancellation, ring exclusion/area budgets, spacing, groups, water, slopes, rock blockers, GPU renderer construction, all original LODs, instancing materials, offsets, rebuild lifetime and full source-map generation.\n");
                Debug.Log("Tree placement checks passed.");
            }
            catch (Exception e) { File.WriteAllText(report, e.ToString()); Debug.LogException(e); }
            finally { UnityEngine.Object.DestroyImmediate(root); map?.Release(); EditorSceneManager.ClosePreviewScene(scene); }
        }
        private static void Same(TreeData a, TreeData b)
        {
            Check(a.Placements.Count == b.Placements.Count && a.Zones.Zip(b.Zones, (x, y) => x.Mask.SequenceEqual(y.Mask)).All(same => same), "Deterministic masks and count");
            for (int i = 0; i < a.Placements.Count; i++)
            {
                var p = a.Placements[i]; var q = b.Placements[i];
                Check(p.Position == q.Position && p.Level == q.Level && p.Prefab == q.Prefab && p.Group == q.Group && p.Rotation == q.Rotation && p.WidthScale == q.WidthScale && p.HeightScale == q.HeightScale, "Deterministic point attributes");
            }
        }
        private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
