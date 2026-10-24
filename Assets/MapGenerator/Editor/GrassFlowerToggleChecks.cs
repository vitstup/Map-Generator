using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using TacticalMaps.Grass;
using UnityEditor;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class GrassFlowerToggleChecks
    {
        private static EditorApplication.CallbackFunction tick;
        [MenuItem("Tools/Tactical Maps/Verify Global Flower Toggle")]
        internal static void Verify()
        {
            if(tick!=null)throw new InvalidOperationException("Toggle check already running.");
            var system=UnityEngine.Object.FindAnyObjectByType<GrassSystem>();
            if(EditorApplication.isPlaying||system?.CurrentMap==null)throw new InvalidOperationException("Built meadow in Edit Mode required.");
            const string folder="Logs/FlowerToggle/";Directory.CreateDirectory(folder);var report=new StringBuilder();
            bool original=system.ShowFlowers;var so=new SerializedObject(system);int debug=so.FindProperty("debugView").intValue;
            var go=new GameObject("FlowerToggleCheckCamera"){hideFlags=HideFlags.HideAndDontSave};var camera=go.AddComponent<Camera>();
            camera.enabled=false;camera.fieldOfView=55;camera.nearClipPlane=.15f;camera.farClipPlane=23000;
            var target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;
            var image=new Texture2D(1280,720,TextureFormat.RGB24,false);var captures=new Dictionary<string,Color32[]>();
            var ground=GrassOrbitCapture.FindMeadow(system.CurrentMap);system.MatureForChecks=true;system.FreezeWindForChecks=true;
            so.FindProperty("debugView").intValue=0;so.ApplyModifiedPropertiesWithoutUndo();
            int[] masks={1,2,4,0};string[] modes={"on","off","reference","restored"};int index=0,frame=0;
            File.WriteAllText(folder+"status.txt","Running");
            tick=()=>
            {
                try
                {
                    int mask=masks[index/4];string mode=modes[index%4];string name=mask+"-"+mode;
                    if(frame==0)
                    {
                        system.GeometryMaskForChecks=mask;camera.cullingMask=mask==4?1<<system.gameObject.layer:~0;
                        system.FocusForChecks=mask==0?ground+Vector3.right*10000:ground;
                        system.SuppressFlowersForChecks=mode=="reference";
                        // Force a fresh on-state after reference suppression also regenerated
                        // the packed near/coarse instances without heads.
                        if(mode=="restored")system.ShowFlowers=false;
                        system.ShowFlowers=mode!="off";
                        camera.transform.position=ground+(mask==1?new Vector3(0,2.2f,-6):new Vector3(0,75,-129.904f));
                        camera.transform.LookAt(ground+(mask==1?Vector3.forward*5:Vector3.zero));
                    }
                    camera.Render();frame++;
                    if(frame>=64)
                    {
                        var previous=RenderTexture.active;
                        try{RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();}
                        finally{RenderTexture.active=previous;}
                        captures[name]=image.GetPixels32();File.WriteAllBytes(folder+name+".png",image.EncodeToPNG());
                        if(mode=="off")
                        {
                            int caches=Resources.FindObjectsOfTypeAll<RenderTexture>().Length;
                            int flowerCaches=0;foreach(var texture in Resources.FindObjectsOfTypeAll<RenderTexture>())if(texture.name=="Flower site cache (20 MiB)")flowerCaches++;
                            if(flowerCaches!=0)throw new InvalidOperationException("Disabled flowers retain their cache.");
                        }
                        if(mode=="restored")
                        {
                            double changed=Difference(captures[mask+"-on"],captures[mask+"-off"]);
                            double bare=Difference(captures[mask+"-off"],captures[mask+"-reference"]);
                            double restored=Difference(captures[mask+"-on"],captures[name]);
                            report.AppendLine($"Layer {mask}: on/off difference={changed:F5}/255; off/reference={bare:F5}/255; restored/on={restored:F5}/255; disabled cache count=0");
                            if(changed<.005||bare>.01||restored>.01)throw new InvalidOperationException("Flower toggle did not round-trip on layer "+mask);
                        }
                        index++;frame=0;File.WriteAllText(folder+"status.txt","Completed "+index+"/16");
                        if(index==16){report.AppendLine("PASS: all geometry LODs and Terrain hide and restore flowers; disabled caches released.");Finish("PASS");return;}
                    }
                    EditorApplication.QueuePlayerLoopUpdate();
                }
                catch(Exception e){report.AppendLine("FAIL: "+e);Finish("FAIL");Debug.LogException(e);}
            };
            EditorApplication.update+=tick;
            void Finish(string status)
            {
                EditorApplication.update-=tick;tick=null;
                system.SuppressFlowersForChecks=false;system.ShowFlowers=original;system.GeometryMaskForChecks=7;
                system.MatureForChecks=false;system.FreezeWindForChecks=false;system.FocusForChecks=null;
                so.Update();so.FindProperty("debugView").intValue=debug;so.ApplyModifiedPropertiesWithoutUndo();
                camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(image);target.Release();UnityEngine.Object.DestroyImmediate(target);
                File.WriteAllText(folder+"checks.txt",report.ToString());File.WriteAllText(folder+"status.txt",status);
            }
        }
        private static double Difference(Color32[] a,Color32[] b)
        {double sum=0;for(int i=0;i<a.Length;i++)sum+=Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b);return sum/(a.Length*3);}
    }
}
