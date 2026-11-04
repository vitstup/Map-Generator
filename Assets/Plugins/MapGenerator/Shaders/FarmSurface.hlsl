#ifndef TACTICAL_FARM_SURFACE
#define TACTICAL_FARM_SURFACE
#include "FarmCropCommon.hlsl"
TEXTURE2D(_FarmMap);
TEXTURE2D(_FarmSoil);
TEXTURE2D(_FarmSoilNormal);
TEXTURE2D(_FarmPages);
TEXTURE2D(_FarmProfiles);
TEXTURE2D_ARRAY(_FarmDistances);
float4 _FarmRect, _FarmDetailGrid, _FarmCenter;
float _FarmEnabled, _FarmVersion;
float _FarmGeometryDistance;
bool FarmDetail(float2 world, out float signedDistance, out float4 shape, out float4 style, out float4 detail, out float4 exclusion)
{
    signedDistance=32;shape=0;style=0;detail=0;exclusion=0;
    float2 grid=(world-_FarmCenter.xy-_FarmDetailGrid.xy)/128;
    int2 cell=(int2)floor(grid);
    if(any(cell<0)||any(cell>=(int2)_FarmDetailGrid.zw))return false;
    int slice=(int)LOAD_TEXTURE2D(_FarmPages,cell).r-1;
    if(slice<0)return false;
    float2 pixel=frac(grid)*128+1;
    float2 encodedDistance=SAMPLE_TEXTURE2D_ARRAY_LOD(_FarmDistances,sampler_GrassCoverage,pixel/130,slice,0).rg;
    // Linear decoding commutes with bilinear filtering, including low-byte carries.
    signedDistance=dot(encodedDistance,float2(65280,255))*(64.0/65535)-32;
    float2 encodedId=LOAD_TEXTURE2D_ARRAY(_FarmDistances,int2(floor(pixel)),slice).ba;
    int id=(int)round(encodedId.x*255)*256+(int)round(encodedId.y*255)-1;
    if(id<0)return true;
    int2 profile=int2(id%2048,(id/2048)*(_FarmVersion>3.5?4:3));
    shape=LOAD_TEXTURE2D(_FarmProfiles,profile);
    style=LOAD_TEXTURE2D(_FarmProfiles,profile+int2(0,1));
    detail=LOAD_TEXTURE2D(_FarmProfiles,profile+int2(0,2));
    if(_FarmVersion>3.5)exclusion=LOAD_TEXTURE2D(_FarmProfiles,profile+int2(0,3));
    shape.xy+=_FarmCenter.xy;
    return true;
}
// Analytically filtered planting dots: once cells are subpixel, converge to their
// area average instead of sparkling or disappearing. No extra texture or draw call.
float FarmPlantDots(float2 local)
{
    float2 cell=local/.8;
    float2 width=max(abs(ddx(cell))+abs(ddy(cell)),.001);
    // Integrate each smooth periodic dot over the pixel footprint. Separate axes retain
    // visible rows at grazing angles even when the other axis is already subpixel.
    // Stop at Nyquist rather than allowing sinc side lobes to reverse the pattern.
    float2 w=min(width,1)*3.14159265;
    float2 filtered=.5+.5*cos(cell*6.2831853)*(sin(w)/w);
    return filtered.x*filtered.y;
}
void FarmSurface(float3 world, inout half3 albedo, inout half3 normal, out half4 canopy)
{
    canopy=0;
    if(_FarmEnabled<.5||_FarmVersion<2.5)return;
    float2 uv=(world.xz-_FarmRect.xy)/_FarmRect.zw;
    if(any(uv<0)||any(uv>1))return;
    float footprint=max(length(ddx(world.xz)),length(ddy(world.xz)));
    float4 far=SAMPLE_TEXTURE2D(_FarmMap,sampler_GrassCoverage,uv);
    float distanceToCamera=distance(world,_WorldSpaceCameraPos);
    float sd;float4 shape,style,detail,exclusion;
    bool detailed=FarmDetail(world.xz,sd,shape,style,detail,exclusion);
    if(detailed && exclusion.z>0 && exclusion.w>0)
    {
        float2 d=world.xz-shape.xy;
        float2 local=float2(dot(d,style.xy),dot(d,float2(-style.y,style.x)));
        if(all(abs(local-exclusion.xy)<=exclusion.zw))return;
    }
    if(detailed && style.z>.5 && style.z<3.5 && sd<-1)
    {
        float2 delta=world.xz-shape.xy;
        float2 local=float2(dot(delta,style.xy),dot(delta,float2(-style.y,style.x)));
        float distanceToParcel=FarmParcelDistance(_WorldSpaceCameraPos,world.y,shape,style.xy);
        float blend=_FarmGeometryDistance>0?FarmCanopyBlend(distanceToParcel,_FarmGeometryDistance):1;
        float edge=1-smoothstep(-2.6,-1.8,sd);
        // First clump root is .4 metres from the parcel's negative corner.
        // One distant mark represents a cluster. This keeps the canopy readable
        // at tactical zoom without retaining thousands of subpixel blade triangles.
        float2 planting=local+shape.zw-.4;
        float dots=FarmPlantDots(planting*.5);
        canopy=half4(FarmCropColor(style.z,style.w)*.91,dots*edge*blend);
    }
    bool accurate=distanceToCamera<1600 && footprint<12 && detailed;
    if(!accurate)
    {
        albedo=lerp(albedo,far.rgb/max(.0001,far.a),far.a);
        canopy=0; // The filtered horizon mask already contains the canopy average.
        return;
    }
    float aa=clamp(footprint*.6,.08,1.2);
    float amount=1-smoothstep(-aa,aa,sd);
    if(amount<.001)return;
    float2 axis=style.xy,across=float2(-axis.y,axis.x),delta=world.xz-shape.xy;
    float2 q=float2(dot(delta,axis),dot(delta,across));
    float crop=style.z,variation=style.w;
    if(exclusion.z>0 && exclusion.w>0 && all(abs(q-exclusion.xy)<=exclusion.zw))return;
    float phase=(q.y+shape.w-.4)*7.8539816;
    float rowWeight=(1-smoothstep(.15,.9,footprint))*(crop<3.5?1:0);
    float row=.5+.5*cos(phase)*rowWeight;
    float2 soilUV=world.xz/1.8;
    half3 a=SAMPLE_TEXTURE2D(_FarmSoil,sampler_GrassTurf,soilUV).rgb;
    half3 b=SAMPLE_TEXTURE2D(_FarmSoil,sampler_GrassTurf,float2(-soilUV.y,soilUV.x)*.713+.371).rgb;
    half3 soil=(a+b)*.5;
    // Preserve source texture contrast, use a continuous regional tint rather than cell noise.
    float luminance=dot(soil,half3(.2126,.7152,.0722));
    half3 earthTint=lerp(SRGBToLinear(half3(.29,.225,.145)),SRGBToLinear(half3(.4,.31,.2)),variation);
    soil=earthTint*clamp(luminance/.18,.45,1.8)*lerp(.82,1.12,row);
    if(crop>3.5)soil*=crop>4.5?1.12:1;
    half3 surface=soil;
    half3 soilNormal=UnpackNormalScale(SAMPLE_TEXTURE2D(_FarmSoilNormal,sampler_GrassTurf,soilUV),.75);
    float ridge=sin(phase)*rowWeight*.18;
    soilNormal.xy+=across*ridge;
    float farBlend=max(smoothstep(6,12,footprint),smoothstep(1000,1550,distanceToCamera));
    canopy.a*=1-farBlend;
    albedo=lerp(albedo,lerp(surface,far.rgb/max(.0001,far.a),farBlend),lerp(amount,far.a,farBlend));
    normal=normalize(lerp(normal,normalize(soilNormal),amount*(1-farBlend)));
}
#endif
