using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TacticalMaps.Generation;
using TacticalMaps.Construction;
using UnityEditor;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class RoadChecks
    {
        internal static string LastResult="Not run";
        [MenuItem("Tools/Tactical Maps/Verify Roads")]
        internal static async void Run()
        {
            LastResult="Running";
            try
            {
                string report=await Verify();Directory.CreateDirectory("Logs/Roads");File.WriteAllText("Logs/Roads/checks.txt",report);LastResult=report;Debug.Log(report);
            }
            catch(Exception e){LastResult="FAILED: "+e;Debug.LogException(e);}
        }
        private static async Task<string> Verify()
        {
            var go=new GameObject("RoadChecks"){hideFlags=HideFlags.HideAndDontSave};var generator=go.AddComponent<RoadGenerator>();
            Texture2D source=null;MapData flat=null,hill=null,water=null;
            try
            {
                source=new Texture2D(64,64,TextureFormat.RGBA32,false,true);var colors=new Color32[4096];
                for(int y=31;y<=33;y++)for(int x=2;x<62;x++)colors[y*64+x]=new Color32(255,0,0,255);
                for(int x=31;x<=33;x++)for(int y=5;y<60;y++)if(colors[y*64+x].r==0)colors[y*64+x]=new Color32(0,255,0,255);
                source.SetPixels32(colors);source.Apply();var serialized=new SerializedObject(generator);serialized.FindProperty("roadSource").objectReferenceValue=source;serialized.ApplyModifiedPropertiesWithoutUndo();
                flat=Map((x,z)=>20);var a=await generator.GenerateAsync(flat,32,64,64,CancellationToken.None);
                Require(a.Segments.Any(s=>s.Type==1)&&a.Segments.Any(s=>s.Type==2),"Both road classes");
                Require(a.Segments.Where(s=>s.Type==1).Any(s=>Mathf.Abs(s.A.y)>1),"Natural bends on flat ground");
                Require(a.FarCoverage[0].r==0&&a.FarCoverage[0].g==0,"Empty distant coverage stays empty");
                Require(a.FarCoverage.Count(p=>p.r>0||p.g>0)<a.FarCoverage.Length*.25f,"Distant roads preserve sparse area");
                var transition=new RoadData(new[]{new RoadSegment(new Vector2(-50,0),Vector2.zero,8,1),new RoadSegment(Vector2.zero,new Vector2(50,0),3,2)},new Vector2(256,256),1,2);
                Require(transition.TypeTransitions.Length==1&&transition.Segments[0].HalfWidthB<=1.51f&&transition.Segments[0].HalfWidthA>=3.9f,"Smooth width transition between classes");
                typeof(RoadGenerator).GetField("network",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(generator,null);
                var b=await generator.GenerateAsync(flat,32,64,64,CancellationToken.None);
                Require(a.Segments.Length==b.Segments.Length&&a.Segments.Zip(b.Segments,(p,q)=>p.A==q.A&&p.B==q.B).All(x=>x),"Deterministic regeneration");
                using(var cancellation=new CancellationTokenSource())
                {
                    cancellation.Cancel();bool cancelled=false;try{await generator.GenerateAsync(flat,32,64,64,cancellation.Token);}catch(OperationCanceledException){cancelled=true;}Require(cancelled,"Cancellation");
                }
                hill=Map((x,z)=>20+100*Mathf.Exp(-((x-210)*(x-210)+z*z)/(2*65*65)));
                var c=await generator.GenerateAsync(hill,32,64,64,CancellationToken.None);
                Require(c.Segments.Any(s=>s.Type==1&&Mathf.Abs((s.A.x+s.B.x)*.5f-210)<90&&Mathf.Abs((s.A.y+s.B.y)*.5f)>35),"Hill detour");
                water=Map((x,z)=>Mathf.Abs(x)<35?0:20);var d=await generator.GenerateAsync(water,32,64,64,CancellationToken.None);
                Require(d.Segments.Any(s=>s.A.x< -50)&&d.Segments.Any(s=>s.A.x>50),"Road retained on both shores");
                Require(d.Segments.All(s=>Mathf.Sign(s.A.x)==Mathf.Sign(s.B.x)&&Mathf.Abs(s.A.x)>30&&Mathf.Abs(s.B.x)>30),"No bridge across water");
                var disabled=new SerializedObject(generator);disabled.FindProperty("generateRoads").boolValue=false;disabled.ApplyModifiedPropertiesWithoutUndo();Require(await generator.GenerateAsync(flat,32,64,64,CancellationToken.None)==null,"Disabled roads");
                Require(a.Blocks(a.Segments[0].A),"Object exclusion");
                flat.Roads=a;var construction=new GameObject("RoadConstructionChecks");var builder=construction.AddComponent<RoadBuilder>();
                try
                {
                    builder.Generate(flat,construction.transform,Vector3.zero);var owned=construction.GetComponent<RoadMap>();Require(owned!=null&&owned.Pages!=null&&owned.Distances.depth==a.Tiles.Count,"Construction owns sparse textures");
                    Require(owned.FarCoverage!=null&&owned.FarCoverage.mipmapCount>1,"Area-filtered distant coverage");
                    var texture=owned.Distances;flat.Release();Require(texture!=null,"Built resources survive MapData.Release");
                    UnityEngine.Object.DestroyImmediate(construction);Require(texture==null,"Construction cleans textures");
                }
                finally {if(construction!=null)UnityEngine.Object.DestroyImmediate(construction);}
                return $"PASS: both types; natural flat-ground bends; sparse mipmapped distant coverage; determinism; hot-reload cache recovery; cancellation; hill detour; water gaps; toggle; exclusion; construction lifetime. Flat: {a.Segments.Length} segments / {a.Tiles.Count} tiles. Hill: {c.Segments.Length}. Water: {d.Segments.Length}.";
            }
            finally {flat?.Release();hill?.Release();water?.Release();if(source!=null)UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(go);}
        }
        private static void Require(bool condition,string name){if(!condition)throw new InvalidOperationException("Road check failed: "+name);}
        private static MapData Map(Func<float,float,float> sample)
        {
            var textures=new Texture2D[3];var sizes=new[]{new Vector2(256,256),new Vector2(512,512),new Vector2(1024,1024)};
            for(int level=0;level<3;level++)
            {
                int n=257;var pixels=new ushort[n*n];
                for(int y=0;y<n;y++)for(int x=0;x<n;x++){float wx=((float)x/(n-1)-.5f)*sizes[level].x,wz=((float)y/(n-1)-.5f)*sizes[level].y;pixels[y*n+x]=(ushort)Mathf.RoundToInt(Mathf.Clamp01(sample(wx,wz)/256)*65535);}
                textures[level]=new Texture2D(n,n,TextureFormat.R16,false,true);textures[level].SetPixelData(pixels,0);textures[level].Apply();
            }
            return new MapData(textures,sizes,256,0,256,new Vector2Int(32,32));
        }
    }
}
