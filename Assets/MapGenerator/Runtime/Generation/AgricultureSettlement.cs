using System;
using System.Collections.Generic;
using UnityEngine;

namespace TacticalMaps.Generation
{
    internal static class AgricultureSettlement
    {
        internal static bool TryEmbedded(AgricultureInput input,Vector2 center,float angle,List<Rect> plots,int x,int z,out Farmstead farm)
        {
            farm=default;if(plots.Count==0)return false;
            int variants=input.Footprints!=null && input.Footprints.Length>0?input.Footprints.Length:5;
            int firstVariant=Mathf.Min(variants-1,(int)(input.Hash(x,z,404)*variants));
            int count=input.Hash(x,z,402)<.55f?1:2;
            var axis=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));var side=new Vector2(-axis.y,axis.x);
            float bottom=float.MaxValue;foreach(var plot in plots)bottom=Mathf.Min(bottom,plot.yMin);
            for(int index=0;index<plots.Count;index++)
            {
                var r=plots[index];if(Mathf.Abs(r.yMin-bottom)>.1f)continue;
                for(int choice=0;choice<variants;choice++)
                {
                    int variant=(firstVariant+choice)%variants;
                    float scale=Mathf.Lerp(.96f,1.04f,input.Hash(x,z,420+variant));
                    var footprint=(input.Footprints!=null&&input.Footprints.Length>variant?input.Footprints[variant]:Vector2.one*8)*scale;
                    float spacing=footprint.y*2+5,rowHalf=count*(footprint.y*2+5)*.5f+3,depth=footprint.x*2+12;
                    const float border=7;
                    if(r.width<rowHalf*2+24 || r.height<depth+36)continue;
                    // Keep the original parcel. The yard sits just inside its outer lower edge.
                    float localX=Mathf.Lerp(r.xMin+rowHalf+10,r.xMax-rowHalf-10,input.Hash(x,z,410+index));
                    var site=center+axis*localX+side*(r.yMin+border+depth*.5f);float yaw=angle-Mathf.PI/2;
                    var half=new Vector2(depth*.5f,rowHalf);bool valid=true;float low=float.MaxValue,high=float.MinValue;
                    for(int b=-1;b<=1;b++)for(int a=-1;a<=1;a++){var p=site-side*(a*half.x)+axis*(b*half.y);float y=input.Sample(p);low=Mathf.Min(low,y);high=Mathf.Max(high,y);if(!input.Suitable(p))valid=false;}
                    if(!valid||high-low>1.5f||input.Roads!=null&&input.Roads.Blocks(site,half.magnitude+4))continue;
                    var buildings=new FarmBuilding[count];
                    for(int i=0;i<count;i++){var p=site+axis*((i-(count-1)*.5f)*spacing);buildings[i]=new FarmBuilding{center=p,angle=yaw,variant=variant,scale=scale,height=input.Sample(p),halfSize=footprint};}
                    farm=new Farmstead{center=site,angle=yaw,variant=variant,buildings=buildings,yardHalfSize=half,embedded=true,fenceStyle=(int)(input.Hash(x,z,411)*3),halfFootprint=footprint,houseHeight=buildings[0].height,parcelCenter=center+axis*r.center.x+side*r.center.y,parcelHalfSize=r.size*.5f-Vector2.one*2.5f,parcelAngle=angle,gateLead=border+8};
                    return true;
                }
            }
            return false;
        }
        internal static bool TryOutside(AgricultureInput input,Vector2 center,float angle,float width,float depth,int x,int z,out Farmstead farm)
        {
            farm=default;
            int variants=input.Footprints!=null && input.Footprints.Length>0?input.Footprints.Length:5;
            int variant=Mathf.Min(variants-1,(int)(input.Hash(x,z,450)*variants));
            float scale=Mathf.Lerp(.96f,1.04f,input.Hash(x,z,451));
            var footprint=(input.Footprints!=null && input.Footprints.Length>variant?input.Footprints[variant]:Vector2.one*8)*scale;
            int count=input.Hash(x,z,452)<.55f?1:2;
            float spacing=footprint.y*2+5;var half=new Vector2(footprint.x+6,count*spacing*.5f+3);
            var axis=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));var side=new Vector2(-axis.y,axis.x);
            int firstEdge=Mathf.Min(3,(int)(input.Hash(x,z,453)*4));
            for(int attempt=0;attempt<4;attempt++)
            {
                int edge=(firstEdge+attempt)%4;float turn=edge*Mathf.PI*.5f,yaw=angle+turn;
                var outward=axis*Mathf.Cos(turn)+side*Mathf.Sin(turn);var across=new Vector2(-outward.y,outward.x);
                float normalExtent=edge%2==0?width*.5f:depth*.5f,tangentExtent=edge%2==0?depth*.5f:width*.5f;
                float along=(input.Hash(x,z,460+edge)-.5f)*2*Mathf.Max(0,tangentExtent-half.y);
                var site=center+outward*(normalExtent+half.x+12)+across*along;
                if(input.Roads!=null && input.Roads.Blocks(site,half.magnitude+4))continue;
                bool valid=true;float low=float.MaxValue,high=float.MinValue;
                for(int b=-1;b<=1;b++)for(int a=-1;a<=1;a++)
                {var point=site+outward*(a*half.x)+across*(b*half.y);float y=input.Sample(point);low=Mathf.Min(low,y);high=Mathf.Max(high,y);if(!input.Suitable(point))valid=false;}
                if(!valid || high-low>1.5f)continue;
                var buildings=new FarmBuilding[count];
                for(int i=0;i<count;i++){var point=site+across*((i-(count-1)*.5f)*spacing);buildings[i]=new FarmBuilding{center=point,angle=yaw,variant=variant,scale=scale,height=input.Sample(point),halfSize=footprint};}
                farm=new Farmstead{center=site,angle=yaw,variant=variant,buildings=buildings,yardHalfSize=half,halfFootprint=footprint,houseHeight=buildings[0].height,fenceStyle=(int)(input.Hash(x,z,470)*3)};
                return true;
            }
            return false;
        }
        internal static void ShapePlots(AgricultureInput input,List<Rect> plots,float width,float depth,int x,int z)
        {
            if(plots.Count<3 || input.Hash(x,z,500)>.6f)return;
            int removals=input.Hash(x,z,501)<.65f?1:2;
            for(int step=0;step<removals && plots.Count>2;step++)
            {
                int chosen=-1;float rank=float.MaxValue;
                for(int i=0;i<plots.Count;i++)
                {
                    var r=plots[i];bool horizontal=Mathf.Abs(r.xMin+width*.5f)<.1f || Mathf.Abs(r.xMax-width*.5f)<.1f;
                    bool vertical=Mathf.Abs(r.yMin+depth*.5f)<.1f || Mathf.Abs(r.yMax-depth*.5f)<.1f;
                    if(!horizontal || !vertical || r.width*r.height>width*depth*.4f || !ConnectedWithout(plots,i) || !IrregularWithout(plots,i))continue;
                    float value=input.Hash(x,z,510+step*16+i);if(value<rank){rank=value;chosen=i;}
                }
                if(chosen>=0)plots.RemoveAt(chosen);
            }
        }
        private static bool IrregularWithout(List<Rect> plots,int removed)
        {
            Vector2 low=Vector2.one*float.MaxValue,high=Vector2.one*float.MinValue;float area=0;
            for(int i=0;i<plots.Count;i++)if(i!=removed){low=Vector2.Min(low,plots[i].min);high=Vector2.Max(high,plots[i].max);area+=plots[i].width*plots[i].height;}
            return area<(high.x-low.x)*(high.y-low.y)*.98f;
        }
        private static bool ConnectedWithout(List<Rect> plots,int removed)
        {
            var visited=new bool[plots.Count];var queue=new Queue<int>();int first=removed==0?1:0;queue.Enqueue(first);visited[first]=true;int count=0;
            while(queue.Count>0)
            {
                int at=queue.Dequeue();count++;var a=plots[at];
                for(int i=0;i<plots.Count;i++)if(i!=removed && !visited[i])
                {
                    var b=plots[i];bool adjacent=(Mathf.Abs(a.xMax-b.xMin)<.1f || Mathf.Abs(a.xMin-b.xMax)<.1f) && Mathf.Min(a.yMax,b.yMax)-Mathf.Max(a.yMin,b.yMin)>.1f
                        || (Mathf.Abs(a.yMax-b.yMin)<.1f || Mathf.Abs(a.yMin-b.yMax)<.1f) && Mathf.Min(a.xMax,b.xMax)-Mathf.Max(a.xMin,b.xMin)>.1f;
                    if(adjacent){visited[i]=true;queue.Enqueue(i);}
                }
            }
            return count==plots.Count-1;
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
