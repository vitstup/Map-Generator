Shader "TacticalMaps/Grass/DistantCards"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off ZWrite On
        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
        #include "UnityIndirect.cginc"
        #include "GrassCommon.hlsl"
        #include "GrassFlowers.hlsl"
#include "GrassFlowerStamps.hlsl"
        struct GrassInstance { float4 positionHeight; float4 variation; };
        StructuredBuffer<GrassInstance> _GrassInstances;
        StructuredBuffer<uint> _VisibleGrass;
        StructuredBuffer<float> _GrassBirth;
        uint _GrassPerSlot;
        float _GrassClock;
        TEXTURE2D(_GrassCardsAlbedo); SAMPLER(sampler_GrassCardsAlbedo);
        TEXTURE2D(_GrassCardsNormal);
        TEXTURE2D(_GrassCardsFlowers);
        TEXTURE2D(_GroundHeight0); TEXTURE2D(_GroundHeight1); TEXTURE2D(_GroundHeight2);
        SAMPLER(sampler_GroundHeight0);
        TEXTURE2D(_GroundCoverage0); TEXTURE2D(_GroundCoverage1); TEXTURE2D(_GroundCoverage2);
        SAMPLER(sampler_GroundCoverage0);
        TEXTURE2D(_GroundHoles0); TEXTURE2D(_GroundHoles1); TEXTURE2D(_GroundHoles2);
        SAMPLER(sampler_GroundHoles0);
        float4 _GroundRect0,_GroundRect1,_GroundRect2,_GroundVertical0,_GroundVertical1,_GroundVertical2;
        float4 _GrassMeadow,_GrassDry,_GrassWind,_GrassOptions,_GrassLodDistances,_GrassProjection;
        float4 _GrassAmbientSky,_GrassAmbientSide,_GrassFocus,_GrassMeadowRange;
        int _GrassTransitionCheck;
        float4 _GrassFieldRanges;
        struct Attributes { float3 positionOS:POSITION; float2 uv:TEXCOORD0; float2 meta:TEXCOORD1; uint instanceID:SV_InstanceID; };
        struct Varyings
        {
            float4 positionCS:SV_POSITION;
            float3 world:TEXCOORD0;
            float2 uv:TEXCOORD1;
            half3 color:TEXCOORD2;
            float2 rotation:TEXCOORD3;
            half fade:TEXCOORD4;
            nointerpolation float2 noiseSeed:TEXCOORD5;
            nointerpolation float edge:TEXCOORD6;
            nointerpolation float top:TEXCOORD7;
            nointerpolation float flowerPermission:TEXCOORD8;
            float canopyHeight:TEXCOORD9;
        };
        float4 _GrassFlowerLodStyle;
        int GroundLevel(float2 world)
        {
            return all(world>=_GroundRect0.xy)&&all(world<_GroundRect0.xy+_GroundRect0.zw)?0:
                   all(world>=_GroundRect1.xy)&&all(world<_GroundRect1.xy+_GroundRect1.zw)?1:2;
        }
        float GroundHeight(float2 world)
        {
            int level=GroundLevel(world);
            float4 rect=level==0?_GroundRect0:level==1?_GroundRect1:_GroundRect2;
            float4 vertical=level==0?_GroundVertical0:level==1?_GroundVertical1:_GroundVertical2;
            float2 uv=((world-rect.xy)/rect.zw*(vertical.z-1)+.5)/vertical.z;
            float h=level==0?SAMPLE_TEXTURE2D_LOD(_GroundHeight0,sampler_GroundHeight0,uv,0).r:
                    level==1?SAMPLE_TEXTURE2D_LOD(_GroundHeight1,sampler_GroundHeight0,uv,0).r:SAMPLE_TEXTURE2D_LOD(_GroundHeight2,sampler_GroundHeight0,uv,0).r;
            return vertical.x+2*h*vertical.y;
        }
        float Permission(float2 world)
        {
            int level=GroundLevel(world);
            float4 rect=level==0?_GroundRect0:level==1?_GroundRect1:_GroundRect2;
            float2 uv=(world-rect.xy)/rect.zw;
            float4 cover=level==0?SAMPLE_TEXTURE2D_LOD(_GroundCoverage0,sampler_GroundCoverage0,uv,0):
                level==1?SAMPLE_TEXTURE2D_LOD(_GroundCoverage1,sampler_GroundCoverage0,uv,0):SAMPLE_TEXTURE2D_LOD(_GroundCoverage2,sampler_GroundCoverage0,uv,0);
            float hole=level==0?SAMPLE_TEXTURE2D_LOD(_GroundHoles0,sampler_GroundHoles0,uv,0).r:
                level==1?SAMPLE_TEXTURE2D_LOD(_GroundHoles1,sampler_GroundHoles0,uv,0).r:SAMPLE_TEXTURE2D_LOD(_GroundHoles2,sampler_GroundHoles0,uv,0).r;
            return GrassPermission(cover.b)*cover.a*step(.5,hole)*step(0,min(min(uv.x,uv.y),min(1-uv.x,1-uv.y)));
        }
        Varyings Vert(Attributes input)
        {
            InitIndirectDrawArgs(0);
            uint id=_VisibleGrass[GetIndirectInstanceID(input.instanceID)];
            GrassInstance instance=_GrassInstances[id];
            float2 anchor=instance.positionHeight.xz;
            float card=input.meta.x,top=input.meta.y;
            float angle=instance.variation.x+card*1.047198;
            float2 right=float2(cos(angle),sin(angle)),forward=float2(-right.y,right.x);
            float scale=instance.positionHeight.w;
            float2 offset;
            float elevation;
            if(top<.5)
            {
                offset=right*(input.uv.x-.5)*3.2*scale+forward*(card-1)*.27;
                elevation=input.uv.y*1.1*scale;
            }
            else
            {
                offset=(right*(input.uv.x-.5)+forward*(input.uv.y-.5))*2.7*scale;
                offset+=right*(card-3.5)*.6;
                elevation=(.19+(input.uv.y-.5)*.22+(card-3)*.07)*scale;
            }
            float gust=GrassGust(anchor,_Time.y,_GrassWind.y);
            offset+=float2(.85,.4)*gust*_GrassWind.x*.12*(top>.5?.3:input.uv.y);
            Varyings o;
            o.world=float3(anchor.x+offset.x,GroundHeight(anchor+offset)+elevation-.025,anchor.y+offset.y);
            o.positionCS=TransformWorldToHClip(o.world);
            // Four independent baked groups. Padding keeps lower mip levels inside each tile.
            float variant=floor(GrassHash(anchor+card*31.9)*3.999);
            o.uv=(float2(variant,top)+lerp(.025,.975,input.uv))/float2(4,2);
            o.rotation=right;
            o.color=GrassPalette(anchor,instance.variation.y,_GrassWind.z,_GrassMeadow.rgb,_GrassDry.rgb);
            if(_GrassOptions.y>1.5&&_GrassOptions.y<2.5) o.color=half3(.6,.09,.75);
            if(_GrassOptions.y>2.5) o.color=half3(GrassHash(floor(anchor/_GrassOptions.x)),.3,.5);
            float arrival=1-GrassFieldFade(anchor,_GrassFocus.xz,_GrassMeadowRange.x,_GrassMeadowRange.y,_GrassWind.z);
            float range=GrassFieldFade(anchor,_GrassFocus.xz,_GrassLodDistances.x*.66,_GrassLodDistances.x,_GrassWind.z);
            float cameraDistance=max(.1,distance(instance.positionHeight.xyz,_WorldSpaceCameraPos));
            float pixels=2.8*_GrassProjection.x/(_GrassProjection.y>.5?1:cameraDistance);
            float delay=GrassHash(anchor+317.3)*.18;
            o.fade=arrival*range*GrassScreenFade(pixels,_GrassLodDistances.z)*smoothstep(delay,delay+.55,_GrassClock-_GrassBirth[id/_GrassPerSlot]);
            if((_GrassTransitionCheck&1)==0) o.fade=GrassScreenFade(pixels,_GrassLodDistances.z)*smoothstep(delay,delay+.55,_GrassClock-_GrassBirth[id/_GrassPerSlot]);
            o.noiseSeed=floor(float2(GrassHash(anchor+card*23.73),GrassHash(anchor+card*37.13))*8192);
            o.edge=instance.variation.w<0?1:0;
            o.top=top;
            o.canopyHeight=o.world.y-elevation+.375;
            o.flowerPermission=abs(instance.variation.w);
            return o;
        }
        half4 SampleAndClip(Varyings i,out float4 flower)
        {
            if((_GrassTransitionCheck&1)==0)
                clip(GrassLayerMask(i.world.xz,i.positionCS.xy,_GrassFocus.xz,_GrassFieldRanges,2,_GrassWind.z,1));
            half4 tex=SAMPLE_TEXTURE2D(_GrassCardsAlbedo,sampler_GrassCardsAlbedo,i.uv);

            float fade=i.fade;
            [branch] if(i.edge>.5) fade*=Permission(i.world.xz);
            clip(fade-GrassHash(floor(i.positionCS.xy)+i.noiseSeed));
            flower=0;
            if(_GrassFlowers.x>0&&(_GrassOptions.y<.5||(_GrassOptions.y>1.5&&_GrassOptions.y<2.5)))
            {
                if(_GrassFlowerStampCheck==1)
                {
                    if(i.top>.5)flower=GrassFlowerStamp(i.world,_GrassWind.z,i.flowerPermission);
                }
                else
                {
                    // Evaluate the same world field on a canopy plane along this pixel's ray.
                    // Upright cards can then carry a compact head instead of hiding it or
                    // stretching an XZ dot into a vertical stripe.
                    float3 ray=i.world-_WorldSpaceCameraPos;
                    if(unity_OrthoParams.w>.5)ray=-UNITY_MATRIX_V[2].xyz;
                    if(abs(ray.y)>.04)
                    {
                        float3 canopy=i.world+ray*((i.canopyHeight-i.world.y)/ray.y);
                        flower=GrassFlowerStamp(canopy,_GrassWind.z,i.flowerPermission,_GrassFlowerLodStyle.x);
                    }
                }
            }
            clip(max(tex.a-.35,(_GrassFlowerStampCheck==1?-1:flower.a-.08)));
            return tex;
        }
        half3 CardNormal(Varyings i)
        {
            // The source bake stores world normals in XZY order, without normal-map import conversion.
            half3 n=SAMPLE_TEXTURE2D(_GrassCardsNormal,sampler_GrassCardsAlbedo,i.uv).rbg*2-1;
            // At this distance a texel represents many leaves; soften individual blade normals.
            n=normalize(half3(n.x*.55,max(n.y,.45),n.z*.55));
            return normalize(half3(i.rotation.x*n.x-i.rotation.y*n.z,n.y,i.rotation.y*n.x+i.rotation.x*n.z));
        }
        half4 Frag(Varyings i):SV_Target
        {
            float4 flower;
            half4 tex=SampleAndClip(i,flower);
            half3 tint=i.color;
            if(_GrassFlowerStampCheck==1)tint=lerp(tint,flower.rgb,flower.a);
            half3 normal=CardNormal(i);
            #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
            Light light=GetMainLight(ComputeScreenPos(TransformWorldToHClip(i.world)));
            #else
            Light light=GetMainLight(TransformWorldToShadowCoord(i.world));
            #endif
            half diffuse=saturate((dot(normal,light.direction)+.25)/1.25);
            half3 ambient=lerp(_GrassAmbientSide.rgb,_GrassAmbientSky.rgb,saturate(normal.y));
            half3 color=tint*tex.rgb*(ambient+light.color*diffuse*light.shadowAttenuation);
            if((_GrassTransitionCheck&2)==0)
                color=lerp(color,tint*lerp(.93,tex.rgb,.45)*GrassCanopyLighting(_GrassAmbientSky.rgb,light.color,light.direction,light.shadowAttenuation),.85);
            if(_GrassFlowerStampCheck!=1)
                color=lerp(color,flower.rgb*GrassCanopyLighting(_GrassAmbientSky.rgb,light.color,light.direction,light.shadowAttenuation),flower.a);
            return half4(MixFog(color,ComputeFogFactor(TransformWorldToHClip(i.world).z)),1);
        }
        half4 Depth(Varyings i):SV_Target { float4 flower; SampleAndClip(i,flower); return 0; }
        half4 Normals(Varyings i):SV_Target { float4 flower; SampleAndClip(i,flower); return half4(CardNormal(i),0); }
        ENDHLSL
        Pass
        {
            Name "GrassCardsForward" Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly" Tags { "LightMode"="DepthOnly" } ColorMask 0
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Depth
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals" Tags { "LightMode"="DepthNormals" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Normals
            ENDHLSL
        }
    }
}
