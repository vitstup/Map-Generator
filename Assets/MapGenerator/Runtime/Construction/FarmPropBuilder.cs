using System;
using System.Collections.Generic;
using TacticalMaps.Generation;
using UnityEngine;
namespace TacticalMaps.Construction
{
    [Serializable] internal struct FarmPropInstance { public Vector3 position;public float angle,scale;public int variant; }
    internal static class FarmPropBuilder
    {
        internal static FarmPropInstance[] Place(MapData map,AgricultureLibrary library,Terrain[] terrains,Vector3 center,int maximum)
        {
            var result=new List<FarmPropInstance>();if(library.props==null||library.propRadii==null)return result.ToArray();var a=map.Agriculture;
            foreach(var farm in a.Farms)
            {
                if(Mathf.Abs(farm.center.x)>map.Sizes[0].x/2+250||Mathf.Abs(farm.center.y)>map.Sizes[0].y/2+250)continue;
                var buildings=farm.Buildings;int count=farm.embedded?2:Mathf.Min(12,buildings.Length*2+2);var occupied=new List<Vector3>();
                for(int i=0;i<count&&result.Count<maximum;i++)
                {
                    int variant=!farm.embedded&&i==0&&buildings.Length>=4?6:!farm.embedded&&i==1&&buildings.Length>=2?5:Mathf.Min(4,(int)(Hash(farm.groupId,i,1)*5));
                    float scale=Mathf.Lerp(.9f,1.1f,Hash(farm.groupId,i,2)),radius=library.propRadii[variant]*scale;
                    for(int attempt=0;attempt<32;attempt++)
                    {
                        Vector2 p;
                        if(attempt<20&&variant<5)
                        {
                            var building=buildings[(i+attempt)%buildings.Length];var axis=new Vector2(Mathf.Cos(building.angle),Mathf.Sin(building.angle));var side=new Vector2(-axis.y,axis.x);
                            float sign=Hash(farm.groupId,i,10+attempt)<.5f?-1:1;
                            p=building.center+axis*(sign*(building.halfSize.x+radius+.65f))+side*((Hash(farm.groupId,i,50+attempt)-.5f)*building.halfSize.y*1.4f);
                        }
                        else p=farm.center+farm.Axis*((Hash(farm.groupId,i,90+attempt)-.5f)*2*Mathf.Max(0,farm.yardHalfSize.x-radius-1))+new Vector2(-farm.Axis.y,farm.Axis.x)*((Hash(farm.groupId,i,130+attempt)-.5f)*2*Mathf.Max(0,farm.yardHalfSize.y-radius-1));
                        if(!farm.ContainsYard(p,-radius-.4f)||farm.GateCorridor(p,-radius-.5f)||a.Blocks(p,radius+.45f)||map.Roads!=null&&map.Roads.Blocks(p,radius+.2f))continue;
                        bool free=true;foreach(var other in occupied)if((p-new Vector2(other.x,other.y)).magnitude<radius+other.z+.5f){free=false;break;}if(!free)continue;
                        var ground=AgricultureBuilder.Ground(p,terrains,center,-.025f);float low=ground.y,high=ground.y;
                        foreach(var d in new[]{Vector2.right,Vector2.left,Vector2.up,Vector2.down}){float y=AgricultureBuilder.Ground(p+d*radius*.65f,terrains,center).y;low=Mathf.Min(low,y);high=Mathf.Max(high,y);}if(high-low>.25f)continue;
                        result.Add(new FarmPropInstance{position=ground,angle=Hash(farm.groupId,i,200)*360,scale=scale,variant=variant});occupied.Add(new Vector3(p.x,p.y,radius));break;
                    }
                }
            }
            return result.ToArray();
        }
        private static float Hash(int group,int n,int salt){unchecked{uint h=(uint)(group^n*73856093^salt*19349663);h^=h>>16;h*=0x7feb352d;h^=h>>15;return(h&0xffffff)/16777216f;}}
    }
}
