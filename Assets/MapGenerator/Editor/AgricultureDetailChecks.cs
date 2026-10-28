using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using TacticalMaps.Generation;
using TacticalMaps.Construction;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace TacticalMaps.Editor
{
    internal static class AgricultureDetailChecks
    {
        [MenuItem("Tools/Tactical Maps/Verify Agriculture Details")]
        internal static void Verify()
        {
            var report=new StringBuilder();var generator=Object.FindAnyObjectByType<MapGenerator>();var lib=AssetDatabase.LoadAssetAtPath<AgricultureLibrary>(AgricultureSetup.Folder+"/AgricultureLibrary.asset");
            try
            {
                {
                    var source=new Mesh();source.vertices=new[]{Vector3.zero,Vector3.right,Vector3.up};source.normals=new[]{Vector3.forward,Vector3.forward,Vector3.forward};source.triangles=new[]{0,1,2};var combined=new Mesh();
                    combined.CombineMeshes(new[]{new CombineInstance{mesh=source,transform=Matrix4x4.Scale(new Vector3(-1,1,1))}},true,true);
                    var v=combined.vertices;var t=combined.triangles;var n=Vector3.Cross(v[t[1]]-v[t[0]],v[t[2]]-v[t[0]]);Require(Vector3.Dot(n,combined.normals[t[0]])>.9f,"Native mesh combination preserves mirrored winding");Object.DestroyImmediate(source);Object.DestroyImmediate(combined);
                }
                foreach(var house in lib.houses){var anchors=house.GetComponent<FarmBuildingGrounding>().supports;Require(anchors.Length==4,"Exactly four corner anchors on every house");report.AppendLine(house.name+": "+anchors.Length+" floor anchors, height "+anchors.Max(p=>p.y).ToString("F2"));}
                Require(lib.props.Length==13&&lib.props.All(p=>p.GetComponentInChildren<MeshFilter>()!=null),"Thirteen baked prop variants");
                foreach(var point in new[]{new Vector2Int(2618,1858),new Vector2Int(2592,2016),new Vector2Int(2496,2240)})
                {
                    var timer=System.Diagnostics.Stopwatch.StartNew();var map=generator.Generate(point);
                    try
                    {
                        var a=map.Agriculture;int embedded=a.Farms.Count(f=>f.embedded);double area=a.Fields.Sum(f=>4.0*f.halfSize.x*f.halfSize.y);report.AppendLine($"{point}: {a.Fields.Length} fields, {a.Farms.Length} farms ({embedded} field rows), {a.Tracks.Length} track curves, area {area:F0} m2; {timer.Elapsed.TotalMilliseconds:F0} ms; {generator.GetComponent<AgricultureGenerator>().LastTimings}");
                        if(a.Farms.Length>20){float share=(float)embedded/a.Farms.Length;Require(Mathf.Abs(share-.4f)<.06f,"Approximately 40 percent of successful farms are embedded");report.AppendLine($"  embedded share {share:P1}");}
                        if(point.x!=2496){Require(a.Farms.Length>0&&a.Farms.Length<=500,"Dense farm budget");Require(embedded>=5,"Field-side rows are actually generated");}else Require(a.Fields.Length<=10&&a.Farms.Length<=2,"Mountain budget");
                        foreach(var farm in a.Farms)
                        {
                            foreach(var field in a.Fields)Require((farm.embedded && (field.center-farm.parcelCenter).sqrMagnitude<.01f) || !AgricultureSettlement.Overlaps(field,farm),"No parcel occupies a reserved yard");
                            if(farm.embedded){Require(a.Fields.Count(f=>(f.center-farm.parcelCenter).sqrMagnitude<.01f)==1,"Embedded farm owns an intact parcel");var host=a.Fields.Single(f=>(f.center-farm.parcelCenter).sqrMagnitude<.01f);Require(farm.Buildings.All(b=>host.ExcludesCrops(b.center)),"All field buildings exclude crops");Require(farm.Buildings.Length<=2,"Field row is limited to two houses");var parcel=new FarmField{center=farm.parcelCenter,halfSize=farm.parcelHalfSize,angle=farm.parcelAngle};foreach(var b in farm.Buildings)Require(parcel.Contains(b.center,-1),"Field houses lie inside the original parcel");}
                            if(map.Roads!=null)foreach(var road in map.Roads.Segments.Take(map.Roads.GlobalSegmentCount))
                                Require(!farm.IntersectsYard(road.A,road.B,road.HalfWidth+map.Roads.Shoulder+1.2f),"Global road footprint avoids the yard");
                            foreach(var track in a.Tracks)
                            {
                                if(track.ownerGroup==farm.groupId)
                                {for(int i=0;i<=16;i++)Require(farm.GateCorridor(track.Point(i/16f)),"Owned entrance crosses only the gate corridor");}
                                else for(int i=1;i<=16;i++)Require(!farm.IntersectsYard(track.Point((i-1)/16f),track.Point(i/16f),generator.Roads.MediumWidth*.5f+(map.Roads?.Shoulder ?? 1)+1.2f),"Road footprint avoids all foreign yards and fences");
                            }
                        }
                        if(a.Tracks.Length>0)
                        {
                            Require(a.CombinedRoads!=null&&map.Roads==a.CombinedRoads,"Access is rendered by the shared road pipeline");
                            Require(!a.Surface.Primitives.Any(p=>p.Style.z>4.5f),"No separate brown access paint");
                            foreach(var s in map.Roads.Segments.Skip(map.Roads.GlobalSegmentCount))Require(s.Type==2,"Access uses the small road type");
                        }
                        if(point.x!=2496)
                        {
                            var source=Object.FindAnyObjectByType<MapBuilder>();var go=new GameObject("Detail check build");go.SetActive(false);
                            try
                            {
                                var preview=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,preview);
                                var builder=AgricultureRevisionChecks.CopyBuilder(source,go);builder.Build(map);
                                var props=map.BuiltRoot.GetComponentInChildren<FarmPropRenderer>();Require(props!=null&&props.placements.Length>0,"Courtyard props populated");
                                int supports=map.BuiltRoot.GetComponentsInChildren<MeshFilter>(true).Count(f=>f.name=="StructuralSupports");Require(supports>0,"Structural post meshes generated");
                                var terrains=map.BuiltRoot.GetComponentsInChildren<Terrain>();int footing=0;
                                foreach(var house in map.BuiltRoot.GetComponentsInChildren<FarmBuildingGrounding>(true))
                                {
                                    var p=house.transform.position;
                                    float ground=AgricultureBuilder.Ground(new Vector2(p.x,p.z),terrains,Vector3.zero).y;
                                    Require(Mathf.Abs(p.y+house.platformHeight*house.transform.localScale.y-ground-FarmBuildingGrounding.PlatformClearance)<.01f,"Platform clearance matches low placement independently of source legs");
                                }
                                foreach(var f in map.BuiltRoot.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.name=="StructuralSupports"))
                                {var v=f.sharedMesh.vertices;Require(v.Length<=80,"At most four added corner posts per house");for(int i=0;i<v.Length;i+=20){var p=f.transform.TransformPoint(v[i]);float y=AgricultureBuilder.Ground(new Vector2(p.x,p.z),terrains,Vector3.zero).y;Require(Mathf.Abs(p.y-y)<.16f,"Each post reaches terrain");footing++;}}
                                if(map.Roads!=null && map.Roads.Tiles.Count>2048){go.SetActive(true);VerifyExtraRoadBank(map,report);}
                                report.AppendLine($"  build: {supports} structural meshes, {footing} checked post feet, {props.placements.Length} instanced props");
                            }finally{var preview=go.scene;Object.DestroyImmediate(go);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);}
                        }
                    }finally{map.Release();}
                }
                report.AppendLine("PASS: structural supports, yard/gate collision, small-road integration, embedded field rows and courtyard props.");
            }catch(Exception e){report.AppendLine("FAIL: "+e);throw;}
            finally{Directory.CreateDirectory("Logs/AgricultureDetail");File.WriteAllText("Logs/AgricultureDetail/verification.txt",report.ToString());Debug.Log(report.ToString());}
        }
        [MenuItem("Tools/Tactical Maps/Verify Farm Gate Routing")]
        internal static void VerifyGateRouting()
        {
            var input=new AgricultureInput{Height=100,Sea=0,BeachHeight=2.5f,ConnectionRadius=1500,AccessLength=1000};
            for(int i=0;i<3;i++){input.Sizes[i]=Vector2.one*(1024<<i);input.Widths[i]=input.Heights[i]=65;input.Pixels[i]=Enumerable.Repeat((ushort)10000,65*65).ToArray();}
            input.Roads=new RoadData(new[]{new RoadSegment(new Vector2(200,-500),new Vector2(200,500),3,2)},input.Sizes[2],1,2);
            var a=new Farmstead{center=Vector2.zero,yardHalfSize=new Vector2(20,20),groupId=1,gateLead=26,buildings=Array.Empty<FarmBuilding>()};
            var b=new Farmstead{center=new Vector2(33,0),yardHalfSize=new Vector2(5,10),groupId=2,buildings=Array.Empty<FarmBuilding>()};
            var tracks=AgricultureAccess.Build(input,Array.Empty<FarmField>(),new[]{a,b},CancellationToken.None);
            Require(!tracks.Any(t=>t.ownerGroup==1)&&tracks.Any(t=>t.ownerGroup==2),"A long entrance cannot cross a neighbour while the neighbour remains connectable");
            a.gateLead=8;a.groupId=3;input.Roads=new RoadData(new[]{new RoadSegment(new Vector2(28,-500),new Vector2(28,500),3,2)},input.Sizes[2],1,2);
            tracks=AgricultureAccess.Build(input,Array.Empty<FarmField>(),new[]{a},CancellationToken.None);
            Require(tracks.Any(t=>t.ownerGroup==3),"Gate directly on the global road still gets its approach");
            var field=new FarmField{center=Vector2.zero,halfSize=new Vector2(80,60),crop=1,groupId=4};
            var embedded=new Farmstead{center=new Vector2(0,-42),angle=-Mathf.PI/2,yardHalfSize=new Vector2(12,20),groupId=4,embedded=true,parcelCenter=field.center,parcelHalfSize=field.halfSize,buildings=Array.Empty<FarmBuilding>()};
            field.ReserveYard(embedded);input.Roads=new RoadData(new[]{new RoadSegment(new Vector2(200,-500),new Vector2(200,500),3,2)},input.Sizes[2],1,2);
            tracks=AgricultureAccess.Build(input,new[]{field},new[]{embedded},CancellationToken.None);
            Require(tracks.Any(t=>t.ownerGroup==4),"Entrance crosses the intact parcel boundary through its crop exclusion");
            foreach(var t in tracks)for(int i=0;i<=32;i++){var p=t.Point(i/32f);Require(!field.Contains(p,2)||field.ExcludesCrops(p,-2),"Access never enters planted rows");}
            Require(!field.ExcludesCrops(field.Point(40,0)) && field.Contains(field.Point(40,0)),"Crops outside the compact yard remain in the original field");
            Directory.CreateDirectory("Logs/AgricultureDetail");File.WriteAllText("Logs/AgricultureDetail/gates.txt","PASS: neighbouring-yard entrance rejection, a gate on the global road, and an embedded farm entrance across an intact field boundary.");
            Debug.Log(File.ReadAllText("Logs/AgricultureDetail/gates.txt"));
        }
        private static void VerifyExtraRoadBank(MapData map,StringBuilder report)
        {
            var ground=map.BuiltRoot.GetComponentsInChildren<Terrain>();Vector3 focus=default;bool found=false;
            for(int i=2048;i<map.Roads.Tiles.Count&&!found;i++)
            {
                int key=map.Roads.TileKeys[i];var origin=map.Roads.Origin+new Vector2(key%map.Roads.Columns,key/map.Roads.Columns)*128;
                for(int y=1;y<129&&!found;y++)for(int x=1;x<129;x++){var pixel=map.Roads.Tiles[i][y*130+x];if(Mathf.Min(pixel.r,pixel.g)>=125)continue;var p=origin+new Vector2(x-.5f,y-.5f);focus=AgricultureBuilder.Ground(p,ground,Vector3.zero);if(focus.y>map.SeaLevelMeters+4){found=true;break;}}
            }
            Require(found,"Overflow bank has a dry visible road texel");
            var cameraObject=new GameObject("Overflow road camera");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject,map.BuiltRoot.gameObject.scene);var camera=cameraObject.AddComponent<Camera>();camera.CopyFrom(Camera.main);camera.scene=map.BuiltRoot.gameObject.scene;camera.enabled=false;camera.nearClipPlane=.1f;camera.farClipPlane=1000;camera.transform.position=focus+new Vector3(20,55,-25);camera.transform.LookAt(focus);
            var lightObject=new GameObject("Overflow sun");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightObject,map.BuiltRoot.gameObject.scene);var sun=lightObject.AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.5f;sun.transform.rotation=Quaternion.Euler(45,-35,0);
            var target=new RenderTexture(512,512,24);target.Create();camera.targetTexture=target;var image=new Texture2D(512,512,TextureFormat.RGB24,false);var before=RenderTexture.active;
            try
            {
                Shader.SetGlobalFloat("_RoadEnabled",1);camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,512,512),0,0);image.Apply();var with=image.GetPixels32();File.WriteAllBytes("Logs/AgricultureDetail/overflow-road.png",image.EncodeToPNG());
                Shader.SetGlobalFloat("_RoadEnabled",0);camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,512,512),0,0);image.Apply();var without=image.GetPixels32();long difference=0;for(int i=0;i<with.Length;i++)difference+=Math.Abs(with[i].r-without[i].r)+Math.Abs(with[i].g-without[i].g)+Math.Abs(with[i].b-without[i].b);
                Require(difference>1000,"Overflow distance bank affects rendered road pixels");report.AppendLine($"  overflow road bank: {map.Roads.Tiles.Count} tiles, rendered difference {difference}");
            }finally{Shader.SetGlobalFloat("_RoadEnabled",1);RenderTexture.active=before;camera.targetTexture=null;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(image);Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(lightObject);}
        }
        private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    }
}
