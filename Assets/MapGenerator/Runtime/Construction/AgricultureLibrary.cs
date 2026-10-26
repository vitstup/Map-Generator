using UnityEngine;

namespace TacticalMaps.Construction
{
    internal sealed class AgricultureLibrary : ScriptableObject
    {
        [SerializeField] internal GameObject[] houses;
        [SerializeField] internal Mesh closeRice, distantRice;
        [SerializeField] internal Material cropMaterial, bankMaterial, waterMaterial;
        [SerializeField] internal ComputeShader culling;
        [SerializeField] internal TerrainLayer soilProfile;
    }
}
