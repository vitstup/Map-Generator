Shader "TacticalMaps/Trees/Foliage"
{
    Properties
    {
        _LeafMap("Leaves", 2D) = "white" {}
        _TrunkMap("Trunk", 2D) = "white" {}
        [Normal] _LeafNormal("Leaf normal", 2D) = "bump" {}
        _NormalStrength("Leaf normal strength", Range(0,1)) = 0.5
        _LeafUV("Leaf tiling / offset", Vector) = (1,1,0,0)
        _TrunkUV("Trunk tiling / offset", Vector) = (1,1,0,0)
        _LeafColor("Leaf tint", Color) = (1,1,1,1)
        _TrunkColor("Trunk tint", Color) = (1,1,1,1)
        _FrostColor("Upper leaf tint", Color) = (1,1,1,1)
        _FrostHeight("Upper tint exponent", Float) = 1
        _FrostFalloff("Upper tint strength", Float) = 0
        _WindStrength("Wind amplitude", Float) = 0.12
        _WindFadeStart("Wind fade start", Float) = 30
        _WindFadeEnd("Wind fade end", Float) = 110
        _Cutoff("Alpha cutoff", Range(0,1)) = 0.25
        [HideInInspector] _TreeFade("LOD coverage", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off ZWrite On
        HLSLINCLUDE
        #pragma target 3.5
        #include "TreeFoliage.hlsl"
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma vertex TreeVert
            #pragma fragment TreeForward
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma shader_feature_local_fragment _TREE_LEAF_NORMAL
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex TreeShadowVert
            #pragma fragment TreeDepth
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex TreeVert
            #pragma fragment TreeDepth
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            HLSLPROGRAM
            #pragma vertex TreeVert
            #pragma fragment TreeDepthNormals
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }
}
