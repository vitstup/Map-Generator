using TacticalMaps.Generation;
using System;
using Unity.Collections;
using UnityEngine;

namespace TacticalMaps.Construction
{
    // Accepts completed heightmaps only. Does not know about generation or its test controller.
    public sealed class TerrainBuilder : MonoBehaviour
    {
        [SerializeField] private Vector3 center;
        [SerializeField, Min(0)] private float seamBlendMeters = 100;
        [SerializeField, Min(0.01f)] private float overlapDepth = 0.25f;
        [SerializeField, Range(1, 200)] private float mainPixelError = 1;
        [SerializeField, Range(1, 200)] private float backgroundPixelError = 5;
        [SerializeField] private Material terrainMaterial;
        [SerializeField] private TerrainLayer surfaceLayer;
        [SerializeField, HideInInspector] private Transform builtRoot;
        [SerializeField, HideInInspector] private TerrainData[] builtData;

        public void Build(MapData map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            var heightmaps = new Texture2D[map.Heightmaps.Count]; var sizes = new Vector2[map.Sizes.Count];
            for (int i = 0; i < heightmaps.Length; i++) heightmaps[i] = map.Heightmaps[i];
            for (int i = 0; i < sizes.Length; i++) sizes[i] = map.Sizes[i];
            float heightMeters = map.TerrainHeightMeters;
            Validate(heightmaps, sizes, heightMeters);
            var nextRoot = new GameObject("GeneratedTerrains");
            nextRoot.transform.SetParent(transform, false);
            nextRoot.SetActive(false);
            var nextData = new TerrainData[3];
            try
            {
                for (int level = 0; level < 3; level++)
                {
                    var texture = heightmaps[level];
                    int resolution = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(texture.width, texture.height) - 1) + 1, 33, 4097);
                    var data = new TerrainData { name = "TerrainData_" + (level + 1), heightmapResolution = resolution };
                    nextData[level] = data;
                    data.size = new Vector3(sizes[level].x, heightMeters, sizes[level].y);
                    if (surfaceLayer != null) data.terrainLayers = new[] { surfaceLayer };
                    var heights = CreateHeights(heightmaps, sizes, level, resolution);
                    data.SetHeights(0, 0, heights);
                    if (level > 0) CutInterior(data, sizes[level], sizes[level - 1]);
                    var go = Terrain.CreateTerrainGameObject(data);
                    go.name = level == 0 ? "Terrain_Main" : level == 1 ? "Terrain_Surroundings" : "Terrain_Horizon";
                    go.transform.SetParent(nextRoot.transform, false);
                    go.transform.localPosition = center + new Vector3(-sizes[level].x * 0.5f, -level * overlapDepth, -sizes[level].y * 0.5f);
                    var terrain = go.GetComponent<Terrain>();
                    terrain.allowAutoConnect = false;
                    terrain.heightmapPixelError = level == 0 ? mainPixelError : backgroundPixelError;
                    terrain.drawInstanced = true;
                    if (terrainMaterial != null) terrain.materialTemplate = terrainMaterial;
                    // Background rings contribute visuals, not gameplay collision.
                    go.GetComponent<TerrainCollider>().enabled = level == 0;
                    terrain.Flush();
                }
            }
            catch
            {
                Release(nextRoot, nextData);
                throw;
            }
            var previousRoot = builtRoot;
            var previousData = builtData;
            builtRoot = nextRoot.transform;
            builtData = nextData;
            if (previousRoot != null) previousRoot.gameObject.SetActive(false);
            nextRoot.SetActive(true);
            Release(previousRoot != null ? previousRoot.gameObject : null, previousData);
        }

        private float[,] CreateHeights(Texture2D[] maps, Vector2[] sizes, int level, int resolution)
        {
            var texture = maps[level];
            var raw = texture.GetPixelData<ushort>(0);
            var previous = level > 0 ? maps[level - 1] : null;
            var previousRaw = previous != null ? previous.GetPixelData<ushort>(0) : default;
            var result = new float[resolution, resolution];
            float cellX = sizes[level].x / (resolution - 1), cellZ = sizes[level].y / (resolution - 1);
            for (int z = 0; z < resolution; z++)
                for (int x = 0; x < resolution; x++)
                {
                    float u = (float)x / (resolution - 1), v = (float)z / (resolution - 1);
                    float h = Sample(raw, texture.width, texture.height, u, v);
                    if (level > 0)
                    {
                        float wx = (u - 0.5f) * sizes[level].x, wz = (v - 0.5f) * sizes[level].y;
                        float distance = Mathf.Max(Mathf.Abs(wx) - sizes[level - 1].x * 0.5f, Mathf.Abs(wz) - sizes[level - 1].y * 0.5f);
                        if (distance < -3 * Mathf.Max(cellX, cellZ)) h = 0;
                        else if (distance < seamBlendMeters || distance <= 0)
                        {
                            float inner = Sample(previousRaw, previous.width, previous.height, wx / sizes[level - 1].x + 0.5f, wz / sizes[level - 1].y + 0.5f);
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
        private static void Release(GameObject root, TerrainData[] data)
        {
            if (root != null) Dispose(root);
            if (data != null) foreach (var item in data) if (item != null) Dispose(item);
        }
        private static void Dispose(UnityEngine.Object value)
        {
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(value)) return;
#endif
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
        private void OnDestroy() => Release(builtRoot != null ? builtRoot.gameObject : null, builtData);
    }
}
