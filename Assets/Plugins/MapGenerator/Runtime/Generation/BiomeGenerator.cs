using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace TacticalMaps.Generation
{
    // Only MapGenerator invokes this stage. Terrain construction consumes its finished SurfaceData.
    internal sealed class BiomeGenerator : MonoBehaviour
    {
        [Header("Terrain layer library")]
        [SerializeField] private TerrainLayer meadow;
        [SerializeField] private TerrainLayer dryGrass;
        [SerializeField] private TerrainLayer earth;
        [SerializeField] private TerrainLayer rock;
        [SerializeField] private TerrainLayer sand;
        [SerializeField] private TerrainLayer silt;
        [SerializeField] private BiomeSettings settings = new BiomeSettings();

        internal SurfaceData Generate(Texture2D[] maps, Vector2[] sizes, float height, float sea, Vector2Int point, int radius, CancellationToken cancellation)
        {
            var layers = Layers();
            var field = new BiomeField(maps, sizes, height, sea, point, radius, settings.Copy());
            return new SurfaceData(layers, field.Render(cancellation), field.TextureOrigin);
        }
        internal async Task<SurfaceData> GenerateAsync(Texture2D[] maps, Vector2[] sizes, float height, float sea, Vector2Int point, int radius, CancellationToken cancellation)
        {
            var layers = Layers();
            var field = new BiomeField(maps, sizes, height, sea, point, radius, settings.Copy());
            var weights = await Task.Run(() => field.Render(cancellation), cancellation);
            return new SurfaceData(layers, weights, field.TextureOrigin);
        }
        internal float BeachHeight => settings.BeachHeight;
        private TerrainLayer[] Layers()
        {
            var layers = new[] { meadow, dryGrass, earth, rock, sand, silt };
            foreach (var layer in layers)
                if (layer == null || layer.diffuseTexture == null) throw new InvalidOperationException("Назначьте 6 слоёв поверхности в BiomeGenerator: meadow, dry grass, earth, rock, sand, silt.");
            return layers;
        }
    }

    [Serializable]
    internal sealed class BiomeSettings
    {
        [Header("Weight map resolution (surface detail comes from Terrain Layers)")]
        [SerializeField, Range(32, 2048)] private int mainResolution = 1024;
        [SerializeField, Range(32, 1024)] private int backgroundResolution = 512;
        [Header("Moisture and exposure")]
        [SerializeField, Range(0, 1)] private float moisture = 0.5f;
        [SerializeField, Min(1)] private float coastalMoistureHeight = 35;
        [SerializeField, Min(1)] private float dryHighlandHeight = 220;
        [SerializeField, Min(10)] private float valleyRadius = 90;
        [SerializeField, Range(0, 1)] private float valleyMoisture = 0.4f;
        [SerializeField, Min(100)] private float climateRegionMeters = 750;
        [SerializeField, Range(0, 0.4f)] private float climateVariation = 0.13f;
        [Header("Erosion and coast")]
        [SerializeField, Range(0, 30)] private float soilSlopeStart = 5;
        [SerializeField, Range(1, 60)] private float soilSlopeEnd = 20;
        [SerializeField, Range(0, 60)] private float rockSlopeStart = 22;
        [SerializeField, Range(1, 89)] private float rockSlopeEnd = 42;
        [SerializeField, Min(0.1f)] private float beachHeight = 2.5f;
        [SerializeField, Min(0.1f)] private float siltDepth = 5;
        [SerializeField] private int seed = 137;

        internal int MainResolution => mainResolution;
        internal int BackgroundResolution => backgroundResolution;
        internal float Moisture => moisture;
        internal float CoastalMoistureHeight => coastalMoistureHeight;
        internal float DryHighlandHeight => dryHighlandHeight;
        internal float ValleyRadius => valleyRadius;
        internal float ValleyMoisture => valleyMoisture;
        internal float ClimateRegionMeters => climateRegionMeters;
        internal float ClimateVariation => climateVariation;
        internal float SoilSlopeStart => soilSlopeStart;
        internal float SoilSlopeEnd => soilSlopeEnd;
        internal float RockSlopeStart => rockSlopeStart;
        internal float RockSlopeEnd => rockSlopeEnd;
        internal float BeachHeight => beachHeight;
        internal float SiltDepth => siltDepth;
        internal int Seed => seed;
        internal BiomeSettings Copy() => JsonUtility.FromJson<BiomeSettings>(JsonUtility.ToJson(this));
        internal void Validate()
        {
            if (mainResolution < 32 || mainResolution > 2048 || backgroundResolution < 32 || backgroundResolution > 1024 ||
                !PowerOfTwo(mainResolution) || !PowerOfTwo(backgroundResolution) || !Positive(coastalMoistureHeight) ||
                !Positive(dryHighlandHeight) || !Positive(valleyRadius) || !Positive(climateRegionMeters) || !Positive(beachHeight) || !Positive(siltDepth) ||
                !Positive(soilSlopeEnd - soilSlopeStart) || !Positive(rockSlopeEnd - rockSlopeStart))
                throw new ArgumentException("Invalid biome settings. Weight map sizes must be powers of two; slope ranges must be increasing.");
        }
        private static bool PowerOfTwo(int n) => (n & (n - 1)) == 0;
        private static bool Positive(float f) => !float.IsNaN(f) && !float.IsInfinity(f) && f > 0;
    }
}
