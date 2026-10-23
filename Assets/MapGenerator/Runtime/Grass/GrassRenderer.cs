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
        private readonly ComputeShader compute;
        private readonly GraphicsBuffer instances, activeSlots, births;
        private readonly float[] birthUpload = new float[1];
        private readonly GraphicsBuffer[] visible = new GraphicsBuffer[2], arguments = new GraphicsBuffer[2];
        private readonly MaterialPropertyBlock[] properties = { new MaterialPropertyBlock(), new MaterialPropertyBlock() };
        private readonly Dictionary<Vector2Int, int> slots = new Dictionary<Vector2Int, int>();
        private readonly List<Vector2Int> wanted = new List<Vector2Int>();
        private readonly HashSet<Vector2Int> wantedSet = new HashSet<Vector2Int>();
        private readonly Queue<int> free = new Queue<int>();
        private readonly List<Vector2Int> evict = new List<Vector2Int>();
        private readonly int[] active;
        private readonly Plane[] planes = new Plane[6];
        private readonly Vector4[] planeVectors = new Vector4[6];
        private readonly int cell, axis, capacity, perSlot, generateKernel, cullKernel, countOffset;
        private readonly int pool;
        private readonly float minY, maxY;
        private Vector3 lastPosition;
        private Quaternion lastRotation;
        private Matrix4x4 lastProjection;
        private int lastPixelHeight;
        private Vector2Int wantedCenter;
        private int wantedRadius = -1;
        private bool validVisibility;
        private int preparedFrame = -1;
        internal int Revision { get; }
        internal int ResidentCount => slots.Count;
        internal int Draws { get; private set; }
        internal float LastUsed { get; private set; }
        internal long BufferBytes => (long)capacity * perSlot * 36 + capacity * 8 + 4 + 2 * GraphicsBuffer.IndirectDrawIndexedArgs.size;

        internal GrassRenderer(GrassLibrary assets, GrassMap source, int chunk, int samples, int maximum, int revision, int poolIndex)
        {
            pool = poolIndex;
            library = assets; map = source; cell = Mathf.Clamp(chunk, 16, 64); axis = Mathf.Clamp(samples, 16, 64);
            capacity = Mathf.Clamp(maximum, 32, 384); perSlot = axis * axis; active = new int[capacity]; Revision = revision;
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
                for (int lod = 0; lod < 2; lod++)
                {
                    visible[lod] = new GraphicsBuffer(GraphicsBuffer.Target.Append, lod==pool?capacity*perSlot:1, 4);
                    arguments[lod] = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
                    var mesh = library.Mesh(lod);
                    arguments[lod].SetData(new[] { new GraphicsBuffer.IndirectDrawIndexedArgs
                    { indexCountPerInstance = mesh.GetIndexCount(0), startIndex = mesh.GetIndexStart(0), baseVertexIndex = mesh.GetBaseVertex(0) } });
                    properties[lod].SetBuffer("_GrassInstances", instances); properties[lod].SetBuffer("_VisibleGrass", visible[lod]);
                    properties[lod].SetInt("_GrassLod", lod);
                    properties[lod].SetInt("_GrassPool", pool);
                    properties[lod].SetBuffer("_GrassBirth",births);properties[lod].SetInt("_GrassPerSlot",perSlot);
                    var directions=new[]{Vector3.up,Vector3.right,Vector3.left,Vector3.forward,Vector3.back};
                    var ambient=new Color[directions.Length]; RenderSettings.ambientProbe.Evaluate(directions,ambient);
                    properties[lod].SetVector("_GrassAmbientSky",(Vector4)ambient[0]);
                    properties[lod].SetVector("_GrassAmbientSide",(Vector4)((ambient[1]+ambient[2]+ambient[3]+ambient[4])*.25f));
                }
                compute.SetBuffer(generateKernel, "_Instances", instances); compute.SetBuffer(cullKernel, "_Instances", instances);
                compute.SetBuffer(cullKernel, "_ActiveSlots", activeSlots);
                compute.SetBuffer(cullKernel, "_Near", visible[0]); compute.SetBuffer(cullKernel, "_Middle", visible[1]);
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
            LastUsed = Time.realtimeSinceStartup;
            compute.SetVector("_HeightRange", new Vector4(Mathf.Max(.05f, s.Heights.x), Mathf.Max(s.Heights.x, s.Heights.y), 0, 0));
            compute.SetVector("_PlantRules", new Vector4(s.Density, Mathf.Cos(s.Slope * Mathf.Deg2Rad), map.SeaLevel + s.Shore, s.Seed));
            bool changed = !validVisibility || lastPosition != camera.transform.position || lastRotation != camera.transform.rotation || lastProjection != camera.projectionMatrix || lastPixelHeight != camera.pixelHeight;
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
                visible[0].SetCounterValue(0); visible[1].SetCounterValue(0);
                if (count > 0)
                {
                    activeSlots.SetData(active, 0, 0, count);
                    compute.SetInt("_ActiveCount", count); compute.SetVectorArray("_Planes", planeVectors);
                    compute.SetVector("_Camera", camera.transform.position);
                    compute.SetVector("_CameraForward", camera.transform.forward);
                    compute.SetFloat("_ViewAngle",Mathf.Abs(camera.transform.forward.y));
                    float pixels = camera.orthographic ? camera.pixelHeight / (2 * camera.orthographicSize) : camera.pixelHeight / (2 * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f));
                    compute.SetVector("_Lod", new Vector4(Mathf.Max(20, s.Distance), Mathf.Max(1, s.NearDistance), s.MinPixels, 0));
                    compute.SetVector("_Projection", new Vector4(pixels, camera.orthographic ? 1 : 0, s.MiddleDensity, s.Wind.x));
                    compute.Dispatch(cullKernel, (count * perSlot + 63) / 64, 1, 1);
                }
                for (int i = 0; i < 2; i++) GraphicsBuffer.CopyCount(visible[i], arguments[i], countOffset);
                lastPosition = camera.transform.position; lastRotation = camera.transform.rotation; lastProjection = camera.projectionMatrix; lastPixelHeight = camera.pixelHeight; validVisibility = true;
            }
            Draws = 0;
            foreach (var entry in properties)
            {
                entry.SetColor("_GrassMeadow", s.Meadow); entry.SetColor("_GrassDry", s.Dry);
                entry.SetVector("_GrassWind", new Vector4(s.Wind.x, s.Wind.y, s.Seed, s.Density));
                entry.SetVector("_GrassOptions", new Vector4(cell, s.Debug, 0, 0));
                entry.SetFloat("_GrassBakeMode",s.BakeMode);
                entry.SetFloat("_GrassClock",Time.realtimeSinceStartup+(s.Mature?1:0));
                entry.SetVector("_GrassLodDistances", new Vector4(s.Distance,s.NearDistance,s.MinPixels,0));
                float screenScale = camera.orthographic ? camera.pixelHeight / (2 * camera.orthographicSize) : camera.pixelHeight / (2 * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f));
                entry.SetVector("_GrassProjection",new Vector4(screenScale,camera.orthographic?1:0,s.MiddleDensity,0));
            }
            var world = map.Terrains[2]; var outer = world.terrainData.size;
            var allBounds = new Bounds(world.transform.position + outer * .5f, outer + Vector3.one * 8);
            for (int i = pool; i <= pool; i++)
            {
                var rp = new RenderParams(library.Material) { camera = camera, layer = s.Layer, worldBounds = allBounds,
                    matProps = properties[i], shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true,
                    lightProbeUsage = LightProbeUsage.Off, reflectionProbeUsage = ReflectionProbeUsage.Off };
                Graphics.RenderMeshIndirect(rp, library.Mesh(i), arguments[i]); Draws++;
            }
        }
        private bool Prepare(Camera camera, GrassRenderSettings s)
        {
            var center = new Vector2Int(Mathf.FloorToInt(camera.transform.position.x / cell), Mathf.FloorToInt(camera.transform.position.z / cell));
            int radius = Mathf.CeilToInt(s.Distance / cell) + 1;
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
                evict.Clear(); foreach (var entry in slots) if (!wantedSet.Contains(entry.Key)) evict.Add(entry.Key);
                changed = evict.Count > 0;
                foreach (var key in evict) { free.Enqueue(slots[key]); slots.Remove(key); }
            }
            int budget = Mathf.Max(1, s.PrepareBudget);
            foreach (var key in wanted)
            {
                if (slots.ContainsKey(key)) continue;
                if (budget-- <= 0 || free.Count == 0) break;
                int slot = free.Dequeue(); slots.Add(key, slot);
                birthUpload[0]=Time.realtimeSinceStartup;births.SetData(birthUpload,0,slot,1);
                compute.SetInt("_Slot", slot); compute.SetVector("_ChunkOrigin", new Vector4(key.x * cell, key.y * cell, 0, 0));
                compute.Dispatch(generateKernel, (perSlot + 63) / 64, 1, 1); changed = true;
            }
            return changed;
        }
        private static float Score(Vector2Int key, Vector2Int center, Vector2 forward)
        { var d = key - center; return d.sqrMagnitude - Vector2.Dot(new Vector2(d.x, d.y), forward) * 3; }
        internal uint[] ReadCounts()
        {
            var result = new uint[2]; var args = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
            for (int i = 0; i < 2; i++) { arguments[i].GetData(args); result[i] = args[0].instanceCount; } return result;
        }
        public void Dispose()
        {
            instances?.Dispose(); activeSlots?.Dispose();births?.Dispose();
            foreach (var b in visible) b?.Dispose(); foreach (var b in arguments) b?.Dispose();
            GrassMap.Dispose(compute);
        }
    }
}
