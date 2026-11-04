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
            int[][] sets={new[]{0,1,2,7,8,9},new[]{3,2,0,10,11,12},new[]{4,1,8,9,10,12}};
            foreach(var farm in a.Farms)
            {
                if(Mathf.Abs(farm.center.x)>map.Sizes[0].x/2+250||Mathf.Abs(farm.center.y)>map.Sizes[0].y/2+250)continue;
                var buildings=farm.Buildings;int count=Mathf.Min(18,buildings.Length*3+3+(int)(Hash(farm.groupId,0,300)*3));var occupied=new List<Vector3>();
                for(int i=0;i<count&&result.Count<maximum;i++)
                {
                    // Several seeded sets keep neighbouring yards from repeating the same collection.
                    var set=sets[Mathf.Min(sets.Length-1,(int)(Hash(farm.groupId,0,301)*sets.Length))];
                    int variant=i==0&&buildings.Length>=2&&Hash(farm.groupId,0,302)<.2f?6:i==1&&buildings.Length>=2&&Hash(farm.groupId,0,303)<.65f?5:set[Mathf.Min(set.Length-1,(int)(Hash(farm.groupId,i,1)*set.Length))];
                    if(variant>=library.props.Length || variant>=library.propRadii.Length)continue;
                    float scale=Mathf.Lerp(.9f,1.1f,Hash(farm.groupId,i,2)),radius=library.propRadii[variant]*scale;
                    for(int attempt=0;attempt<32;attempt++)
                    {
                        Vector2 p;
                        if(i%3!=0 && occupied.Count>0 && attempt<8 && variant!=5 && variant!=6)
                        {
                            var previous=occupied[occupied.Count-1];float turn=Hash(farm.groupId,i,20+attempt)*Mathf.PI*2;
                            p=new Vector2(previous.x,previous.y)+new Vector2(Mathf.Cos(turn),Mathf.Sin(turn))*(previous.z+radius+.6f);
                        }
                        else if(attempt<20&&variant!=5&&variant!=6)
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
                        result.Add(new FarmPropInstance{position=ground,angle=-farm.angle*Mathf.Rad2Deg+Mathf.Round(Hash(farm.groupId,i,200)*3)*90+Mathf.Lerp(-12,12,Hash(farm.groupId,i,201)),scale=scale,variant=variant});occupied.Add(new Vector3(p.x,p.y,radius));break;
                    }
                }
            }
            return result.ToArray();
        }
        private static float Hash(int group,int n,int salt){unchecked{uint h=(uint)(group^n*73856093^salt*19349663);h^=h>>16;h*=0x7feb352d;h^=h>>15;return(h&0xffffff)/16777216f;}}
    }
}
