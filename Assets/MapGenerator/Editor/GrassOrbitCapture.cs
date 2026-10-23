using System;
using System.IO;
using TacticalMaps.Grass;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace TacticalMaps.Editor
{
    internal static class GrassOrbitCapture
    {
        private static EditorApplication.CallbackFunction tick;
        [MenuItem("Tools/Tactical Maps/Capture Meadow Camera Flight")]
        internal static void Start() => Start(false);
        [MenuItem("Tools/Tactical Maps/Capture Grass Transitions And Beach")]
        internal static void Transitions() => Start(true);
        [MenuItem("Tools/Tactical Maps/Capture Maximum Zoom Orbit")]
        internal static void MaximumOrbit() => Start(false,true);
        private static void Start(bool transitions,bool maximum=false)
        {
            if(tick!=null) throw new InvalidOperationException("A meadow capture is already running.");
            var system=UnityEngine.Object.FindAnyObjectByType<GrassSystem>();
            if(system==null||system.CurrentMap==null) throw new InvalidOperationException("Configure grass first.");
            var ground=maximum?FindInteriorMeadow(system.CurrentMap):FindMeadow(system.CurrentMap);
            var beach=transitions?FindBeach(system.CurrentMap,ground):ground;
            var cameraObject=new GameObject("MeadowFlightCamera"){hideFlags=HideFlags.HideAndDontSave};
            var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.fieldOfView=55;camera.nearClipPlane=.15f;camera.farClipPlane=23000;
            var extra=cameraObject.AddComponent<UniversalAdditionalCameraData>();extra.renderPostProcessing=true;
            extra.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;extra.antialiasingQuality=AntialiasingQuality.High;
            var target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;
            var pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
            string folder=maximum?"Temp/grass-max-orbit":transitions?"Temp/grass-transitions":"Temp/meadow-flight";Directory.CreateDirectory(folder);
            var keys=new[]{new Vector3(0,2.5f,-8),new Vector3(12,3,-10),new Vector3(0,45,-65),
                new Vector3(0,70,-.1f),new Vector3(0,270,-400),new Vector3(90,40,-50),new Vector3(12,3,-8)};
            Vector3[] targets=null;
            if(transitions)
            {
                var shore=beach-ground;
                keys=new[]{new Vector3(0,2.5f,-8),new Vector3(0,35,-55),new Vector3(0,110,-.1f),
                    new Vector3(0,2.5f,-8),new Vector3(90,3,-8),new Vector3(0,3,-8),
                    shore+new Vector3(0,25,-35),shore+new Vector3(0,3,-8),shore+new Vector3(0,50,-60)};
                targets=new[]{Vector3.zero,Vector3.zero,Vector3.zero,Vector3.zero,new Vector3(90,0,0),Vector3.zero,shore,shore,shore};
            }
            int frames=transitions||maximum?480:240;int frame=-60;
            File.WriteAllText(folder+"-status.txt","Running");
            tick=()=>
            {
                try
                {
                    float t=Mathf.Clamp01((float)frame/(frames-1))*(keys.Length-1);int segment=Mathf.Min(keys.Length-2,(int)t);
                    float blend=Mathf.SmoothStep(0,1,t-segment);
                    camera.transform.position=ground+Vector3.Lerp(keys[segment],keys[segment+1],blend);
                    camera.transform.LookAt(ground+(targets!=null?Vector3.Lerp(targets[segment],targets[segment+1],blend):Vector3.zero)+Vector3.up*.3f);
                    if(maximum)
                    {
                        float progress=Mathf.Clamp01((float)frame/(frames-1));
                        float pitch=progress<.3333f?Mathf.Lerp(5,85,Mathf.SmoothStep(0,1,progress*3)):
                            progress<.6667f?Mathf.Lerp(85,5,Mathf.SmoothStep(0,1,(progress-.3333f)*3)):
                            Mathf.Lerp(5,85,Mathf.SmoothStep(0,1,(progress-.6667f)*3));
                        var focus=ground+Vector3.right*(progress>.3333f?Mathf.SmoothStep(0,1,(progress-.3333f)*1.5f)*90:0);
                        float yaw=progress>.6667f?Mathf.SmoothStep(0,1,(progress-.6667f)*3)*90:0;
                        camera.transform.position=focus+Quaternion.Euler(pitch,yaw,0)*Vector3.back*150;
                        camera.transform.LookAt(focus);system.FocusForChecks=focus;
                    }
                    camera.Render();
                    if(frame>=0)
                    {
                        var previous=RenderTexture.active;
                        try {RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();}
                        finally {RenderTexture.active=previous;}
                        File.WriteAllBytes(folder+"/frame-"+frame.ToString("D4")+".jpg",pixels.EncodeToJPG(92));
                    }
                    frame++;
                    if(frame>=frames) {Finish("PASS: captured "+frames+" frames. Ground="+ground);return;}
                    EditorApplication.QueuePlayerLoopUpdate();
                }
                catch(Exception e) {Finish("FAIL: "+e);Debug.LogException(e);}
            };
            EditorApplication.update+=tick;
            void Finish(string message)
            {
                EditorApplication.update-=tick;tick=null;camera.targetTexture=null;system.FocusForChecks=null;
                UnityEngine.Object.DestroyImmediate(cameraObject);target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixels);
                File.WriteAllText(folder+"-status.txt",message);Debug.Log(message);
            }
        }
        private static Vector3 FindBeach(GrassMap map,Vector3 meadow)
        {
            Vector3 best=meadow;float distance=float.PositiveInfinity;
            for(int level=0;level<3;level++)
            {
                var terrain=map.Terrains[level];var data=terrain.terrainData;var mask=map.Coverage[level];
                for(int z=2;z<mask.height-2;z++) for(int x=2;x<mask.width-2;x++)
                {
                    var cover=mask.GetPixel(x,z);if(cover.b<.25f||cover.b>.8f) continue;
                    float u=(x+.5f)/mask.width,v=(z+.5f)/mask.height;
                    var position=terrain.transform.position+new Vector3(u*data.size.x,data.GetInterpolatedHeight(u,v),v*data.size.z);
                    if(position.y<map.SeaLevel+.4f) continue;
                    // Ignore the holes occupied by the inner Terrain.
                    if(level>0) {var inner=map.Terrains[level-1];var p=inner.transform.position;var size=inner.terrainData.size;if(position.x>=p.x&&position.z>=p.z&&position.x<p.x+size.x&&position.z<p.z+size.z)continue;}
                    float d=(position-meadow).sqrMagnitude;if(d<distance){distance=d;best=position;}
                }
            }
            if(float.IsPositiveInfinity(distance)) throw new InvalidOperationException("No beach found for capture.");
            return best;
        }
        private static Vector3 FindInteriorMeadow(GrassMap map)
        {
            var terrain=map.Terrains[0];var data=terrain.terrainData;var mask=map.Coverage[0];var colors=mask.GetPixels32();
            var fallback=FindMeadow(map);var best=fallback;float distance=float.PositiveInfinity;
            int step=Mathf.CeilToInt(250*mask.width/data.size.x);
            for(int z=step;z<mask.height-step;z+=4) for(int x=step;x<mask.width-step;x+=4)
            {
                bool clear=true;
                for(int dz=-1;dz<=1&&clear;dz++) for(int dx=-1;dx<=1;dx++)
                {
                    var cover=colors[(z+dz*step)*mask.width+x+dx*step];
                    if(cover.r<110||cover.b>3) {clear=false;break;}
                }
                if(!clear) continue;
                float u=(x+.5f)/mask.width,v=(z+.5f)/mask.height;
                if(data.GetInterpolatedNormal(u,v).y<.98f) continue;
                var position=terrain.transform.position+new Vector3(u*data.size.x,data.GetInterpolatedHeight(u,v),v*data.size.z);
                float d=(position-fallback).sqrMagnitude;if(d<distance){distance=d;best=position;}
            }
            return best;
        }
        internal static Vector3 FindMeadow(GrassMap map)
        {
            var terrain=map.Terrains[0];var data=terrain.terrainData;var mask=map.Coverage[0];var colors=mask.GetPixels32();
            Vector2 uv=new Vector2(.5f,.5f);float best=-1;
            for(int z=10;z<mask.height-10;z+=3) for(int x=10;x<mask.width-10;x+=3)
            {
                float u=(x+.5f)/mask.width,v=(z+.5f)/mask.height;
                float height=terrain.transform.position.y+data.GetInterpolatedHeight(u,v);
                float score=colors[z*mask.width+x].r/255f-Vector2.Distance(new Vector2(u,v),new Vector2(.5f,.5f))*.2f;
                if(height>map.SeaLevel+3&&data.GetInterpolatedNormal(u,v).y>.98f&&score>best){best=score;uv=new Vector2(u,v);}
            }
            return terrain.transform.position+new Vector3(uv.x*data.size.x,data.GetInterpolatedHeight(uv.x,uv.y),uv.y*data.size.z);
        }
    }
}
