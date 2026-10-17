using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;

namespace TacticalMaps.Construction
{
    [ExecuteAlways, DisallowMultipleComponent]
    internal sealed class InstancedTreeRenderer : MonoBehaviour
    {
        // TerrainData is the serialized placement store; its native tree rendering is disabled.
        [SerializeField] private Terrain[] terrains = Array.Empty<Terrain>();
        [SerializeField] private TreeRenderLibrary library;
        [SerializeField, Min(1)] private float drawDistance = 6000;
        [SerializeField, Min(16)] private float cellSize = 128;
        [SerializeField, Min(0)] private float shadowDistance = 100;
        private readonly Dictionary<int, CameraCache> cameras = new Dictionary<int, CameraCache>();
        private SpeciesRuntime[] species;
        private Part[] parts;
        private Cell[] cells;
        private Matrix4x4 cachedTransform;
        private bool dirty = true;

        internal int TreeCount { get; private set; }
        internal int VisibleTrees { get; private set; }
        internal int SubmittedDraws { get; private set; }
        internal int[] VisibleLods { get; } = new int[8];
        internal double CullMilliseconds { get; private set; }
        internal double SubmitMilliseconds { get; private set; }
        internal int CameraCacheCount => cameras.Count;

        internal void Configure(Terrain[] sources, TreeRenderLibrary assets, float distance, float gridSize, float shadows)
        {
            terrains = sources; library = assets; drawDistance = distance; cellSize = gridSize; shadowDistance = shadows;
            Rebuild();
        }
        private void OnEnable() { RenderPipelineManager.beginCameraRendering += Render; if (cells == null) dirty = true; }
        private void OnDisable() { RenderPipelineManager.beginCameraRendering -= Render; cameras.Clear(); cells = null; species = null; parts = null; dirty = true; }
        private void OnValidate() => dirty = true;

        private void Rebuild()
        {
            cameras.Clear(); TreeCount = 0;
            if (library == null || terrains == null) { cells = Array.Empty<Cell>(); parts = Array.Empty<Part>(); species = Array.Empty<SpeciesRuntime>(); dirty = false; return; }
            if (!SystemInfo.supportsInstancing) throw new InvalidOperationException("Tree rendering requires GPU instancing support.");
            var entries = library.SpeciesList; species = new SpeciesRuntime[entries.Length];
            var allParts = new List<Part>();
            var sourceIndices = new Dictionary<GameObject, int>();
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i]; sourceIndices.Add(entry.source, i);
                var runtime = new SpeciesRuntime { Size = entry.size, Reference = entry.referencePoint, Scale = entry.rootScale, Levels = new LevelRuntime[entry.levels.Length] };
                for (int lod = 0; lod < entry.levels.Length; lod++)
                {
                    var source = entry.levels[lod]; var indexes = new List<int>();
                    if (source.prefab == null) throw new InvalidOperationException("An instanced tree LOD prefab is missing.");
                    foreach (var renderer in source.prefab.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                        var relative = source.prefab.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                        var materials = renderer.sharedMaterials;
                        for (int sub = 0; sub < mesh.subMeshCount; sub++)
                        {
                            var material = materials[Mathf.Min(sub, materials.Length - 1)];
                            if (material == null || !material.enableInstancing) throw new InvalidOperationException("Tree LOD materials must enable GPU instancing.");
                            indexes.Add(allParts.Count);
                            allParts.Add(new Part { Mesh = mesh, Material = material, Submesh = sub, Relative = relative, Identity = relative == Matrix4x4.identity });
                        }
                    }
                    runtime.Levels[lod] = new LevelRuntime { Threshold = source.screenHeight, Parts = indexes.ToArray() };
                }
                species[i] = runtime;
            }
            parts = allParts.ToArray();
            cachedTransform = transform.localToWorldMatrix;
            var grid = new Dictionary<Vector2Int, List<TreeRecord>>();
            foreach (var terrain in terrains)
            {
                if (terrain == null || terrain.terrainData == null) continue;
                var data = terrain.terrainData; var prototypes = data.treePrototypes;
                var remap = new int[prototypes.Length];
                for (int i = 0; i < prototypes.Length; i++)
                    if (!sourceIndices.TryGetValue(prototypes[i].prefab, out remap[i])) throw new InvalidOperationException("Tree prototype is absent from the instancing library.");
                foreach (var tree in data.treeInstances)
                {
                    int index = remap[tree.prototypeIndex]; var definition = species[index];
                    var scale = Vector3.Scale(new Vector3(tree.widthScale, tree.heightScale, tree.widthScale), definition.Scale);
                    var world = terrain.transform.localToWorldMatrix * Matrix4x4.TRS(Vector3.Scale(tree.position, data.size), Quaternion.Euler(0, tree.rotation * Mathf.Rad2Deg, 0), scale);
                    var centre = world.MultiplyPoint3x4(definition.Reference);
                    float height = definition.Size * MaxAbs(world.lossyScale);
                    var record = new TreeRecord { Matrix = world, Centre = centre, Size = height, Radius = height * 0.75f + 2, Species = index, Layer = terrain.gameObject.layer };
                    var key = new Vector2Int(Mathf.FloorToInt(centre.x / cellSize), Mathf.FloorToInt(centre.z / cellSize));
                    if (!grid.TryGetValue(key, out var bucket)) grid.Add(key, bucket = new List<TreeRecord>());
                    bucket.Add(record); TreeCount++;
                }
            }
            cells = new Cell[grid.Count]; int cellIndex = 0;
            foreach (var bucket in grid.Values)
            {
                var bounds = new Bounds(bucket[0].Centre, Vector3.zero);
                foreach (var record in bucket) bounds.Encapsulate(new Bounds(record.Centre, Vector3.one * record.Radius * 2));
                cells[cellIndex++] = new Cell { Bounds = bounds, Trees = bucket.ToArray() };
            }
            dirty = false;
        }

        private void Render(ScriptableRenderContext context, Camera camera)
        {
            if (camera == null || camera.cameraType == CameraType.Reflection || camera.cameraType == CameraType.Preview) return;
#if UNITY_EDITOR
            bool preview = UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(gameObject.scene);
            bool cameraPreview = camera.scene.IsValid() && UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(camera.scene);
            if (preview ? camera.scene != gameObject.scene : cameraPreview) return;
#endif
            if (dirty || cachedTransform != transform.localToWorldMatrix) Rebuild();
            if (TreeCount == 0 || parts == null || cells == null) return;
            int key = camera.GetInstanceID();
            if (!cameras.TryGetValue(key, out var cache))
            {
                // Offscreen verification/preview cameras must not accumulate persistent arrays.
                if (cameras.Count >= 4) cameras.Clear();
                cameras.Add(key, cache = new CameraCache(camera, parts.Length * 3));
            }
            float bias = Mathf.Max(0.01f, QualitySettings.lodBias);
            int minimumLod = QualitySettings.maximumLODLevel;
            var view = camera.worldToCameraMatrix; var projection = camera.projectionMatrix;
            if (!cache.Valid || cache.View != view || cache.Projection != projection || cache.Bias != bias || cache.MinimumLod != minimumLod || cache.Mask != camera.cullingMask)
            {
                long started = Stopwatch.GetTimestamp();
                Populate(camera, cache, bias, minimumLod);
                cache.View = view; cache.Projection = projection; cache.Bias = bias; cache.MinimumLod = minimumLod; cache.Mask = camera.cullingMask; cache.Valid = true;
                cache.CullTime = Milliseconds(started);
            }
            else cache.CullTime = 0;
            long submitStarted = Stopwatch.GetTimestamp(); int draws = 0;
            for (int i = 0; i < cache.Buckets.Length; i++)
            {
                var bucket = cache.Buckets[i]; if (bucket == null) continue;
                var part = parts[i / 3]; int mode = i % 3;
                var parameters = new RenderParams(part.Material)
                {
                    camera = camera, layer = bucket.Layer, receiveShadows = true,
                    shadowCastingMode = mode == 0 ? ShadowCastingMode.On : mode == 1 ? ShadowCastingMode.Off : ShadowCastingMode.ShadowsOnly,
                    lightProbeUsage = LightProbeUsage.Off, reflectionProbeUsage = ReflectionProbeUsage.Off,
                    renderingLayerMask = uint.MaxValue, motionVectorMode = MotionVectorGenerationMode.Camera
                };
                foreach (var batch in bucket.Batches)
                {
                    if (batch.Count == 0) break;
                    parameters.worldBounds = batch.Bounds;
                    Graphics.RenderMeshInstanced(parameters, part.Mesh, part.Submesh, batch.Matrices, batch.Count);
                    draws++;
                }
            }
            VisibleTrees = cache.Visible; SubmittedDraws = draws; CullMilliseconds = cache.CullTime; SubmitMilliseconds = Milliseconds(submitStarted);
            Array.Copy(cache.Lods, VisibleLods, VisibleLods.Length);
        }

        private void Populate(Camera camera, CameraCache cache, float bias, int minimumLod)
        {
            foreach (var bucket in cache.Buckets) bucket?.Clear();
            Array.Clear(cache.Lods, 0, cache.Lods.Length); cache.Visible = 0;
            GeometryUtility.CalculateFrustumPlanes(camera, cache.Planes);
            var eye = camera.transform.position;
            float distanceLimit = Mathf.Min(drawDistance, camera.farClipPlane), limitSquared = distanceLimit * distanceLimit;
            float projectionScale = camera.orthographic ? bias / Mathf.Max(0.001f, 2 * camera.orthographicSize) : bias / (2 * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad));
            foreach (var cell in cells)
            {
                if (cell.Bounds.SqrDistance(eye) > limitSquared) continue;
                var padded = cell.Bounds; padded.Expand(shadowDistance * 2);
                if (!GeometryUtility.TestPlanesAABB(cache.Planes, padded)) continue;
                foreach (var tree in cell.Trees)
                {
                    if ((camera.cullingMask & (1 << tree.Layer)) == 0) continue;
                    float squared = (tree.Centre - eye).sqrMagnitude;
                    if (squared > (distanceLimit + tree.Radius) * (distanceLimit + tree.Radius)) continue;
                    bool visible = SphereVisible(cache.Planes, tree.Centre, tree.Radius);
                    bool shadow = squared <= (shadowDistance + tree.Radius) * (shadowDistance + tree.Radius);
                    if (!visible && !shadow) continue;
                    var definition = species[tree.Species];
                    float screenHeight = tree.Size * projectionScale / (camera.orthographic ? 1 : Mathf.Max(0.01f, Mathf.Sqrt(squared)));
                    int lod = ChooseLod(definition.Levels, screenHeight, minimumLod);
                    if (lod < 0) continue;
                    int mode = visible ? (shadow ? 0 : 1) : 2;
                    if (visible) { cache.Visible++; cache.Lods[Mathf.Min(lod, cache.Lods.Length - 1)]++; }
                    foreach (int partIndex in definition.Levels[lod].Parts)
                    {
                        int bucketIndex = partIndex * 3 + mode;
                        var bucket = cache.Buckets[bucketIndex];
                        if (bucket == null) cache.Buckets[bucketIndex] = bucket = new Bucket();
                        bucket.Layer = tree.Layer;
                        var part = parts[partIndex];
                        bucket.Add(part.Identity ? tree.Matrix : tree.Matrix * part.Relative, tree.Centre, tree.Radius);
                    }
                }
            }
        }
        internal static int SelectLod(float[] thresholds, float screenHeight, int minimumLod = 0)
        {
            for (int i = Mathf.Clamp(minimumLod, 0, thresholds.Length - 1); i < thresholds.Length; i++) if (screenHeight >= thresholds[i]) return i;
            return -1;
        }
        private static int ChooseLod(LevelRuntime[] levels, float height, int minimum)
        {
            for (int i = Mathf.Clamp(minimum, 0, levels.Length - 1); i < levels.Length; i++) if (height >= levels[i].Threshold) return i;
            return -1;
        }
        private static bool SphereVisible(Plane[] planes, Vector3 centre, float radius)
        { foreach (var plane in planes) if (plane.GetDistanceToPoint(centre) < -radius) return false; return true; }
        private static double Milliseconds(long start) => (Stopwatch.GetTimestamp() - start) * (1000.0 / Stopwatch.Frequency);
        private static float MaxAbs(Vector3 v) => Mathf.Max(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        private sealed class SpeciesRuntime { internal Vector3 Reference, Scale; internal float Size; internal LevelRuntime[] Levels; }
        private sealed class LevelRuntime { internal float Threshold; internal int[] Parts; }
        private sealed class Part { internal Mesh Mesh; internal Material Material; internal Matrix4x4 Relative; internal int Submesh; internal bool Identity; }
        private struct TreeRecord { internal Matrix4x4 Matrix; internal Vector3 Centre; internal float Size, Radius; internal int Species, Layer; }
        private sealed class Cell { internal Bounds Bounds; internal TreeRecord[] Trees; }
        private sealed class CameraCache
        {
            internal readonly Camera Camera; internal readonly Bucket[] Buckets; internal readonly Plane[] Planes = new Plane[6];
            internal readonly int[] Lods = new int[8];
            internal Matrix4x4 View, Projection; internal float Bias; internal int MinimumLod, Mask, Visible;
            internal bool Valid; internal double CullTime;
            internal CameraCache(Camera camera, int count) { Camera = camera; Buckets = new Bucket[count]; }
        }
        private sealed class Bucket
        {
            internal readonly List<Batch> Batches = new List<Batch>(); internal int Layer; private int current;
            internal void Clear() { foreach (var batch in Batches) batch.Count = 0; current = 0; }
            internal void Add(Matrix4x4 matrix, Vector3 centre, float radius)
            {
                if (current == Batches.Count) Batches.Add(new Batch());
                var batch = Batches[current];
                if (batch.Count == 511) { current++; if (current == Batches.Count) Batches.Add(new Batch()); batch = Batches[current]; }
                var bounds = new Bounds(centre, Vector3.one * (radius * 2));
                if (batch.Count == 0) batch.Bounds = bounds; else batch.Bounds.Encapsulate(bounds);
                batch.Matrices[batch.Count++] = matrix;
            }
        }
        private sealed class Batch { internal readonly Matrix4x4[] Matrices = new Matrix4x4[511]; internal Bounds Bounds; internal int Count; }
    }
}
