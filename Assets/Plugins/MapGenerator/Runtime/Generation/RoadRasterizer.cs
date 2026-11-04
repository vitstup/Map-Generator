using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
namespace TacticalMaps.Generation
{
    internal static class RoadRasterizer
    {
            internal static Color32[] Tile(RoadData data,int key,Func<Vector2,bool> dry,CancellationToken token)
            {
                int n=RoadData.TilePixels;var red=new float[n*n];var green=new float[n*n];var flowRed=new Vector2[n*n];var flowGreen=new Vector2[n*n];
                for(int i=0;i<red.Length;i++)red[i]=green[i]=16;
                var origin=data.Origin+new Vector2(key%data.Columns,key/data.Columns)*RoadData.TileSize;
                foreach(int index in data.Bins[key])
                {
                    token.ThrowIfCancellationRequested();var s=data.Segments[index];var delta=s.B-s.A;float squared=delta.sqrMagnitude;if(squared<.0001f)continue;
                    float inverse=1/squared;var tangent=delta/Mathf.Sqrt(squared);float margin=s.HalfWidth+Mathf.Min(16,data.Shoulder+2);
                    var lo=Vector2.Min(s.A,s.B)-origin-Vector2.one*margin;var hi=Vector2.Max(s.A,s.B)-origin+Vector2.one*margin;
                    int x0=Mathf.Max(0,Mathf.FloorToInt(lo.x+.5f)),x1=Mathf.Min(n-1,Mathf.CeilToInt(hi.x+.5f));
                    int y0=Mathf.Max(0,Mathf.FloorToInt(lo.y+.5f)),y1=Mathf.Min(n-1,Mathf.CeilToInt(hi.y+.5f));
                    var distances=s.Type==1?red:green;var flows=s.Type==1?flowRed:flowGreen;
                    for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++)
                    {
                        int pixel=y*n+x;var p=origin+new Vector2(x-.5f,y-.5f);float t=Mathf.Clamp01(Vector2.Dot(p-s.A,delta)*inverse);
                        float halfWidth=Mathf.Lerp(s.HalfWidthA,s.HalfWidthB,t);
                        float d=(p-s.A-delta*t).sqrMagnitude;float limit=halfWidth+distances[pixel];
                        if(limit<=0||d>=limit*limit)continue;
                        distances[pixel]=Mathf.Sqrt(d)-halfWidth;flows[pixel]=tangent;
                    }
                }
                var pixels=new Color32[n*n];var area=new Vector2[16];var nearbyTransitions=new List<Vector2>();
                foreach(var node in data.TypeTransitions){var p=new Vector2(node.x,node.y);if(p.x>origin.x-32&&p.y>origin.y-32&&p.x<origin.x+160&&p.y<origin.y+160)nearbyTransitions.Add(p);}
                for(int y=0;y<n;y++)
                {
                    if((y&15)==0)token.ThrowIfCancellationRequested();
                    for(int x=0;x<n;x++)
                    {
                        int i=y*n+x;float a=red[i],b=green[i];var p=origin+new Vector2(x-.5f,y-.5f);
                        if(Mathf.Min(a,b)<data.Shoulder+1&&!dry(p))a=b=16;
                        var flow=a<data.Shoulder+1?flowRed[i]:flowGreen[i];
                        float mix=0;if(a<data.Shoulder+1)foreach(var node in nearbyTransitions)mix=Mathf.Max(mix,1-Mathf.SmoothStep(0,1,Vector2.Distance(p,node)/28));
                        float angle=Mathf.Repeat(Mathf.Atan2(flow.y,flow.x)/(2*Mathf.PI),1);
                        pixels[i]=new Color32(Encode(a),Encode(b),(byte)Mathf.RoundToInt(angle*255),(byte)Mathf.RoundToInt(mix*255));
                        if(x<1||y<1||x>128||y>128)continue;
                        float ca=Coverage(a,data.Shoulder),cb=Coverage(b,data.Shoulder)*(1-ca);area[((y-1)/32)*4+(x-1)/32]+=new Vector2(ca*(1-mix),cb+ca*mix);
                    }
                }
                int tx=(key%data.Columns)*4,ty=(key/data.Columns)*4;
                for(int y=0;y<4;y++)for(int x=0;x<4;x++){var a=area[y*4+x]/1024;data.FarCoverage[(ty+y)*data.FarWidth+tx+x]=new Color(a.x,a.y,0,1);}
                return pixels;
            }
            private static byte Encode(float d)=>(byte)Mathf.RoundToInt(Mathf.Clamp01((d+16)/32)*255);
            private static float Coverage(float d,float shoulder){float t=Mathf.Clamp01((d+.35f)/(shoulder+.7f));return 1-t*t*(3-2*t);}
    }
}
