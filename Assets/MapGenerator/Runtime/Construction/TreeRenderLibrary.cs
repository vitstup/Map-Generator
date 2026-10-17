using System;
using UnityEngine;

namespace TacticalMaps.Construction
{
    // Shared assets only: no instances, runtime materials or per-map resources.
    internal sealed class TreeRenderLibrary : ScriptableObject
    {
        [SerializeField] private Species[] species = Array.Empty<Species>();
        internal Species[] SpeciesList => species;

        [Serializable]
        internal sealed class Species
        {
            public GameObject source;
            public Vector3 referencePoint, rootScale = Vector3.one;
            public float size;
            public Level[] levels;
        }
        [Serializable]
        internal sealed class Level
        {
            public GameObject prefab;
            public float screenHeight;
        }

#if UNITY_EDITOR
        internal void Configure(Species[] entries) => species = entries;
#endif
    }
}
