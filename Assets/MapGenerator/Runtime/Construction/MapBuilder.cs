using System;
using System.Collections.Generic;
using TacticalMaps.Generation;
using UnityEngine;

namespace TacticalMaps.Construction
{
    [RequireComponent(typeof(TerrainGenerator), typeof(WaterGenerator))]
    public sealed class MapBuilder : MonoBehaviour
    {
        [SerializeField] private Vector3 center;
        [SerializeField] private TerrainGenerator terrainGenerator;
        [SerializeField] private WaterGenerator waterGenerator;
        [SerializeField, HideInInspector] private Transform builtRoot;
        [SerializeField, HideInInspector] private UnityEngine.Object[] builtResources;

        public void Build(MapData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (terrainGenerator == null) terrainGenerator = GetComponent<TerrainGenerator>();
            if (waterGenerator == null) waterGenerator = GetComponent<WaterGenerator>();
            if (terrainGenerator == null || waterGenerator == null || terrainGenerator.gameObject != gameObject || waterGenerator.gameObject != gameObject)
                throw new InvalidOperationException("Construction components must be on the MapBuilder object.");
            var next = new GameObject("GeneratedMap");
            next.transform.SetParent(transform, false); next.SetActive(false);
            try
            {
                terrainGenerator.Generate(data, next.transform, center);
                waterGenerator.Generate(data, next.transform, center);
            }
            catch { Release(next); throw; }
            var previous = builtRoot;
            var previousResources = builtResources;
            builtRoot = next.transform;
            builtResources = CollectResources(next);
            if (previous != null) previous.gameObject.SetActive(false);
            next.SetActive(true);
            Release(previous != null ? previous.gameObject : null, previousResources);
        }

        private static UnityEngine.Object[] CollectResources(GameObject root)
        {
            var resources = new HashSet<UnityEngine.Object>();
            foreach (var terrain in root.GetComponentsInChildren<Terrain>(true)) resources.Add(terrain.terrainData);
            foreach (var mesh in root.GetComponentsInChildren<MeshFilter>(true)) resources.Add(mesh.sharedMesh);
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var material = renderer.sharedMaterial;
                if (material == null) continue;
                resources.Add(material);
                for (int i = 0; i < 3; i++) resources.Add(material.GetTexture("_Depth" + i));
            }
            var array = new UnityEngine.Object[resources.Count]; resources.CopyTo(array); return array;
        }
        private static void Release(GameObject root, UnityEngine.Object[] resources = null)
        {
            if (resources == null && root != null) resources = CollectResources(root);
            if (root != null) Dispose(root);
            if (resources != null) foreach (var resource in resources) Dispose(resource);
        }
        private static void Dispose(UnityEngine.Object value)
        {
            if (value == null) return;
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(value)) return;
#endif
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
        private void OnDestroy() => Release(builtRoot != null ? builtRoot.gameObject : null, builtResources);
    }

    internal sealed class LayerIndexAttribute : PropertyAttribute { }
}
