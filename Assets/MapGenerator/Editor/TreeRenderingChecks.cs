using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TacticalMaps.Construction;
using UnityEditor;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class TreeRenderingChecks
    {
        [MenuItem("Tools/Tactical Maps/Verify And Profile Tree Rendering")]
        private static void Run()
        {
            const string report = "Temp/tree-rendering-checks.txt";
            File.WriteAllText(report, "Running...\n");
            var builder = UnityEngine.Object.FindAnyObjectByType<MapBuilder>();
            var renderer = builder.GetComponentInChildren<InstancedTreeRenderer>();
            if (renderer == null) throw new InvalidOperationException("Rebuild instanced trees first.");
            var terrains = builder.GetComponentsInChildren<Terrain>().OrderBy(t => t.terrainData.size.x).ToArray();
            var flags = terrains.Select(t => t.drawTreesAndFoliage).ToArray();
            GameObject cameraObject = null;
            var output = new System.Text.StringBuilder();
            try
            {
                Check(terrains.Length == 3 && terrains.All(t => t.terrainData.treeInstanceCount > 0), "Trees on all three terrains");
                Check(terrains.All(t => !t.drawTreesAndFoliage), "Native tree drawing disabled");
                Check(renderer.TreeCount == terrains.Sum(t => t.terrainData.treeInstanceCount), "Instanced cache includes every placement");
                for (int level = 0; level < terrains.Length; level++)
                {
                    var terrain = terrains[level]; var data = terrain.terrainData; var instances = data.treeInstances;
                    var point = terrain.transform.TransformPoint(Vector3.Scale(instances[instances.Length / 2].position, data.size));
                    cameraObject = MapVisualChecks.CameraAt(point, 90, new Vector3(0, 120, -140), -1);
                    var camera = cameraObject.GetComponent<Camera>(); camera.nearClipPlane = 0.2f; camera.farClipPlane = 12000;
                    var on = MapVisualChecks.Capture(camera, $"Temp/trees-instanced-level-{level}.png", 1000, 700);
                    uint impostors = renderer.ReadImpostorCountForChecks(camera);
                    Check(renderer.VisibleTrees + impostors > 0 && renderer.SubmittedDraws > 0, "Instanced draw submission at level " + level);
                    int visible = renderer.VisibleTrees + (int)impostors, draws = renderer.SubmittedDraws;
                    var lods = (int[])renderer.VisibleLods.Clone();
                    Check(draws < Math.Max(30, visible / 2), "Trees are submitted in batches, not one draw per tree");
                    double submit = renderer.SubmitMilliseconds, cull = renderer.CullMilliseconds;
                    // Same tree set and camera, including a synchronized readback. These are not gameplay FPS measurements.
                    double instancedMs = Measure(camera);
                    Check(renderer.CullMilliseconds == 0, "Stationary cameras reuse their visibility/LOD batches");
                    renderer.enabled = false;
                    foreach (var t in terrains) t.drawTreesAndFoliage = true;
                    double nativeMs;
                    try
                    {
                        nativeMs = Measure(camera);
                        if (level == 0) MapVisualChecks.Capture(camera, "Temp/trees-native-main.png", 1000, 700);
                    }
                    finally
                    {
                        for (int i = 0; i < terrains.Length; i++) terrains[i].drawTreesAndFoliage = flags[i];
                        renderer.enabled = true;
                    }
                    var off = MapVisualChecks.Capture(camera, null, 400, 280);
                    renderer.enabled = false;
                    Color32[] empty;
                    try { empty = MapVisualChecks.Capture(camera, null, 400, 280); }
                    finally { renderer.enabled = true; }
                    int changed = 0;
                    for (int i = 0; i < empty.Length; i++)
                        if (Math.Abs(off[i].r - empty[i].r) + Math.Abs(off[i].g - empty[i].g) + Math.Abs(off[i].b - empty[i].b) > 10) changed++;
                    Check(changed > 200, "Instanced geometry reaches the image, level " + level);
                    output.AppendLine($"level {level}: total={data.treeInstanceCount}, visible={visible}, impostors={impostors}, instance draw submissions={draws}, LOD counts={string.Join(",", lods)}, cull/rebuild={cull:F3}ms, submit={submit:F3}ms, synchronized render/readback native={nativeMs:F2}ms vs instanced={instancedMs:F2}ms, changed pixels={changed}.");
                    UnityEngine.Object.DestroyImmediate(cameraObject); cameraObject = null;
                }
                cameraObject = MapVisualChecks.CameraAt(new Vector3(80000, 10000, 80000), 100, Vector3.forward * 200, -1);
                var far = cameraObject.GetComponent<Camera>(); far.transform.rotation = Quaternion.identity;
                MapVisualChecks.Capture(far, null, 160, 100);
                Check(renderer.VisibleTrees == 0 && renderer.SubmittedDraws == 0 && renderer.ReadImpostorCountForChecks(far) == 0, "No draws when the forest is outside the view/distance");
                output.AppendLine("PASS: rendered pixels on all terrains, original LOD meshes, GPU instance batches, cached stationary views, native rendering disabled, distant/frustum culling. Timings include CPU, GPU, pipeline overhead and readback; compare them only as this fixed offscreen experiment.");
                File.WriteAllText(report, output.ToString()); UnityEngine.Debug.Log("Tree rendering checks passed.\n" + output);
            }
            catch (Exception e) { File.WriteAllText(report, output + e.ToString()); UnityEngine.Debug.LogException(e); }
            finally
            {
                if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
                for (int i = 0; i < terrains.Length; i++) terrains[i].drawTreesAndFoliage = flags[i];
                if (renderer != null) renderer.enabled = true;
            }
        }

        private static double Measure(Camera camera)
        {
            var target = new RenderTexture(1000, 700, 24); var pixel = new Texture2D(1, 1, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                camera.aspect = 1000f / 700; camera.targetTexture = target;
                var times = new double[7];
                for (int i = -3; i < times.Length; i++)
                {
                    long started = Stopwatch.GetTimestamp();
                    camera.Render(); RenderTexture.active = target;
                    pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); pixel.Apply();
                    if (i >= 0) times[i] = (Stopwatch.GetTimestamp() - started) * (1000.0 / Stopwatch.Frequency);
                }
                Array.Sort(times); return times[times.Length / 2];
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = previous;
                target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixel);
            }
        }
        private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
