using TacticalMaps.Generation;
using UnityEngine;

namespace TacticalMaps.Construction
{
    [DisallowMultipleComponent]
    internal sealed class RockBuilder : MonoBehaviour
    {
        [SerializeField, LayerIndex] private int rockLayer;

        internal void Generate(MapData map, Transform parent, Vector3 center)
        {
            var root = new GameObject("Rocks"); root.transform.SetParent(parent, false);
            if (map.Rocks == null) return;
            if (rockLayer < 0 || rockLayer > 31) throw new System.InvalidOperationException("Rock layer must be 0–31.");
            var offsets = new float[3];
            var terrainRoot = parent.Find("Terrain_Main") != null ? parent : parent.parent;
            var names = new[] { "Terrain_Main", "Terrain_Surroundings", "Terrain_Horizon" };
            if (terrainRoot != null) for (int i = 0; i < 3; i++)
            {
                var terrain = terrainRoot.Find(names[i]);
                if (terrain != null) offsets[i] = terrain.localPosition.y - center.y;
            }
            foreach (var placement in map.Rocks.Placements)
            {
                var prefab = map.Rocks.Prefabs[placement.Prefab];
                GameObject instance;
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    instance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, root.transform);
                else
#endif
                    instance = Instantiate(prefab, root.transform);
                instance.transform.localPosition = center + placement.Position + Vector3.up * offsets[placement.Level];
                instance.transform.localRotation = placement.Rotation;
                instance.transform.localScale = placement.Scale;
                foreach (var t in instance.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = rockLayer;
                // Borrow mesh/material assets; only the scene instances belong to this builder.
                foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.sharedMesh != null && filter.GetComponent<Collider>() == null)
                        filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                { collider.enabled = true; collider.isTrigger = false; }
            }
        }
    }
}
