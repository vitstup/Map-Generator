using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using TacticalMaps.Grass;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TacticalMaps.Editor
{
    internal static class GrassCardChecks
    {
        [MenuItem("Tools/Tactical Maps/Verify Distant Grass Groups")]
        internal static void Verify()
        {
            var system=UnityEngine.Object.FindAnyObjectByType<GrassSystem>();
            if(system==null||system.CurrentMap==null) throw new InvalidOperationException("Configure grass first.");
            var report=new StringBuilder();
            var settings=new SerializedObject(system);int debug=settings.FindProperty("debugView").intValue;
            bool enabled=system.enabled;
            GameObject cameraObject=null;RenderTexture target=null;Texture2D pixels=null;
            try
            {
                if(!system.Library.CardsValid) throw new InvalidOperationException("Bake distant groups first.");
                foreach(var message in ShaderUtil.GetShaderMessages(system.Library.CardsMaterial.shader))
                    if(message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) throw new InvalidOperationException(message.message);
                system.enabled=false;system.enabled=true;
                system.MatureForChecks=true;system.FreezeWindForChecks=true;
                var ground=GrassOrbitCapture.FindMeadow(system.CurrentMap);
                report.AppendLine("Meadow: "+ground);
                cameraObject=new GameObject("DistantGrassCheckCamera") {hideFlags=HideFlags.HideAndDontSave};
                var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.fieldOfView=55;camera.nearClipPlane=.15f;camera.farClipPlane=23000;
                target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;
                pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
                foreach(var view in new[]{"close","rts","maxLow","maxOblique","maxTop","wide"})
                {
                    File.WriteAllText("Temp/grass-card-checks-progress.txt",view);
                    Vector3 offset=view=="close"?new Vector3(0,2.2f,-6):view=="rts"?new Vector3(0,35,-55):
                        view=="maxLow"?new Vector3(0,13.073f,-149.429f):view=="maxOblique"?new Vector3(0,75,-129.904f):
                        view=="maxTop"?new Vector3(0,149.429f,-13.073f):new Vector3(0,260,-380);
                    camera.transform.position=ground+offset;camera.transform.LookAt(ground);
                    system.FocusForChecks=ground;SetDebug(0);Warm();
                    var after=Capture("Temp/grass-card-"+view+"-after.png");
                    var counts=system.ReadDrawCountsForChecks(camera);
                    int submitted=system.SubmittedDraws;
                    if((view=="maxLow"||view=="wide")&&counts[3]==0) throw new InvalidOperationException("No distant groups in "+view);
                    double added=Measure();
                    system.SuppressCardsForChecks=true;
                    var before=Capture("Temp/grass-card-"+view+"-before.png");
                    double previous=Measure();system.SuppressCardsForChecks=false;
                    report.AppendLine($"{view}: detailed={counts[0]}, simplified={counts[1]}, orange={counts[2]}, cards={counts[3]}, image difference={Difference(after,before):F4}/255; full={added:F2} ms, previous={previous:F2} ms");
                    report.AppendLine($"  buffers={system.BufferBytes/1048576f:F2} MiB; submitted draws={submitted}");
                    if(view=="maxLow"||view=="maxOblique"||view=="maxTop") {SetDebug(2);Warm();Capture("Temp/grass-card-"+view+"-lod.png");}
                }
                system.enabled=false;
                if(system.BufferBytes!=0) throw new InvalidOperationException("Buffers survived disable.");
                report.AppendLine("PASS: new geometry renders, shader compiles, buffers released. Timings include synchronized Editor rendering/readback; not isolated GPU time or gameplay FPS.");

                void SetDebug(int value) {settings.Update();settings.FindProperty("debugView").intValue=value;settings.ApplyModifiedPropertiesWithoutUndo();}
                void Warm() {for(int i=0;i<64;i++){camera.Render();AsyncGPUReadback.Request(target,0).WaitForCompletion();}}
                Color32[] Capture(string path)
                {
                    camera.Render();var active=RenderTexture.active;
                    try {RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG());return pixels.GetPixels32();}
                    finally {RenderTexture.active=active;}
                }
                double Measure()
                {
                    var clock=Stopwatch.StartNew();for(int i=0;i<16;i++)camera.Render();AsyncGPUReadback.Request(target,0).WaitForCompletion();
                    return clock.Elapsed.TotalMilliseconds/16;
                }
            }
            catch(Exception e) {report.AppendLine("FAIL: "+e);UnityEngine.Debug.LogException(e);}
            finally
            {
                system.SuppressCardsForChecks=false;system.MatureForChecks=false;system.FreezeWindForChecks=false;system.FocusForChecks=null;
                settings.Update();settings.FindProperty("debugView").intValue=debug;settings.ApplyModifiedPropertiesWithoutUndo();
                system.enabled=enabled;
                if(cameraObject!=null)UnityEngine.Object.DestroyImmediate(cameraObject);
                if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}
                if(pixels!=null)UnityEngine.Object.DestroyImmediate(pixels);
                File.WriteAllText("Temp/grass-card-checks.txt",report.ToString());UnityEngine.Debug.Log(report.ToString());
            }
        }
        private static double Difference(Color32[] a,Color32[] b)
        {double sum=0;for(int i=0;i<a.Length;i++)sum+=Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b);return sum/(a.Length*3);}
    }
}
