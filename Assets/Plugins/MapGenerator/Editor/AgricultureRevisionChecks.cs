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
                        else Require(a.Farms.Length > 0 && a.Farms.Length <= 500 && a.Fields.Length <= 3000, "Dense landscape budget");
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
                report.AppendLine("PASS: real high/low density selections, variable parcel areas, bounded populations, sparse contour distances and dry fields.");
            }
            catch (Exception e) { report.AppendLine("FAIL: " + e); throw; }
            finally { File.WriteAllText(Folder + "/verification.txt", report.ToString()); UnityEngine.Debug.Log(report.ToString()); }
        }
        internal static MapBuilder CopyBuilder(MapBuilder source, GameObject go)
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
            Require(map.Agriculture.Fields.All(f=>f.crop!=3),"No flooded field types");
            Require(!map.BuiltRoot.GetComponentsInChildren<MeshFilter>(true).Any(f=>f.name.StartsWith("PaddyWater_")),"No field water geometry");
        }
        [MenuItem("Tools/Tactical Maps/Capture Agriculture Revision Details")]
        internal static void CaptureDetails()
        {
            Directory.CreateDirectory(Folder); float savedRoads = Shader.GetGlobalFloat("_RoadEnabled"); Shader.SetGlobalFloat("_RoadEnabled",0); var scene = EditorSceneManager.NewPreviewScene();
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
                var fields = new[] { new FarmField { center=new Vector2(-105,55),halfSize=new Vector2(65,58),angle=.18f,crop=1,height=61.9225f,variation=.3f }, new FarmField { center=new Vector2(90,60),halfSize=new Vector2(80,36),angle=-.13f,crop=0,height=62.12f,variation=.6f }, new FarmField { center=new Vector2(-100,-110),halfSize=new Vector2(60,24),angle=.1f,crop=2,height=61.85f,variation=.7f } };
                var footprint=library.houses[0].GetComponent<FarmBuildingGrounding>().halfFootprint;
                var buildings = new FarmBuilding[6];
                for(int i=0;i<6;i++){var p=new Vector2(95+(i%2==0?-20:20),-100+(i/2-1)*34);buildings[i]=new FarmBuilding{center=p,angle=i%2==0?0:Mathf.PI,variant=i%5,scale=1,height=62+p.x*.001f+p.y*.0005f,halfSize=library.houses[i%5].GetComponent<FarmBuildingGrounding>().halfFootprint};}
                var farms=new[]{new Farmstead{center=new Vector2(95,-100),angle=0,variant=0,buildings=buildings,yardHalfSize=new Vector2(40,57),fenceStyle=0,halfFootprint=footprint,houseHeight=62.03f,storeHeight=62.05f}};
                var tracks=new[]{new FarmTrack{a=farms[0].Gate,b=new Vector2(220,-170),controlA=new Vector2(175,-95),controlB=new Vector2(205,-135)}};
                var controlRoads=AgricultureDetailFixtures.Expand(ref fields,ref farms,ref tracks,textures,sizes,library);
                var agriculture=new AgricultureData(fields,farms,null,new[]{1024,1024,1024},tracks); agriculture.CombinedRoads=controlRoads; agriculture.BuildSurfaces(sizes,CancellationToken.None);
                var biome=Object.FindAnyObjectByType<BiomeGenerator>(); var serialized=new SerializedObject(biome);var layers=new TerrainLayer[6];string[] names={"meadow","dryGrass","earth","rock","sand","silt"};for(int i=0;i<6;i++)layers[i]=(TerrainLayer)serialized.FindProperty(names[i]).objectReferenceValue;
                var weights=new float[3][,,];for(int i=0;i<3;i++){weights[i]=new float[64,64,6];for(int z=0;z<64;z++)for(int x=0;x<64;x++)weights[i][z,x,0]=1;}
                var surface=new SurfaceData(layers,weights,Vector2.zero);var coverage=GrassCoverage.Generate(surface,CancellationToken.None);coverage.ExcludeAgriculture(agriculture,sizes);
                map=new MapData(textures,sizes,256,10,256,Vector2Int.zero,1,surface,null,null,coverage);map.SetAgriculture(agriculture);map.Roads=controlRoads;
                var source=Object.FindAnyObjectByType<MapBuilder>();builderObject=new GameObject("Agriculture detail control");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(builderObject,scene);var builder=CopyBuilder(source,builderObject);builder.Build(map);
                var grassSource=Object.FindAnyObjectByType<GrassSystem>();var grassObject=new GameObject("Control meadow");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(grassObject,scene);var grass=grassObject.AddComponent<GrassSystem>();AgricultureSetup.Set(grass,"library",grassSource.Library);grass.Build(map);grass.FreezeWindForChecks=true;grass.MatureForChecks=true;
                var lightSource=Object.FindObjectsByType<Light>(FindObjectsSortMode.None).First(l=>l.type==LightType.Directional);var lightObject=new GameObject("Control sun");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightObject,scene);var light=lightObject.AddComponent<Light>();EditorUtility.CopySerialized(lightSource,light);light.transform.rotation=lightSource.transform.rotation;
                cameraObject=new GameObject("AgricultureDetailCamera");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject,scene);var camera=cameraObject.AddComponent<Camera>();camera.CopyFrom(Camera.main);camera.scene=scene;camera.enabled=false;camera.fieldOfView=55;camera.nearClipPlane=.15f;camera.farClipPlane=5000;
                var extra=cameraObject.AddComponent<UniversalAdditionalCameraData>();extra.renderPostProcessing=Camera.main.GetUniversalAdditionalCameraData().renderPostProcessing;extra.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                target=new RenderTexture(1400,900,24);target.Create();camera.targetTexture=target;image=new Texture2D(1400,900,TextureFormat.RGB24,false);
                var terrains=map.BuiltRoot.GetComponentsInChildren<Terrain>();Vector3 Ground(Vector2 p)=>AgricultureBuilder.Ground(p,terrains,Vector3.zero);
                var bank=Ground(fields[0].Point(10,-fields[0].halfSize.y));
                Shot("bank-close",bank+new Vector3(4,2.3f,-4),bank+Vector3.up*.3f);
                Shot("dry-field-edge",bank+new Vector3(0,1.6f,-3),bank+new Vector3(0,.05f,3));
                var wet=Ground(fields[0].center);Shot("dry-field-surface",wet+new Vector3(20,10,-24),wet);
                var earth=Ground(fields[1].center);Shot("soil-close",earth+new Vector3(0,3,-3),earth);
                var track=Ground(tracks[0].Point(.6f));Shot("track-close",track+new Vector3(0,3,-6),track);
                var home=Ground(farms[0].center);Shot("house-grounding",home+new Vector3(15,5,-18),home+Vector3.up*1.5f);
                var row=farms.First(f=>f.embedded);var rowPosition=Ground(row.center);Shot("embedded-field-row",rowPosition+new Vector3(50,40,-65),rowPosition);
                var rowSide=new Vector2(-row.Axis.y,row.Axis.x);var edge=Ground(row.center+rowSide*(row.yardHalfSize.y+1.2f));
                Shot("yard-ground-boundary",edge+new Vector3(rowSide.x*2.5f+row.Axis.x*3,1.3f,rowSide.y*2.5f+row.Axis.y*3),edge+Vector3.up*.15f);
                var plants=Ground(fields[0].center);Shot("plants-ground-close",plants+new Vector3(2,1.2f,-3),plants+Vector3.up*.25f);
                var props=map.BuiltRoot.GetComponentInChildren<FarmPropRenderer>();
                if(props!=null&&props.placements.Length>0)
                {
                    var chosen=props.placements.FirstOrDefault(p=>p.variant>=5);if(chosen.scale==0)chosen=props.placements[0];
                    var prop=props.transform.TransformPoint(chosen.position);Shot("props-close",prop+new Vector3(5,3,-6),prop+Vector3.up*.6f);Require(props.LastVisible>0,"Nearby props render");
                    report.AppendLine($"Props: {props.placements.Length} placements; near {props.LastVisible} visible / {props.LastDraws} batches");
                    Shot("props-culled",prop+Vector3.up*350,prop);Require(props.LastVisible==0,"Props beyond draw distance are culled");
                }
                Shot("settlement-overview",home+new Vector3(95,70,-110),home);
                var houseLods=map.BuiltRoot.GetComponentsInChildren<LODGroup>().Where(l=>l.name.StartsWith("FarmBuilding")).ToArray();
                foreach(var house in houseLods.Take(5))
                {
                    house.fadeMode=LODFadeMode.None;var p=house.transform.position; foreach(var other in houseLods)other.gameObject.SetActive(other==house);
                    for(int lod=0;lod<3;lod++){house.ForceLOD(lod);Shot("house-"+house.name+"-lod"+lod,p+new Vector3(17,9,-22),p+Vector3.up*3);}
                    house.ForceLOD(-1);
                }
                foreach(var house in houseLods)house.gameObject.SetActive(true);
                Shot("control-overview",new Vector3(160,240,-260),new Vector3(0,62,0));
                // Fixed projected scale isolates LOD/culling changes from perspective shrinking.
                var cropRenderer=map.BuiltRoot.GetComponentInChildren<CropRenderer>();
                var metrics=new System.Collections.Generic.Dictionary<int,float>();
                camera.orthographic=true;camera.orthographicSize=95;var aim=Ground(fields[0].center)+Vector3.up*.3f;
                var direction=new Vector3(0,.5f,-1).normalized;var pixelsWith=new Color32[0];
                foreach(int distance in new[]{80,105,130,160,250,330,450,650,900,1150,1300})
                {
                    cropRenderer.SuppressForChecks=false;Shot("crop-distance-"+distance,aim+direction*distance,aim);pixelsWith=image.GetPixels32();
                    cropRenderer.SuppressForChecks=true;Shot("crop-baseline-"+distance,aim+direction*distance,aim);var without=image.GetPixels32();
                    double total=0;for(int pixel=0;pixel<without.Length;pixel++)total+=Math.Abs(pixelsWith[pixel].r-without[pixel].r)+Math.Abs(pixelsWith[pixel].g-without[pixel].g)+Math.Abs(pixelsWith[pixel].b-without[pixel].b);
                    metrics[distance]=(float)(total/(without.Length*3));
                    report.AppendLine($"Crop signal at {distance}m: {metrics[distance]:F4}/255");
                }
                cropRenderer.SuppressForChecks=false;camera.orthographic=false;
                Require(metrics[450]>metrics[250]*.35f && metrics[450]>.01f,"Crops remain visible beyond the former 330m cutoff");
                Require(metrics[130]>metrics[80]*.5f && metrics[160]>metrics[80]*.5f,"Near/far handoff preserves crop coverage");
                Require(metrics[900]>metrics[1150] && metrics[1300]<metrics[450]*.1f,"Distant crops fade across a broad interval");
                VerifyWater(map); VerifyAtlas(agriculture);
                var farmSettings=new SerializedObject(builderObject.GetComponent<AgricultureBuilder>());
                Require(map.BuiltRoot.GetComponentsInChildren<MeshFilter>(true).Any(f=>f.name.StartsWith("FieldBanks_")),"Field bank generation enabled");
                int planted=cropRenderer.PatchCount;grassObject.SetActive(false);
                farmSettings.FindProperty("generateFieldBanks").boolValue=false;farmSettings.ApplyModifiedPropertiesWithoutUndo();builder.Build(map);
                Require(!map.BuiltRoot.GetComponentsInChildren<MeshFilter>(true).Any(f=>f.name.StartsWith("FieldBanks_")),"Field bank toggle removes all bank geometry");
                Require(map.BuiltRoot.GetComponentsInChildren<MeshFilter>(true).Any(f=>f.name.StartsWith("FarmFence_")),"Courtyard fences remain when field banks are disabled");
                Require(map.BuiltRoot.GetComponentInChildren<CropRenderer>().PatchCount==planted,"Field bank toggle preserves crops");
                report.AppendLine("PASS: field bank toggle removes bank geometry independently of crops and courtyard fences.");
                report.AppendLine("PASS: dry fields, intact embedded farm parcel, corner supports, soil, tracks and grounded house captures; signed-distance accuracy checks.");
                void Shot(string name,Vector3 position,Vector3 focus)
                {
                    camera.transform.position=position;camera.transform.LookAt(focus);for(int i=0;i<2;i++){camera.Render();}
                    var previous=RenderTexture.active;try{RenderTexture.active=target;image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();File.WriteAllBytes(Folder+"/"+name+".png",image.EncodeToPNG());}finally{RenderTexture.active=previous;}
                }
            }
            catch(Exception e){report.AppendLine("FAIL: "+e);throw;}
            finally
            {
                if(cameraObject!=null)Object.DestroyImmediate(cameraObject);if(target!=null){target.Release();Object.DestroyImmediate(target);}if(image!=null)Object.DestroyImmediate(image);
                EditorSceneManager.ClosePreviewScene(scene);map?.Release();Shader.SetGlobalFloat("_RoadEnabled",savedRoads);File.WriteAllText(Folder+"/details.txt",report.ToString());UnityEngine.Debug.Log(report.ToString());
            }
        }
        private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    }
}

