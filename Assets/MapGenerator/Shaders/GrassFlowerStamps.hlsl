#ifndef TACTICAL_GRASS_FLOWER_STAMPS
#define TACTICAL_GRASS_FLOWER_STAMPS
#include "GrassFlowers.hlsl"
// Paint individual, world-anchored heads directly in existing materials.
// This has no additional draw, renderer, global map, or distant flower mesh.
float4 GrassFlowerStampLegacyLevel(float3 world,float seed,float spacing,float permission,float retention=1,float sizeScale=1)
{
    if(_GrassFlowerCount==0||_GrassFlowers.x<=0)return 0;
    float distanceToCamera=distance(world,_WorldSpaceCameraPos);
    float visibility=smoothstep(65,140,distanceToCamera);
    if(visibility<=0)return 0;
    float4 positionCS=TransformWorldToHClip(world);
    // A grazing card-to-canopy ray can intersect behind the eye. Dividing by
    // negative w mirrors that point onto the image and creates a bright stripe.
    if(positionCS.w<=.01)return 0;
    float2 screen=positionCS.xy/positionCS.w*_ScreenParams.xy*.5;
    float pixelsPerMetre=_ScreenParams.y*.5*abs(UNITY_MATRIX_P._m11)/(unity_OrthoParams.w>.5?1:max(.1,positionCS.w));
    float2 cell=floor(world.xz/spacing);
    float3 sum=0;float coverage=0;
    [loop]for(int z=-1;z<=1;z++)[loop]for(int x=-1;x<=1;x++)
    {
        float2 center;
        float2 tile=cell+float2(x,z);
        // At sub-pixel ground resolution retain a stable subset of real sites,
        // rather than accumulating many enlarged heads into a colored carpet.
        float2 sub=floor(float2(GrassHash(tile+seed+31),GrassHash(tile.yx+seed+73))*spacing);
        if(!GrassFlowerSite(tile*spacing+sub,seed,center))continue;
        if(GrassHash(floor(center)+seed+509.3)>retention)continue;
        // Match the near grass donor's biome/density rejection, rather than
        // painting extra heads into gaps where the donor plant was rejected.
        float2 donor=floor(center*4)*.25;
        if(GrassHash(donor+191.7+seed)>=permission)continue;
        int species=GrassFlowerSpecies(center,seed),form=GrassFlowerForm(species);
        float4 centerCS=TransformWorldToHClip(float3(center.x,world.y,center.y));
        if(centerCS.w<=.01)continue;
        float2 delta=screen-centerCS.xy/centerCS.w*_ScreenParams.xy*.5;
        float nominal=_GrassFlowers.z*GrassFlowerSize(species)*pixelsPerMetre;
        // Keep a head readable, not a broad color wash. Bound its enlargement.
        float radius=max(nominal,min(.7,nominal*5.5))*sizeScale;
        float2 p=delta/max(.001,radius);
        float angle=atan2(p.y,p.x)+GrassHash(center+seed)*6.283185;
        float lobe=1-abs(frac(angle*(GrassFlowerPetals(form)/6.283185)+.5)-.5)*2;
        float edge=form==0?.62+.38*lobe:form==1?.55+.45*lobe:form==2?.6+.4*lobe:.22+.78*lobe;
        float d=length(p);
        float aa=.35/max(.25,radius);
        float mask=1-smoothstep(edge-aa,edge+aa,d);
        float core=1-smoothstep(.18-aa*.25,.25+aa*.25,d);
        float3 color=lerp(GrassFlowerColor(species).rgb,GrassFlowerCenterColor(form),core*.7);
        float amount=mask*visibility;
        sum+=color*amount;coverage+=amount;
    }
    return float4(sum/max(.0001,coverage),saturate(coverage));
}
float4 GrassFlowerStampLegacy(float3 world,float seed,float permission,float retention=1,float sizeScale=1)
{
    if(permission<=0)return 0;
    float footprint=max(length(ddx(world.xz)),length(ddy(world.xz)));
    float level=clamp(log2(max(1,footprint)),0,4);
    float spacing=exp2(floor(level)),blend=smoothstep(.15,.85,frac(level));
    float4 a=GrassFlowerStampLegacyLevel(world,seed,spacing,permission,retention,sizeScale);
    if(blend<.001)return a;
    float4 b=GrassFlowerStampLegacyLevel(world,seed,spacing*2,permission,retention,sizeScale);
    float alpha=lerp(a.a,b.a,blend);
    return float4(lerp(a.rgb*a.a,b.rgb*b.a,blend)/max(.0001,alpha),alpha);
}
// Point-sampled nearest-head metadata. The expensive world-site search is cached.
TEXTURE2D_ARRAY(_GrassFlowerField);
float4 _GrassFlowerFieldRects[5];
float _GrassFlowerFieldReady;
int _GrassFlowerStampCheck;
float4 GrassFlowerStampCachedLevel(float3 world,float seed,float permission,int level,float retention,float sizeScale)
{
    float4 rect=_GrassFlowerFieldRects[level];
    float2 cell=floor((world.xz-rect.xy)/rect.z);
    if(any(cell<0)||any(cell>=1024))
    {
        // A secondary camera or extreme horizon can lie outside the bounded cache.
        // Retain the deterministic field there instead of cutting off flower coverage.
        return GrassFlowerStampLegacyLevel(world,seed,exp2(level),permission,retention,sizeScale);
    }
    int2 tile=(int2)floor(world.xz/rect.z);
    int2 slot=(tile&1023);
    float4 data=LOAD_TEXTURE2D_ARRAY(_GrassFlowerField,slot,level);
    uint packed=(uint)round(data.b*255);
    if(packed==0||data.a>retention)return float4(0,0,0,0);
    uint code=packed-1;
    int species=(int)(code>>5);
    if((code&31u)/30.0>=permission)return float4(0,0,0,0);
    float2 center=rect.xy+(cell+.5+(data.rg-.5)*4)*rect.z;
    float4 positionCS=TransformWorldToHClip(world);
    if(positionCS.w<=.01)return 0;
    float4 centerCS=TransformWorldToHClip(float3(center.x,world.y,center.y));
    if(centerCS.w<=.01)return 0;
    float2 delta=(positionCS.xy/positionCS.w-centerCS.xy/centerCS.w)*_ScreenParams.xy*.5;
    float ppm=_ScreenParams.y*.5*abs(UNITY_MATRIX_P._m11)/(unity_OrthoParams.w>.5?1:max(.1,positionCS.w));
    float nominal=_GrassFlowers.z*GrassFlowerSize(species)*ppm;
    float radius=max(nominal,min(.7,nominal*5.5))*sizeScale;
    // Subpixel marks cannot resolve individual petals. A compact analytic silhouette
    // avoids atan2 and the eight-way palette/site work previously done per neighbour.
    float2 p=abs(delta)/max(.001,radius);
    int form=GrassFlowerForm(species);
    float d=form==3?dot(p,1):form==1?max(p.x,p.y):length(p);
    float aa=.35/max(.25,radius);
    float mask=1-smoothstep(.82-aa,.82+aa,d);
    float core=1-smoothstep(.15,.32,d);
    float3 color=lerp(GrassFlowerColor(species).rgb,GrassFlowerCenterColor(form),core*.7);
    return float4(color,mask*smoothstep(65,140,distance(world,_WorldSpaceCameraPos)));
}
float4 GrassFlowerStamp(float3 world,float seed,float permission,float retention,float sizeScale=1,float maximumDistance=0)
{
    float cameraDistance=distance(world,_WorldSpaceCameraPos);
    if(permission<=0||retention<=0||_GrassFlowers.x<=0||_GrassFlowerCount==0||cameraDistance<=65)return 0;
    // Reject distant marks before any cache lookup or procedural fallback work.
    float distanceFade=maximumDistance>0?1-smoothstep(maximumDistance*.9,maximumDistance,cameraDistance):1;
    if(distanceFade<=0)return 0;
    if(_GrassFlowerStampCheck==1||_GrassFlowerFieldReady<.5)
    {
        float4 value=GrassFlowerStampLegacy(world,seed,permission,_GrassFlowerStampCheck==1?1:retention,sizeScale);
        value.a*=distanceFade;return value;
    }
    float footprint=max(length(ddx(world.xz)),length(ddy(world.xz)));
    float level=clamp(log2(max(1,footprint)),0,4);
    int slice=(int)floor(level);
    float blend=smoothstep(.15,.85,frac(level));
    float4 a=GrassFlowerStampCachedLevel(world,seed,permission,slice,retention,sizeScale);
    if(blend<.001||slice==4){a.a*=distanceFade;return a;}
    float4 b=GrassFlowerStampCachedLevel(world,seed,permission,slice+1,retention,sizeScale);
    float alpha=lerp(a.a,b.a,blend);
    return float4(lerp(a.rgb*a.a,b.rgb*b.a,blend)/max(.0001,alpha),alpha*distanceFade);
}
float4 GrassFlowerStamp(float3 world,float seed,float permission)
{
    return GrassFlowerStamp(world,seed,permission,1);
}
#endif
