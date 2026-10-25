using System;
using System.IO;
using System.Text;
using System.Threading;
using TacticalMaps.Grass;
using TacticalMaps.Generation;
using TacticalMaps.Construction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace TacticalMaps.Editor
{
    internal static class GrassBandChecks
    {
        [MenuItem("Tools/Tactical Maps/Verify Grass Transition Bands")]
        internal static void Verify() { Capture(false);RealMap(); }
        internal static void Baseline() => Capture(true);
        private static void Capture(bool baseline)
        {
            var system=UnityEngine.Object.FindAnyObjectByType<GrassSystem>();
            if(system==null||system.CurrentMap==null||EditorApplication.isPlaying) throw new InvalidOperationException("A built meadow in Edit Mode is required.");
            Directory.CreateDirectory("Temp/grass-bands");
            var report=new StringBuilder();
            var scene=EditorSceneManager.NewPreviewScene();MapData map=null;
            bool enabled=system.enabled;
            try
            {
                system.enabled=false;
                var go=new GameObject("FlatMeadowCheck");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
                var builder=go.AddComponent<MapBuilder>();ConstructionSetup.Configure(builder);
                var grassObject=new GameObject("FlatMeadowGrass");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(grassObject,scene);
                var grass=grassObject.AddComponent<GrassSystem>();
                EditorUtility.CopySerialized(system,grass);
                var settings=new SerializedObject(grass);
                settings.FindProperty("currentMap").objectReferenceValue=null;
                settings.FindProperty("debugView").intValue=0;settings.ApplyModifiedPropertiesWithoutUndo();
                grass.enabled=true;grass.MatureForChecks=true;grass.FreezeWindForChecks=true;
                var textures=new Texture2D[3];var sizes=new[]{Vector2.one*2048,Vector2.one*8192,Vector2.one*20000};var weights=new float[3][,,];
                for(int i=0;i<3;i++)
                {
                    textures[i]=new Texture2D(33,33,TextureFormat.R16,false,true);var heights=new ushort[33*33];for(int p=0;p<heights.Length;p++)heights[p]=19660;
                    textures[i].SetPixelData(heights,0);textures[i].Apply();
                    weights[i]=new float[16,16,6];for(int z=0;z<16;z++)for(int x=0;x<16;x++)weights[i][z,x,0]=1;
                }
                var surface=new SurfaceData(system.CurrentMap.Terrains[0].terrainData.terrainLayers,weights,Vector2.zero);
                map=new MapData(textures,sizes,100,5,4000,Vector2Int.zero,surface:surface,grass:GrassCoverage.Generate(surface,CancellationToken.None));
                builder.Build(map);grass.Build(map);
                var lightObject=new GameObject("CheckSun");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightObject,scene);
                var sun=lightObject.AddComponent<Light>();sun.type=LightType.Directional;sun.shadows=LightShadows.None;
                if(RenderSettings.sun!=null) {sun.color=RenderSettings.sun.color;sun.intensity=RenderSettings.sun.intensity;sun.transform.rotation=RenderSettings.sun.transform.rotation;}
                else {sun.intensity=1;sun.transform.rotation=Quaternion.Euler(50,-30,0);}
                var ground=new Vector3(0,30,0);
                var cameraObject=new GameObject("GrassBandCamera");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject,scene);
                var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.scene=scene;camera.fieldOfView=55;camera.nearClipPlane=.15f;camera.farClipPlane=23000;
                var target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;
                var pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
                try
                {
                    foreach(var pitch in new[]{15f,30f,60f,85f})
                    {
                        camera.transform.position=ground+Quaternion.Euler(pitch,0,0)*Vector3.back*150;camera.transform.LookAt(ground);
                        grass.FocusForChecks=ground;
                        string prefix="Temp/grass-bands/flat-"+pitch.ToString("F0");
                        grass.GeometryMaskForChecks=7;grass.TransitionForChecks=3;Warm();
                        var before=Read(prefix+"-before.png");
                        grass.SuppressGeometryForChecks=true;
                        var terrain=Read(prefix+"-surface.png");grass.SuppressGeometryForChecks=false;
                        if(baseline)
                        {
                            foreach(var layer in new[]{1,2,4}) {grass.GeometryMaskForChecks=layer;Warm();Read(prefix+"-layer"+layer+".png");}
                        }
                        else
                        {
                            grass.GeometryMaskForChecks=7;grass.TransitionForChecks=0;Warm();var after=Read(prefix+"-after.png");
                            WriteProfile(prefix+"-profile.csv",before,after,terrain,camera,ground);
                            // Keep the camera fixed while moving only the residency/LOD focus. Changes
                            // then come from transitions, not camera reprojection, wind or biome motion.
                            var initialFocus=ground;grass.FocusForChecks=ground+Vector3.right*32;
                            grass.TransitionForChecks=3;Warm();var oldMoved=Read(prefix+"-before-moved.png");
                            grass.TransitionForChecks=0;Warm();var newMoved=Read(prefix+"-after-moved.png");
                            double oldChange=Difference(before,oldMoved),newChange=Difference(after,newMoved);
                            report.AppendLine($"pitch {pitch}: fixed-ground transition change before={oldChange:F4}, after={newChange:F4} /255");
                            if(newChange>=oldChange*.75) throw new InvalidOperationException("Transition change was not reduced at pitch "+pitch);
                            if(pitch==30&&Difference(after,terrain)<.15) throw new InvalidOperationException("Distant grass lost its visible texture.");
                            grass.FocusForChecks=initialFocus;
                        }
                    }
                    report.AppendLine(baseline?"PASS: isolated baseline views captured.":"PASS: matched flat-meadow transition views and fixed-camera focus translation captured.");
                }
                finally {camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(pixels);target.Release();UnityEngine.Object.DestroyImmediate(target);}

                void Warm() {for(int i=0;i<64;i++){camera.Render();AsyncGPUReadback.Request(camera.targetTexture,0).WaitForCompletion();}}
                Color32[] Read(string path)
                {
                    camera.Render();var old=RenderTexture.active;
                    try {RenderTexture.active=camera.targetTexture;pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG());return pixels.GetPixels32();}
                    finally {RenderTexture.active=old;}
                }
            }
            catch(Exception e) {report.AppendLine("FAIL: "+e);UnityEngine.Debug.LogException(e);}
            finally
            {
                map?.Release();EditorSceneManager.ClosePreviewScene(scene);system.enabled=enabled;
                File.WriteAllText("Temp/grass-bands/"+(baseline?"baseline":"checks")+".txt",report.ToString());
            }
        }
        internal static void RealMap()
        {
            var system=UnityEngine.Object.FindAnyObjectByType<GrassSystem>();
            if(system==null||system.CurrentMap==null||EditorApplication.isPlaying) throw new InvalidOperationException("A built meadow in Edit Mode is required.");
            var report=new StringBuilder();
            var settings=new SerializedObject(system);int debug=settings.FindProperty("debugView").intValue;
            bool enabled=system.enabled;
            var go=new GameObject("GrassBandRealCamera") {hideFlags=HideFlags.HideAndDontSave};
            var camera=go.AddComponent<Camera>();camera.enabled=false;camera.fieldOfView=55;camera.nearClipPlane=.15f;camera.farClipPlane=23000;
            var target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;
            var pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
            try
            {
                system.enabled=false;system.enabled=true;system.MatureForChecks=true;system.FreezeWindForChecks=true;
                settings.FindProperty("debugView").intValue=0;settings.ApplyModifiedPropertiesWithoutUndo();
                var ground=GrassOrbitCapture.FindMeadow(system.CurrentMap);system.FocusForChecks=ground;
                foreach(var view in new[]{"close","rts","maxLow","maxOblique","maxTop"})
                {
                    var offset=view=="close"?new Vector3(0,2.2f,-6):view=="rts"?new Vector3(0,35,-55):
                        view=="maxLow"?new Vector3(0,13.073f,-149.429f):view=="maxOblique"?new Vector3(0,75,-129.904f):new Vector3(0,149.429f,-13.073f);
                    camera.transform.position=ground+offset;camera.transform.LookAt(ground+(view=="close"?Vector3.forward*5:Vector3.zero));
                    system.TransitionForChecks=3;Warm();var before=Read("Temp/grass-bands/real-"+view+"-before.png");double oldTime=Measure();
                    system.TransitionForChecks=0;Warm();var after=Read("Temp/grass-bands/real-"+view+"-after.png");double newTime=Measure();
                    report.AppendLine($"{view}: before={oldTime:F3} ms, after={newTime:F3} ms; buffers={system.BufferBytes/1048576f:F2} MiB, draws={system.SubmittedDraws}");
                    if(view=="close")
                    {
                        double sum=0;for(int i=0;i<1280*360;i++)sum+=Math.Abs(before[i].r-after[i].r)+Math.Abs(before[i].g-after[i].g)+Math.Abs(before[i].b-after[i].b);
                        double difference=sum/(1280*360*3);report.AppendLine($"Close foreground difference={difference:F5}/255");
                        if(difference>.1) throw new InvalidOperationException("Close foreground changed.");
                    }
                }
                report.AppendLine("PASS: close foreground preserved; actual RTS/max-radius views captured. Timings include synchronized Editor rendering and readback, not isolated GPU time.");
                void Warm() {for(int i=0;i<64;i++){camera.Render();AsyncGPUReadback.Request(target,0).WaitForCompletion();}}
                Color32[] Read(string path)
                {
                    camera.Render();var old=RenderTexture.active;
                    try {RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG());return pixels.GetPixels32();}
                    finally {RenderTexture.active=old;}
                }
                double Measure()
                {
                    var clock=System.Diagnostics.Stopwatch.StartNew();for(int i=0;i<16;i++)camera.Render();AsyncGPUReadback.Request(target,0).WaitForCompletion();return clock.Elapsed.TotalMilliseconds/16;
                }
            }
            catch(Exception e) {report.AppendLine("FAIL: "+e);UnityEngine.Debug.LogException(e);}
            finally
            {
                system.TransitionForChecks=0;system.FocusForChecks=null;system.MatureForChecks=false;system.FreezeWindForChecks=false;
                settings.Update();settings.FindProperty("debugView").intValue=debug;settings.ApplyModifiedPropertiesWithoutUndo();system.enabled=enabled;
                camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(pixels);target.Release();UnityEngine.Object.DestroyImmediate(target);
                File.WriteAllText("Temp/grass-bands/real-checks.txt",report.ToString());
            }
        }
        private static void WriteProfile(string path,Color32[] before,Color32[] after,Color32[] surface,Camera camera,Vector3 ground)
        {
            const int bins=100;var old=new double[bins];var current=new double[bins];var reference=new double[bins];var count=new int[bins];
            var focus=(camera.transform.position+ground)*.5f;
            for(int y=0;y<720;y++)for(int x=160;x<1120;x++)
            {
                var ray=camera.ViewportPointToRay(new Vector3((x+.5f)/1280,(y+.5f)/720,0));if(ray.direction.y>=-.01f)continue;
                var world=ray.origin+ray.direction*((ground.y-ray.origin.y)/ray.direction.y);
                int bin=(int)(Vector2.Distance(new Vector2(world.x,world.z),new Vector2(focus.x,focus.z))/10);if(bin<0||bin>=bins)continue;
                int i=y*1280+x;old[bin]+=Luma(before[i]);current[bin]+=Luma(after[i]);reference[bin]+=Luma(surface[i]);count[bin]++;
            }
            var csv=new StringBuilder("distance,count,before,after,surface\n");
            for(int i=0;i<bins;i++)if(count[i]>50)csv.AppendLine(FormattableString.Invariant($"{i*10+5},{count[i]},{old[i]/count[i]:F5},{current[i]/count[i]:F5},{reference[i]/count[i]:F5}"));
            File.WriteAllText(path,csv.ToString());
        }
        private static double Luma(Color32 c) => c.r*.2126+c.g*.7152+c.b*.0722;
        private static double Difference(Color32[] a,Color32[] b)
        {double sum=0;for(int i=0;i<a.Length;i++)sum+=Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b);return sum/(a.Length*3);}
    }
}
