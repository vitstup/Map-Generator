#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TacticalMaps.Grass;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TacticalMaps.Construction
{
    // Opt-in diagnostic. Cascades/atlas are selected before the first render, never mid-frame.
    internal sealed class ShadowPerformanceProbe : MonoBehaviour
    {
        private static readonly string[] Arguments = Environment.GetCommandLineArgs();
        private static string Option(string key, string fallback)
        {
            int i = Array.IndexOf(Arguments,key);
            return i >= 0 && i + 1 < Arguments.Length ? Arguments[i+1] : fallback;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Application.isEditor || !Arguments.Contains("-shadow-benchmark")) return;
            Application.runInBackground=true;
            new GameObject("Shadow performance probe").AddComponent<ShadowPerformanceProbe>();
        }
        private IEnumerator Start()
        {
            // Keep the standalone matrix uninstrumented. Use ShadowGameViewChecks in a
            // non-Graphics-Jobs Editor session for supported GPU pass measurements.
            int sampleCount=int.Parse(Option("-shadow-samples","600"));
            string label=Option("-shadow-label","ablation");
            string folder=Path.Combine(Environment.CurrentDirectory,"Logs","ShadowBench",label);
            Directory.CreateDirectory(folder);
            var pipeline=(UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
            // This process owns its pipeline instance; the source asset on disk is never saved.
            pipeline.shadowCascadeCount=int.Parse(Option("-shadow-cascades","4"));
            pipeline.shadowDistance=float.Parse(Option("-shadow-distance","50"),CultureInfo.InvariantCulture);
            pipeline.mainLightShadowmapResolution=int.Parse(Option("-shadow-atlas","2048"));
            pipeline.cascadeBorder=float.Parse(Option("-shadow-border","0.2"),CultureInfo.InvariantCulture);
            var light=FindObjectsByType<Light>(FindObjectsSortMode.None).First(l=>l.enabled&&l.type==LightType.Directional);
            light.shadowStrength=float.Parse(Option("-shadow-strength","0.85"),CultureInfo.InvariantCulture);
            light.shadows=Arguments.Contains("-shadow-hard")?LightShadows.Hard:LightShadows.Soft;
            var trees=FindAnyObjectByType<InstancedTreeRenderer>();
            trees.CastShadowsForChecks=true;
            var grass=FindAnyObjectByType<GrassSystem>();
            var terrains=FindObjectsByType<Terrain>(FindObjectsSortMode.None);
            var terrain=terrains.First(t=>t.name=="Terrain_Main");
            var modes=terrains.Select(t=>t.shadowCastingMode).ToArray();
            var meshes=FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
            var meshModes=meshes.Select(t=>t.shadowCastingMode).ToArray();
            var source=Camera.main;
            foreach(var workbench in FindObjectsByType<TacticalMaps.Testing.MapWorkbench>(FindObjectsSortMode.None))workbench.enabled=false;
            foreach(var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))canvas.enabled=false;
            foreach(var existing in FindObjectsByType<Camera>(FindObjectsSortMode.None))existing.enabled=false;
            var camera=new GameObject("Shadow benchmark camera").AddComponent<Camera>();camera.CopyFrom(source);camera.enabled=false;
            var cameraData=camera.GetUniversalAdditionalCameraData();
            camera.orthographic=false;camera.fieldOfView=40;camera.aspect=1600f/900;
            var target=new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32);camera.targetTexture=target;
            bool explicitRender=Arguments.Contains("-shadow-render-request");
            bool screenRender=Arguments.Contains("-shadow-visible");
            if(screenRender)camera.targetTexture=null;
            camera.enabled=!explicitRender;
            int renderedFrames=0;
            Action<ScriptableRenderContext,Camera> countRender=(_,rendered)=>{if(rendered==camera)renderedFrames++;};
            RenderPipelineManager.endCameraRendering+=countRender;
            var renderRequest=new UniversalRenderPipeline.SingleCameraRequest { destination=target };
            // Let URP initialize, then use its explicit render-request API.
            yield return null;
            if(explicitRender&&!RenderPipeline.SupportsRenderRequest(camera,renderRequest))
            {File.WriteAllText(Path.Combine(folder,"report.txt"),"FAIL: URP single-camera render requests unsupported.");Application.Quit(1);yield break;}
            QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;Application.runInBackground=true;
            grass.MatureForChecks=true;
            grass.FreezeWindForChecks=true;
            var placements=terrain.terrainData.treeInstances;
            Vector3 centre=terrain.transform.TransformPoint(Vector3.Scale(placements[placements.Length/2].position,terrain.terrainData.size));
            var report=new StringBuilder();
            report.AppendLine($"{SystemInfo.graphicsDeviceName}; {SystemInfo.graphicsDeviceType}; 1600x900; cascades={pipeline.shadowCascadeCount}; distance={pipeline.shadowDistance}; atlas={pipeline.mainLightShadowmapResolution}; light={light.shadows}; supports={pipeline.supportsMainLightShadows}");
            report.AppendLine($"Development player; 90 warmup + {sampleCount} measured real frames; explicit render requests={explicitRender}. Uninstrumented frame elapsed includes the player loop, not isolated GPU time. End-camera callbacks verify rendering every measured frame.");
            bool quality=Arguments.Contains("-shadow-quality");
            string[] states=quality?new[]{"off","reference","all","no-trees","no-trees-terrain","off-repeat"}:new[]{"off","reference","all","no-trees","no-terrain","no-other","no-trees-terrain","no-grass-receive","no-tree-receive","no-terrain-receive","no-all-receive","terrain-on","off-repeat"};
            var farm=meshes.Where(m=>m.enabled&&m.shadowCastingMode!=ShadowCastingMode.Off&&m.bounds.extents.y>2&&m.bounds.extents.y<20).OrderBy(m=>(m.bounds.center-centre).sqrMagnitude).FirstOrDefault();
            foreach(string view in farm==null?new[]{"near","grove","wide"}:new[]{"near","grove","wide","farm"})
            foreach(string state in states)
            {
                camera.transform.position=centre+(view=="near"?new Vector3(0,4,-22):view=="grove"?new Vector3(0,85,-180):new Vector3(0,500,-1000));
                camera.transform.LookAt(centre+(view=="near"?Vector3.up*7:Vector3.zero));
                if(view=="farm"){camera.transform.position=farm.bounds.center+new Vector3(0,18,-35);camera.transform.LookAt(farm.bounds.center);}
                cameraData.renderShadows=!state.StartsWith("off");
                trees.SuppressCastingForChecks=state=="no-trees"||state=="no-trees-terrain";
                Shader.SetGlobalFloat("_ShadowReferenceSampling",state=="reference"?1:0);
                Shader.SetGlobalFloat("_GrassSkipShadowChecks",state=="no-grass-receive"||state=="no-all-receive"?1:0);
                Shader.SetGlobalFloat("_TreeSkipShadowChecks",state=="no-tree-receive"||state=="no-all-receive"?1:0);
                Shader.SetGlobalFloat("_TerrainSkipShadowChecks",state=="no-terrain-receive"||state=="no-all-receive"?1:0);
                for(int i=0;i<terrains.Length;i++)terrains[i].shadowCastingMode=state=="no-terrain"||state=="no-trees-terrain"?ShadowCastingMode.Off:state=="terrain-on"||(quality&&state=="no-trees")?ShadowCastingMode.On:modes[i];
                for(int i=0;i<meshes.Length;i++)meshes[i].shadowCastingMode=state=="no-other"?ShadowCastingMode.Off:meshModes[i];
                var cpu=new List<double>();
                int firstFrame=Time.frameCount;
                int firstRender=renderedFrames;
                for(int frame=-90;frame<sampleCount;frame++)
                {
                    if(explicitRender)RenderPipeline.SubmitRenderRequest(camera,renderRequest);
                    yield return new WaitForEndOfFrame();
                    if(frame>=0)cpu.Add(Time.unscaledDeltaTime*1000);
                }
                if(Time.frameCount-firstFrame<sampleCount+80 || renderedFrames-firstRender<sampleCount+80 || trees.SubmittedDraws==0 || grass.SubmittedDraws==0)
                {File.WriteAllText(Path.Combine(folder,"report.txt"),report+"FAIL: game frames or vegetation draws missing.");Application.Quit(1);yield break;}
                report.AppendLine($"{view}/{state}: frame median={Percentile(cpu,.5)} p95={Percentile(cpu,.95)} rendered={renderedFrames-firstRender} trees={trees.SubmittedDraws} grass={grass.SubmittedDraws}");
                if(view!="wide")
                {
                    var previous=RenderTexture.active;RenderTexture.active=screenRender?null:target;
                    Texture2D image;
                    if(screenRender)image=ScreenCapture.CaptureScreenshotAsTexture();
                    else{image=new Texture2D(1600,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1600,900),0,0);image.Apply();}
                    File.WriteAllBytes(Path.Combine(folder,view+"-"+state+".png"),image.EncodeToPNG());Destroy(image);RenderTexture.active=previous;
                }
                File.WriteAllText(Path.Combine(folder,"report.txt"),report.ToString());
                yield return null;
            }
            report.AppendLine("PASS: every state advanced game frames and submitted grass and trees. Frame elapsed is not isolated GPU time.");
            RenderPipelineManager.endCameraRendering-=countRender;
            File.WriteAllText(Path.Combine(folder,"report.txt"),report.ToString());
            Application.Quit(0);
        }
        private static string Percentile(List<double> samples,double fraction)
        {
            if(samples.Count==0)return "unavailable";
            samples.Sort();return samples[Math.Min(samples.Count-1,(int)(samples.Count*fraction))].ToString("F3",CultureInfo.InvariantCulture)+"ms";
        }
    }
}
#endif
