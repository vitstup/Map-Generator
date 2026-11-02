using System;
using System.Collections.Generic;
using TacticalMaps.Generation;
using UnityEngine;

namespace TacticalMaps.Construction
{
    [DisallowMultipleComponent]
    internal sealed class TreeBuilder : MonoBehaviour
    {
        [SerializeField] private TreeRenderLibrary renderLibrary;
        [SerializeField, LayerIndex, Tooltip("Physics layer for the separate main-terrain tree colliders. Background trees have no physics.")] private int treeLayer;
        [SerializeField, Min(1)] private float treeDistance = 6000;
        [SerializeField, Min(16)] private float cullingCellSize = 128;
        [SerializeField, Min(0)] private float shadowDistance = 100;
        [SerializeField, Tooltip("Separate shadow meshes; can be disabled independently of grass and object shadows.")] private bool castShadows = true;
        [SerializeField, Range(0.02f, 0.3f), Tooltip("Biased screen size at the centre of the mesh/impostor cross-fade.")] private float meshScreenHeight = InstancedTreeRenderer.DefaultMeshScreenHeight;
        [SerializeField, Range(0.5f, 4)] private float minimumImpostorPixels = 1.5f;

        internal void Generate(MapData map, Transform parent)
        {
            if (map.Trees == null) return;
            if (map.Trees.Placements.Count > 0 && renderLibrary == null)
                throw new InvalidOperationException("Assign a tree instancing library on TreeBuilder.");
            var trees = map.Trees;
            var terrains = new Terrain[3];
            var names = new[] { "Terrain_Main", "Terrain_Surroundings", "Terrain_Horizon" };
            var instances = new List<TreeInstance>[3];
            for (int level = 0; level < terrains.Length; level++)
            {
                var terrainObject = parent.Find(names[level]);
                if (terrainObject == null) throw new InvalidOperationException("Build all terrains before placing trees.");
                terrains[level] = terrainObject.GetComponent<Terrain>(); instances[level] = new List<TreeInstance>();
            }
            foreach (var p in trees.Placements)
            {
                if (p.Level < 0 || p.Level >= 3) throw new InvalidOperationException("Invalid tree terrain level.");
                var data = terrains[p.Level].terrainData;
                float u = p.Position.x / data.size.x + 0.5f, v = p.Position.z / data.size.z + 0.5f;
                instances[p.Level].Add(new TreeInstance
                {
                    position = new Vector3(u, data.GetInterpolatedHeight(u, v) / data.size.y, v),
                    prototypeIndex = p.Prefab, rotation = p.Rotation,
                    widthScale = p.WidthScale, heightScale = p.HeightScale, color = Color.white, lightmapColor = Color.white
                });
            }
            for (int level = 0; level < terrains.Length; level++)
            {
                var prototypes = new TreePrototype[trees.Prefabs.Count];
                for (int i = 0; i < prototypes.Length; i++) prototypes[i] = new TreePrototype { prefab = trees.Prefabs[i], bendFactor = 0 };
                var terrain = terrains[level]; var data = terrain.terrainData;
                data.treePrototypes = prototypes; data.SetTreeInstances(instances[level].ToArray(), false);
                // Keep only the serialized placement data. The instanced renderer submits all tree draws.
                terrain.drawTreesAndFoliage = false; terrain.treeDistance = treeDistance;
                terrain.treeBillboardDistance = treeDistance; terrain.treeMaximumFullLODCount = instances[level].Count;
                terrain.Flush();
            }
            if (trees.Placements.Count == 0) return;
            var root = new GameObject("Trees"); root.transform.SetParent(parent, false);
            try
            {
                root.AddComponent<InstancedTreeRenderer>().Configure(terrains, renderLibrary, treeDistance, cullingCellSize, shadowDistance, meshScreenHeight, minimumImpostorPixels, castShadows);
                RebuildColliders(terrains[0], root.transform);
            }
            catch
            {
                if (Application.isPlaying) Destroy(root); else DestroyImmediate(root);
                throw;
            }
        }

        // These static objects own physics only. GPU rendering still reads all three TerrainData sets.
        internal void RebuildColliders(Terrain main, Transform root)
        {
            if (treeLayer < 0 || treeLayer > 31) throw new InvalidOperationException("Invalid tree physics layer.");
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i).gameObject;
                child.SetActive(false);
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }
            var data = main.terrainData;
            var prototypes = data.treePrototypes;
            var shapes = new CapsuleCollider[prototypes.Length][];
            for (int i = 0; i < prototypes.Length; i++)
            {
                var prefab = prototypes[i].prefab;
                foreach (var collider in prefab.GetComponentsInChildren<Collider>(true))
                    if (!(collider is CapsuleCollider)) throw new InvalidOperationException(prefab.name + " needs capsule-only tree physics.");
                shapes[i] = prefab.GetComponentsInChildren<CapsuleCollider>(true);
            }
            int index = 0;
            foreach (var tree in data.treeInstances)
            {
                var prefab = prototypes[tree.prototypeIndex].prefab;
                var sources = shapes[tree.prototypeIndex];
                if (sources.Length == 0) continue;
                var instance = new GameObject(prefab.name + "_" + index++) { layer = treeLayer };
                instance.transform.SetParent(root, false);
                var relative = root.worldToLocalMatrix * main.transform.localToWorldMatrix * Matrix4x4.TRS(
                    Vector3.Scale(tree.position, data.size), Quaternion.Euler(0, tree.rotation * Mathf.Rad2Deg, 0),
                    Vector3.Scale(prefab.transform.localScale, new Vector3(tree.widthScale, tree.heightScale, tree.widthScale)));
                instance.transform.localPosition = relative.GetColumn(3);
                instance.transform.localRotation = relative.rotation;
                instance.transform.localScale = relative.lossyScale;
                foreach (var source in sources)
                {
                    if (!source.enabled || !ActiveInPrefab(source.transform, prefab.transform)) continue;
                    var target = instance;
                    if (source.transform != prefab.transform)
                    {
                        target = new GameObject(source.name) { layer = treeLayer };
                        target.transform.SetParent(instance.transform, false);
                        var local = prefab.transform.worldToLocalMatrix * source.transform.localToWorldMatrix;
                        target.transform.localPosition = local.GetColumn(3);
                        target.transform.localRotation = local.rotation;
                        target.transform.localScale = local.lossyScale;
                    }
                    var capsule = target.AddComponent<CapsuleCollider>();
                    capsule.center = source.center; capsule.radius = source.radius; capsule.height = source.height; capsule.direction = source.direction;
                    capsule.sharedMaterial = source.sharedMaterial; capsule.isTrigger = source.isTrigger;
                    capsule.contactOffset = source.contactOffset; capsule.providesContacts = source.providesContacts;
                    capsule.includeLayers = source.includeLayers; capsule.excludeLayers = source.excludeLayers;
                    capsule.layerOverridePriority = source.layerOverridePriority;
                }
            }
        }

        private static bool ActiveInPrefab(Transform node, Transform root)
        {
            // Prefab assets are not active in a scene; activeInHierarchy is always false there.
            for (var current = node; current != null; current = current.parent)
            {
                if (!current.gameObject.activeSelf) return false;
                if (current == root) return true;
            }
            return false;
        }
    }
}
