Shader "TacticalMaps/Trees/Impostor"
{
    Properties
    {
        _BaseMap("Albedo Views", 2D) = "white" {}
        _NormalMap("Root-space Normal Views", 2D) = "gray" {}
        _Capture("Local Centre and Radius", Vector) = (0, 5, 0, 10)
        _Atlas("Columns, Rows, Tile Pixels, Padding", Vector) = (16, 5, 256, 4)
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.3
        _Tint("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Pass
        {
            Name "TreeImpostor"
            Tags { "LightMode"="UniversalForward" }
            Cull Off ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "TacticalShadowSampling.hlsl"
            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
            #include "UnityIndirect.cginc"
            #include "TreeDither.hlsl"

            struct Tree { float4x4 objectToWorld; float4 sphere; };
            StructuredBuffer<Tree> _Trees;
            StructuredBuffer<uint> _Visible;
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            float _TreeSkipShadowChecks;
            CBUFFER_START(UnityPerMaterial)
                float4 _Capture, _Atlas, _Tint;
                float _Cutoff;
            CBUFFER_END
            float4 _ViewerPosition, _ViewerDirection;
            float _Orthographic, _ProjectionScale, _PixelHeight;
            float4 _MeshTransition;
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                nointerpolation float2 view : TEXCOORD1;
                half fog : TEXCOORD2;
                nointerpolation float pixelHeight : TEXCOORD3;
                nointerpolation float3 normalX : TEXCOORD4;
                nointerpolation float3 normalY : TEXCOORD5;
                nointerpolation float3 normalZ : TEXCOORD6;
                float3 positionWS : TEXCOORD7;
            };
            Varyings Vert(Attributes input, uint instanceId : SV_InstanceID)
            {
                InitIndirectDrawArgs(0);
                Tree tree = _Trees[_Visible[GetIndirectInstanceID(instanceId)]];
                float3 centreWS = mul(tree.objectToWorld, float4(_Capture.xyz, 1)).xyz;
                float3 directionWS = _Orthographic > 0.5 ? _ViewerDirection.xyz : _ViewerPosition.xyz - centreWS;
                float3 x = float3(tree.objectToWorld._m00, tree.objectToWorld._m10, tree.objectToWorld._m20);
                float3 y = float3(tree.objectToWorld._m01, tree.objectToWorld._m11, tree.objectToWorld._m21);
                float3 z = float3(tree.objectToWorld._m02, tree.objectToWorld._m12, tree.objectToWorld._m22);
                float3 forward = normalize(float3(dot(directionWS,x)/dot(x,x), dot(directionWS,y)/dot(y,y), dot(directionWS,z)/dot(z,z)));
                // The bake camera looks TOWARD the tree (-forward). Its screen-right is
                // cross(forward, up), not cross(up, forward); the latter mirrors the atlas.
                float3 right = normalize(cross(forward, abs(forward.y) > 0.999 ? float3(0,0,-1) : float3(0,1,0)));
                float3 up = cross(right, forward);
                float3 local = _Capture.xyz + (right * input.positionOS.x + up * input.positionOS.y) * _Capture.w;
                float3 world = mul(tree.objectToWorld, float4(local,1)).xyz;
                Varyings output;
                output.positionCS = TransformWorldToHClip(world);
                output.uv = input.uv;
                float angle = atan2(forward.x, forward.z) * (1.0 / (2 * PI));
                float elevation = asin(saturate(forward.y)) * (180.0 / PI);
                output.view = float2(frac(angle) * _Atlas.x, clamp(elevation / 20.0, 0, _Atlas.y-1));
                // Inverse-transpose for non-uniformly scaled, yaw-rotated tree instances.
                output.normalX = x / dot(x,x); output.normalY = y / dot(y,y); output.normalZ = z / dot(z,z);
                output.positionWS = world;
                output.fog = ComputeFogFactor(output.positionCS.z);
                output.pixelHeight = tree.sphere.w * _ProjectionScale * _PixelHeight / (_Orthographic > 0.5 ? 1 : max(length(tree.sphere.xyz - _ViewerPosition.xyz),0.01));
                return output;
            }
            half4 SampleView(float2 uv, float column, float row, out half3 normal)
            {
                float2 tile = float2(fmod(column + _Atlas.x, _Atlas.x), row);
                // Bake cameras reserve the same inset. Keep each sample inside its tile.
                float inset = 0.5 / _Atlas.z;
                uv = clamp(uv, inset, 1-inset);
                float2 atlasUV = (uv + tile) / _Atlas.xy;
                half4 texel = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, atlasUV);
                half4 n = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, atlasUV);
                normal = (n.rgb * 2 - 1) * texel.a;
                texel.rgb *= texel.a;
                return texel;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                if (_MeshTransition.z > 0.5)
                {
                    float height = input.pixelHeight / max(_PixelHeight, 1);
                    float meshCoverage = saturate((height - _MeshTransition.x) / max(_MeshTransition.y - _MeshTransition.x, 0.00001));
                    if (meshCoverage >= 1) discard;
                    if (meshCoverage > 0) TreeDitherClip(input.positionCS.xy, -meshCoverage);
                }
                float2 baseView = floor(input.view), blend = frac(input.view);
                float nextRow = min(baseView.y + 1, _Atlas.y-1);
                half4 color;
                half3 normalOS;
                [branch] if (input.pixelHeight < 16)
                    color = SampleView(input.uv, floor(input.view.x+0.5), min(floor(input.view.y+0.5),_Atlas.y-1), normalOS);
                else
                {
                    half3 n0,n1,n2,n3;
                    half4 c0 = SampleView(input.uv,baseView.x,baseView.y,n0), c1 = SampleView(input.uv,baseView.x+1,baseView.y,n1);
                    half4 c2 = SampleView(input.uv,baseView.x,nextRow,n2), c3 = SampleView(input.uv,baseView.x+1,nextRow,n3);
                    half4 a = lerp(c0,c1,blend.x), b = lerp(c2,c3,blend.x);
                    color = lerp(a,b,blend.y);
                    normalOS = lerp(lerp(n0,n1,blend.x),lerp(n2,n3,blend.x),blend.y);
                }
                clip(color.a - _Cutoff);
                color.rgb /= max(color.a,0.001);
                half3 normal = normalize(input.normalX * normalOS.x + input.normalY * normalOS.y + input.normalZ * normalOS.z);
                Light main = TacticalVegetationLight(input.positionWS, _TreeSkipShadowChecks, 2);
                half3 diffuse = SampleSH(normal) + main.color * (saturate(dot(normal,main.direction)) * main.distanceAttenuation * main.shadowAttenuation);
                #if defined(_ADDITIONAL_LIGHTS)
                    InputData inputData = (InputData)0;
                    inputData.positionWS = input.positionWS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                    #if USE_CLUSTER_LIGHT_LOOP
                        UNITY_LOOP for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                        { Light light = GetAdditionalLight(lightIndex, input.positionWS); diffuse += light.color * saturate(dot(normal,light.direction)) * light.distanceAttenuation * light.shadowAttenuation; }
                    #endif
                    uint count = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(count)
                        Light light = GetAdditionalLight(lightIndex,input.positionWS);
                        diffuse += light.color * saturate(dot(normal,light.direction)) * light.distanceAttenuation * light.shadowAttenuation;
                    LIGHT_LOOP_END
                #endif
                color.rgb = MixFog(color.rgb * _Tint.rgb * diffuse, input.fog);
                return half4(color.rgb,1);
            }
            ENDHLSL
        }
    }
}


