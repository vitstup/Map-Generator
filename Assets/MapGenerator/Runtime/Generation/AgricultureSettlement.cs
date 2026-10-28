using System;
using System.Collections.Generic;
using UnityEngine;

namespace TacticalMaps.Generation
{
    internal static class AgricultureSettlement
    {
        internal static Farmstead Plan(AgricultureInput input, Vector2 center, float angle, float width, float depth, int x, int z)
        {
            float choice = input.Hash(x,z,310); int count = choice < .36f ? 1 : choice < .70f ? 2 : choice < .9f ? 4 : 6;
            int variants = input.Footprints != null && input.Footprints.Length > 0 ? input.Footprints.Length : 5;
            float extent = 8; if (input.Footprints != null) foreach(var f in input.Footprints) extent = Mathf.Max(extent, f.x, f.y);
            int columns = count == 1 ? 1 : 2, rows = (count + columns - 1) / columns;
            float spacing = extent * 2 + 7;
            var half = new Vector2(columns * spacing * .5f + 4, rows * spacing * .5f + 4);
            var axis = new Vector2(Mathf.Cos(angle),Mathf.Sin(angle)); var side = new Vector2(-axis.y,axis.x);
            // Try a reserved corner inside the field block, an edge straddling it, and an outside site.
            int mode = input.Hash(x,z,311)<.5f?1:2;
            for(int attempt=0;attempt<8;attempt++)
            {
                int placement=attempt<4?mode:(mode==1?2:1), edge=attempt%4;
                float signX = edge%2==0 ? 1:-1, signY=edge<2?1:-1;
                float px = placement==0 ? Mathf.Max(0,width*.5f-half.x-5) : width*.5f+(placement==1?0:half.x+10);
                float pz = placement==0 ? Mathf.Max(0,depth*.5f-half.y-5) : (depth*.5f-half.y)*signY;
                var site = center+axis*(px*signX)+side*(placement==0?pz*signY:pz);
                if(input.Roads!=null && input.Roads.Blocks(site,half.magnitude+3))continue;
                bool valid=true;float low=float.MaxValue, high=float.MinValue;
                for(float b=-half.y;b<=half.y+.01f && valid;b+=Mathf.Max(4,half.y/4))
                    for(float a=-half.x;a<=half.x+.01f;a+=Mathf.Max(4,half.x/4))
                    { var p=site+axis*a+side*b;float elevation=input.Sample(p);low=Mathf.Min(low,elevation);high=Mathf.Max(high,elevation);if(!input.Suitable(p)){valid=false;break;} }
                if(!valid || high-low>3)continue;
                var buildings=new FarmBuilding[count];
                for(int i=0;i<count;i++)
                {
                    int variant=Mathf.Min(variants-1,(int)(input.Hash(x,z,320+i)*variants));
                    float turn=(i%2==0?0:Mathf.PI), yaw=angle+turn;
                    var p=site+axis*((i%columns-(columns-1)*.5f)*spacing)+side*((i/columns-(rows-1)*.5f)*spacing);
                    float scale=Mathf.Lerp(.94f,1.06f,input.Hash(x,z,340+i));
                    var footprint=input.Footprints!=null && input.Footprints.Length>variant?input.Footprints[variant]:Vector2.one*8;
                    buildings[i]=new FarmBuilding{center=p,angle=yaw,height=input.Sample(p),scale=scale,halfSize=footprint*scale,variant=variant};
                }
                var first=buildings[0];
                return new Farmstead{center=site,angle=angle,variant=first.variant,houseHeight=first.height,halfFootprint=first.halfSize,
                    buildings=buildings,yardHalfSize=half,fenceStyle=(int)(input.Hash(x,z,350)*3)};
            }
            return default;
        }
        internal static bool TryEmbedded(AgricultureInput input,Vector2 center,float angle,List<Rect> plots,int x,int z,out Farmstead farm)
        {
            farm=default;if(input.Hash(x,z,401)>=input.FieldBuildingsChance || plots.Count==0)return false;
            int variant=0;float smallest=float.MaxValue;
            if(input.Footprints!=null)for(int i=0;i<input.Footprints.Length;i++){float area=input.Footprints[i].x*input.Footprints[i].y;if(area<smallest){smallest=area;variant=i;}}
            var footprint=input.Footprints!=null&&input.Footprints.Length>variant?input.Footprints[variant]:Vector2.one*8;
            int count=input.Hash(x,z,402)<.55f?1:2;float spacing=footprint.y*2+5;
            float rowHalf=count*spacing*.5f+3, depth=footprint.x*2+10;
            float bottom=float.MaxValue;foreach(var r in plots)bottom=Mathf.Min(bottom,r.yMin);
            var axis=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));var side=new Vector2(-axis.y,axis.x);
            for(int index=0;index<plots.Count;index++)
            {
                var r=plots[index];if(Mathf.Abs(r.yMin-bottom)>.1f || r.width<rowHalf*2+36 || r.height<depth+44)continue;
                const float front=18;var cut=new Rect(r.center.x-rowHalf-5,r.yMin+front,rowHalf*2+10,depth+8);
                var site=center+axis*cut.center.x+side*(r.yMin+front+depth*.5f+3);float yaw=angle-Mathf.PI/2;
                var half=new Vector2(depth*.5f,rowHalf);bool valid=true;float low=float.MaxValue,high=float.MinValue;
                for(int b=-1;b<=1;b++)for(int a=-1;a<=1;a++){var p=site-side*(a*half.x)+axis*(b*half.y);float y=input.Sample(p);low=Mathf.Min(low,y);high=Mathf.Max(high,y);if(!input.Suitable(p))valid=false;}
                if(!valid||high-low>1.5f||input.Roads!=null&&input.Roads.Blocks(site,half.magnitude+4))continue;
                var buildings=new FarmBuilding[count];
                for(int i=0;i<count;i++){var p=site+axis*((i-(count-1)*.5f)*spacing);buildings[i]=new FarmBuilding{center=p,angle=yaw,variant=variant,scale=1,height=input.Sample(p),halfSize=footprint};}
                farm=new Farmstead{center=site,angle=yaw,variant=variant,buildings=buildings,yardHalfSize=half,embedded=true,fenceStyle=0,halfFootprint=footprint,houseHeight=buildings[0].height,parcelCenter=center+axis*r.center.x+side*r.center.y,parcelHalfSize=r.size*.5f,parcelAngle=angle,gateLead=front+8};
                plots.RemoveAt(index);
                float lane=input.AccessWidth*.5f+(input.Roads?.Shoulder ?? 1)+5;
                plots.Add(new Rect(r.xMin,r.yMin,r.center.x-lane-r.xMin,front));
                plots.Add(new Rect(r.center.x+lane,r.yMin,r.xMax-r.center.x-lane,front));
                plots.Add(new Rect(r.xMin,cut.yMin,cut.xMin-r.xMin,cut.height));
                plots.Add(new Rect(cut.xMax,cut.yMin,r.xMax-cut.xMax,cut.height));
                plots.Add(new Rect(r.xMin,cut.yMax,r.width,r.yMax-cut.yMax));
                return true;
            }
            return false;
        }
        internal static bool Overlaps(FarmField field,Farmstead farm)
        {
            // Conservative SAT between the parcel and the reserved grassy yard, including the fence.
            var a=field.Axis;var b=new Vector2(-a.y,a.x);var c=new Vector2(Mathf.Cos(farm.angle),Mathf.Sin(farm.angle));var d=new Vector2(-c.y,c.x);
            var delta=farm.center-field.center;var h=farm.yardHalfSize+Vector2.one*4;
            foreach(var n in new[]{a,b,c,d})
                if(Mathf.Abs(Vector2.Dot(delta,n))>field.halfSize.x*Mathf.Abs(Vector2.Dot(a,n))+field.halfSize.y*Mathf.Abs(Vector2.Dot(b,n))+h.x*Mathf.Abs(Vector2.Dot(c,n))+h.y*Mathf.Abs(Vector2.Dot(d,n)))return false;
            return true;
        }
    }
}
