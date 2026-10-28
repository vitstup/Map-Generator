using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TacticalMaps.Generation;
using TacticalMaps.Construction;
using UnityEngine;
namespace TacticalMaps.Editor
{
    internal static class AgricultureDetailFixtures
    {
        internal static RoadData Expand(ref FarmField[] fields,ref Farmstead[] farms,ref FarmTrack[] tracks,Texture2D[] maps,Vector2[] sizes,AgricultureLibrary library)
        {
            var input=new AgricultureInput{Height=256,Sea=10,BeachHeight=2.5f,FieldBuildingsChance=1,Seed=1467,Footprints=library.houses.Select(h=>h.GetComponent<FarmBuildingGrounding>().halfFootprint).ToArray()};
            for(int i=0;i<3;i++){input.Sizes[i]=sizes[i];input.Widths[i]=maps[i].width;input.Heights[i]=maps[i].height;input.Pixels[i]=maps[i].GetPixelData<ushort>(0).ToArray();}
            var original=fields[2];original.halfSize=new Vector2(80,40);var plots=new List<Rect>();Farmstead row=default;
            for(int i=0;i<32;i++){plots=new List<Rect>{new Rect(-80,-40,160,80)};if(AgricultureSettlement.TryEmbedded(input,original.center,original.angle,plots,i,1,out row)&&row.Buildings.Length==2)break;}
            if(!row.embedded)throw new InvalidOperationException("Cannot prepare embedded-row fixture");row.groupId=888;row.gateHalfWidth=4;
            var parcels=new List<FarmField>{fields[0],fields[1]};
            foreach(var r in plots)if(r.width>16&&r.height>16){var f=original;f.center=original.Point(r.center.x,r.center.y);f.halfSize=r.size*.5f-Vector2.one*2.5f;f.height=input.Sample(f.center);f.groupId=888;f.ReserveYard(row);parcels.Add(f);}
            fields=parcels.ToArray();var all=new List<Farmstead>(farms){row};farms=all.ToArray();
            var connections=new List<FarmTrack>(tracks);var end=new Vector2(row.Gate.x,-220);connections.Add(new FarmTrack{a=row.Gate,b=end,controlA=Vector2.Lerp(row.Gate,end,1f/3),controlB=Vector2.Lerp(row.Gate,end,2f/3),ownerGroup=int.MinValue});tracks=connections.ToArray();
            var global=new RoadData(new[]{new RoadSegment(new Vector2(220,-220),new Vector2(220,230),3,2),new RoadSegment(new Vector2(-240,-220),new Vector2(220,-220),3,2)},sizes[2],1,2);
            foreach(int key in global.Bins.Keys.OrderBy(k=>k)){global.TileKeys.Add(key);global.Tiles.Add(RoadRasterizer.Tile(global,key,p=>true,CancellationToken.None));}
            return AgricultureRoads.Combine(global,tracks,sizes[2],3,p=>true,CancellationToken.None);
        }
    }
}
