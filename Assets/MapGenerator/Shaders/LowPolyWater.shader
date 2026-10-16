Shader "TacticalMaps/LowPolyWater"
{
    Properties
    {
        _ShallowColor ("Shallow water", Color) = (0.10,0.55,0.62,1)
        _DeepColor ("Deep water", Color) = (0.025,0.28,0.40,1)
        _SkyColor ("Sky reflection tint", Color) = (0.48,0.72,0.80,1)
        _FoamColor ("Shore foam", Color) = (0.78,0.92,0.87,1)
        _WaveAmplitude ("Wave height, metres", Range(0,3)) = 1.2
        _WaveLength ("Wave length, metres", Range(20,400)) = 100
        _WaveSpeed ("Wave speed", Range(0,3)) = 1.3
        _RippleSize ("Surface ripple size, metres", Range(2,80)) = 18
        _RippleStrength ("Moving ripple normals", Range(0,0.5)) = 0.16
        _FlowSpeed ("Surface drift, metres per second", Range(0,8)) = 1.8
        _RippleHighlights ("Moving surface highlights", Range(0,1)) = 0.25
        _SurfaceLift ("Surface offset, metres", Range(0.01,0.5)) = 0.2
        _DepthColorDistance ("Deep color depth, metres", Range(1,100)) = 22
        _FoamDepth ("Foam depth, metres", Range(0.1,10)) = 0.7
        _FoamStrength ("Foam strength", Range(0,1)) = 0.35
        _GlintStrength ("Sun glints", Range(0,2)) = 0.55
        _ShallowOpacity ("Shallow opacity", Range(0,1)) = 0.12
        _DeepOpacity ("Deep opacity", Range(0,1)) = 0.88
        _AbsorptionDepth ("Opacity depth, metres", Range(1,100)) = 18
        [HideInInspector] _Depth0 ("Main seabed", 2D) = "black" {}
        [HideInInspector] _Depth1 ("Middle seabed", 2D) = "black" {}
        [HideInInspector] _Depth2 ("Outer seabed", 2D) = "black" {}
        [HideInInspector] _MapCenter ("Map center", Vector) = (0,0,0,0)
        [HideInInspector] _MapSize0 ("Main size", Vector) = (2048,2048,0.00048828125,0.00048828125)
        [HideInInspector] _MapSize1 ("Middle size", Vector) = (8096,8096,0.0001235,0.0001235)
        [HideInInspector] _MapSize2 ("Outer size", Vector) = (20000,20000,0.00005,0.00005)
        [HideInInspector] _HeightRange ("Height range", Float) = 4000
        [HideInInspector] _WaterLevel ("Sea level", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "WaterForward"
            Tags { "LightMode"="UniversalForward" }
            Cull Back ZWrite Off ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_Depth0); SAMPLER(sampler_Depth0);
            TEXTURE2D(_Depth1); SAMPLER(sampler_Depth1);
            TEXTURE2D(_Depth2); SAMPLER(sampler_Depth2);
            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor, _DeepColor, _SkyColor, _FoamColor;
                float _WaveAmplitude, _WaveLength, _WaveSpeed, _SurfaceLift;
                float _RippleSize, _RippleStrength, _FlowSpeed, _RippleHighlights;
                float _DepthColorDistance, _FoamDepth, _FoamStrength, _GlintStrength;
                float _ShallowOpacity, _DeepOpacity, _AbsorptionDepth;
                float4 _MapCenter, _MapSize0, _MapSize1, _MapSize2;
                float _HeightRange, _WaterLevel;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half fog : TEXCOORD1; half crest : TEXCOORD2; };
            float Hash(float2 p)
            {
                float3 q = frac(float3(p.xyx) * 0.1031);
                q += dot(q,q.yzx+33.33);
                return frac((q.x+q.y)*q.z);
            }
            float FoamNoise(float2 p)
            {
                float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y);
            }
            float Ground(float2 position)
            {
                float2 p = position - _MapCenter.xz;
                float h;
                [branch] if (all(abs(p) <= _MapSize0.xy * 0.5))
                    h = SAMPLE_TEXTURE2D_LOD(_Depth0,sampler_Depth0,p*_MapSize0.zw+0.5,0).r;
                else if (all(abs(p) <= _MapSize1.xy * 0.5))
                    h = SAMPLE_TEXTURE2D_LOD(_Depth1,sampler_Depth1,p*_MapSize1.zw+0.5,0).r;
                else h = SAMPLE_TEXTURE2D_LOD(_Depth2,sampler_Depth2,p*_MapSize2.zw+0.5,0).r;
                return _MapCenter.y + h * _HeightRange;
            }
            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 p = TransformObjectToWorld(input.positionOS.xyz);
                float2 q = p.xz - _MapCenter.xz;
                float phase = _Time.y * _WaveSpeed;
                float k = 6.2831853 / max(1,_WaveLength);
                float wave = sin(dot(q,float2(0.8,0.6))*k+phase)*0.65
                           + sin(dot(q,float2(-0.6,0.8))*k*1.73-phase*1.17)*0.35;
                float ground = Ground(p.xz);
                float depth = max(0,_WaterLevel-ground);
                // Waves disappear at the shore instead of being clamped onto nearly coincident ground.
                float amplitude = min(_WaveAmplitude * saturate(depth/4),depth*0.3);
                p.y = _WaterLevel+_SurfaceLift+wave*amplitude;
                o.positionWS = p;
                o.positionCS = TransformWorldToHClip(p);
                o.fog = ComputeFogFactor(o.positionCS.z);
                o.crest = wave;
                return o;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float ground = Ground(input.positionWS.xz);
                // Terrain depth testing handles geometry; this clips uphill water triangles at the shore.
                float depth = max(0,_WaterLevel-ground);
                clip(depth-0.02);
                half3 normal = normalize(cross(ddy(input.positionWS),ddx(input.positionWS)));
                normal *= normal.y < 0 ? -1 : 1;
                float2 surface = input.positionWS.xz-_MapCenter.xz;
                float2 flow = surface-float2(0.8,0.6)*_Time.y*_FlowSpeed;
                float rippleK=6.2831853/max(1,_RippleSize);
                float warp=FoamNoise(flow*0.035)*6;
                float r0=dot(flow,float2(0.8,0.6))*rippleK+warp;
                float r1=dot(flow,float2(-0.45,0.89))*rippleK*1.41-warp;
                float2 perturb=(cos(r0)*float2(0.8,0.6)+cos(r1)*float2(-0.45,0.89)*0.55)*_RippleStrength;
                normal=normalize(normal+float3(perturb.x,0,perturb.y)*saturate(depth/2));
                half3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                Light light = GetMainLight();
                half diffuse = saturate(dot(normal,light.direction));
                half3 color = lerp(_ShallowColor.rgb,_DeepColor.rgb,saturate(depth/max(0.1,_DepthColorDistance)));
                color *= 0.65 + diffuse * 0.35;
                color *= 0.96 + input.crest * 0.10;
                color += _SkyColor.rgb * smoothstep(0.65,0.95,input.crest) * 0.035;
                half movingCrests = smoothstep(0.82,0.99,sin(r0))
                    * smoothstep(0.45,0.75,FoamNoise(flow*float2(0.16,0.13)+float2(91,17)));
                color += _SkyColor.rgb * movingCrests * _RippleHighlights * saturate(depth/2);
                half fresnel = 1-saturate(dot(normal,view)); fresnel *= fresnel; fresnel *= fresnel;
                color = lerp(color,_SkyColor.rgb,fresnel*0.55);
                half sun = saturate(dot(reflect(-light.direction,normal),view));
                color += light.color * smoothstep(0.96,0.998,sun) * _GlintStrength;
                half shore = (1-smoothstep(0,max(0.1,_FoamDepth),depth))*smoothstep(0.02,0.4,depth);
                half foam = 0;
                [branch] if (shore > 0.001)
                    foam = shore * smoothstep(0.4,0.75,FoamNoise((input.positionWS.xz-_MapCenter.xz)*0.08+_Time.y*float2(0.08,0.035))) * _FoamStrength;
                color = lerp(color,_FoamColor.rgb,foam);
                half opacity = lerp(_ShallowOpacity,_DeepOpacity,1-exp2(-depth/max(0.1,_AbsorptionDepth)));
                opacity = lerp(opacity,1,foam);
                return half4(MixFog(color,input.fog),opacity);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
