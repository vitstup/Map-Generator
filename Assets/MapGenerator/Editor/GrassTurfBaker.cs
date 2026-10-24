using System;
using System.Threading;
using TacticalMaps.Generation;
using TacticalMaps.Grass;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class GrassTurfBaker
    {
        [MenuItem("Tools/Tactical Maps/Bake Meadow Surface")]
        internal static void Bake()
        {
            var library=GrassSetup.EnsureLibrary();
            var scene=EditorSceneManager.NewPreviewScene();
            Texture2D image=null; RenderTexture target=null; MapData map=null;
            try
            {
                var systemObject=new GameObject("TurfBakeSystem"); systemObject.layer=30;
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(systemObject,scene);
                var system=systemObject.AddComponent<GrassSystem>();
                system.GeometryMaskForChecks=1;
                var config=new SerializedObject(system); config.FindProperty("library").objectReferenceValue=library;
                config.FindProperty("density").floatValue=1; config.FindProperty("windStrength").floatValue=0;
                config.FindProperty("chunksPreparedPerFrame").intValue=16; config.ApplyModifiedPropertiesWithoutUndo();
                var root=new GameObject("TurfBakeGround"); root.SetActive(false);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
                var names=new[]{"Terrain_Main","Terrain_Surroundings","Terrain_Horizon"};
                var weights=new float[3][,,]; var sizes=new Vector2[3];
                for(int i=0;i<3;i++)
                {
                    var go=new GameObject(names[i]); go.transform.SetParent(root.transform); go.layer=31;
                    var data=new TerrainData {heightmapResolution=33,size=new Vector3(128*Mathf.Pow(4,i),10,128*Mathf.Pow(4,i))};
                    var terrain=go.AddComponent<Terrain>(); terrain.terrainData=data;
                    go.transform.position=new Vector3(-data.size.x*.5f,2,-data.size.z*.5f);
                    var heights=new float[33,33]; for(int z=0;z<33;z++) for(int x=0;x<33;x++) heights[z,x]=.5f;
                    data.SetHeights(0,0,heights); terrain.Flush();
                    weights[i]=new float[16,16,6]; for(int z=0;z<16;z++) for(int x=0;x<16;x++) weights[i][z,x,0]=1;
                    sizes[i]=new Vector2(data.size.x,data.size.z);
                }
                var surface=new SurfaceData(new TerrainLayer[6],weights,Vector2.zero);
                map=new MapData(new Texture2D[3],sizes,10,0,4000,Vector2Int.zero,grass:GrassCoverage.Generate(surface,CancellationToken.None));
                system.Stage(map,root.transform); root.SetActive(true);
                var cameraObject=new GameObject("TurfBakeCamera"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject,scene);
                var camera=cameraObject.AddComponent<Camera>(); camera.enabled=false; camera.scene=scene;
                camera.cullingMask=1<<30; camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.clear;
                camera.orthographic=true; camera.orthographicSize=2; camera.nearClipPlane=.1f; camera.farClipPlane=40;
                camera.transform.SetPositionAndRotation(new Vector3(0,15,0),Quaternion.Euler(90,0,0));
                target=new RenderTexture(512,512,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear); target.Create(); camera.targetTexture=target;
                image=new Texture2D(512,512,TextureFormat.RGBA32,true,true);
                system.BakeMode=1; for(int i=0;i<25;i++) camera.Render();
                var albedo=Read(camera,target,image,false);
                System.IO.File.WriteAllBytes("Temp/turf-bake-debug.png",image.EncodeToPNG());
                Debug.Log("Turf bake GPU counts: "+string.Join(",",system.ReadCountsForChecks(camera)));
                system.BakeMode=2; var normals=Read(camera,target,image,true);
                system.BakeMode=3; var flowers=Read(camera,target,image,false,true);
                int covered=0; foreach(var p in albedo) if(p.a>0) covered++;
                if(covered < albedo.Length*.45f) throw new InvalidOperationException("Turf bake is too sparse: "+(float)covered/albedo.Length);
                var turf=Save("TurfAlbedo",albedo,false); var normal=Save("TurfNormal",normals,true);
                var mask=Save("TurfFlowers",flowers,false,true);
                var lib=new SerializedObject(library); lib.FindProperty("turfAlbedo").objectReferenceValue=turf;
                lib.FindProperty("turfNormal").objectReferenceValue=normal; lib.ApplyModifiedPropertiesWithoutUndo();
                lib.Update();lib.FindProperty("turfFlowers").objectReferenceValue=mask;lib.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
                foreach(var live in UnityEngine.Object.FindObjectsByType<GrassSystem>(FindObjectsSortMode.None))
                    if(live!=system&&live.CurrentMap!=null) live.Attach(live.CurrentMap);
                Debug.Log($"Baked meadow albedo/normal from actual GPU plants: {covered*100f/albedo.Length:F1}% top-down coverage.");
            }
            finally
            {
                map?.Release(); if(image!=null) UnityEngine.Object.DestroyImmediate(image);
                if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}
                foreach(var root in scene.GetRootGameObjects()) foreach(var terrain in root.GetComponentsInChildren<Terrain>(true))
                    if(terrain.terrainData!=null) UnityEngine.Object.DestroyImmediate(terrain.terrainData);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
        private static Color32[] Read(Camera camera,RenderTexture target,Texture2D image,bool normal,bool mask=false)
        {
            camera.Render(); var previous=RenderTexture.active;
            try { RenderTexture.active=target;image.ReadPixels(new Rect(0,0,512,512),0,0);image.Apply(); }
            finally {RenderTexture.active=previous;}
            var pixels=image.GetPixels32();
            for(int i=0;i<pixels.Length;i++) if(pixels[i].a==0) pixels[i]=mask?new Color32(0,0,0,0):normal?new Color32(128,128,255,0):new Color32(208,208,208,0);
            // Make texture edges periodic without losing the real coverage alpha channel.
            for(int y=0;y<512;y++) for(int x=0;x<12;x++)
            {
                var a=pixels[y*512+x];var b=pixels[y*512+511-x];var c=Color32.Lerp(a,b,.5f);
                float w=1-x/12f; pixels[y*512+x]=Color32.Lerp(a,c,w); pixels[y*512+511-x]=Color32.Lerp(b,c,w);
            }
            for(int x=0;x<512;x++) for(int y=0;y<12;y++)
            {
                var a=pixels[y*512+x];var b=pixels[(511-y)*512+x];var c=Color32.Lerp(a,b,.5f);
                float w=1-y/12f;pixels[y*512+x]=Color32.Lerp(a,c,w);pixels[(511-y)*512+x]=Color32.Lerp(b,c,w);
            }
            return pixels;
        }
        private static Texture2D Save(string name,Color32[] pixels,bool normal,bool mask=false)
        {
            string path=GrassSetup.Folder+"/"+name+".asset";
            var old=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var texture=old!=null?old:new Texture2D(512,512,mask?TextureFormat.R8:TextureFormat.RGBA32,true,true);
            texture.name=name; texture.wrapMode=TextureWrapMode.Repeat;texture.filterMode=FilterMode.Trilinear;texture.anisoLevel=8;
            texture.SetPixels32(pixels);texture.Apply(true,false);
            if(old==null) AssetDatabase.CreateAsset(texture,path); else EditorUtility.SetDirty(texture);
            return texture;
        }
    }
}
