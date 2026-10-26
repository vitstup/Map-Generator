Shader "TacticalMaps/Agriculture/Banks"
{
    Properties { _BaseMap("Earth",2D)="white"{} _NormalMap("Earth normals",2D)="bump"{} _BaseColor("Tint",Color)=(1,1,1,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Cull Off
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);TEXTURE2D(_NormalMap);
            struct A { float3 p:POSITION;float3 n:NORMAL;float4 color:COLOR; };
            struct V { float4 p:SV_POSITION;float3 world:TEXCOORD0;half3 n:TEXCOORD1;half4 color:COLOR;float fog:TEXCOORD2; };
            V Vert(A a){V o;o.world=TransformObjectToWorld(a.p);o.p=TransformWorldToHClip(o.world);o.n=TransformObjectToWorldNormal(a.n);o.color=a.color;o.fog=ComputeFogFactor(o.p.z);return o;}
            half4 Frag(V i):SV_Target
            {
                half3 n=normalize(i.n);float3 weights=pow(abs(n),4);weights/=max(.001,dot(weights,1));
                float3 p=i.world/1.8;
                half3 earth=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,p.zy).rgb*weights.x+SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,p.xz).rgb*weights.y+SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,p.xy).rgb*weights.z;
                earth=SRGBToLinear(half3(.36,.31,.22))*clamp(dot(earth,half3(.2126,.7152,.0722))/.18,.45,1.8);
                float mottling=.5+.5*sin(i.world.x*.37+sin(i.world.z*.29)*2.3);
                half3 moss=SRGBToLinear(half3(.28,.35,.135));
                half3 color=lerp(earth*.88,moss,saturate(n.y)*mottling*.32);
                float3 detail=UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap,sampler_BaseMap,p.xz),.5);
                n=normalize(n+float3(detail.x,0,detail.y)*.35);
                if(i.color.a>.75)color=i.color.rgb;
                else if(i.color.a>.25)color=i.color.rgb*lerp(.8,1.1,mottling);
                Light sun=GetMainLight(TransformWorldToShadowCoord(i.world));
                color*=SampleSH(n)+sun.color*(.18+.82*saturate(dot(n,sun.direction)))*sun.shadowAttenuation;
                return half4(MixFog(color,i.fog),1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }
}
