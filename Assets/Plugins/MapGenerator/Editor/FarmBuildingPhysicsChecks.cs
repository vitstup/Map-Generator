using System;
using System.IO;
using System.Linq;
using System.Text;
using TacticalMaps.Construction;
using TacticalMaps.Generation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class FarmBuildingPhysicsChecks
    {
        [MenuItem("Tools/Tactical Maps/Verify House Colliders")]
        internal static void Verify()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("House physics checks");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var report = new StringBuilder();
            try
            {
                var library = AssetDatabase.LoadAssetAtPath<AgricultureLibrary>(AgricultureSetup.Folder + "/AgricultureLibrary.asset");
                var main = new Vector2(100, 80); var surroundings = new Vector2(300, 240);
                Check(FarmBuildingPhysics.Level(new Vector2(50, 40), main, surroundings) == 0, "Main boundary belongs to Main");
                Check(FarmBuildingPhysics.Level(new Vector2(51, 0), main, surroundings) == 1, "Each house centre selects its own ring");
                Check(FarmBuildingPhysics.Level(new Vector2(150, 120), main, surroundings) == 1, "Surroundings boundary");
                Check(FarmBuildingPhysics.Level(new Vector2(151, 0), main, surroundings) == 2, "Horizon boundary");
                foreach (var prefab in library.houses)
                {
                    var shapes = prefab.GetComponentsInChildren<Collider>(true);
                    Check(shapes.Length >= 3 && shapes.Length <= 6 && shapes.All(c => c is BoxCollider), "Three to six primitive boxes: " + prefab.name);
                    Check(prefab.GetComponents<Collider>().Length == 0 && shapes.All(c => c.transform.IsChildOf(prefab.transform.Find("Colliders"))), "Separate Colliders subtree");
                    Check(shapes.Count(c => c.name.StartsWith("Roof_", StringComparison.Ordinal) && Quaternion.Angle(c.transform.localRotation, Quaternion.identity) > 10) >= 2, "At least two sloped roof boxes");
                    VerifySolidCoverage(prefab, report);
                    for (int level = 0; level < 3; level++) foreach (bool enabled in new[] { false, true }) foreach (bool distant in new[] { false, true })
                    {
                        var instance = distant ? new GameObject("Distant house") : UnityEngine.Object.Instantiate(prefab);
                        instance.transform.SetParent(root.transform, false);
                        instance.transform.localPosition = new Vector3(173, 29, -241);
                        instance.transform.localRotation = Quaternion.Euler(0, 37, 0); instance.transform.localScale = Vector3.one * .65f;
                        if (distant) UnityEngine.Object.Instantiate(prefab.transform.Find("Distant"), instance.transform, false);
                        try
                        {
                            FarmBuildingPhysics.Configure(instance, prefab, level, 9, enabled);
                            bool expected = level == 0 || (level == 1 && enabled);
                            var actual = instance.GetComponentsInChildren<Collider>(true);
                            Check(actual.Length == (expected ? shapes.Length : 0), "Zone policy independent of render LOD");
                            Check(actual.All(c => c.gameObject.layer == 9), "Requested collision layer");
                            FarmBuildingPhysics.Configure(instance, prefab, level, 10, enabled);
                            Check(instance.GetComponentsInChildren<Collider>(true).Length == actual.Length, "Reconfiguration has no duplicate shapes");
                            Check(instance.GetComponentsInChildren<Collider>(true).All(c => c.gameObject.layer == 10), "Changing layer updates every shape");
                            if (expected)
                            {
                                var subtree = instance.transform.Find("Colliders");
                                Check(subtree.GetComponentsInChildren<Renderer>().Length == 0 && subtree.GetComponentsInChildren<Rigidbody>().Length == 0, "Physics-only subtree");
                                Physics.SyncTransforms();
                                foreach (var c in instance.GetComponentsInChildren<BoxCollider>())
                                {
                                    var point = c.transform.TransformPoint(c.center); var up = c.transform.up;
                                    Check(c.Raycast(new Ray(point + up * 20, -up), out _, 40), "Scaled and rotated primitive raycast");
                                }
                            }
                        }
                        finally { UnityEngine.Object.DestroyImmediate(instance); }
                    }
                    report.AppendLine(prefab.name + ": " + shapes.Length + " boxes; Main/Surroundings/Horizon x toggle x detailed/distant PASS");
                }
                var input = AgricultureChecks.Input(); ulong original = input.Fingerprint();
                input.BuildingLayer = 9; Check(original != input.Fingerprint(), "Layer invalidates generator cache");
                original = input.Fingerprint(); input.SurroundingsBuildingColliders = true;
                Check(original != input.Fingerprint(), "Ring toggle invalidates generator cache");
                var data = input.Render(System.Threading.CancellationToken.None);
                Check(data.BuildingLayer == 9 && data.SurroundingsBuildingColliders, "Generated result captures physics settings");
                report.AppendLine("PASS: primitive-only compounds, roof slopes, all zone policies/LODs, layers, scale/rotation, raycasts, no duplicates, generator cache and settings snapshot.");
            }
            catch (Exception e) { report.AppendLine("FAIL: " + e); throw; }
            finally
            {
                Directory.CreateDirectory("Temp/HousePhysics"); File.WriteAllText("Temp/HousePhysics/checks.txt", report.ToString());
                UnityEngine.Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(scene);
            }
        }
        private static void VerifySolidCoverage(GameObject prefab, StringBuilder report)
        {
            int variant = int.Parse(prefab.name.Substring("Farmhouse_".Length));
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Synty/PolygonSamuraiEmpire/Prefabs/Buildings/Presets/Building_House_ Small_" + (variant + 1).ToString("00") + ".prefab");
            Vector3 pivot = -prefab.transform.Find("Detailed").localPosition;
            var shapes = prefab.GetComponentsInChildren<BoxCollider>(true);
            var body = prefab.transform.Find("Colliders/Body").GetComponent<BoxCollider>();
            var platform = prefab.transform.Find("Colliders/Platform").GetComponent<BoxCollider>();
            Check(platform.size.y < .6f, "Platform excludes upright floor modules: " + prefab.name);
            Bounds? gables = null, stairs = null;
            foreach (var filter in source.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh; if (mesh == null) continue;
                bool gable = mesh.name.StartsWith("SM_Bld_Roof_End_", StringComparison.Ordinal);
                bool stair = mesh.name.Contains("Steps") || mesh.name.Contains("Stairs");
                if (!gable && !stair) continue;
                var matrix = Matrix4x4.Translate(-pivot) * source.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                var bounds = new Bounds(matrix.MultiplyPoint3x4(mesh.bounds.center), Vector3.zero);
                for (int i = 0; i < 8; i++) bounds.Encapsulate(matrix.MultiplyPoint3x4(mesh.bounds.center + Vector3.Scale(mesh.bounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                if (gable) { if (gables.HasValue) { var b = gables.Value; b.Encapsulate(bounds); gables = b; } else gables = bounds; }
                if (stair) { if (stairs.HasValue) { var b = stairs.Value; b.Encapsulate(bounds); stairs = b; } else stairs = bounds; }
            }
            bool Contains(BoxCollider collider, Vector3 point)
            {
                var local = collider.transform.InverseTransformPoint(prefab.transform.TransformPoint(point)) - collider.center;
                var half = collider.size * .5f + Vector3.one * .002f;
                return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
            }
            if (stairs.HasValue)
            {
                Check(prefab.transform.Find("Colliders/Steps") != null, "Dedicated stair collider including Stairs modules: " + prefab.name);
                var empty = stairs.Value.center; empty.y = stairs.Value.max.y + .35f;
                Check(!Contains(body, empty) && !shapes.Any(c => Contains(c, empty)), "Clear air above stairs: " + prefab.name);
                var stair = prefab.transform.Find("Colliders/Steps").GetComponent<BoxCollider>();
                Check(Quaternion.Angle(stair.transform.localRotation, Quaternion.identity) > 10, "Stairs use a sloped ramp instead of a tall bounding box");
                var up = stair.transform.up;
                var rise = new Vector3(-up.x, 0, -up.z).normalized;
                float run = Mathf.Abs(rise.x) > Mathf.Abs(rise.z) ? stairs.Value.size.x : stairs.Value.size.z;
                var lowerHeadroom = stairs.Value.center - rise * run * .4f;
                lowerHeadroom.y = stairs.Value.min.y + stairs.Value.size.y * .1f + .35f;
                Check(!shapes.Any(c => Contains(c, lowerHeadroom)), "Clear air above the lower treads: " + prefab.name);
            }
            Check(gables.HasValue, "Source has identifiable gables");
            var g = gables.Value;
            var roof = prefab.transform.Find("Colliders/Roof_0");
            bool alongZ = Mathf.Abs(roof.forward.z) > Mathf.Abs(roof.forward.x);
            int across = alongZ ? 0 : 2, along = alongZ ? 2 : 0;
            int samples = 0;
            // Independently sample the visible triangular gable bounds, rather than the generated boxes.
            foreach (float end in new[] { g.min[along] + .025f, Mathf.Lerp(g.min[along], g.max[along], .25f), g.center[along], Mathf.Lerp(g.min[along], g.max[along], .75f), g.max[along] - .025f })
                for (int y = 1; y <= 9; y++) for (int x = -4; x <= 4; x++)
                {
                    float t = y / 10f; var point = g.center;
                    point.y = Mathf.Lerp(g.min.y, g.max.y, t);
                    point[across] = g.center[across] + g.extents[across] * (1 - t) * (x / 4f) * .92f;
                    point[along] = end;
                    Check(shapes.Any(c => Contains(c, point)), "Solid gable coverage " + prefab.name + " at " + point);
                    samples++;
                }
            report.AppendLine(prefab.name + ": " + samples + " solid gable samples; clear stair headroom; thin platform PASS");
        }
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
