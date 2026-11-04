using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using TacticalMaps.Grass;
using UnityEditor;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class GrassFlowerTuningChecks
    {
        private static EditorApplication.CallbackFunction tick;
        [MenuItem("Tools/Tactical Maps/Verify Flower LOD Tuning")]
        internal static void Verify()
        {
            if(tick!=null)throw new InvalidOperationException("Flower LOD check already running.");
            var system=UnityEngine.Object.FindAnyObjectByType<GrassSystem>();
            if(EditorApplication.isPlaying||system?.CurrentMap==null)throw new InvalidOperationException("Built meadow in Edit Mode required.");
            const string folder="Logs/FlowerLodTuning/";Directory.CreateDirectory(folder);
            var report=new StringBuilder();var captures=new Dictionary<string,Color32[]>();
            var so=new SerializedObject(system);int debug=so.FindProperty("debugView").intValue;
            var go=new GameObject("FlowerLodCheckCamera"){hideFlags=HideFlags.HideAndDontSave};var camera=go.AddComponent<Camera>();
            camera.enabled=false;camera.fieldOfView=55;camera.nearClipPlane=.15f;camera.farClipPlane=23000;
            var target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;
            var image=new Texture2D(1280,720,TextureFormat.RGB24,false);
            var ground=GrassOrbitCapture.FindMeadow(system.CurrentMap);bool enabled=system.enabled;
            string[] fields={"purpleFlowerRetention","distantFlowerSize","distantFlowerMaximumDistance"};
            var saved=new float[3];for(int i=0;i<3;i++)saved[i]=so.FindProperty(fields[i]).floatValue;
            // Do not discard live buffers or block on AsyncGPUReadback.WaitForCompletion.
            system.MatureForChecks=true;system.FreezeWindForChecks=true;
            so.FindProperty("debugView").intValue=0;so.ApplyModifiedPropertiesWithoutUndo();
            var cases=new List<string>();
            foreach(int pitch in new[]{15,30,45})foreach(var mode in new[]{"full","none","half"})cases.Add("purple-"+pitch+"-"+mode);
            cases.AddRange(new[]{"blue-30-full","blue-30-none","blue-30-small","cutoff-30-full","cutoff-30-none","cutoff-30-limited"});
            foreach(int pitch in new[]{15,30,60})foreach(var mode in new[]{"full","tuned"})cases.Add("rts-"+pitch+"-"+mode);
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
                        bool terrain=name.StartsWith("blue")||name.StartsWith("cutoff");
                        system.GeometryMaskForChecks=terrain?0:name.StartsWith("purple")?4:7;
                        system.FocusForChecks=terrain?ground+Vector3.right*10000:ground;
                        bool full=name.EndsWith("full");
                        Set(fields[0],full?1:.5f);Set(fields[1],full?1:2f/3f);
                        Set(fields[2],name.StartsWith("cutoff")?(full?3000:50):3000);
                        Shader.SetGlobalInt("_GrassFlowerStampCheck",0);
                        system.SuppressFlowersForChecks=name.EndsWith("none");
                        int pitch=int.Parse(name.Split('-')[1]);
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
                            if(name.StartsWith("purple")&&name.EndsWith("half"))
                            {
                                string prefix=name.Substring(0,name.LastIndexOf('-')+1);
                                int full=Changed(captures[prefix+"full"],captures[prefix+"none"]),half=Changed(data,captures[prefix+"none"]);
                                float ratio=(float)half/Math.Max(1,full);report.AppendLine($"Purple retained head coverage at {name}: {ratio:F3}, full={full}, half={half}");
                                if(ratio<.38f||ratio>.62f)throw new InvalidOperationException("Purple density was not approximately halved.");
                            }
                            if(name=="blue-30-small")
                            {
                                int full=Changed(captures["blue-30-full"],captures["blue-30-none"]),small=Changed(data,captures["blue-30-none"]);
                                float ratio=(float)small/Math.Max(1,full);report.AppendLine($"Blue head pixel coverage after 2/3 linear size: {ratio:F3}, full={full}, small={small}");
                                if(ratio>=.9f||ratio<.15f)throw new InvalidOperationException("Blue marks did not become smaller.");
                            }
                            if(name=="cutoff-30-limited")
                            {
                                int unlimited=Changed(captures["cutoff-30-full"],captures["cutoff-30-none"]),limited=Changed(data,captures["cutoff-30-none"]);
                                report.AppendLine($"RTS camera: head pixels with 3000m limit={unlimited}, with temporary 50m limit={limited}; production limit restored to 3000m");
                                if(unlimited<10||limited!=0)throw new InvalidOperationException("Terrain distance limit failed.");
                            }
                            index++;frame=0;
                            File.WriteAllText(folder+"status.txt","Completed "+index+"/"+cases.Count);
                            if(index==cases.Count)
                            {
                                report.AppendLine("PASS: purple retention=.5; blue linear size=2/3; Terrain flower limit=3000m; density="+so.FindProperty("flowerDensity").floatValue);
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
            void Set(string name,float value)=>typeof(GrassSystem).GetField(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(system,value);
            void Read()
            {
                var previous=RenderTexture.active;
                try{RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();}
                finally{RenderTexture.active=previous;}
            }
            void Finish(string status)
            {
                EditorApplication.update-=tick;tick=null;
                Shader.SetGlobalInt("_GrassFlowerStampCheck",0);for(int i=0;i<3;i++)Set(fields[i],saved[i]);system.GeometryMaskForChecks=7;
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
