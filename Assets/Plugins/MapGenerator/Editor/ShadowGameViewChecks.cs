using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TacticalMaps.Construction;
using TacticalMaps.Grass;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TacticalMaps.Editor
{
    // Real Game View rendering: no Camera.Render, no readbacks in measured frames.
    [InitializeOnLoad]
    internal static class ShadowGameViewChecks
    {
        private const string Key="TacticalMaps.ShadowGameViewChecks";
        private static Camera camera;
        private static InstancedTreeRenderer trees;
        private static Terrain[] terrains;
        private static ShadowCastingMode[] terrainModes;
        private static Vector3 centre;
        private static Vector3 farmCentre;
        private static bool collectGpu, motion;
        private static int stage, frame, lastFrame;
        private static readonly string[] States={"off","reference","all","no-trees","no-terrain","no-grass-receive","no-all-receive","off-repeat"};
        private static readonly string[] Views={"near","canopy","grove","farm"};
        private static readonly string[] Names={"Grass.LitNormals","MainLightShadow","DrawOpaqueObjects"};
        private static Recorder[] recorders;
        private static List<double>[] gpu;
        private static readonly List<double> elapsed=new();
        private static StringBuilder report;
        static ShadowGameViewChecks(){EditorApplication.playModeStateChanged+=Changed;}

        [MenuItem("Tools/Tactical Maps/Shadows/Verify in Game View")]
        internal static void Run()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Start outside Play Mode.");
            if(SessionState.GetBool(Key,false))throw new InvalidOperationException("Shadow check already pending.");
            var pipeline=(UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
            SessionState.SetString(Key+".pipeline",EditorJsonUtility.ToJson(pipeline));
            SessionState.SetString(Key+".path",AssetDatabase.GetAssetPath(pipeline));
            SessionState.SetBool(Key+".profiler",Profiler.enabled);
            SessionState.SetBool(Key+".gpu",Profiler.GetAreaEnabled(ProfilerArea.GPU));
            SessionState.SetBool(Key+".background",Application.runInBackground);
            SessionState.SetInt(Key+".vsync",QualitySettings.vSyncCount);
            SessionState.SetInt(Key+".target",Application.targetFrameRate);
            var game=EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
            SessionState.SetBool(Key+".maximized",game.maximized);game.maximized=true;game.Focus();
            var data=new SerializedObject(pipeline);data.FindProperty("m_MainLightShadowsSupported").boolValue=true;data.FindProperty("m_AnyShadowsSupported").boolValue=true;data.ApplyModifiedPropertiesWithoutUndo();
            SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;
        }
        private static void Changed(PlayModeStateChange change)
        {
            if(!SessionState.GetBool(Key,false))return;
            if(change==PlayModeStateChange.EnteredPlayMode)
            {
                try
                {
                    foreach(var workbench in UnityEngine.Object.FindObjectsByType<TacticalMaps.Testing.MapWorkbench>(FindObjectsSortMode.None))workbench.enabled=false;
                    foreach(var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))canvas.enabled=false;
                    camera=Camera.main;trees=UnityEngine.Object.FindAnyObjectByType<InstancedTreeRenderer>();
                    terrains=UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);terrainModes=terrains.Select(t=>t.shadowCastingMode).ToArray();
                    foreach(var other in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))if(other!=camera)other.enabled=false;
                    foreach(var component in camera.GetComponents<Behaviour>())if(component.GetType().Name.Contains("Cinemachine"))component.enabled=false;
                    var grass=UnityEngine.Object.FindAnyObjectByType<GrassSystem>();grass.MatureForChecks=true;grass.FreezeWindForChecks=true;
                    var terrain=terrains.First(t=>t.name=="Terrain_Main");var p=terrain.terrainData.treeInstances[terrain.terrainData.treeInstanceCount/2].position;
                    centre=terrain.transform.TransformPoint(Vector3.Scale(p,terrain.terrainData.size));
                    var farm=UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(m=>m.enabled&&m.shadowCastingMode!=ShadowCastingMode.Off&&m.bounds.extents.y>2&&m.bounds.extents.y<20).OrderBy(m=>(m.bounds.center-centre).sqrMagnitude).FirstOrDefault();
                    farmCentre=farm!=null?farm.bounds.center:centre;
                    Application.runInBackground=true;QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
                    var mode=SystemInfo.renderingThreadingMode;
                    collectGpu=mode==RenderingThreadingMode.MultiThreaded||mode==RenderingThreadingMode.Direct||mode==RenderingThreadingMode.SingleThreaded;
                    // Unity 6.3 GPU profiling is unsupported with Graphics Jobs, even if a
                    // Recorder happens to return nonzero timestamps. Never interpret them.
                    Profiler.SetAreaEnabled(ProfilerArea.GPU,collectGpu);Profiler.enabled=collectGpu;
                    recorders=Names.Select(Recorder.Get).ToArray();
                    report=new StringBuilder("Editor Game View; normal camera rendering; 60 warmup + 180 measured advancing frames per state. CPU elapsed includes Editor overhead.\n");
                    report.AppendLine(SystemInfo.graphicsDeviceName+"; "+SystemInfo.graphicsDeviceType+"; "+mode+"; GPU profiling="+collectGpu);
                    Directory.CreateDirectory("Logs/ShadowGameView");stage=0;motion=false;lastFrame=Time.frameCount;
                    Configure();EditorApplication.update+=Tick;
                }
                catch(Exception e){Debug.LogException(e);Stop("FAIL: "+e);}
            }
            if(change==PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.update-=Tick;
                foreach(string name in Names)Recorder.Get(name).enabled=false;
                Shader.SetGlobalFloat("_ShadowReferenceSampling",0);Shader.SetGlobalFloat("_GrassSkipShadowChecks",0);Shader.SetGlobalFloat("_TreeSkipShadowChecks",0);Shader.SetGlobalFloat("_TerrainSkipShadowChecks",0);
                var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(SessionState.GetString(Key+".path",""));
                EditorJsonUtility.FromJsonOverwrite(SessionState.GetString(Key+".pipeline",""),pipeline);EditorUtility.SetDirty(pipeline);AssetDatabase.SaveAssets();
                Profiler.enabled=SessionState.GetBool(Key+".profiler",false);Profiler.SetAreaEnabled(ProfilerArea.GPU,SessionState.GetBool(Key+".gpu",false));
                Application.runInBackground=SessionState.GetBool(Key+".background",false);
                QualitySettings.vSyncCount=SessionState.GetInt(Key+".vsync",0);Application.targetFrameRate=SessionState.GetInt(Key+".target",-1);
                EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).maximized=SessionState.GetBool(Key+".maximized",false);
                SessionState.SetBool(Key,false);
            }
        }
        private static void Configure()
        {
            string state=States[stage%States.Length],view=Views[stage/States.Length];
            camera.transform.position=centre+(view=="near"?new Vector3(0,4,-22):view=="canopy"?new Vector3(0,14,-35):new Vector3(0,85,-180));
            camera.transform.LookAt(centre+(view=="grove"?Vector3.zero:Vector3.up*7));
            if(view=="farm"){camera.transform.position=farmCentre+new Vector3(0,18,-35);camera.transform.LookAt(farmCentre);}
            camera.GetUniversalAdditionalCameraData().renderShadows=!state.StartsWith("off");
            trees.SuppressCastingForChecks=state=="no-trees";
            for(int i=0;i<terrains.Length;i++)terrains[i].shadowCastingMode=state=="no-terrain"?ShadowCastingMode.Off:terrainModes[i];
            Shader.SetGlobalFloat("_ShadowReferenceSampling",state=="reference"?1:0);
            Shader.SetGlobalFloat("_GrassSkipShadowChecks",state=="no-grass-receive"||state=="no-all-receive"?1:0);
            Shader.SetGlobalFloat("_TreeSkipShadowChecks",state=="no-all-receive"?1:0);
            Shader.SetGlobalFloat("_TerrainSkipShadowChecks",state=="no-all-receive"?1:0);
            elapsed.Clear();gpu=Names.Select(_=>new List<double>()).ToArray();frame=-60;
        }
        private static void Tick()
        {
            if(!EditorApplication.isPlaying||camera==null){EditorApplication.update-=Tick;return;}
            if(Time.frameCount==lastFrame)return;
            lastFrame=Time.frameCount;
            try
            {
                if(motion)
                {
                    camera.transform.position=centre+Vector3.Lerp(new Vector3(0,8,-22),new Vector3(0,80,-340),frame/120f);camera.transform.LookAt(centre+Vector3.up*7);
                    if(frame%30==0)ScreenCapture.CaptureScreenshot("Logs/ShadowGameView/motion-"+frame.ToString("D3")+".png");
                    if(++frame>120)Stop("PASS: all states rendered; 120 advancing camera-motion frames captured with tree casting disabled.");
                    return;
                }
                for(int i=0;collectGpu&&i<recorders.Length;i++)
                {
                    if(!recorders[i].isValid)recorders[i]=Recorder.Get(Names[i]);recorders[i].enabled=true;
                    if(frame>=0&&recorders[i].gpuSampleBlockCount>0)gpu[i].Add(recorders[i].gpuElapsedNanoseconds/1e6);
                }
                if(frame>=0)elapsed.Add(Time.unscaledDeltaTime*1000);
                if(++frame<180)return;
                string state=States[stage%States.Length],view=Views[stage/States.Length];
                report.AppendLine($"{view}/{state}: {camera.pixelWidth}x{camera.pixelHeight}; Editor elapsed={Median(elapsed)}; trees={trees.SubmittedDraws}; GPU "+string.Join("; ",Names.Select((n,i)=>n+"="+Median(gpu[i])+" samples="+gpu[i].Count+" distinct="+gpu[i].Distinct().Count())));
                File.WriteAllText("Logs/ShadowGameView/report.txt",report.ToString());
                if(state=="off"||state=="reference"||state=="all"||state=="no-trees")ScreenCapture.CaptureScreenshot("Logs/ShadowGameView/"+view+"-"+state+".png");
                if(++stage>=Views.Length*States.Length)
                {
                    motion=true;frame=0;camera.GetUniversalAdditionalCameraData().renderShadows=true;trees.SuppressCastingForChecks=true;
                    Shader.SetGlobalFloat("_ShadowReferenceSampling",0);Shader.SetGlobalFloat("_GrassSkipShadowChecks",0);Shader.SetGlobalFloat("_TreeSkipShadowChecks",0);Shader.SetGlobalFloat("_TerrainSkipShadowChecks",0);
                    return;
                }
                Configure();
            }
            catch(Exception e){Debug.LogException(e);Stop("FAIL: "+e);}
        }
        private static void Stop(string result)
        {
            EditorApplication.update-=Tick;
            if(recorders!=null)foreach(var recorder in recorders)recorder.enabled=false;
            Shader.SetGlobalFloat("_ShadowReferenceSampling",0);Shader.SetGlobalFloat("_GrassSkipShadowChecks",0);Shader.SetGlobalFloat("_TreeSkipShadowChecks",0);Shader.SetGlobalFloat("_TerrainSkipShadowChecks",0);
            Directory.CreateDirectory("Logs/ShadowGameView");File.AppendAllText("Logs/ShadowGameView/report.txt",result+"\n");EditorApplication.isPlaying=false;
        }
        private static string Median(List<double> values)
        {if(values.Count==0)return "unavailable";values.Sort();return values[values.Count/2].ToString("F3",CultureInfo.InvariantCulture)+"ms";}
    }
}
