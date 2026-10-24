using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using TacticalMaps.Grass;
using UnityEditor;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class GrassFlowerLodChecks
    {
        private static EditorApplication.CallbackFunction tick;
        [MenuItem("Tools/Tactical Maps/Verify Distant Flower LODs")]
        internal static void Verify()
        {
            if(tick!=null)throw new InvalidOperationException("Flower LOD check already running.");
            var system=UnityEngine.Object.FindAnyObjectByType<GrassSystem>();
            if(EditorApplication.isPlaying||system?.CurrentMap==null)throw new InvalidOperationException("Built meadow in Edit Mode required.");
            const string folder="Logs/FlowerLodFix/Verified/";Directory.CreateDirectory(folder);
            var report=new StringBuilder();var captures=new Dictionary<string,Color32[]>();
            var so=new SerializedObject(system);int debug=so.FindProperty("debugView").intValue;
            var go=new GameObject("FlowerLodCheckCamera"){hideFlags=HideFlags.HideAndDontSave};var camera=go.AddComponent<Camera>();
            camera.enabled=false;camera.fieldOfView=55;camera.nearClipPlane=.15f;camera.farClipPlane=23000;
            var target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;
            var image=new Texture2D(1280,720,TextureFormat.RGB24,false);
            var ground=GrassOrbitCapture.FindMeadow(system.CurrentMap);bool enabled=system.enabled;
            // Do not discard live buffers or block on AsyncGPUReadback.WaitForCompletion.
            system.MatureForChecks=true;system.FreezeWindForChecks=true;
            so.FindProperty("debugView").intValue=0;so.ApplyModifiedPropertiesWithoutUndo();
            var cases=new List<string>{"purple-before","purple-none","purple-after","blue-full","blue-reduced","blue-none"};
            foreach(int pitch in new[]{5,30,85})foreach(var mode in new[]{"before","after","none"})cases.Add("rts-"+pitch+"-"+mode);
            int index=0,frame=0;var samples=new List<double>();int builds=0;
            File.WriteAllText(folder+"status.txt","Running");
            tick=()=>
            {
                try
                {
                    string name=cases[index];
                    if(frame==0)
                    {
                        system.GeometryMaskForChecks=name.StartsWith("purple")?4:name.StartsWith("blue")?0:7;
                        camera.cullingMask=name.StartsWith("purple")?1<<system.gameObject.layer:~0;
                        system.FocusForChecks=name.StartsWith("blue")?ground+Vector3.right*10000:ground;
                        system.BlueFlowerRetentionForChecks=name=="blue-full"?1:.55f;
                        Shader.SetGlobalInt("_GrassFlowerStampCheck",name.EndsWith("before")?1:0);
                        system.SuppressFlowersForChecks=name.EndsWith("none");
                        int pitch=name.StartsWith("rts")?int.Parse(name.Split('-')[1]):30;
                        camera.transform.position=ground+Quaternion.Euler(pitch,0,0)*Vector3.back*150;camera.transform.LookAt(ground);
                        samples.Clear();
                    }
                    camera.Render();frame++;
                    if(frame>=64)
                    {
                        var clock=System.Diagnostics.Stopwatch.StartNew();
                        for(int n=0;n<8;n++)camera.Render();
                        Read();samples.Add(clock.Elapsed.TotalMilliseconds/8);
                        if(samples.Count==3)
                        {
                            var data=image.GetPixels32();captures[name]=data;
                            File.WriteAllBytes(folder+name+".png",image.EncodeToPNG());
                            samples.Sort();report.AppendLine(name+": synchronized Editor render="+samples[1].ToString("F3")+" ms; draws="+system.SubmittedDraws);
                            if(name=="purple-after")
                            {
                                int before=Changed(captures["purple-before"],captures["purple-none"]),after=Changed(data,captures["purple-none"]);
                                report.AppendLine($"Purple head pixels: before={before}, after={after}");
                                if(after<=before)throw new InvalidOperationException("Purple visibility did not improve.");
                            }
                            if(name=="blue-none")
                            {
                                int full=Changed(captures["blue-full"],data),reduced=Changed(captures["blue-reduced"],data);
                                float ratio=(float)reduced/Math.Max(1,full);report.AppendLine($"Blue retained head coverage={ratio:F3} (full={full}, reduced={reduced})");
                                if(ratio<.4f||ratio>.7f)throw new InvalidOperationException("Blue thinning differs from approximately 45%.");
                            }
                            index++;frame=0;
                            File.WriteAllText(folder+"status.txt","Completed "+index+"/"+cases.Count);
                            if(index==cases.Count)
                            {
                                report.AppendLine("PASS: flower density="+so.FindProperty("flowerDensity").floatValue+"; blue retention=.55; flower cache=20 MiB per view, at most 40 MiB; instance buffers="+(system.BufferBytes/1048576f).ToString("F2")+" MiB");
                                Finish("PASS");return;
                            }
                        }
                    }
                    if(frame==24)builds=system.FlowerCacheBuildsForChecks(camera);
                    if(frame==48&&system.FlowerCacheBuildsForChecks(camera)!=builds)throw new InvalidOperationException("Stationary camera rebuilds the cache.");
                    EditorApplication.QueuePlayerLoopUpdate();
                }
                catch(Exception e){report.AppendLine("FAIL: "+e);Finish("FAIL");Debug.LogException(e);}
            };
            EditorApplication.update+=tick;
            void Read()
            {
                var previous=RenderTexture.active;
                try{RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();}
                finally{RenderTexture.active=previous;}
            }
            void Finish(string status)
            {
                EditorApplication.update-=tick;tick=null;
                Shader.SetGlobalInt("_GrassFlowerStampCheck",0);system.BlueFlowerRetentionForChecks=.55f;system.GeometryMaskForChecks=7;
                system.SuppressFlowersForChecks=false;system.MatureForChecks=false;system.FreezeWindForChecks=false;system.FocusForChecks=null;
                so.Update();so.FindProperty("debugView").intValue=debug;so.ApplyModifiedPropertiesWithoutUndo();system.enabled=enabled;
                camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(image);target.Release();UnityEngine.Object.DestroyImmediate(target);
                File.WriteAllText(folder+"checks.txt",report.ToString());File.WriteAllText(folder+"status.txt",status);
            }
        }
        private static int Changed(Color32[] a,Color32[] b)
        {int count=0;for(int i=0;i<a.Length;i++)if(Math.Max(Math.Abs(a[i].r-b[i].r),Math.Max(Math.Abs(a[i].g-b[i].g),Math.Abs(a[i].b-b[i].b)))>24)count++;return count;}
    }
}
