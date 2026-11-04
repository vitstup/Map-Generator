#ifndef TACTICAL_SHADOW_SAMPLING_INCLUDED
#define TACTICAL_SHADOW_SAMPLING_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

// Zero during normal rendering; opt-in development-player A/B diagnostics only.
float _ShadowReferenceSampling;
float _TerrainSkipShadowChecks;

half TacticalMainLightShadow(float4 coord, float3 world, half4 shadowMask, half4 probeChannels)
{
    half result = 1;
    [branch] if (_ShadowReferenceSampling > .5 && _TerrainSkipShadowChecks < .5)
        result = MainLightShadow(coord, world, shadowMask, probeChannels);
    else
    {
        half fade = 1, realtime = 1, baked = 1;
        #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
            fade = GetMainLightShadowFade(world);
            // URP samples before fading. Distant pixels skip the entire PCF lookup.
            [branch] if (fade < 1 && _TerrainSkipShadowChecks < .5)
                realtime = MainLightRealtimeShadow(coord);
        #endif
        #if defined(CALCULATE_BAKED_SHADOWS)
            baked = BakedShadow(shadowMask, probeChannels, GetMainLightShadowParams());
        #endif
        result = MixRealtimeAndBakedShadows(realtime, baked, fade);
    }
    return result;
}

// Scope the override to URP's light accessor, preserving GI, cookies and AO.
// Shadows.hlsl is already included; its original implementation stays intact.
#define MainLightShadow TacticalMainLightShadow
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#undef MainLightShadow

Light TacticalVegetationLight(float3 world, float skipShadows, int filtering)
{
    Light light = GetMainLight();
    #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
        [branch] if (skipShadows < .5 && (filtering > 0 || _ShadowReferenceSampling > .5))
        {
            [branch] if (_ShadowReferenceSampling > .5)
                light.shadowAttenuation = MainLightRealtimeShadow(TransformWorldToShadowCoord(world));
            else
            {
                half fade = GetMainLightShadowFade(world);
                [branch] if (fade < 1)
                {
                    float4 coord = TransformWorldToShadowCoord(world);
                    half shadow = 1;
                    #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                        shadow = MainLightRealtimeShadow(coord);
                    #else
                        [branch] if (filtering == 1)
                        {
                            // One hardware 2x2 compare, rather than PCF at every grass fragment.
                            half sampled = SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture, sampler_LinearClampCompare, coord.xyz);
                            shadow = BEYOND_SHADOW_FAR(coord) ? 1 : LerpWhiteTo(sampled, GetMainLightShadowParams().x);
                        }
                        else shadow = MainLightRealtimeShadow(coord);
                    #endif
                    light.shadowAttenuation = lerp(shadow, 1, fade);
                }
            }
        }
    #endif
    return light;
}
#endif
