using UnityEngine;

namespace TacticalMaps.Construction
{
    internal sealed class FarmBuildingGrounding : MonoBehaviour
    {
        [SerializeField] internal Vector2 halfFootprint = new Vector2(8, 8);
        [SerializeField] internal Vector3[] supports;
    }
}
