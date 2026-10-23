// Terrain property declarations and reused pass interfaces adapted from Unity URP.
// Unity Technologies copyright and Unity Companion License: see THIRD_PARTY_NOTICES.md.
Shader "Hidden/TacticalMaps/Terrain/MatteAdd"
{
    Properties
    {
        [HideInInspector] _GrassEnabled("Grass enabled", Float) = 0
        [HideInInspector] _GrassTurf("Grass turf albedo", 2D) = "white" {}
        [HideInInspector] _GrassTurfNormal("Grass turf normal", 2D) = "bump" {}
        [HideInInspector] _GrassTurfStrength("Grass turf strength", Float) = 1
        [HideInInspector] _GrassCoverage("Grass coverage", 2D) = "black" {}
        [HideInInspector] _GrassRect("Grass bounds", Vector) = (0,0,1,1)
        [HideInInspector] _GrassMeadow("Grass meadow", Color) = (.28,.39,.115,1)
        [HideInInspector] _GrassDry("Grass dry", Color) = (.49,.43,.22,1)
        [HideInInspector] _GrassWind("Grass wind", Vector) = (.22,1.2,7819,.9)
        [HideInInspector] _GrassOptions("Grass options", Vector) = (32,0,0,.8)
        [HideInInspector] [ToggleUI] _EnableHeightBlend("EnableHeightBlend", Float) = 0.0
        _HeightTransition("Height Transition", Range(0, 1.0)) = 0.0
        // Layer count is passed down to guide height-blend enable/disable, due
        // to the fact that heigh-based blend will be broken with multipass.
        [HideInInspector] [PerRendererData] _NumLayersCount ("Total Layer Count", Float) = 1.0

        // set by terrain engine
        [HideInInspector] _Control("Control (RGBA)", 2D) = "red" {}
        [HideInInspector] _Splat3("Layer 3 (A)", 2D) = "grey" {}
        [HideInInspector] _Splat2("Layer 2 (B)", 2D) = "grey" {}
        [HideInInspector] _Splat1("Layer 1 (G)", 2D) = "grey" {}
        [HideInInspector] _Splat0("Layer 0 (R)", 2D) = "grey" {}
        [HideInInspector] _Normal3("Normal 3 (A)", 2D) = "bump" {}
        [HideInInspector] _Normal2("Normal 2 (B)", 2D) = "bump" {}
        [HideInInspector] _Normal1("Normal 1 (G)", 2D) = "bump" {}
        [HideInInspector] _Normal0("Normal 0 (R)", 2D) = "bump" {}
        [HideInInspector] _Mask3("Mask 3 (A)", 2D) = "grey" {}
        [HideInInspector] _Mask2("Mask 2 (B)", 2D) = "grey" {}
        [HideInInspector] _Mask1("Mask 1 (G)", 2D) = "grey" {}
        [HideInInspector] _Mask0("Mask 0 (R)", 2D) = "grey" {}
        [HideInInspector][Gamma] _Metallic0("Metallic 0", Range(0.0, 1.0)) = 0.0
        [HideInInspector][Gamma] _Metallic1("Metallic 1", Range(0.0, 1.0)) = 0.0
        [HideInInspector][Gamma] _Metallic2("Metallic 2", Range(0.0, 1.0)) = 0.0
        [HideInInspector][Gamma] _Metallic3("Metallic 3", Range(0.0, 1.0)) = 0.0
        [HideInInspector] _Smoothness0("Smoothness 0", Range(0.0, 1.0)) = 0.5
        [HideInInspector] _Smoothness1("Smoothness 1", Range(0.0, 1.0)) = 0.5
        [HideInInspector] _Smoothness2("Smoothness 2", Range(0.0, 1.0)) = 0.5
        [HideInInspector] _Smoothness3("Smoothness 3", Range(0.0, 1.0)) = 0.5

        // used in fallback on old cards & base map
        [HideInInspector] _MainTex("BaseMap (RGB)", 2D) = "grey" {}
        [HideInInspector] _BaseColor("Main Color", Color) = (1,1,1,1)

        [HideInInspector] _TerrainHolesTexture("Holes Map (RGB)", 2D) = "white" {}

        [ToggleUI] _EnableInstancedPerPixelNormal("Enable Instanced per-pixel normal", Float) = 1.0
        _StochasticStrength ("Anti-tiling strength", Range(0,1)) = 1
        _StochasticPatchTiles ("Patch width, texture tiles", Range(0.5,8)) = 2
        _StochasticBlendSharpness ("Patch blend sharpness", Range(1,6)) = 3
        _StochasticSeed ("Pattern seed", Float) = 137
        _AntiTileFadeStart ("Anti-tiling fade start, metres", Float) = 1200
        _AntiTileFadeEnd ("Anti-tiling fade end, metres", Float) = 3000
        _DetailNormalStrength ("Detail normal strength", Range(0,1)) = 0.5
        [HideInInspector] _TacticalTerrainSize ("Terrain world size", Vector) = (2048,2048,0,0)
    }

    SubShader
    {
        Tags { "Queue"="Geometry-100" "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "TerrainCompatible"="True" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend One One
            ZWrite Off
            HLSLPROGRAM
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
            #pragma vertex SplatmapVert
            #pragma fragment MatteFragment
            #include_with_pragmas "TerrainMatteKeywords.hlsl"
#define TERRAIN_SPLAT_ADDPASS 1
            #include "TerrainMattePasses.hlsl"
            ENDHLSL
        }
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
