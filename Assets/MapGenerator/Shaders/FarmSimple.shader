Shader "TacticalMaps/Agriculture/Simple"
{
    Properties { _BaseColor("Tint",Color)=(1,1,1,1) }
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
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct A { float3 p:POSITION; float3 n:NORMAL; float4 c:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 p:SV_POSITION; float3 world:TEXCOORD0; half3 n:TEXCOORD1; half4 c:COLOR; float fog:TEXCOORD2; };
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            CBUFFER_END
            V Vert(A a){UNITY_SETUP_INSTANCE_ID(a);V o;o.world=TransformObjectToWorld(a.p);o.p=TransformWorldToHClip(o.world);o.n=TransformObjectToWorldNormal(a.n);o.c=a.c*_BaseColor;o.fog=ComputeFogFactor(o.p.z);return o;}
            half4 Frag(V i):SV_Target{Light l=GetMainLight(TransformWorldToShadowCoord(i.world));half3 c=i.c.rgb*(SampleSH(i.n)+l.color*(.25+.75*saturate(dot(normalize(i.n),l.direction)))*l.shadowAttenuation);return half4(MixFog(c,i.fog),1);}
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }
}
