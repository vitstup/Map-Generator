#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TacticalMaps.Construction
{
    // Opt-in development-player benchmark. Normal launches create no component or cameras.
    internal sealed class TreePerformanceProbe : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Application.isEditor || !Environment.GetCommandLineArgs().Contains("-tree-benchmark")) return;
            new GameObject("Tree performance probe").AddComponent<TreePerformanceProbe>();
        }

        private IEnumerator Start()
        {
            string folder = Path.Combine(Environment.CurrentDirectory, "Temp", "tree-player"); Directory.CreateDirectory(folder);
            var trees = FindAnyObjectByType<InstancedTreeRenderer>();
            var terrain = FindObjectsByType<Terrain>(FindObjectsSortMode.None).FirstOrDefault(t => t.name == "Terrain_Main");
            var source = Camera.main;
            if (trees == null || terrain == null || source == null)
            { File.WriteAllText(Path.Combine(folder, "report.txt"), "FAIL: missing saved map, trees or camera."); Application.Quit(1); yield break; }
            // The workbench queues map generation in Awake. Benchmark the saved map, without
            // an unrelated background generation job competing with the first measurement.
            foreach (var workbench in FindObjectsByType<TacticalMaps.Testing.MapWorkbench>(FindObjectsSortMode.None)) workbench.enabled = false;
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None)) canvas.enabled = false;
            foreach (var existing in FindObjectsByType<Camera>(FindObjectsSortMode.None)) existing.enabled = false;
            var camera = new GameObject("Benchmark camera").AddComponent<Camera>(); camera.CopyFrom(source); camera.enabled = false;
            camera.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            camera.orthographic = false; camera.fieldOfView = 40;
            // Hidden/occluded Windows players can skip their swapchain entirely. A real offscreen
            // camera render is required; advancing frameCount alone is not a rendering check.
            var target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target; camera.aspect = 1600f / 900;
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; Application.runInBackground = true;
            var data = terrain.terrainData; var placements = data.treeInstances;
            Vector3 centre = terrain.transform.TransformPoint(Vector3.Scale(placements[placements.Length / 2].position, data.size));
            var timing = new FrameTiming[1];
            var report = new StringBuilder();
            report.AppendLine("Development player, offscreen 1600x900, " + SystemInfo.graphicsDeviceName + ", " + SystemInfo.graphicsDeviceType);
            report.AppendLine("Identical saved placements; warm-up 60 frames + 180 samples per state. GPU/CPU frame timings; no ReadPixels in measured frames.");
            foreach (string view in new[] { "near", "canopy", "grove", "wide" })
            foreach (bool legacy in new[] { true, false })
            {
                trees.LegacyHybridForChecks = legacy;
                camera.transform.position = centre + (view == "near" ? new Vector3(0,4,-22) : view == "canopy" ? new Vector3(0,14,-35) : view == "grove" ? new Vector3(0,85,-180) : new Vector3(0,500,-1000));
                camera.transform.LookAt(centre + (view == "near" || view == "canopy" ? Vector3.up * 7 : Vector3.zero));
                var gpu = new List<double>(180); var cpu = new List<double>(180);
                for (int frame = -60; frame < 180; frame++)
                {
                    camera.Render();
                    FrameTimingManager.CaptureFrameTimings();
                    yield return new WaitForEndOfFrame();
                    if (frame < 0) continue;
                    uint count = FrameTimingManager.GetLatestTimings(1, timing);
                    if (count > 0 && timing[0].gpuFrameTime > 0) gpu.Add(timing[0].gpuFrameTime);
                    if (count > 0 && timing[0].cpuFrameTime > 0) cpu.Add(timing[0].cpuFrameTime);
                }
                string label = view + (legacy ? "-before" : "-after");
                if (trees.SubmittedDraws == 0)
                { File.WriteAllText(Path.Combine(folder,"report.txt"), report + "FAIL: no tree draws in " + label); Application.Quit(1); yield break; }
                report.AppendLine(label + ": GPU median=" + Percentile(gpu,0.5) + ", p95=" + Percentile(gpu,0.95) + ", samples=" + gpu.Count + "; CPU median=" + Percentile(cpu,0.5) + "; mesh triangles=" + trees.MeshTriangles + ", mesh trees=" + trees.VisibleTrees + ", draws=" + trees.SubmittedDraws);
                var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                var previous = RenderTexture.active; RenderTexture.active = target;
                image.ReadPixels(new Rect(0,0,target.width,target.height),0,0); image.Apply();
                File.WriteAllBytes(Path.Combine(folder,label + ".png"),image.EncodeToPNG());
                RenderTexture.active = previous; Destroy(image);
                File.WriteAllText(Path.Combine(folder, "report.txt"), report.ToString());
                yield return null;
            }
            trees.LegacyHybridForChecks = false;
            camera.targetTexture = null; target.Release(); Destroy(target);
            report.AppendLine("PASS: all eight states rendered. GPU samples of zero mean timing is unavailable, not a zero-cost frame.");
            File.WriteAllText(Path.Combine(folder, "report.txt"), report.ToString());
            Application.Quit(0);
        }
        private static string Percentile(List<double> samples, double fraction)
        {
            if (samples.Count == 0) return "unavailable";
            samples.Sort(); return samples[Math.Min(samples.Count - 1, (int)(samples.Count * fraction))].ToString("F3", CultureInfo.InvariantCulture) + "ms";
        }
    }
}
#endif
