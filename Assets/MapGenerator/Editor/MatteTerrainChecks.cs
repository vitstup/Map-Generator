using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TacticalMaps.Editor
{
    internal static class MatteTerrainChecks
    {
        [MenuItem("Tools/Tactical Maps/Capture Matte Terrain Angles")]
        private static void CaptureSceneAngles()
        {
            var builder = Object.FindAnyObjectByType<TacticalMaps.Construction.MapBuilder>();
            var terrain = Array.Find(builder.GetComponentsInChildren<Terrain>(),t=>t.name=="Terrain_Main");
            var size = terrain.terrainData.size;
            float x = terrain.transform.position.x+size.x*0.25f, z = terrain.transform.position.z+size.z*0.65f;
            var point = new Vector3(x,terrain.SampleHeight(new Vector3(x,0,z))+terrain.transform.position.y,z);
            var go = MapVisualChecks.CameraAt(point,85,Vector3.up*200,1<<terrain.gameObject.layer);
            var camera = go.GetComponent<Camera>();
            try
            {
                camera.transform.rotation=Quaternion.Euler(90,0,0);
                MapVisualChecks.Capture(camera,"Temp/matte-top.png",1280,800);
                camera.transform.position=point+new Vector3(0,25,-198);camera.transform.LookAt(point);
                MapVisualChecks.Capture(camera,"Temp/matte-grazing.png",1280,800);
                camera.transform.position=point+new Vector3(0,1600,-2000);camera.transform.LookAt(point);camera.orthographicSize=800;
                MapVisualChecks.Capture(camera,"Temp/matte-distance.png",1280,800);
            }
            finally { Object.DestroyImmediate(go); }
        }
        [MenuItem("Tools/Tactical Maps/Verify Matte Terrain")]
        private static void Run()
        {
            var owned = new List<Object>();
            var root = new GameObject("Matte terrain regression") { hideFlags = HideFlags.HideAndDontSave, layer = 29 };
            GameObject cameraObject = null;
            try
            {
                var data = new TerrainData { heightmapResolution = 33, size = new Vector3(256, 10, 256), alphamapResolution = 32, baseMapResolution = 128 };
                owned.Add(data);
                var terrain = root.AddComponent<Terrain>(); terrain.terrainData = data; terrain.drawInstanced = true;
                terrain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; terrain.basemapDistance = 20000;
                root.transform.position = new Vector3(30000, 0, 30000);
                var material = new Material(Shader.Find("TacticalMaps/Terrain/MatteStochastic")) { enableInstancing = true };
                material.EnableKeyword("_ALPHATEST_ON"); material.SetVector("_TacticalTerrainSize",new Vector4(256,256,0,0));
                terrain.materialTemplate = material; owned.Add(material);
                var layers = new TerrainLayer[6];
                for (int i = 0; i < layers.Length; i++)
                {
                    var texture = new Texture2D(64,64,TextureFormat.RGBA32,true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 16 };
                    owned.Add(texture); var pixels = new Color[64*64];
                    for(int p=0;p<pixels.Length;p++) pixels[p] = i == 5 ? new Color(0.06f,0.12f,0.6f,1) : new Color(0.3f,0.5f,0.15f,1);
                    texture.SetPixels(pixels); texture.Apply();
                    layers[i] = new TerrainLayer { diffuseTexture = texture, tileSize = new Vector2(8,8), smoothness = 1, metallic = 0 };
                    owned.Add(layers[i]);
                }
                data.terrainLayers = layers;
                SetCoverage(data,0);
                var point = root.transform.position + new Vector3(128,0,128);
                cameraObject = MapVisualChecks.CameraAt(point, 80, new Vector3(0,180,-87.178f),1<<29);
                var camera = cameraObject.GetComponent<Camera>();
                var overhead = MapVisualChecks.Capture(camera,"Temp/matte-solid-high.png",128,128);
                camera.transform.position = point + new Vector3(0,25,-198.431f); camera.transform.LookAt(point);
                var low = MapVisualChecks.Capture(camera,"Temp/matte-solid-low.png",128,128);
                float angleError = CenterDifference(overhead,low,128);
                Check(angleError < 0.015f, "View angle must not introduce a specular wash: " + angleError);
                SetCoverage(data,5);
                var add = MapVisualChecks.Capture(camera,null,128,128);
                var center = add[64*128+64]; Check(center.b > center.r*1.5f && center.b > 10, "Layers 5/6 use the custom additive pass");
                SetCoverage(data,0);
                camera.transform.position = point + Vector3.up*200; camera.transform.rotation=Quaternion.Euler(90,0,0); camera.orthographicSize=128;
                var patterned = layers[0].diffuseTexture; var pattern = new Color[64*64];
                for(int y=0;y<64;y++) for(int x=0;x<64;x++)
                {
                    float value = ((x/8 + y/8)%2==0 ? 0.25f : 0.7f) + ((x*17+y*31)%13)*0.008f;
                    pattern[y*64+x] = new Color(value,value*0.8f,value*0.55f,1);
                }
                patterned.SetPixels(pattern); patterned.Apply(); data.SetBaseMapDirty(); terrain.Flush();
                material.SetFloat("_StochasticStrength",0);
                var regular = MapVisualChecks.Capture(camera,"Temp/terrain-regular-test.png",512,512);
                material.SetFloat("_StochasticStrength",1);
                var stochastic = MapVisualChecks.Capture(camera,"Temp/terrain-stochastic-test.png",512,512);
                float repeatPlain = PeriodError(regular,512,16), repeatRandom = PeriodError(stochastic,512,16);
                Check(repeatRandom > repeatPlain + 0.025f, $"Stochastic sampling must break tile-period repetition ({repeatPlain:F4} -> {repeatRandom:F4})");
                // A synchronized offscreen comparison, including CPU submission/readback (not a pure GPU timer).
                double regularMs = Benchmark(camera,material,0), randomMs = Benchmark(camera,material,1);
                terrain.basemapDistance=1;
                var far = MapVisualChecks.Capture(camera,"Temp/matte-basemap-test.png",128,128);
                Check(far[64*128+64].r > 5, "Custom matte basemap remains visible");
                var frameTimings = new FrameTiming[1]; FrameTimingManager.CaptureFrameTimings();
                uint count = FrameTimingManager.GetLatestTimings(1,frameTimings);
                string gpu = count>0 && frameTimings[0].gpuFrameTime>0 ? frameTimings[0].gpuFrameTime.ToString("F2")+" ms (whole editor frame)" : "unavailable in current Editor configuration";
                File.WriteAllText("Temp/matte-terrain-checks.txt",$"PASS: angle RGB error={angleError:F4}; additive layers; tile-shift difference {repeatPlain:F4} -> {repeatRandom:F4}; matte basemap. Synchronized offscreen render/readback 512px: regular={regularMs:F2}ms stochastic={randomMs:F2}ms. GPU frame timing: {gpu}.");
            }
            catch(Exception e) { File.WriteAllText("Temp/matte-terrain-checks.txt",e.ToString()); UnityEngine.Debug.LogException(e); }
            finally
            {
                if(cameraObject!=null) Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(root); foreach(var value in owned) if(value!=null) Object.DestroyImmediate(value);
            }
        }
        private static void SetCoverage(TerrainData data,int layer)
        {
            var weights = new float[32,32,6]; for(int y=0;y<32;y++) for(int x=0;x<32;x++) weights[y,x,layer]=1;
            data.SetAlphamaps(0,0,weights);
        }
        private static float CenterDifference(Color32[] a,Color32[] b,int n)
        {
            float difference=0; int count=0;
            for(int y=n/2-4;y<n/2+4;y++) for(int x=n/2-4;x<n/2+4;x++)
            { var p=a[y*n+x];var q=b[y*n+x]; difference+=Mathf.Abs(p.r-q.r)+Mathf.Abs(p.g-q.g)+Mathf.Abs(p.b-q.b);count++; }
            return difference/(count*3*255);
        }
        private static float PeriodError(Color32[] pixels,int width,int shift)
        {
            double result=0;int count=0;
            for(int y=64;y<width-64;y++) for(int x=64;x<width-64-shift;x++)
            {var a=pixels[y*width+x];var b=pixels[y*width+x+shift];result+=Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b);count++;}
            return (float)(result/(count*3*255));
        }
        private static double Benchmark(Camera camera,Material material,float strength)
        {
            material.SetFloat("_StochasticStrength",strength);MapVisualChecks.Capture(camera,null,512,512);
            var watch=Stopwatch.StartNew();for(int i=0;i<5;i++)MapVisualChecks.Capture(camera,null,512,512);
            return watch.Elapsed.TotalMilliseconds/5;
        }
        private static void Check(bool condition,string message) { if(!condition) throw new Exception("Matte terrain check failed: "+message); }
    }
}
