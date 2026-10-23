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
        internal static void Start()
        {
            if(tick!=null) throw new InvalidOperationException("A meadow capture is already running.");
            var system=UnityEngine.Object.FindAnyObjectByType<GrassSystem>();
            if(system==null||system.CurrentMap==null) throw new InvalidOperationException("Configure grass first.");
            var ground=FindMeadow(system.CurrentMap);
            var cameraObject=new GameObject("MeadowFlightCamera"){hideFlags=HideFlags.HideAndDontSave};
            var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.fieldOfView=55;camera.nearClipPlane=.15f;camera.farClipPlane=23000;
            var extra=cameraObject.AddComponent<UniversalAdditionalCameraData>();extra.renderPostProcessing=true;
            extra.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;extra.antialiasingQuality=AntialiasingQuality.High;
            var target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;
            var pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
            const string folder="Temp/meadow-flight";Directory.CreateDirectory(folder);
            var keys=new[]{new Vector3(0,2.5f,-8),new Vector3(12,3,-10),new Vector3(0,45,-65),
                new Vector3(0,70,-.1f),new Vector3(0,270,-400),new Vector3(90,40,-50),new Vector3(12,3,-8)};
            const int frames=240;int frame=-45;
            File.WriteAllText("Temp/meadow-flight-status.txt","Running");
            tick=()=>
            {
                try
                {
                    float t=Mathf.Clamp01((float)frame/(frames-1))*(keys.Length-1);int segment=Mathf.Min(keys.Length-2,(int)t);
                    float blend=Mathf.SmoothStep(0,1,t-segment);
                    camera.transform.position=ground+Vector3.Lerp(keys[segment],keys[segment+1],blend);
                    camera.transform.LookAt(ground+Vector3.up*.3f);
                    camera.Render();
                    if(frame>=0)
                    {
                        var previous=RenderTexture.active;
                        try {RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();}
                        finally {RenderTexture.active=previous;}
                        File.WriteAllBytes(folder+"/frame-"+frame.ToString("D4")+".jpg",pixels.EncodeToJPG(92));
                    }
                    frame++;
                    if(frame>=frames) {Finish("PASS: captured 240 frames, low / RTS / top / far / return. Ground="+ground);return;}
                    EditorApplication.QueuePlayerLoopUpdate();
                }
                catch(Exception e) {Finish("FAIL: "+e);Debug.LogException(e);}
            };
            EditorApplication.update+=tick;
            void Finish(string message)
            {
                EditorApplication.update-=tick;tick=null;camera.targetTexture=null;
                UnityEngine.Object.DestroyImmediate(cameraObject);target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixels);
                File.WriteAllText("Temp/meadow-flight-status.txt",message);Debug.Log(message);
            }
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
