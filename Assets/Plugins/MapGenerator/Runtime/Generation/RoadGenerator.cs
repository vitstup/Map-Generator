using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace TacticalMaps.Generation
{
    [DisallowMultipleComponent]
    internal sealed class RoadGenerator : MonoBehaviour
    {
        [SerializeField] private bool generateRoads = true;
        [SerializeField] private Texture2D roadSource;
        [SerializeField, Min(2)] private float largeWidth = 6;
        [SerializeField, Min(2)] private float mediumWidth = 3;
        [SerializeField, Range(.25f, 6)] private float shoulderWidth = 1;
        [SerializeField, Min(0)] private float objectClearance = 2;
        [Header("Routing")]
        [SerializeField, Range(1, 35)] private float maximumGrade = 12;
        [SerializeField, Range(1, 40)] private float maximumCrossSlope = 20;
        [SerializeField, Range(40, 1200)] private float maximumDeviation = 400;
        [SerializeField, Range(8, 40)] private float analysisSpacing = 16;
        [SerializeField, Range(0, 20)] private float slopeAvoidance = 8;
        [SerializeField] private int seed = 7431;
        [Header("Natural alignment")]
        [SerializeField, Range(0, 35)] private float largeWander = 10;
        [SerializeField, Range(0, 50)] private float mediumWander = 18;
        [SerializeField, Range(80, 500)] private float bendLength = 210;
        [SerializeField, Range(0, .2f)] private float widthVariation = .12f;
        private string cachedKey;
        private RoadData cachedData;
        internal string LastTimings { get; private set; }
        private Texture2D loadedSource;
        private Hash128 loadedHash;
        private byte[] source;
        private Task<Network> network;
        internal Texture2D Source => roadSource;
        internal byte[] SourceTypes { get { Load(); return source; } }
        internal bool Enabled => generateRoads;
        internal float MediumWidth => mediumWidth;
        internal byte ReadType(int x, int y)
        { Load(); return source != null && x >= 0 && y >= 0 && x < roadSource.width && y < roadSource.height ? source[y * roadSource.width + x] : (byte)0; }
        private void Load()
        {
            if (roadSource == null) { source = null; network = null; loadedSource = null; return; }
            // Unity hot reload can restore private arrays/references, but cannot restore Task.
            var hash = TextureContentRevision.Get(roadSource);
            if (loadedSource == roadSource && loadedHash == hash && source != null && network != null) return;
            if (!roadSource.isReadable) throw new InvalidOperationException("Road source must have Read/Write enabled.");
            var pixels = roadSource.GetPixels32(); source = new byte[pixels.Length];
            ulong fingerprint=1469598103934665603UL;
            for (int i = 0; i < source.Length; i++)
            {
                source[i] = pixels[i].r > 127 && pixels[i].r >= pixels[i].g ? (byte)1 : pixels[i].g > 127 ? (byte)2 : (byte)0;
                unchecked{fingerprint=(fingerprint^source[i])*1099511628211UL;}
            }
            loadedSource = roadSource;
            loadedHash = hash;
            cachedData=null;
            var snapshot = source; int w = roadSource.width, h = roadSource.height;
            string networkKey=hash+"_"+fingerprint.ToString("X16");
            network = Task.Run(() => Network.LoadOrTrace(snapshot, w, h,networkKey));
        }
        private Routing Capture(MapData map, int radius, int sourceWidth, int sourceHeight)
        {
            Load();
            if (roadSource == null) throw new InvalidOperationException("Assign the road source, or disable Generate Roads.");
            if (roadSource.width != sourceWidth || roadSource.height != sourceHeight) throw new InvalidOperationException("Road, height and land maps must have identical dimensions.");
            foreach (float v in new[] { largeWidth, mediumWidth, shoulderWidth, objectClearance, maximumGrade, maximumCrossSlope, maximumDeviation, analysisSpacing, slopeAvoidance,largeWander,mediumWander,bendLength,widthVariation })
                if (float.IsNaN(v) || float.IsInfinity(v)) throw new InvalidOperationException("Road settings must be finite.");
            if (largeWidth < 2 || mediumWidth < 2 || largeWidth > 40 || mediumWidth > 40 || shoulderWidth < .25f || shoulderWidth > 6 || objectClearance < 0 || maximumGrade < 1 || maximumGrade > 35 || maximumCrossSlope < 1 || maximumCrossSlope > 40 || maximumDeviation < 40 || maximumDeviation > 1200 || analysisSpacing < 8 || analysisSpacing > 40 || slopeAvoidance < 0 || largeWander<0 || largeWander>35 || mediumWander<0 || mediumWander>50 || bendLength<80 || bendLength>500 || widthVariation<0 || widthVariation>.2f)
                throw new InvalidOperationException("Invalid road widths or routing settings.");
            return new Routing(map, source, sourceWidth, sourceHeight, radius, largeWidth, mediumWidth, shoulderWidth, objectClearance,
                maximumGrade, maximumCrossSlope, maximumDeviation, analysisSpacing, slopeAvoidance, seed,largeWander,mediumWander,bendLength,widthVariation);
        }
        internal RoadData Generate(MapData map, int radius, int width, int height, CancellationToken token)
        {
            if (!generateRoads) return null;
            var field = Capture(map, radius, width, height);
            token.ThrowIfCancellationRequested();
            string key=CacheKey(map,field);
            if(cachedData!=null&&cachedKey==key){LastTimings="road result cache hit";return cachedData;}
            var result=field.Generate(network.GetAwaiter().GetResult(),token);
            cachedKey=key;cachedData=result;LastTimings=result.Timings;return result;
        }
        internal async Task<RoadData> GenerateAsync(MapData map, int radius, int width, int height, CancellationToken token)
        {
            if (!generateRoads) return null;
            token.ThrowIfCancellationRequested();var watch=System.Diagnostics.Stopwatch.StartNew();
            var field = Capture(map, radius, width, height);double capture=watch.Elapsed.TotalMilliseconds;
            string key=CacheKey(map,field);
            if(cachedData!=null&&cachedKey==key){LastTimings=$"road result cache hit; capture {capture:F0} ms";return cachedData;}
            var topology = await network;
            token.ThrowIfCancellationRequested();
            double topologyTime=watch.Elapsed.TotalMilliseconds-capture;
            var result=await Task.Run(() => field.Generate(topology, token), token);
            token.ThrowIfCancellationRequested();result.CaptureMilliseconds=capture;result.TopologyMilliseconds=topologyTime;
            cachedKey=key;cachedData=result;LastTimings=result.Timings;return result;
        }
        private string CacheKey(MapData map,Routing field)
        {
            var culture=System.Globalization.CultureInfo.InvariantCulture;
            string identity=map.SourcePoint.x+":"+map.SourcePoint.y+":"+map.TerrainHeightMeters.ToString("R",culture)+":"+map.SeaLevelMeters.ToString("R",culture);
            foreach(var size in map.Sizes)identity+=":"+size.x.ToString("R",culture)+":"+size.y.ToString("R",culture);
            return loadedHash+JsonUtility.ToJson(this)+identity+":"+field.Fingerprint;
        }

        // Topology is derived from the full source, so cropping never creates artificial junctions.
        private sealed class Network
        {
            internal readonly List<int[]> Paths = new List<int[]>();
            internal readonly List<byte> Types = new List<byte>();
            internal readonly List<Rect> Bounds = new List<Rect>();
            private static readonly int[] DX = { -1, 0, 1, -1, 1, -1, 0, 1 }, DY = { -1, -1, -1, 0, 0, 1, 1, 1 };
            internal static Network LoadOrTrace(byte[] types,int w,int h,string key)
            {
#if UNITY_EDITOR
                // Derived data belongs in Library, not in source assets. An algorithm version invalidates old results.
                string folder=System.IO.Path.Combine("Library","TacticalMaps","RoadNetworks");
                string path=System.IO.Path.Combine(folder,"v4_"+w+"x"+h+"_"+key+".bin");
                try
                {
                    if(System.IO.File.Exists(path))
                    {
                        using(var reader=new System.IO.BinaryReader(System.IO.File.OpenRead(path)))
                        {
                            if(reader.ReadInt32()!=0x524F4144||reader.ReadInt32()!=4||reader.ReadInt32()!=w||reader.ReadInt32()!=h)throw new System.IO.InvalidDataException();
                            int count=reader.ReadInt32(),total=0;if(count<0||count>1000000)throw new System.IO.InvalidDataException();var cached=new Network();
                            for(int i=0;i<count;i++)
                            {
                                byte type=reader.ReadByte();int n=reader.ReadInt32();total+=n;
                                if(type<1||type>2||n<2||n>w*h||total>4000000)throw new System.IO.InvalidDataException();
                                var points=new int[n];for(int j=0;j<n;j++){points[j]=reader.ReadInt32();if(points[j]<0||points[j]>=w*h)throw new System.IO.InvalidDataException();}
                                cached.Paths.Add(points);cached.Types.Add(type);cached.Bounds.Add(Rect.MinMaxRect(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle()));
                            }
                            return cached;
                        }
                    }
                }
                catch(Exception error) when(error is System.IO.IOException||error is System.IO.InvalidDataException||error is UnauthorizedAccessException)
                {
                    try{if(System.IO.File.Exists(path))System.IO.File.Delete(path);}catch(System.IO.IOException){}catch(UnauthorizedAccessException){}
                }
#endif
                var result=Trace(types,w,h);
#if UNITY_EDITOR
                string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                try
                {
                    System.IO.Directory.CreateDirectory(folder);
                    using(var writer=new System.IO.BinaryWriter(System.IO.File.Create(temporary)))
                    {
                        writer.Write(0x524F4144);writer.Write(4);writer.Write(w);writer.Write(h);writer.Write(result.Paths.Count);
                        for(int i=0;i<result.Paths.Count;i++)
                        {
                            writer.Write(result.Types[i]);writer.Write(result.Paths[i].Length);foreach(int point in result.Paths[i])writer.Write(point);
                            var bounds=result.Bounds[i];writer.Write(bounds.xMin);writer.Write(bounds.yMin);writer.Write(bounds.xMax);writer.Write(bounds.yMax);
                        }
                    }
                    // Another generation may have populated the same key while this one was working.
                    if(!System.IO.File.Exists(path))System.IO.File.Move(temporary,path);
                }
                catch(System.IO.IOException) { } catch(UnauthorizedAccessException) { }
                finally{try{if(System.IO.File.Exists(temporary))System.IO.File.Delete(temporary);}catch(System.IO.IOException){}catch(UnauthorizedAccessException){}}
#endif
                return result;
            }
            internal static Network Trace(byte[] types, int w, int h)
            {
                var mask = (byte[])types.Clone(); var occupied = new List<int>();
                for (int i = 0; i < mask.Length; i++) if (mask[i] != 0) occupied.Add(i);
                var remove = new List<int>(); bool changed;
                do
                {
                    changed = false;
                    for (int pass = 0; pass < 2; pass++)
                    {
                        foreach (int p in occupied)
                        {
                            if (mask[p] == 0) continue; int x = p % w, y = p / w;
                            if (x == 0 || y == 0 || x == w - 1 || y == h - 1) continue;
                            int a = mask[p + w] > 0 ? 1 : 0, b = mask[p + w + 1] > 0 ? 1 : 0, c = mask[p + 1] > 0 ? 1 : 0, d = mask[p - w + 1] > 0 ? 1 : 0;
                            int e = mask[p - w] > 0 ? 1 : 0, f = mask[p - w - 1] > 0 ? 1 : 0, g = mask[p - 1] > 0 ? 1 : 0, j = mask[p + w - 1] > 0 ? 1 : 0;
                            int count = a + b + c + d + e + f + g + j;
                            int transitions = (1-a)*b+(1-b)*c+(1-c)*d+(1-d)*e+(1-e)*f+(1-f)*g+(1-g)*j+(1-j)*a;
                            if (count >= 2 && count <= 6 && transitions == 1 && (pass == 0 ? a*c*e == 0 && c*e*g == 0 : a*c*g == 0 && a*e*g == 0)) remove.Add(p);
                        }
                        foreach (int p in remove) mask[p] = 0;
                        changed |= remove.Count > 0; remove.Clear();
                    }
                } while (changed);
                var result = new Network(); var visited = new HashSet<long>();
                var neighbors = new List<int>(8);
                // Junctions/endpoints first, closed loops second.
                for (int pass = 0; pass < 2; pass++) foreach (int p in occupied)
                {
                    if (mask[p] == 0) continue;
                    Neighbors(mask, p, w, h, neighbors); int degree = neighbors.Count;
                    if (pass == 0 && degree == 2) continue;
                    var starts = neighbors.ToArray();
                    foreach (int next in starts)
                    {
                        if (visited.Contains(Key(p, next))) continue;
                        var path = new List<int> { p }; int previous = p, current = next;
                        byte type = types[next];
                        for (int guard = 0; guard < occupied.Count; guard++)
                        {
                            visited.Add(Key(previous, current)); path.Add(current);
                            Neighbors(mask, current, w, h, neighbors);
                            if (current == p || neighbors.Count != 2 || types[current] != type) break;
                            int following = neighbors[0] == previous ? neighbors[1] : neighbors[0];
                            if (visited.Contains(Key(current, following))) break;
                            previous = current; current = following;
                        }
                        if (path.Count > 1)
                        {
                            int minX=w,minY=h,maxX=0,maxY=0;
                            foreach(int pixel in path){minX=Math.Min(minX,pixel%w);maxX=Math.Max(maxX,pixel%w);minY=Math.Min(minY,pixel/w);maxY=Math.Max(maxY,pixel/w);}
                            result.Paths.Add(path.ToArray());result.Types.Add(type);result.Bounds.Add(Rect.MinMaxRect(minX,minY,maxX,maxY));
                        }
                    }
                }
                return result;
            }
            private static long Key(int a, int b) => ((long)Math.Min(a,b) << 32) | (uint)Math.Max(a,b);
            private static void Neighbors(byte[] m, int p, int w, int h, List<int> list)
            {
                list.Clear(); int x = p % w, y = p / w;
                for (int i = 0; i < 8; i++)
                {
                    int nx=x+DX[i], ny=y+DY[i];
                    if(nx<0||ny<0||nx>=w||ny>=h||m[ny*w+nx]==0) continue;
                    // A diagonal touching a filled orthogonal neighbor is not another branch.
                    if(DX[i]!=0 && DY[i]!=0 && (m[y*w+nx]!=0||m[ny*w+x]!=0)) continue;
                    list.Add(ny*w+nx);
                }
            }
        }

        private sealed class Routing
        {
            private readonly ushort[][] heights = new ushort[3][];
            private readonly int[] resolutions = new int[3];
            private readonly Vector2[] sizes = new Vector2[3];
            private readonly byte[] source;
            private readonly int width, height, radius, seed;
            private readonly Vector2 center, metres;
            private readonly float vertical, sea, large, medium, shoulder, clearance, grade, cross, deviation, spacing, slopeCost;
            private readonly float bigWander,smallWander,bendSpan,widthChange;
            internal readonly ulong Fingerprint;
            private readonly Dictionary<Vector2, Vector2> anchors = new Dictionary<Vector2, Vector2>();
            private readonly Dictionary<Vector2Int,Relief> analysis=new Dictionary<Vector2Int,Relief>();
            private readonly struct Relief { internal readonly float Height;internal readonly Vector2 Gradient;internal Relief(float h,Vector2 g){Height=h;Gradient=g;} }
            internal Routing(MapData map, byte[] src, int w, int h, int r, float big, float small, float edge, float clear, float maxGrade, float maxCross, float dev, float step, float avoid, int random,float wanderBig,float wanderSmall,float span,float variation)
            {
                source=src; width=w; height=h; radius=r; center=map.SourcePoint; vertical=map.TerrainHeightMeters; sea=map.SeaLevelMeters;
                for(int i=0;i<3;i++){heights[i]=map.Heightmaps[i].GetPixelData<ushort>(0).ToArray();resolutions[i]=map.Heightmaps[i].width;sizes[i]=map.Sizes[i];}
                metres=sizes[2]/(2*r); large=big;medium=small;shoulder=edge;clearance=clear;grade=Mathf.Tan(maxGrade*Mathf.Deg2Rad);cross=Mathf.Tan(maxCross*Mathf.Deg2Rad);deviation=dev;spacing=step;slopeCost=avoid;seed=random;
                bigWander=wanderBig;smallWander=wanderSmall;bendSpan=span;widthChange=variation;
                ulong hash=1469598103934665603UL;
                unchecked{foreach(var pixels in heights)foreach(ushort value in pixels)hash=(hash^value)*1099511628211UL;}
                Fingerprint=hash^(ulong)map.SourcePoint.GetHashCode()^(ulong)map.Sizes[0].GetHashCode()^(ulong)map.Sizes[1].GetHashCode()^(ulong)map.Sizes[2].GetHashCode()^(ulong)vertical.GetHashCode()^(ulong)sea.GetHashCode()^(ulong)r;
            }
            private Vector2 World(int pixel) => Vector2.Scale(new Vector2(pixel%width,pixel/width)-center,metres);
            private float Height(Vector2 p)
            {
                int level=2;for(int i=0;i<2;i++)if(Mathf.Abs(p.x)<=sizes[i].x/2&&Mathf.Abs(p.y)<=sizes[i].y/2){level=i;break;}
                int n=resolutions[level];float x=Mathf.Clamp01(p.x/sizes[level].x+.5f)*(n-1),y=Mathf.Clamp01(p.y/sizes[level].y+.5f)*(n-1);
                int ix=Mathf.Min((int)x,n-2),iy=Mathf.Min((int)y,n-2),k=iy*n+ix;var a=heights[level];
                return Mathf.Lerp(Mathf.Lerp(a[k],a[k+1],x-ix),Mathf.Lerp(a[k+n],a[k+n+1],x-ix),y-iy)/65535f*vertical;
            }
            private bool Dry(Vector2 p)
            {
                if(Mathf.Abs(p.x)>sizes[2].x/2||Mathf.Abs(p.y)>sizes[2].y/2)return false;
                return Height(p)>sea+.15f;
            }
            private Vector2 Gradient(Vector2 p) => new Vector2(Height(p+Vector2.right*4)-Height(p-Vector2.right*4),Height(p+Vector2.up*4)-Height(p-Vector2.up*4))/8;
            private Relief Analyze(Vector2 p)
            {
                float cell=spacing*.5f;var key=new Vector2Int(Mathf.RoundToInt(p.x/cell),Mathf.RoundToInt(p.y/cell));
                if(!analysis.TryGetValue(key,out var value)){var q=new Vector2(key.x,key.y)*cell;value=new Relief(Height(q),Gradient(q));analysis.Add(key,value);}return value;
            }
            private bool SearchPassage(Vector2 a,Vector2 b,float half)
            {
                var delta=b-a;float length=delta.magnitude;if(length<.01f)return Dry(a);var tangent=delta/length;var side=new Vector2(-tangent.y,tangent.x);
                var p=Analyze(a);var q=Analyze(b);var middle=Analyze((a+b)*.5f);
                if(Mathf.Min(p.Height,Mathf.Min(q.Height,middle.Height))<=sea+.3f||Mathf.Abs(q.Height-p.Height)>grade*length)return false;
                return Mathf.Abs(Vector2.Dot(middle.Gradient,side))<=cross&&Analyze((a+b)*.5f+side*half).Height>sea+.3f&&Analyze((a+b)*.5f-side*half).Height>sea+.3f;
            }
            private bool Passage(Vector2 a,Vector2 b,float half)
            {
                Vector2 delta=b-a;float len=delta.magnitude;if(len<.01f)return Dry(a);
                var direction=delta/len;var side=new Vector2(-direction.y,direction.x);int count=Mathf.CeilToInt(len/2);
                float previous=Height(a);
                for(int i=0;i<=count;i++)
                {
                    var p=Vector2.Lerp(a,b,(float)i/count);if(!Dry(p)||!Dry(p+side*half)||!Dry(p-side*half))return false;
                    float current=Height(p);if(i>0&&Mathf.Abs(current-previous)>grade*len/count+.03f)return false;
                    if(Mathf.Abs(Height(p+side*half)-Height(p-side*half))>cross*2*half+.03f)return false;
                    previous=current;
                }
                return true;
            }
            private Vector2 Anchor(Vector2 p)
            {
                if(anchors.TryGetValue(p,out var value))return value;
                value=p;float best=float.PositiveInfinity;
                for(int y=-3;y<=3;y++)for(int x=-3;x<=3;x++)
                {var q=p+new Vector2(x,y)*spacing;if(!Dry(q))continue;float cost=(q-p).magnitude+Gradient(q).sqrMagnitude*spacing*20;if(cost<best){best=cost;value=q;}}
                anchors.Add(p,value);return value;
            }
            internal RoadData Generate(Network network,CancellationToken token)
            {
                var clock=System.Diagnostics.Stopwatch.StartNew();var segments=new List<RoadSegment>();int rejected=0;
                for(int pathIndex=0;pathIndex<network.Paths.Count;pathIndex++)
                {
                    token.ThrowIfCancellationRequested();var path=network.Paths[pathIndex];byte type=network.Types[pathIndex];float roadWidth=type==1?large:medium;
                    var sourceBounds=network.Bounds[pathIndex];float padding=deviation/Mathf.Min(metres.x,metres.y)+2;
                    if(sourceBounds.xMax<center.x-radius-padding||sourceBounds.xMin>center.x+radius+padding||sourceBounds.yMax<center.y-radius-padding||sourceBounds.yMin>center.y+radius+padding)continue;
                    // Simplify only the reference corridor; junction endpoints remain shared.
                    var guide=new List<Vector2>();float distance=0;Vector2 previous=World(path[0]);guide.Add(previous);
                    for(int i=1;i<path.Length;i++){var p=World(path[i]);distance+=Vector2.Distance(p,previous);previous=p;if(distance>=120){guide.Add(p);distance=0;}}
                    var end=World(path[path.Length-1]);if(guide[guide.Count-1]!=end)guide.Add(end);
                    // Bound A* working sets, retaining the original bends within each corridor.
                    int begin=0;
                    while(begin<guide.Count-1)
                    {
                        int finish=begin+1;float length=Vector2.Distance(guide[begin],guide[finish]);
                        while(finish<guide.Count-1&&length<1800){length+=Vector2.Distance(guide[finish],guide[finish+1]);finish++;}
                        var part=guide.GetRange(begin,finish-begin+1);begin=finish;
                        Vector2 lo=part[0],hi=part[0];foreach(var p in part){lo=Vector2.Min(lo,p);hi=Vector2.Max(hi,p);}
                        if(hi.x< -sizes[2].x/2-deviation||hi.y< -sizes[2].y/2-deviation||lo.x>sizes[2].x/2+deviation||lo.y>sizes[2].y/2+deviation)continue;
                        // Outside context defines direction, but routing is clipped to the available tactical relief.
                        var inside=new List<Vector2>();
                        foreach(var p in part)inside.Add(new Vector2(Mathf.Clamp(p.x,-sizes[2].x/2+2,sizes[2].x/2-2),Mathf.Clamp(p.y,-sizes[2].y/2+2,sizes[2].y/2-2)));
                        foreach (var dryGuide in DryGuides(inside))
                        {
                        var route=Find(dryGuide,roadWidth/2+shoulder,token);
                        if(route==null){rejected++;continue;}
                        // Greedy validated smoothing never cuts across water or a steep slope.
                        int first=0;var simplified=new List<Vector2>{route[0]};
                        while(first<route.Count-1)
                        {
                            int last=Mathf.Min(first+12,route.Count-1);
                            while(last>first+1&&!Passage(route[first],route[last],roadWidth/2+shoulder))last--;
                            if(Passage(route[first],route[last],roadWidth/2+shoulder))simplified.Add(route[last]);
                            first=last;
                        }
                        AddNatural(segments,simplified,roadWidth,type,token);
                        }
                    }
                }
                double routingTime=clock.Elapsed.TotalMilliseconds;
                var result=new RoadData(segments.ToArray(),sizes[2],shoulder,clearance){RejectedSections=rejected,RoutingMilliseconds=routingTime};
                var keys=new List<int>(result.Bins.Keys);keys.Sort();
                if(keys.Count>2048)throw new InvalidOperationException("Road coverage exceeds 2048 sparse tiles. Reduce the selected area or road density.");
                var tiles=new Color32[keys.Count][];
                System.Threading.Tasks.Parallel.For(0,keys.Count,new ParallelOptions{CancellationToken=token,MaxDegreeOfParallelism=Math.Max(1,Math.Min(4,Environment.ProcessorCount-1))},i=>tiles[i]=RoadRasterizer.Tile(result,keys[i],Dry,token));
                result.TileKeys.AddRange(keys);result.Tiles.AddRange(tiles);
                result.RasterMilliseconds=clock.Elapsed.TotalMilliseconds-routingTime;
                return result;
            }
            private List<List<Vector2>> DryGuides(List<Vector2> guide)
            {
                var result=new List<List<Vector2>>();List<Vector2> run=null;
                for(int i=1;i<guide.Count;i++)
                {
                    int count=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(guide[i-1],guide[i])/4));
                    for(int k=i==1?0:1;k<=count;k++)
                    {
                        var p=Vector2.Lerp(guide[i-1],guide[i],(float)k/count);
                        if(!Dry(p)){if(run!=null&&run.Count>1)result.Add(run);run=null;continue;}
                        if(run==null)run=new List<Vector2>();
                        // Keep a compact corridor while retaining the last dry point at each shore.
                        if(run.Count<2||Vector2.Distance(run[run.Count-2],p)>100)run.Add(p);else run[run.Count-1]=p;
                    }
                }
                if(run!=null&&run.Count>1)result.Add(run);
                return result;
            }
            private static byte Encode(float d)=>(byte)Mathf.RoundToInt(Mathf.Clamp01((d+16)/32)*255);
            private void AddNatural(List<RoadSegment> output,List<Vector2> points,float roadWidth,byte type,CancellationToken token)
            {
                if(points.Count<2)return;
                var controls=new List<Vector2>{points[0]};
                for(int i=1;i<points.Count-1;i++)if(RoadData.Distance(points[i],controls[controls.Count-1],points[i+1])>.8f)controls.Add(points[i]);
                controls.Add(points[points.Count-1]);
                float total=0;for(int i=1;i<controls.Count;i++)total+=Vector2.Distance(controls[i-1],controls[i]);
                float amplitude=Mathf.Min(type==1?bigWander:smallWander,deviation*.15f);float half=roadWidth*(1+widthChange)/2+shoulder+1.2f;
                for(int attempt=0;attempt<4;attempt++)
                {
                    token.ThrowIfCancellationRequested();var curve=new List<Vector2>{controls[0]};float travelled=0;bool safe=true;
                    for(int i=0;i<controls.Count-1;i++)
                    {
                        var p0=controls[Mathf.Max(0,i-1)];var p1=controls[i];var p2=controls[i+1];var p3=controls[Mathf.Min(controls.Count-1,i+2)];
                        float length=Vector2.Distance(p1,p2);int steps=Mathf.Max(1,Mathf.CeilToInt(length/24));
                        // Uneven control spacing must not pull a short final span past its junction.
                        var m1=(p2-p0).normalized*Mathf.Min(length,i==0?length:Vector2.Distance(p0,p1))*.8f;
                        var m2=(p3-p1).normalized*Mathf.Min(length,i==controls.Count-2?length:Vector2.Distance(p2,p3))*.8f;
                        if(Vector2.Dot(m1,p2-p1)<0)m1=(p2-p1)*.5f;
                        if(Vector2.Dot(m2,p2-p1)<0)m2=(p2-p1)*.5f;
                        for(int j=1;j<=steps;j++)
                        {
                            float t=(float)j/steps,t2=t*t,t3=t2*t;
                            var p=(2*t3-3*t2+1)*p1+(t3-2*t2+t)*m1+(-2*t3+3*t2)*p2+(t3-t2)*m2;
                            if(attempt==3)p=Vector2.Lerp(p1,p2,t);
                            var tangent=(p2-p1).normalized;float along=travelled+length*t;
                            float envelope=Mathf.SmoothStep(0,1,Mathf.Min(along,total-along)/80);
                            var global=p+Vector2.Scale(center,metres);
                            float wander=(Noise(global/bendSpan+Vector2.one*(seed*.017f))-.5f)*2;
                            p+=new Vector2(-tangent.y,tangent.x)*wander*amplitude*envelope*(attempt==3?0:Mathf.Pow(.5f,attempt));
                            var previous=curve[curve.Count-1];if(!Passage(previous,p,half)){safe=false;break;}curve.Add(p);
                        }
                        if(!safe)break;travelled+=length;
                    }
                    if(!safe)continue;
                    for(int i=1;i<curve.Count;i++)
                    {
                        if((curve[i]-curve[i-1]).sqrMagnitude<.01f)continue;
                        var offset=Vector2.Scale(center,metres);
                        float a=roadWidth*(1+(Noise((curve[i-1]+offset)/37+Vector2.one*seed)-.5f)*2*widthChange);
                        float b=roadWidth*(1+(Noise((curve[i]+offset)/37+Vector2.one*seed)-.5f)*2*widthChange);
                        output.Add(new RoadSegment(curve[i-1],curve[i],a,b,type));
                    }
                    return;
                }
                // The original validated alignment remains usable if the wider decorative margin cannot fit.
                for(int i=1;i<points.Count;i++)if(Passage(points[i-1],points[i],roadWidth/2+shoulder))output.Add(new RoadSegment(points[i-1],points[i],roadWidth,type));
            }
            private static float Noise(Vector2 p)
            {
                int x=Mathf.FloorToInt(p.x),y=Mathf.FloorToInt(p.y);float u=p.x-x,v=p.y-y;u=u*u*(3-2*u);v=v*v*(3-2*v);
                return Mathf.Lerp(Mathf.Lerp(Hash(x,y),Hash(x+1,y),u),Mathf.Lerp(Hash(x,y+1),Hash(x+1,y+1),u),v);
            }
            private static float Hash(int x,int y){unchecked{uint h=(uint)(x*374761393+y*668265263);h=(h^(h>>13))*1274126177;return (h^(h>>16))/(float)uint.MaxValue;}}
            private List<Vector2> Find(List<Vector2> guide,float half,CancellationToken token,bool precise=false,float refinementReach=0)
            {
                float step=precise?spacing*.5f:spacing*2;
                Vector2 start=precise?guide[0]:Anchor(guide[0]),end=precise?guide[guide.Count-1]:Anchor(guide[guide.Count-1]);
                if(!Dry(start)||!Dry(end))return null;
                if(Vector2.Distance(start,end)<.01f)return new List<Vector2>{start};
                Vector2 lo=start,hi=start;foreach(var p in guide){lo=Vector2.Min(lo,p);hi=Vector2.Max(hi,p);}
                float reach=precise?(refinementReach>0?refinementReach:spacing*4):deviation;lo-=Vector2.one*reach;hi+=Vector2.one*reach;
                lo=Vector2.Max(lo,-sizes[2]/2+Vector2.one);hi=Vector2.Min(hi,sizes[2]/2-Vector2.one);
                lo=new Vector2(Mathf.Floor(lo.x/step)*step,Mathf.Floor(lo.y/step)*step);
                int nx=Mathf.CeilToInt((hi.x-lo.x)/step)+1,ny=Mathf.CeilToInt((hi.y-lo.y)/step)+1;
                if(nx<1||ny<1||(long)nx*ny>200000)return null;
                int count=nx*ny;var cost=new float[count];var from=new int[count];var closed=new bool[count];var valid=new byte[count];var gradients=new Vector2[count];
                var referenceDistances=new float[count];
                for(int i=0;i<count;i++){cost[i]=float.PositiveInfinity;from[i]=-1;}
                Func<Vector2,int> index=p=>Mathf.Clamp(Mathf.RoundToInt((p.y-lo.y)/step),0,ny-1)*nx+Mathf.Clamp(Mathf.RoundToInt((p.x-lo.x)/step),0,nx-1);
                int a=index(start),b=index(end);
                Func<int,Vector2> position=i=>i==a?start:i==b?end:lo+new Vector2(i%nx,i/nx)*step;
                var heap=new MinHeap();cost[a]=0;heap.Push(a,Vector2.Distance(start,end));int iterations=0;
                while(heap.Count>0)
                {
                    if((iterations++&127)==0)token.ThrowIfCancellationRequested();int current=heap.Pop();if(closed[current])continue;closed[current]=true;
                    if(current==b)
                    {
                        var path=new List<Vector2>();for(int i=b;i>=0;i=from[i])path.Add(position(i));path.Reverse();
                        if(!precise)for(int i=1;i<path.Count;i++)if(!Passage(path[i-1],path[i],half))return Find(path,half,token,true);
                        return path;
                    }
                    var p=position(current);
                    for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                    {
                        if(dx==0&&dy==0)continue;int x=current%nx+dx,y=current/nx+dy;if(x<0||y<0||x>=nx||y>=ny)continue;
                        int next=y*nx+x;if(closed[next])continue;var q=position(next);
                        if(valid[next]==0)
                        {
                            float dist=float.PositiveInfinity;for(int i=1;i<guide.Count;i++)dist=Mathf.Min(dist,RoadData.Distance(q,guide[i-1],guide[i]));
                            var relief=Analyze(q);valid[next]=Mathf.Abs(q.x)<sizes[2].x/2&&Mathf.Abs(q.y)<sizes[2].y/2&&relief.Height>sea+.15f&&dist<=reach?(byte)1:(byte)2;gradients[next]=relief.Gradient;referenceDistances[next]=dist;
                        }
                        if(valid[next]==2)continue;
                        var delta=q-p;float len=delta.magnitude;if(len<.001f)continue;var direction=delta/len;var g=gradients[next];
                        float longitudinal=Mathf.Abs(Vector2.Dot(g,direction)),side=Mathf.Abs(Vector2.Dot(g,new Vector2(-direction.y,direction.x)));
                        if(longitudinal>grade||side>cross||!(precise?Passage(p,q,half):SearchPassage(p,q,half)))continue;
                        float reference=referenceDistances[next];
                        float bend=from[current]>=0?1-Vector2.Dot((p-position(from[current])).normalized,direction):0;
                        uint hash=unchecked((uint)((int)(q.x+center.x*metres.x)*73856093^(int)(q.y+center.y*metres.y)*19349663^seed));
                        float slopePenalty=Mathf.Pow(longitudinal/grade,2)+Mathf.Pow(side/cross,2);
                        float candidate=cost[current]+len*(1+slopeCost*slopePenalty+.45f*Mathf.Pow(reference/deviation,2)+bend*.3f+(hash%997)*.000001f);
                        if(candidate>=cost[next])continue;cost[next]=candidate;from[next]=current;heap.Push(next,candidate+Vector2.Distance(q,end));
                    }
                }
                if(precise)return null;
                var snapped=new List<Vector2>(guide);snapped[0]=start;snapped[snapped.Count-1]=end;
                return Find(snapped,half,token,true,deviation);
            }
            private sealed class MinHeap
            {
                private readonly List<(int id,float cost)> items=new List<(int,float)>();internal int Count=>items.Count;
                internal void Push(int id,float cost){items.Add((id,cost));int i=items.Count-1;while(i>0){int parent=(i-1)/2;if(items[parent].cost<=cost)break;items[i]=items[parent];i=parent;}items[i]=(id,cost);}
                internal int Pop(){int result=items[0].id;var last=items[items.Count-1];items.RemoveAt(items.Count-1);if(items.Count==0)return result;int i=0;while(i*2+1<items.Count){int child=i*2+1;if(child+1<items.Count&&items[child+1].cost<items[child].cost)child++;if(items[child].cost>=last.cost)break;items[i]=items[child];i=child;}items[i]=last;return result;}
            }
        }
    }
}
