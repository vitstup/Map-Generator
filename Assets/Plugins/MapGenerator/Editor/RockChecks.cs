using System;
using System.Linq;
using System.Threading;
using TacticalMaps.Construction;
using TacticalMaps.Generation;
using UnityEditor;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class RockChecks
    {
        [MenuItem("Tools/Tactical Maps/Verify Rock Placement")]
        private static void Run()
        {
            var root = new GameObject("RockChecks");
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var textures = new Texture2D[3]; MapData map = null;
            try
            {
                var generator = root.AddComponent<MapGenerator>(); var builder = root.AddComponent<MapBuilder>();
                RockSetup.Configure(generator, builder); ConstructionSetup.Configure(builder);
                SurfaceLibrarySetup.Configure(generator, true);
                var sizes = new[] { new Vector2(2048, 1536), new Vector2(8096, 6096), new Vector2(20000, 15000) };
                for (int level = 0; level < 3; level++)
                {
                    const int n = 257; var raw = new ushort[n * n];
                    for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
                    {
                        float wx = ((float)x / (n - 1) - 0.5f) * sizes[level].x;
                        float factor = Mathf.Pow(1.6f, level);
                        raw[z * n + x] = (ushort)Mathf.RoundToInt((650 + 100 * factor * (float)Math.Tanh(wx / (60 * factor))) / 2000 * 65535);
                    }
                    textures[level] = new Texture2D(n, n, TextureFormat.R16, false, true);
                    textures[level].SetPixelData(raw, 0); textures[level].Apply();
                }
                map = new MapData(textures, sizes, 2000, 20, 4000, new Vector2Int(42, 71));
                var rocks = root.GetComponent<RockPlacementGenerator>();
                var first = rocks.Generate(map, CancellationToken.None); var second = rocks.Generate(map, CancellationToken.None);
                Check(first.Placements.Count > 0, "Steep escarpment receives cliff sections");
                Check(first.Placements.Count == second.Placements.Count, "Deterministic count");
                var counts = new int[3];
                for (int i = 0; i < first.Placements.Count; i++)
                {
                    var a = first.Placements[i]; var b = second.Placements[i]; counts[a.Level]++;
                    Check(a.Position == b.Position && a.Rotation == b.Rotation && a.Scale == b.Scale && a.Prefab == b.Prefab, "Deterministic transforms");
                    Check(a.Scale.x > 0 && a.Scale.y > 0 && a.Scale.z > 0 && !float.IsNaN(a.Position.y), "Finite positive transforms");
                    var downhill = a.Rotation * Vector3.forward; Check(downhill.x < -0.9f, "Exposed face points downhill");
                    var meshSize = first.Prefabs[a.Prefab].GetComponentInChildren<MeshFilter>().sharedMesh.bounds.size;
                    Check(meshSize.y * a.Scale.y > 150, "Massif covers the full height difference, without the former 50m ceiling");
                    Check(meshSize.x * a.Scale.x > 140, "Mountain-scale sections, not small rocks");
                    if (a.Level > 0) Check(Mathf.Abs(a.Position.x) > sizes[a.Level - 1].x * 0.5f || Mathf.Abs(a.Position.z) > sizes[a.Level - 1].y * 0.5f, "No duplicate objects in hidden interiors");
                }
                Check(counts.All(c => c > 0), "All three terrain levels receive cliffs");
                bool canceled = false;
                try { rocks.Generate(map, new CancellationToken(true)); } catch (OperationCanceledException) { canceled = true; }
                Check(canceled, "Canceled generation stops");
                var config = new SerializedObject(rocks); config.FindProperty("analysisSpacing").floatValue = 48; config.ApplyModifiedPropertiesWithoutUndo();
                var coarser = rocks.Generate(map, CancellationToken.None);
                float firstHeight = Tallest(first), coarserHeight = Tallest(coarser);
                Check(coarserHeight / firstHeight > 0.8f && coarserHeight / firstHeight < 1.25f, "Section dimensions do not follow the analysis grid spacing");
                config.FindProperty("analysisSpacing").floatValue = 32; config.ApplyModifiedPropertiesWithoutUndo();
                var surface = root.GetComponent<BiomeGenerator>().Generate(textures, sizes, 2000, 20, map.SourcePoint, 50, CancellationToken.None);
                var withRocks = new MapData(textures, sizes, 2000, 20, 4000, map.SourcePoint, surface: surface, rocks: first);
                builder.Build(withRocks);
                var built = new SerializedObject(builder).FindProperty("builtRoot").objectReferenceValue as Transform;
                Check(built.Find("Rocks").childCount == first.Placements.Count, "Dedicated Rocks parent");
                Check(built.Find("Rocks").GetComponentsInChildren<Collider>(true).All(c => c.enabled && !c.isTrigger), "Solid colliders at every level");
                var borrowedMesh = first.Prefabs[0].GetComponentInChildren<MeshFilter>().sharedMesh;
                var borrowedMaterial = first.Prefabs[0].GetComponentInChildren<Renderer>().sharedMaterial;
                builder.Build(withRocks);
                Check(borrowedMesh != null && borrowedMaterial != null, "Rebuild preserves prefab mesh and material assets");
                CaptureExample(root, scene);
                // A steep submerged field and a flat field must both remain empty.
                var underwater = new MapData(textures, sizes, 2000, 1700, 4000, map.SourcePoint);
                Check(rocks.Generate(underwater, CancellationToken.None).Placements.Count == 0, "No submerged cliffs");
                foreach (var texture in textures)
                {
                    var raw = new ushort[texture.width * texture.height];
                    for (int i = 0; i < raw.Length; i++) raw[i] = 20000;
                    texture.SetPixelData(raw, 0); texture.Apply();
                }
                Check(rocks.Generate(map, CancellationToken.None).Placements.Count == 0, "Flat terrain remains empty");
                var message = "PASS: massif placement; three levels=" + string.Join(",", counts) + "; full slope heights; large section widths; grid-independent dimensions; deterministic transforms; downhill orientation; water/flat exclusion; cancellation; Rocks parent; colliders; rebuild preserves borrowed resources.";
                System.IO.File.WriteAllText("Temp/rock-checks.txt", message); Debug.Log(message);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                if (map != null) map.Release(); else foreach (var t in textures) if (t != null) UnityEngine.Object.DestroyImmediate(t);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            }
        }
        private static void Check(bool condition, string message)
        { if (!condition) throw new Exception("Rock check failed: " + message); }

        private static float Tallest(RockData rocks)
        {
            float height = 0;
            foreach (var p in rocks.Placements) height = Mathf.Max(height, rocks.Prefabs[p.Prefab].GetComponentInChildren<MeshFilter>().sharedMesh.bounds.size.y * p.Scale.y);
            return height;
        }

        private static void CaptureExample(GameObject root, UnityEngine.SceneManagement.Scene scene)
        {
            var lightObject = new GameObject("Preview light"); lightObject.transform.SetParent(root.transform, false);
            var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2;
            light.transform.rotation = Quaternion.Euler(45, -35, 0);
            var cameraObject = new GameObject("Preview camera"); cameraObject.transform.SetParent(root.transform, false);
            var camera = cameraObject.AddComponent<Camera>(); camera.scene = scene;
            camera.transform.position = new Vector3(-620, 1000, -730);
            camera.transform.LookAt(new Vector3(0, 650, 0));
            camera.fieldOfView = 48; camera.nearClipPlane = 0.5f; camera.farClipPlane = 20000;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.55f, 0.7f, 0.8f);
            var target = new RenderTexture(1200, 800, 24); var previous = RenderTexture.active;
            var image = new Texture2D(1200, 800, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1200, 800), 0, 0); image.Apply();
                System.IO.File.WriteAllBytes("Temp/rock-check-preview.png", image.EncodeToPNG());
            }
            finally { camera.targetTexture = null; RenderTexture.active = previous; target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(image); }
        }
    }
}
