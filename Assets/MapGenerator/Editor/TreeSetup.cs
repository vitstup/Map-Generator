using System;
using System.Threading;
using TacticalMaps.Construction;
using TacticalMaps.Generation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class TreeSetup
    {
        internal static void Configure(MapGenerator generator, MapBuilder builder)
        {
            var trees = generator.GetComponent<TreePlacementGenerator>();
            if (trees == null) trees = Undo.AddComponent<TreePlacementGenerator>(generator.gameObject);
            var treeBuilder = builder.GetComponent<TreeBuilder>();
            if (treeBuilder == null) treeBuilder = Undo.AddComponent<TreeBuilder>(builder.gameObject);
            var settings = new SerializedObject(trees); var prefabs = settings.FindProperty("treePrefabs");
            if (prefabs.arraySize == 0)
            {
                var names = new[] { "Pine_01", "Pine_02", "Pine_03", "Maple_01", "Maple_02", "Ginko_01" };
                prefabs.arraySize = names.Length;
                for (int i = 0; i < names.Length; i++)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RockSetup.PrefabFolder + "SM_Env_Tree_" + names[i] + ".prefab");
                    if (prefab == null) throw new InvalidOperationException("Missing Samurai tree " + names[i]);
                    prefabs.GetArrayElementAtIndex(i).objectReferenceValue = prefab;
                }
            }
            var bamboo = settings.FindProperty("bambooPrefabs");
            if (bamboo.arraySize == 0)
            {
                var names = new[] { "01", "03", "04" }; bamboo.arraySize = names.Length;
                for (int i = 0; i < names.Length; i++)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RockSetup.PrefabFolder + "SM_Env_Bamboo_" + names[i] + ".prefab");
                    if (prefab == null) throw new InvalidOperationException("Missing Samurai bamboo " + names[i]);
                    bamboo.GetArrayElementAtIndex(i).objectReferenceValue = prefab;
                }
            }
            settings.ApplyModifiedProperties();
            var sources = new GameObject[prefabs.arraySize + bamboo.arraySize];
            for (int i = 0; i < prefabs.arraySize; i++) sources[i] = (GameObject)prefabs.GetArrayElementAtIndex(i).objectReferenceValue;
            for (int i = 0; i < bamboo.arraySize; i++) sources[prefabs.arraySize + i] = (GameObject)bamboo.GetArrayElementAtIndex(i).objectReferenceValue;
            var render = new SerializedObject(treeBuilder);
            render.FindProperty("renderLibrary").objectReferenceValue = TreeInstancingSetup.EnsureLibrary(sources);
            render.ApplyModifiedProperties();
            var g = new SerializedObject(generator); g.FindProperty("treePlacementGenerator").objectReferenceValue = trees; g.ApplyModifiedProperties();
            var b = new SerializedObject(builder); b.FindProperty("treeBuilder").objectReferenceValue = treeBuilder; b.ApplyModifiedProperties();
        }

        private static bool rebuilding;

        [MenuItem("Tools/Tactical Maps/Configure And Rebuild Trees")]
        private static async void Rebuild()
        {
            if (rebuilding) return;
            rebuilding = true;
            const string report = "Temp/tree-preview-result.txt";
            System.IO.File.WriteAllText(report, "Running...\n");
            MapData map = null;
            var textures = new Texture2D[3];
            try
            {
                if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
                var generator = UnityEngine.Object.FindAnyObjectByType<MapGenerator>();
                var builder = UnityEngine.Object.FindAnyObjectByType<MapBuilder>();
                if (generator == null || builder == null) throw new InvalidOperationException("MapGenerator and MapBuilder are required.");
                Configure(generator, builder);
                var built = new SerializedObject(builder).FindProperty("builtRoot").objectReferenceValue as Transform;
                if (built == null) throw new InvalidOperationException("Build terrain first.");
                var names = new[] { "Terrain_Main", "Terrain_Surroundings", "Terrain_Horizon" };
                var terrains = new Terrain[3]; var sizes = new Vector2[3]; float height = 0;
                for (int level = 0; level < 3; level++)
                {
                    terrains[level] = built.Find(names[level]).GetComponent<Terrain>(); var data = terrains[level].terrainData;
                    height = data.size.y; sizes[level] = new Vector2(data.size.x, data.size.z);
                    int sourceResolution = data.heightmapResolution, n = Mathf.Min(1025, sourceResolution);
                    var heights = data.GetHeights(0, 0, sourceResolution, sourceResolution); var raw = new ushort[n * n];
                    for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
                        raw[z * n + x] = (ushort)Mathf.RoundToInt(heights[z * (sourceResolution - 1) / (n - 1), x * (sourceResolution - 1) / (n - 1)] * 65535);
                    textures[level] = new Texture2D(n, n, TextureFormat.R16, false, true);
                    textures[level].SetPixelData(raw, 0); textures[level].Apply();
                }
                var workbench = UnityEngine.Object.FindAnyObjectByType<TacticalMaps.Testing.MapWorkbench>();
                var point = workbench != null ? workbench.SelectedPoint : Vector2Int.zero;
                map = new MapData(textures, sizes, height, generator.Settings.SeaLevelMeters, generator.Settings.GlobalMaxHeightMeters, point);
                var origin = terrains[0].transform.localPosition + new Vector3(sizes[0].x * 0.5f, 0, sizes[0].y * 0.5f);
                var blockers = new System.Collections.Generic.List<Bounds>(); var rocks = built.Find("Rocks");
                if (rocks != null) foreach (var renderer in rocks.GetComponentsInChildren<Renderer>(true))
                {
                    var world = renderer.bounds; var local = new Bounds(built.InverseTransformPoint(world.center) - origin, Vector3.zero);
                    for (int corner = 0; corner < 8; corner++)
                    {
                        var p = world.center + Vector3.Scale(world.extents, new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                        local.Encapsulate(built.InverseTransformPoint(p) - origin);
                    }
                    local.Expand(4); blockers.Add(local);
                }
                var trees = await generator.GetComponent<TreePlacementGenerator>().GenerateAsync(map, CancellationToken.None, blockers.ToArray());
                var previous = built.Find("Trees");
                var previousInstances = new TreeInstance[3][]; var previousPrototypes = new TreePrototype[3][]; var previousFlags = new bool[3];
                for (int i = 0; i < 3; i++)
                {
                    var data = terrains[i].terrainData;
                    previousInstances[i] = data.treeInstances; previousPrototypes[i] = data.treePrototypes; previousFlags[i] = terrains[i].drawTreesAndFoliage;
                    Undo.RecordObject(data, "Rebuild instanced trees"); Undo.RecordObject(terrains[i], "Disable native tree drawing");
                }
                try { builder.GetComponent<TreeBuilder>().Generate(new MapData(textures, sizes, height, map.SeaLevelMeters, map.GlobalMaxHeightMeters, point, trees: trees), built); }
                catch
                {
                    for (int i = 0; i < 3; i++)
                    {
                        terrains[i].terrainData.treePrototypes = previousPrototypes[i]; terrains[i].terrainData.SetTreeInstances(previousInstances[i], false); terrains[i].drawTreesAndFoliage = previousFlags[i];
                    }
                    throw;
                }
                if (previous != null) Undo.DestroyObjectImmediate(previous.gameObject);
                var next = built.Find("Trees"); if (next != null) Undo.RegisterCreatedObjectUndo(next.gameObject, "Create instanced trees");
                for (int i = 0; i < 3; i++) { EditorUtility.SetDirty(terrains[i].terrainData); EditorUtility.SetDirty(terrains[i]); }
                EditorUtility.SetDirty(generator); EditorUtility.SetDirty(builder);
                AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(builder.gameObject.scene); EditorSceneManager.SaveScene(builder.gameObject.scene);
                var counts = new int[3]; foreach (var p in trees.Placements) counts[p.Level]++;
                string message = $"PASS: GPU-instanced trees by level: {string.Join(", ", counts)}; main forest zones {trees.Coverage:P2}.";
                System.IO.File.WriteAllText(report, message); Debug.Log(message);
            }
            catch (Exception e) { System.IO.File.WriteAllText(report, e.ToString()); Debug.LogException(e); }
            finally
            {
                if (map != null) map.Release(); else foreach (var texture in textures) if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                rebuilding = false;
            }
        }
    }
}
