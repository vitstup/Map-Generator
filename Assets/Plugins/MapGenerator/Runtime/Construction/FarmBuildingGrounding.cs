using UnityEngine;

namespace TacticalMaps.Construction
{
    internal sealed class FarmBuildingGrounding : MonoBehaviour
    {
        [SerializeField] internal Vector2 halfFootprint = new Vector2(8, 8);
        [SerializeField] internal Vector3[] supports;
        [SerializeField] internal float supportHalfWidth = .2f;
        // Height of the underside of the lowest platform relative to the prepared pivot.
        [SerializeField] internal float platformHeight;
        internal const float PlatformClearance = .18f;
    }
}
