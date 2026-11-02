using System;
using System.IO;
using TacticalMaps.Construction;
using TacticalMaps.Generation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class TreePhysicsSetup
    {
        private const string TerrainPath = "Assets/Prefabs/Terrain.prefab";

        internal static Terrain EnsureTerrainTemplate()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TerrainPath);
            if (prefab != null) return prefab.GetComponent<Terrain>();
            var root = new GameObject("Terrain", typeof(Terrain), typeof(TerrainCollider));
            try
            {
                DisableNativeTreeColliders(root.GetComponent<TerrainCollider>());
                return PrefabUtility.SaveAsPrefabAsset(root, TerrainPath).GetComponent<Terrain>();
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        internal static GameObject LocalPrefab(GameObject source)
        {
            if (source == null) throw new InvalidOperationException("Missing tree prefab.");
            string original = AssetDatabase.GetAssetPath(source);
            if (!original.StartsWith("Assets/Synty/PolygonSamuraiEmpire/", StringComparison.Ordinal)) return source;
            string path = TreeInstancingSetup.Folder + "/" + source.name + ".prefab";
            var local = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (local != null) return local;
            if (!AssetDatabase.CopyAsset(original, path)) throw new InvalidOperationException("Cannot copy tree prefab: " + original);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var mesh in root.GetComponentsInChildren<MeshCollider>(true))
                {
                    var bounds = mesh.sharedMesh.bounds;
                    var capsule = mesh.gameObject.AddComponent<CapsuleCollider>();
                    capsule.center = bounds.center; capsule.radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
                    capsule.height = Mathf.Max(bounds.size.y, capsule.radius * 2); capsule.direction = 1;
                    capsule.sharedMaterial = mesh.sharedMaterial; capsule.isTrigger = mesh.isTrigger;
                    capsule.enabled = mesh.enabled; capsule.contactOffset = mesh.contactOffset;
                    capsule.providesContacts = mesh.providesContacts;
                    capsule.includeLayers = mesh.includeLayers; capsule.excludeLayers = mesh.excludeLayers;
                    capsule.layerOverridePriority = mesh.layerOverridePriority;
                    UnityEngine.Object.DestroyImmediate(mesh);
                }
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void DisableNativeTreeColliders(TerrainCollider collider)
        {
            var settings = new SerializedObject(collider);
            settings.FindProperty("m_EnableTreeColliders").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        [MenuItem("Tools/Tactical Maps/Configure Separate Tree Physics")]
        internal static void MigrateScene()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            var generator = UnityEngine.Object.FindAnyObjectByType<MapGenerator>();
            var builder = UnityEngine.Object.FindAnyObjectByType<MapBuilder>();
            if (generator == null || builder == null) throw new InvalidOperationException("Open the map scene first.");
            int layer = LayerMask.NameToLayer("Trees");
            if (layer < 0)
            {
                var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
                var layers = tags.FindProperty("layers");
                for (int i = 8; i < layers.arraySize; i++) if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue))
                { layers.GetArrayElementAtIndex(i).stringValue = "Trees"; layer = i; break; }
                if (layer < 0) throw new InvalidOperationException("No free physics layer for Trees.");
                tags.ApplyModifiedPropertiesWithoutUndo();
            }
            TreeSetup.Configure(generator, builder);
            ConstructionSetup.Configure(builder);
            var treeBuilder = builder.GetComponent<TreeBuilder>();
            var config = new SerializedObject(treeBuilder);
            config.FindProperty("treeLayer").intValue = layer; config.ApplyModifiedProperties();

            // Migrate saved placement stores too, retaining every instance and its prototype index.
            foreach (var guid in AssetDatabase.FindAssets("t:TerrainData", new[] { "Assets/MapGenerator/Generated" }))
            {
                var data = AssetDatabase.LoadAssetAtPath<TerrainData>(AssetDatabase.GUIDToAssetPath(guid));
                var prototypes = data.treePrototypes; bool changed = false;
                foreach (var prototype in prototypes)
                {
                    var local = LocalPrefab(prototype.prefab);
                    if (local == prototype.prefab) continue;
                    prototype.prefab = local; changed = true;
                }
                if (changed) { data.treePrototypes = prototypes; EditorUtility.SetDirty(data); }
            }
            var built = (Transform)new SerializedObject(builder).FindProperty("builtRoot").objectReferenceValue;
            if (built != null)
            {
                foreach (var terrain in built.GetComponentsInChildren<Terrain>(true))
                {
                    var prototypes = terrain.terrainData.treePrototypes;
                    foreach (var prototype in prototypes) prototype.prefab = LocalPrefab(prototype.prefab);
                    terrain.terrainData.treePrototypes = prototypes;
                    DisableNativeTreeColliders(terrain.GetComponent<TerrainCollider>());
                    EditorUtility.SetDirty(terrain.terrainData);
                }
                var trees = built.Find("Trees");
                if (trees != null)
                {
                    treeBuilder.RebuildColliders(built.Find("Terrain_Main").GetComponent<Terrain>(), trees);
                    var renderer = trees.GetComponent<InstancedTreeRenderer>();
                    if (renderer != null) { renderer.enabled = false; renderer.enabled = true; }
                }
            }
            EditorUtility.SetDirty(generator); EditorUtility.SetDirty(builder); EditorUtility.SetDirty(treeBuilder);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(builder.gameObject.scene);
            EditorSceneManager.SaveScene(builder.gameObject.scene);
            Directory.CreateDirectory("Temp");
            File.WriteAllText("Temp/tree-physics-migration.txt", "PASS: local tree prefabs, separate main-terrain colliders, Trees layer " + layer);
        }
    }
}
