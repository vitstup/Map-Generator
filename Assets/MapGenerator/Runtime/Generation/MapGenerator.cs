using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace TacticalMaps.Generation
{
    [RequireComponent(typeof(HeightmapGenerator))]
    [UnityEngine.Scripting.APIUpdating.MovedFrom("TacticalMaps")]
    public sealed class MapGenerator : MonoBehaviour
    {
        [SerializeField] private Texture2D heightSource;
        [SerializeField] private Texture2D landSource;
        [SerializeField] private MapSettings settings = new MapSettings();
        [SerializeField] private HeightmapGenerator heightmapGenerator;
        private CancellationTokenSource activeGeneration;

        internal Texture2D HeightSource => heightSource;
        internal MapSettings Settings => settings.Copy();

        internal void Configure(MapSettings requested)
        {
            ValidateSources();
            var snapshot = requested.Copy();
            snapshot.Validate(heightSource.width, heightSource.height);
            settings = snapshot;
        }

        internal MapData Generate(Vector2Int point)
        {
            ValidatePoint(point);
            activeGeneration?.Cancel();
            var snapshot = settings.Copy();
            var output = ResolveHeightmapGenerator().Generate(heightSource, landSource, point, snapshot);
            return Complete(point, snapshot, output.Textures, output.Compression);
        }

        public async Task<MapData> GenerateAsync(Vector2Int point, CancellationToken cancellation = default)
        {
            ValidatePoint(point);
            activeGeneration?.Cancel();
            var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            activeGeneration = operation;
            var snapshot = settings.Copy();
            try
            {
                var output = await ResolveHeightmapGenerator().GenerateAsync(heightSource, landSource, point, snapshot, operation.Token);
                var result = Complete(point, snapshot, output.Textures, output.Compression);
                if (operation.IsCancellationRequested) { result.Release(); operation.Token.ThrowIfCancellationRequested(); }
                return result;
            }
            finally
            {
                if (activeGeneration == operation) activeGeneration = null;
                operation.Dispose();
            }
        }

        internal bool TryReadSource(int x, int y, out float height, out bool land)
            => ResolveHeightmapGenerator().TryReadSource(heightSource, landSource, x, y, out height, out land);

        private static MapData Complete(Vector2Int point, MapSettings snapshot, Texture2D[] textures, float compression)
        {
            var sizes = new Vector2[3];
            for (int i = 0; i < 3; i++) sizes[i] = snapshot.Levels[i].Size;
            return new MapData(textures, sizes, snapshot.TerrainHeightMeters, snapshot.SeaLevelMeters, snapshot.GlobalMaxHeightMeters, point, compression);
        }
        private HeightmapGenerator ResolveHeightmapGenerator()
        {
            if (heightmapGenerator == null) heightmapGenerator = GetComponent<HeightmapGenerator>();
            if (heightmapGenerator == null || heightmapGenerator.gameObject != gameObject)
                throw new InvalidOperationException("HeightmapGenerator must be on the same object as MapGenerator.");
            return heightmapGenerator;
        }
        private void ValidatePoint(Vector2Int point)
        {
            Configure(settings);
            if (point.x < 0 || point.y < 0 || point.x >= heightSource.width || point.y >= heightSource.height)
                throw new ArgumentException("Центр должен находиться внутри исходной карты.");
        }
        private void ValidateSources()
        {
            if (heightSource == null || landSource == null || !heightSource.isReadable || !landSource.isReadable ||
                heightSource.format != TextureFormat.R16 || landSource.format != TextureFormat.R8)
                throw new InvalidOperationException("Назначьте читаемые исходные карты R16 (высота) и R8 (суша).");
            if (heightSource.width != landSource.width || heightSource.height != landSource.height)
                throw new InvalidOperationException("Height and land textures must have matching dimensions.");
        }
        private void OnDestroy() => activeGeneration?.Cancel();
    }
}
