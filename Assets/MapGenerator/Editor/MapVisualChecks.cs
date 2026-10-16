using System;
using System.IO;
using TacticalMaps.Construction;
using UnityEditor;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class MapVisualChecks
    {
        [MenuItem("Tools/Tactical Maps/Capture Surface Preview")]
        private static void SurfacePreview()
        {
            var builder = UnityEngine.Object.FindAnyObjectByType<MapBuilder>();
            var terrain = Array.Find(builder.GetComponentsInChildren<Terrain>(), t => t.name == "Terrain_Main");
            var size = terrain.terrainData.size;
            float x = terrain.transform.position.x + size.x * 0.25f, z = terrain.transform.position.z + size.z * 0.65f;
            var point = new Vector3(x, terrain.SampleHeight(new Vector3(x, 0, z)) + terrain.transform.position.y, z);
            var go = CameraAt(point, 85, new Vector3(0, 120, -100), (1 << terrain.gameObject.layer) | (1 << builder.GetComponentInChildren<MeshRenderer>().gameObject.layer));
            try { Capture(go.GetComponent<Camera>(), "Temp/surface-close.png", 1280, 800); }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [MenuItem("Tools/Tactical Maps/Verify Water Motion")]
        private static void WaterMotion()
        {
            try
            {
                if (EditorApplication.isPaused) throw new InvalidOperationException("Unpause the editor first.");
                var sceneView = SceneView.lastActiveSceneView;
                if (sceneView != null) { sceneView.sceneViewState.alwaysRefresh = true; sceneView.sceneViewState.alwaysRefresh = true; sceneView.Focus(); }
                EditorApplication.QueuePlayerLoopUpdate();
                var builder = UnityEngine.Object.FindAnyObjectByType<MapBuilder>();
                var terrain = Array.Find(builder.GetComponentsInChildren<Terrain>(), t => t.name == "Terrain_Main");
                var water = builder.GetComponentInChildren<MeshRenderer>();
                float sea = water.sharedMaterial.GetFloat("_WaterLevel");
                var size = terrain.terrainData.size;
                var point = terrain.transform.position + new Vector3(size.x * 0.5f, sea - terrain.transform.position.y, size.z * 0.5f);
                float best = float.MaxValue;
                for (int z = 1; z < 16; z++) for (int x = 1; x < 16; x++)
                {
                    float depth = sea - terrain.transform.position.y - terrain.terrainData.GetInterpolatedHeight(x / 16f, z / 16f);
                    if (depth < 5 || Mathf.Abs(depth - 18) >= best) continue;
                    best = Mathf.Abs(depth - 18);
                    point.x = terrain.transform.position.x + size.x * x / 16;
                    point.z = terrain.transform.position.z + size.z * z / 16;
                }
                if (best == float.MaxValue) throw new InvalidOperationException("Choose a coastal or water point first.");
                var go = CameraAt(point, 120, new Vector3(0, 180, -140), (1 << terrain.gameObject.layer) | (1 << water.gameObject.layer));
                var camera = go.GetComponent<Camera>();
                var first = Capture(camera, "Temp/water-motion-a.png", 640, 400);
                double started = EditorApplication.timeSinceStartup;
                EditorApplication.CallbackFunction next = null;
                next = () =>
                {
                    if (EditorApplication.timeSinceStartup - started < 0.8) return;
                    EditorApplication.update -= next;
                    try
                    {
                        var second = Capture(camera, "Temp/water-motion-b.png", 640, 400);
                        double difference = 0; int changed = 0;
                        for (int i = 0; i < first.Length; i++)
                        {
                            int delta = Mathf.Abs(first[i].r - second[i].r) + Mathf.Abs(first[i].g - second[i].g) + Mathf.Abs(first[i].b - second[i].b);
                            difference += delta; if (delta > 6) changed++;
                        }
                        difference /= first.Length * 3 * 255.0;
                        float fraction = (float)changed / first.Length;
                        if (difference < 0.002 || fraction < 0.05f) throw new Exception($"Water motion too subtle: average difference {difference:F4}, changed pixels {fraction:P1}.");
                        File.WriteAllText("Temp/water-motion-check.txt", $"PASS: fixed-camera {(EditorApplication.isPlaying ? "Play" : "Edit")} Mode frames, average RGB difference {difference:F4}, changed pixels {fraction:P1}, interval {EditorApplication.timeSinceStartup - started:F2}s.");
                    }
                    catch (Exception e) { File.WriteAllText("Temp/water-motion-check.txt", e.ToString()); Debug.LogException(e); }
                    finally { if (go != null) UnityEngine.Object.DestroyImmediate(go); }
                };
                EditorApplication.update += next;
            }
            catch (Exception e) { File.WriteAllText("Temp/water-motion-check.txt", e.ToString()); Debug.LogException(e); }
        }
        private static GameObject CameraAt(Vector3 point, float size, Vector3 offset, int layers)
        {
            var go = new GameObject("Map verification camera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = go.AddComponent<Camera>(); camera.enabled = false;
            camera.orthographic = true; camera.orthographicSize = size; camera.nearClipPlane = 5; camera.farClipPlane = 10000;
            camera.cullingMask = layers; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.14f, 0.23f, 0.27f);
            go.transform.position = point + offset; go.transform.LookAt(point);
            return go;
        }
        private static Color32[] Capture(Camera camera, string path, int width, int height)
        {
            var render = new RenderTexture(width, height, 24);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                camera.aspect = (float)width / height; camera.targetTexture = render; camera.Render();
                RenderTexture.active = render; texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG()); return texture.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous; camera.targetTexture = null; render.Release();
                UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(render);
            }
        }
    }
}
