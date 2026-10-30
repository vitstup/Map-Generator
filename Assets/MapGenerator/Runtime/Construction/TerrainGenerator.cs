using TacticalMaps.Generation;
using System;
using Unity.Collections;
using UnityEngine;

namespace TacticalMaps.Construction
{
    // Applies completed heightmaps and surface weights, without invoking generation or the test controller.
    internal sealed class TerrainGenerator : MonoBehaviour
    {
        [SerializeField, LayerIndex] private int terrainLayer;
        [SerializeField, Min(0)] private float seamBlendMeters = 100;
        [SerializeField, Min(0.01f)] private float overlapDepth = 0.25f;
        [SerializeField, Range(1, 200)] private float mainPixelError = 1;
        [SerializeField, Range(1, 200)] private float backgroundPixelError = 5;
        [SerializeField] private Material terrainMaterial;
        [SerializeField, Min(100)] private float detailedSurfaceDistance = 6000;
        [SerializeField, Min(100)] private float mainSurfaceDistance = 12000;
        [SerializeField] private UnityEngine.Rendering.ShadowCastingMode shadowCasting = UnityEngine.Rendering.ShadowCastingMode.On;

        internal void Generate(MapData map, Transform parent, Vector3 center)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            var heightmaps = new Texture2D[map.Heightmaps.Count]; var sizes = new Vector2[map.Sizes.Count];
            for (int i = 0; i < heightmaps.Length; i++) heightmaps[i] = map.Heightmaps[i];
            for (int i = 0; i < sizes.Length; i++) sizes[i] = map.Sizes[i];
            float heightMeters = map.TerrainHeightMeters;
            Validate(heightmaps, sizes, heightMeters);
            if (terrainMaterial == null) throw new InvalidOperationException("Assign the terrain surface material on TerrainGenerator.");
            for (int level = 0; level < 3; level++)
            {
                var texture = heightmaps[level];
                int resolution = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(texture.width, texture.height) - 1) + 1, 33, 4097);
                var data = new TerrainData { name = "TerrainData_" + (level + 1), heightmapResolution = resolution };

                var go = new GameObject(level == 0 ? "Terrain_Main" : level == 1 ? "Terrain_Surroundings" : "Terrain_Horizon");
                go.transform.SetParent(parent, false);
                var terrain = go.AddComponent<Terrain>(); terrain.terrainData = data;
                var collider = go.AddComponent<TerrainCollider>(); collider.terrainData = data;
                data.size = new Vector3(sizes[level].x, heightMeters, sizes[level].y);
                var heights = CreateHeights(heightmaps, sizes, level, resolution);
                AgricultureTerrain.Apply(map, heights, level, overlapDepth);
                data.SetHeights(0, 0, heights);
                ApplySurface(data, map, level);
                if (level > 0) CutInterior(data, sizes[level], sizes[level - 1]);
                go.layer = terrainLayer;
                go.transform.localPosition = center + new Vector3(-sizes[level].x * 0.5f, -level * overlapDepth, -sizes[level].y * 0.5f);

                terrain.allowAutoConnect = false;
                terrain.heightmapPixelError = level == 0 ? mainPixelError : backgroundPixelError;
                terrain.drawInstanced = true;
                terrain.shadowCastingMode = shadowCasting;
                terrain.basemapDistance = level == 0 ? mainSurfaceDistance : detailedSurfaceDistance;
                var material = new Material(terrainMaterial) { name = "TerrainSurface_" + level };
                terrain.materialTemplate = material;
                if (material.HasProperty("_TacticalTerrainSize"))
                    material.SetVector("_TacticalTerrainSize", new Vector4(sizes[level].x, sizes[level].y, 0, 0));
                // Background rings contribute visuals, not gameplay collision.
                go.GetComponent<TerrainCollider>().enabled = level == 0;
                terrain.Flush();
            }
        }

        private static void ApplySurface(TerrainData data, MapData map, int level)
        {
            if (map.Surface == null) return; // Height-only data is also accepted by the construction facade.
            var surface = map.Surface;
            var weights = surface.Weights[level];
            if (weights.GetLength(0) != weights.GetLength(1) || weights.GetLength(2) != surface.Layers.Count)
                throw new ArgumentException("Surface weights must be square and match the layer library.");
            foreach (var layer in surface.Layers)
                if (layer == null || layer.tileSize.x <= 0 || layer.tileSize.y <= 0)
                    throw new ArgumentException("Surface layers need positive tile dimensions.");
            data.alphamapResolution = weights.GetLength(0);
            data.baseMapResolution = 1024;
            var layers = new TerrainLayer[surface.Layers.Count];
            for (int i = 0; i < layers.Length; i++)
            {
                layers[i] = Instantiate(surface.Layers[i]);
                layers[i].name = "Surface_" + level + "_" + i;
                // A common texture phase at every terrain boundary, regardless of its dimensions.
                var size = layers[i].tileSize;
                layers[i].tileOffset = new Vector2(Mathf.Repeat(surface.TextureOrigin.x - map.Sizes[level].x * 0.5f, size.x), Mathf.Repeat(surface.TextureOrigin.y - map.Sizes[level].y * 0.5f, size.y));
            }
            data.terrainLayers = layers;
            data.SetAlphamaps(0, 0, weights);
        }

        private float[,] CreateHeights(Texture2D[] maps, Vector2[] sizes, int level, int resolution)
        {
            var texture = maps[level];
            var raw = texture.GetPixelData<ushort>(0);
            var previous = level > 0 ? maps[level - 1] : null;
            var previousRaw = previous != null ? previous.GetPixelData<ushort>(0) : default;
            int width = texture.width, height = texture.height, previousWidth = previous != null ? previous.width : 0, previousHeight = previous != null ? previous.height : 0;
            bool direct = width == resolution && height == resolution;
            var result = new float[resolution, resolution];
            float cellX = sizes[level].x / (resolution - 1), cellZ = sizes[level].y / (resolution - 1);
            for (int z = 0; z < resolution; z++)
                for (int x = 0; x < resolution; x++)
                {
                    float u = (float)x / (resolution - 1), v = (float)z / (resolution - 1);
                    float h = direct ? raw[z * resolution + x] / 65535f : Sample(raw, width, height, u, v);
                    if (level > 0)
                    {
                        float wx = (u - 0.5f) * sizes[level].x, wz = (v - 0.5f) * sizes[level].y;
                        float distance = Mathf.Max(Mathf.Abs(wx) - sizes[level - 1].x * 0.5f, Mathf.Abs(wz) - sizes[level - 1].y * 0.5f);
                        if (distance < -3 * Mathf.Max(cellX, cellZ)) h = 0;
                        else if (distance < seamBlendMeters || distance <= 0)
                        {
                            float inner = Sample(previousRaw, previousWidth, previousHeight, wx / sizes[level - 1].x + 0.5f, wz / sizes[level - 1].y + 0.5f);
                            // Each background terrain is slightly lower, preventing coincident surfaces in the overlap.
                            float t = Mathf.SmoothStep(0, 1, Mathf.Clamp01(distance / Mathf.Max(0.001f, seamBlendMeters)));
                            h = Mathf.Lerp(inner, h, t);
                        }
                    }
                    result[z, x] = h;
                }
            return result;
        }

        private static float Sample(NativeArray<ushort> raw, int width, int height, float u, float v)
        {
            float px = Mathf.Clamp01(u) * (width - 1), py = Mathf.Clamp01(v) * (height - 1);
            int x = Mathf.Min((int)px, width - 2), y = Mathf.Min((int)py, height - 2);
            return Mathf.Lerp(Mathf.Lerp(raw[y * width + x], raw[y * width + x + 1], px - x),
                Mathf.Lerp(raw[(y + 1) * width + x], raw[(y + 1) * width + x + 1], px - x), py - y) / 65535f;
        }

        private static void CutInterior(TerrainData data, Vector2 size, Vector2 innerSize)
        {
            int n = data.holesResolution;
            var surface = new bool[n, n];
            float dx = size.x / n, dz = size.y / n;
            float halfX = innerSize.x * 0.5f - 2 * dx, halfZ = innerSize.y * 0.5f - 2 * dz;
            for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
            {
                float x0 = x * dx - size.x * 0.5f, z0 = z * dz - size.y * 0.5f;
                bool hidden = x0 >= -halfX && x0 + dx <= halfX && z0 >= -halfZ && z0 + dz <= halfZ;
                surface[z, x] = !hidden;
            }
            data.SetHoles(0, 0, surface);
        }

        private void Validate(Texture2D[] maps, Vector2[] sizes, float heightMeters)
        {
            if (maps == null || sizes == null || maps.Length != 3 || sizes.Length != 3)
                throw new ArgumentException("TerrainBuilder requires three heightmaps and three sizes.");
            if (terrainLayer < 0 || terrainLayer > 31) throw new ArgumentException("Terrain layer must be 0–31.");
            if (!Finite(heightMeters) || heightMeters <= 0 || !Finite(overlapDepth) || overlapDepth <= 0 || !Finite(seamBlendMeters) || seamBlendMeters < 0)
                throw new ArgumentException("Invalid terrain height or seam parameters.");
            for (int i = 0; i < 3; i++)
            {
                if (maps[i] == null || !maps[i].isReadable || maps[i].format != TextureFormat.R16 || maps[i].width < 2 || maps[i].height < 2 || maps[i].width > 4097 || maps[i].height > 4097)
                    throw new ArgumentException("Heightmaps must be readable R16 textures, dimensions 2–4097.");
                if (!Finite(sizes[i].x) || !Finite(sizes[i].y) || sizes[i].x <= 0 || sizes[i].y <= 0 ||
                    (i > 0 && (sizes[i].x <= sizes[i - 1].x || sizes[i].y <= sizes[i - 1].y)))
                    throw new ArgumentException("Terrain rectangles must be positive and nested.");
            }
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
