Shader "TacticalMaps/Trees/Impostor"
{
    Properties
    {
        _BaseMap("Baked Views", 2D) = "white" {}
        _Capture("Local Centre and Radius", Vector) = (0, 5, 0, 10)
        _Atlas("Columns, Rows, Tile Pixels, Padding", Vector) = (8, 3, 256, 4)
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
            #include "UnityIndirect.cginc"

            struct Tree { float4x4 objectToWorld; float4 sphere; };
            StructuredBuffer<Tree> _Trees;
            StructuredBuffer<uint> _Visible;
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _Capture, _Atlas, _Tint;
                float _Cutoff;
            CBUFFER_END
            float4 _ViewerPosition, _ViewerDirection;
            float _Orthographic, _ProjectionScale, _PixelHeight;
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                nointerpolation float2 view : TEXCOORD1;
                half fog : TEXCOORD2;
                nointerpolation half pixelHeight : TEXCOORD3;
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
                float3 right = normalize(cross(abs(forward.y) > 0.999 ? float3(0,0,-1) : float3(0,1,0), forward));
                float3 up = cross(forward, right);
                float3 local = _Capture.xyz + (right * input.positionOS.x + up * input.positionOS.y) * _Capture.w;
                float3 world = mul(tree.objectToWorld, float4(local,1)).xyz;
                Varyings output;
                output.positionCS = TransformWorldToHClip(world);
                output.uv = input.uv;
                float angle = atan2(forward.x, forward.z) * (1.0 / (2 * PI));
                float elevation = asin(saturate(forward.y)) * (180.0 / PI);
                output.view = float2(frac(angle) * _Atlas.x, clamp(elevation / 40.0, 0, _Atlas.y-1));
                output.fog = ComputeFogFactor(output.positionCS.z);
                output.pixelHeight = tree.sphere.w * _ProjectionScale * _PixelHeight / (_Orthographic > 0.5 ? 1 : max(length(tree.sphere.xyz - _ViewerPosition.xyz),0.01));
                return output;
            }
            half4 SampleView(float2 uv, float column, float row)
            {
                float2 tile = float2(fmod(column + _Atlas.x, _Atlas.x), row);
                // Bake cameras reserve the same inset. Keep each sample inside its tile.
                float inset = 0.5 / _Atlas.z;
                uv = clamp(uv, inset, 1-inset);
                half4 texel = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, (uv + tile) / _Atlas.xy);
                texel.rgb *= texel.a;
                return texel;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 baseView = floor(input.view), blend = frac(input.view);
                float nextRow = min(baseView.y + 1, _Atlas.y-1);
                half4 color;
                [branch] if (input.pixelHeight < 16)
                    color = SampleView(input.uv, floor(input.view.x+0.5), min(floor(input.view.y+0.5),_Atlas.y-1));
                else
                {
                    half4 a = lerp(SampleView(input.uv,baseView.x,baseView.y), SampleView(input.uv,baseView.x+1,baseView.y), blend.x);
                    half4 b = lerp(SampleView(input.uv,baseView.x,nextRow), SampleView(input.uv,baseView.x+1,nextRow), blend.x);
                    color = lerp(a,b,blend.y);
                }
                clip(color.a - _Cutoff);
                color.rgb /= max(color.a,0.001);
                color.rgb = MixFog(color.rgb * _Tint.rgb, input.fog);
                return half4(color.rgb,1);
            }
            ENDHLSL
        }
    }
}


