using System;
using System.Threading;
using TacticalMaps.Construction;
using TacticalMaps.Generation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class RockSetup
    {
        internal const string PrefabFolder = "Assets/Synty/PolygonSamuraiEmpire/Prefabs/Environment/";

        internal static void Configure(MapGenerator generator, MapBuilder builder)
        {
            var rocks = generator.GetComponent<RockPlacementGenerator>();
            if (rocks == null) rocks = Undo.AddComponent<RockPlacementGenerator>(generator.gameObject);
            if (builder.GetComponent<RockBuilder>() == null) Undo.AddComponent<RockBuilder>(builder.gameObject);
            var config = new SerializedObject(rocks);
            // The original 5m default produced pebble-sized details on kilometre-scale maps.
            if (Mathf.Approximately(config.FindProperty("minimumRelief").floatValue, 5))
                config.FindProperty("minimumRelief").floatValue = 80;
            var prefabs = config.FindProperty("cliffPrefabs");
            bool oldWallLibrary = prefabs.arraySize > 0;
            for (int i = 0; i < prefabs.arraySize; i++)
            {
                var assigned = prefabs.GetArrayElementAtIndex(i).objectReferenceValue;
                if (assigned == null || !assigned.name.StartsWith("SM_Env_Rock_Cliff_", StringComparison.Ordinal)) oldWallLibrary = false;
            }
            if (prefabs.arraySize == 0 || oldWallLibrary)
            {
                if (oldWallLibrary && Mathf.Approximately(config.FindProperty("minimumSlope").floatValue, 30))
                    config.FindProperty("minimumSlope").floatValue = 20;
                if (oldWallLibrary)
                {
                    config.FindProperty("minimumRelief").floatValue = 80;
                    config.FindProperty("minimumSectionWidth").floatValue = 180;
                    config.FindProperty("sectionOverlap").floatValue = 0.42f;
                    config.FindProperty("minimumEscarpmentLength").floatValue = 350;
                }
                prefabs.arraySize = 6;
                for (int i = 0; i < 6; i++)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "SM_Env_Cliff_Island_" + (i + 3).ToString("00") + ".prefab");
                    if (prefab == null) throw new InvalidOperationException("Missing Samurai cliff prefab " + (i + 1));
                    prefabs.GetArrayElementAtIndex(i).objectReferenceValue = prefab;
                }
            }
            config.ApplyModifiedProperties();
            var generatorConfig = new SerializedObject(generator);
            generatorConfig.FindProperty("rockPlacementGenerator").objectReferenceValue = rocks;
            generatorConfig.ApplyModifiedProperties();
            var builderConfig = new SerializedObject(builder);
            builderConfig.FindProperty("rockBuilder").objectReferenceValue = builder.GetComponent<RockBuilder>();
            builderConfig.ApplyModifiedProperties();
        }

        [MenuItem("Tools/Tactical Maps/Configure And Rebuild Rocks")]
        private static void Rebuild()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            var generator = UnityEngine.Object.FindAnyObjectByType<MapGenerator>();
            var builder = UnityEngine.Object.FindAnyObjectByType<MapBuilder>();
            if (generator == null || builder == null) throw new InvalidOperationException("MapGenerator and MapBuilder are required.");
            Configure(generator, builder);
            var serialized = new SerializedObject(builder);
            var built = serialized.FindProperty("builtRoot").objectReferenceValue as Transform;
            if (built == null) throw new InvalidOperationException("Build terrain first.");
            var names = new[] { "Terrain_Main", "Terrain_Surroundings", "Terrain_Horizon" };
            var textures = new Texture2D[3]; var sizes = new Vector2[3];
            MapData map = null;
            try
            {
                float height = 0;
                for (int level = 0; level < 3; level++)
                {
                    var terrain = built.Find(names[level]).GetComponent<Terrain>(); var data = terrain.terrainData;
                    height = data.size.y; sizes[level] = new Vector2(data.size.x, data.size.z);
                    int n = data.heightmapResolution; var heights = data.GetHeights(0, 0, n, n); var raw = new ushort[n * n];
                    for (int z = 0; z < n; z++) for (int x = 0; x < n; x++) raw[z * n + x] = (ushort)Mathf.RoundToInt(heights[z, x] * 65535);
                    textures[level] = new Texture2D(n, n, TextureFormat.R16, false, true);
                    textures[level].SetPixelData(raw, 0); textures[level].Apply();
                }
                var workbench = UnityEngine.Object.FindAnyObjectByType<TacticalMaps.Testing.MapWorkbench>();
                map = new MapData(textures, sizes, height, generator.Settings.SeaLevelMeters, generator.Settings.GlobalMaxHeightMeters,
                    workbench != null ? workbench.SelectedPoint : Vector2Int.zero);
                var placements = generator.GetComponent<RockPlacementGenerator>().Generate(map, CancellationToken.None);
                var complete = new MapData(textures, sizes, height, map.SeaLevelMeters, map.GlobalMaxHeightMeters, map.SourcePoint, rocks: placements);
                var next = new GameObject("RockPreviewStaging"); next.transform.SetParent(built, false); next.SetActive(false);
                try
                {
                    builder.GetComponent<RockBuilder>().Generate(complete, next.transform, serialized.FindProperty("center").vector3Value);
                    var replacement = next.transform.Find("Rocks");
                    var previous = built.Find("Rocks");
                    if (previous != null) Undo.DestroyObjectImmediate(previous.gameObject);
                    replacement.SetParent(built, false); Undo.RegisterCreatedObjectUndo(replacement.gameObject, "Rebuild rocks");
                }
                finally { UnityEngine.Object.DestroyImmediate(next); }
                EditorUtility.SetDirty(generator); EditorUtility.SetDirty(builder);
                var scene = builder.gameObject.scene; EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                System.IO.File.WriteAllText("Temp/rock-preview-result.txt", "PASS: " + placements.Placements.Count + " cliff instances under GeneratedMap/Rocks.");
                Debug.Log("Rocks rebuilt: " + placements.Placements.Count);
            }
            finally
            {
                if (map != null) map.Release();
                else foreach (var texture in textures) if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
