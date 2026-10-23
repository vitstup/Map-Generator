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
        float4 _GroundRect0,_GroundRect1,_GroundRect2,_GroundVertical0,_GroundVertical1,_GroundVertical2;
        float4 _GrassMeadow,_GrassDry,_GrassWind,_GrassOptions,_GrassLodDistances,_GrassProjection;
        float4 _GrassAmbientSky,_GrassAmbientSide;
        int _GrassLod,_GrassPool;
        float _GrassBakeMode;
        struct Attributes { float3 positionOS:POSITION; float2 uv:TEXCOORD0; float2 meta:TEXCOORD1; uint instanceID:SV_InstanceID; };
        struct Varyings { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; half3 normal:TEXCOORD1; half3 color:TEXCOORD2; half fog:TEXCOORD3; float4 shadowCoord:TEXCOORD4; half fade:TEXCOORD5; };
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
        Varyings Vert(Attributes input)
        {
            InitIndirectDrawArgs(0);
            uint id=_VisibleGrass[GetIndirectInstanceID(input.instanceID)];
            GrassInstance instance=_GrassInstances[id];
            float2 anchor=instance.positionHeight.xz;
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
                // A low, wide meadow patch. Leaves face many directions and cover area from above.
                root=float2(GrassHash(anchor+blade*29)-.5,GrassHash(anchor.yx+blade*19)-.5)*2.4;
                height=instance.positionHeight.w*lerp(.5,1.5,r2);
                width=lerp(.08,.16,r); reach=lerp(.5,.9,r2);
            }
            float2 xz=root+along*reach*t*t+across*width*side*pow(saturate(1-t),.55);
            float y=height*t;
            float3 normal=normalize(float3(-along.x*height*.35,reach*t+.15,-along.y*height*.35));
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
            xz+=float2(.85,.4)*(gust+sin(_Time.y*2.1+facing)*.08)*_GrassWind.x*t*t*height;
            float d=distance(instance.positionHeight.xyz,_WorldSpaceCameraPos);
            float depth=max(.1,dot(instance.positionHeight.xyz-_WorldSpaceCameraPos,UNITY_MATRIX_V[2].xyz*-1));
            float projected=(_GrassPool==0?height:2.8)*_GrassProjection.x/(_GrassProjection.y>.5?1:depth);
            // Modest widening protects edge-on blades; it never turns the plants into billboards.
            if(_GrassPool==0&&part<.5) xz+=across*side*width*saturate(1-projected/5)*.45;
            Varyings output;
            float3 position=instance.positionHeight.xyz+float3(xz.x,y,xz.y);
            if(_GrassPool==1) position.y=GroundHeight(anchor+root)+y-.02;
            output.world=position;
            output.positionCS=TransformWorldToHClip(position); output.shadowCoord=ComputeScreenPos(output.positionCS);
            output.normal=normal;
            half3 palette=GrassPalette(anchor,instance.variation.y,_GrassWind.z,_GrassMeadow.rgb,_GrassDry.rgb);
            if(_GrassBakeMode>.5) palette=1;
            output.color=palette*lerp(.72,1.2,t)*lerp(.91,1.08,r);
            if(part>.5) output.color=lerp(half3(.8,.83,.62),half3(.34,.39,.62),GrassHash(anchor+91));
            if(_GrassOptions.y>.5&&_GrassOptions.y<1.5) output.color=half3(.1,.7,.1)*instance.variation.w;
            if(_GrassOptions.y>1.5&&_GrassOptions.y<2.5) output.color=_GrassPool==0?half3(.1,.7,.2):half3(.7,.3,.04);
            if(_GrassOptions.y>2.5) output.color=half3(GrassHash(floor(anchor/_GrassOptions.x)),.3,.5);
            float fadeStart=_GrassPool==0?min(_GrassLodDistances.y,_GrassLodDistances.x*.9):_GrassLodDistances.x*.72;
            output.fade=(1-smoothstep(fadeStart,_GrassLodDistances.x,d))*smoothstep(_GrassLodDistances.z,_GrassLodDistances.z*2,projected);
            float angleKeep=lerp(.65,1,smoothstep(.2,.75,abs(UNITY_MATRIX_V[2].y)));
            if(_GrassPool==0) output.fade*=saturate((angleKeep-instance.variation.z)*12);
            else output.fade*=saturate((_GrassProjection.z-instance.variation.z)*12);
            output.fade*=smoothstep(0,.28,_GrassClock-_GrassBirth[id/_GrassPerSlot]);
            if(_GrassBakeMode>.5) output.fade=1;
            output.fog=ComputeFogFactor(output.positionCS.z); return output;
        }
        void Fade(Varyings input)
        {
            // Spatial dither: stable in world space, identical in color/depth/normal passes.
            float noise=GrassHash(floor(input.world.xz*190)+floor(input.world.y*190));
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
