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
        [SerializeField, Min(1)] private float treeDistance = 6000;
        [SerializeField, Min(16)] private float cullingCellSize = 128;
        [SerializeField, Min(0)] private float shadowDistance = 100;
        [SerializeField, Range(0.02f, 0.3f), Tooltip("Use full meshes only above this fraction of screen height.")] private float meshScreenHeight = 0.2f;
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
            try { root.AddComponent<InstancedTreeRenderer>().Configure(terrains, renderLibrary, treeDistance, cullingCellSize, shadowDistance, meshScreenHeight, minimumImpostorPixels); }
            catch
            {
                if (Application.isPlaying) Destroy(root); else DestroyImmediate(root);
                throw;
            }
        }
    }
}
