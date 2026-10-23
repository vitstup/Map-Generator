Shader "TacticalMaps/Grass/LowPolyIndirect"
{
    Properties { _GrassMeadow("Meadow",Color)=(.38,.47,.17,1) _GrassDry("Dry",Color)=(.49,.46,.23,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off ZWrite On
        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
        #include "UnityIndirect.cginc"
        #include "GrassCommon.hlsl"
        struct GrassInstance { float4 positionHeight; float4 variation; };
        StructuredBuffer<GrassInstance> _GrassInstances;
        StructuredBuffer<uint> _VisibleGrass;
        StructuredBuffer<float> _GrassBirth;
        uint _GrassPerSlot;
        float _GrassClock;
        TEXTURE2D(_GroundHeight0); TEXTURE2D(_GroundHeight1); TEXTURE2D(_GroundHeight2);
        SAMPLER(sampler_GroundHeight0);
        TEXTURE2D(_GroundCoverage0); TEXTURE2D(_GroundCoverage1); TEXTURE2D(_GroundCoverage2);
        SAMPLER(sampler_GroundCoverage0);
        float4 _GroundRect0,_GroundRect1,_GroundRect2,_GroundVertical0,_GroundVertical1,_GroundVertical2;
        float4 _GrassMeadow,_GrassDry,_GrassWind,_GrassOptions,_GrassLodDistances,_GrassProjection;
        float4 _GrassAmbientSky,_GrassAmbientSide,_GrassFocus,_GrassNearRange;
        int _GrassLod,_GrassPool,_GrassTopologyCheck;
        float _GrassBakeMode;
        float4 _GrassBakeRegion;
        struct Attributes { float3 positionOS:POSITION; float2 uv:TEXCOORD0; float2 meta:TEXCOORD1; uint instanceID:SV_InstanceID; };
        struct Varyings { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; half3 normal:TEXCOORD1; half3 color:TEXCOORD2; half fog:TEXCOORD3; float4 shadowCoord:TEXCOORD4; half fade:TEXCOORD5; half detail:TEXCOORD6; nointerpolation float2 noiseSeed:TEXCOORD7; };
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
            float2 anchor=instance.positionHeight.xz;
            float cameraDistance=max(.1,distance(instance.positionHeight.xyz,_WorldSpaceCameraPos));
            float projected=(_GrassPool==0?instance.positionHeight.w:2.8)*_GrassProjection.x/(_GrassProjection.y>.5?1:cameraDistance);
            float detail=GrassDetailWeight(projected/GrassLodBias(anchor,_GrassWind.z));
            if(_GrassTopologyCheck>0) detail=0;
            if(_GrassBakeMode>.5) detail=1;
            float nearCover=GrassFieldFade(anchor,_GrassFocus.xz,_GrassNearRange.x,_GrassNearRange.y,_GrassWind.z);
            float blade=input.meta.x, part=input.meta.y, t=input.uv.y, side=input.uv.x;
            float r=GrassHash(anchor+blade*13.71+_GrassWind.z), r2=GrassHash(anchor.yx+blade*7.3+17);
            float cluster=GrassNoise(anchor/2.3+_GrassWind.z*.01);
            int family=cluster<.33?0:cluster<.66?1:2;
            // Species selection is independent of the rank used for angle/density thinning.
            bool flower=cluster>.58&&GrassHash(anchor+138.7)>.965;
            float facing=instance.variation.x*.4+r*6.283185;
            float2 along=float2(cos(facing),sin(facing)), across=float2(-along.y,along.x);
            float2 root=along*lerp(.04,.32,r2);
            float height=instance.positionHeight.w*lerp(.65,1.35,r2);
            float width=lerp(.025,.055,r), reach=height*lerp(.2,.8,r);
            if(family==0) {height*=.6; reach*=1.8;}
            if(family==2) {height*=.42; width*=2.1; reach*=1.5;}
            if(flower&&blade==0) {height=instance.positionHeight.w*1.35; width=.009; reach=0;root=0;}
            if(_GrassPool==1)
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
            float morph=_GrassPool==0?detail:1;
            float2 curved=root+along*reach*t*t+across*width*side*pow(saturate(1-t),.55);
            float2 straight=root+along*reach*t+across*width*side*(1-t);
            float2 xz=lerp(straight,curved,morph);
            float y=height*t;
            float3 rootNormal=normalize(float3(-along.x*height*.35,.15,-along.y*height*.35));
            float3 tipNormal=normalize(float3(-along.x*height*.35,reach+.15,-along.y*height*.35));
            float3 normal=lerp(lerp(rootNormal,tipNormal,t),normalize(float3(-along.x*height*.35,reach*t+.15,-along.y*height*.35)),morph);
            if(_GrassPool==0&&part<.5) xz+=across*side*width*saturate(1-projected/5)*.45*lerp(1-t,1,morph);
            if(part>.5)
            {
                // Petals share a stem tip. Only flower instances retain this geometry.
                float theta=(blade-12)*1.256637;
                float2 direction=float2(cos(theta),sin(theta));
                float2 crosswise=float2(-direction.y,direction.x);
                xz=direction*(.018+t*.085)+crosswise*side*.034;
                y=instance.positionHeight.w*1.35+(1-t)*.025;
                normal=float3(0,1,0);
                if(!flower||_GrassPool==1) {xz=0;y=0;}
            }
            float gust=GrassGust(anchor,_Time.y,_GrassWind.y);
            // At morph=0 intermediate vertices lie on the same moving triangle, including normals.
            xz+=float2(.85,.4)*(gust+sin(_Time.y*2.1+facing)*.08)*_GrassWind.x*lerp(t,t*t,morph)*height;
            Varyings output;
            float3 position=instance.positionHeight.xyz+float3(xz.x,y,xz.y);
            if(_GrassPool==1) position.y=GroundHeight(anchor+root)+y-.02;
            output.world=position;
            // A per-blade world seed keeps the fade independent of triangle subdivision.
            output.noiseSeed=floor(float2(GrassHash(anchor+blade*23.73),GrassHash(anchor+blade*37.13+_GrassWind.z))*8192);
            output.positionCS=TransformWorldToHClip(position); output.shadowCoord=ComputeScreenPos(output.positionCS);
            output.normal=normal;
            half3 palette=GrassPalette(anchor,instance.variation.y,_GrassWind.z,_GrassMeadow.rgb,_GrassDry.rgb);
            if(_GrassBakeMode>.5) palette=1;
            output.color=palette*lerp(.72,1.2,t)*lerp(.91,1.08,r);
            if(part>.5) output.color=lerp(half3(.8,.83,.62),half3(.34,.39,.62),GrassHash(anchor+91));
            if(_GrassOptions.y>.5&&_GrassOptions.y<1.5) output.color=half3(.1,.7,.1)*abs(instance.variation.w);
            if(_GrassOptions.y>1.5&&_GrassOptions.y<2.5) output.color=_GrassPool==0?lerp(half3(.8,.7,.08),half3(.1,.7,.2),detail):half3(.7,.3,.04);
            if(_GrassOptions.y>2.5) output.color=half3(GrassHash(floor(anchor/_GrassOptions.x)),.3,.5);
            float fadeStart=_GrassPool==0?min(_GrassLodDistances.y,_GrassLodDistances.x*.9):_GrassLodDistances.x*.72;
            output.fade=GrassFieldFade(anchor,_GrassFocus.xz,fadeStart,_GrassLodDistances.x,_GrassWind.z)*GrassScreenFade(projected,_GrassLodDistances.z);
            output.detail=detail;
            [branch] if(instance.variation.w<0) output.fade*=GroundPermission(anchor+root);
            if(_GrassPool==1) output.fade*=saturate((_GrassProjection.z-instance.variation.z)*12);
            // Stable individual delays prevent an entire streamed cell blooming together.
            float delay=GrassHash(anchor+317.3)*.18;
            output.fade*=smoothstep(delay,delay+.55,_GrassClock-_GrassBirth[id/_GrassPerSlot]);
            if(_GrassBakeMode>.5) {output.fade=1;output.detail=1;}
            output.fog=ComputeFogFactor(output.positionCS.z); return output;
        }
        void Fade(Varyings input)
        {
            if(_GrassBakeMode>.5&&_GrassBakeRegion.w>0)
                clip(_GrassBakeRegion.z-max(abs(input.world.x-_GrassBakeRegion.x),abs(input.world.z-_GrassBakeRegion.y)));
            // Pixel coordinates are exact across topologies; interpolated positions amplified
            // subdivision roundoff into different clipping in the range-fade zone. No frame jitter.
            float noise=GrassHash(floor(input.positionCS.xy)+input.noiseSeed);
            clip(input.fade-noise);
        }
        half4 Frag(Varyings input):SV_Target
        {
            Fade(input);
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
            return half4(MixFog(color,input.fog),1);
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
