using System;
using System.IO;
using TacticalMaps.Construction;
using UnityEditor;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class ConstructionSetup
    {
        internal static void Configure(MapBuilder builder)
        {
            const string path = "Assets/MapGenerator/Materials/LowPolyWater.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Directory.CreateDirectory("Assets/MapGenerator/Materials"); AssetDatabase.Refresh();
                var shader = Shader.Find("TacticalMaps/LowPolyWater");
                if (shader == null) throw new InvalidOperationException("Water shader is missing.");
                material = new Material(shader) { name = "LowPolyWater" };
                AssetDatabase.CreateAsset(material, path);
            }
            var terrain = new SerializedObject(builder.GetComponent<TerrainGenerator>());
            terrain.FindProperty("terrainLayer").intValue = Mathf.Max(0, LayerMask.NameToLayer("Terrain"));
            terrain.ApplyModifiedPropertiesWithoutUndo();
            var water = new SerializedObject(builder.GetComponent<WaterGenerator>());
            water.FindProperty("waterLayer").intValue = Mathf.Max(0, LayerMask.NameToLayer("Water"));
            water.FindProperty("waterMaterial").objectReferenceValue = material;
            water.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void SavePreviewResources(MapBuilder builder, string folder)
        {
            var serialized = new SerializedObject(builder);
            var resources = serialized.FindProperty("builtResources");
            for (int i = 0; i < resources.arraySize; i++)
            {
                var asset = resources.GetArrayElementAtIndex(i).objectReferenceValue;
                if (asset == null || AssetDatabase.Contains(asset)) continue;
                string extension = asset is Material ? ".mat" : ".asset";
                AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + asset.name + extension));
            }
        }
    }
}
