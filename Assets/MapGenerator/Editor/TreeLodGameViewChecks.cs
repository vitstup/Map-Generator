using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TacticalMaps.Construction;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TacticalMaps.Editor
{
    // Native Game View, with end-camera callbacks proving that measured frames rendered.
    [InitializeOnLoad]
    internal static class TreeLodGameViewChecks
    {
        private const string Key = "TacticalMaps.TreeLodGameViewChecks";
        private static Camera camera;
        private static InstancedTreeRenderer trees;
        private static Vector3 centre;
        private static int stage, frame, rendered;
        private static float finalThreshold;
        private static bool motion;
        private static readonly List<double> times = new List<double>();
        private static StringBuilder report;
        private static readonly string[] Views = { "near", "canopy", "grove", "wide" };
        static TreeLodGameViewChecks() { EditorApplication.playModeStateChanged += Changed; }

        [MenuItem("Tools/Tactical Maps/Verify Tree LODs In Game View")]
        internal static void Run()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Start outside Play Mode.");
            Directory.CreateDirectory("Temp/TreeLodRevision");
            SessionState.SetBool(Key + ".background", Application.runInBackground);
            SessionState.SetInt(Key + ".vsync", QualitySettings.vSyncCount);
            SessionState.SetInt(Key + ".target", Application.targetFrameRate);
            SessionState.SetBool(Key,true); EditorApplication.isPlaying = true;
        }
        private static void Changed(PlayModeStateChange change)
        {
            if (!SessionState.GetBool(Key,false)) return;
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                try
                {
                    foreach (var workbench in UnityEngine.Object.FindObjectsByType<TacticalMaps.Testing.MapWorkbench>(FindObjectsSortMode.None)) workbench.enabled = false;
                    foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) canvas.enabled = false;
                    camera = Camera.main; trees = UnityEngine.Object.FindAnyObjectByType<InstancedTreeRenderer>();
                    foreach (var other in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) if (other != camera) other.enabled = false;
                    foreach (var component in camera.GetComponents<Behaviour>()) if (component.GetType().Name.Contains("Cinemachine")) component.enabled = false;
                    var terrain = UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).First(t => t.name == "Terrain_Main");
                    var data = terrain.terrainData;
                    int pine = Array.FindIndex(data.treePrototypes, p => p.prefab.name.Contains("Pine_02"));
                    var placements = data.treeInstances.Where(t => t.prototypeIndex == pine).ToArray();
                    if (placements.Length == 0) placements = data.treeInstances;
                    centre = terrain.transform.TransformPoint(Vector3.Scale(placements[placements.Length/2].position,data.size));
                    finalThreshold = new SerializedObject(trees).FindProperty("meshScreenHeight").floatValue;
                    Application.runInBackground = true; QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
                    report = new StringBuilder($"Native Editor Game View; same new meshes, earlier impostor threshold .13 versus final {finalThreshold.ToString(CultureInfo.InvariantCulture)}; 60 warmup + 180 rendered frames per state. Editor elapsed, not isolated GPU time.\n");
                    report.AppendLine(SystemInfo.graphicsDeviceName + "; " + SystemInfo.graphicsDeviceType + "; " + SystemInfo.renderingThreadingMode);
                    stage = 0; motion = false; Configure(); RenderPipelineManager.endCameraRendering += Rendered;
                    EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
                }
                catch (Exception e) { Stop("FAIL: " + e); }
            }
            if (change == PlayModeStateChange.EnteredEditMode)
            {
                RenderPipelineManager.endCameraRendering -= Rendered;
                Application.runInBackground = SessionState.GetBool(Key + ".background",false);
                QualitySettings.vSyncCount = SessionState.GetInt(Key + ".vsync",0);
                Application.targetFrameRate = SessionState.GetInt(Key + ".target",-1);
                SessionState.SetBool(Key,false);
            }
        }
        private static void Configure()
        {
            string view = Views[stage/3];
            camera.transform.position = centre + (view == "near" ? new Vector3(0,4,-22) : view == "canopy" ? new Vector3(0,14,-35) : view == "grove" ? new Vector3(0,85,-180) : new Vector3(0,500,-1000));
            camera.transform.LookAt(centre + (view == "near" || view == "canopy" ? Vector3.up*4 : Vector3.zero));
            camera.orthographic = false; camera.fieldOfView = 40;
            var serialized = new SerializedObject(trees); serialized.FindProperty("meshScreenHeight").floatValue = stage%3 == 0 ? .13f : finalThreshold; serialized.ApplyModifiedPropertiesWithoutUndo();
            times.Clear(); frame = -60; rendered = 0;
        }
        private static void Rendered(ScriptableRenderContext context, Camera view)
        {
            if (view != camera) return;
            try
            {
                rendered++;
                if (motion)
                {
                    float t = Mathf.PingPong(frame/120f,1);
                    camera.transform.position = centre + Vector3.Lerp(new Vector3(0,8,-22),new Vector3(0,100,-600),t);
                    camera.transform.LookAt(centre + Vector3.up*4);
                    if (frame%30 == 0) ScreenCapture.CaptureScreenshot("Temp/TreeLodRevision/motion-" + frame.ToString("D3") + ".png");
                    if (++frame > 240) Stop("PASS: all states rendered; 240 camera-motion frames crossed LOD transitions in both directions.");
                    return;
                }
                if (frame >= 0) times.Add(Time.unscaledDeltaTime*1000);
                if (++frame < 180) return;
                if (trees.SubmittedDraws == 0) throw new InvalidOperationException("No tree draws");
                times.Sort();
                string label = Views[stage/3] + (stage%3 == 0 ? "/earlier" : stage%3 == 1 ? "/final" : "/repeat");
                report.AppendLine($"{label}: {camera.pixelWidth}x{camera.pixelHeight}; median={times[times.Count/2].ToString("F3",CultureInfo.InvariantCulture)}ms, p95={times[(int)(times.Count*.95)].ToString("F3",CultureInfo.InvariantCulture)}ms; rendered={rendered}; mesh trees={trees.VisibleTrees}; triangles={trees.MeshTriangles}; draws={trees.SubmittedDraws}; lods={string.Join(",",trees.VisibleLods)}");
                File.WriteAllText("Temp/TreeLodRevision/GameView.txt",report.ToString());
                if (stage%3 == 1) ScreenCapture.CaptureScreenshot("Temp/TreeLodRevision/" + Views[stage/3] + ".png");
                if (++stage == Views.Length*3) { motion = true; frame = 0; return; }
                Configure();
            }
            catch (Exception e) { Stop("FAIL: " + e); }
        }
        private static void Stop(string result)
        {
            RenderPipelineManager.endCameraRendering -= Rendered;
            File.AppendAllText("Temp/TreeLodRevision/GameView.txt",result + "\n"); EditorApplication.isPlaying = false;
        }
    }
}
