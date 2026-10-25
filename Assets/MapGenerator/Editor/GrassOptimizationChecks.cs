using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using TacticalMaps.Grass;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace TacticalMaps.Editor
{
    internal static class GrassOptimizationChecks
    {
        [MenuItem("Tools/Tactical Maps/Verify Grass Optimization")]
        internal static void Verify()
        {
            const string folder="Logs/GrassOptimization/Verified";
            Directory.CreateDirectory(folder);
            var system=UnityEngine.Object.FindAnyObjectByType<GrassSystem>();
            if(system==null||system.CurrentMap==null)throw new InvalidOperationException("Built meadow required.");
            bool reference=system.ReferenceRenderingForChecks,frozen=system.FreezeWindForChecks,mature=system.MatureForChecks,flowers=system.ShowFlowers;
            bool suppressed=system.SuppressGeometryForChecks;int topology=system.TopologyForChecks;
            var focus=system.FocusForChecks;
            var go=new GameObject("Grass optimization verification"){hideFlags=HideFlags.HideAndDontSave};
            var camera=go.AddComponent<Camera>();camera.CopyFrom(Camera.main);camera.enabled=false;camera.fieldOfView=40;camera.nearClipPlane=.15f;camera.farClipPlane=23000;
            var extra=go.AddComponent<UniversalAdditionalCameraData>();var main=Camera.main.GetUniversalAdditionalCameraData();
            extra.renderPostProcessing=main.renderPostProcessing;extra.renderShadows=main.renderShadows;extra.antialiasing=main.antialiasing;
            var target=new RenderTexture(1600,900,24);target.Create();camera.targetTexture=target;
            var image=new Texture2D(1600,900,TextureFormat.RGB24,false);var active=RenderTexture.active;
            var report=new StringBuilder();
            try
            {
                system.FreezeWindForChecks=true;system.MatureForChecks=true;system.SuppressGeometryForChecks=false;system.TopologyForChecks=0;
                system.ShowFlowers=true;
                var ground=GrassOrbitCapture.FindMeadow(system.CurrentMap);system.FocusForChecks=ground;
                foreach(string view in new[]{"close","side","rts","top65","top150"})
                {
                    File.WriteAllText(folder+"/progress.txt",view);
                    var offset=view=="close"?new Vector3(0,2.2f,-6):view=="rts"?new Vector3(0,35,-55):view=="top65"?new Vector3(0,65,0):view=="top150"?new Vector3(0,150,0):new Vector3(0,13.073f,-149.429f);
                    camera.transform.position=ground+offset;camera.transform.LookAt(ground);
                    system.ReferenceRenderingForChecks=true;Warm();var before=Capture(view+"-reference");double oldTime=Measure();
                    system.ReferenceRenderingForChecks=false;Warm();var after=Capture(view+"-optimized");double newTime=Measure();
                    var counts=system.ReadDrawCountsForChecks(camera);var heads=system.ReadHeadCountsForChecks(camera);
                    Require(heads[0]>0&&heads[0]<counts[0]+counts[1],"Near flower list must be sparse.");
                    Require(heads[1]<counts[2],"Understorey flower list must be sparse.");
                    double difference=Difference(before,after);
                    Require(difference<(view=="close"?.15:4),"Image difference too large: "+view+" "+difference);
                    report.AppendLine($"{view}: reference={oldTime:F3}ms optimized={newTime:F3}ms ratio={oldTime/newTime:F2}x; MAE={difference:F4}/255; blades={string.Join(",",counts)}; flower-bearing instances={string.Join(",",heads)}; buffers={system.BufferBytes/1048576.0:F2}MiB");
                }
                system.ShowFlowers=false;Warm();var disabled=system.ReadHeadCountsForChecks(camera);
                Require(disabled[0]==0&&disabled[1]==0,"Disabled flowers must not enter the head lists.");
                system.ShowFlowers=true;Warm();Require(system.ReadHeadCountsForChecks(camera)[0]>0,"Flowers must return after enabling.");
                report.AppendLine("PASS: sparse heads, flower toggle, close image preservation, bounded distant image difference. Timings include synchronized Editor rendering and readback, not gameplay FPS or isolated GPU time.");
            }
            catch(Exception e){report.AppendLine("FAIL: "+e);UnityEngine.Debug.LogException(e);}
            finally
            {
                system.ReferenceRenderingForChecks=reference;system.FreezeWindForChecks=frozen;system.MatureForChecks=mature;system.FocusForChecks=focus;
                system.SuppressGeometryForChecks=suppressed;system.TopologyForChecks=topology;system.ShowFlowers=flowers;
                camera.targetTexture=null;RenderTexture.active=active;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(go);
                File.WriteAllText(folder+"/report.txt",report.ToString());UnityEngine.Debug.Log(report.ToString());
            }
            void Warm(){for(int i=0;i<70;i++){camera.Render();if(i%12==0)Flush();}}
            void Flush(){RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1,1),0,0);image.Apply();}
            Color32[] Capture(string name){camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1600,900),0,0);image.Apply();File.WriteAllBytes(folder+"/"+name+".png",image.EncodeToPNG());return image.GetPixels32();}
            double Measure(){var samples=new List<double>();for(int n=0;n<7;n++){Flush();var clock=Stopwatch.StartNew();for(int i=0;i<20;i++)camera.Render();Flush();samples.Add(clock.Elapsed.TotalMilliseconds/20);}samples.Sort();return samples[3];}
        }
        private static double Difference(Color32[] a,Color32[] b){double total=0;for(int i=0;i<a.Length;i++)total+=Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b);return total/a.Length/3;}
        private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    }
}
