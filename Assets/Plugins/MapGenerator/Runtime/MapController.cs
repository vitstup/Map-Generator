using System;
using System.Threading.Tasks;
using TacticalMaps.Construction;
using TacticalMaps.Generation;
using TacticalMaps.Grass;
using UnityEngine;

namespace TacticalMaps
{
    [DisallowMultipleComponent]
    public sealed class MapController : MonoBehaviour
    {
        [SerializeField] private MapGenerator generator;
        [SerializeField] private MapBuilder mapBuilder;
        [SerializeField] private GrassSystem grassSystem;

        /// <summary>Generates and builds a map centered on a source pixel. Call on Unity's main thread.</summary>
        public async Task Generate(int x, int y)
        {
            if (generator == null || mapBuilder == null || grassSystem == null)
                throw new InvalidOperationException("Assign MapGenerator, MapBuilder and GrassSystem on MapController.");

            var cancellation = destroyCancellationToken;
            var map = await generator.GenerateAsync(new Vector2Int(x, y), cancellation);
            try
            {
                cancellation.ThrowIfCancellationRequested();
                mapBuilder.Build(map);
                grassSystem.Build(map);
            }
            finally
            {
                map.Release();
            }
        }
    }
}
