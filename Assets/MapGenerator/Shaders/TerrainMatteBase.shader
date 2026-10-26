// Terrain property declarations and reused pass interfaces adapted from Unity URP.
// Unity Technologies copyright and Unity Companion License: see THIRD_PARTY_NOTICES.md.
Shader "Hidden/TacticalMaps/Terrain/MatteBase"
{
    Properties
    {
        [HideInInspector] _FarmMap("Farm parcels", 2D) = "black" {}
        [HideInInspector] _FarmVersion("Farm layout version", Float) = 0
        [HideInInspector] _FarmSoilNormal("Farm soil normals", 2D) = "bump" {}
        [HideInInspector] _FarmPages("Farm distance pages", 2D) = "black" {}
        [HideInInspector] _FarmProfiles("Farm parcel profiles", 2D) = "black" {}
        [HideInInspector] _FarmDistances("Farm distance tiles", 2DArray) = "" {}
        [HideInInspector] _FarmDetailGrid("Farm detail grid", Vector) = (0,0,1,1)
        [HideInInspector] _FarmCenter("Farm center", Vector) = (0,0,0,0)
        [HideInInspector] _FarmSoil("Farm soil", 2D) = "gray" {}
        [HideInInspector] _FarmRect("Farm bounds", Vector) = (0,0,1,1)
        [HideInInspector] _FarmEnabled("Farm surface enabled", Float) = 0
        [HideInInspector] _GrassTurfFlowers("Flower petal mask", 2D) = "black" {}
        [HideInInspector] _GrassFlowers("Flower settings", Vector) = (0,12,.095,0)
        [HideInInspector] _GrassFlowerForms0("Flower forms 0", Vector) = (0,1,2,3)
        [HideInInspector] _GrassFlowerForms1("Flower forms 1", Vector) = (0,0,0,0)
        [HideInInspector] _GrassFlowerCount("Flower palette count", Int) = 0
        [HideInInspector] _GrassFlowerWeights0("Flower weights 0", Vector) = (1,1,1,1)
        [HideInInspector] _GrassFlowerWeights1("Flower weights 1", Vector) = (1,1,1,1)
        [HideInInspector] _GrassFlowerColor0("Flower color 0", Vector) = (1,1,1,1)
        [HideInInspector] _GrassFlowerColor1("Flower color 1", Vector) = (1,1,1,1)
        [HideInInspector] _GrassFlowerColor2("Flower color 2", Vector) = (1,1,1,1)
        [HideInInspector] _GrassFlowerColor3("Flower color 3", Vector) = (1,1,1,1)
        [HideInInspector] _GrassFlowerColor4("Flower color 4", Vector) = (1,1,1,1)
        [HideInInspector] _GrassFlowerColor5("Flower color 5", Vector) = (1,1,1,1)
        [HideInInspector] _GrassFlowerColor6("Flower color 6", Vector) = (1,1,1,1)
        [HideInInspector] _GrassFlowerColor7("Flower color 7", Vector) = (1,1,1,1)
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
            HLSLPROGRAM
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
            #pragma vertex SplatmapVert
            #pragma fragment MatteFragment
            #include_with_pragmas "TerrainMatteKeywords.hlsl"
#define TERRAIN_SPLAT_BASEPASS 1
            #include "TerrainMattePasses.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "MatteDepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On
            HLSLPROGRAM
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
            #pragma vertex SplatmapVert
            #pragma fragment MatteDepthNormals
            #include_with_pragmas "TerrainMatteKeywords.hlsl"
#define TERRAIN_SPLAT_BASEPASS 1
            #include "TerrainMattePasses.hlsl"
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Terrain/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Terrain/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Terrain/Lit/SceneSelectionPass"
        UsePass "Hidden/Nature/Terrain/Utilities/PICKING"
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
