using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TacticalMaps.Construction;
using UnityEditor;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class TreeCostProfile
    {
        [MenuItem("Tools/Tactical Maps/Profile Tree Cost Before")]
        private static void Before() => Run("before");
        [MenuItem("Tools/Tactical Maps/Profile Tree Cost After")]
        private static void After() => Run("after");
        [MenuItem("Tools/Tactical Maps/Profile Legacy Mesh Trees")]
        private static void Legacy() => Run("legacy", true);
        [MenuItem("Tools/Tactical Maps/Compare Tree Optimization")]
        internal static void Compare() { Run("hybrid-before", hybrid: true); Run("hybrid-after"); }
        private static void Run(string label, bool legacy = false, bool hybrid = false)
        {
            var builder = UnityEngine.Object.FindAnyObjectByType<MapBuilder>();
            var trees = builder.GetComponentInChildren<InstancedTreeRenderer>();
            var source = Camera.main;
            var terrain = builder.GetComponentsInChildren<Terrain>().First(t => t.name == "Terrain_Main");
            var data = terrain.terrainData; var p = data.treeInstances[data.treeInstanceCount / 2].position;
            var centre = terrain.transform.TransformPoint(Vector3.Scale(p, data.size));
            var text = new System.Text.StringBuilder();
            var go = new GameObject("Tree cost camera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = go.AddComponent<Camera>(); camera.enabled = false; camera.CopyFrom(source); camera.enabled = false;
            var target = new RenderTexture(1600, 900, 24); var pixel = new Texture2D(1, 1, TextureFormat.RGB24, false);
            var active = RenderTexture.active;
            try
            {
                trees.DetailedGeometryForChecks = legacy;
                trees.LegacyHybridForChecks = hybrid;
                foreach (string view in new[] { "game", "near", "canopy", "grove", "wide" })
                {
                    if (view == "game") { go.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation); }
                    else
                    {
                        go.transform.position = centre + (view == "near" ? new Vector3(0, 4, -22) : view == "canopy" ? new Vector3(0, 14, -35) : view == "grove" ? new Vector3(0, 85, -180) : new Vector3(0, 500, -1000));
                        go.transform.LookAt(centre + (view == "near" || view == "canopy" ? Vector3.up * 7 : Vector3.zero));
                    }
                    camera.orthographic = false; camera.fieldOfView = 40; camera.aspect = 1600f / 900; camera.targetTexture = target;
                    trees.enabled = true; double on = Measure(camera, target, pixel);
                    int visible = trees.VisibleTrees, draws = trees.SubmittedDraws;
                    uint impostors = trees.ReadImpostorCountForChecks(camera); long triangles = trees.MeshTriangles + impostors * 2;
                    double cull = trees.CullMilliseconds, submit = trees.SubmitMilliseconds;
                    var lods = (int[])trees.VisibleLods.Clone();
                    MapVisualChecks.Capture(camera, $"Temp/tree-cost-{label}-{view}.png", 1600, 900); camera.targetTexture = target;
                    trees.SuppressDrawingForChecks = true; double off = Measure(camera, target, pixel);
                    trees.SuppressDrawingForChecks = false;
                    camera.Render();
                    double movingStarted = Stopwatch.GetTimestamp();
                    for (int i = 0; i < 8; i++) { go.transform.position += Vector3.right; camera.Render(); RenderTexture.active = target; pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); pixel.Apply(); }
                    double moving = (Stopwatch.GetTimestamp() - movingStarted) * (1000.0 / Stopwatch.Frequency) / 8;
                    text.AppendLine($"{view}: enabled={on:F3}ms, disabled={off:F3}ms, difference={on-off:F3}ms, moving={moving:F3}ms, visible meshes={visible}, impostors={impostors}, triangles={triangles}, draws={draws}, lods={string.Join(",", lods)}, cull={cull:F3}ms, submit={submit:F3}ms, movingCull={trees.CullMilliseconds:F3}ms");
                }
                File.WriteAllText("Temp/tree-cost-" + label + ".txt", text.ToString()); UnityEngine.Debug.Log(text.ToString());
            }
            finally { trees.SuppressDrawingForChecks = false; trees.DetailedGeometryForChecks = false; trees.LegacyHybridForChecks = false; trees.enabled = true; RenderTexture.active = active; camera.targetTexture = null; target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixel); UnityEngine.Object.DestroyImmediate(go); }
        }
        private static double Measure(Camera camera, RenderTexture target, Texture2D pixel)
        {
            var times = new double[9];
            for (int i = -3; i < times.Length; i++)
            {
                long started = Stopwatch.GetTimestamp(); camera.Render(); RenderTexture.active = target;
                pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); pixel.Apply();
                if (i >= 0) times[i] = (Stopwatch.GetTimestamp() - started) * (1000.0 / Stopwatch.Frequency);
            }
            Array.Sort(times); return times[times.Length / 2];
        }
    }
}
