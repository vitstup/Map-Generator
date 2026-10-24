using System;
using System.IO;
using System.Text;
using TacticalMaps.Grass;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TacticalMaps.Editor
{
    internal static class GrassFlowerStampChecks
    {
        internal static void Forms()
        {
            var system=UnityEngine.Object.FindAnyObjectByType<GrassSystem>();var so=new SerializedObject(system);
            var palette=so.FindProperty("flowerPalette");var saved=new GrassFlowerType[palette.arraySize];
            for(int i=0;i<saved.Length;i++)
            {
                var e=palette.GetArrayElementAtIndex(i);saved[i]=new GrassFlowerType(e.FindPropertyRelative("name").stringValue,e.FindPropertyRelative("color").colorValue,
                    e.FindPropertyRelative("weight").floatValue,e.FindPropertyRelative("size").floatValue,(GrassFlowerForm)e.FindPropertyRelative("form").enumValueIndex);
            }
            int debug=so.FindProperty("debugView").intValue;bool enabled=system.enabled;
            var go=new GameObject("FlowerFormsCamera") {hideFlags=HideFlags.HideAndDontSave};var camera=go.AddComponent<Camera>();camera.enabled=false;camera.fieldOfView=55;camera.nearClipPlane=.02f;camera.farClipPlane=23000;
            var target=new RenderTexture(512,512,24);target.Create();camera.targetTexture=target;var image=new Texture2D(512,512,TextureFormat.RGB24,false);
            try
            {
                Directory.CreateDirectory("Temp/flower-heads");system.enabled=false;system.enabled=true;system.GeometryMaskForChecks=1;system.MatureForChecks=true;system.FreezeWindForChecks=true;
                so.FindProperty("debugView").intValue=0;so.ApplyModifiedPropertiesWithoutUndo();
                var ground=GrassOrbitCapture.FindMeadow(system.CurrentMap);camera.transform.position=ground+new Vector3(0,15,-20);camera.transform.LookAt(ground);system.FocusForChecks=ground;Warm();
                var selected=system.FindFlowerForChecks(camera);if(!selected.HasValue)throw new InvalidOperationException("No original-density flower found.");
                var head=selected.Value;system.FocusForChecks=head;camera.orthographic=true;camera.orthographicSize=.55f;
                camera.transform.position=head+new Vector3(0,.65f,-1);camera.transform.LookAt(head);
                for(int kind=0;kind<4;kind++)
                {
                    so.Update();palette=so.FindProperty("flowerPalette");
                    for(int i=0;i<palette.arraySize;i++)
                    {
                        var entry=palette.GetArrayElementAtIndex(i);entry.FindPropertyRelative("weight").floatValue=i==0?1:0;
                        entry.FindPropertyRelative("form").enumValueIndex=kind;entry.FindPropertyRelative("size").floatValue=1;
                        entry.FindPropertyRelative("color").colorValue=saved[Mathf.Min(kind,saved.Length-1)].color;
                    }
                    so.ApplyModifiedPropertiesWithoutUndo();Warm();camera.Render();var previous=RenderTexture.active;
                    try{RenderTexture.active=target;image.ReadPixels(new Rect(0,0,512,512),0,0);image.Apply();File.WriteAllBytes("Temp/flower-heads/form-"+kind+".png",image.EncodeToPNG());}
                    finally{RenderTexture.active=previous;}
                }
                void Warm(){for(int i=0;i<64;i++){camera.Render();AsyncGPUReadback.Request(target,0).WaitForCompletion();}}
            }
            finally
            {
                so.Update();palette=so.FindProperty("flowerPalette");palette.arraySize=saved.Length;
                for(int i=0;i<saved.Length;i++)
                {
                    var e=palette.GetArrayElementAtIndex(i);e.FindPropertyRelative("name").stringValue=saved[i].name;e.FindPropertyRelative("color").colorValue=saved[i].color;
                    e.FindPropertyRelative("weight").floatValue=saved[i].weight;e.FindPropertyRelative("size").floatValue=saved[i].size;e.FindPropertyRelative("form").enumValueIndex=(int)saved[i].form;
                }
                so.FindProperty("debugView").intValue=debug;so.ApplyModifiedPropertiesWithoutUndo();
                system.GeometryMaskForChecks=7;system.MatureForChecks=false;system.FocusForChecks=null;system.FreezeWindForChecks=false;system.enabled=enabled;
                camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(image);target.Release();UnityEngine.Object.DestroyImmediate(target);
            }
        }
        [MenuItem("Tools/Tactical Maps/Verify Flower Head Stamps")]
        internal static void Verify() => Capture(0);
        internal static void DebugViews() => Capture(2);
        private static void Capture(int debugMode)
        {
            var system=UnityEngine.Object.FindAnyObjectByType<GrassSystem>();
            if(system==null||system.CurrentMap==null||EditorApplication.isPlaying)throw new InvalidOperationException("A built map in Edit Mode is required.");
            Directory.CreateDirectory("Temp/flower-heads");var report=new StringBuilder();
            bool enabled=system.enabled;var so=new SerializedObject(system);int debug=so.FindProperty("debugView").intValue;
            var go=new GameObject("FlowerStampCheckCamera") {hideFlags=HideFlags.HideAndDontSave};
            var camera=go.AddComponent<Camera>();camera.enabled=false;camera.fieldOfView=55;camera.nearClipPlane=.15f;camera.farClipPlane=23000;
            var target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;
            var image=new Texture2D(1280,720,TextureFormat.RGB24,false);
            try
            {
                system.enabled=false;system.enabled=true;system.FreezeWindForChecks=true;system.MatureForChecks=true;
                so.FindProperty("debugView").intValue=debugMode;so.ApplyModifiedPropertiesWithoutUndo();
                var ground=GrassOrbitCapture.FindMeadow(system.CurrentMap);system.FocusForChecks=ground;
                foreach(int layer in new[]{0,4})foreach(int pitch in layer==4?new[]{15,30}:new[]{30,60})
                {
                    camera.cullingMask=layer==4?1<<system.gameObject.layer:~0;
                    system.GeometryMaskForChecks=layer;camera.transform.position=ground+Quaternion.Euler(pitch,0,0)*Vector3.back*150;camera.transform.LookAt(ground);
                    string suffix=debugMode==2?"-lod":"";
                    system.SuppressFlowersForChecks=false;Warm();var on=Read($"Temp/flower-heads/layer-{layer}-{pitch}{suffix}-on.png");
                    system.SuppressFlowersForChecks=true;Warm();var off=Read($"Temp/flower-heads/layer-{layer}-{pitch}{suffix}-off.png");
                    system.SuppressFlowersForChecks=false;
                    var changed=new bool[on.Length];int pixels=0;
                    for(int i=0;i<on.Length;i++)if(Math.Max(Math.Abs(on[i].r-off[i].r),Math.Max(Math.Abs(on[i].g-off[i].g),Math.Abs(on[i].b-off[i].b)))>24){changed[i]=true;pixels++;}
                    int spots=CountSpots(changed,1280,720);
                    report.AppendLine($"layer={layer}, pitch={pitch}: high-contrast pixels={pixels}, isolated 1-40 pixel spots={spots}, draws={system.SubmittedDraws}");
                    if(layer==0&&system.SubmittedDraws!=0)throw new InvalidOperationException("Terrain check submitted geometry.");
                    if(spots<30)throw new InvalidOperationException("Individual default-palette heads are not visibly resolved.");
                }
                report.AppendLine("PASS: default-density individual head marks visible on Terrain and existing distant cards; no additional geometry in Terrain check.");
                void Warm(){for(int i=0;i<64;i++){camera.Render();AsyncGPUReadback.Request(target,0).WaitForCompletion();}}
                Color32[] Read(string path)
                {
                    camera.Render();var previous=RenderTexture.active;
                    try{RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());return image.GetPixels32();}
                    finally{RenderTexture.active=previous;}
                }
            }
            catch(Exception e){report.AppendLine("FAIL: "+e);Debug.LogException(e);}
            finally
            {
                system.GeometryMaskForChecks=7;system.SuppressFlowersForChecks=false;system.MatureForChecks=false;system.FocusForChecks=null;system.FreezeWindForChecks=false;
                so.Update();so.FindProperty("debugView").intValue=debug;so.ApplyModifiedPropertiesWithoutUndo();system.enabled=enabled;
                camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(image);target.Release();UnityEngine.Object.DestroyImmediate(target);
                File.WriteAllText("Temp/flower-heads/"+(debugMode==2?"checks-lod.txt":"checks.txt"),report.ToString());
            }
        }
        private static int CountSpots(bool[] mask,int width,int height)
        {
            int result=0;var queue=new int[mask.Length];
            for(int i=0;i<mask.Length;i++)
            {
                if(!mask[i])continue;int head=0,tail=1;queue[0]=i;mask[i]=false;
                while(head<tail)
                {
                    int p=queue[head++],x=p%width,y=p/width;
                    for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                    {
                        int xx=x+dx,yy=y+dy;if(xx<0||xx>=width||yy<0||yy>=height)continue;int n=yy*width+xx;
                        if(mask[n]){mask[n]=false;queue[tail++]=n;}
                    }
                }
                if(tail>=1&&tail<=40)result++;
            }
            return result;
        }
    }
}
