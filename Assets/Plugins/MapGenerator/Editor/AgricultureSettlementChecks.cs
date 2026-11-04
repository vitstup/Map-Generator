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
    internal static class AgricultureSettlementChecks
    {
        [MenuItem("Tools/Tactical Maps/Verify Agriculture Settlements")]
        internal static void Verify()
        {
            var report=new StringBuilder();var generator=Object.FindAnyObjectByType<MapGenerator>();
            try
            {
                foreach(var point in new[]{new Vector2Int(2618,1858),new Vector2Int(2592,2016),new Vector2Int(2496,2240)})
                {
                    var timer=System.Diagnostics.Stopwatch.StartNew();var map=generator.Generate(point);
                    try
                    {
                        var a=map.Agriculture;double area=a.Fields.Sum(f=>4.0*f.halfSize.x*f.halfSize.y);
                        var counts=a.Farms.GroupBy(f=>f.Buildings.Length).ToDictionary(g=>g.Key,g=>g.Count());
                        report.AppendLine($"{point}: fields {a.Fields.Length}; farms {a.Farms.Length}; buildings {a.Farms.Sum(f=>f.Buildings.Length)}; area {area:F0} m2; tracks {a.Tracks.Length}; generation {timer.Elapsed.TotalMilliseconds:F0} ms; {generator.GetComponent<AgricultureGenerator>().LastTimings}");
                        report.AppendLine("  layouts "+string.Join(", ",counts.OrderBy(p=>p.Key).Select(p=>$"{p.Key} buildings: {p.Value}")));
                        if(point.x==2496)Require(a.Fields.Length<=10&&a.Farms.Length<=2,"Mountain budgets");
                        else
                        {
                            Require(a.Farms.Length>0&&a.Farms.Length<=500,"Suitability-limited farm budget");Require(new[]{1,2}.All(counts.ContainsKey),"One- and two-house field layouts present");
                            var groups = a.Fields.GroupBy(f=>f.groupId).Select(g=>g.Sum(f=>4f*f.halfSize.x*f.halfSize.y)).ToArray();
                            Require(groups.Max()/groups.Min()>12,"Different field group areas");
                            double baseline=point.x==2618?37956920.0294428:64045931.1697563;Require(area/baseline>=.33&&area/baseline<=.53,"Area reduced about 2-2.5 times");
                        }
                        float beach=map.SeaLevelMeters+generator.GetComponent<BiomeGenerator>().BeachHeight+.3f;
                        foreach(var f in a.Fields)Require(f.height>beach,"No beach fields");
                        foreach(var farm in a.Farms)
                        {
                            Require(farm.buildings!=null,"Explicit building layouts");
                            foreach(var building in farm.Buildings){Require(building.height>beach,"No beach houses");Require(a.Blocks(building.center),"Grass excluded under buildings");}
                            Require(!a.Surface.Primitives.Any(p=>p.Style.z==4),"No painted farmyards");
                            Require(!a.Fields.Any(f=>(!farm.embedded || (f.center-farm.parcelCenter).sqrMagnitude>=.01f) && AgricultureSettlement.Overlaps(f,farm)),"Fields do not cross reserved farmyards");
                        }
                    }finally{map.Release();}
                }
                var lib=AssetDatabase.LoadAssetAtPath<AgricultureLibrary>(AgricultureSetup.Folder+"/AgricultureLibrary.asset");
                foreach(var house in lib.houses)
                {
                    Require(house.GetComponentsInChildren<Renderer>().All(r=>AssetDatabase.GetAssetPath(r.sharedMaterial.shader).StartsWith(AgricultureSetup.Folder+"/Shaders/")),"Dedicated crossfade shaders");
                    var lods=house.GetComponent<LODGroup>().GetLODs();Require(lods.Length==3,"Three house LODs");
                    int[] triangles=lods.Select(l=>l.renderers.Sum(r=>r.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3)).ToArray();
                    Require(triangles[2]<triangles[0]&&triangles[1]<=triangles[0],"LOD triangle reduction");
                    var bounds=lods.Select(l=>l.renderers.Select(r=>r.GetComponent<MeshFilter>().sharedMesh.bounds).Aggregate((a,b)=>{a.Encapsulate(b);return a;})).ToArray();
                    Require((bounds[0].size-bounds[2].size).magnitude<2,"Far silhouette extents retained");
                    report.AppendLine(house.name+" triangles "+string.Join(" / ",triangles));
                }
                Require(lib.fences.Length==2 && lib.fences.All(f=>f.GetComponent<FenceSection>().length>1),"Wood and bamboo fences baked");
                var coast = AgricultureChecks.Input(); coast.BeachHeight = 60;
                var barren = coast.Render(CancellationToken.None); Require(barren.Fields.Length == 0 && barren.Farms.Length == 0, "Sandy land above sea level is rejected");
                VerifyRouting(report);
                report.AppendLine("PASS: area, populations, beach rejection, reservations, individual LODs, fences and access routing.");
            }catch(Exception e){report.AppendLine("FAIL: "+e);throw;}
            finally{Directory.CreateDirectory("Logs/AgricultureSettlement");File.WriteAllText("Logs/AgricultureSettlement/verification.txt",report.ToString());Debug.Log(report.ToString());}
        }
        private static void VerifyRouting(StringBuilder report)
        {
            var input=new AgricultureInput{Height=100,Sea=0,BeachHeight=2.5f,ConnectionRadius=1500,AccessLength=1000};
            for(int i=0;i<3;i++){input.Sizes[i]=Vector2.one*(1024<<i);input.Widths[i]=input.Heights[i]=65;input.Pixels[i]=Enumerable.Repeat((ushort)10000,65*65).ToArray();}
            input.Roads=new RoadData(new[]{new RoadSegment(new Vector2(350,-1000),new Vector2(350,1000),8,1)},input.Sizes[2],2,2);
            var fields=new[]{new FarmField{center=Vector2.zero,halfSize=new Vector2(90,65),groupId=1},new FarmField{center=new Vector2(180,0),halfSize=new Vector2(30,110),groupId=2}};
            var tracks=AgricultureAccess.Build(input,fields,Array.Empty<Farmstead>(),CancellationToken.None);Require(tracks.Length>0,"Groups connect without houses");
            foreach(var t in tracks)for(int i=0;i<=30;i++)Require(!fields.Any(f=>f.Contains(t.Point(i/30f),1)),"Routes avoid parcels");
            input.ConnectionRadius=1;Require(AgricultureAccess.Build(input,fields,Array.Empty<Farmstead>(),CancellationToken.None).Length==0,"Connection radius honored");
            report.AppendLine("  routing: "+tracks.Length+" curve segments; field-only groups and radius verified");
        }
        private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    }
}
