Shader "Hidden/TacticalMaps/GrassComposite"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite On
        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D_X_FLOAT(_GrassStoredDepth);
        TEXTURE2D_X(_GrassStoredColor);
        struct Varyings { float4 positionCS:SV_POSITION; };
        Varyings Vert(uint vertexID:SV_VertexID)
        {
            Varyings o; o.positionCS=GetFullScreenTriangleVertexPosition(vertexID); return o;
        }
        struct Result { half4 color:SV_Target; float depth:SV_Depth; };
        Result Composite(Varyings i)
        {
            Result o; o.color=LOAD_TEXTURE2D_X(_GrassStoredColor,uint2(i.positionCS.xy));
            clip(o.color.a-.5);
            o.depth=LOAD_TEXTURE2D_X(_GrassStoredDepth,uint2(i.positionCS.xy)).r; return o;
        }
        ENDHLSL
        Pass
        {
            Name "Composite" ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Composite
            ENDHLSL
        }
    }
}
