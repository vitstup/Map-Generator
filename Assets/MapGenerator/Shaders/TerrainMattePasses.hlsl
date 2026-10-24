#ifndef TACTICAL_TERRAIN_MATTE_INCLUDED
#define TACTICAL_TERRAIN_MATTE_INCLUDED

// Reuse Unity's terrain instancing, height normals, GI, shadow and depth plumbing.
// The stock PBR fragment is deliberately not used by any pass in our shaders.
#define _NORMALMAP 1
#define _TERRAIN_INSTANCED_PERPIXEL_NORMAL 1
#include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitInput.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitPasses.hlsl"

CBUFFER_START(TacticalTerrainSettings)
    float _StochasticStrength, _StochasticPatchTiles, _StochasticBlendSharpness, _StochasticSeed;
    float _AntiTileFadeStart, _AntiTileFadeEnd, _DetailNormalStrength;
    float4 _TacticalTerrainSize;
CBUFFER_END

#include "GrassCommon.hlsl"
#define GRASS_FLOWER_MATERIAL_PALETTE
#include "GrassFlowers.hlsl"
#include "GrassFlowerStamps.hlsl"
TEXTURE2D(_GrassTurfFlowers);
TEXTURE2D(_GrassCoverage); SAMPLER(sampler_GrassCoverage);
TEXTURE2D(_GrassTurf); SAMPLER(sampler_GrassTurf);
TEXTURE2D(_GrassTurfNormal); SAMPLER(sampler_GrassTurfNormal);
float4 _GrassRect, _GrassMeadow, _GrassDry, _GrassWind, _GrassOptions;
float _GrassEnabled, _GrassTurfStrength;
float _GrassBlueFlowerRetention;
float4 _GrassFlowerLodFocus,_GrassFlowerLodRanges,_GrassFlowerLodStyle;
float4 TerrainFlowerStamp(float3 world,float permission)
{
    float medium=1-GrassHandoffFade(world.xz,_GrassFlowerLodFocus.xz,_GrassFlowerLodRanges.z,_GrassFlowerLodRanges.w,_GrassWind.z);
    float distant=1-GrassHandoffFade(world.xz,_GrassFlowerLodFocus.xz,_GrassFlowerLodRanges.x,_GrassFlowerLodRanges.y,_GrassWind.z);
    float retention=lerp(lerp(1,_GrassFlowerLodStyle.x,medium),_GrassBlueFlowerRetention,distant);
    float sizeScale=lerp(1,_GrassFlowerLodStyle.y,distant);
    return GrassFlowerStamp(world,_GrassWind.z,permission,retention,sizeScale,_GrassFlowerLodStyle.z);
}

#ifndef TERRAIN_SPLAT_BASEPASS
float3 TerrainHash(float2 cell)
{
    float3 p = frac(float3(cell.xyx) * float3(0.1031,0.1030,0.0973) + _StochasticSeed * 0.0137);
    p += dot(p,p.yxz+33.33);
    return frac((p.xxy+p.yzz)*p.zyx);
}
float2 QuarterTurn(float2 p, uint rotation)
{
    if (rotation == 1) return float2(-p.y,p.x);
    if (rotation == 2) return -p;
    if (rotation == 3) return float2(p.y,-p.x);
    return p;
}
struct TerrainSample { half3 albedo; half3 normal; };

TerrainSample SampleTerrainLayer(TEXTURE2D_PARAM(albedoMap, albedoSampler), TEXTURE2D_PARAM(normalMap, normalSampler),
    float2 uv, float2 worldUV, half normalScale, float quality)
{
    TerrainSample result=(TerrainSample)0;
    float2 dx = ddx(uv), dy = ddy(uv);
    // At long range the same mip-filtered layer remains, with one sample per map.
    if (quality < 0.001)
    {
        result.albedo = SAMPLE_TEXTURE2D_GRAD(albedoMap,albedoSampler,uv,dx,dy).rgb;
        result.normal = UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(normalMap,normalSampler,uv,dx,dy),normalScale);
    }
    else
    {
    float patch = max(0.25,_StochasticPatchTiles);
    float2 p = worldUV/patch;
    float2 skew = float2(p.x-p.y*0.577350269,p.y*1.154700538);
    float2 cell = floor(skew), f = frac(skew);
    float2 a=cell,b=cell,c=cell; float3 weights=float3(1,0,0);
    if (f.x+f.y <= 1)
    { a=cell; b=cell+float2(1,0); c=cell+float2(0,1); weights=float3(1-f.x-f.y,f.x,f.y); }
    else
    { a=cell+1; b=cell+float2(0,1); c=cell+float2(1,0); weights=float3(f.x+f.y-1,1-f.x,1-f.y); }
    weights=pow(max(weights,0),max(1,_StochasticBlendSharpness));
    weights/=max(0.00001,dot(weights,1));
    result.albedo=0; result.normal=0;
    float2 vertices[3]={a,b,c};
    [unroll] for(uint i=0;i<3;i++)
    {
        float3 random=TerrainHash(vertices[i]);
        uint rotation=min(3,(uint)(random.z*4));
        float2 anchor=float2(vertices[i].x+vertices[i].y*0.5,vertices[i].y*0.866025404)*patch;
        float2 sampleUV=uv-worldUV+QuarterTurn(worldUV-anchor,rotation)+random.xy*31;
        float2 gx=QuarterTurn(dx,rotation), gy=QuarterTurn(dy,rotation);
        half3 color=SAMPLE_TEXTURE2D_GRAD(albedoMap,albedoSampler,sampleUV,gx,gy).rgb;
        half3 n=UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(normalMap,normalSampler,sampleUV,gx,gy),normalScale);
        n.xy=QuarterTurn(n.xy,(4-rotation)&3);
        result.albedo+=color*weights[i]; result.normal+=n*weights[i];
    }
    // Preserve texture variance instead of washing detail out where three patches overlap.
    half3 mean=SAMPLE_TEXTURE2D_LOD(albedoMap,albedoSampler,float2(0.5,0.5),16).rgb;
    float varianceCorrection=rsqrt(max(0.0001,dot(weights,weights)));
    result.albedo=saturate(mean+(result.albedo-mean)*varianceCorrection);
    result.normal.xy*=varianceCorrection;
    if (quality < 0.999)
    {
        half3 plainColor=SAMPLE_TEXTURE2D_GRAD(albedoMap,albedoSampler,uv,dx,dy).rgb;
        half3 plainNormal=UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(normalMap,normalSampler,uv,dx,dy),normalScale);
        result.albedo=lerp(plainColor,result.albedo,quality);
        result.normal=lerp(plainNormal,result.normal,quality);
    }
    result.normal=normalize(result.normal+half3(0,0,0.0001));
    }
    return result;
}

void MatteLayers(Varyings input, out half3 albedo, out half3 normal, out half coverage)
{
    float2 controlUV=(input.uvMainAndLM.xy*(_Control_TexelSize.zw-1)+0.5)*_Control_TexelSize.xy;
    half4 weights=SAMPLE_TEXTURE2D(_Control,sampler_Control,controlUV);
    coverage=dot(weights,1);
#ifdef TERRAIN_SPLAT_ADDPASS
    clip(coverage-0.005h);
#endif
    weights/=max(coverage,0.0001h);
    float distanceToCamera=distance(input.positionWS,_WorldSpaceCameraPos);
    float quality=saturate(_StochasticStrength)*(1-smoothstep(_AntiTileFadeStart,max(_AntiTileFadeStart+1,_AntiTileFadeEnd),distanceToCamera));
    albedo=0; normal=0;
    // Terrain supplies UV scale per layer. The world lattice is shared by every nested level.
    float2 world=input.positionWS.xz/max(float2(1,1),_TacticalTerrainSize.xy);
    TerrainSample s=(TerrainSample)0;
    if(weights.r>0.0001h) { s=SampleTerrainLayer(TEXTURE2D_ARGS(_Splat0,sampler_Splat0),TEXTURE2D_ARGS(_Normal0,sampler_Normal0),input.uvSplat01.xy,world*_Splat0_ST.xy,_NormalScale0,quality); albedo+=s.albedo*_DiffuseRemapScale0.rgb*weights.r; normal+=s.normal*weights.r; }
    if(weights.g>0.0001h) { s=SampleTerrainLayer(TEXTURE2D_ARGS(_Splat1,sampler_Splat1),TEXTURE2D_ARGS(_Normal1,sampler_Normal0),input.uvSplat01.zw,world*_Splat1_ST.xy,_NormalScale1,quality); albedo+=s.albedo*_DiffuseRemapScale1.rgb*weights.g; normal+=s.normal*weights.g; }
    if(weights.b>0.0001h) { s=SampleTerrainLayer(TEXTURE2D_ARGS(_Splat2,sampler_Splat2),TEXTURE2D_ARGS(_Normal2,sampler_Normal0),input.uvSplat23.xy,world*_Splat2_ST.xy,_NormalScale2,quality); albedo+=s.albedo*_DiffuseRemapScale2.rgb*weights.b; normal+=s.normal*weights.b; }
    if(weights.a>0.0001h) { s=SampleTerrainLayer(TEXTURE2D_ARGS(_Splat3,sampler_Splat3),TEXTURE2D_ARGS(_Normal3,sampler_Normal0),input.uvSplat23.zw,world*_Splat3_ST.xy,_NormalScale3,quality); albedo+=s.albedo*_DiffuseRemapScale3.rgb*weights.a; normal+=s.normal*weights.a; }
    normal=normalize(half3(normal.xy*_DetailNormalStrength,max(0.0001h,normal.z)));
}
#endif

void MatteSurface(Varyings input, out half3 albedo, out half3 normal, out half coverage)
{
#ifdef _ALPHATEST_ON
    ClipHoles(input.uvMainAndLM.xy);
#endif
#ifdef TERRAIN_SPLAT_BASEPASS
    albedo=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uvMainAndLM.xy).rgb;
    normal=half3(0,0,1); coverage=1;
#else
    MatteLayers(input,albedo,normal,coverage);
#endif
}
half4 MatteFragment(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    half3 albedo,normal; half coverage;
    MatteSurface(input,albedo,normal,coverage);
    if (_GrassEnabled > .5)
    {
        float2 grassUV = (input.positionWS.xz - _GrassRect.xy) / max(_GrassRect.zw, 1);
        half4 grass = SAMPLE_TEXTURE2D(_GrassCoverage,sampler_GrassCoverage,grassUV);
        float patch = GrassPatch(input.positionWS.xz,_GrassWind.z);
        float slopeNormal=abs(normalize(cross(ddy(input.positionWS),ddx(input.positionWS))).y);
        float suitable = smoothstep(_GrassOptions.z,_GrassOptions.z+1.2,input.positionWS.y)
                       * smoothstep(_GrassOptions.w,min(1,_GrassOptions.w+.08),slopeNormal);
        half amount=saturate(grass.r*1.6)*grass.a*GrassPermission(grass.b)*patch*_GrassWind.w*suitable*_GrassTurfStrength;
        half3 tint=GrassPalette(input.positionWS.xz,grass.g,_GrassWind.z,_GrassMeadow.rgb,_GrassDry.rgb);
        float2 turfUV=input.positionWS.xz/4;
        float2 otherUV=float2(-turfUV.y,turfUV.x)*.73+.371;
        half4 turfA=SAMPLE_TEXTURE2D(_GrassTurf,sampler_GrassTurf,turfUV);
        half4 turfB=SAMPLE_TEXTURE2D(_GrassTurf,sampler_GrassTurf,otherUV);
        half4 turf=(turfA+turfB)*.5;
        half3 nA=SAMPLE_TEXTURE2D(_GrassTurfNormal,sampler_GrassTurfNormal,turfUV).xyz*2-1;
        half3 nB=SAMPLE_TEXTURE2D(_GrassTurfNormal,sampler_GrassTurfNormal,otherUV).xyz*2-1;
        nB.xy=float2(nB.y,-nB.x);
        half3 canopyNormal=normalize(half3((nA.xy+nB.xy)*.5,max(.15,(nA.z+nB.z)*.5)));
        float normalDetail=lerp(.25,.85,1-smoothstep(120,900,distance(input.positionWS,_WorldSpaceCameraPos)));
        canopyNormal.xy*=normalDetail;
        normal=normalize(lerp(normal,canopyNormal,amount*.85));
        float gust=GrassGust(input.positionWS.xz,_Time.y,_GrassWind.y);
        // The texture is a neutral albedo/coverage bake of the same plants, not a lighting screenshot.
        half3 canopy=tint*max(.45,turf.rgb);
        if(_GrassFlowers.x>0)
        {
            float4 flower=TerrainFlowerStamp(input.positionWS,amount);
            canopy=lerp(canopy,flower.rgb,flower.a);
        }
        albedo=lerp(albedo,canopy,amount*saturate(turf.a*2+.25));
        normal.xy+=float2(.85,.4)*gust*_GrassWind.x*amount*.18;
        normal=normalize(normal);
        if (_GrassOptions.y > .5 && _GrassOptions.y < 1.5) albedo=lerp(half3(.4,.06,.03),half3(.1,.7,.1),amount);
        if (_GrassOptions.y > 1.5 && _GrassOptions.y < 2.5) albedo=lerp(albedo,half3(.1,.25,.65),amount);
        if (_GrassOptions.y > 1.5 && _GrassOptions.y < 2.5 && _GrassFlowers.x>0)
        {
            float4 head=TerrainFlowerStamp(input.positionWS,amount);
            albedo=lerp(albedo,head.rgb,amount*head.a);
        }
        if (_GrassOptions.y > 2.5) albedo=lerp(albedo,half3(GrassHash(floor(input.positionWS.xz/_GrassOptions.x)),.3,.5),amount);
    }
    InputData lighting; InitializeInputData(input,normal,lighting); InitializeBakedGIData(input,lighting);
    SurfaceData surface=(SurfaceData)0;
    surface.albedo=albedo; surface.normalTS=normal; surface.occlusion=1; surface.alpha=coverage;
    // BlinnPhong with zero specular is diffuse-only: no Fresnel or environment reflection term.
    half4 color=UniversalFragmentBlinnPhong(lighting,surface);
    SplatmapFinalColor(color,lighting.fogCoord);
    return half4(color.rgb,1);
}
half4 MatteDepthNormals(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    half3 albedo,normal; half coverage; MatteSurface(input,albedo,normal,coverage);
    InputData lighting; InitializeInputData(input,normal,lighting);
    return half4(lighting.normalWS,0);
}
#endif
