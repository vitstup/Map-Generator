#ifndef TACTICAL_GRASS_COMMON
#define TACTICAL_GRASS_COMMON
float GrassHash(float2 p)
{
    float3 q = frac(float3(p.xyx) * .1031);
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}
float GrassNoise(float2 p)
{
    float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
    return lerp(lerp(GrassHash(i), GrassHash(i + float2(1,0)), f.x),
                lerp(GrassHash(i + float2(0,1)), GrassHash(i + 1), f.x), f.y);
}
float GrassPatch(float2 world, float seed)
{
    // Dense meadow is the default; occasional clearings are controlled separately.
    return lerp(.86, 1, GrassNoise(world / 11 + seed * .013));
}
float GrassGust(float2 world, float time, float speed)
{
    float2 drift=world*.045-float2(.8,.35)*time*speed*.16;
    return (GrassNoise(drift)*2-1)*.75 + sin(dot(world,float2(.14,.095))-time*speed)*.25;
}
float3 GrassPalette(float2 world, float dryness, float seed, float3 meadow, float3 dry)
{
    float regional=GrassNoise(world/19+seed*.013);
    float local=GrassNoise(world/2.7+seed*.021);
    float3 fresh=meadow*float3(.8,1.02,1.1);
    float3 warm=meadow*float3(1.17,.98,.73);
    return lerp(lerp(fresh,warm,smoothstep(.25,.8,regional)),dry,dryness*.75)
           *lerp(.82,1.13,local);
}
// The same beach exclusion and projected-size transitions are used in every representation.
float GrassPermission(float sand) { return 1-smoothstep(.03,.18,sand); }
float GrassScreenFade(float pixels, float minimum) { return smoothstep(minimum, max(minimum*3,2), pixels); }
float GrassLodBias(float2 anchor,float seed)
{
    return lerp(.82,1.18,GrassHash(anchor+seed+513.7))*lerp(.94,1.06,GrassNoise(anchor/13+seed));
}
float GrassDetailWeight(float pixels) { return smoothstep(4,7,pixels*1.5); }
float GrassFieldFade(float2 anchor,float2 focus,float start,float end,float seed)
{
    float range=end*lerp(.83,1,GrassHash(anchor+seed+271.3));
    range*=lerp(.96,1,GrassNoise(anchor/17+seed));
    return 1-smoothstep(start/end*range,range,distance(anchor,focus));
}
float GrassRangeFade(float distanceXZ, float start, float end) { return 1-smoothstep(start,end,distanceXZ); }

// Smooth world-anchored variation is shared by both sides of every handoff.
// Independent per-instance fade ranges cannot form complementary coverage.
float GrassHandoffFade(float2 world,float2 focus,float start,float end,float seed)
{
    float bias=lerp(.82,1,GrassNoise(world/19+seed*.031));
    return 1-smoothstep(start*bias,end*bias,distance(world,focus));
}
float GrassLayerMask(float2 world,float2 pixel,float2 focus,float4 ranges,int pool,float seed,float canopy)
{
    float d=distance(world,focus);
    // Entirely owned regions avoid noise work in the large close-up pixel footprint.
    if(d<ranges.x*.82)
    {
        if(pool==0) return 1;
        if(pool==2) return -1;
        // Preserve the original understorey when individual nearby blades are resolved.
        return 1-canopy-GrassHash(floor(pixel)+float2(seed*.17,seed*.37));
    }
    if(pool==2&&d<ranges.z*.72*.82) return -1;
    if(pool==1&&d>ranges.y&&d<ranges.z*.72*.82) return 1;
    float rank=GrassHash(floor(pixel)+float2(seed*.17,seed*.37));
    float near=GrassHandoffFade(world,focus,ranges.x,ranges.y,seed);
    if(pool==0) return near-rank;
    float meadow=GrassHandoffFade(world,focus,ranges.z*.72,ranges.z,seed);
    if(pool==1) return min(rank-near*canopy,meadow-rank);
    float far=GrassHandoffFade(world,focus,ranges.w*.66,ranges.w,seed);
    return min(rank-meadow,far-rank);
}
float GrassCanopyWeight(float pixels) { return 1-smoothstep(2,7,pixels); }
half3 GrassCanopyLighting(half3 sky,half3 lightColor,half3 lightDirection,half shadow)
{
    // Distant texels represent many differently oriented blades. Their average
    // lighting converges to the same upward, diffuse canopy used by Terrain.
    return sky+lightColor*saturate(lightDirection.y)*shadow;
}
#endif
