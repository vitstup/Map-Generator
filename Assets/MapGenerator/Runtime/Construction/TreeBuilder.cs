using System;
using TacticalMaps.Generation;
using UnityEngine;

namespace TacticalMaps.Construction
{
    [DisallowMultipleComponent]
    internal sealed class TreeBuilder : MonoBehaviour
    {
        [SerializeField, Min(0)] private float treeDistance = 6000;
        [SerializeField, Min(0)] private float crossFadeLength = 30;

        internal void Generate(MapData map, Transform parent)
        {
            if (map.Trees == null) return;
            var terrainObject = parent.Find("Terrain_Main");
            if (terrainObject == null) throw new InvalidOperationException("Build the main terrain before placing trees.");
            var terrain = terrainObject.GetComponent<Terrain>(); var data = terrain.terrainData;
            var trees = map.Trees;
            var prototypes = new TreePrototype[trees.Prefabs.Count];
            for (int i = 0; i < prototypes.Length; i++) prototypes[i] = new TreePrototype { prefab = trees.Prefabs[i], bendFactor = 0 };
            data.treePrototypes = prototypes;
            var instances = new TreeInstance[trees.Placements.Count];
            for (int i = 0; i < instances.Length; i++)
            {
                var p = trees.Placements[i];
                float u = p.Position.x / data.size.x + 0.5f, v = p.Position.z / data.size.z + 0.5f;
                instances[i] = new TreeInstance
                {
                    position = new Vector3(u, data.GetInterpolatedHeight(u, v) / data.size.y, v),
                    prototypeIndex = p.Prefab, rotation = p.Rotation,
                    widthScale = p.WidthScale, heightScale = p.HeightScale, color = Color.white, lightmapColor = Color.white
                };
            }
            data.SetTreeInstances(instances, false);
            terrain.drawTreesAndFoliage = true;
            // Samurai prefabs have mesh LODs, without Terrain-compatible billboard assets.
            // Keep them in the mesh path for the whole draw distance and use their own LODGroups.
            terrain.treeDistance = treeDistance; terrain.treeBillboardDistance = treeDistance;
            terrain.treeCrossFadeLength = crossFadeLength; terrain.treeMaximumFullLODCount = instances.Length;
            terrain.Flush();
        }
    }
}
