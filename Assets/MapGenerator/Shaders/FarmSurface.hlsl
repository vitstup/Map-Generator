#ifndef TACTICAL_FARM_SURFACE
#define TACTICAL_FARM_SURFACE
TEXTURE2D(_FarmMap);
TEXTURE2D(_FarmSoil);
float4 _FarmRect;
float _FarmEnabled;
void FarmSurface(float3 world, inout half3 albedo, inout half3 normal)
{
    if(_FarmEnabled<.5)return;
    float2 uv=(world.xz-_FarmRect.xy)/_FarmRect.zw;
    if(any(uv<0)||any(uv>1))return;
    // Sample encoded categorical data at mip zero. Coverage alone uses ordinary area-filtered mips.
    float4 field=SAMPLE_TEXTURE2D_LOD(_FarmMap,sampler_GrassCoverage,uv,0);
    float crop=round(field.b*255/64);
    float coverage=SAMPLE_TEXTURE2D(_FarmMap,sampler_GrassCoverage,uv).r;
    if(coverage<.001)return;
    float angle=field.g*6.2831853;
    float2 axis=float2(cos(angle),sin(angle));
    float2 q=float2(dot(world.xz,axis),dot(world.xz,float2(-axis.y,axis.x)));
    float footprint=max(length(ddx(world.xz)),length(ddy(world.xz)));
    float row=.5+.5*cos(q.y*7.8539816);
    row=lerp(.5,row,1-smoothstep(.25,1.4,footprint));
    if(crop>3.5)row=.5;
    float grain=frac(sin(dot(floor(q*3),float2(127.1,311.7)))*43758.5453);
    half3 soil=lerp(SRGBToLinear(half3(.27,.205,.13)),SRGBToLinear(half3(.38,.29,.18)),field.a)*lerp(.78,1.15,row);
    half3 soilDetail=SAMPLE_TEXTURE2D(_FarmSoil,sampler_GrassTurf,world.xz/3).rgb;
    soil*=lerp(half3(.75,.75,.75),half3(1.25,1.25,1.25),saturate(soilDetail*2));
    soil*=lerp(.92,1.08,grain*(1-smoothstep(.2,3,footprint))+.5*smoothstep(.2,3,footprint));
    half3 green=lerp(SRGBToLinear(half3(.25,.37,.12)),SRGBToLinear(half3(.4,.49,.19)),field.a);
    half3 gold=lerp(SRGBToLinear(half3(.49,.43,.18)),SRGBToLinear(half3(.64,.55,.27)),field.a);
    half3 canopy=crop>1.5&&crop<2.5?gold:green;
    float plant=crop<.5||crop>3.5?0:crop>2.5?.34:.78;
    half3 surface=lerp(soil,canopy,plant*lerp(.65,1,row));
    albedo=lerp(albedo,surface,coverage);
    normal=normalize(lerp(normal,half3(0,0,1),coverage*.9));
}
#endif
