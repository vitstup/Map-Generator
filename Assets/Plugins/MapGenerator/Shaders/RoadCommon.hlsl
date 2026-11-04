#ifndef TACTICAL_ROAD_COMMON
#define TACTICAL_ROAD_COMMON
Texture2D<float4> _RoadPages;
Texture2DArray<float4> _RoadDistances, _RoadDistancesExtra;
float _RoadEnabled;
float4 _RoadGrid, _RoadOptions;
float4 RoadTexel(int2 p,int index)
{
    int split=max(1,(int)_RoadOptions.w);
    if(index>=split)return _RoadDistancesExtra.Load(int4(p,index-split,0));
    return _RoadDistances.Load(int4(p,index,0));
}
float4 RoadSample(float2 world)
{
    if(_RoadEnabled<.5)return float4(1,1,.5,.5);
    float2 p=(world-_RoadGrid.xy)/128;
    int2 cell=(int2)floor(p);
    if(any(cell<0)||any(cell>=(int2)_RoadGrid.zw))return float4(1,1,.5,.5);
    float2 code=_RoadPages.Load(int3(cell,0)).rg;
    int index=(int)round(code.r*255)+(int)round(code.g*255)*256-1;
    if(index<0)return float4(1,1,.5,.5);
    // Explicit bilinear loads leave the Terrain shader's sampler budget unchanged.
    float2 texel=frac(p)*128+.5;
    int2 a=(int2)floor(texel);float2 f=frac(texel);
    float4 d00=RoadTexel(a,index);
    float4 d10=RoadTexel(a+int2(1,0),index);
    float4 d01=RoadTexel(a+int2(0,1),index);
    float4 d11=RoadTexel(a+1,index);
    return lerp(lerp(d00,d10,f.x),lerp(d01,d11,f.x),f.y);
}
float2 RoadDistance(float2 world) { return RoadSample(world).rg*32-16; }
float RoadHash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
float RoadNoise(float2 p)
{
    float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);
    return lerp(lerp(RoadHash(i),RoadHash(i+float2(1,0)),f.x),lerp(RoadHash(i+float2(0,1)),RoadHash(i+1),f.x),f.y);
}
float2 RoadEdgeOffset(float2 world)
{
    float n=(RoadNoise(world/3.7)-.5)*1.5+(RoadNoise(world*1.3)-.5)*.35;
    return n*float2(.8,1.3);
}
float RoadGrassPermission(float2 world)
{
    if(_RoadEnabled<.5)return 1;
    float2 d=RoadDistance(world);
    float nearest=min(d.x,d.y);
    // Almost every blade is outside a road edge; do not evaluate edge noise for those vertices.
    if(nearest>_RoadOptions.x+1.3)return 1;
    if(nearest< -1.3)return 0;
    d+=RoadEdgeOffset(world);
    return smoothstep(0,max(.25,_RoadOptions.x),min(d.x,d.y));
}
#endif
