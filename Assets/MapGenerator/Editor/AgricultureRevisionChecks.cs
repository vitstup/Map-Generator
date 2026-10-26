using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using TacticalMaps.Construction;
using TacticalMaps.Generation;
using TacticalMaps.Grass;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace TacticalMaps.Editor
{
    internal static class AgricultureRevisionChecks
    {
        private const string Folder = "Logs/AgricultureRevision";
        [MenuItem("Tools/Tactical Maps/Verify Agriculture Revision")]
        internal static void Verify()
        {
            Directory.CreateDirectory(Folder); var report = new StringBuilder();
            var generator = Object.FindAnyObjectByType<MapGenerator>(); var source = Object.FindAnyObjectByType<MapBuilder>();
            try
            {
                foreach (var point in new[] { new Vector2Int(2618,1858), new Vector2Int(2592,2016), new Vector2Int(2496,2240) })
                {
                    var clock = Stopwatch.StartNew(); var map = generator.Generate(point); double generation = clock.Elapsed.TotalMilliseconds;
                    try
                    {
                        var a = map.Agriculture; bool mountain = point == new Vector2Int(2496,2240);
                        Require(a != null, "Agriculture enabled");
                        if (mountain) Require(a.Fields.Length <= 10 && a.Farms.Length <= 2, "Mountain density budget");
                        else Require(a.Farms.Length >= 300 && a.Farms.Length <= 500 && a.Fields.Length <= 3000, "Dense landscape budget");
                        Require(a.Fields.Length == 0 || a.Fields.Max(f => f.halfSize.x * f.halfSize.y) / a.Fields.Min(f => f.halfSize.x * f.halfSize.y) > 3, "Diverse field areas");
                        report.AppendLine($"{point}: {a.Fields.Length} fields, {a.Farms.Length} farms, {a.Tracks.Length} tracks; complete generation {generation:F1} ms; agriculture {generator.GetComponent<AgricultureGenerator>().LastTimings}");
                        if (!mountain)
                        {
                            var go = new GameObject("AgricultureRevisionTemporaryBuild"); go.SetActive(false);
                            try
                            {
                                var builder = CopyBuilder(source, go); clock.Restart(); builder.Build(map);
                                report.AppendLine($"  complete build {clock.Elapsed.TotalMilliseconds:F1} ms; agriculture build {go.GetComponent<AgricultureBuilder>().LastBuildMilliseconds:F1} ms; detail tiles {a.Surface.Tiles.Length}");
                                VerifyWater(map); VerifyAtlas(a);
                            }
                            finally { Object.DestroyImmediate(go); }
                        }
                    }
                    finally { map.Release(); }
                }
                report.AppendLine("PASS: real high/low density selections, variable parcel areas, bounded populations, sparse contour distances and flat paddy beds.");
            }
            catch (Exception e) { report.AppendLine("FAIL: " + e); throw; }
            finally { File.WriteAllText(Folder + "/verification.txt", report.ToString()); UnityEngine.Debug.Log(report.ToString()); }
        }
        private static MapBuilder CopyBuilder(MapBuilder source, GameObject go)
        {
            var builder = go.AddComponent<MapBuilder>();
            foreach (var c in source.GetComponents<MonoBehaviour>())
            { if (c is MapBuilder) continue; var target = go.GetComponent(c.GetType()) ?? go.AddComponent(c.GetType()); EditorUtility.CopySerialized(c, target); }
            var settings = new SerializedObject(builder); settings.FindProperty("center").vector3Value = new SerializedObject(source).FindProperty("center").vector3Value; settings.ApplyModifiedPropertiesWithoutUndo();
            return builder;
        }
        private static void VerifyAtlas(AgricultureData data)
        {
            var surface = data.Surface;
            foreach (var field in data.Fields.Where(f => Mathf.Abs(f.center.x) < 900 && Mathf.Abs(f.center.y) < 900).Take(12))
            {
                var p = field.Point(field.halfSize.x, 0); var cell = new Vector2Int(Mathf.FloorToInt((p.x - surface.Origin.x) / 128), Mathf.FloorToInt((p.y - surface.Origin.y) / 128));
                int index = Array.IndexOf(surface.Keys, cell.y * surface.Columns + cell.x); Require(index >= 0, "Contour tile exists");
                float x = p.x - surface.Origin.x - cell.x * 128 + .5f, z = p.y - surface.Origin.y - cell.y * 128 + .5f;
                int ix = Mathf.FloorToInt(x), iz = Mathf.FloorToInt(z); var pixels = surface.Tiles[index];
                float d = Mathf.Lerp(Mathf.Lerp(AgricultureSurfaceData.DecodeDistance(pixels[iz * 130 + ix]), AgricultureSurfaceData.DecodeDistance(pixels[iz * 130 + ix + 1]), x - ix), Mathf.Lerp(AgricultureSurfaceData.DecodeDistance(pixels[(iz + 1) * 130 + ix]), AgricultureSurfaceData.DecodeDistance(pixels[(iz + 1) * 130 + ix + 1]), x - ix), z - iz);
                Require(Mathf.Abs(d) < .2f, "Sub-texel contour accuracy");
            }
        }
        private static void VerifyWater(MapData map)
        {
            var terrain = map.BuiltRoot.Find("Terrain_Main").GetComponent<Terrain>(); var p = terrain.transform.position; var s = terrain.terrainData.size;
            foreach (var f in map.Agriculture.Fields.Where(f => f.crop == 3 && Mathf.Abs(f.center.x) + f.halfSize.magnitude < s.x / 2 && Mathf.Abs(f.center.y) + f.halfSize.magnitude < s.z / 2))
            {
                float low = float.MaxValue, high = float.MinValue;
                for (int z = -1; z <= 1; z++) for (int x = -1; x <= 1; x++)
                { var q = f.Point(x * f.halfSize.x * .6f, z * f.halfSize.y * .6f); float h = terrain.SampleHeight(new Vector3(q.x,0,q.y)) + p.y; low = Mathf.Min(low,h); high = Mathf.Max(high,h); }
                Require(high - low < .05f && f.height + p.y - high > .09f, "Paddy bed lies below a single water level");
            }
        }
        [MenuItem("Tools/Tactical Maps/Capture Agriculture Revision Details")]
        internal static void CaptureDetails()
        {
            Directory.CreateDirectory(Folder); var scene = EditorSceneManager.NewPreviewScene();
            MapData map = null; GameObject builderObject = null; GameObject cameraObject = null; RenderTexture target = null; Texture2D image = null;
            var report = new StringBuilder();
            try
            {
                var library = AssetDatabase.LoadAssetAtPath<AgricultureLibrary>(AgricultureSetup.Folder + "/AgricultureLibrary.asset");
                var sizes = new[] { Vector2.one * 512, Vector2.one * 1024, Vector2.one * 2048 }; var textures = new Texture2D[3];
                for (int level = 0; level < 3; level++)
                {
                    int n = 513; var pixels = new ushort[n*n];
                    for (int z = 0; z < n; z++) for (int x = 0; x < n; x++) { float wx = ((float)x/(n-1)-.5f)*sizes[level].x, wz = ((float)z/(n-1)-.5f)*sizes[level].y; pixels[z*n+x]=(ushort)Mathf.RoundToInt((62+wx*.001f+wz*.0005f)/256*65535); }
                    textures[level]=new Texture2D(n,n,TextureFormat.R16,false,true); textures[level].SetPixelData(pixels,0); textures[level].Apply(false,false);
                }
                var fields = new[] { new FarmField { center=new Vector2(-105,55),halfSize=new Vector2(65,58),angle=.18f,crop=3,height=61.9225f,variation=.3f }, new FarmField { center=new Vector2(90,60),halfSize=new Vector2(80,36),angle=-.13f,crop=0,height=62.12f,variation=.6f }, new FarmField { center=new Vector2(-100,-110),halfSize=new Vector2(60,24),angle=.1f,crop=2,height=61.85f,variation=.7f } };
                var footprint=library.houses[0].GetComponent<FarmBuildingGrounding>().halfFootprint;
                var farms=new[]{new Farmstead{center=new Vector2(70,-80),angle=.1f,variant=0,halfFootprint=footprint,houseHeight=62.03f,storeHeight=62.05f}};
                var tracks=new[]{new FarmTrack{a=farms[0].center,b=new Vector2(190,-145),controlA=new Vector2(110,-70),controlB=new Vector2(160,-100)}};
                var agriculture=new AgricultureData(fields,farms,null,new[]{1024,1024,1024},tracks); agriculture.BuildSurfaces(sizes,CancellationToken.None);
                var biome=Object.FindAnyObjectByType<BiomeGenerator>(); var serialized=new SerializedObject(biome);var layers=new TerrainLayer[6];string[] names={"meadow","dryGrass","earth","rock","sand","silt"};for(int i=0;i<6;i++)layers[i]=(TerrainLayer)serialized.FindProperty(names[i]).objectReferenceValue;
                var weights=new float[3][,,];for(int i=0;i<3;i++){weights[i]=new float[64,64,6];for(int z=0;z<64;z++)for(int x=0;x<64;x++)weights[i][z,x,0]=1;}
                var surface=new SurfaceData(layers,weights,Vector2.zero);var coverage=GrassCoverage.Generate(surface,CancellationToken.None);coverage.ExcludeAgriculture(agriculture,sizes);
                map=new MapData(textures,sizes,256,10,256,Vector2Int.zero,1,surface,null,null,coverage);map.SetAgriculture(agriculture);
                var source=Object.FindAnyObjectByType<MapBuilder>();builderObject=new GameObject("Agriculture detail control");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(builderObject,scene);var builder=CopyBuilder(source,builderObject);builder.Build(map);
                var grassSource=Object.FindAnyObjectByType<GrassSystem>();var grassObject=new GameObject("Control meadow");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(grassObject,scene);var grass=grassObject.AddComponent<GrassSystem>();AgricultureSetup.Set(grass,"library",grassSource.Library);grass.Build(map);grass.FreezeWindForChecks=true;grass.MatureForChecks=true;
                var lightSource=Object.FindObjectsByType<Light>(FindObjectsSortMode.None).First(l=>l.type==LightType.Directional);var lightObject=new GameObject("Control sun");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightObject,scene);var light=lightObject.AddComponent<Light>();EditorUtility.CopySerialized(lightSource,light);light.transform.rotation=lightSource.transform.rotation;
                cameraObject=new GameObject("AgricultureDetailCamera");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject,scene);var camera=cameraObject.AddComponent<Camera>();camera.CopyFrom(Camera.main);camera.scene=scene;camera.enabled=false;camera.fieldOfView=55;camera.nearClipPlane=.15f;camera.farClipPlane=5000;
                var extra=cameraObject.AddComponent<UniversalAdditionalCameraData>();extra.renderPostProcessing=Camera.main.GetUniversalAdditionalCameraData().renderPostProcessing;extra.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                target=new RenderTexture(1400,900,24);target.Create();camera.targetTexture=target;image=new Texture2D(1400,900,TextureFormat.RGB24,false);
                var terrains=map.BuiltRoot.GetComponentsInChildren<Terrain>();Vector3 Ground(Vector2 p)=>AgricultureBuilder.Ground(p,terrains,Vector3.zero);
                var bank=Ground(fields[0].Point(10,-fields[0].halfSize.y));
                Shot("bank-close",bank+new Vector3(4,2.3f,-4),bank+Vector3.up*.3f);
                Shot("water-edge",bank+new Vector3(0,1.6f,-3),bank+new Vector3(0,.05f,3));
                var earth=Ground(fields[1].center);Shot("soil-close",earth+new Vector3(0,3,-3),earth);
                var track=Ground(tracks[0].Point(.6f));Shot("track-close",track+new Vector3(0,3,-6),track);
                var home=Ground(farms[0].center);Shot("house-grounding",home+new Vector3(15,5,-18),home+Vector3.up*1.5f);
                Shot("control-overview",new Vector3(160,240,-260),new Vector3(0,62,0));
                VerifyWater(map); VerifyAtlas(agriculture);
                report.AppendLine("PASS: isolated close bank, water/berm overlap, soil, curved track and grounded house captures; paddy bed and signed-distance accuracy checks.");
                void Shot(string name,Vector3 position,Vector3 focus)
                {
                    camera.transform.position=position;camera.transform.LookAt(focus);for(int i=0;i<12;i++){camera.Render();AsyncGPUReadback.Request(target,0).WaitForCompletion();}
                    var previous=RenderTexture.active;try{RenderTexture.active=target;image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();File.WriteAllBytes(Folder+"/"+name+".png",image.EncodeToPNG());}finally{RenderTexture.active=previous;}
                }
            }
            catch(Exception e){report.AppendLine("FAIL: "+e);throw;}
            finally
            {
                if(cameraObject!=null)Object.DestroyImmediate(cameraObject);if(target!=null){target.Release();Object.DestroyImmediate(target);}if(image!=null)Object.DestroyImmediate(image);
                EditorSceneManager.ClosePreviewScene(scene);map?.Release();File.WriteAllText(Folder+"/details.txt",report.ToString());UnityEngine.Debug.Log(report.ToString());
            }
        }
        private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    }
}

