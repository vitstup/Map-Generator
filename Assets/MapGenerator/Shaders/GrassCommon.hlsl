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
#endif
