using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace TacticalMaps
{
    // Owns source data, settings, generation and output lifetime. No UI dependencies.
    internal sealed class MapGenerator : MonoBehaviour
    {
        [SerializeField] private Texture2D heightSource;
        [SerializeField] private Texture2D landSource;
        [SerializeField] private MapSettings settings = new MapSettings();
        [SerializeField] private Texture2D[] generated = new Texture2D[3];
        private float[] sourceHeights;
        private byte[] sourceLand;
        private Texture2D loadedHeightSource, loadedLandSource;
        private CancellationTokenSource activeGeneration;
        private bool ownsResults;
        [SerializeField, HideInInspector] private MapSettings resultSettings;
        [SerializeField, HideInInspector] private float heightCompression = 1;

        internal Texture2D HeightSource => heightSource;
        internal MapSettings Settings => settings.Copy();
        internal MapSettings ResultSettings => (resultSettings ?? settings).Copy();
        internal float HeightCompression => heightCompression;
        internal Texture2D GetHeightmap(int level) => generated[level];

        internal void Configure(MapSettings requested)
        {
            LoadSources();
            var snapshot = requested.Copy();
            snapshot.Validate(heightSource.width, heightSource.height);
            if (snapshot.Center.x < 0 || snapshot.Center.y < 0 || snapshot.Center.x >= heightSource.width || snapshot.Center.y >= heightSource.height)
                throw new ArgumentException("Центр должен находиться внутри исходной карты.");
            settings = snapshot;
        }

        internal async Task GenerateAsync(CancellationToken cancellation = default)
        {
            Configure(settings);
            activeGeneration?.Cancel();
            var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            activeGeneration = operation;
            var snapshot = settings.Copy();
            int width = heightSource.width, height = heightSource.height;
            // Capture CPU arrays so inspector changes cannot alter an in-flight request.
            var heights = sourceHeights; var land = sourceLand;
            try
            {
                var data = await Task.Run(() => Build(heights, land, width, height, snapshot, operation.Token), operation.Token);
                operation.Token.ThrowIfCancellationRequested();
                Apply(data);
            }
            finally
            {
                if (activeGeneration == operation) activeGeneration = null;
                operation.Dispose();
            }
        }

        internal void Generate()
        {
            Configure(settings);
            activeGeneration?.Cancel();
            Apply(Build(sourceHeights, sourceLand, heightSource.width, heightSource.height, settings.Copy(), CancellationToken.None));
        }

        internal bool TryReadSource(int x, int y, out float height, out bool land)
        {
            LoadSources();
            height = 0; land = false;
            if (x < 0 || y < 0 || x >= heightSource.width || y >= heightSource.height) return false;
            int index = y * heightSource.width + x;
            height = sourceHeights[index]; land = sourceLand[index] >= 128;
            return true;
        }

        private void LoadSources()
        {
            if (heightSource == null || landSource == null) throw new InvalidOperationException("Назначьте исходную карту высот и маску суши на MapGenerator.");
            if (sourceHeights != null && loadedHeightSource == heightSource && loadedLandSource == landSource) return;
            if (!heightSource.isReadable || !landSource.isReadable || heightSource.format != TextureFormat.R16 || landSource.format != TextureFormat.R8)
                throw new InvalidOperationException("Source formats must be readable linear R16 height and R8 land mask.");
            if (heightSource.width != landSource.width || heightSource.height != landSource.height)
                throw new InvalidOperationException("Height and land textures must have matching dimensions.");
            var raw = heightSource.GetPixelData<ushort>(0);
            sourceHeights = new float[raw.Length];
            for (int i = 0; i < raw.Length; i++) sourceHeights[i] = raw[i] / 65535f;
            sourceLand = landSource.GetPixelData<byte>(0).ToArray();
            loadedHeightSource = heightSource; loadedLandSource = landSource;
        }

        private static GenerationData Build(float[] heights, byte[] land, int width, int height, MapSettings snapshot, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var field = new HeightField(heights, land, width, height, snapshot);
            var pixels = new ushort[3][];
            for (int i = 0; i < 3; i++) pixels[i] = field.Render(i, token);
            return new GenerationData(snapshot, pixels, field.Compression);
        }

        private void Apply(GenerationData data)
        {
            var textures = new Texture2D[3];
            try
            {
                for (int i = 0; i < textures.Length; i++)
                {
                    var level = data.Settings.Levels[i];
                    var texture = new Texture2D(level.Resolution.x, level.Resolution.y, TextureFormat.R16, false, true)
                    { name = "Heightmap_" + (i + 1), wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                    textures[i] = texture;
                    texture.SetPixelData(data.Pixels[i], 0); texture.Apply(false, false);
                }
            }
            catch
            {
                foreach (var texture in textures) Release(texture);
                throw;
            }
            ReleaseResults();
            generated = textures;
            resultSettings = data.Settings;
            heightCompression = data.Compression;
            ownsResults = true;
        }

        private void ReleaseResults()
        {
            if (ownsResults) foreach (var texture in generated) Release(texture);
        }
        private static void Release(Texture2D texture)
        {
            if (texture == null) return;
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(texture)) return;
#endif
            if (Application.isPlaying) Destroy(texture); else DestroyImmediate(texture);
        }
        private void OnDestroy()
        {
            activeGeneration?.Cancel();
            ReleaseResults();
        }

        private sealed class GenerationData
        {
            internal MapSettings Settings { get; }
            internal ushort[][] Pixels { get; }
            internal float Compression { get; }
            internal GenerationData(MapSettings settings, ushort[][] pixels, float compression)
            { Settings = settings; Pixels = pixels; Compression = compression; }
        }
    }
}
