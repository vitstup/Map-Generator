using System;
using System.IO;
using System.Linq;
using TacticalMaps.Construction;
using TacticalMaps.Generation;
using UnityEditor;
using UnityEngine;

namespace TacticalMaps.Editor
{
    internal static class AgricultureVisibilityChecks
    {
        [MenuItem("Tools/Tactical Maps/Verify Agriculture Visibility")]
        internal static void Verify()
        {
            var library=AssetDatabase.LoadAssetAtPath<AgricultureLibrary>(AgricultureSetup.Folder+"/AgricultureLibrary.asset");
            var close=library.closeRice.uv2.Distinct().ToArray();var far=library.distantRice.uv2.Distinct().ToArray();
            Require(close.Length==25 && far.Length==25 && far.All(p=>close.Any(q=>(p-q).sqrMagnitude<.00001f)),"Both LODs preserve all planting positions");
            Require(library.distantRice.triangles.Length<library.closeRice.triangles.Length,"Far LOD still reduces triangle count");
            var field=new FarmField{center=Vector2.zero,halfSize=new Vector2(80,40),angle=.37f};
            var axis=field.Axis;var side=new Vector2(-axis.y,axis.x);
            var farm=new Farmstead{center=field.Point(12,-20),angle=field.angle-Mathf.PI*.5f,yardHalfSize=new Vector2(10,17),embedded=true};
            var contour=AgricultureBuilder.Contour(field,farm);
            Vector2 Local(Vector2 p)=>new Vector2(Vector2.Dot(p,axis),Vector2.Dot(p,side));
            Require(!contour.Any(p=>Mathf.Abs(Local(p.Position).y+40)<.01f && Mathf.Abs(Local(p.Position).x-12)<17.5f),"Outer bank does not cross the yard frontage");
            var notch=contour.Where(p=>p.Width<.36f).ToArray();Require(notch.Length>12,"Bank follows all three sides of the yard cutout");
            foreach(var point in notch)
            {
                var toe=Local(point.Position+point.Normal*(1.7f*point.Width));
                float fenceDistance=Mathf.Min(Mathf.Abs(toe.y+10),Mathf.Abs(Mathf.Abs(toe.x-12)-17));
                Require(fenceDistance<.02f,"Notch toe aligns with actual courtyard fence dimensions");
            }
            Directory.CreateDirectory("Logs/AgricultureVisibility");
            File.WriteAllText("Logs/AgricultureVisibility/checks.txt","PASS: 25 identical clump roots in both LODs, reduced far triangles, full yard frontage opening and bank toes aligned to courtyard fences.\n");
        }
        private static void Require(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
    }
}
