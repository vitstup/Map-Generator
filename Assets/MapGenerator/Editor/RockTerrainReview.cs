using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TacticalMaps.Construction;
using TacticalMaps.Generation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class RockTerrainReview
    {
        private static bool running;

        [MenuItem("Tools/Tactical Maps/Capture Rock Terrain Cases")]
        private static async void Run()
        {
            if (running) return;
            running = true;
            const string report = "Temp/rock-terrain-review.txt";
            File.WriteAllText(report, "Running terrain cases...\n");
            try
            {
                var source = UnityEngine.Object.FindAnyObjectByType<MapGenerator>();
                var points = new[] { new Vector2Int(2424, 1816), new Vector2Int(2424, 1816), new Vector2Int(616, 256), new Vector2Int(2108, 2204), new Vector2Int(2684, 2140) };
                var names = new[] { "mountains", "mountains-smooth", "coast", "coast-other", "plain" };
                var slopes = new[] { 40f, 40f, 35f, 35f, 12f };
                var smoothing = new[] { 1f, 3f, 1f, 1f, 3f };
                for (int test = 0; test < names.Length; test++)
                {
                    var scene = EditorSceneManager.NewPreviewScene();
                    var root = new GameObject("Rock terrain review");
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
                    MapData map = null;
                    try
                    {
                        var clone = UnityEngine.Object.Instantiate(source.gameObject, root.transform);
                        var generator = clone.GetComponent<MapGenerator>();
                        var settings = generator.Settings; settings.MaxSlope = slopes[test]; settings.SmoothingPixels = smoothing[test];
                        foreach (var level in settings.Levels) level.Resolution = new Vector2Int(513, 513);
                        generator.Configure(settings);
                        SurfaceLibrarySetup.Configure(generator, true);
                        var builder = root.AddComponent<MapBuilder>(); ConstructionSetup.Configure(builder); RockSetup.Configure(generator, builder);
                        var rockConfig = new SerializedObject(generator.GetComponent<RockPlacementGenerator>());
                        rockConfig.FindProperty("minimumRelief").floatValue = 80;
                        rockConfig.FindProperty("minimumSlope").floatValue = 20;
                        rockConfig.ApplyModifiedPropertiesWithoutUndo();
                        map = await generator.GenerateAsync(points[test], CancellationToken.None);
                        builder.Build(map);
                        // Terrain's GPU uploads must settle before its offscreen appearance is judged.
                        await SettleTerrain();
                        var sun = new GameObject("Review light"); sun.transform.SetParent(root.transform, false);
                        var light = sun.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2;
                        light.transform.rotation = Quaternion.Euler(48, -35, 0);
                        var terrain = root.GetComponentsInChildren<Terrain>().First(t => t.name == "Terrain_Main");
                        var centre = terrain.transform.position + terrain.terrainData.size * 0.5f;
                        centre.y = terrain.terrainData.GetInterpolatedHeight(0.5f, 0.5f);
                        var cameraObject = MapVisualChecks.CameraAt(centre, 1450, new Vector3(-800, 1800, -1700), -1);
                        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
                        cameraObject.transform.SetParent(root.transform, true);
                        var camera = cameraObject.GetComponent<Camera>(); camera.scene = scene; camera.farClipPlane = 40000;
                        MapVisualChecks.Capture(camera, "Temp/cliff-" + names[test] + "-overview.png", 1500, 1000);

                        var rocks = builder.GetComponentsInChildren<Transform>().First(t => t.name == "Rocks");
                        int hero = -1; float tallest = 0;
                        var counts = new int[3];
                        for (int i = 0; i < map.Rocks.Placements.Count; i++)
                        {
                            var p = map.Rocks.Placements[i]; counts[p.Level]++;
                            float h = rocks.GetChild(i).GetComponentInChildren<Renderer>().bounds.size.y;
                            if (p.Level == 0 && h > tallest) { tallest = h; hero = i; }
                        }
                        // Also inspect surrounding mountains when the playable core is deliberately smooth.
                        if (hero < 0 && rocks.childCount > 0) hero = 0;
                        if (hero >= 0)
                        {
                            var instance = rocks.GetChild(hero); var bounds = instance.GetComponentInChildren<Renderer>().bounds;
                            var target = bounds.center; float distance = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                            camera.orthographicSize = distance * 0.75f;
                            camera.transform.position = target + instance.forward * distance * 1.5f + Vector3.up * distance * 0.7f + instance.right * distance * 0.35f;
                            camera.transform.LookAt(target);
                            MapVisualChecks.Capture(camera, "Temp/cliff-" + names[test] + "-close.png", 1500, 1000);
                        }
                        if (names[test] == "plain" && counts[0] != 0) throw new Exception("The plain received mountain masses.");
                        if (test == 0) SaveReviewScene(root, builder, camera, scene);
                        float maximumSlope = 0;
                        for (int z = 0; z < 50; z++) for (int x = 0; x < 50; x++) maximumSlope = Mathf.Max(maximumSlope, terrain.terrainData.GetSteepness(x / 49f, z / 49f));
                        File.AppendAllText(report, names[test] + " source=" + points[test] + " maxSlope=" + slopes[test] + " smoothing=" + smoothing[test] + " actualSlope=" + maximumSlope.ToString("F1") + " sections=" + string.Join(",", counts) + " tallestMain=" + tallest.ToString("F1") + "m\n");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(root); map?.Release(); EditorSceneManager.ClosePreviewScene(scene); }
                }
                File.AppendAllText(report, "PASS: real mountain, coastal and plain source crops; main scene and camera unchanged.\n");
                Debug.Log("Rock terrain review completed.");
            }
            catch (Exception e) { File.AppendAllText(report, e + "\n"); Debug.LogException(e); }
            finally { running = false; }
        }

        private static Task SettleTerrain()
        {
            var completion = new TaskCompletionSource<bool>(); int frames = 0;
            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                EditorApplication.QueuePlayerLoopUpdate();
                if (++frames < 4) return;
                EditorApplication.update -= tick; completion.SetResult(true);
            };
            EditorApplication.update += tick;
            return completion.Task;
        }

        private static void SaveReviewScene(GameObject root, MapBuilder builder, Camera camera, UnityEngine.SceneManagement.Scene preview)
        {
            const string folder = "Assets/MapGenerator/Generated/RockReview";
            Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            ConstructionSetup.SavePreviewResources(builder, folder, true);
            File.Copy("Temp/cliff-mountains-overview.png", folder + "/Overview.png", true);
            if (File.Exists("Temp/cliff-mountains-close.png")) File.Copy("Temp/cliff-mountains-close.png", folder + "/Close.png", true);
            AssetDatabase.SaveAssets();
            var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var review = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, review);
                camera.gameObject.hideFlags = HideFlags.None;
                camera.enabled = true; camera.tag = "MainCamera";
                EditorSceneManager.SaveScene(review, folder + "/RockReview.unity");
            }
            finally
            {
                camera.enabled = false;
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, preview);
                EditorSceneManager.CloseScene(review, true);
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(active);
            }
        }
    }
}
