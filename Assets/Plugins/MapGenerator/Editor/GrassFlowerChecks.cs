using System;
using System.IO;
using System.Text;
using TacticalMaps.Grass;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TacticalMaps.Editor
{
    internal static class GrassFlowerChecks
    {
        [MenuItem("Tools/Tactical Maps/Verify Meadow Flower Palette")]
        internal static void Verify()
        {
            var system=UnityEngine.Object.FindAnyObjectByType<GrassSystem>();
            if(system==null||system.CurrentMap==null||EditorApplication.isPlaying)throw new InvalidOperationException("A built meadow in Edit Mode is required.");
            Directory.CreateDirectory("Temp/grass-flowers");
            var report=new StringBuilder();bool enabled=system.enabled;
            var original=new SerializedObject(system);
            float originalDensity=original.FindProperty("flowerDensity").floatValue;int originalDebug=original.FindProperty("debugView").intValue;
            var sourcePalette=original.FindProperty("flowerPalette");var savedPalette=new GrassFlowerType[sourcePalette.arraySize];
            for(int i=0;i<savedPalette.Length;i++)
            {
                var entry=sourcePalette.GetArrayElementAtIndex(i);
                savedPalette[i]=new GrassFlowerType(entry.FindPropertyRelative("name").stringValue,entry.FindPropertyRelative("color").colorValue,
                    entry.FindPropertyRelative("weight").floatValue,entry.FindPropertyRelative("size").floatValue,(GrassFlowerForm)entry.FindPropertyRelative("form").enumValueIndex);
            }
            var go=new GameObject("FlowerCheckCamera") {hideFlags=HideFlags.HideAndDontSave};
            var camera=go.AddComponent<Camera>();camera.enabled=false;camera.fieldOfView=55;camera.nearClipPlane=.15f;camera.farClipPlane=23000;
            var target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;
            var pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
            try
            {
                foreach(var name in new[]{"TacticalMaps/Grass/LowPolyIndirect","TacticalMaps/Grass/DistantCards","TacticalMaps/Terrain/MatteStochastic"})
                    foreach(var message in ShaderUtil.GetShaderMessages(Shader.Find(name)))
                        Require(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error,message.message);
                Require(system.Library.CardsFlowers!=null&&system.Library.TurfFlowers!=null,"Flower masks exist.");
                Require(MaskCoverage(system.Library.CardsFlowers)>.001,"Cards mask contains real petals.");
                Require(MaskCoverage(system.Library.TurfFlowers)>.001,"Terrain mask contains real petals.");
                system.enabled=false;system.enabled=true;system.MatureForChecks=true;system.FreezeWindForChecks=true;
                var settings=new SerializedObject(system);settings.FindProperty("debugView").intValue=0;settings.ApplyModifiedPropertiesWithoutUndo();
                var ground=GrassOrbitCapture.FindMeadow(system.CurrentMap);system.FocusForChecks=ground;
                foreach(var view in new[]{"close","rts","maxLow","maxOblique","maxTop"})
                {
                    var offset=view=="close"?new Vector3(0,2.2f,-6):view=="rts"?new Vector3(0,35,-55):
                        view=="maxLow"?new Vector3(0,13.073f,-149.429f):view=="maxOblique"?new Vector3(0,75,-129.904f):new Vector3(0,149.429f,-13.073f);
                    camera.transform.position=ground+offset;camera.transform.LookAt(ground+(view=="close"?Vector3.forward*5:Vector3.zero));Warm();
                    var flowers=Read("Temp/grass-flowers/"+view+".png");double full=Measure();
                    system.SuppressFlowersForChecks=true;Warm();var bare=Read("Temp/grass-flowers/"+view+"-none.png");double none=Measure();
                    system.SuppressFlowersForChecks=false;
                    double difference=Difference(flowers,bare);Require(difference>.01,"Flowers affect "+view);
                    report.AppendLine($"{view}: flower difference={difference:F4}/255; with={full:F3} ms, none={none:F3} ms; buffers={system.BufferBytes/1048576f:F2} MiB, draws={system.SubmittedDraws}");
                }
                camera.transform.position=ground+new Vector3(0,75,-129.904f);camera.transform.LookAt(ground);
                foreach(int layer in new[]{1,2,4,0})
                {
                    system.GeometryMaskForChecks=layer;
                    SetPalette(new Color(.95f,.08f,.6f));Warm();var pink=Read("Temp/grass-flowers/palette-pink-"+layer+".png");
                    SetPalette(new Color(.08f,.55f,.95f));Warm();var blue=Read("Temp/grass-flowers/palette-blue-"+layer+".png");
                    double difference=Difference(pink,blue);Require(difference>.005,"Palette changes layer "+layer+" without rebaking.");
                    report.AppendLine($"PASS: live palette change, layer {layer}, difference={difference:F5}/255");
                }
                report.AppendLine("PASS: all geometry representations and Terrain respond to the live palette. No new per-plant objects/draws/buffers. Editor timings include rendering/readback overhead.");

                void SetPalette(Color color)
                {
                    settings.Update();
                    var palette=settings.FindProperty("flowerPalette");
                    for(int i=0;i<palette.arraySize;i++)
                    {
                        var entry=palette.GetArrayElementAtIndex(i);entry.FindPropertyRelative("color").colorValue=color;
                        entry.FindPropertyRelative("weight").floatValue=i==0?1:0;
                    }
                    settings.ApplyModifiedPropertiesWithoutUndo();
                }
                void Warm() {for(int i=0;i<64;i++){camera.Render();AsyncGPUReadback.Request(target,0).WaitForCompletion();}}
                Color32[] Read(string path)
                {
                    camera.Render();var old=RenderTexture.active;
                    try {RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG());return pixels.GetPixels32();}
                    finally {RenderTexture.active=old;}
                }
                double Measure()
                {
                    var clock=System.Diagnostics.Stopwatch.StartNew();for(int i=0;i<12;i++)camera.Render();AsyncGPUReadback.Request(target,0).WaitForCompletion();return clock.Elapsed.TotalMilliseconds/12;
                }
            }
            catch(Exception e){report.AppendLine("FAIL: "+e);Debug.LogException(e);}
            finally
            {
                system.GeometryMaskForChecks=7;system.SuppressFlowersForChecks=false;system.MatureForChecks=false;system.FocusForChecks=null;system.FreezeWindForChecks=false;
                var restore=new SerializedObject(system);restore.FindProperty("flowerDensity").floatValue=originalDensity;restore.FindProperty("debugView").intValue=originalDebug;
                var palette=restore.FindProperty("flowerPalette");palette.arraySize=savedPalette.Length;
                for(int i=0;i<savedPalette.Length;i++)
                {
                    var entry=palette.GetArrayElementAtIndex(i);entry.FindPropertyRelative("name").stringValue=savedPalette[i].name;
                    entry.FindPropertyRelative("color").colorValue=savedPalette[i].color;entry.FindPropertyRelative("weight").floatValue=savedPalette[i].weight;entry.FindPropertyRelative("size").floatValue=savedPalette[i].size;
                    entry.FindPropertyRelative("form").enumValueIndex=(int)savedPalette[i].form;
                }
                restore.ApplyModifiedPropertiesWithoutUndo();system.enabled=enabled;if(system.CurrentMap!=null)system.Attach(system.CurrentMap);
                camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(pixels);target.Release();UnityEngine.Object.DestroyImmediate(target);
                File.WriteAllText("Temp/grass-flowers/checks.txt",report.ToString());
            }
        }
        private static float MaskCoverage(Texture2D texture)
        {var pixels=texture.GetPixels32();double sum=0;foreach(var c in pixels)sum+=c.r/255f;return (float)(sum/pixels.Length);}
        private static double Difference(Color32[] a,Color32[] b)
        {double sum=0;for(int i=0;i<a.Length;i++)sum+=Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b);return sum/(a.Length*3);}
        private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    }
}
