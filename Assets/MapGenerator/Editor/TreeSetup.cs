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
            settings.ApplyModifiedProperties();
            var g = new SerializedObject(generator); g.FindProperty("treePlacementGenerator").objectReferenceValue = trees; g.ApplyModifiedProperties();
            var b = new SerializedObject(builder); b.FindProperty("treeBuilder").objectReferenceValue = treeBuilder; b.ApplyModifiedProperties();
        }

        [MenuItem("Tools/Tactical Maps/Configure And Rebuild Trees")]
        private static void Rebuild()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            var generator = UnityEngine.Object.FindAnyObjectByType<MapGenerator>();
            var builder = UnityEngine.Object.FindAnyObjectByType<MapBuilder>();
            if (generator == null || builder == null) throw new InvalidOperationException("MapGenerator and MapBuilder are required.");
            Configure(generator, builder);
            var built = new SerializedObject(builder).FindProperty("builtRoot").objectReferenceValue as Transform;
            if (built == null || built.Find("Terrain_Main") == null) throw new InvalidOperationException("Build terrain first.");
            var terrain = built.Find("Terrain_Main").GetComponent<Terrain>(); var data = terrain.terrainData;
            int n = data.heightmapResolution; var heights = data.GetHeights(0, 0, n, n); var raw = new ushort[n * n];
            for (int z = 0; z < n; z++) for (int x = 0; x < n; x++) raw[z * n + x] = (ushort)Mathf.RoundToInt(heights[z, x] * 65535);
            var texture = new Texture2D(n, n, TextureFormat.R16, false, true);
            texture.SetPixelData(raw, 0); texture.Apply();
            try
            {
                var textures = new[] { texture, texture, texture };
                var sizes = new[] { new Vector2(data.size.x, data.size.z), new Vector2(data.size.x * 4, data.size.z * 4), new Vector2(data.size.x * 10, data.size.z * 10) };
                var workbench = UnityEngine.Object.FindAnyObjectByType<TacticalMaps.Testing.MapWorkbench>();
                var point = workbench != null ? workbench.SelectedPoint : Vector2Int.zero;
                var map = new MapData(textures, sizes, data.size.y, generator.Settings.SeaLevelMeters, generator.Settings.GlobalMaxHeightMeters, point);
                // Use actual saved rock bounds, including any manually edited preview transforms.
                var rocks = built.Find("Rocks");
                var blockers = new System.Collections.Generic.List<Bounds>();
                if (rocks != null) foreach (var renderer in rocks.GetComponentsInChildren<Renderer>(true))
                {
                    var world = renderer.bounds;
                    var local = new Bounds(world.center - terrain.transform.position - new Vector3(data.size.x * 0.5f, 0, data.size.z * 0.5f), world.size);
                    local.Expand(4); blockers.Add(local);
                }
                var trees = generator.GetComponent<TreePlacementGenerator>().Generate(map, CancellationToken.None, blockers.ToArray());
                Undo.RecordObject(data, "Rebuild Terrain trees"); Undo.RecordObject(terrain, "Configure Terrain trees");
                builder.GetComponent<TreeBuilder>().Generate(new MapData(textures, sizes, data.size.y, map.SeaLevelMeters, map.GlobalMaxHeightMeters, point, trees: trees), built);
                EditorUtility.SetDirty(data); EditorUtility.SetDirty(terrain); EditorUtility.SetDirty(generator); EditorUtility.SetDirty(builder);
                AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(builder.gameObject.scene); EditorSceneManager.SaveScene(builder.gameObject.scene);
                System.IO.File.WriteAllText("Temp/tree-preview-result.txt", $"PASS: {trees.Placements.Count} Terrain trees; forest zones {trees.Coverage:P2} of main terrain.");
                Debug.Log($"Trees rebuilt: {trees.Placements.Count}; forest area {trees.Coverage:P2}.");
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }
    }
}

