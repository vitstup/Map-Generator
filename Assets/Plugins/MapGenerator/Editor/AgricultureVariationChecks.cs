using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using TacticalMaps.Construction;
using TacticalMaps.Generation;
using UnityEditor;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class AgricultureVariationChecks
    {
        [MenuItem("Tools/Tactical Maps/Verify Agriculture Variation")]
        internal static void Verify()
        {
            int shaped=0;
            var input=new AgricultureInput{Seed=1467};
            for(int seed=0;seed<100;seed++)
            {
                List<Rect> Grid(){var p=new List<Rect>();for(int y=0;y<2;y++)for(int x=0;x<3;x++)p.Add(new Rect(-30+x*20,-20+y*20,20,20));return p;}
                var a=Grid();var b=Grid();AgricultureSettlement.ShapePlots(input,a,60,40,seed,4);AgricultureSettlement.ShapePlots(input,b,60,40,seed,4);
                Require(a.SequenceEqual(b),"Deterministic group contours");
                if(a.Count==6)continue;shaped++;
                var low=a.Select(r=>r.min).Aggregate(Vector2.Min);var high=a.Select(r=>r.max).Aggregate(Vector2.Max);
                Require(a.Sum(r=>r.width*r.height)<(high.x-low.x)*(high.y-low.y)*.98f,"Removed parcels make an irregular outline");
                var reached=new HashSet<int>{0};bool changed=true;
                while(changed){changed=false;for(int i=0;i<a.Count;i++)if(!reached.Contains(i)&&reached.Any(j=>Vector2.Distance(a[i].center,a[j].center)<20.1f)){reached.Add(i);changed=true;}}
                Require(reached.Count==a.Count,"Remaining parcels stay connected");
            }
            Require(shaped>20&&shaped<80,"Both rectangular and irregular groups remain");

            var field=new FarmField{center=Vector2.zero,halfSize=new Vector2(30,30),angle=.37f,crop=1,cropExclusion=new Vector4(4,-5,8,10)};
            var primitive=AgricultureSurfaceData.Describe(new[]{field},Array.Empty<Farmstead>(),Array.Empty<FarmTrack>())[0];
            var yard=field.Point(4,-5);var crop=field.Point(18,10);
            Require(primitive.Distance(yard)>7 && primitive.Distance(crop)<-1,"Terrain surface has a real hole for the yard");
            var mask=AgricultureRaster.FarMask(new[]{primitive},Vector2.one*128,256,CancellationToken.None);
            Color32 Pixel(Vector2 p)=>mask[Mathf.FloorToInt((p.y/128+.5f)*256)*256+Mathf.FloorToInt((p.x/128+.5f)*256)];
            Require(Pixel(yard).a==0 && Pixel(crop).a==255,"Far coverage restores original terrain in the yard");
            var fallow=primitive;fallow.Style.z=0;
            Require(AgricultureRaster.ColorFor(primitive)==AgricultureRaster.ColorFor(fallow),"No green canopy tint is painted onto terrain");

            var library=AssetDatabase.LoadAssetAtPath<AgricultureLibrary>(AgricultureSetup.Folder+"/AgricultureLibrary.asset");
            var close=library.closeRice.uv2.Distinct().ToArray();var far=library.distantRice.uv2.Distinct().ToArray();
            Require(close.Length==25&&far.Length==25&&far.All(p=>close.Any(q=>(p-q).sqrMagnitude<.00001f)),"Distant clumps share close clump roots, keeping jitter stable across LODs");
            foreach(var name in new[]{"TacticalMaps/Agriculture/Rice","TacticalMaps/Terrain/MatteStochastic"})
                Require(!ShaderUtil.GetShaderMessages(Shader.Find(name)).Any(m=>m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error),"Shader compiles: "+name);
            Directory.CreateDirectory("Logs/AgricultureVariation");
            File.WriteAllText("Logs/AgricultureVariation/checks.txt",$"PASS: {shaped}/100 irregular connected groups; original terrain in yard near/far; no ground canopy tint; shared near/far plant roots.\n");
        }
        private static void Require(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
    }
}
