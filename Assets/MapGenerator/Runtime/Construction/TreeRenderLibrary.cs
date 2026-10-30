using System;
using UnityEngine;

namespace TacticalMaps.Construction
{
    // Shared assets only: no instances, runtime materials or per-map resources.
    internal sealed class TreeRenderLibrary : ScriptableObject
    {
        [SerializeField] private Species[] species = Array.Empty<Species>();
        [SerializeField] private ComputeShader impostorCulling;
        [SerializeField] private Mesh impostorMesh;
        [SerializeField] private int optimizationVersion;
        internal Species[] SpeciesList => species;
        internal ComputeShader ImpostorCulling => impostorCulling;
        internal Mesh ImpostorMesh => impostorMesh;
        internal int OptimizationVersion => optimizationVersion;

        [Serializable]
        internal sealed class Species
        {
            public GameObject source;
            public Vector3 referencePoint, rootScale = Vector3.one;
            public float size;
            public Level[] levels;
            // Retained for reproducible A/B profiling and authoring; never mutated by optimization.
            public Level[] originalLevels;
            public Material impostorMaterial;
            public Vector4 impostorCapture;
        }
        [Serializable]
        internal sealed class Level
        {
#if UNITY_EDITOR
            // Optional authoring reference; player rendering uses the mesh/material records below.
            public GameObject prefab;
#endif
            public MeshPart[] parts;
            public float screenHeight;
        }

        [Serializable]
        internal sealed class MeshPart
        {
            public Mesh mesh;
            public Material material;
            public int submesh;
            public Matrix4x4 localToRoot;
        }

#if UNITY_EDITOR
        internal void Configure(Species[] entries) => species = entries;
        internal void MarkOptimized(int version) => optimizationVersion = version;
        internal void ConfigureImpostors(ComputeShader culling, Mesh mesh) { impostorCulling = culling; impostorMesh = mesh; }
#endif
    }
}
