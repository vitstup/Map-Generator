Shader "TacticalMaps/Agriculture/Water"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct A { float3 p:POSITION; };
            struct V { float4 p:SV_POSITION; float3 world:TEXCOORD0; float fog:TEXCOORD1; };
            V Vert(A a){V o;o.world=TransformObjectToWorld(a.p);o.p=TransformWorldToHClip(o.world);o.fog=ComputeFogFactor(o.p.z);return o;}
            half4 Frag(V i):SV_Target{float ripple=sin(i.world.x*2+_Time.y)*sin(i.world.z*2.7+_Time.y*.8);half3 color=half3(.18,.235,.19)+ripple*.012;return half4(MixFog(color,i.fog),.48);}
            ENDHLSL
        }
    }
}
