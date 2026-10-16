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

        internal static void SavePreviewResources(MapBuilder builder, string folder, bool replace = false)
        {
            var serialized = new SerializedObject(builder);
            var resources = serialized.FindProperty("builtResources");
            var ordered = new System.Collections.Generic.List<UnityEngine.Object>();
            for (int i = 0; i < resources.arraySize; i++)
                ordered.Add(resources.GetArrayElementAtIndex(i).objectReferenceValue);
            // Save dependencies first: importing a native asset drops references to unsaved objects.
            ordered.Sort((a, b) => Priority(a).CompareTo(Priority(b)));
            int depth = 0;
            foreach (var asset in ordered)
            {
                if (asset == null || AssetDatabase.Contains(asset)) continue;
                string extension = asset is Material ? ".mat" : ".asset";
                string name = asset.name == "WaterDepth" ? asset.name + depth++ : asset.name;
                string path = folder + "/" + name + extension;
                if (replace && AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(asset, replace ? path : AssetDatabase.GenerateUniqueAssetPath(path));
            }
        }
        private static int Priority(UnityEngine.Object asset) => asset is Texture ? 0 : asset is TerrainLayer ? 1 : asset is Material ? 2 : 3;
    }
}
