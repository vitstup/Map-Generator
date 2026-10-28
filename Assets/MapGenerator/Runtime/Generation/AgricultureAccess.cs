using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace TacticalMaps.Generation
{
    // Bounded local routing; the global road graph is never rebuilt for agricultural access.
    internal static class AgricultureAccess
    {
        private sealed class RoadIndex
        {
            private readonly Dictionary<Vector2Int,List<int>> bins=new Dictionary<Vector2Int,List<int>>();
            private readonly RoadData roads;
            internal RoadIndex(RoadData roads)
            {
                this.roads=roads;
                for(int i=0;i<roads.Segments.Length;i++)
                {var r=roads.Segments[i];var lo=Vector2.Min(r.A,r.B);var hi=Vector2.Max(r.A,r.B);
                    for(int y=Mathf.FloorToInt(lo.y/256);y<=Mathf.FloorToInt(hi.y/256);y++)for(int x=Mathf.FloorToInt(lo.x/256);x<=Mathf.FloorToInt(hi.x/256);x++)
                    {var key=new Vector2Int(x,y);if(!bins.TryGetValue(key,out var list))bins[key]=list=new List<int>();list.Add(i);}}
            }
            internal bool Nearest(Vector2 p,float limit,out Vector2 end)
            {
                end=default;float best=limit*limit;bool found=false;var seen=new HashSet<int>();
                for(int y=Mathf.FloorToInt((p.y-limit)/256);y<=Mathf.FloorToInt((p.y+limit)/256);y++)for(int x=Mathf.FloorToInt((p.x-limit)/256);x<=Mathf.FloorToInt((p.x+limit)/256);x++)
                    if(bins.TryGetValue(new Vector2Int(x,y),out var list))foreach(int i in list)if(seen.Add(i))
                    {var r=roads.Segments[i];var ab=r.B-r.A;var q=r.A+ab*Mathf.Clamp01(Vector2.Dot(p-r.A,ab)/Mathf.Max(.001f,ab.sqrMagnitude));float d=(p-q).sqrMagnitude;if(d<best){best=d;end=q;found=true;}}
                return found;
            }
        }
        internal static FarmTrack[] Build(AgricultureInput input,FarmField[] fields,Farmstead[] farms,CancellationToken token, Action<int,int> report = null)
        {
            if(input.Roads==null || input.Roads.Segments.Length==0 || input.ConnectionRadius<=0)return Array.Empty<FarmTrack>();
            var roads=new RoadIndex(input.Roads);var obstacles=new AgricultureData(fields,farms,null,null);
            var groups=new Dictionary<int,List<Vector2>>();
            foreach(var f in fields)
            {
                if(!groups.TryGetValue(f.groupId,out var points))groups[f.groupId]=points=new List<Vector2>();
                points.Add(f.Point(f.halfSize.x+4,0));points.Add(f.Point(-f.halfSize.x-4,0));points.Add(f.Point(0,f.halfSize.y+4));points.Add(f.Point(0,-f.halfSize.y-4));
            }
            var owners=new Dictionary<int,Farmstead>();foreach(var farm in farms){groups[farm.groupId]=new List<Vector2>{farm.Gate};owners[farm.groupId]=farm;}
            var tracks=new List<FarmTrack>();var shared=new List<Vector2>();int requested=0,connected=0;
            foreach(var pair in groups)
            {
                token.ThrowIfCancellationRequested();
                bool eligible=false;foreach(var p in pair.Value)if(p.sqrMagnitude<=input.ConnectionRadius*input.ConnectionRadius){eligible=true;break;}
                if(!eligible)continue;requested++;
                float best=float.MaxValue;Vector2 start=default,end=default;
                foreach(var p in pair.Value)
                {
                    if(p.sqrMagnitude>input.ConnectionRadius*input.ConnectionRadius || obstacles.Blocks(p,2))continue;
                    if(roads.Nearest(p,input.AccessLength,out var q) && (p-q).sqrMagnitude<best){best=(p-q).sqrMagnitude;start=p;end=q;}
                }
                if(best==float.MaxValue)continue;
                foreach(var p in shared)if((p-start).sqrMagnitude<best*.7f){end=p;best=(p-start).sqrMagnitude;}
                float clearance=input.AccessWidth*.5f+(input.Roads?.Shoulder ?? 1)+1.5f;
                bool Free(Vector2 p)=>input.Suitable(p)&&!obstacles.Blocks(p,clearance)&&!obstacles.YardsBlockSegment(p,p,clearance);
                bool Clear(Vector2 a,Vector2 b)
                {
                    if(obstacles.YardsBlockSegment(a,b,clearance))return false;
                    int n=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(a,b)/3));float before=input.Sample(a);
                    for(int i=0;i<=n;i++){var p=Vector2.Lerp(a,b,(float)i/n);float h=input.Sample(p);if(!Free(p)||Mathf.Abs(h-before)>1.1f)return false;before=h;}return true;
                }
                FarmTrack? entrance=null;
                if(owners.TryGetValue(pair.Key,out var owner))
                {
                    var inner=owner.center+owner.Axis*(owner.yardHalfSize.x-.5f);
                    if(obstacles.YardsBlockSegment(inner,owner.Gate,clearance,owner.groupId))continue;
                    int samples=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(inner,owner.Gate)/3));bool valid=true;float elevation=input.Sample(inner);
                    for(int j=0;j<=samples;j++){var p=Vector2.Lerp(inner,owner.Gate,j/(float)samples);float y=input.Sample(p);if(!input.Suitable(p)||obstacles.Blocks(p,clearance)||Mathf.Abs(y-elevation)>1.1f){valid=false;break;}elevation=y;}
                    if(!valid)continue;
                    entrance=new FarmTrack{a=inner,b=owner.Gate,controlA=Vector2.Lerp(inner,owner.Gate,1f/3),controlB=Vector2.Lerp(inner,owner.Gate,2f/3),ownerGroup=owner.groupId};
                }
                List<Vector2> path;
                if(Clear(start,end))path=new List<Vector2>{start,end};
                else path=Route(start,end,input.AccessLength,Free,Clear,input.Sample,token);
                if(path==null)continue;
                float length=0;for(int i=1;i<path.Count;i++)length+=Vector2.Distance(path[i-1],path[i]);if(length>input.AccessLength)continue;
                // Round each junction with a checked cubic rather than painting square cells.
                var cursor=path[0];
                for(int i=1;i<path.Count-1;i++)
                {
                    var p=path[i];float r=Mathf.Min(18,Vector2.Distance(path[i-1],p)*.3f,Vector2.Distance(p,path[i+1])*.3f);
                    var a=p+(path[i-1]-p).normalized*r;var b=p+(path[i+1]-p).normalized*r;
                    var curve=new FarmTrack{a=a,b=b,controlA=Vector2.Lerp(a,p,.67f),controlB=Vector2.Lerp(b,p,.67f),ownerGroup=int.MinValue};
                    bool valid=true;var before=a;for(int j=1;j<=16;j++){var p2=curve.Point(j/16f);if(!Clear(before,p2)){valid=false;break;}before=p2;}
                    if(valid){AddLine(cursor,a);tracks.Add(curve);cursor=b;}else{AddLine(cursor,p);cursor=p;}
                }
                AddLine(cursor,end);connected++;
                if(entrance.HasValue)tracks.Add(entrance.Value);
                for(int i=1;i<path.Count;i++) { int n=Mathf.CeilToInt(Vector2.Distance(path[i-1],path[i])/80);for(int j=0;j<n;j++)shared.Add(Vector2.Lerp(path[i-1],path[i],j/(float)n)); }
                void AddLine(Vector2 a,Vector2 b){if((a-b).sqrMagnitude>1)tracks.Add(new FarmTrack{a=a,b=b,controlA=Vector2.Lerp(a,b,1f/3),controlB=Vector2.Lerp(a,b,2f/3),ownerGroup=int.MinValue});}
            }
            report?.Invoke(requested,connected);return tracks.ToArray();
        }
        private static List<Vector2> Route(Vector2 start,Vector2 end,float limit,Func<Vector2,bool> free,Func<Vector2,Vector2,bool> clear,Func<Vector2,float> height,CancellationToken token)
        {
            const float step=12;var origin=Vector2.Min(start,end)-Vector2.one*144;var extent=Vector2.Max(start,end)+Vector2.one*144-origin;
            int width=Mathf.CeilToInt(extent.x/step)+1,rows=Mathf.CeilToInt(extent.y/step)+1;
            int Key(Vector2 p)=>Mathf.Clamp(Mathf.RoundToInt((p.y-origin.y)/step),0,rows-1)*width+Mathf.Clamp(Mathf.RoundToInt((p.x-origin.x)/step),0,width-1);
            Vector2 Point(int key)=>origin+new Vector2(key%width,key/width)*step;
            int first=Key(start),goal=Key(end);var scores=new Dictionary<int,float>{{first,0}};var previous=new Dictionary<int,int>();var queue=new Heap();queue.Push(first,Vector2.Distance(start,end));
            var closed=new HashSet<int>();int visited=0,finish=-1;
            while(queue.Count>0 && visited++<5000)
            {
                if((visited&127)==0)token.ThrowIfCancellationRequested();int current=queue.Pop();if(!closed.Add(current))continue;var p=current==first?start:Point(current);
                if((current==goal || Vector2.Distance(p,end)<80)&&clear(p,end)){finish=current;break;}
                for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++)
                {
                    if(x==0&&y==0)continue;int cx=current%width+x,cy=current/width+y;if(cx<0||cy<0||cx>=width||cy>=rows)continue;int next=cy*width+cx;if(closed.Contains(next))continue;
                    var q=Point(next);if(!free(q)||!clear(p,q))continue;float cost=scores[current]+Vector2.Distance(p,q)+Mathf.Abs(height(q)-height(p))*5;
                    if(cost>limit || scores.TryGetValue(next,out float old)&&old<=cost)continue;scores[next]=cost;previous[next]=current;queue.Push(next,cost+Vector2.Distance(q,end)*1.05f);
                }
            }
            if(finish<0)return null;var path=new List<Vector2>{end};int at=finish;
            while(at!=first){path.Add(Point(at));at=previous[at];}path.Add(start);path.Reverse();
            var smooth=new List<Vector2>{start};int index=0;
            while(index<path.Count-1){int last=path.Count-1;while(last>index+1&&!clear(path[index],path[last]))last--;smooth.Add(path[last]);index=last;}return smooth;
        }
        private sealed class Heap
        {
            private readonly List<(int key,float score)> values=new List<(int,float)>();internal int Count=>values.Count;
            internal void Push(int key,float score){values.Add((key,score));int i=values.Count-1;while(i>0){int p=(i-1)/2;if(values[p].score<=score)break;values[i]=values[p];i=p;}values[i]=(key,score);}
            internal int Pop(){var result=values[0];var tail=values[values.Count-1];values.RemoveAt(values.Count-1);if(values.Count==0)return result.key;int i=0;while(i*2+1<values.Count){int child=i*2+1;if(child+1<values.Count&&values[child+1].score<values[child].score)child++;if(values[child].score>=tail.score)break;values[i]=values[child];i=child;}values[i]=tail;return result.key;}
        }
    }
}
