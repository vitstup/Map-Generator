using UnityEngine;

namespace TacticalMaps.Construction
{
    internal sealed class AgricultureLibrary : ScriptableObject
    {
        [SerializeField] internal GameObject[] houses;
        [SerializeField] internal GameObject[] fences;
        [SerializeField] internal Mesh closeRice, distantRice;
        [SerializeField] internal Material cropMaterial, bankMaterial, waterMaterial, supportMaterial;
        [SerializeField] internal GameObject[] props;
        [SerializeField] internal float[] propRadii;
        [SerializeField] internal int waterVersion;
        [SerializeField] internal ComputeShader culling;
        [SerializeField] internal TerrainLayer soilProfile;
    }
}
