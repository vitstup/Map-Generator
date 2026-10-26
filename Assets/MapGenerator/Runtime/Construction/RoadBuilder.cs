using System;
using TacticalMaps.Generation;
using UnityEngine;

namespace TacticalMaps.Construction
{
    [DisallowMultipleComponent]
    internal sealed class RoadBuilder : MonoBehaviour
    {
        [SerializeField] private TerrainLayer largeSurface;
        [SerializeField] private TerrainLayer mediumSurface;
        [SerializeField] private Color largeTint = new Color(.94f,.85f,.68f);
        [SerializeField] private Color mediumTint = new Color(.78f,.66f,.48f);
        internal double LastBuildMilliseconds { get; private set; }
        internal void Generate(MapData data, Transform parent, Vector3 center)
        {
            var roads=data.Roads;if(roads==null||roads.Tiles.Count==0)return;
            var watch=System.Diagnostics.Stopwatch.StartNew();
            if(roads.Tiles.Count>SystemInfo.maxTextureArraySlices)throw new InvalidOperationException("Too many road tiles for this graphics device.");
            var owned=parent.gameObject.AddComponent<RoadMap>();
            owned.Configure(roads,center,data.SeaLevelMeters,largeSurface,mediumSurface,largeTint,mediumTint);
            LastBuildMilliseconds=watch.Elapsed.TotalMilliseconds;
        }
    }
}
