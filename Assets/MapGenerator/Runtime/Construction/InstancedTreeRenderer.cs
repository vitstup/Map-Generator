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
        [SerializeField, Range(0.02f, 0.3f)] private float meshScreenHeight = DefaultMeshScreenHeight;
        [SerializeField, Range(0.5f, 4)] private float minimumImpostorPixels = 1.5f;
        private readonly Dictionary<int, CameraCache> cameras = new Dictionary<int, CameraCache>();
        private SpeciesRuntime[] species;
        private Part[] parts;
        private Cell[] cells;
        private Dictionary<Vector2Int, Cell> spatialCells;
        private float maximumTreeSize;
        private Matrix4x4 cachedTransform;
        private bool dirty = true;
        private bool detailedGeometryForChecks;
        private bool legacyHybridForChecks;
        private IndirectTreeImpostors impostors;
        private List<IndirectTreeImpostors.Tree>[] pendingGpuTrees;
        private int gpuLayer;
        internal const float TransitionFraction = 0.12f;
        internal const float DefaultMeshScreenHeight = 0.13f;
        private static readonly int TreeFadeId = Shader.PropertyToID("_TreeFade");
        private bool SmoothLods => library != null && library.OptimizationVersion > 0 && !detailedGeometryForChecks && !legacyHybridForChecks;
        private float MeshThreshold => legacyHybridForChecks ? 0.2f : meshScreenHeight;

        internal int TreeCount { get; private set; }
        internal int VisibleTrees { get; private set; }
        internal int SubmittedDraws { get; private set; }
        internal int[] VisibleLods { get; } = new int[8];
        internal double CullMilliseconds { get; private set; }
        internal double SubmitMilliseconds { get; private set; }
        internal int CameraCacheCount => cameras.Count;
        internal long MeshTriangles { get; private set; }
        internal int IndirectDraws { get; private set; }
        internal bool DetailedGeometryForChecks { get => detailedGeometryForChecks; set { if (detailedGeometryForChecks != value) { detailedGeometryForChecks = value; dirty = true; } } }
        internal bool LegacyHybridForChecks { get => legacyHybridForChecks; set { if (legacyHybridForChecks != value) { legacyHybridForChecks = value; dirty = true; } } }
        internal uint ReadImpostorCountForChecks(Camera camera) => impostors?.ReadVisibleCount(camera) ?? 0;
        internal bool SuppressDrawingForChecks { get; set; }

        internal void Configure(Terrain[] sources, TreeRenderLibrary assets, float distance, float gridSize, float shadows, float meshThreshold = DefaultMeshScreenHeight, float minimumPixels = 1.5f)
        {
            terrains = sources; library = assets; drawDistance = distance; cellSize = gridSize; shadowDistance = shadows;
            meshScreenHeight = meshThreshold; minimumImpostorPixels = minimumPixels;
            Rebuild();
        }
        private void OnEnable() { RenderPipelineManager.beginCameraRendering += Render; if (cells == null) dirty = true; }
        private void OnDisable() { RenderPipelineManager.beginCameraRendering -= Render; impostors?.Dispose(); impostors = null; pendingGpuTrees = null; cameras.Clear(); cells = null; spatialCells = null; species = null; parts = null; dirty = true; }
        private void OnValidate() => dirty = true;

        private void Rebuild()
        {
            cameras.Clear(); TreeCount = 0;
            maximumTreeSize = 0;
            impostors?.Dispose(); impostors = null;
            pendingGpuTrees = null;
            if (library == null || terrains == null) { cells = Array.Empty<Cell>(); parts = Array.Empty<Part>(); species = Array.Empty<SpeciesRuntime>(); dirty = false; return; }
            if (!SystemInfo.supportsInstancing) throw new InvalidOperationException("Tree rendering requires GPU instancing support.");
            var entries = library.SpeciesList; species = new SpeciesRuntime[entries.Length];
            var allParts = new List<Part>();
            var sourceIndices = new Dictionary<GameObject, int>();
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i]; sourceIndices.Add(entry.source, i);
                var levels = (detailedGeometryForChecks || legacyHybridForChecks) && entry.originalLevels != null && entry.originalLevels.Length > 0 ? entry.originalLevels : entry.levels;
                var runtime = new SpeciesRuntime { Size = entry.size, Reference = entry.referencePoint, Scale = entry.rootScale, Levels = new LevelRuntime[levels.Length] };
                for (int lod = 0; lod < levels.Length; lod++)
                {
                    var source = levels[lod]; var indexes = new List<int>();
                    if (source.parts == null || source.parts.Length == 0) throw new InvalidOperationException("Bake the mesh/material tree library before building.");
                    foreach (var geometry in source.parts)
                    {
                            var mesh = geometry.mesh; var relative = geometry.localToRoot;
                            var material = geometry.material;
                            if (material == null || !material.enableInstancing) throw new InvalidOperationException("Tree LOD materials must enable GPU instancing.");
                            indexes.Add(allParts.Count);
                            allParts.Add(new Part { Mesh = mesh, Material = material, Submesh = geometry.submesh, Relative = relative, Identity = relative == Matrix4x4.identity, Triangles = mesh.GetIndexCount(geometry.submesh) / 3 });
                    }
                    runtime.Levels[lod] = new LevelRuntime { Threshold = source.screenHeight, Parts = indexes.ToArray() };
                }
                species[i] = runtime;
            }
            parts = allParts.ToArray();
            cachedTransform = transform.localToWorldMatrix;
            var grid = new Dictionary<Vector2Int, List<TreeRecord>>();
            bool ready = !detailedGeometryForChecks && SystemInfo.supportsComputeShaders && library.ImpostorCulling != null && library.ImpostorMesh != null;
            foreach (var entry in entries) ready &= entry.impostorMaterial != null;
            var gpu = new List<IndirectTreeImpostors.Tree>[entries.Length];
            for (int i = 0; i < gpu.Length; i++) gpu[i] = new List<IndirectTreeImpostors.Tree>();
            int renderLayer = 0;
            foreach (var terrain in terrains)
            {
                if (terrain == null || terrain.terrainData == null) continue;
                var data = terrain.terrainData; var prototypes = data.treePrototypes;
                renderLayer = terrain.gameObject.layer;
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
                    maximumTreeSize = Mathf.Max(maximumTreeSize, height);
                    var record = new TreeRecord { Matrix = world, Centre = centre, Size = height, Radius = height * 0.75f + 2, Species = index, Layer = terrain.gameObject.layer };
                    var key = new Vector2Int(Mathf.FloorToInt(centre.x / cellSize), Mathf.FloorToInt(centre.z / cellSize));
                    if (!grid.TryGetValue(key, out var bucket)) grid.Add(key, bucket = new List<TreeRecord>());
                    bucket.Add(record); TreeCount++;
                    if (ready) gpu[index].Add(new IndirectTreeImpostors.Tree { Matrix = world, Sphere = new Vector4(centre.x, centre.y, centre.z, height) });
                }
            }
            cells = new Cell[grid.Count]; int cellIndex = 0;
            spatialCells = new Dictionary<Vector2Int, Cell>(grid.Count);
            foreach (var entry in grid)
            {
                var bucket = entry.Value;
                var bounds = new Bounds(bucket[0].Centre, Vector3.zero);
                float maxSize = 0;
                foreach (var record in bucket) { bounds.Encapsulate(new Bounds(record.Centre, Vector3.one * record.Radius * 2)); maxSize = Mathf.Max(maxSize, record.Size); }
                var cell = new Cell { Bounds = bounds, Trees = bucket.ToArray(), MaxSize = maxSize };
                cells[cellIndex++] = cell; spatialCells.Add(entry.Key, cell);
            }
            // Staged/inactive maps must not allocate GPU resources: they may be rolled back without ever enabling.
            if (ready && TreeCount > 0) { pendingGpuTrees = gpu; gpuLayer = renderLayer; }
            dirty = false;
        }

        private void Render(ScriptableRenderContext context, Camera camera)
        {
            if (SuppressDrawingForChecks || camera == null || camera.cameraType == CameraType.Reflection || camera.cameraType == CameraType.Preview) return;
#if UNITY_EDITOR
            bool preview = UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(gameObject.scene);
            bool cameraPreview = camera.scene.IsValid() && UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(camera.scene);
            if (preview ? camera.scene != gameObject.scene : cameraPreview) return;
#endif
            if (dirty || cachedTransform != transform.localToWorldMatrix) Rebuild();
            if (TreeCount == 0 || parts == null || cells == null) return;
            if (pendingGpuTrees != null)
            { impostors = new IndirectTreeImpostors(library, pendingGpuTrees, gpuLayer); pendingGpuTrees = null; }
            int key = camera.GetInstanceID();
            if (!cameras.TryGetValue(key, out var cache))
            {
                // Offscreen verification/preview cameras must not accumulate persistent arrays.
                if (cameras.Count >= 4) cameras.Clear();
                cameras.Add(key, cache = new CameraCache(camera, parts.Length * 3));
            }
            float bias = Mathf.Max(0.01f, QualitySettings.lodBias);
            float effectiveShadows = Mathf.Min(shadowDistance, camera.farClipPlane);
            var pipeline = GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            effectiveShadows = Mathf.Min(effectiveShadows, pipeline != null ? pipeline.shadowDistance : QualitySettings.shadowDistance);
            if (camera.TryGetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(out var cameraData) && !cameraData.renderShadows) effectiveShadows = 0;
            int minimumLod = QualitySettings.maximumLODLevel;
            var view = camera.worldToCameraMatrix; var projection = camera.projectionMatrix;
            bool refresh = !cache.Valid || cache.View != view || cache.Projection != projection || cache.Bias != bias || cache.MinimumLod != minimumLod || cache.Mask != camera.cullingMask || cache.PixelHeight != camera.pixelHeight || cache.Shadows != effectiveShadows;
            if (refresh)
            {
                long started = Stopwatch.GetTimestamp();
                Populate(camera, cache, bias, minimumLod, effectiveShadows);
                cache.View = view; cache.Projection = projection; cache.Bias = bias; cache.MinimumLod = minimumLod; cache.Mask = camera.cullingMask; cache.PixelHeight = camera.pixelHeight; cache.Shadows = effectiveShadows; cache.Valid = true;
                cache.CullTime = Milliseconds(started);
            }
            else cache.CullTime = 0;
            long submitStarted = Stopwatch.GetTimestamp(); int draws = 0; long triangles = 0;
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
                    parameters.matProps = SmoothLods ? batch.Properties : null;
                    Graphics.RenderMeshInstanced(parameters, part.Mesh, part.Submesh, batch.Matrices, batch.Count);
                    draws++; triangles += part.Triangles * batch.Count;
                }
            }
            float projectionScale = camera.orthographic ? bias / Mathf.Max(0.001f, 2 * camera.orthographicSize) : bias / (2 * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad));
            IndirectDraws = impostors?.Render(camera, cache.Planes, projectionScale, MeshThreshold, minimumImpostorPixels, drawDistance, refresh, SmoothLods ? TransitionFraction : 0) ?? 0;
            VisibleTrees = cache.Visible; MeshTriangles = triangles; SubmittedDraws = draws + IndirectDraws; CullMilliseconds = cache.CullTime; SubmitMilliseconds = Milliseconds(submitStarted);
            Array.Copy(cache.Lods, VisibleLods, VisibleLods.Length);
        }

        private void Populate(Camera camera, CameraCache cache, float bias, int minimumLod, float shadowRange)
        {
            foreach (var bucket in cache.Buckets) bucket?.Clear();
            Array.Clear(cache.Lods, 0, cache.Lods.Length); cache.Visible = 0;
            GeometryUtility.CalculateFrustumPlanes(camera, cache.Planes);
            var eye = camera.transform.position;
            float distanceLimit = Mathf.Min(drawDistance, camera.farClipPlane), limitSquared = distanceLimit * distanceLimit;
            float projectionScale = camera.orthographic ? bias / Mathf.Max(0.001f, 2 * camera.orthographicSize) : bias / (2 * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad));
            bool smooth = SmoothLods;
            float meshThreshold = MeshThreshold;
            float minimumMeshHeight = meshThreshold * (smooth ? 1 - TransitionFraction : 1);
            cache.NearCells.Clear();
            if (impostors != null && camera.orthographic && maximumTreeSize * projectionScale < minimumMeshHeight && shadowRange <= 0) return;
            if (impostors != null && !camera.orthographic)
            {
                // Detailed geometry can only appear inside this radius. Query those grid cells directly.
                float radius = Mathf.Min(distanceLimit, Mathf.Max(maximumTreeSize * projectionScale / minimumMeshHeight, shadowRange + maximumTreeSize * 1.5f));
                int minX = Mathf.FloorToInt((eye.x-radius)/cellSize), maxX = Mathf.FloorToInt((eye.x+radius)/cellSize);
                int minZ = Mathf.FloorToInt((eye.z-radius)/cellSize), maxZ = Mathf.FloorToInt((eye.z+radius)/cellSize);
                for (int z = minZ; z <= maxZ; z++) for (int x = minX; x <= maxX; x++)
                    if (spatialCells.TryGetValue(new Vector2Int(x,z), out var cell)) cache.NearCells.Add(cell);
            }
            else cache.NearCells.AddRange(cells);
            foreach (var cell in cache.NearCells)
            {
                float nearest = cell.Bounds.SqrDistance(eye);
                if (nearest > limitSquared) continue;
                if (impostors != null && cell.MaxSize * projectionScale / (camera.orthographic ? 1 : Mathf.Max(0.01f, Mathf.Sqrt(nearest))) < minimumMeshHeight && nearest > shadowRange * shadowRange) continue;
                var padded = cell.Bounds; padded.Expand(shadowRange * 2);
                if (!GeometryUtility.TestPlanesAABB(cache.Planes, padded)) continue;
                foreach (var tree in cell.Trees)
                {
                    if ((camera.cullingMask & (1 << tree.Layer)) == 0) continue;
                    float squared = (tree.Centre - eye).sqrMagnitude;
                    if (squared > (distanceLimit + tree.Radius) * (distanceLimit + tree.Radius)) continue;
                    bool visible = SphereVisible(cache.Planes, tree.Centre, tree.Radius);
                    bool shadow = shadowRange > 0 && squared <= (shadowRange + tree.Radius) * (shadowRange + tree.Radius);
                    if (!visible && !shadow) continue;
                    var definition = species[tree.Species];
                    float screenHeight = tree.Size * projectionScale / (camera.orthographic ? 1 : Mathf.Max(0.01f, Mathf.Sqrt(squared)));
                    if (smooth)
                    {
                        // Shadows have their own coarse mesh and never duplicate the visible caster.
                        if (shadow)
                        {
                            int shadowLod = Mathf.Max(Mathf.Clamp(minimumLod, 0, definition.Levels.Length - 1), Mathf.Min(2, definition.Levels.Length - 1));
                            AddLevel(cache, tree, definition.Levels[shadowLod], 2, 1);
                        }
                        if (!visible) continue;
                        // If an editable impostor threshold overlaps a mesh transition, omit the
                        // now-unreachable lower levels instead of switching mesh partway through a fade.
                        int lastMesh = impostors == null ? definition.Levels.Length - 1 : ChooseLod(definition.Levels, meshThreshold * (1 + TransitionFraction) / (1 - TransitionFraction), minimumLod);
                        int first, second; float coverage;
                        if (impostors != null && screenHeight < meshThreshold * (1 + TransitionFraction))
                        {
                            if (screenHeight <= minimumMeshHeight) continue;
                            first = lastMesh; second = -1;
                            coverage = Mathf.InverseLerp(minimumMeshHeight, meshThreshold * (1 + TransitionFraction), screenHeight);
                        }
                        else ChooseBlend(definition.Levels, screenHeight, minimumLod, lastMesh, impostors != null, out first, out second, out coverage);
                        if (first < 0) continue;
                        cache.Visible++; cache.Lods[Mathf.Min(first, cache.Lods.Length - 1)]++;
                        if (coverage > 0) AddLevel(cache, tree, definition.Levels[first], 1, coverage);
                        if (second >= 0 && coverage < 1) AddLevel(cache, tree, definition.Levels[second], 1, coverage > 0 ? -coverage : 1);
                        continue;
                    }
                    if (impostors != null && screenHeight < meshThreshold) continue;
                    int lod = ChooseLod(definition.Levels, screenHeight, minimumLod);
                    if (lod < 0) continue;
                    int mode = visible ? (shadow ? 0 : 1) : 2;
                    if (visible) { cache.Visible++; cache.Lods[Mathf.Min(lod, cache.Lods.Length - 1)]++; }
                    AddLevel(cache, tree, definition.Levels[lod], mode, 1);
                }
            }
            if (smooth) foreach (var bucket in cache.Buckets) bucket?.UploadFades();
        }
        private void AddLevel(CameraCache cache, TreeRecord tree, LevelRuntime level, int mode, float fade)
        {
            foreach (int partIndex in level.Parts)
            {
                int index = partIndex * 3 + mode;
                var bucket = cache.Buckets[index];
                if (bucket == null) cache.Buckets[index] = bucket = new Bucket();
                bucket.Layer = tree.Layer;
                var part = parts[partIndex];
                bucket.Add(part.Identity ? tree.Matrix : tree.Matrix * part.Relative, tree.Centre, tree.Radius, fade);
            }
        }
        private static void ChooseBlend(LevelRuntime[] levels, float height, int minimum, int last, bool impostor, out int first, out int second, out float coverage)
        {
            first = second = -1; coverage = 1;
            for (int i = Mathf.Clamp(minimum, 0, levels.Length - 1); i <= last; i++)
            {
                if (impostor && i == last) { first = i; return; }
                float low = levels[i].Threshold * (1 - TransitionFraction), high = levels[i].Threshold * (1 + TransitionFraction);
                if (height >= high) { first = i; return; }
                if (height > low)
                {
                    first = i; second = i + 1 <= last ? i + 1 : -1;
                    coverage = Mathf.InverseLerp(low, high, height); return;
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
        private sealed class Part { internal Mesh Mesh; internal Material Material; internal Matrix4x4 Relative; internal int Submesh; internal bool Identity; internal uint Triangles; }
        private struct TreeRecord { internal Matrix4x4 Matrix; internal Vector3 Centre; internal float Size, Radius; internal int Species, Layer; }
        private sealed class Cell { internal Bounds Bounds; internal TreeRecord[] Trees; internal float MaxSize; }
        private sealed class CameraCache
        {
            internal readonly Camera Camera; internal readonly Bucket[] Buckets; internal readonly Plane[] Planes = new Plane[6];
            internal readonly int[] Lods = new int[8];
            internal readonly List<Cell> NearCells = new List<Cell>();
            internal Matrix4x4 View, Projection; internal float Bias, Shadows; internal int MinimumLod, Mask, Visible, PixelHeight;
            internal bool Valid; internal double CullTime;
            internal CameraCache(Camera camera, int count) { Camera = camera; Buckets = new Bucket[count]; }
        }
        private sealed class Bucket
        {
            internal readonly List<Batch> Batches = new List<Batch>(); internal int Layer; private int current;
            internal void Clear() { foreach (var batch in Batches) batch.Count = 0; current = 0; }
            internal void Add(Matrix4x4 matrix, Vector3 centre, float radius, float fade)
            {
                if (current == Batches.Count) Batches.Add(new Batch());
                var batch = Batches[current];
                if (batch.Count == 511) { current++; if (current == Batches.Count) Batches.Add(new Batch()); batch = Batches[current]; }
                var bounds = new Bounds(centre, Vector3.one * (radius * 2));
                if (batch.Count == 0) batch.Bounds = bounds; else batch.Bounds.Encapsulate(bounds);
                batch.Fades[batch.Count] = fade; batch.Matrices[batch.Count++] = matrix;
            }
            internal void UploadFades() { foreach (var batch in Batches) if (batch.Count > 0) batch.Properties.SetFloatArray(TreeFadeId, batch.Fades); }
        }
        private sealed class Batch
        {
            internal readonly Matrix4x4[] Matrices = new Matrix4x4[511];
            internal readonly float[] Fades = new float[511];
            internal readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
            internal Bounds Bounds; internal int Count;
        }
    }
}
