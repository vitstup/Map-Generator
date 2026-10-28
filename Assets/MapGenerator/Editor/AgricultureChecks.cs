using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using TacticalMaps.Construction;
using TacticalMaps.Generation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace TacticalMaps.Editor
{
    internal static class AgricultureChecks
    {
        [MenuItem("Tools/Tactical Maps/Verify Agriculture")]
        internal static void Verify()
        {
            Directory.CreateDirectory("Logs/Agriculture"); var report = new StringBuilder();
            try
            {
                var input = Input(); var first = input.Render(CancellationToken.None); var second = input.Render(CancellationToken.None);
                Require(first.Fields.Length > 20 && first.Farms.Length > 0, "Populated flat agricultural landscape");
                Require(first.Farms.All(f=>f.Buildings.Length>=1 && f.Buildings.Length<=2) && first.Farms.Any(f=>f.embedded) && first.Farms.Any(f=>!f.embedded),"Mixed compact field and standalone yards");
                Require(first.Fields.SequenceEqual(second.Fields) && first.Farms.Select(f => JsonUtility.ToJson(f)).SequenceEqual(second.Farms.Select(f => JsonUtility.ToJson(f))), "Deterministic districts, fields and farms");
                Require(first.Fields.All(f => f.crop != 3) && first.Fields.Any(f => f.crop == 0) && first.Fields.Any(f=>f.crop==1), "Dry rice and fallow fields");
                Require(first.Fields.Max(f => f.halfSize.x * f.halfSize.y) > first.Fields.Min(f => f.halfSize.x * f.halfSize.y) * 2, "Substantial parcel size variation");
                for (int l = 0; l < 3; l++) Require(first.Masks[l].SequenceEqual(second.Masks[l]), "Deterministic masks on terrain " + l);
                input.Roads = new RoadData(new[] { new RoadSegment(new Vector2(-1024, 0), new Vector2(1024, 0), 8, 1) }, input.Sizes[2], 2, 2);
                var roads = input.Render(CancellationToken.None);
                foreach (var f in roads.Fields) Require(!input.Roads.Blocks(f.center, f.halfSize.magnitude + 2), "Whole field excludes roads");
                foreach (var f in roads.Farms) Require(!input.Roads.Blocks(f.center, 24), "Farmyard excludes roads");
                foreach (var track in roads.Tracks)
                {
                    for (int s = 0; s <= 20; s++) Require(!roads.Fields.Any(f => f.Contains(track.Point(s / 20f), 1) && !f.ExcludesCrops(track.Point(s / 20f),-1)), "Access tracks avoid fields");
                    Require(roads.Blocks(track.Point(.5f)), "Access track exclusion query");
                }
                var coverage = new TacticalMaps.Grass.GrassCoverage(new[] { Enumerable.Repeat(new Color32(255, 0, 0, 255), 256 * 256).ToArray(), Enumerable.Repeat(new Color32(255, 0, 0, 255), 256 * 256).ToArray(), Enumerable.Repeat(new Color32(255, 0, 0, 255), 256 * 256).ToArray() }, new[] { 256, 256, 256 });
                coverage.ExcludeAgriculture(first, input.Sizes); Require(coverage.Pixels[0].Any(c => c.a == 0) && coverage.Pixels[0].Any(c => c.a == 255), "Fields exclude grass while meadows remain");
                foreach (var f in first.Fields.Take(20)) Require(first.Blocks(f.center), "Object exclusion query");
                var cts = new CancellationTokenSource(); cts.Cancel(); bool canceled = false;
                try { input.Render(cts.Token); } catch (OperationCanceledException) { canceled = true; } finally { cts.Dispose(); }
                Require(canceled, "Cancellation");
                input.Density = Enumerable.Repeat((ushort)100, 65 * 65).ToArray(); Require(input.Render(CancellationToken.None).Fields.Length == 0, "Near-zero density is rejected before placement");
                input.Density = new ushort[65 * 65]; Require(input.Render(CancellationToken.None).Fields.Length == 0, "Zero density");
                input = Input(); for (int l = 0; l < 3; l++) Array.Clear(input.Pixels[l], 0, input.Pixels[l].Length);
                Require(input.Render(CancellationToken.None).Fields.Length == 0, "Water exclusion");
                input = Input(); input.Slope = 1;
                for (int l = 0; l < 3; l++) for (int z = 0; z < 65; z++) for (int x = 0; x < 65; x++) input.Pixels[l][z * 65 + x] = (ushort)(x * 1000);
                Require(input.Render(CancellationToken.None).Fields.Length == 0, "Steep terrain exclusion");
                report.AppendLine($"PASS: deterministic fields/farms/masks, water and slope rejection, road footprint exclusion, grass exclusion, cancellation, zero density. Flat landscape: {first.Fields.Length} fields / {first.Farms.Length} farms.");
                foreach (string name in new[] { "TacticalMaps/Agriculture/Rice", "TacticalMaps/Agriculture/Simple", "TacticalMaps/Agriculture/Water", "TacticalMaps/Terrain/MatteStochastic" })
                {
                    var shader = Shader.Find(name); Require(shader != null && shader.isSupported, name);
                    foreach (var m in ShaderUtil.GetShaderMessages(shader)) Require(m.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, m.message);
                }
                report.AppendLine("PASS: agriculture and terrain shaders supported, no reported shader compiler errors.");
                var library = AssetDatabase.LoadAssetAtPath<AgricultureLibrary>(AgricultureSetup.Folder + "/AgricultureLibrary.asset");
                Require(library != null && library.houses.Length == 5, "Prepared house library");
                foreach (var house in library.houses)
                {
                    Require(house.GetComponent<LODGroup>()?.GetLODs().Length == 3, "Three individual house LODs");
                    var lods = house.GetComponent<LODGroup>().GetLODs();
                    Require(lods[2].renderers.Sum(r => r.GetComponent<MeshFilter>().sharedMesh.triangles.Length) < lods[0].renderers.Sum(r => r.GetComponent<MeshFilter>().sharedMesh.triangles.Length), "Individual far mesh is simplified");
                    Require(house.GetComponentsInChildren<Collider>().Length == 1, "One simplified house collider");
                }
                report.AppendLine("PASS: five house variants, three individual LODs, one collider per detailed house.");
                File.WriteAllText("Logs/Agriculture/checks.txt", report.ToString()); UnityEngine.Debug.Log(report.ToString());
            }
            catch (Exception e) { File.WriteAllText("Logs/Agriculture/checks.txt", report + "FAIL: " + e); throw; }
        }
        internal static AgricultureInput Input()
        {
            var input = new AgricultureInput { Density = Enumerable.Repeat((ushort)65535, 65 * 65).ToArray(), SourceWidth = 65, SourceHeight = 65, SourcePoint = new Vector2Int(32, 32), Radius = 20,
                Height = 256, Sea = 10, Coverage = 1, District = 600, Slope = 9, FarmProbability = 1, Limit = 2400, Seed = 1467 };
            for (int l = 0; l < 3; l++) { input.Pixels[l] = Enumerable.Repeat((ushort)16000, 65 * 65).ToArray(); input.Widths[l] = input.Heights[l] = 65; input.Sizes[l] = Vector2.one * (1024 << l); }
            return input;
        }
        private static void Require(bool condition, string description) { if (!condition) throw new InvalidOperationException("Agriculture check failed: " + description); }

        [MenuItem("Tools/Tactical Maps/Capture And Profile Agriculture")]
        internal static void Capture()
        {
            Directory.CreateDirectory("Logs/Agriculture");
            var map = Object.FindAnyObjectByType<AgricultureMap>(); var crops = Object.FindAnyObjectByType<CropRenderer>();
            if (map == null || crops == null || map.fields == null) throw new InvalidOperationException("Rebuild the agriculture preview first.");
            var ground = map.GetComponentsInChildren<Terrain>(); var center = ground[0].transform.position + ground[0].terrainData.size * .5f; center.y = ground[0].transform.position.y;
            var main = ground.First(t => t.name == "Terrain_Main");
            var candidates = map.fields.Where(f => Mathf.Abs(f.center.x) < main.terrainData.size.x * .43f && Mathf.Abs(f.center.y) < main.terrainData.size.z * .43f && f.crop != 0).ToArray();
            Require(candidates.Length > 0, "Fields exist on the main terrain for visual verification");
            var chosen = candidates.OrderBy(f => f.center.sqrMagnitude).First();
            var focus = AgricultureBuilder.Ground(chosen.center, ground, center);
            var cameraObject = new GameObject("AgricultureVerificationCamera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.AddComponent<Camera>();
            if (Camera.main != null) camera.CopyFrom(Camera.main);
            camera.enabled = false; camera.fieldOfView = 55; camera.nearClipPlane = .2f; camera.farClipPlane = 23000;
            var extra = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            if (Camera.main != null)
            {
                var source = Camera.main.GetUniversalAdditionalCameraData(); extra.renderPostProcessing = source.renderPostProcessing;
                extra.renderShadows = source.renderShadows; extra.antialiasing = source.antialiasing;
            }
            var target = new RenderTexture(1280, 720, 24); target.Create(); camera.targetTexture = target;
            var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var grass = Object.FindAnyObjectByType<TacticalMaps.Grass.GrassSystem>(); bool freeze = grass != null && grass.FreezeWindForChecks;
            if (grass != null) { grass.FreezeWindForChecks = true; grass.MatureForChecks = true; }
            var report = new StringBuilder();
            try
            {
                var names = new[] { "close", "rts", "overview", "horizon" };
                var offsets = new[] { new Vector3(0, 3, -9), new Vector3(65, 80, -115), new Vector3(100, 450, -550), new Vector3(0, 30, -150) };
                for (int v = 0; v < names.Length; v++)
                {
                    camera.transform.position = focus + offsets[v]; camera.transform.LookAt(focus);
                    for (int i = 0; i < 12; i++) { camera.Render(); AsyncGPUReadback.Request(target, 0).WaitForCompletion(); }
                    if (grass != null) grass.VerifyPreparedDispatchForChecks(camera);
                    crops.SuppressForChecks = false; var with = Image("Logs/Agriculture/" + names[v] + ".png"); double withTime = Time();
                    crops.SuppressForChecks = true; var without = Image("Logs/Agriculture/" + names[v] + "-without-crops.png"); double withoutTime = Time();
                    crops.SuppressForChecks = false;
                    // Isolate the whole agriculture rendering cost. Existing grass masks stay fixed,
                    // so this comparison does not claim the cost of a separately generated natural map.
                    var agricultureGroup = map.transform.Find("Agriculture").gameObject;
                    agricultureGroup.SetActive(false); foreach (var terrain in ground) terrain.materialTemplate.SetFloat("_FarmEnabled", 0);
                    double baseline;
                    try { camera.Render(); baseline = Time(); }
                    finally { agricultureGroup.SetActive(true); foreach (var terrain in ground) terrain.materialTemplate.SetFloat("_FarmEnabled", 1); }
                    double diff = 0; for (int i = 0; i < with.Length; i++) diff += Math.Abs(with[i].r - without[i].r) + Math.Abs(with[i].g - without[i].g) + Math.Abs(with[i].b - without[i].b);
                    diff /= with.Length * 3;
                    report.AppendLine($"{names[v]}: crops enabled {withTime:F2} ms, crops disabled {withoutTime:F2} ms, farm geometry/surfaces disabled (shared roads retained) {baseline:F2} ms; pixel difference {diff:F4}/255");
                    if (v < 2) Require(diff > .02, "Visible rice geometry in " + names[v]);
                }
                var house = map.GetComponentsInChildren<LODGroup>().OrderBy(g => (g.transform.position - focus).sqrMagnitude).FirstOrDefault();
                if (house != null)
                {
                    var p = house.transform.position + Vector3.up * 3; camera.transform.position = p + new Vector3(20, 15, -25); camera.transform.LookAt(p); Image("Logs/Agriculture/farmhouse.png");
                }
                report.AppendLine($"Fields {map.fields.Length}; farms {map.farms.Length}; access tracks {map.tracks?.Length}; resident crop patches {crops.PatchCount}; crop buffers {crops.BufferBytes / 1048576f:F2} MiB; focus {focus}; crop {chosen.crop}");
                report.AppendLine("Mipmapped 1024-square far masks plus sparse full-precision distance tiles. Farm comparisons keep grass masks, prepared terrain and shared roads fixed.");
                report.AppendLine("Synchronized offscreen Editor render/readback timings include CPU and GPU work; these are not gameplay FPS or isolated GPU timings.");
                crops.enabled = false; Require(crops.BufferBytes <= crops.PatchCount * 16L + map.fields.Length * 32L, "GPU cleanup after disable"); crops.enabled = true;
                report.AppendLine("PASS: crop image differences, near/RTS/distant captures, valid grass preparation dispatch and GPU cleanup.");
                File.WriteAllText("Logs/Agriculture/rendering.txt", report.ToString()); UnityEngine.Debug.Log(report.ToString());
                Color32[] Image(string path)
                {
                    camera.Render(); var previous = RenderTexture.active;
                    try { RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply(); File.WriteAllBytes(path, pixels.EncodeToPNG()); return pixels.GetPixels32(); }
                    finally { RenderTexture.active = previous; }
                }
                double Time()
                {
                    var clock = Stopwatch.StartNew(); for (int i = 0; i < 4; i++) camera.Render(); AsyncGPUReadback.Request(target, 0).WaitForCompletion(); return clock.Elapsed.TotalMilliseconds / 4;
                }
            }
            catch (Exception e) { File.WriteAllText("Logs/Agriculture/rendering.txt", report + "FAIL: " + e); throw; }
            finally
            {
                crops.SuppressForChecks = false; if (grass != null) { grass.FreezeWindForChecks = freeze; grass.MatureForChecks = false; }
                camera.targetTexture = null; target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(pixels); Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
