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
    internal static class FarmFencePhysicsChecks
    {
        [MenuItem("Tools/Tactical Maps/Verify Fence Colliders")]
        internal static void Verify()
        {
            var scene = EditorSceneManager.NewPreviewScene(); var root = new GameObject("Fence physics checks");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var terrains = new Terrain[3]; var data = new TerrainData[3]; var report = new StringBuilder();
            var center = new Vector3(83, 17, -211); var main = new Vector2(200, 160); var around = new Vector2(800, 640);
            var library = AssetDatabase.LoadAssetAtPath<AgricultureLibrary>(AgricultureSetup.Folder + "/AgricultureLibrary.asset");
            var owned = root.AddComponent<AgricultureMap>();
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    var go = new GameObject("Terrain_" + i, typeof(Terrain)); go.transform.SetParent(root.transform, false);
                    data[i] = new TerrainData { heightmapResolution = 65, size = new Vector3(main.x * (1 << (i * 2)), 100, main.y * (1 << (i * 2))) };
                    terrains[i] = go.GetComponent<Terrain>(); terrains[i].terrainData = data[i];
                    go.transform.position = center - new Vector3(data[i].size.x * .5f, 0, data[i].size.z * .5f);
                }
                Heights((x, z) => 20);
                for (int style = 0; style < 2; style++)
                {
                    var farm = Farm(Vector2.zero, style); farm.angle = .37f;
                    int previousMeshes = owned.Resources.OfType<Mesh>().Count();
                    FarmFenceBuilder.Build(new[] { farm }, library, terrains, center, main, root.transform, owned, around, 10, false);
                    var boxes = Boxes(); var profile = FarmFenceColliders.Profile(library.fences[style]);
                    Check(boxes.Length == 5, "Exactly five continuous lines for a yard with one gate");
                    Check(boxes.All(c => c.gameObject.layer == 10 && Mathf.Abs(c.size.y - profile.size.y) < .004f && Mathf.Abs(c.size.z - profile.size.z) < .004f), "Prefab-derived height and depth for style " + style);
                    Check(boxes.All(c => c.size.x > 4 * library.fences[style].GetComponent<FenceSection>().length), "Merge many mesh sections per collider");
                    int sections = Coverage(farm, boxes);
                    long trianglesPerSection = 0;
                    foreach (var filter in library.fences[style].GetComponentsInChildren<MeshFilter>())
                        for (int sub = 0; sub < filter.sharedMesh.subMeshCount; sub++) trianglesPerSection += filter.sharedMesh.GetIndexCount(sub) / 3;
                    long renderedTriangles = owned.Resources.OfType<Mesh>().Skip(previousMeshes).Sum(m => (long)m.GetIndexCount(0) / 3);
                    Check(renderedTriangles == sections * trianglesPerSection, "Line physics preserves all original fence mesh triangles");
                    var axis = new Vector3(Mathf.Cos(farm.angle), 0, Mathf.Sin(farm.angle));
                    var from = center + new Vector3(farm.center.x, 20 + profile.center.y - .045f, farm.center.y);
                    Check(!boxes.Any(c => c.Raycast(new Ray(from, axis), out _, farm.yardHalfSize.x + 10)), "Gate remains physically open");
                    var group = root.transform.Find("FenceColliders");
                    Check(group.GetComponentsInChildren<Renderer>().Length == 0 && group.GetComponentsInChildren<Rigidbody>().Length == 0 && group.GetComponentsInChildren<MonoBehaviour>().Length == 0, "Empty physics objects only");
                    Check(boxes.All(c => c.gameObject.GetComponents<Component>().Length == 2), "Only Transform and BoxCollider per line");
                    report.AppendLine("Style " + style + ": " + sections + " sections -> " + boxes.Length + " boxes; gate, rotation, dimensions, coverage PASS");
                    FarmFenceBuilder.BuildColliders(new[] { farm }, library, terrains, center, main, around, root.transform, 11, false);
                    Check(Boxes().Length == 5 && Boxes().All(c => c.gameObject.layer == 11), "Rebuild replaces physics and applies the selected layer");
                }
                var open = Farm(Vector2.zero, 2);
                Build(open, false); Check(Boxes().Length == 4, "Open yard side has no collider");
                var edge = Farm(new Vector2(95, 0), 0);
                Build(edge, false); Check(Boxes().Length == 3, "Main-only physics clips lines at the boundary");
                foreach (var c in Boxes()) for (int i = 0; i < 8; i++)
                {
                    var p = c.transform.TransformPoint(c.center + Vector3.Scale(c.size * .5f, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))) - center;
                    Check(p.x <= main.x * .5f + .005f, "No physical line extends into disabled surroundings");
                }
                Build(edge, true); Check(Boxes().Length == 5, "Surroundings option restores the full yard");
                var outside = Farm(new Vector2(250, 0), 0);
                FarmFenceBuilder.BuildColliders(new[] { outside }, library, terrains, center, main, new Vector2(400, 320), root.transform, 10, true);
                Check(Boxes().Length == 0, "Horizon has no physics even when its fence is rendered");

                Heights((x, z) => 20 + .04f * x + .02f * z);
                var slope = Farm(Vector2.zero, 0); Build(slope, false);
                Check(Boxes().Length == 5, "Straight slopes still merge into whole lines"); Coverage(slope, Boxes());
                Check(Boxes().Any(c => Quaternion.Angle(c.transform.rotation, Quaternion.Euler(0, c.transform.eulerAngles.y, 0)) > .5f), "Boxes follow terrain pitch");
                Heights((x, z) => x < 0 ? 20 : 30);
                Build(slope, false); Check(Boxes().Length > 5, "Terrain discontinuities split physics lines"); Coverage(slope, Boxes());
                var input = AgricultureChecks.Input(); ulong key = input.Fingerprint(); input.FenceLayer = 10;
                Check(key != input.Fingerprint(), "Fence layer invalidates generator cache"); key = input.Fingerprint(); input.SurroundingsFenceColliders = true;
                Check(key != input.Fingerprint(), "Fence zone option invalidates cache");
                var generated = input.Render(System.Threading.CancellationToken.None);
                Check(generated.FenceLayer == 10 && generated.SurroundingsFenceColliders, "Generated result captures fence settings");
                report.AppendLine("PASS: line aggregation, gate/open-side gaps, prefab dimensions, layers, rebuild, zone clipping, slopes and terrain breaks, generator settings.");
            }
            catch (Exception e) { report.AppendLine("FAIL: " + e); throw; }
            finally
            {
                Directory.CreateDirectory("Temp/FencePhysics"); File.WriteAllText("Temp/FencePhysics/checks.txt", report.ToString());
                foreach (var mesh in owned.Resources.OfType<Mesh>()) UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(root); foreach (var item in data) if (item != null) UnityEngine.Object.DestroyImmediate(item);
                EditorSceneManager.ClosePreviewScene(scene);
            }

            BoxCollider[] Boxes() => root.transform.Find("FenceColliders").GetComponentsInChildren<BoxCollider>();
            void Build(Farmstead farm, bool surroundings) => FarmFenceBuilder.BuildColliders(new[] { farm }, library, terrains, center, main, around, root.transform, 10, surroundings);
            void Heights(Func<float, float, float> height)
            {
                for (int i = 0; i < 3; i++)
                {
                    var h = new float[65, 65];
                    for (int z = 0; z < 65; z++) for (int x = 0; x < 65; x++) h[z, x] = Mathf.Clamp01(height((x / 64f - .5f) * data[i].size.x, (z / 64f - .5f) * data[i].size.z) / 100);
                    data[i].SetHeights(0, 0, h);
                }
            }
            int Coverage(Farmstead farm, BoxCollider[] boxes)
            {
                int sections = 0;
                FarmFenceBuilder.VisitSections(new[] { farm }, library, terrains, center, main, p =>
                {
                    sections++; var bounds = FarmFenceColliders.Profile(p.Prefab);
                    for (int i = 0; i < 8; i++)
                    {
                        var point = p.Matrix.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                        Check(boxes.Any(c => Contains(c, point)), "Every rendered section corner is covered by its line collider");
                    }
                }, () => { });
                return sections;
            }
        }
        private static Farmstead Farm(Vector2 center, int style) => new Farmstead { center = center, yardHalfSize = new Vector2(28, 22), gateHalfWidth = 4.5f, fenceStyle = style, buildings = Array.Empty<FarmBuilding>() };
        private static bool Contains(BoxCollider c, Vector3 point)
        {
            var p = c.transform.InverseTransformPoint(point) - c.center; var half = c.size * .5f + Vector3.one * .004f;
            return Mathf.Abs(p.x) <= half.x && Mathf.Abs(p.y) <= half.y && Mathf.Abs(p.z) <= half.z;
        }
        private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
