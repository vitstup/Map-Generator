using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace TacticalMaps.Generation
{
    [DisallowMultipleComponent]
    internal sealed class AgricultureGenerator : MonoBehaviour
    {
        [SerializeField] private bool generateAgriculture = true;
        [SerializeField] private Texture2D densitySource;
        [SerializeField, Range(0, 1)] private float coverage = .9f;
        [SerializeField, Min(300)] private float districtSize = 600;
        [SerializeField, Range(0, 20)] private float maximumSlope = 9;
        [SerializeField, Range(0, .2f)] private float densityThreshold = .04f;
        [SerializeField, Range(.1f, 1)] private float fullDensity = .6f;
        [SerializeField, Range(1, 3)] private float densityExponent = 1.6f;
        [SerializeField, Range(1, 500)] private int maximumFarms = 500;
        [SerializeField, Range(100, 4000)] private int maximumFields = 2400;
        [SerializeField, Range(.1f, 1)] private float fieldAreaScale = .45f;
        [Tooltip("Distance from the current map center. Zero disables access roads; increase to cover the horizon.")]
        [SerializeField, Min(0)] private float connectionRadius = 6000;
        [SerializeField, Min(100)] private float maximumAccessLength = 1800;
        [SerializeField, Min(0)] private float beachClearance = 8;
        [SerializeField, Range(0,.5f)] private float fieldBuildingsChance = .18f;
        [SerializeField] private int seed = 1467;
        [SerializeField, HideInInspector] private Vector2[] houseFootprints;
        [SerializeField, HideInInspector] private int layoutVersion;
        private Texture2D cachedSource;
        private ushort[] cachedDensity;
        private Hash128 cachedHash;
        private ulong cachedKey;
        private AgricultureData cachedResult;
        private readonly object cacheGate = new object();
        internal string LastTimings { get; private set; }
        internal AgricultureData Generate(MapData map, int radius, CancellationToken token)
        {
            var input = Capture(map, radius); if (input == null) return null;
            return Resolve(input, token);
        }
        internal async Task<AgricultureData> GenerateAsync(MapData map, int radius, CancellationToken token)
        {
            var input = Capture(map, radius); if (input == null) return null;
            return await Task.Run(() => Resolve(input, token), token);
        }
        private AgricultureData Resolve(AgricultureInput input, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); var clock = Stopwatch.StartNew(); ulong key = input.Fingerprint();
            lock (cacheGate) if (cachedResult != null && cachedKey == key) { LastTimings = $"cache hit {clock.Elapsed.TotalMilliseconds:F1} ms"; return cachedResult; }
            var result = input.Render(token); token.ThrowIfCancellationRequested();
            lock (cacheGate) { cachedKey = key; cachedResult = result; LastTimings = $"{clock.Elapsed.TotalMilliseconds:F1} ms; plan {result.PlanningMilliseconds:F1}; surfaces {result.SurfaceMilliseconds:F1}; access {result.AccessConnected}/{result.AccessRequested}"; }
            return result;
        }
        private AgricultureInput Capture(MapData map, int radius)
        {
            if (!generateAgriculture) return null;
            if (densitySource == null || !densitySource.isReadable || densitySource.format != TextureFormat.R16)
                throw new InvalidOperationException("Agriculture requires a readable linear R16 density map.");
            if (districtSize < 120 || maximumFields < 1 || maximumFarms < 1 || coverage < 0 || coverage > 1 || maximumSlope < 0 || maximumSlope > 20 || fullDensity <= densityThreshold || fieldAreaScale < .1f || fieldAreaScale > 1 || connectionRadius < 0 || maximumAccessLength < 100 || beachClearance < 0)
                throw new InvalidOperationException("Invalid agriculture settings.");
            if (cachedSource != densitySource || cachedDensity == null || cachedHash != densitySource.imageContentsHash)
            { cachedDensity = densitySource.GetPixelData<ushort>(0).ToArray(); cachedSource = densitySource; cachedHash = densitySource.imageContentsHash; cachedResult = null; }
            var input = new AgricultureInput { Density = cachedDensity, SourceWidth = densitySource.width, SourceHeight = densitySource.height,
                SourcePoint = map.SourcePoint, Radius = radius, Height = map.TerrainHeightMeters, Sea = map.SeaLevelMeters,
                Coverage = coverage, District = districtSize, Slope = maximumSlope, FarmProbability = 1, Limit = maximumFields, FarmLimit = maximumFarms,
                Threshold = densityThreshold, FullDensity = fullDensity, Exponent = densityExponent, Seed = seed, Roads = map.Roads, AreaScale = fieldAreaScale, FieldBuildingsChance = fieldBuildingsChance, AccessWidth = GetComponent<RoadGenerator>() != null ? GetComponent<RoadGenerator>().MediumWidth : 3, ConnectionRadius = connectionRadius, AccessLength = maximumAccessLength, BeachClearance = beachClearance, BeachHeight = GetComponent<BiomeGenerator>() != null ? GetComponent<BiomeGenerator>().BeachHeight : 2.5f };
            input.Footprints = houseFootprints != null ? (Vector2[])houseFootprints.Clone() : null;
            for (int i = 0; i < 3; i++)
            { input.Pixels[i] = map.Heightmaps[i].GetPixelData<ushort>(0).ToArray(); input.Widths[i] = map.Heightmaps[i].width; input.Heights[i] = map.Heightmaps[i].height; input.Sizes[i] = map.Sizes[i]; }
            return input;
        }
    }
    internal sealed class AgricultureInput
    {
        internal ushort[] Density;
        internal int SourceWidth, SourceHeight, Radius, Limit, Seed, FarmLimit = 450;
        internal Vector2Int SourcePoint;
        internal readonly ushort[][] Pixels = new ushort[3][];
        internal readonly int[] Widths = new int[3], Heights = new int[3];
        internal readonly Vector2[] Sizes = new Vector2[3];
        internal float Height, Sea, Coverage, District, Slope, FarmProbability = 1;
        internal float Threshold = .04f, FullDensity = .6f, Exponent = 1.6f;
        internal RoadData Roads;
        internal Vector2[] Footprints;
        internal float FieldBuildingsChance = .18f, AccessWidth = 3;
        internal float AreaScale = .45f, ConnectionRadius = 6000, AccessLength = 1800, BeachClearance = 8, BeachHeight = 2.5f;
        private struct DistrictCandidate { internal int X, Z; internal Vector2 Center; internal float Weight, Rank; }
        internal ulong Fingerprint()
        {
            ulong h = 1469598103934665603UL;
            void Add(int value) { unchecked { h = (h ^ (uint)value) * 1099511628211UL; } }
            Add(SourcePoint.x); Add(SourcePoint.y); Add(Radius); Add(Seed); Add(Limit); Add(FarmLimit);
            foreach (float v in new[] { Height, Sea, Coverage, District, Slope, Threshold, FullDensity, Exponent, AreaScale, ConnectionRadius, AccessLength, BeachClearance, BeachHeight, FieldBuildingsChance, AccessWidth }) Add(BitConverter.SingleToInt32Bits(v));
            foreach (var s in Sizes) { Add(BitConverter.SingleToInt32Bits(s.x)); Add(BitConverter.SingleToInt32Bits(s.y)); }
            if (Footprints != null) foreach (var s in Footprints) { Add(BitConverter.SingleToInt32Bits(s.x)); Add(BitConverter.SingleToInt32Bits(s.y)); }
            foreach (ushort v in Density) Add(v);
            foreach (var pixels in Pixels) foreach (ushort v in pixels) Add(v);
            if (Roads != null) foreach (var r in Roads.Segments)
            { Add(BitConverter.SingleToInt32Bits(r.A.x)); Add(BitConverter.SingleToInt32Bits(r.A.y)); Add(BitConverter.SingleToInt32Bits(r.B.x)); Add(BitConverter.SingleToInt32Bits(r.B.y)); Add(BitConverter.SingleToInt32Bits(r.HalfWidth)); }
            if (Roads != null) { Add(BitConverter.SingleToInt32Bits(Roads.Shoulder)); Add(BitConverter.SingleToInt32Bits(Roads.Clearance)); }
            return h;
        }
        internal float Sample(Vector2 p)
        {
            int l = Mathf.Abs(p.x) < Sizes[0].x / 2 && Mathf.Abs(p.y) < Sizes[0].y / 2 ? 0 : Mathf.Abs(p.x) < Sizes[1].x / 2 && Mathf.Abs(p.y) < Sizes[1].y / 2 ? 1 : 2;
            return Bilinear(Pixels[l], Widths[l], Heights[l], p.x / Sizes[l].x + .5f, p.y / Sizes[l].y + .5f) * Height;
        }
        internal bool Suitable(Vector2 p)
        {
            float border = Mathf.Max(1, BeachClearance);
            if (Mathf.Abs(p.x) + border >= Sizes[2].x / 2 || Mathf.Abs(p.y) + border >= Sizes[2].y / 2) return false;
            float minimum = Sea + BeachHeight + .35f;
            if (Sample(p) <= minimum) return false;
            return Sample(p + Vector2.right * border) > minimum && Sample(p - Vector2.right * border) > minimum
                && Sample(p + Vector2.up * border) > minimum && Sample(p - Vector2.up * border) > minimum;
        }
        private float DensityAt(Vector2 p)
        {
            float x = SourcePoint.x + p.x / Sizes[2].x * (2 * Radius), y = SourcePoint.y + p.y / Sizes[2].y * (2 * Radius);
            if (x < 0 || y < 0 || x >= SourceWidth || y >= SourceHeight) return 0;
            float d = Bilinear(Density, SourceWidth, SourceHeight, x / (SourceWidth - 1), y / (SourceHeight - 1));
            return Mathf.Pow(Mathf.Clamp01((d - Threshold) / Mathf.Max(.001f, FullDensity - Threshold)), Exponent);
        }
        private static float Bilinear(ushort[] data, int w, int h, float u, float v)
        {
            float x = Mathf.Clamp01(u) * (w - 1), y = Mathf.Clamp01(v) * (h - 1); int ix = Mathf.Min((int)x, w - 2), iy = Mathf.Min((int)y, h - 2);
            return Mathf.Lerp(Mathf.Lerp(data[iy * w + ix], data[iy * w + ix + 1], x - ix), Mathf.Lerp(data[(iy + 1) * w + ix], data[(iy + 1) * w + ix + 1], x - ix), y - iy) / 65535f;
        }
        internal float Hash(int x, int z, int salt)
        {
            unchecked { uint h = (uint)(x * 73856093 ^ z * 19349663 ^ Seed * 83492791 ^ salt * 1640531513); h ^= h >> 16; h *= 0x7feb352d; h ^= h >> 15; h *= 0x846ca68b; h ^= h >> 16; return (h & 0xffffff) / 16777216f; }
        }
        internal AgricultureData Render(CancellationToken token)
        {
            var clock = Stopwatch.StartNew(); token.ThrowIfCancellationRequested();
            var fields = new List<FarmField>(); var farms = new List<Farmstead>(); var candidates = new List<DistrictCandidate>();
            Vector2 origin = new Vector2(SourcePoint.x * Sizes[2].x / (2 * Radius), SourcePoint.y * Sizes[2].y / (2 * Radius));
            int x0 = Mathf.FloorToInt((origin.x - Sizes[2].x / 2) / District), x1 = Mathf.CeilToInt((origin.x + Sizes[2].x / 2) / District);
            int z0 = Mathf.FloorToInt((origin.y - Sizes[2].y / 2) / District), z1 = Mathf.CeilToInt((origin.y + Sizes[2].y / 2) / District);
            float total = 0; int districtCount = 0;
            for (int z = z0; z < z1; z++) for (int x = x0; x < x1; x++)
            {
                token.ThrowIfCancellationRequested(); var center = new Vector2((x + .5f) * District, (z + .5f) * District) - origin;
                if (Mathf.Abs(center.x) > Sizes[2].x / 2 || Mathf.Abs(center.y) > Sizes[2].y / 2) continue;
                float weight = 0; for (int b = -1; b <= 1; b++) for (int a = -1; a <= 1; a++) weight += DensityAt(center + new Vector2(a, b) * District * .28f) / 9;
                total += weight; districtCount++;
                if (weight < .003f || Hash(x, z, 2) > weight * Coverage) continue;
                candidates.Add(new DistrictCandidate { X = x, Z = z, Center = center, Weight = weight, Rank = Hash(x, z, 3) / Mathf.Max(.01f, weight) });
            }
            float mean = total / Mathf.Max(1, districtCount), area = Sizes[2].x * Sizes[2].y / 400000000f;
            int fieldBudget = Mathf.Min(Limit, Mathf.RoundToInt(Limit * area * Mathf.Pow(mean, 1.25f)));
            int farmBudget = Mathf.Min(FarmLimit, Mathf.RoundToInt(FarmLimit * area * Mathf.Pow(mean, .85f)));
            if (mean < .02f) { fieldBudget = Mathf.Min(fieldBudget, 10); farmBudget = Mathf.Min(farmBudget, 2); }
            candidates.Sort((a, b) => a.Rank.CompareTo(b.Rank));
            int parcelSubdivisions=0;
            foreach (var district in candidates)
            {
                token.ThrowIfCancellationRequested(); if (fields.Count>=Limit || fields.Count-parcelSubdivisions>=fieldBudget) break;
                int x = district.X, z = district.Z; var center = district.Center + new Vector2(Hash(district.X,district.Z,301)-.5f,Hash(district.X,district.Z,302)-.5f)*District*.12f;
                float angle = (Hash(x, z, 1) - .5f) * 1.1f;
                var axis = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)); var side = new Vector2(-axis.y, axis.x);
                float scale = Mathf.Sqrt(AreaScale);
                float w = District * Mathf.Lerp(.22f, 1.03f, Mathf.Pow(Hash(x, z, 4), .72f)) * scale;
                float h = District * Mathf.Lerp(.2f, 1.0f, Mathf.Pow(Hash(x, z, 5), .72f)) * scale;
                var plots = new List<Rect> { new Rect(-w / 2, -h / 2, w, h) }; int count = 2 + (int)(Hash(x, z, 6) * 6);
                for (int split = 1; split < count; split++)
                {
                    int largest = 0; for (int i = 1; i < plots.Count; i++) if (plots[i].width * plots[i].height > plots[largest].width * plots[largest].height) largest = i;
                    var r = plots[largest]; plots.RemoveAt(largest); float ratio = Mathf.Lerp(.24f, .76f, Hash(x, z, 40 + split));
                    if (r.width > r.height) { plots.Add(new Rect(r.x, r.y, r.width * ratio, r.height)); plots.Add(new Rect(r.x + r.width * ratio, r.y, r.width * (1 - ratio), r.height)); }
                    else { plots.Add(new Rect(r.x, r.y, r.width, r.height * ratio)); plots.Add(new Rect(r.x, r.y + r.height * ratio, r.width, r.height * (1 - ratio))); }
                }
                var uncutPlots=new List<Rect>(plots);
                Farmstead planned;
                if (farms.Count>=farmBudget || Hash(x,z,200)>FarmProbability || !AgricultureSettlement.TryEmbedded(this,center,angle,plots,x,z,out planned)) planned=AgricultureSettlement.Plan(this,center,angle,w,h,x,z);
                planned.groupId=x*73856093^z*19349663;
                planned.gateHalfWidth=AccessWidth*.5f+(Roads?.Shoulder ?? 1)+1.75f;
                bool hasFarm=farms.Count<farmBudget && Hash(x,z,200)<=FarmProbability && planned.buildings!=null;
                if(hasFarm)
                {
                    foreach(var other in farms)if((other.center-planned.center).sqrMagnitude<Mathf.Pow(other.yardHalfSize.magnitude+planned.yardHalfSize.magnitude+5,2) && AgricultureSettlement.Overlaps(new FarmField{center=other.center,angle=other.angle,halfSize=other.yardHalfSize},planned)){hasFarm=false;break;}
                    if(hasFarm)foreach(var other in fields)if((other.center-planned.center).sqrMagnitude<Mathf.Pow(other.halfSize.magnitude+planned.yardHalfSize.magnitude+5,2)&&AgricultureSettlement.Overlaps(other,planned)){hasFarm=false;break;}
                }
                if(!hasFarm && planned.embedded)plots=uncutPlots;
                int accepted = 0, suitablePlots = 0;
                for (int i = 0; i < plots.Count && fields.Count < Limit; i++)
                {
                    var r = plots[i]; float gap = hasFarm && planned.embedded ? 2.5f : Mathf.Lerp(4, 7, Hash(x, z, 60 + i));
                    var f = new FarmField { center = center + axis * r.center.x + side * r.center.y, halfSize = new Vector2(r.width / 2 - gap, r.height / 2 - gap), angle = angle, variation = Hash(x, z, 90 + i), groupId = planned.groupId, crop = Hash(x, z, 110 + i) < .2f ? 0 : Hash(x, z, 120 + i) < .55f ? 1 : 2 };
                    if (Mathf.Min(f.halfSize.x, f.halfSize.y) < (hasFarm && planned.embedded ? 6 : 12) || Mathf.Abs(f.center.x) + f.halfSize.magnitude + 5 > Sizes[2].x / 2 || Mathf.Abs(f.center.y) + f.halfSize.magnitude + 5 > Sizes[2].y / 2) continue;
                    if (Roads != null && Roads.Blocks(f.center, f.halfSize.magnitude + 4)) continue;
                    float lo = float.MaxValue, hi = float.MinValue; bool valid = true;
                    for (int b = -1; b <= 1 && valid; b++) for (int a = -1; a <= 1; a++)
                    {
                        var p = f.Point(a * f.halfSize.x, b * f.halfSize.y); float height = Sample(p); lo = Mathf.Min(lo, height); hi = Mathf.Max(hi, height);
                        float gx = (Sample(p + Vector2.right * 4) - Sample(p - Vector2.right * 4)) / 8, gz = (Sample(p + Vector2.up * 4) - Sample(p - Vector2.up * 4)) / 8;
                        if (!Suitable(p) || gx * gx + gz * gz > Mathf.Pow(Mathf.Tan(Slope * Mathf.Deg2Rad), 2)) { valid = false; break; }
                    }
                    // Cover the interior too: a bay or sandy inlet may lie between corner samples.
                    if (valid)
                    {
                        int nx=Mathf.Max(1,Mathf.CeilToInt(f.halfSize.x*2/12)), nz=Mathf.Max(1,Mathf.CeilToInt(f.halfSize.y*2/12));
                        for(int iz=0;iz<=nz && valid;iz++)for(int ix=0;ix<=nx;ix++)
                            if(!Suitable(f.Point(Mathf.Lerp(-f.halfSize.x,f.halfSize.x,ix/(float)nx),Mathf.Lerp(-f.halfSize.y,f.halfSize.y,iz/(float)nz)))){valid=false;break;}
                    }
                    if (valid) suitablePlots++;
                    if (valid && hasFarm && AgricultureSettlement.Overlaps(f, planned)) valid = false;
                    if(valid)foreach(var other in farms)if((other.center-f.center).sqrMagnitude<Mathf.Pow(other.yardHalfSize.magnitude+f.halfSize.magnitude+5,2) && AgricultureSettlement.Overlaps(f,other)){valid=false;break;}
                    if (!valid) continue;
                    f.height = (lo + hi) / 2; if (f.crop != 0 && hi - lo < .65f) f.crop = 3;
                    fields.Add(f); accepted++;
                }
                if(hasFarm && planned.embedded)parcelSubdivisions+=Mathf.Max(0,accepted-uncutPlots.Count);
                if (suitablePlots > 0 && hasFarm) farms.Add(planned);
            }
            int requested=0,connected=0;
            var tracks = AgricultureAccess.Build(this, fields.ToArray(), farms.ToArray(), token, (r,c)=>{requested=r;connected=c;});

            double planning = clock.Elapsed.TotalMilliseconds;
            var result = new AgricultureData(fields.ToArray(), farms.ToArray(), null, new[] { 1024, 1024, 1024 }, tracks);
            result.CombinedRoads=AgricultureRoads.Combine(Roads,tracks,Sizes[2],AccessWidth,p=>Sample(p)>Sea+.2f,token);
            result.AccessRequested=requested;result.AccessConnected=connected;
            result.BuildSurfaces(Sizes, token); result.PlanningMilliseconds = planning; result.SurfaceMilliseconds = clock.Elapsed.TotalMilliseconds - planning;
            return result;
        }
    }
}
