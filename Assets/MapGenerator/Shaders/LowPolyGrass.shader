Shader "TacticalMaps/Grass/LowPolyIndirect"
{
    Properties { _GrassMeadow("Meadow",Color)=(.38,.47,.17,1) _GrassDry("Dry",Color)=(.49,.46,.23,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off ZWrite On
        HLSLINCLUDE
        #pragma target 4.5
        #pragma multi_compile_local _ GRASS_PREPARED
        #pragma multi_compile_local _ GRASS_SPARSE
        #pragma multi_compile_local _ GRASS_HEADS
        #pragma multi_compile_local _ GRASS_UNDERSTOREY
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
        #include "UnityIndirect.cginc"
        #include "GrassCommon.hlsl"
        #include "GrassFlowers.hlsl"
        struct GrassInstance { float4 positionHeight; float4 variation; };
        StructuredBuffer<GrassInstance> _GrassInstances;
        struct GrassPrepared { float4 paletteFamily; float4 motion; };
        StructuredBuffer<uint4> _GrassPrepared;
        GrassPrepared DecodePrepared(uint4 data)
        {
            GrassPrepared value;
            value.paletteFamily=float4(f16tof32(data.x&65535u),f16tof32(data.x>>16),f16tof32(data.y&65535u),data.w>>16);
            value.motion=float4(f16tof32(data.y>>16),asfloat(data.z),f16tof32(data.w&65535u),0);
            return value;
        }
        StructuredBuffer<uint> _VisibleGrass;
        StructuredBuffer<float> _GrassBirth;
        uint _GrassPerSlot;
        float _GrassClock;
        float _GrassFrameTime;
        TEXTURE2D(_GroundHeight0); TEXTURE2D(_GroundHeight1); TEXTURE2D(_GroundHeight2);
        SAMPLER(sampler_GroundHeight0);
        TEXTURE2D(_GroundCoverage0); TEXTURE2D(_GroundCoverage1); TEXTURE2D(_GroundCoverage2);
        SAMPLER(sampler_GroundCoverage0);
        float4 _GroundRect0,_GroundRect1,_GroundRect2,_GroundVertical0,_GroundVertical1,_GroundVertical2;
        float4 _GrassMeadow,_GrassDry,_GrassWind,_GrassOptions,_GrassLodDistances,_GrassProjection;
        float4 _GrassAmbientSky,_GrassAmbientSide,_GrassFocus,_GrassNearRange;
        int _GrassLod,_GrassPool,_GrassTopologyCheck;
#if defined(GRASS_PREPARED)
    #if defined(GRASS_UNDERSTOREY)
        #define GRASS_POOL 1
    #else
        #define GRASS_POOL 0
    #endif
#else
        #define GRASS_POOL _GrassPool
#endif
        int _GrassTransitionCheck;
        float4 _GrassFieldRanges;
        float _GrassBakeMode;
        float4 _GrassBakeRegion;
        struct Attributes { float3 positionOS:POSITION; float2 uv:TEXCOORD0; float2 meta:TEXCOORD1; float2 coefficients:TEXCOORD2; uint instanceID:SV_InstanceID; };
        struct Varyings { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; half3 normal:TEXCOORD1; half3 color:TEXCOORD2; half fog:TEXCOORD3; float4 shadowCoord:TEXCOORD4; half fade:TEXCOORD5; half detail:TEXCOORD6; nointerpolation float2 noiseSeed:TEXCOORD7; half canopy:TEXCOORD8; nointerpolation half petal:TEXCOORD9; };
        float GroundHeight(float2 world)
        {
            int level=all(world>=_GroundRect0.xy)&&all(world<_GroundRect0.xy+_GroundRect0.zw)?0:
                      all(world>=_GroundRect1.xy)&&all(world<_GroundRect1.xy+_GroundRect1.zw)?1:2;
            float4 rect=level==0?_GroundRect0:level==1?_GroundRect1:_GroundRect2;
            float4 vertical=level==0?_GroundVertical0:level==1?_GroundVertical1:_GroundVertical2;
            float2 uv=((world-rect.xy)/rect.zw*(vertical.z-1)+.5)/vertical.z;
            float h=level==0?SAMPLE_TEXTURE2D_LOD(_GroundHeight0,sampler_GroundHeight0,uv,0).r:
                    level==1?SAMPLE_TEXTURE2D_LOD(_GroundHeight1,sampler_GroundHeight0,uv,0).r:SAMPLE_TEXTURE2D_LOD(_GroundHeight2,sampler_GroundHeight0,uv,0).r;
            return vertical.x+2*h*vertical.y;
        }
        float GroundPermission(float2 world)
        {
            int level=all(world>=_GroundRect0.xy)&&all(world<_GroundRect0.xy+_GroundRect0.zw)?0:
                      all(world>=_GroundRect1.xy)&&all(world<_GroundRect1.xy+_GroundRect1.zw)?1:2;
            float4 rect=level==0?_GroundRect0:level==1?_GroundRect1:_GroundRect2;
            float2 uv=(world-rect.xy)/rect.zw;
            float4 cover=level==0?SAMPLE_TEXTURE2D_LOD(_GroundCoverage0,sampler_GroundCoverage0,uv,0):
                         level==1?SAMPLE_TEXTURE2D_LOD(_GroundCoverage1,sampler_GroundCoverage0,uv,0):SAMPLE_TEXTURE2D_LOD(_GroundCoverage2,sampler_GroundCoverage0,uv,0);
            return GrassPermission(cover.b)*cover.a;
        }
        Varyings Vert(Attributes input)
        {
            InitIndirectDrawArgs(0);
            uint id=_VisibleGrass[GetIndirectInstanceID(input.instanceID)];
            GrassInstance instance=_GrassInstances[id];
#if defined(GRASS_PREPARED)
            GrassPrepared prepared=DecodePrepared(_GrassPrepared[id]);
#endif
            float2 anchor=instance.positionHeight.xz;
            float cameraDistance=max(.1,distance(instance.positionHeight.xyz,_WorldSpaceCameraPos));
            float projected=(GRASS_POOL==0?instance.positionHeight.w:2.8)*_GrassProjection.x/(_GrassProjection.y>.5?1:cameraDistance);
#if defined(GRASS_PREPARED)
            float detail=prepared.motion.x;
            float nearCover=prepared.motion.z;
#else
            float detail=smoothstep(4,7,projected/GrassLodBias(anchor,_GrassWind.z)*1.5);
            float nearCover=GrassFieldFade(anchor,_GrassFocus.xz,_GrassNearRange.x,_GrassNearRange.y,_GrassWind.z);
            if((_GrassTransitionCheck&1)==0) nearCover=GrassHandoffFade(anchor,_GrassFocus.xz,_GrassNearRange.x,_GrassNearRange.y,_GrassWind.z);
#endif
            if(_GrassTopologyCheck>0) detail=0;
            if(_GrassBakeMode>.5) detail=1;
            float blade=input.meta.x, part=input.meta.y, t=input.uv.y, side=input.uv.x;
            float r=GrassHash(anchor+blade*13.71+_GrassWind.z), r2=GrassHash(anchor.yx+blade*7.3+17);
#if defined(GRASS_PREPARED)
            int family=(int)prepared.paletteFamily.w;
#else
            float cluster=GrassNoise(anchor/2.3+_GrassWind.z*.01);
            int family=cluster<.33?0:cluster<.66?1:2;
#endif
            int head=GRASS_POOL==0?0:part>.5?(int)floor((blade-32)*.111111111):(int)blade;
            bool floral=part>.5||blade<(GRASS_POOL==0?1:2);
            uint packed=asuint(instance.variation.x),flowerShift=head==1?16:9;
            uint code=(packed>>flowerShift)&127u;
            int species=(int)(code>>4),siteIndex=(int)(code&15u)-1;
            bool flower=floral&&_GrassFlowers.x>0&&siteIndex>=0;
            float2 flowerWorld=0,flowerRoot=0;
            int form=0;
            float stemHeight=0,nominalRadius=1,radius=1;
#if !defined(GRASS_PREPARED) || defined(GRASS_HEADS)
            float2 siteCell=floor(anchor);
            if(GRASS_POOL==1)
            {
                int row=(int)floor(siteIndex/3.0);
                siteCell=floor(floor(anchor/1.6)*1.6)+float2(siteIndex-row*3,row);
            }
            flowerWorld=GrassFlowerSitePosition(siteCell,_GrassWind.z);
            flowerRoot=flowerWorld-anchor;
            form=GrassFlowerForm(species);
            stemHeight=GRASS_POOL==0?instance.positionHeight.w*1.35:lerp(.48,.75,GrassHash(flowerWorld+27));
            nominalRadius=_GrassFlowers.z*GrassFlowerSize(species);
            radius=max(nominalRadius,min(nominalRadius*2.2,.45*(_GrassProjection.y>.5?1:cameraDistance)/max(1,_GrassProjection.x)));
#else
            // Ordinary blades never evaluate the flower palette, head shape or radius.
            [branch]if(flower)
            {
                float2 siteCell=floor(anchor);
                if(GRASS_POOL==1)
                {
                    int row=(int)floor(siteIndex/3.0);
                    siteCell=floor(floor(anchor/1.6)*1.6)+float2(siteIndex-row*3,row);
                }
                flowerWorld=GrassFlowerSitePosition(siteCell,_GrassWind.z);flowerRoot=flowerWorld-anchor;
                stemHeight=GRASS_POOL==0?instance.positionHeight.w*1.35:lerp(.48,.75,GrassHash(flowerWorld+27));
            }
#endif
            float facing=(packed&511u)*(6.283185/511)*.4+r*6.283185;
            float2 along=float2(cos(facing),sin(facing)), across=float2(-along.y,along.x);
            float2 root=along*lerp(.04,.32,r2);
            float height=instance.positionHeight.w*lerp(.65,1.35,r2);
            float width=lerp(.025,.055,r), reach=height*lerp(.2,.8,r);
            if(family==0) {height*=.6; reach*=1.8;}
            if(family==2) {height*=.42; width*=2.1; reach*=1.5;}
            if(GRASS_POOL==1)
            {
                // Half of the patch keeps upright stems instead of losing all meadow height.
                // Broad leaves preserve overhead coverage; both use the same palette/wind field.
                root=float2(GrassHash(anchor+blade*29)-.5,GrassHash(anchor.yx+blade*19)-.5)*2.4;
                if(blade<16)
                {
                    height=instance.positionHeight.w*lerp(.5,1.5,r2);
                    width=lerp(.11,.21,r);reach=lerp(.5,.9,r2);
                }
                else
                {
                    height=instance.positionHeight.w*lerp(1.4,2.6,r2);
                    width=lerp(.05,.10,r);reach=height*lerp(.2,.8,r2);
                }
                height*=lerp(1.15,1,nearCover);
            }
#if defined(GRASS_SPARSE)
            width*=GRASS_POOL==0?2:1.6;
#endif
            if(flower&&part<.5) {height=stemHeight;width=GRASS_POOL==0?.009:.013;reach=0;root=flowerRoot;}
            float morph=GRASS_POOL==0?detail:1;
#if defined(GRASS_PREPARED)
            float widthCurve=input.coefficients.x;
#else
            float widthCurve=pow(saturate(1-t),.55);
#endif
            float2 curved=root+along*reach*t*t+across*width*side*widthCurve;
            float2 straight=root+along*reach*t+across*width*side*(1-t);
            float2 xz=lerp(straight,curved,morph);
            float y=height*t;
            float3 normal;
            [branch]if(morph>=1)normal=normalize(float3(-along.x*height*.35,reach*t+.15,-along.y*height*.35));
            else
            {
                float3 rootNormal=normalize(float3(-along.x*height*.35,.15,-along.y*height*.35));
                float3 tipNormal=normalize(float3(-along.x*height*.35,reach+.15,-along.y*height*.35));
                normal=lerp(rootNormal,tipNormal,t);
                [branch]if(morph>0)normal=lerp(normal,normalize(float3(-along.x*height*.35,reach*t+.15,-along.y*height*.35)),morph);
            }
            if(GRASS_POOL==0&&part<.5) xz+=across*side*width*saturate(1-projected/5)*.45*lerp(1-t,1,morph);
#if !defined(GRASS_PREPARED) || defined(GRASS_HEADS)
            if(part>.5)
            {
                int petal=(int)(GRASS_POOL==0?blade-12:blade-32-head*9);
                int petals=GrassFlowerPetals(form);
                float theta=petal*(6.283185/petals)+GrassHash(flowerWorld+91)*6.283185;
                float2 direction=float2(cos(theta),sin(theta)),crosswise=float2(-direction.y,direction.x);
                float width=form==0?.22:form==1?.6:form==2?.48:.17;
                float baseRadius=form==2?.36:.12;
                root=flowerRoot;
                xz=flowerRoot+direction*radius*lerp(baseRadius,1,t)+crosswise*side*radius*width;
                y=stemHeight+(form==1?t*.55:form==2?-t*.9:form==3?t*.18:(1-t)*.06)*radius;
                normal=normalize(float3(direction.x*(form==2?.65:form==1?-.4:0),1,direction.y*(form==2?.65:form==1?-.4:0)));
                if(part>1.5)
                {
                    xz=flowerRoot+float2(side*.23,lerp(-.15,.28,t))*radius;
                    y=stemHeight+radius*.08;normal=float3(0,1,0);
                }
                height=stemHeight;
                if(!flower||(part<1.5&&petal>=petals)){xz=flowerRoot;y=stemHeight;}
            }
#endif
#if defined(GRASS_PREPARED)
            float gust=prepared.motion.y,windTime=_GrassFrameTime;
#else
            float gust=GrassGust(anchor,_Time.y,_GrassWind.y),windTime=_Time.y;
#endif
            // At morph=0 intermediate vertices lie on the same moving triangle, including normals.
            float windTip=part>.5?1:lerp(t,t*t,morph);
            float windPhase=part>.5||flower?GrassHash(flowerWorld+43)*6.283185:facing;
            xz+=float2(.85,.4)*(gust+sin(windTime*2.1+windPhase)*.08)*_GrassWind.x*windTip*height;
            Varyings output;
            float3 position=instance.positionHeight.xyz+float3(xz.x,y,xz.y);
            [branch]if(GRASS_POOL==1||flower) position.y=GroundHeight(anchor+root)+y-.02;
            output.world=position;
            // A per-blade world seed keeps the fade independent of triangle subdivision.
            output.noiseSeed=floor(float2(GrassHash(anchor+blade*23.73),GrassHash(anchor+blade*37.13+_GrassWind.z))*8192);
            output.positionCS=TransformWorldToHClip(position); output.shadowCoord=ComputeScreenPos(output.positionCS);
            output.normal=normal;
#if defined(GRASS_PREPARED)
            half3 palette=prepared.paletteFamily.rgb;
#else
            half3 palette=GrassPalette(anchor,instance.variation.y,_GrassWind.z,_GrassMeadow.rgb,_GrassDry.rgb);
#endif
            if(_GrassBakeMode>.5) palette=1;
            output.color=palette*lerp(.72,1.2,t)*lerp(.91,1.08,r);
            output.petal=part>.5&&flower?1:0;
#if !defined(GRASS_PREPARED) || defined(GRASS_HEADS)
            if(part>.5)
            {
                output.color=(part>1.5?GrassFlowerCenterColor(form):GrassFlowerColor(species).rgb)*lerp(1,.7,saturate((radius/nominalRadius-1)/1.2));
                if(_GrassBakeMode>.5) output.color=1;
            }
#endif
            float canopyPixels=.45*_GrassProjection.x/(_GrassProjection.y>.5?1:cameraDistance);
            output.canopy=(_GrassTransitionCheck&2)==0?GrassCanopyWeight(canopyPixels):0;
            if(_GrassBakeMode<.5&&part<.5) output.color=lerp(output.color,palette*.93*lerp(.82,1.18,r),output.canopy);
            if(_GrassOptions.y>.5&&_GrassOptions.y<1.5) output.color=half3(.1,.7,.1)*abs(instance.variation.w);
            if(_GrassOptions.y>1.5&&_GrassOptions.y<2.5&&part<.5) output.color=GRASS_POOL==0?lerp(half3(.8,.7,.08),half3(.1,.7,.2),detail):half3(.7,.3,.04);
            if(_GrassOptions.y>2.5) output.color=half3(GrassHash(floor(anchor/_GrassOptions.x)),.3,.5);
            float fadeStart=GRASS_POOL==0?min(_GrassLodDistances.y,_GrassLodDistances.x*.9):_GrassLodDistances.x*.72;
            output.fade=GrassFieldFade(anchor,_GrassFocus.xz,fadeStart,_GrassLodDistances.x,_GrassWind.z)*GrassScreenFade(projected,_GrassLodDistances.z);
            if((_GrassTransitionCheck&1)==0) output.fade=GrassScreenFade(projected,_GrassLodDistances.z);
            if(part>.5&&flower) output.fade=(_GrassTransitionCheck&1)==0?1:GrassFieldFade(anchor,_GrassFocus.xz,fadeStart,_GrassLodDistances.x,_GrassWind.z);
            output.detail=detail;
            [branch] if(instance.variation.w<0) output.fade*=GroundPermission(anchor+root);
            if(GRASS_POOL==1) output.fade*=saturate((_GrassProjection.z-instance.variation.z)*12);
            // Stable individual delays prevent an entire streamed cell blooming together.
            float delay=GrassHash(anchor+317.3)*.18;
            output.fade*=smoothstep(delay,delay+.55,_GrassClock-_GrassBirth[id/_GrassPerSlot]);
            if(_GrassBakeMode>.5) {output.fade=1;output.detail=1;}
            output.fog=ComputeFogFactor(output.positionCS.z); return output;
        }
        void Fade(Varyings input)
        {
            if(_GrassBakeMode<.5&&(_GrassTransitionCheck&1)==0)
                clip(GrassLayerMask(input.world.xz,input.positionCS.xy,_GrassFocus.xz,_GrassFieldRanges,GRASS_POOL,_GrassWind.z,
                    GrassCanopyWeight(.45*_GrassProjection.x/(_GrassProjection.y>.5?1:max(.1,distance(input.world,_WorldSpaceCameraPos))))));
            if(_GrassBakeMode>.5&&_GrassBakeRegion.w>0)
                clip(_GrassBakeRegion.z-max(abs(input.world.x-_GrassBakeRegion.x),abs(input.world.z-_GrassBakeRegion.y)));
            // Pixel coordinates are exact across topologies; interpolated positions amplified
            // subdivision roundoff into different clipping in the range-fade zone. No frame jitter.
            float noise=GrassHash(floor(input.positionCS.xy)+input.noiseSeed);
            clip(input.fade-noise);
        }
        half4 Shade(Varyings input)
        {
            if(_GrassBakeMode>2.5) return half4(input.petal.xxx,1);
            if(_GrassBakeMode>1.5) return half4(normalize(input.normal).xzy*.5+.5,1);
            if(_GrassBakeMode>.5) return half4(input.color,1);
            #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
            Light light=GetMainLight(input.shadowCoord);
            #else
            Light light=GetMainLight(TransformWorldToShadowCoord(input.world));
            #endif
            float3 normal=normalize(input.normal);
            half diffuse=saturate((dot(normal,light.direction)+.25)/1.25);
            half3 ambient=lerp(_GrassAmbientSide.rgb,_GrassAmbientSky.rgb,saturate(normal.y));
            // Indirect draws have no Renderer LightData.z. A directional sun has no distance attenuation.
            half3 color=input.color*(ambient+light.color*diffuse*light.shadowAttenuation);
            color=lerp(color,input.color*GrassCanopyLighting(_GrassAmbientSky.rgb,light.color,light.direction,light.shadowAttenuation),input.canopy*.85);
            return half4(MixFog(color,input.fog),1);
        }
        half4 Frag(Varyings input):SV_Target
        {
            Fade(input);
            return Shade(input);
        }
        struct LitNormalOutput { half4 color:SV_Target0; half4 normal:SV_Target1; };
        LitNormalOutput LitNormals(Varyings input)
        {
            Fade(input); LitNormalOutput o; o.color=Shade(input);o.normal=half4(normalize(input.normal),0);return o;
        }
        half4 Depth(Varyings input):SV_Target { Fade(input); return 0; }
        ENDHLSL
        Pass
        {
            Name "GrassForward" Tags { "LightMode"="UniversalForward" }
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
            Name "GrassLitNormals" Tags { "LightMode"="GrassLitNormals" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment LitNormals
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
            half4 Normals(Varyings input):SV_Target { Fade(input); return half4(normalize(input.normal),0); }
            ENDHLSL
        }
    }
}
