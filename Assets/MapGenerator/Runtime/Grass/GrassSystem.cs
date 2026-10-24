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
        [SerializeField] private Transform focusTarget;
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
        [Header("Flowers: shared by every LOD")]
        [SerializeField, Tooltip("Show flowers on every geometry LOD and on Terrain. Keeps the palette, density and distance settings.")]
        private bool showFlowers = true;
        public bool ShowFlowers
        {
            get => showFlowers;
            set
            {
                if(showFlowers==value)return;
                showFlowers=value;revision++;
                ApplySurface(true);
            }
        }
        [SerializeField, Range(0,.2f)] private float flowerDensity = .035f;
        [SerializeField, Range(4,40)] private float flowerPatchSize = 12;
        [SerializeField, Range(.04f,.16f)] private float flowerRadius = .095f;
        [SerializeField, Range(0,1), Tooltip("Fraction of flower marks retained on the far blue Terrain LOD.")] private float distantFlowerRetention=.55f;
        [SerializeField, Range(0,1), Tooltip("Fraction of flower marks retained on purple distant cards.")] private float purpleFlowerRetention=.5f;
        [SerializeField, Range(.1f,1), Tooltip("Head size on the far blue Terrain LOD, relative to other LODs.")] private float distantFlowerSize=2f/3f;
        [SerializeField, Min(1), Tooltip("Camera distance in metres beyond which Terrain flower marks stop drawing. They fade over the final 10%.")] private float distantFlowerMaximumDistance=3000;
        [SerializeField, Tooltip("Up to eight weighted flower types. Changing colors does not require rebaking.")]
        private GrassFlowerType[] flowerPalette = {
            new GrassFlowerType("White rosette",new Color(.83f,.84f,.72f),.5f),
            new GrassFlowerType("Gold cup",new Color(.76f,.51f,.13f),.24f,.9f,GrassFlowerForm.Cup),
            new GrassFlowerType("Violet bell",new Color(.4f,.31f,.65f),.18f,1.1f,GrassFlowerForm.Bell),
            new GrassFlowerType("Red star",new Color(.65f,.12f,.1f),.08f,1,GrassFlowerForm.Star)
        };
        private readonly Vector4[] flowerColors=new Vector4[GrassFlowerType.Maximum];
        private readonly Vector4[] flowerShapes=new Vector4[GrassFlowerType.Maximum];
        private int flowerRevision=int.MinValue,flowerCount;
        [Header("LOD and memory")]
        [SerializeField, Range(16, 64)] private int chunkSize = 16;
        [SerializeField, Range(16, 64)] private int candidatesPerAxis = 64;
        [SerializeField, Range(32, 512)] private int maximumResidentChunks = 416;
        [SerializeField, Range(1, 16)] private int chunksPreparedPerFrame = 8;
        [SerializeField, Min(20)] private float geometryDistance = 144;
        [SerializeField, Min(1)] private float nearDistance = 84;
        [SerializeField, Range(.2f, 8)] private float minimumPixels = .7f;
        [SerializeField, Range(.1f, 1)] private float middleDensity = 1;
        [SerializeField, Min(100)] private float meadowDistance = 420;
        [SerializeField, Range(450, 900)] private float distantDistance = 900;
        [SerializeField, Range(0, 1)] private float turfStrength = 1;
        [Header("Diagnostics: 0 normal, 1 density, 2 LOD, 3 chunks")]
        [SerializeField, Range(0, 3)] private int debugView;
        private readonly Dictionary<int, GrassView> views = new Dictionary<int, GrassView>();
        private int revision;
        private readonly Dictionary<int,GrassFlowerField> flowerFields=new Dictionary<int,GrassFlowerField>();
        internal int FlowerCacheBuildsForChecks(Camera camera)=>flowerFields.TryGetValue(camera.GetInstanceID(),out var field)?field.Builds:0;
        internal float BlueFlowerRetentionForChecks {get=>distantFlowerRetention;set=>distantFlowerRetention=value;}
        private int surfaceRevision = int.MinValue;
        internal GrassMap CurrentMap => currentMap;
        internal int ResidentChunks { get; private set; }
        internal long BufferBytes { get; private set; }
        internal int SubmittedDraws { get; private set; }
        internal bool SuppressGeometryForChecks { get; set; }
        internal GrassLibrary Library => library;
        internal int BakeMode { get; set; }
        internal bool MatureForChecks { get; set; }
        internal bool SuppressCardsForChecks { get; set; }
        internal Vector4 BakeRegion { get; set; }
        internal Vector3? FocusForChecks { get; set; }
        internal int TopologyForChecks { get; set; }
        internal int TransitionForChecks { get; set; }
        internal int GeometryMaskForChecks { get; set; } = 7;
        private bool suppressFlowers;
        internal bool SuppressFlowersForChecks { get=>suppressFlowers;set {suppressFlowers=value;ApplySurface(true);} }
        private Vector4 FlowerSettings => new Vector4(!showFlowers||suppressFlowers||flowerCount==0?0:flowerDensity,flowerPatchSize,flowerRadius,0);
        private bool freezeWind;
        internal bool FreezeWindForChecks { get=>freezeWind; set {freezeWind=value;ApplySurface(true);} }

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
        internal void Detach(GrassMap map) { if (currentMap != map) return; ReleaseViews(); ReleaseFlowerFields(); SetSurface(false); currentMap = null; }
        private void OnEnable() { RenderPipelineManager.beginCameraRendering += Render; ApplySurface(true); }
        private void OnDisable() { RenderPipelineManager.beginCameraRendering -= Render; ReleaseViews(); ReleaseFlowerFields(); SetSurface(false); }
        private void OnValidate() { revision++; }
        private void SetSurface(bool active)
        {
            if (currentMap == null) return;
            foreach (var terrain in currentMap.Terrains)
                if (terrain != null && terrain.materialTemplate != null) terrain.materialTemplate.SetFloat("_GrassEnabled", active ? 1 : 0);
        }
        private void ApplySurface(bool force = false)
        {
            if(force)flowerRevision=int.MinValue;
            PrepareFlowerPalette();
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
                m.SetTexture("_GrassTurfFlowers",library!=null&&library.TurfFlowers!=null?library.TurfFlowers:Texture2D.blackTexture);
                m.SetVector("_GrassFlowers",FlowerSettings);
                m.SetInt("_GrassFlowerCount",flowerCount);
                m.SetVectorArray("_GrassFlowerColors",flowerColors);m.SetVectorArray("_GrassFlowerShapes",flowerShapes);
                // Terrain's derived materials copy declared properties, not anonymous arrays.
                for(int j=0;j<GrassFlowerType.Maximum;j++)
                    m.SetVector("_GrassFlowerColor"+j,new Vector4(flowerColors[j].x,flowerColors[j].y,flowerColors[j].z,flowerShapes[j].x));
                m.SetVector("_GrassFlowerWeights0",new Vector4(flowerColors[0].w,flowerColors[1].w,flowerColors[2].w,flowerColors[3].w));
                m.SetVector("_GrassFlowerWeights1",new Vector4(flowerColors[4].w,flowerColors[5].w,flowerColors[6].w,flowerColors[7].w));
                m.SetVector("_GrassFlowerForms0",new Vector4(flowerShapes[0].y,flowerShapes[1].y,flowerShapes[2].y,flowerShapes[3].y));
                m.SetVector("_GrassFlowerForms1",new Vector4(flowerShapes[4].y,flowerShapes[5].y,flowerShapes[6].y,flowerShapes[7].y));
                m.SetFloat("_GrassTurfStrength", turfStrength);
                m.SetVector("_GrassRect", new Vector4(p.x, p.z, size.x, size.z));
                m.SetColor("_GrassMeadow", meadowColor); m.SetColor("_GrassDry", dryColor);
                m.SetVector("_GrassWind", new Vector4(freezeWind?0:windStrength, windSpeed, seed, density));
                m.SetVector("_GrassOptions", new Vector4(chunkSize, debugView, currentMap.SeaLevel + shoreClearanceHeight, Mathf.Cos(maximumSlope * Mathf.Deg2Rad)));
                m.SetFloat("_GrassEnabled", isActiveAndEnabled ? 1 : 0);
            }
        }
        private void PrepareFlowerPalette()
        {
            if(flowerRevision==revision) return;
            flowerRevision=revision;flowerCount=0;float total=0;
            if(flowerPalette!=null) foreach(var type in flowerPalette)
            {
                if(type==null||type.weight<=0)continue;if(flowerCount==GrassFlowerType.Maximum)break;
                total+=type.weight;flowerCount++;
            }
            flowerCount=0;float cumulative=0;
            if(total>0&&flowerPalette!=null) foreach(var type in flowerPalette)
            {
                if(type==null||type.weight<=0)continue;if(flowerCount==GrassFlowerType.Maximum)break;
                cumulative+=type.weight/total;var c=type.color;
                flowerColors[flowerCount]=new Vector4(c.r,c.g,c.b,cumulative);
                flowerShapes[flowerCount]=new Vector4(Mathf.Clamp(type.size,.5f,2),(int)type.form,0,0);flowerCount++;
            }
            for(int i=flowerCount;i<GrassFlowerType.Maximum;i++){flowerColors[i]=Vector4.zero;flowerShapes[i]=Vector4.one;}
        }
        private void Render(ScriptableRenderContext context, Camera camera)
        {
            if (currentMap == null || !currentMap.isActiveAndEnabled || library == null || !library.Valid) return;
            if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView) return;
            if ((camera.cullingMask & (1 << gameObject.layer)) == 0) return;
#if UNITY_EDITOR
            // Preview scenes (baking/checks) must not draw into the live world, or vice versa.
            bool preview=UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(gameObject.scene);
            bool cameraPreview=camera.scene.IsValid()&&UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(camera.scene);
            if((preview||cameraPreview)&&camera.scene!=gameObject.scene) return;
#endif
            ApplySurface(); SubmittedDraws = 0;
            if(showFlowers&&flowerCount>0&&flowerDensity>0&&SystemInfo.supportsComputeShaders&&library.FlowerField!=null)
            {
                int cameraId=camera.GetInstanceID();
                if(!flowerFields.TryGetValue(cameraId,out var field))
                {
                    if(flowerFields.Count>=2)
                    {
                        int oldest=0;float oldestTime=float.PositiveInfinity;
                        foreach(var pair in flowerFields)if(pair.Value.LastUsed<oldestTime){oldest=pair.Key;oldestTime=pair.Value.LastUsed;}
                        if(Time.realtimeSinceStartup-oldestTime>=.5f){flowerFields[oldest].Dispose();flowerFields.Remove(oldest);}
                    }
                    if(flowerFields.Count<2){field=new GrassFlowerField(library.FlowerField);flowerFields.Add(cameraId,field);}
                }
                if(field!=null)field.Prepare(camera,new Vector4(flowerDensity,flowerPatchSize,flowerRadius,0),flowerColors,flowerCount,seed);
                else Shader.SetGlobalFloat("_GrassFlowerFieldReady",0);
            }
            else
            {
                if(!showFlowers)ReleaseFlowerFields();
                Shader.SetGlobalFloat("_GrassFlowerFieldReady",0);
            }
            Shader.SetGlobalFloat("_GrassBlueFlowerRetention",BlueFlowerRetentionForChecks);
            Shader.SetGlobalVector("_GrassFlowerLodFocus",ResolveFocus(camera));
            Shader.SetGlobalVector("_GrassFlowerLodRanges",new Vector4(distantDistance*.66f,distantDistance,meadowDistance*.72f,meadowDistance));
            Shader.SetGlobalVector("_GrassFlowerLodStyle",new Vector4(purpleFlowerRetention,distantFlowerSize,Mathf.Max(1,distantFlowerMaximumDistance),0));
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
                CheckTopology=TopologyForChecks, TransitionCheck=TransitionForChecks, GeometryMask=GeometryMaskForChecks,
                Focus=ResolveFocus(camera), NearRange=new Vector2(nearDistance,geometryDistance),
                Density = density, Heights = heightRange, Slope = maximumSlope, Shore = shoreClearanceHeight,
                Seed = seed, Wind = new Vector2(freezeWind?0:windStrength, windSpeed), Meadow = meadowColor, Dry = dryColor,
                Distance = geometryDistance, NearDistance = nearDistance, MinPixels = minimumPixels,
                MiddleDensity = middleDensity, PrepareBudget = chunksPreparedPerFrame, Debug = debugView, Layer = gameObject.layer
                , MeadowDistance = meadowDistance, DistantDistance = distantDistance, BakeMode = BakeMode, Mature = MatureForChecks,
                SuppressCards = SuppressCardsForChecks, BakeRegion = BakeRegion
                , Flowers=FlowerSettings,FlowerColors=flowerColors,FlowerShapes=flowerShapes,FlowerCount=flowerCount
            });
            ResidentChunks = renderer.ResidentCount; SubmittedDraws = renderer.Draws;
            BufferBytes = 0; foreach (var v in views.Values) BufferBytes += v.BufferBytes;
        }
        private Vector3 ResolveFocus(Camera camera)
        {
            var origin=camera.transform.position;
            // Cover the foreground and orbit target together. Centering solely on the target
            // would remove foreground grass at a low viewing angle.
            if(FocusForChecks.HasValue) return Vector3.Lerp(origin,FocusForChecks.Value,.5f);
            if(focusTarget!=null&&camera.CompareTag("MainCamera")) return Vector3.Lerp(origin,focusTarget.position,.5f);
            // Editor/auxiliary cameras have no RTS orbit target. Intersect their central ray
            // with the actual Terrain height, using two refinements and a bounded horizon fallback.
            var direction=camera.transform.forward;
            if(direction.y>-.08f) return origin;
            Vector3 point=origin;
            for(int j=0;j<3;j++)
            {
                float height=currentMap.SeaLevel;
                foreach(var terrain in currentMap.Terrains)
                {
                    var p=terrain.transform.position;var size=terrain.terrainData.size;
                    if(point.x<p.x||point.z<p.z||point.x>p.x+size.x||point.z>p.z+size.z) continue;
                    height=p.y+terrain.terrainData.GetInterpolatedHeight((point.x-p.x)/size.x,(point.z-p.z)/size.z);break;
                }
                float distance=Mathf.Clamp((height-origin.y)/direction.y,0,2000);
                point=origin+direction*distance;
            }
            return Vector3.Lerp(origin,point,.5f);
        }
        internal uint[] ReadCountsForChecks(Camera camera) => views.TryGetValue(camera.GetInstanceID(), out var v) ? v.ReadCounts() : new uint[2];
#if UNITY_EDITOR
        internal Vector3? FindFlowerForChecks(Camera camera) => views.TryGetValue(camera.GetInstanceID(),out var v)?v.FindFlowerForChecks():null;
#endif
        internal uint[] ReadDrawCountsForChecks(Camera camera) => views.TryGetValue(camera.GetInstanceID(),out var v)?v.ReadDrawCounts():new uint[4];
        internal Dictionary<uint,int> ReadLodMembershipForChecks(Camera camera) => views.TryGetValue(camera.GetInstanceID(),out var v)?v.ReadLodMembershipForChecks():new Dictionary<uint,int>();
        private void ReleaseFlowerFields() {foreach(var field in flowerFields.Values)field.Dispose();flowerFields.Clear();}
        private void ReleaseViews() { foreach (var v in views.Values) v.Dispose(); views.Clear(); ResidentChunks = 0; BufferBytes = 0; SubmittedDraws = 0; }
    }
    internal struct GrassRenderSettings
    {
        internal float Density, Slope, Shore, Distance, NearDistance, MinPixels, MiddleDensity;
        internal float MeadowDistance, DistantDistance;
        internal Vector4 BakeRegion;
        internal Vector4 Flowers;
        internal Vector4[] FlowerColors,FlowerShapes;
        internal int FlowerCount;
        internal Vector2 Heights, Wind, NearRange;
        internal Vector3 Focus;
        internal Color Meadow, Dry;
        internal int Seed, PrepareBudget, Debug, Layer;
        internal int BakeMode, CheckTopology, TransitionCheck, GeometryMask;
        internal bool Mature, SuppressCards;
    }

    // Three bounded pools. Extending distant groups never expands the dense grass buffer.
    internal sealed class GrassView : IDisposable
    {
        private readonly GrassRenderer near, meadow, cards;
        internal int Revision => near.Revision;
        internal float LastUsed => near.LastUsed;
        internal int ResidentCount => near.ResidentCount + meadow.ResidentCount + (cards?.ResidentCount ?? 0);
        internal int Draws => near.Draws + meadow.Draws + (cards?.Draws ?? 0);
        internal long BufferBytes => near.BufferBytes + meadow.BufferBytes + (cards?.BufferBytes ?? 0);
        internal GrassView(GrassLibrary library, GrassMap map, int chunk, int axis, int capacity, int revision)
        {
            try
            {
                near = new GrassRenderer(library,map,chunk,axis,capacity,revision,0);
                meadow = new GrassRenderer(library,map,64,40,224,revision,1);
                if(library.CardsValid) cards = new GrassRenderer(library,map,128,40,320,revision,2);
            }
            catch { near?.Dispose(); meadow?.Dispose(); cards?.Dispose(); throw; }
        }
        internal void Render(Camera camera, GrassRenderSettings settings)
        {
            near.Render(camera,settings);
            var coarse=settings; coarse.Distance=settings.MeadowDistance; coarse.PrepareBudget=Mathf.Max(4,settings.PrepareBudget);
            meadow.Render(camera,coarse);
            if(cards!=null) { coarse.Distance=settings.DistantDistance; cards.Render(camera,coarse); }
        }
        internal Dictionary<uint,int> ReadLodMembershipForChecks() => near.ReadLodMembershipForChecks();
#if UNITY_EDITOR
        internal Vector3? FindFlowerForChecks() => near.FindFlowerForChecks();
#endif
        internal uint[] ReadDrawCounts() {var n=near.ReadCounts();return new[]{n[0],n[1],meadow.ReadCounts()[1],cards?.ReadCounts()[1] ?? 0};}
        internal uint[] ReadCounts() {var n=ReadDrawCounts();return new[]{n[0]+n[1],n[2]+n[3]};}
        public void Dispose() { near.Dispose(); meadow.Dispose(); cards?.Dispose(); }
    }
}
