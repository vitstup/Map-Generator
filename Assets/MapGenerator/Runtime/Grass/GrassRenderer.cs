using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace TacticalMaps.Grass
{
    // One bounded residency pool per camera. No CPU placement arrays or visibility readback.
    internal sealed class GrassRenderer : IDisposable
    {
        private readonly GrassLibrary library;
        private readonly GrassMap map;
        private readonly Dictionary<Vector2Int, List<TacticalMaps.Generation.FarmBuilding>> buildingChunks = new Dictionary<Vector2Int, List<TacticalMaps.Generation.FarmBuilding>>();
        private readonly Vector4[] buildingRects = new Vector4[64], buildingAxes = new Vector4[64];
        private readonly ComputeShader compute;
        private readonly GraphicsBuffer instances, activeSlots, births;
        private readonly GraphicsBuffer prepared, frameCounts, frameDispatch;
        private readonly int frameKernel, frameArgsKernel;
        private readonly CommandBuffer work = new CommandBuffer {name="Grass GPU work"};
        private static readonly ProfilingSampler prepareSampler=new ProfilingSampler("Grass.PreparePlants");
        private static readonly ProfilingSampler cullSampler=new ProfilingSampler("Grass.CullPlants");
        private readonly float[] birthUpload = new float[1];
        private readonly GraphicsBuffer[] visible = new GraphicsBuffer[4], arguments = new GraphicsBuffer[4];
        private readonly MaterialPropertyBlock[] properties = { new MaterialPropertyBlock(), new MaterialPropertyBlock(), new MaterialPropertyBlock(), new MaterialPropertyBlock() };
        private readonly Mesh[] renderMeshes = new Mesh[4];
        private readonly GraphicsBuffer[][] drawInputs=new GraphicsBuffer[4][];
        private readonly Material preparedMaterial, headsMaterial, sparseMaterial, cardsMaterial;
        private readonly Dictionary<Vector2Int, int> slots = new Dictionary<Vector2Int, int>();
        private readonly List<Vector2Int> wanted = new List<Vector2Int>();
        private readonly HashSet<Vector2Int> wantedSet = new HashSet<Vector2Int>();
        private readonly Queue<int> free = new Queue<int>();
        private readonly int[] active;
        private readonly Plane[] planes = new Plane[6];
        private readonly Vector4[] planeVectors = new Vector4[6];
        private readonly int cell, axis, capacity, perSlot, generateKernel, cullKernel, countOffset;
        private readonly int pool;
        private readonly float minY, maxY;
        private Vector3 lastPosition,lastFocus;
        private Quaternion lastRotation;
        private Matrix4x4 lastProjection;
        private int lastPixelHeight,lastTopology,lastTransition,lastOptimization;
        private Vector2Int wantedCenter;
        private int wantedRadius = -1;
        private bool validVisibility;
        private bool referenceGeometry, reducedCards;
        private int preparedFrame = -1;
        internal int Revision { get; }
        internal int ResidentCount => slots.Count;
        internal int Draws { get; private set; }
        internal float LastUsed { get; private set; }
        internal long BufferBytes => (long)capacity * perSlot * (pool==0?64:pool==1?60:36) + capacity * 8 + (pool==0?24:pool==1?28:12) + 4 * GraphicsBuffer.IndirectDrawIndexedArgs.size;

        internal GrassRenderer(GrassLibrary assets, GrassMap source, int chunk, int samples, int maximum, int revision, int poolIndex)
        {
            pool = poolIndex;
            library = assets; map = source; cell = Mathf.Clamp(chunk, 16, 128); axis = Mathf.Clamp(samples, 16, 64);
            capacity = Mathf.Clamp(maximum, 32, 512); perSlot = axis * axis; active = new int[capacity]; Revision = revision;
            var agriculture = source.GetComponent<TacticalMaps.Construction.AgricultureMap>();
            if (agriculture != null && agriculture.farms != null)
            {
                var terrain = source.Terrains[0]; var origin = terrain.transform.position + terrain.terrainData.size * .5f;
                foreach (var farm in agriculture.farms) foreach (var original in farm.Buildings)
                {
                    var building = original; building.center += new Vector2(origin.x, origin.z);
                    float radius = building.halfSize.magnitude + 4;
                    for (int y = Mathf.FloorToInt((building.center.y-radius)/cell); y <= Mathf.FloorToInt((building.center.y+radius)/cell); y++)
                        for (int x = Mathf.FloorToInt((building.center.x-radius)/cell); x <= Mathf.FloorToInt((building.center.x+radius)/cell); x++)
                        { var key = new Vector2Int(x,y); if (!buildingChunks.TryGetValue(key, out var list)) buildingChunks[key] = list = new List<TacticalMaps.Generation.FarmBuilding>(); list.Add(building); }
                }
            }
            minY = float.PositiveInfinity; maxY = float.NegativeInfinity;
            foreach (var t in map.Terrains) { minY = Mathf.Min(minY, t.transform.position.y); maxY = Mathf.Max(maxY, t.transform.position.y + t.terrainData.size.y); }
            try
            {
                compute = UnityEngine.Object.Instantiate(assets.Compute);
                generateKernel = compute.FindKernel("Generate"); cullKernel = compute.FindKernel("Cull");
                instances = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity * perSlot, 32);
                activeSlots = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, 4);
                births = new GraphicsBuffer(GraphicsBuffer.Target.Structured,capacity,4);
                for (int i = 0; i < capacity; i++) free.Enqueue(i);
                countOffset = CounterOffset();
                if(pool<2)
                {
                    preparedMaterial=new Material(library.Material){name="Prepared grass",hideFlags=HideFlags.HideAndDontSave};
                    preparedMaterial.EnableKeyword("GRASS_PREPARED");
                    if(pool==1)preparedMaterial.EnableKeyword("GRASS_UNDERSTOREY");
                    headsMaterial=new Material(preparedMaterial){name="Prepared flower heads",hideFlags=HideFlags.HideAndDontSave};
                    headsMaterial.EnableKeyword("GRASS_HEADS");
                    sparseMaterial=new Material(preparedMaterial){name="Sparse grass patches",hideFlags=HideFlags.HideAndDontSave};
                    sparseMaterial.EnableKeyword("GRASS_SPARSE");
                    prepared=new GraphicsBuffer(GraphicsBuffer.Target.Structured,capacity*perSlot,16);
                    frameCounts=new GraphicsBuffer(GraphicsBuffer.Target.Raw,3,4);
                    // IndirectArguments is a typed R32_UInt UAV. Combining it with Raw is rejected
                    // by Unity and leaves a ByteAddressBuffer writer unable to initialize dispatch counts.
                    frameDispatch=new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments,3,4);
                    frameKernel=compute.FindKernel("PrepareFrame");frameArgsKernel=compute.FindKernel("FrameArguments");
                    renderMeshes[0]=GrassLibrary.CreatePartMesh(library.Mesh(0),false);
                    renderMeshes[1]=GrassLibrary.CreatePartMesh(pool==0?library.Simplified:library.Mesh(1),false);
                    renderMeshes[2]=GrassLibrary.CreatePartMesh(library.Mesh(pool==0?0:1),true);
                    renderMeshes[3]=GrassLibrary.CreatePartMesh(pool==0?library.Simplified:library.Mesh(1),false,true,pool==1);
                }
                if(pool==2)
                {
                    cardsMaterial=new Material(library.CardsMaterial){name="View grass cards",hideFlags=HideFlags.HideAndDontSave};
                    renderMeshes[3]=GrassLibrary.CreateReducedCards(library.CardsMesh);
                }
                for (int lod = 0; lod < 4; lod++)
                {
                    visible[lod] = new GraphicsBuffer(GraphicsBuffer.Target.Append, (lod>=2?pool<2:pool==0||lod==1)?capacity*perSlot:1, 4);
                    arguments[lod] = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
                    var mesh = Mesh(lod);
                    arguments[lod].SetData(new[] { new GraphicsBuffer.IndirectDrawIndexedArgs
                    { indexCountPerInstance = mesh.GetIndexCount(0), startIndex = mesh.GetIndexStart(0), baseVertexIndex = mesh.GetBaseVertex(0) } });
                    drawInputs[lod]=pool<2?new[]{instances,births,visible[lod],prepared}:new[]{instances,births,visible[lod]};
                    properties[lod].SetBuffer("_GrassInstances", instances); properties[lod].SetBuffer("_VisibleGrass", visible[lod]);
                    properties[lod].SetInt("_GrassLod", lod);
                    properties[lod].SetInt("_GrassPool", pool);
                    if(pool<2)properties[lod].SetBuffer("_GrassPrepared",prepared);
                    if(pool==2)
                    {
                        properties[lod].SetTexture("_GrassCardsAlbedo",assets.CardsAlbedo);
                        properties[lod].SetTexture("_GrassCardsNormal",assets.CardsNormal);
                        properties[lod].SetTexture("_GrassCardsFlowers",assets.CardsFlowers!=null?assets.CardsFlowers:Texture2D.blackTexture);
                    }
                    properties[lod].SetBuffer("_GrassBirth",births);properties[lod].SetInt("_GrassPerSlot",perSlot);
                    var directions=new[]{Vector3.up,Vector3.right,Vector3.left,Vector3.forward,Vector3.back};
                    var ambient=new Color[directions.Length]; RenderSettings.ambientProbe.Evaluate(directions,ambient);
                    properties[lod].SetVector("_GrassAmbientSky",(Vector4)ambient[0]);
                    properties[lod].SetVector("_GrassAmbientSide",(Vector4)((ambient[1]+ambient[2]+ambient[3]+ambient[4])*.25f));
                }
                compute.SetBuffer(generateKernel, "_Instances", instances); compute.SetBuffer(cullKernel, "_Instances", instances);
                compute.SetBuffer(cullKernel, "_ActiveSlots", activeSlots);
                compute.SetBuffer(cullKernel, "_Near", visible[0]); compute.SetBuffer(cullKernel, "_Middle", visible[1]);
                compute.SetBuffer(cullKernel,"_Heads",visible[2]);compute.SetBuffer(cullKernel,"_Sparse",visible[3]);
                if(pool<2)
                {
                    compute.SetBuffer(frameArgsKernel,"_FrameCounts",frameCounts);compute.SetBuffer(frameArgsKernel,"_FrameDispatch",frameDispatch);
                    compute.SetBuffer(frameKernel,"_FrameCounts",frameCounts);compute.SetBuffer(frameKernel,"_FrameNear",visible[0]);compute.SetBuffer(frameKernel,"_FrameMiddle",visible[1]);
                    compute.SetBuffer(frameKernel,"_FrameSparse",visible[3]);
                    compute.SetBuffer(frameKernel,"_Instances",instances);compute.SetBuffer(frameKernel,"_Prepared",prepared);
                }
                compute.SetInt("_Axis", axis); compute.SetInt("_PerSlot", perSlot); compute.SetFloat("_ChunkSize", cell);
                compute.SetInt("_Pool",pool);
                for (int i = 0; i < 3; i++)
                {
                    var t = map.Terrains[i]; var p = t.transform.position; var size = t.terrainData.size;
                    compute.SetTexture(generateKernel, "_Height" + i, t.terrainData.heightmapTexture);
                    compute.SetTexture(generateKernel, "_Coverage" + i, map.Coverage[i]);
                    compute.SetTexture(generateKernel, "_Holes" + i, t.terrainData.holesTexture);
                    compute.SetVector("_Rect" + i, new Vector4(p.x, p.z, size.x, size.z));
                    compute.SetVector("_Vertical" + i, new Vector4(p.y, size.y, t.terrainData.heightmapResolution, 0));
                    foreach(var block in properties)
                    {
                        block.SetTexture("_GroundHeight"+i,t.terrainData.heightmapTexture);
                        block.SetTexture("_GroundCoverage"+i,map.Coverage[i]);
                        block.SetTexture("_GroundHoles"+i,t.terrainData.holesTexture);
                        block.SetVector("_GroundRect"+i,new Vector4(p.x,p.z,size.x,size.z));
                        block.SetVector("_GroundVertical"+i,new Vector4(p.y,size.y,t.terrainData.heightmapResolution,0));
                    }
                }
            }
            catch { Dispose(); throw; }
        }
        private static int CounterOffset()
        {
            const uint marker = 0x1234ABCD;
            var data = new[] { new GraphicsBuffer.IndirectDrawIndexedArgs { instanceCount = marker } };
            var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                var bytes = new byte[GraphicsBuffer.IndirectDrawIndexedArgs.size]; Marshal.Copy(handle.AddrOfPinnedObject(), bytes, 0, bytes.Length);
                for (int i = 0; i <= bytes.Length - 4; i += 4) if (BitConverter.ToUInt32(bytes, i) == marker) return i;
                throw new InvalidOperationException("Indirect instance-count field unavailable.");
            }
            finally { handle.Free(); }
        }
        internal void Render(Camera camera, GrassRenderSettings s)
        {
            Draws = 0;
            if((s.GeometryMask&(1<<pool))==0) return;
            if((pool==2&&(s.SuppressCards||s.BakeMode>0))||(pool==1&&s.BakeMode>0&&s.BakeRegion.w>0)) return;
            LastUsed = Time.realtimeSinceStartup;
            compute.SetVector("_HeightRange", new Vector4(Mathf.Max(.05f, s.Heights.x), Mathf.Max(s.Heights.x, s.Heights.y), 0, 0));
            compute.SetVector("_PlantRules", new Vector4(s.Density, Mathf.Cos(s.Slope * Mathf.Deg2Rad), map.SeaLevel + s.Shore, s.Seed));
            var flowers=s.Flowers;flowers.w=s.BakeMode>0?1:0;
            compute.SetVector("_GrassFlowers",flowers);compute.SetInt("_GrassFlowerCount",s.FlowerCount);
            compute.SetVectorArray("_GrassFlowerColors",s.FlowerColors);
            compute.SetInt("_ReferenceGeometry",s.Reference?1:0);
            compute.SetInt("_Optimization",s.BakeMode>0?0:s.Optimization);
            bool cardsChanged=pool==2&&reducedCards!=(!s.Reference&&(s.Optimization&8)!=0&&s.BakeMode==0);
            reducedCards=!s.Reference&&(s.Optimization&8)!=0&&s.BakeMode==0;
            if(referenceGeometry!=s.Reference||cardsChanged)
            {
                referenceGeometry=s.Reference;validVisibility=false;
                for(int i=0;i<4;i++)
                {
                    var mesh=Mesh(i);
                    arguments[i].SetData(new[]{new GraphicsBuffer.IndirectDrawIndexedArgs
                    {indexCountPerInstance=mesh.GetIndexCount(0),startIndex=mesh.GetIndexStart(0),baseVertexIndex=mesh.GetBaseVertex(0)}});
                }
            }
            bool changed = !validVisibility || lastOptimization!=s.Optimization || lastTopology!=s.CheckTopology || lastTransition!=s.TransitionCheck || lastFocus!=s.Focus || lastPosition != camera.transform.position || lastRotation != camera.transform.rotation || lastProjection != camera.projectionMatrix || lastPixelHeight != camera.pixelHeight;
            // Every render in Edit Mode may have the same Time.frameCount. Only Game views use the frame guard.
            if (!Application.isPlaying || preparedFrame != Time.frameCount)
            {
                changed |= Prepare(camera, s); preparedFrame = Time.frameCount;
            }
            if (changed)
            {
                GeometryUtility.CalculateFrustumPlanes(camera, planes);
                for (int i = 0; i < 6; i++) planeVectors[i] = new Vector4(planes[i].normal.x, planes[i].normal.y, planes[i].normal.z, planes[i].distance);
                int count = 0;
                foreach (var entry in slots)
                {
                    var key = entry.Key;
                    var bounds = new Bounds(new Vector3((key.x + .5f) * cell, (minY + maxY) * .5f, (key.y + .5f) * cell), new Vector3(cell + 3, maxY - minY + 4, cell + 3));
                    if (GeometryUtility.TestPlanesAABB(planes, bounds)) active[count++] = entry.Value;
                }
                foreach(var buffer in visible)buffer.SetCounterValue(0);
                if (count > 0)
                {
                    activeSlots.SetData(active, 0, 0, count);
                    compute.SetInt("_ActiveCount", count); compute.SetVectorArray("_Planes", planeVectors);
                    compute.SetVector("_Camera", camera.transform.position);
                    compute.SetVector("_Focus", s.Focus);
                    compute.SetInt("_TopologyCheck",s.CheckTopology);
                    compute.SetInt("_TransitionCheck",s.TransitionCheck);
                    compute.SetVector("_MeadowRange",new Vector4(s.MeadowDistance*.48f,s.MeadowDistance*.82f,s.Seed,s.MeadowDistance));
                    float pixels = camera.orthographic ? camera.pixelHeight / (2 * camera.orthographicSize) : camera.pixelHeight / (2 * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f));
                    compute.SetVector("_Lod", new Vector4(Mathf.Max(20, s.Distance), Mathf.Max(1, s.NearDistance), s.MinPixels, 0));
                    compute.SetVector("_Projection", new Vector4(pixels, camera.orthographic ? 1 : 0, s.MiddleDensity, s.Wind.x));
                    work.Clear();cullSampler.Begin(work);
                    work.DispatchCompute(compute,cullKernel,(count*perSlot+63)/64,1,1);
                    cullSampler.End(work);Graphics.ExecuteCommandBuffer(work);
                }
                for (int i = 0; i < 4; i++) GraphicsBuffer.CopyCount(visible[i], arguments[i], countOffset);
                lastOptimization=s.Optimization;lastTransition=s.TransitionCheck;lastTopology=s.CheckTopology;lastFocus=s.Focus; lastPosition = camera.transform.position; lastRotation = camera.transform.rotation; lastProjection = camera.projectionMatrix; lastPixelHeight = camera.pixelHeight; validVisibility = true;
            }
            float frameTime=Application.isPlaying?Time.time:Time.realtimeSinceStartup;
            if(pool<2&&!s.Reference&&(changed||s.Wind.x>0))
            {
                // One evaluation per visible plant, reused by every vertex and depth/normal pass.
                work.Clear();prepareSampler.Begin(work);
                if(changed)
                {
                    GraphicsBuffer.CopyCount(visible[0],frameCounts,0);GraphicsBuffer.CopyCount(visible[1],frameCounts,4);GraphicsBuffer.CopyCount(visible[3],frameCounts,8);
                    work.DispatchCompute(compute,frameArgsKernel,1,1,1);
                }
                compute.SetVector("_FrameWind",new Vector4(frameTime,s.Wind.y,s.Seed,0));
                compute.SetVector("_FrameMeadow",QualitySettings.activeColorSpace==ColorSpace.Linear?s.Meadow.linear:s.Meadow);
                compute.SetVector("_FrameDry",QualitySettings.activeColorSpace==ColorSpace.Linear?s.Dry.linear:s.Dry);
                compute.SetVector("_FrameNearRange",new Vector4(s.NearRange.x,s.NearRange.y,0,0));
                work.DispatchCompute(compute,frameKernel,frameDispatch,0);
                prepareSampler.End(work);Graphics.ExecuteCommandBuffer(work);
            }
            Draws = 0;
            foreach (var entry in properties)
            {
                entry.SetInt("_GrassTopologyCheck",s.CheckTopology);
                entry.SetInt("_GrassTransitionCheck",s.TransitionCheck);
                entry.SetFloat("_GrassFrameTime",frameTime);
                entry.SetVector("_GrassFlowers",s.Flowers);entry.SetInt("_GrassFlowerCount",s.FlowerCount);
                entry.SetVectorArray("_GrassFlowerColors",s.FlowerColors);entry.SetVectorArray("_GrassFlowerShapes",s.FlowerShapes);
                entry.SetVector("_GrassFieldRanges",new Vector4(s.NearRange.x,s.NearRange.y,s.MeadowDistance,s.Distance));
                entry.SetVector("_GrassFocus",s.Focus);
                entry.SetVector("_GrassNearRange",new Vector4(s.NearRange.x,s.NearRange.y,0,0));
                entry.SetColor("_GrassMeadow", s.Meadow); entry.SetColor("_GrassDry", s.Dry);
                entry.SetVector("_GrassWind", new Vector4(s.Wind.x, s.Wind.y, s.Seed, s.Density));
                entry.SetVector("_GrassOptions", new Vector4(cell, s.Debug, 0, 0));
                entry.SetFloat("_GrassBakeMode",s.BakeMode);
                entry.SetVector("_GrassBakeRegion",s.BakeRegion);
                entry.SetVector("_GrassMeadowRange",new Vector4(s.MeadowDistance*.48f,s.MeadowDistance*.82f,0,0));
                entry.SetFloat("_GrassClock",Time.realtimeSinceStartup+(s.Mature?1:0));
                entry.SetVector("_GrassLodDistances", new Vector4(s.Distance,s.NearDistance,s.MinPixels,0));
                float screenScale = camera.orthographic ? camera.pixelHeight / (2 * camera.orthographicSize) : camera.pixelHeight / (2 * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f));
                entry.SetVector("_GrassProjection",new Vector4(screenScale,camera.orthographic?1:0,s.MiddleDensity,0));
            }
            var world = map.Terrains[2]; var outer = world.terrainData.size;
            var allBounds = new Bounds(world.transform.position + outer * .5f, outer + Vector3.one * 8);
            for (int i = pool==0?0:1; i < (pool<2&&!s.Reference?4:2); i++)
            {
                if(i==2&&(s.Flowers.x<=0||s.FlowerCount==0))continue;
                var material=pool==2?cardsMaterial:s.Reference?library.Material:i==2?headsMaterial:i==3?sparseMaterial:preparedMaterial;
                if(pool==2){if(reducedCards)material.EnableKeyword("GRASS_REDUCED_CARDS");else material.DisableKeyword("GRASS_REDUCED_CARDS");}
                if(s.Request!=null)
                {
                    s.Request.Draws.Add(new GrassRenderFeature.Draw{Mesh=Mesh(i),Material=material,Arguments=arguments[i],Properties=properties[i],Inputs=drawInputs[i]});Draws++;continue;
                }
                var rp = new RenderParams(material) { camera = camera, layer = s.Layer, worldBounds = allBounds,
                    matProps = properties[i], shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true,
                    lightProbeUsage = LightProbeUsage.Off, reflectionProbeUsage = ReflectionProbeUsage.Off };
                Graphics.RenderMeshIndirect(rp, Mesh(i), arguments[i]); Draws++;
            }
        }
        private Mesh Mesh(int lod) => pool==2?(reducedCards?renderMeshes[3]:library.CardsMesh):referenceGeometry&&lod<2?(pool==0&&lod==1?library.Simplified:library.Mesh(lod)):renderMeshes[lod];
        private bool Prepare(Camera camera, GrassRenderSettings s)
        {
            var center = new Vector2Int(Mathf.FloorToInt(s.Focus.x / cell), Mathf.FloorToInt(s.Focus.z / cell));
            int radius = Mathf.CeilToInt(s.Distance / cell) + (pool==0?2:1);
            bool changed = false;
            if (wantedRadius != radius || wantedCenter != center)
            {
                wantedRadius = radius; wantedCenter = center;
                wanted.Clear(); wantedSet.Clear();
                var outer = map.Terrains[2]; var p = outer.transform.position; var size = outer.terrainData.size;
                for (int z = -radius; z <= radius; z++) for (int x = -radius; x <= radius; x++)
                {
                    if (x * x + z * z > radius * radius) continue;
                    var key = center + new Vector2Int(x, z);
                    if ((key.x + 1) * cell < p.x || key.x * cell > p.x + size.x || (key.y + 1) * cell < p.z || key.y * cell > p.z + size.z) continue;
                    wanted.Add(key);
                }
                // Prefer nearby terrain in front of the camera; distances remain deterministic.
                var forward = new Vector2(camera.transform.forward.x, camera.transform.forward.z);
                wanted.Sort((a, b) => Score(a, center, forward).CompareTo(Score(b, center, forward)));
                if (wanted.Count > capacity) wanted.RemoveRange(capacity, wanted.Count - capacity);
                foreach (var key in wanted) wantedSet.Add(key);
            }
            int budget = Mathf.Max(1, s.PrepareBudget);
            foreach (var key in wanted)
            {
                if (slots.ContainsKey(key)) continue;
                if (budget-- <= 0) break;
                if(free.Count==0)
                {
                    Vector2 cameraXZ=new Vector2(s.Focus.x,s.Focus.z);
                    Vector2Int victim=default;float farthest=-1;
                    foreach(var entry in slots)
                    {
                        if(wantedSet.Contains(entry.Key)) continue;
                        var minimum=new Vector2(entry.Key.x*cell,entry.Key.y*cell);
                        var closest=new Vector2(Mathf.Clamp(cameraXZ.x,minimum.x,minimum.x+cell),Mathf.Clamp(cameraXZ.y,minimum.y,minimum.y+cell));
                        float distance=(closest-cameraXZ).sqrMagnitude;
                        if(distance>s.Distance*s.Distance&&distance>farthest) {farthest=distance;victim=entry.Key;}
                    }
                    if(farthest<0) break;
                    free.Enqueue(slots[victim]);slots.Remove(victim);
                }
                int slot = free.Dequeue(); slots.Add(key, slot);
                birthUpload[0]=Time.realtimeSinceStartup;births.SetData(birthUpload,0,slot,1);
                compute.SetInt("_Slot", slot); compute.SetVector("_ChunkOrigin", new Vector4(key.x * cell, key.y * cell, 0, 0));
                int buildingCount = 0;
                if (buildingChunks.TryGetValue(key, out var chunkBuildings))
                    foreach (var building in chunkBuildings)
                    {
                        if (buildingCount == 64) throw new InvalidOperationException("Grass chunk exceeds the building footprint limit.");
                        buildingRects[buildingCount] = new Vector4(building.center.x, building.center.y, building.halfSize.x, building.halfSize.y);
                        buildingAxes[buildingCount++] = new Vector4(Mathf.Cos(building.angle), Mathf.Sin(building.angle), 0, 0);
                    }
                compute.SetInt("_BuildingCount", buildingCount);
                if (buildingCount > 0) { compute.SetVectorArray("_BuildingRects", buildingRects); compute.SetVectorArray("_BuildingAxes", buildingAxes); }
                compute.Dispatch(generateKernel, (perSlot + 63) / 64, 1, 1); changed = true;
            }
            return changed;
        }
        private static float Score(Vector2Int key, Vector2Int center, Vector2 forward)
        { var d = key - center; return d.sqrMagnitude - Vector2.Dot(new Vector2(d.x, d.y), forward) * 3; }
        internal uint[] ReadCounts()
        {
            var result = new uint[2]; var args = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
            for (int i = 0; i < 2; i++) { arguments[i].GetData(args); result[i] = args[0].instanceCount; }
            if(pool<2){arguments[3].GetData(args);result[1]+=args[0].instanceCount;} return result;
        }
        internal uint ReadHeadCount()
        {
            if(pool==2)return 0;
            var args=new GraphicsBuffer.IndirectDrawIndexedArgs[1];arguments[2].GetData(args);return args[0].instanceCount;
        }
#if UNITY_EDITOR
        internal void VerifyPreparedDispatchForChecks()
        {
            if (pool >= 2 || frameCounts == null) return;
            var counts = new uint[3]; var dispatch = new uint[3]; frameCounts.GetData(counts); frameDispatch.GetData(dispatch);
            if (counts[0] + counts[1] + counts[2] > 0 && (dispatch[0] == 0 || dispatch[1] != 1 || dispatch[2] != 1))
                throw new InvalidOperationException("Visible grass has uninitialized GPU preparation dispatch arguments.");
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct FlowerCheckInstance { public Vector4 position,variation; }
        internal Vector3? FindFlowerForChecks()
        {
            var data=new FlowerCheckInstance[perSlot];Vector3? best=null;float height=0;int chunks=0;
            foreach(var slot in slots.Values)
            {
                instances.GetData(data,0,slot*perSlot,perSlot);
                foreach(var item in data)
                {
                    uint packed=unchecked((uint)BitConverter.SingleToInt32Bits(item.variation.x));
                    if(((packed>>9)&15)==0||item.position.w<=height)continue;
                    height=item.position.w;best=new Vector3(item.position.x,item.position.y+item.position.w*1.35f,item.position.z);
                }
                if(++chunks>=4)break;
            }
            return best;
        }
#endif
        internal Dictionary<uint,int> ReadLodMembershipForChecks()
        {
            var result=new Dictionary<uint,int>();var counts=ReadCounts();
            if(pool<2){var a=new GraphicsBuffer.IndirectDrawIndexedArgs[1];arguments[3].GetData(a);uint sparse=a[0].instanceCount;counts[1]-=sparse;var ids=new uint[(int)sparse];if(sparse>0)visible[3].GetData(ids);foreach(var id in ids)result.Add(id,1);}
            for(int lod=0;lod<2;lod++)
            {
                var indices=new uint[(int)counts[lod]];
                if(indices.Length>0) visible[lod].GetData(indices,0,0,indices.Length);
                foreach(var index in indices) result.Add(index,lod);
            }
            return result;
        }
        public void Dispose()
        {
            instances?.Dispose(); activeSlots?.Dispose();births?.Dispose();
            prepared?.Dispose();frameCounts?.Dispose();frameDispatch?.Dispose();
            work.Dispose();
            foreach (var b in visible) b?.Dispose(); foreach (var b in arguments) b?.Dispose();
            foreach(var mesh in renderMeshes)GrassMap.Dispose(mesh);
            GrassMap.Dispose(preparedMaterial);
            GrassMap.Dispose(headsMaterial);
            GrassMap.Dispose(sparseMaterial);
            GrassMap.Dispose(cardsMaterial);
            GrassMap.Dispose(compute);
        }
    }
}
