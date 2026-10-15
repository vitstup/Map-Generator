using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace TacticalMaps.Generation
{
    // Called only by MapGenerator. Owns source caches, not completed map lifetimes.
    [DisallowMultipleComponent]
    internal sealed class HeightmapGenerator : MonoBehaviour
    {
        private Texture2D loadedHeightSource, loadedLandSource;
        private float[] sourceHeights;
        private byte[] sourceLand;

        internal (Texture2D[] Textures, float Compression) Generate(Texture2D heightSource, Texture2D landSource, Vector2Int point, MapSettings settings)
        {
            LoadSources(heightSource, landSource);
            return CreateTextures(Build(sourceHeights, sourceLand, heightSource.width, heightSource.height, point, settings, CancellationToken.None));
        }
        internal async Task<(Texture2D[] Textures, float Compression)> GenerateAsync(Texture2D heightSource, Texture2D landSource,
            Vector2Int point, MapSettings settings, CancellationToken cancellation)
        {
            LoadSources(heightSource, landSource);
            int width = heightSource.width, height = heightSource.height;
            var heights = sourceHeights; var land = sourceLand;
            var output = await Task.Run(() => Build(heights, land, width, height, point, settings, cancellation), cancellation);
            cancellation.ThrowIfCancellationRequested();
            return CreateTextures(output);
        }
        internal bool TryReadSource(Texture2D heightSource, Texture2D landSource, int x, int y, out float height, out bool land)
        {
            LoadSources(heightSource, landSource);
            height = 0; land = false;
            if (x < 0 || y < 0 || x >= heightSource.width || y >= heightSource.height) return false;
            int index = y * heightSource.width + x;
            height = sourceHeights[index]; land = sourceLand[index] >= 128;
            return true;
        }
        private void LoadSources(Texture2D heightSource, Texture2D landSource)
        {
            if (heightSource == null || landSource == null || !heightSource.isReadable || !landSource.isReadable ||
                heightSource.format != TextureFormat.R16 || landSource.format != TextureFormat.R8 ||
                heightSource.width != landSource.width || heightSource.height != landSource.height)
                throw new InvalidOperationException("Sources must be matching readable R16 / R8 textures.");
            if (sourceHeights != null && loadedHeightSource == heightSource && loadedLandSource == landSource) return;
            var raw = heightSource.GetPixelData<ushort>(0);
            sourceHeights = new float[raw.Length];
            for (int i = 0; i < raw.Length; i++) sourceHeights[i] = raw[i] / 65535f;
            sourceLand = landSource.GetPixelData<byte>(0).ToArray();
            loadedHeightSource = heightSource; loadedLandSource = landSource;
        }
        private static HeightmapOutput Build(float[] heights, byte[] land, int width, int height, Vector2Int point, MapSettings snapshot, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var field = new HeightField(heights, land, width, height, point, snapshot);
            var pixels = new ushort[3][];
            for (int i = 0; i < 3; i++) pixels[i] = field.Render(i, token);
            return new HeightmapOutput(snapshot, pixels, field.Compression);
        }
        private static (Texture2D[] Textures, float Compression) CreateTextures(HeightmapOutput output)
        {
            var textures = new Texture2D[3];
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    var level = output.Settings.Levels[i];
                    var texture = new Texture2D(level.Resolution.x, level.Resolution.y, TextureFormat.R16, false, true)
                    { name = "Heightmap_" + (i + 1), wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                    textures[i] = texture;
                    texture.SetPixelData(output.Pixels[i], 0); texture.Apply(false, false);
                }
            }
            catch
            {
                foreach (var texture in textures)
                    if (texture != null) { if (Application.isPlaying) Destroy(texture); else DestroyImmediate(texture); }
                throw;
            }
            return (textures, output.Compression);
        }
        private sealed class HeightmapOutput
        {
            internal MapSettings Settings { get; private set; }
            internal ushort[][] Pixels { get; private set; }
            internal float Compression { get; private set; }
            internal HeightmapOutput(MapSettings settings, ushort[][] pixels, float compression)
            { Settings = settings; Pixels = pixels; Compression = compression; }
        }
    }
}
