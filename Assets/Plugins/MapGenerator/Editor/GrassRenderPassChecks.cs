using System;
using System.IO;
using System.Text;
using TacticalMaps.Grass;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace TacticalMaps.Editor
{
    internal static class GrassRenderPassChecks
    {
        [MenuItem("Tools/Tactical Maps/Verify Grass Combined Pass")]
        internal static void Verify()
        {
            const string folder="Logs/GrassOptimization2/PassChecks";
            Directory.CreateDirectory(folder);
            var system=UnityEngine.Object.FindAnyObjectByType<GrassSystem>();
            if(system?.CurrentMap==null)throw new InvalidOperationException("Built grass map required.");
            int optimization=system.OptimizationForChecks,mask=system.GeometryMaskForChecks;
            bool frozen=system.FreezeWindForChecks,mature=system.MatureForChecks;
            var focus=system.FocusForChecks;
            var go=new GameObject("Grass MRT validation"){hideFlags=HideFlags.HideAndDontSave};
            var camera=go.AddComponent<Camera>();camera.CopyFrom(Camera.main);camera.enabled=false;camera.fieldOfView=40;
            var extra=go.AddComponent<UniversalAdditionalCameraData>();var source=Camera.main.GetUniversalAdditionalCameraData();
            extra.renderPostProcessing=source.renderPostProcessing;extra.renderShadows=source.renderShadows;extra.antialiasing=source.antialiasing;
            var target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;
            var image=new Texture2D(1280,720,TextureFormat.RGB24,false);var previous=RenderTexture.active;
            var ground=GrassOrbitCapture.FindMeadow(system.CurrentMap);
            var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.name="Grass occlusion check";cube.hideFlags=HideFlags.HideAndDontSave;
            cube.transform.position=ground+new Vector3(0,1.5f,-8);cube.transform.localScale=new Vector3(4,3,4);
            var material=new Material(Shader.Find("Universal Render Pipeline/Lit")){hideFlags=HideFlags.HideAndDontSave};
            material.SetColor("_BaseColor",new Color(.7f,.08f,.02f,1));cube.GetComponent<Renderer>().sharedMaterial=material;
            var report=new StringBuilder();int errors=0;
            Application.LogCallback onLog=(message,stack,type)=>{if(type==LogType.Error||type==LogType.Exception)errors++;};
            Application.logMessageReceived+=onLog;
            try
            {
                system.MatureForChecks=true;system.FreezeWindForChecks=true;system.FocusForChecks=ground;
                foreach(string view in new[]{"close","side","top","orthographic"})
                {
                    camera.orthographic=view=="orthographic";camera.orthographicSize=40;
                    var offset=view=="close"?new Vector3(0,2.2f,-16):view=="side"?new Vector3(0,13.073f,-149.429f):new Vector3(0,65,-.01f);
                    camera.transform.position=ground+offset;camera.transform.LookAt(ground);
                    foreach(int layer in new[]{1,2,4,7})
                    {
                        system.GeometryMaskForChecks=layer;system.OptimizationForChecks=0;Warm();var ordinary=Capture(view+"-"+layer+"-ordinary");
                        system.OptimizationForChecks=64;Warm();var combined=Capture(view+"-"+layer+"-combined");
                        double difference=Difference(ordinary,combined);
                        report.AppendLine($"{view}, mask={layer}: combined/ordinary MAE={difference:F5}/255");
                        if(difference>.03)throw new InvalidOperationException("Combined pass image mismatch: "+view+" layer "+layer+" "+difference);
                    }
                }
                system.GeometryMaskForChecks=7;system.OptimizationForChecks=75;system.FreezeWindForChecks=false;
                camera.orthographic=false;
                for(int i=0;i<32;i++)
                {
                    camera.transform.position=ground+new Vector3(i*.2f,13.073f,-149.429f+i*.12f);camera.transform.LookAt(ground);camera.Render();
                }
                Capture("moving-camera-wind");
                if(errors!=0)throw new InvalidOperationException("Rendering logged "+errors+" errors.");
                report.AppendLine("PASS: opaque occluder and shadows, all grass layers, perspective/orthographic, live wind and camera movement, no render errors.");
            }
            catch(Exception ex){report.AppendLine("FAIL: "+ex);Debug.LogException(ex);}
            finally
            {
                Application.logMessageReceived-=onLog;
                system.OptimizationForChecks=optimization;system.GeometryMaskForChecks=mask;system.FocusForChecks=focus;system.FreezeWindForChecks=frozen;system.MatureForChecks=mature;
                camera.targetTexture=null;RenderTexture.active=previous;target.Release();
                UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(cube);UnityEngine.Object.DestroyImmediate(material);
                File.WriteAllText(folder+"/report.txt",report.ToString());Debug.Log(report.ToString());
            }
            void Warm(){for(int i=0;i<70;i++){camera.Render();if(i%16==0)Flush();}}
            void Flush(){RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1,1),0,0);image.Apply();}
            Color32[] Capture(string name){camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(folder+"/"+name+".png",image.EncodeToPNG());return image.GetPixels32();}
        }
        private static double Difference(Color32[] a,Color32[] b)
        {
            double sum=0;for(int i=0;i<a.Length;i++)sum+=Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b);
            return sum/a.Length/3;
        }
    }
}
