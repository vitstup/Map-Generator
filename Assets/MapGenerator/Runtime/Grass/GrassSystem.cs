using System;
using System.Collections.Generic;
using TacticalMaps.Generation;
using UnityEngine;
using UnityEngine.Rendering;

namespace TacticalMaps.Grass
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class GrassSystem : MonoBehaviour
    {
        [Header("Resources")]
        [SerializeField] private GrassLibrary library;
        [SerializeField] private GrassMap currentMap;
        [Header("Coverage")]
        [SerializeField, Range(0, 1)] private float density = .9f;
        [SerializeField] private Vector2 heightRange = new Vector2(.22f, .72f);
        [SerializeField, Range(0, 60)] private float maximumSlope = 35;
        [SerializeField, Min(0)] private float shoreClearanceHeight = .4f;
        [SerializeField] private int seed = 7819;
        [Header("Appearance")]
        [SerializeField] private Color meadowColor = new Color(.32f, .43f, .19f);
        [SerializeField] private Color dryColor = new Color(.49f, .45f, .25f);
        [SerializeField, Range(0, 1)] private float windStrength = .22f;
        [SerializeField, Min(0)] private float windSpeed = 1.2f;
        [Header("LOD and memory")]
        [SerializeField, Range(16, 64)] private int chunkSize = 16;
        [SerializeField, Range(16, 64)] private int candidatesPerAxis = 64;
        [SerializeField, Range(32, 384)] private int maximumResidentChunks = 128;
        [SerializeField, Range(1, 16)] private int chunksPreparedPerFrame = 4;
        [SerializeField, Min(20)] private float geometryDistance = 80;
        [SerializeField, Min(1)] private float nearDistance = 45;
        [SerializeField, Range(.2f, 8)] private float minimumPixels = .7f;
        [SerializeField, Range(.1f, 1)] private float middleDensity = 1;
        [SerializeField, Min(100)] private float meadowDistance = 280;
        [SerializeField, Range(0, 1)] private float turfStrength = 1;
        [Header("Diagnostics: 0 normal, 1 density, 2 LOD, 3 chunks")]
        [SerializeField, Range(0, 3)] private int debugView;
        private readonly Dictionary<int, GrassView> views = new Dictionary<int, GrassView>();
        private int revision;
        private int surfaceRevision = int.MinValue;
        internal GrassMap CurrentMap => currentMap;
        internal int ResidentChunks { get; private set; }
        internal long BufferBytes { get; private set; }
        internal int SubmittedDraws { get; private set; }
        internal bool SuppressGeometryForChecks { get; set; }
        internal GrassLibrary Library => library;
        internal int BakeMode { get; set; }
        internal bool MatureForChecks { get; set; }

        /// <summary>Suppress grass in a world-space XZ rectangle, at coverage-map resolution.</summary>
        public void Exclude(Bounds worldArea)
        {
            if (currentMap == null) return;
            currentMap.Exclude(worldArea);
            // Coverage texture references may have changed by copy-on-write.
            ReleaseViews(); ApplySurface(true);
        }

        // Called only while a replacement map is inactive; failed builds leave the old map intact.
        internal void Stage(MapData map, Transform root)
        {
            if (library == null || !library.Valid) throw new InvalidOperationException("Assign the grass resource library.");
            var ground = new Terrain[3]; var textures = new Texture2D[3];
            try
            {
                var names = new[] { "Terrain_Main", "Terrain_Surroundings", "Terrain_Horizon" };
                for (int i = 0; i < 3; i++)
                {
                    ground[i] = root.Find(names[i]).GetComponent<Terrain>();
                    int n = map.Grass != null ? map.Grass.Resolutions[i] : 1;
                    textures[i] = new Texture2D(n, n, TextureFormat.RGBA32, false, true)
                    { name = "GrassCoverage_" + i, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                    textures[i].SetPixels32(map.Grass != null ? map.Grass.Pixels[i] : new[] { new Color32(0, 0, 0, 255) });
                    textures[i].Apply(false, false);
                }
                root.gameObject.AddComponent<GrassMap>().Configure(this, ground, textures, ground[0].transform.position.y + map.SeaLevelMeters);
            }
            catch { foreach (var texture in textures) GrassMap.Dispose(texture); throw; }
        }
        internal void Attach(GrassMap map) { ReleaseViews(); currentMap = map; ApplySurface(true); }
        internal void Detach(GrassMap map) { if (currentMap != map) return; ReleaseViews(); SetSurface(false); currentMap = null; }
        private void OnEnable() { RenderPipelineManager.beginCameraRendering += Render; ApplySurface(true); }
        private void OnDisable() { RenderPipelineManager.beginCameraRendering -= Render; ReleaseViews(); SetSurface(false); }
        private void OnValidate() { revision++; }
        private void SetSurface(bool active)
        {
            if (currentMap == null) return;
            foreach (var terrain in currentMap.Terrains)
                if (terrain != null && terrain.materialTemplate != null) terrain.materialTemplate.SetFloat("_GrassEnabled", active ? 1 : 0);
        }
        private void ApplySurface(bool force = false)
        {
            if (currentMap == null) return;
            if (!force && surfaceRevision == revision) return;
            surfaceRevision = revision;
            for (int i = 0; i < currentMap.Terrains.Length; i++)
            {
                var t = currentMap.Terrains[i]; if (t == null || t.materialTemplate == null) continue;
                var m = t.materialTemplate; var p = t.transform.position; var size = t.terrainData.size;
                m.SetTexture("_GrassCoverage", currentMap.Coverage[i]);
                m.SetTexture("_GrassTurf", library != null ? library.TurfAlbedo : null);
                m.SetTexture("_GrassTurfNormal", library != null ? library.TurfNormal : null);
                m.SetFloat("_GrassTurfStrength", turfStrength);
                m.SetVector("_GrassRect", new Vector4(p.x, p.z, size.x, size.z));
                m.SetColor("_GrassMeadow", meadowColor); m.SetColor("_GrassDry", dryColor);
                m.SetVector("_GrassWind", new Vector4(windStrength, windSpeed, seed, density));
                m.SetVector("_GrassOptions", new Vector4(chunkSize, debugView, currentMap.SeaLevel + shoreClearanceHeight, Mathf.Cos(maximumSlope * Mathf.Deg2Rad)));
                m.SetFloat("_GrassEnabled", isActiveAndEnabled ? 1 : 0);
            }
        }
        private void Render(ScriptableRenderContext context, Camera camera)
        {
            if (currentMap == null || !currentMap.isActiveAndEnabled || library == null || !library.Valid) return;
            if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView) return;
            if ((camera.cullingMask & (1 << gameObject.layer)) == 0) return;
            ApplySurface(); SubmittedDraws = 0;
            if (SuppressGeometryForChecks || !SystemInfo.supportsComputeShaders || !SystemInfo.supportsInstancing) return;
            int key = camera.GetInstanceID();
            if (!views.TryGetValue(key, out var renderer))
            {
                if (views.Count >= 2)
                {
                    int oldestKey=0; float oldestTime=float.PositiveInfinity;
                    foreach (var pair in views)
                        if (pair.Value.LastUsed < oldestTime) { oldestKey=pair.Key; oldestTime=pair.Value.LastUsed; }
                    // Extra simultaneous cameras retain the Terrain representation instead of thrashing pools.
                    if (Time.realtimeSinceStartup-oldestTime < .5f) return;
                    views[oldestKey].Dispose(); views.Remove(oldestKey);
                }
                renderer = new GrassView(library, currentMap, chunkSize, candidatesPerAxis, maximumResidentChunks, revision);
                views.Add(key, renderer);
            }
            if (renderer.Revision != revision) { ReleaseViews(); return; }
            renderer.Render(camera, new GrassRenderSettings
            {
                Density = density, Heights = heightRange, Slope = maximumSlope, Shore = shoreClearanceHeight,
                Seed = seed, Wind = new Vector2(windStrength, windSpeed), Meadow = meadowColor, Dry = dryColor,
                Distance = geometryDistance, NearDistance = nearDistance, MinPixels = minimumPixels,
                MiddleDensity = middleDensity, PrepareBudget = chunksPreparedPerFrame, Debug = debugView, Layer = gameObject.layer
                , MeadowDistance = meadowDistance, BakeMode = BakeMode, Mature = MatureForChecks
            });
            ResidentChunks = renderer.ResidentCount; SubmittedDraws = renderer.Draws;
            BufferBytes = 0; foreach (var v in views.Values) BufferBytes += v.BufferBytes;
        }
        internal uint[] ReadCountsForChecks(Camera camera) => views.TryGetValue(camera.GetInstanceID(), out var v) ? v.ReadCounts() : new uint[2];
        private void ReleaseViews() { foreach (var v in views.Values) v.Dispose(); views.Clear(); ResidentChunks = 0; BufferBytes = 0; SubmittedDraws = 0; }
    }
    internal struct GrassRenderSettings
    {
        internal float Density, Slope, Shore, Distance, NearDistance, MinPixels, MiddleDensity;
        internal float MeadowDistance;
        internal Vector2 Heights, Wind;
        internal Color Meadow, Dry;
        internal int Seed, PrepareBudget, Debug, Layer;
        internal int BakeMode;
        internal bool Mature;
    }

    // Independent dense near pool and coarse meadow pool. A larger far radius never expands the dense buffer.
    internal sealed class GrassView : IDisposable
    {
        private readonly GrassRenderer near, meadow;
        internal int Revision => near.Revision;
        internal float LastUsed => near.LastUsed;
        internal int ResidentCount => near.ResidentCount + meadow.ResidentCount;
        internal int Draws => near.Draws + meadow.Draws;
        internal long BufferBytes => near.BufferBytes + meadow.BufferBytes;
        internal GrassView(GrassLibrary library, GrassMap map, int chunk, int axis, int capacity, int revision)
        {
            try { near = new GrassRenderer(library,map,chunk,axis,capacity,revision,0); meadow = new GrassRenderer(library,map,64,32,128,revision,1); }
            catch { near?.Dispose(); meadow?.Dispose(); throw; }
        }
        internal void Render(Camera camera, GrassRenderSettings settings)
        { near.Render(camera,settings); var coarse=settings; coarse.Distance=settings.MeadowDistance; coarse.PrepareBudget=Mathf.Max(4,settings.PrepareBudget); meadow.Render(camera,coarse); }
        internal uint[] ReadCounts() => new[] { near.ReadCounts()[0], meadow.ReadCounts()[1] };
        public void Dispose() { near.Dispose(); meadow.Dispose(); }
    }
}
