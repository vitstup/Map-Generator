using System;
using System.Collections.Generic;
using TacticalMaps.Generation;
using UnityEngine;
using UnityEngine.Rendering;

namespace TacticalMaps.Construction
{
    [ExecuteAlways, DisallowMultipleComponent]
    internal sealed class CropRenderer : MonoBehaviour
    {
        [SerializeField] private AgricultureLibrary library;
        [SerializeField] private Terrain[] terrains;
        [SerializeField] private Vector4[] patches = Array.Empty<Vector4>();
        [SerializeField] private Vector4[] fieldShapes = Array.Empty<Vector4>(), fieldStyles = Array.Empty<Vector4>();
        [SerializeField] private Vector3 mapCenter;
        [SerializeField] private float nearDistance = 110, farDistance = 330;
        private ComputeBuffer patchBuffer, shapeBuffer, styleBuffer;
        private readonly Dictionary<int, View> views = new Dictionary<int, View>();
        internal int PatchCount => patches.Length;
        internal long BufferBytes => (patchBuffer != null ? patches.Length * 16L + fieldShapes.Length * 32L : 0) + views.Count * (patches.Length * 8L + 40);
        internal bool SuppressForChecks { get; set; }
        private sealed class View : IDisposable
        {
            internal ComputeBuffer Near, Far, NearArgs, FarArgs;
            internal Camera Camera;
            internal float LastUsed;
            public void Dispose() { Near?.Release(); Far?.Release(); NearArgs?.Release(); FarArgs?.Release(); }
        }
        internal void Configure(AgricultureLibrary resources, FarmField[] fields, Terrain[] ground, Vector3 center, Vector2 mainSize, float near, float far, int limit)
        {
            Release(); library = resources; terrains = ground; mapCenter = center; nearDistance = near; farDistance = far;
            var points = new List<Vector4>(); var shapes = new List<Vector4>(); var styles = new List<Vector4>();
            foreach (var f in fields)
            {
                if (f.crop == 0 || Mathf.Abs(f.center.x) > mainSize.x / 2 + far + 100 || Mathf.Abs(f.center.y) > mainSize.y / 2 + far + 100) continue;
                int id = shapes.Count; shapes.Add(new Vector4(f.center.x + center.x, f.center.y + center.z, f.halfSize.x, f.halfSize.y)); styles.Add(new Vector4(f.angle, f.crop, f.variation, 0));
                for (float z = -f.halfSize.y + 2; z <= f.halfSize.y - 2; z += 4)
                    for (float x = -f.halfSize.x + 2; x <= f.halfSize.x - 2; x += 4)
                    {
                        var p = f.Point(x, z);
                        if (f.SignedDistance(p) > -2) continue;
                        var world = AgricultureBuilder.Ground(p, ground, center);
                        if (points.Count >= limit) throw new InvalidOperationException("Crop patch budget exceeded. Increase Maximum Crop Patches or lower agricultural coverage.");
                        points.Add(new Vector4(world.x, world.y, world.z, id));
                    }
            }
            patches = points.ToArray(); fieldShapes = shapes.ToArray(); fieldStyles = styles.ToArray();
        }
        private void OnEnable() => RenderPipelineManager.beginCameraRendering += Render;
        private void OnDisable() { RenderPipelineManager.beginCameraRendering -= Render; Release(); }
        private void OnValidate() => Release();
        private void Release()
        {
            foreach (var v in views.Values) v.Dispose(); views.Clear();
            patchBuffer?.Release(); shapeBuffer?.Release(); styleBuffer?.Release(); patchBuffer = shapeBuffer = styleBuffer = null;
        }
        private void Render(ScriptableRenderContext context, Camera camera)
        {
            if (SuppressForChecks || patches.Length == 0 || library == null || terrains == null || terrains.Length != 3 || !SystemInfo.supportsComputeShaders || !SystemInfo.supportsInstancing || camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection || (camera.cullingMask & (1 << gameObject.layer)) == 0) return;
#if UNITY_EDITOR
            if (camera.scene.IsValid() && UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(camera.scene) && camera.scene != gameObject.scene) return;
#endif
            if (patchBuffer == null)
            {
                patchBuffer = new ComputeBuffer(patches.Length, 16); patchBuffer.SetData(patches);
                shapeBuffer = new ComputeBuffer(fieldShapes.Length, 16); shapeBuffer.SetData(fieldShapes);
                styleBuffer = new ComputeBuffer(fieldStyles.Length, 16); styleBuffer.SetData(fieldStyles);
            }
            int id = camera.GetInstanceID();
            if (!views.TryGetValue(id, out var view))
            {
                // Limit residency for auxiliary cameras; Terrain always supplies their distant appearance.
                if (views.Count >= 2)
                {
                    int victim = 0; bool found = false;
                    foreach (var pair in views) if (pair.Value.Camera == null || Time.realtimeSinceStartup - pair.Value.LastUsed > .5f) { victim = pair.Key; found = true; break; }
                    if (!found) return;
                    views[victim].Dispose(); views.Remove(victim);
                }
                view = new View { Near = new ComputeBuffer(patches.Length, 4, ComputeBufferType.Append), Far = new ComputeBuffer(patches.Length, 4, ComputeBufferType.Append),
                    NearArgs = new ComputeBuffer(1, 20, ComputeBufferType.IndirectArguments), FarArgs = new ComputeBuffer(1, 20, ComputeBufferType.IndirectArguments), Camera = camera };
                view.NearArgs.SetData(Args(library.closeRice)); view.FarArgs.SetData(Args(library.distantRice)); views.Add(id, view);
            }
            view.LastUsed = Time.realtimeSinceStartup;
            view.Near.SetCounterValue(0); view.Far.SetCounterValue(0);
            var compute = library.culling; int kernel = 0;
            compute.SetBuffer(kernel, "_CropPatches", patchBuffer); compute.SetBuffer(kernel, "_Near", view.Near); compute.SetBuffer(kernel, "_Far", view.Far);
            compute.SetInt("_Count", patches.Length); compute.SetVector("_Camera", camera.transform.position); compute.SetVector("_Ranges", new Vector4(nearDistance, farDistance, 0, 0));
            var planes = GeometryUtility.CalculateFrustumPlanes(camera); var vectors = new Vector4[6];
            for (int i = 0; i < 6; i++) vectors[i] = new Vector4(planes[i].normal.x, planes[i].normal.y, planes[i].normal.z, planes[i].distance);
            compute.SetVectorArray("_Planes", vectors); compute.Dispatch(kernel, (patches.Length + 63) / 64, 1, 1);
            ComputeBuffer.CopyCount(view.Near, view.NearArgs, 4); ComputeBuffer.CopyCount(view.Far, view.FarArgs, 4);
            Draw(camera, library.closeRice, view.Near, view.NearArgs, 0); Draw(camera, library.distantRice, view.Far, view.FarArgs, 1);
        }
        private static uint[] Args(Mesh mesh) => new[] { mesh.GetIndexCount(0), 0u, mesh.GetIndexStart(0), (uint)mesh.GetBaseVertex(0), 0u };
        private void Draw(Camera camera, Mesh mesh, ComputeBuffer visible, ComputeBuffer args, int lod)
        {
            var block = new MaterialPropertyBlock(); block.SetBuffer("_CropPatches", patchBuffer); block.SetBuffer("_CropShapes", shapeBuffer); block.SetBuffer("_CropStyles", styleBuffer); block.SetBuffer("_VisibleCrops", visible);
            block.SetVector("_CropRanges", new Vector4(nearDistance, farDistance, lod, 0));
            for (int l = 0; l < 3; l++)
            {
                var terrain = terrains[l]; var p = terrain.transform.position; var s = terrain.terrainData.size;
                block.SetTexture("_CropHeight" + l, terrain.terrainData.heightmapTexture);
                block.SetVector("_CropRect" + l, new Vector4(p.x, p.z, s.x, s.z));
                block.SetVector("_CropVertical" + l, new Vector4(p.y, s.y, terrain.terrainData.heightmapResolution, 0));
            }
            var bounds = new Bounds(mapCenter + Vector3.up * terrains[0].terrainData.size.y / 2, new Vector3(terrains[2].terrainData.size.x, terrains[0].terrainData.size.y + 100, terrains[2].terrainData.size.z));
            Graphics.DrawMeshInstancedIndirect(mesh, 0, library.cropMaterial, bounds, args, 0, block, ShadowCastingMode.Off, true, gameObject.layer, camera, LightProbeUsage.Off);
        }
    }
}
