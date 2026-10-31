#ifndef TACTICAL_FARM_CROP_COMMON
#define TACTICAL_FARM_CROP_COMMON
// Shared by terrain, plants and GPU culling. Distance from the parcel edge
// avoids a moving boundary across a field; altitude also handles overhead zoom.
float FarmParcelDistance(float3 camera, float groundHeight, float4 shape, float2 axis)
{
    float2 d=camera.xz-shape.xy;
    float2 outside=max(abs(float2(dot(d,axis),dot(d,float2(-axis.y,axis.x))))-shape.zw,0);
    return length(float3(outside,camera.y-groundHeight));
}
float FarmCanopyBlend(float distanceToParcel, float endDistance)
{
    return smoothstep(endDistance*.55,endDistance,distanceToParcel);
}
#ifndef FARM_CROP_CULLING
half3 FarmCropColor(float crop,float variation)
{
    half3 green=lerp(SRGBToLinear(half3(.25,.38,.115)),SRGBToLinear(half3(.43,.54,.21)),variation);
    half3 gold=lerp(SRGBToLinear(half3(.49,.43,.17)),SRGBToLinear(half3(.66,.57,.28)),variation);
    return crop>1.5 && crop<2.5?lerp(gold,SRGBToLinear(half3(.69,.59,.29)),.22):green;
}
#endif
#endif
