Shader "TacticalMaps/Agriculture/Water"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
            Offset -1, -2
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct A { float3 p:POSITION; };
            struct V { float4 p:SV_POSITION; float3 world:TEXCOORD0; float fog:TEXCOORD1; };
            V Vert(A a){V o;o.world=TransformObjectToWorld(a.p);o.p=TransformWorldToHClip(o.world);o.fog=ComputeFogFactor(o.p.z);return o;}
            half4 Frag(V i):SV_Target
            {
                float ripple=sin(i.world.x*2+_Time.y)*sin(i.world.z*2.7+_Time.y*.8);
                float fresnel=pow(1-saturate(normalize(_WorldSpaceCameraPos-i.world).y),5);
                half3 color=SRGBToLinear(half3(.25,.32,.27))+ripple*.004+fresnel*half3(.07,.09,.10);
                float range=1-smoothstep(250,600,distance(i.world,_WorldSpaceCameraPos));
                return half4(MixFog(color,i.fog),(.32+fresnel*.25)*range);
            }
            ENDHLSL
        }
    }
}
