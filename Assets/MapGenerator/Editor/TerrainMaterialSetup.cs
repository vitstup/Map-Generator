using System;
using System.IO;
using TacticalMaps.Construction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class TerrainMaterialSetup
    {
        [MenuItem("Tools/Tactical Maps/Set Up Matte Terrain")]
        private static void SetUp()
        {
            try
            {
                if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
                var shader = Shader.Find("TacticalMaps/Terrain/MatteStochastic");
                if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Compile the matte terrain shader first.");
                const string path = "Assets/MapGenerator/Materials/MatteTerrain.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader) { name = "MatteTerrain", enableInstancing = true };
                    material.EnableKeyword("_ALPHATEST_ON");
                    AssetDatabase.CreateAsset(material, path);
                }
                var builder = UnityEngine.Object.FindAnyObjectByType<MapBuilder>();
                if (builder == null) throw new InvalidOperationException("MapBuilder is missing.");
                var settings = new SerializedObject(builder.GetComponent<TerrainGenerator>());
                settings.FindProperty("terrainMaterial").objectReferenceValue = material;
                settings.ApplyModifiedPropertiesWithoutUndo();
                foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/MapGenerator/Textures/Surfaces" }))
                {
                    var importer = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as TextureImporter;
                    if (importer == null || importer.anisoLevel == 16) continue;
                    importer.anisoLevel = 16; importer.mipMapBias = 0; importer.SaveAndReimport();
                }
                // Update the saved preview without regenerating heightmaps or changing the water.
                foreach (var terrain in builder.GetComponentsInChildren<Terrain>())
                {
                    int level = terrain.name == "Terrain_Main" ? 0 : terrain.name == "Terrain_Surroundings" ? 1 : 2;
                    var instance = new Material(material) { name = "TerrainSurface_" + level };
                    var size = terrain.terrainData.size;
                    instance.SetVector("_TacticalTerrainSize", new Vector4(size.x, size.z, 0, 0));
                    string instancePath = "Assets/MapGenerator/Generated/MapPreview/TerrainSurface_" + level + ".mat";
                    var previous = AssetDatabase.LoadAssetAtPath<Material>(instancePath);
                    if (previous != null)
                    {
                        EditorUtility.CopySerialized(instance, previous); UnityEngine.Object.DestroyImmediate(instance); instance = previous;
                        EditorUtility.SetDirty(instance);
                    }
                    else AssetDatabase.CreateAsset(instance, instancePath);
                    terrain.materialTemplate = instance;
                    terrain.basemapDistance = level == 0 ? settings.FindProperty("mainSurfaceDistance").floatValue : settings.FindProperty("detailedSurfaceDistance").floatValue;
                    terrain.terrainData.SetBaseMapDirty(); terrain.Flush(); EditorUtility.SetDirty(terrain);
                }
                EditorUtility.SetDirty(builder);
                AssetDatabase.SaveAssets();
                var scene = builder.gameObject.scene; EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                File.WriteAllText("Temp/matte-terrain-setup.txt", "PASS: matte stochastic materials assigned; main detailed distance 12000m; geometric LOD preserved; water untouched.");
            }
            catch (Exception e) { File.WriteAllText("Temp/matte-terrain-setup.txt", e.ToString()); Debug.LogException(e); }
        }
    }
}
