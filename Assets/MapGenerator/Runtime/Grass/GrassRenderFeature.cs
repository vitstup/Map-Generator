using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace TacticalMaps.Grass
{
    // Grass scheduling only. Lighting, SSAO and camera quality settings remain owned by URP.
    public sealed class GrassRenderFeature : ScriptableRendererFeature
    {
        [SerializeField] private Shader compositeShader;
        [SerializeField] private UniversalRendererData rendererData;
        private Material composite;
        private GrassPass grassPass;
        private CompositePass compositePass;
        private static readonly Dictionary<ScriptableRenderer, GrassRenderFeature> renderers = new();
        private static readonly Dictionary<int, Request> requests = new();

        internal sealed class Request
        {
            internal readonly List<Draw> Draws = new(8);
        }
        internal struct Draw
        {
            internal Mesh Mesh;
            internal Material Material;
            internal GraphicsBuffer Arguments;
            internal GraphicsBuffer[] Inputs;
            internal MaterialPropertyBlock Properties;
        }
        internal static Request Begin(Camera camera, int optimization, int bakeMode, bool reference, Request request)
        {
            requests.Remove(camera.GetInstanceID());
            request.Draws.Clear();
            if (reference || bakeMode != 0 || (optimization & 64) == 0 || UniversalRenderPipeline.asset == null) return null;
            if (camera.stereoEnabled || camera.GetUniversalAdditionalCameraData().renderType != CameraRenderType.Base) return null;
            if(camera.targetTexture!=null&&camera.targetTexture.antiAliasing>1)return null;
            if (camera.allowMSAA && UniversalRenderPipeline.asset.msaaSampleCount > 1) return null;
            var renderer = camera.GetUniversalAdditionalCameraData().scriptableRenderer;
            if (!renderers.TryGetValue(renderer, out var feature) || feature == null || !feature.isActive || feature.composite == null) return null;
            if(feature.rendererData==null||feature.rendererData.depthPrimingMode!=DepthPrimingMode.Disabled)return null;
            if(feature.rendererData.renderingMode!=RenderingMode.Forward&&feature.rendererData.renderingMode!=RenderingMode.ForwardPlus)return null;
            if(SystemInfo.supportedRenderTargetCount<2)return null;
            var compatibility = GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>();
            if (compatibility != null && compatibility.enableRenderCompatibilityMode) return null;
            requests.Add(camera.GetInstanceID(), request);
            return request;
        }
        public override void Create()
        {
            CoreUtils.Destroy(composite);
            if (compositeShader != null) composite = CoreUtils.CreateEngineMaterial(compositeShader);
            grassPass = new GrassPass();
            compositePass = new CompositePass();
        }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderers[renderer] = this;
            if (composite == null || !requests.TryGetValue(renderingData.cameraData.camera.GetInstanceID(), out var request)) return;
            grassPass.Request = request;
            renderer.EnqueuePass(grassPass);
            compositePass.Material = composite;
            renderer.EnqueuePass(compositePass);
            requests.Remove(renderingData.cameraData.camera.GetInstanceID());
        }
        protected override void Dispose(bool disposing)
        {
            var keys = new List<ScriptableRenderer>();
            foreach (var entry in renderers) if (entry.Value == this) keys.Add(entry.Key);
            foreach (var key in keys) renderers.Remove(key);
            requests.Clear(); CoreUtils.Destroy(composite);
        }
        private sealed class GrassResources : ContextItem
        {
            internal TextureHandle Color;
            public override void Reset() { Color = TextureHandle.nullHandle; }
        }
        private sealed class PassData
        {
            internal TextureHandle Depth, Color;
            internal Material Material;
            internal MaterialPropertyBlock Properties;
            internal List<Draw> Draws;
        }
        private sealed class GrassPass : ScriptableRenderPass
        {
            internal Request Request;
            private readonly HashSet<GraphicsBuffer> buffers=new();
            private static readonly ProfilingSampler sampler=new("Grass.LitNormals");
            internal GrassPass()
            {
                renderPassEvent = RenderPassEvent.AfterRenderingPrePasses;
                ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
            }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                var resources = frame.Get<UniversalResourceData>();
                var depth = resources.cameraDepthTexture;
                var desc = resources.activeColorTexture.GetDescriptor(graph);
                desc.name = "Grass prelit color"; desc.depthBufferBits = DepthBits.None;
                desc.colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat;
                desc.clearBuffer = true; desc.clearColor = Color.clear; desc.msaaSamples = MSAASamples.None;
                var color = graph.CreateTexture(desc);
                frame.Create<GrassResources>().Color = color;
                using (var builder = graph.AddRasterRenderPass<PassData>("Grass.LitNormals", out var data, sampler))
                {
                    data.Depth = depth; data.Color = color; data.Material = null; data.Properties=null; data.Draws = Request.Draws;
                    builder.SetRenderAttachment(color, 0, AccessFlags.Write);
                    buffers.Clear();
                    foreach(var draw in data.Draws)
                    {
                        if(buffers.Add(draw.Arguments))builder.UseBuffer(graph.ImportBuffer(draw.Arguments),AccessFlags.Read);
                        foreach(var buffer in draw.Inputs)if(buffers.Add(buffer))builder.UseBuffer(graph.ImportBuffer(buffer),AccessFlags.Read);
                    }
                    builder.SetRenderAttachment(resources.cameraNormalsTexture, 1, AccessFlags.ReadWrite);
                    builder.SetRenderAttachmentDepth(depth, AccessFlags.ReadWrite);
                    // Material functions use URP's light/shadow globals, prepared before this event.
                    if(resources.mainShadowsTexture.IsValid())builder.UseTexture(resources.mainShadowsTexture,AccessFlags.Read);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (PassData d, RasterGraphContext ctx) =>
                    {
                        foreach (var draw in d.Draws)
                            ctx.cmd.DrawMeshInstancedIndirect(draw.Mesh, 0, draw.Material, draw.Material.FindPass("GrassLitNormals"), draw.Arguments, 0, draw.Properties);
                    });
                }
            }
        }
        private sealed class CompositePass : ScriptableRenderPass
        {
            internal Material Material;
            private static readonly ProfilingSampler sampler=new("Grass.Composite");
            private readonly MaterialPropertyBlock properties=new();
            internal CompositePass() { renderPassEvent = RenderPassEvent.AfterRenderingOpaques; }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                var resources = frame.Get<UniversalResourceData>();
                var color = frame.Get<GrassResources>().Color;
                using var builder = graph.AddRasterRenderPass<PassData>("Grass.Composite", out var data, sampler);
                data.Depth = resources.cameraDepthTexture; data.Color = color; data.Material = Material; data.Properties=properties; data.Draws = null;
                builder.UseTexture(data.Depth, AccessFlags.Read); builder.UseTexture(color, AccessFlags.Read);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);
                builder.SetRenderFunc(static (PassData d, RasterGraphContext ctx) =>
                {
                    d.Properties.SetTexture("_GrassStoredDepth",(Texture)d.Depth);
                    d.Properties.SetTexture("_GrassStoredColor",(Texture)d.Color);
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, d.Material, 0, MeshTopology.Triangles, 3, 1, d.Properties);
                });
            }
        }
    }
}
