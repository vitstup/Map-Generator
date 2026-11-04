#ifndef TACTICAL_GRASS_FLOWERS
#define TACTICAL_GRASS_FLOWERS
#include "GrassCommon.hlsl"
float4 _GrassFlowers;
float4 _GrassFlowerColors[8],_GrassFlowerShapes[8];
int _GrassFlowerCount;
#if defined(GRASS_FLOWER_MATERIAL_PALETTE)
float4 _GrassFlowerColor0,_GrassFlowerColor1,_GrassFlowerColor2,_GrassFlowerColor3;
float4 _GrassFlowerColor4,_GrassFlowerColor5,_GrassFlowerColor6,_GrassFlowerColor7;
float4 _GrassFlowerWeights0,_GrassFlowerWeights1;
float4 _GrassFlowerForms0,_GrassFlowerForms1;
#endif
float4 GrassFlowerColor(int i)
{
#if defined(GRASS_FLOWER_MATERIAL_PALETTE)
    return i==0?_GrassFlowerColor0:i==1?_GrassFlowerColor1:i==2?_GrassFlowerColor2:i==3?_GrassFlowerColor3:
        i==4?_GrassFlowerColor4:i==5?_GrassFlowerColor5:i==6?_GrassFlowerColor6:_GrassFlowerColor7;
#else
    return _GrassFlowerColors[i];
#endif
}
int GrassFlowerForm(int i)
{
#if defined(GRASS_FLOWER_MATERIAL_PALETTE)
    float4 forms=i<4?_GrassFlowerForms0:_GrassFlowerForms1;
    return (int)forms[i&3];
#else
    return (int)_GrassFlowerShapes[i].y;
#endif
}
int GrassFlowerPetals(int form) {return form==0?8:form==3?6:5;}
float3 GrassFlowerCenterColor(int form)
{
    return form==0?float3(.72,.45,.08):form==1?float3(.28,.16,.035):form==2?float3(.72,.7,.54):float3(.7,.46,.1);
}
float GrassFlowerSize(int i)
{
#if defined(GRASS_FLOWER_MATERIAL_PALETTE)
    return GrassFlowerColor(i).a;
#else
    return _GrassFlowerShapes[i].x;
#endif
}
float GrassFlowerAbundance(float2 world,float seed)
{
    if(_GrassFlowers.w>.5)return .12;
    // At the default 12 m setting, restore the original 2.3 m flower field and
    // its 58% gate. The original near-plant chance inside this field was 3.5%.
    float patch=GrassNoise(world/(max(4,_GrassFlowers.y)*(2.3/12))+seed*.01);
    return patch>.58?_GrassFlowers.x:0;
}
int GrassFlowerSpecies(float2 world,float seed)
{
    // Coherent color regions use the same seed and coordinates in all representations.
    float pick=GrassHash(floor(world/max(4,_GrassFlowers.y))+seed+218.3);
    int selected=0;
    [unroll] for(int i=0;i<8;i++)
    {
#if defined(GRASS_FLOWER_MATERIAL_PALETTE)
        float4 weights=i<4?_GrassFlowerWeights0:_GrassFlowerWeights1;
        float threshold=weights[i&3];
#else
        float threshold=_GrassFlowerColors[i].a;
#endif
        if(i<_GrassFlowerCount&&pick>threshold) selected=min(i+1,max(0,_GrassFlowerCount-1));
    }
    return selected;
}
float2 GrassFlowerRoot(float2 anchor,int head,float seed)
{
    return float2(GrassHash(anchor+head*31.7+seed)-.5,GrassHash(anchor.yx+head*19.3+seed+17)-.5)*1.7;
}
float2 GrassFlowerSitePosition(float2 cell,float seed)
{
    return cell+.18+float2(GrassHash(cell+seed+47.2),GrassHash(cell.yx+seed+81.3))*.64;
}
bool GrassFlowerSite(float2 cell,float seed,out float2 center)
{
    center=cell+.5;
    if(_GrassFlowerCount==0||_GrassFlowers.x<=0)return false;
    float maximum=saturate(_GrassFlowers.x*14);
    float rank=GrassHash(cell+seed+138.7);
    if(rank<=1-maximum)return false;
    center=GrassFlowerSitePosition(cell,seed);
    // Fourteen candidates/m2 is a conservative replacement for the old sixteen
    // grass candidates: this shared head field does not increase flower density.
    return rank>1-saturate(GrassFlowerAbundance(center,seed)*14);
}
// The reference bakes contain 12% flower-bearing plants. Color and abundance are
// applied at runtime; the neutral atlas never locks a palette into the meadow.
float GrassFlowerMaskWeight(float mask,float2 world,float seed)
{
    float size=GrassFlowerSize(GrassFlowerSpecies(world,seed))*(_GrassFlowers.z/.095);
    return saturate(mask*GrassFlowerAbundance(world,seed)/.12*2.5*size*size);
}
#endif
