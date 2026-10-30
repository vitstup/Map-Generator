#ifndef TACTICAL_TREE_FOLIAGE_INCLUDED
#define TACTICAL_TREE_FOLIAGE_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "TacticalShadowSampling.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#include "TreeDither.hlsl"
TEXTURE2D(_LeafMap); SAMPLER(sampler_LeafMap);
TEXTURE2D(_TrunkMap); SAMPLER(sampler_TrunkMap);
TEXTURE2D(_LeafNormal); SAMPLER(sampler_LeafNormal);
CBUFFER_START(UnityPerMaterial)
    float4 _LeafUV, _TrunkUV, _LeafColor, _TrunkColor, _FrostColor;
    float _Cutoff, _FrostHeight, _FrostFalloff, _WindStrength, _WindFadeStart, _WindFadeEnd, _NormalStrength;
CBUFFER_END
UNITY_INSTANCING_BUFFER_START(TreeProperties)
    UNITY_DEFINE_INSTANCED_PROP(float, _TreeFade)
UNITY_INSTANCING_BUFFER_END(TreeProperties)
float3 _LightDirection;
float3 _LightPosition;
float _TreeSkipShadowChecks;
struct TreeAttributes
{
    float3 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 uv : TEXCOORD0;
    half4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct TreeVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    half3 normalWS : TEXCOORD2;
    half4 tint : TEXCOORD3;
    half fog : TEXCOORD4;
    half4 tangentWS : TEXCOORD5;
    half frost : TEXCOORD6;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};
float3 TreePosition(TreeAttributes input)
{
    float3 world = TransformObjectToWorld(input.positionOS);
    // One inexpensive gust, shared by the visible, depth and shadow passes.
    float3 root = TransformObjectToWorld(float3(0,0,0));
    float distanceToEye = length(root - _WorldSpaceCameraPos);
    float fade = 1 - smoothstep(_WindFadeStart, _WindFadeEnd, distanceToEye);
    float phase = _Time.y * 1.6 + dot(root.xz, float2(0.17, 0.23));
    float weight = saturate(input.color.r) * saturate(input.positionOS.y * 0.35);
    world.xz += float2(sin(phase + input.positionOS.y * 0.7), cos(phase * 0.83)) * (_WindStrength * weight * fade);
    return world;
}
TreeVaryings TreeVert(TreeAttributes input)
{
    TreeVaryings output = (TreeVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    output.positionWS = TreePosition(input);
    output.positionCS = TransformWorldToHClip(output.positionWS);
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    half leaf = step(0.5001, input.color.b);
    output.uv = input.uv * lerp(_TrunkUV.xy, _LeafUV.xy, leaf) + lerp(_TrunkUV.zw, _LeafUV.zw, leaf);
    half frost = saturate(pow(saturate(input.normalOS.y), max(0.01, _FrostHeight)) * _FrostFalloff) * leaf;
    output.tint = half4(lerp(_TrunkColor.rgb, _LeafColor.rgb, leaf), leaf);
    output.frost = frost;
    output.tangentWS = half4(TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w * GetOddNegativeScale());
    output.fog = ComputeFogFactor(output.positionCS.z);
    return output;
}
half4 TreeSample(TreeVaryings input)
{
    // Blue vertex channel is Synty's leaf/trunk selector; a coherent branch avoids a second fetch.
    half4 texel = 0;
    [branch] if (input.tint.a > 0.5) texel = SAMPLE_TEXTURE2D(_LeafMap, sampler_LeafMap, input.uv);
    else texel = SAMPLE_TEXTURE2D(_TrunkMap, sampler_TrunkMap, input.uv);
    return texel;
}
void TreeClip(TreeVaryings input, half alpha)
{
    clip(alpha - _Cutoff);
    TreeDitherClip(input.positionCS.xy, UNITY_ACCESS_INSTANCED_PROP(TreeProperties, _TreeFade));
}
half4 TreeForward(TreeVaryings input, FRONT_FACE_TYPE front : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    half4 texel = TreeSample(input);
    TreeClip(input, texel.a);
    half3 normal = normalize(input.normalWS);
    #if defined(_TREE_LEAF_NORMAL)
        if (input.tint.a > 0.5)
        {
            half3 tangent = normalize(input.tangentWS.xyz);
            half3 bitangent = cross(normal, tangent) * input.tangentWS.w;
            half3 mapped = UnpackNormalScale(SAMPLE_TEXTURE2D(_LeafNormal, sampler_LeafNormal, input.uv), _NormalStrength);
            normal = normalize(TransformTangentToWorld(mapped, half3x3(tangent, bitangent, normal)));
        }
    #endif
    Light main = TacticalVegetationLight(input.positionWS, _TreeSkipShadowChecks, 2);
    half3 diffuse = SampleSH(normal);
    half ao = 1;
    #if defined(_SCREEN_SPACE_OCCLUSION)
        AmbientOcclusionFactor occlusion = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(input.positionCS));
        diffuse *= occlusion.indirectAmbientOcclusion;
        ao = occlusion.directAmbientOcclusion;
    #endif
    diffuse += main.color * (saturate(dot(normal, main.direction)) * main.distanceAttenuation * main.shadowAttenuation * ao);
    #if defined(_ADDITIONAL_LIGHTS)
        InputData inputData = (InputData)0;
        inputData.positionWS = input.positionWS;
        inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
        #if USE_CLUSTER_LIGHT_LOOP
            UNITY_LOOP for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
            {
                Light light = GetAdditionalLight(lightIndex, input.positionWS);
                diffuse += light.color * (saturate(dot(normal,light.direction)) * light.distanceAttenuation * light.shadowAttenuation);
            }
        #endif
        uint count = GetAdditionalLightsCount();
        LIGHT_LOOP_BEGIN(count)
            Light light = GetAdditionalLight(lightIndex, input.positionWS);
            diffuse += light.color * (saturate(dot(normal,light.direction)) * light.distanceAttenuation * light.shadowAttenuation);
        LIGHT_LOOP_END
    #endif
    half3 albedo = lerp(texel.rgb * input.tint.rgb, _FrostColor.rgb, input.frost);
    return half4(MixFog(albedo * diffuse, input.fog), 1);
}
TreeVaryings TreeShadowVert(TreeAttributes input)
{
    TreeVaryings output = TreeVert(input);
    float3 direction = _LightDirection;
    #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
        direction = normalize(_LightPosition - output.positionWS);
    #endif
    output.positionCS = TransformWorldToHClip(ApplyShadowBias(output.positionWS, output.normalWS, direction));
    output.positionCS = ApplyShadowClamping(output.positionCS);
    return output;
}
half4 TreeDepth(TreeVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    TreeClip(input, TreeSample(input).a);
    return 0;
}
half4 TreeDepthNormals(TreeVaryings input, FRONT_FACE_TYPE front : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    TreeClip(input, TreeSample(input).a);
    half3 normal = normalize(input.normalWS);
    #if defined(_GBUFFER_NORMALS_OCT)
        float2 oct = PackNormalOctQuadEncode(normal);
        return half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)), 0);
    #else
        return half4(normal, 0);
    #endif
}
#endif
