using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace TacticalMaps.Construction
{
    // All instance transforms are uploaded once. Visibility compaction and draw counts stay on the GPU.
    internal sealed class IndirectTreeImpostors : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Tree { internal Matrix4x4 Matrix; internal Vector4 Sphere; }
        private readonly TreeRenderLibrary library;
        private readonly Species[] species;
        private readonly int kernel;
        private readonly Dictionary<int, View> views = new Dictionary<int, View>();
        private readonly Vector4[] planes = new Vector4[6];
        private static readonly int CountOffset = FindCountOffset();
        internal int InputCount { get; }

        private static int FindCountOffset()
        {
            // Unity exposes instanceCount as a property over platform-specific private storage.
            // Locate it in the native argument payload instead of assuming a field name/layout.
            const uint marker = 0x1234ABCD;
            var data = new[] { new GraphicsBuffer.IndirectDrawIndexedArgs { instanceCount = marker } };
            var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                var bytes = new byte[GraphicsBuffer.IndirectDrawIndexedArgs.size];
                Marshal.Copy(handle.AddrOfPinnedObject(), bytes, 0, bytes.Length);
                for (int i = 0; i <= bytes.Length - sizeof(uint); i += sizeof(uint))
                    if (BitConverter.ToUInt32(bytes, i) == marker) return i;
                throw new InvalidOperationException("Cannot locate indirect instance-count storage.");
            }
            finally { handle.Free(); }
        }

        internal IndirectTreeImpostors(TreeRenderLibrary assets, List<Tree>[] input, int layer)
        {
            library = assets; kernel = assets.ImpostorCulling.FindKernel("CullTrees");
            species = new Species[input.Length];
            try
            {
                for (int i = 0; i < input.Length; i++)
                {
                    if (input[i].Count == 0) continue;
                    var trees = input[i]; var bounds = new Bounds((Vector3)trees[0].Sphere, Vector3.zero);
                    foreach (var tree in trees)
                        bounds.Encapsulate(new Bounds((Vector3)tree.Sphere, Vector3.one * (tree.Sphere.w * 1.5f + 4)));
                    var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, trees.Count, 80);
                    species[i] = new Species { Buffer = buffer, Count = trees.Count, Bounds = bounds, Layer = layer };
                    buffer.SetData(trees); InputCount += trees.Count;
                }
            }
            catch { Dispose(); throw; }
        }

        internal int Render(Camera camera, Plane[] frustum, float projectionScale, float nearHeight, float minimumPixels, float distance, bool refresh)
        {
            int id = camera.GetInstanceID();
            if (!views.TryGetValue(id, out var view))
            {
                if (views.Count >= 4) { foreach (var previous in views.Values) previous.Dispose(); views.Clear(); }
                views.Add(id, view = new View(species.Length)); refresh = true;
            }
            var shader = library.ImpostorCulling;
            if (refresh)
            {
                for (int i = 0; i < 6; i++) planes[i] = new Vector4(frustum[i].normal.x, frustum[i].normal.y, frustum[i].normal.z, frustum[i].distance);
                shader.SetVectorArray("_Planes", planes); shader.SetVector("_Eye", camera.transform.position);
                shader.SetFloat("_ProjectionScale", projectionScale); shader.SetFloat("_NearScreenHeight", nearHeight);
                shader.SetFloat("_MinScreenHeight", minimumPixels / Mathf.Max(1, camera.pixelHeight));
                shader.SetFloat("_DrawDistance", Mathf.Min(distance, camera.farClipPlane));
                shader.SetInt("_Orthographic", camera.orthographic ? 1 : 0);
                for (int i = 0; i < species.Length; i++)
                {
                    var source = species[i]; if (source == null) continue;
                    var output = view.Species[i];
                    if (output == null) view.Species[i] = output = new Visible(source, library.ImpostorMesh, library.SpeciesList[i]);
                    output.Active = (camera.cullingMask & (1 << source.Layer)) != 0 && source.Bounds.SqrDistance(camera.transform.position) <= distance * distance && GeometryUtility.TestPlanesAABB(frustum, source.Bounds);
                    output.Indices.SetCounterValue(0);
                    if (output.Active)
                    {
                        shader.SetInt("_Count", source.Count); shader.SetBuffer(kernel, "_Trees", source.Buffer); shader.SetBuffer(kernel, "_Visible", output.Indices);
                        shader.Dispatch(kernel, (source.Count + 127) / 128, 1, 1);
                    }
                    GraphicsBuffer.CopyCount(output.Indices, output.Arguments, CountOffset);
                    output.Properties.SetVector("_ViewerPosition", camera.transform.position);
                    output.Properties.SetVector("_ViewerDirection", -camera.transform.forward);
                    output.Properties.SetFloat("_Orthographic", camera.orthographic ? 1 : 0);
                    output.Properties.SetFloat("_ProjectionScale", projectionScale); output.Properties.SetFloat("_PixelHeight", camera.pixelHeight);
                }
            }
            int draws = 0;
            for (int i = 0; i < species.Length; i++)
            {
                var source = species[i]; var output = view.Species[i];
                if (source == null || output == null || !output.Active) continue;
                var parameters = new RenderParams(library.SpeciesList[i].impostorMaterial)
                {
                    camera = camera, layer = source.Layer, worldBounds = source.Bounds, matProps = output.Properties,
                    shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false,
                    lightProbeUsage = LightProbeUsage.Off, reflectionProbeUsage = ReflectionProbeUsage.Off,
                    motionVectorMode = MotionVectorGenerationMode.ForceNoMotion, renderingLayerMask = uint.MaxValue
                };
                Graphics.RenderMeshIndirect(parameters, library.ImpostorMesh, output.Arguments);
                draws++;
            }
            return draws;
        }

        // Explicit diagnostic readback only. Never called from the render loop.
        internal uint ReadVisibleCount(Camera camera)
        {
            uint count = 0;
            if (!views.TryGetValue(camera.GetInstanceID(), out var view)) return 0;
            var data = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
            foreach (var output in view.Species)
            { if (output == null || !output.Active) continue; output.Arguments.GetData(data); count += data[0].instanceCount; }
            return count;
        }
        public void Dispose()
        {
            foreach (var view in views.Values) view.Dispose(); views.Clear();
            if (species != null) foreach (var source in species) source?.Buffer?.Dispose();
        }
        private sealed class Species { internal GraphicsBuffer Buffer; internal int Count, Layer; internal Bounds Bounds; }
        private sealed class View : IDisposable
        {
            internal readonly Visible[] Species;
            internal View(int count) => Species = new Visible[count];
            public void Dispose() { foreach (var source in Species) source?.Dispose(); }
        }
        private sealed class Visible : IDisposable
        {
            internal readonly GraphicsBuffer Indices, Arguments;
            internal readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
            internal bool Active;
            internal Visible(Species source, Mesh mesh, TreeRenderLibrary.Species definition)
            {
                try
                {
                    Indices = new GraphicsBuffer(GraphicsBuffer.Target.Append, source.Count, sizeof(uint));
                    Arguments = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
                    Arguments.SetData(new[] { new GraphicsBuffer.IndirectDrawIndexedArgs { indexCountPerInstance = mesh.GetIndexCount(0), startIndex = mesh.GetIndexStart(0), baseVertexIndex = (uint)mesh.GetBaseVertex(0) } });
                    Properties.SetBuffer("_Trees", source.Buffer); Properties.SetBuffer("_Visible", Indices);
                    Properties.SetVector("_Capture", definition.impostorCapture);
                }
                catch { Dispose(); throw; }
            }
            public void Dispose() { Indices?.Dispose(); Arguments?.Dispose(); }
        }
    }
}
