Shader "TacticalMaps/LowPolyWater"
{
    Properties
    {
        _ShallowColor ("Shallow water", Color) = (0.10,0.55,0.62,1)
        _DeepColor ("Deep water", Color) = (0.025,0.28,0.40,1)
        _SkyColor ("Sky reflection tint", Color) = (0.48,0.72,0.80,1)
        _FoamColor ("Shore foam", Color) = (0.78,0.92,0.87,1)
        _WaveAmplitude ("Surface motion strength", Range(0,3)) = 1.2
        _WaveLength ("Broad surface variation, metres", Range(20,400)) = 100
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
        _ShallowOpacity ("Shallow opacity, vertical view", Range(0,1)) = 0.12
        _DeepOpacity ("Deep opacity, vertical view", Range(0,1)) = 0.88
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
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
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half fog : TEXCOORD1; };
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
            // Smooth 2D noise and its analytic gradient. No repeating wave trains.
            float SurfaceNoise(float2 p,out float2 gradient)
            {
                float2 cell=floor(p),f=frac(p);
                float2 u=f*f*f*(f*(f*6-15)+10);
                float2 du=30*f*f*(f-1)*(f-1);
                float a=Hash(cell),b=Hash(cell+float2(1,0));
                float c=Hash(cell+float2(0,1)),d=Hash(cell+1);
                float crossTerm=a-b-c+d;
                gradient=float2(b-a+crossTerm*u.y,c-a+crossTerm*u.x)*du;
                return a+(b-a)*u.x+(c-a)*u.y+crossTerm*u.x*u.y-.5;
            }
            float SurfaceMotion(float2 p,float2 footprint,out float2 slope)
            {
                float time=_Time.y*_WaveSpeed*_FlowSpeed*.18;
                float small=max(12,_RippleSize),large=max(32,_WaveLength*.55);
                float2 g0,g1;
                float nearFilter=1-smoothstep(.1,.5,max(footprint.x,footprint.y)/small);
                float largeFilter=1-smoothstep(.1,.5,max(footprint.x,footprint.y)/large);
                float a=SurfaceNoise((p+float2(time,-time*.37))/small,g0)*nearFilter;
                float2 rotated=float2(p.x*.6-p.y*.8,p.x*.8+p.y*.6);
                float b=SurfaceNoise((rotated+float2(-time*.43,time*.61))/large+37.1,g1)*largeFilter;
                g1=float2(g1.x*.6+g1.y*.8,-g1.x*.8+g1.y*.6);
                slope=(g0*nearFilter*.65+g1*largeFilter*.35)*_RippleStrength*.65*min(_WaveAmplitude,1.5);
                return a*.65+b*.35;
            }
            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 p = TransformObjectToWorld(input.positionOS.xyz);
                float2 surface=p.xz-_MapCenter.xz;
                float2 gradient;
                float movement=SurfaceMotion(surface,0,gradient);
                float regional=SurfaceNoise(surface/140,gradient);
                // A moving waterline and irregular low swells, on the existing mesh.
                // No directional sine trains and no camera-local replacement patch.
                float surge=.16+.11*sin(_Time.y*_WaveSpeed*.72+regional*3);
                p.y = _WaterLevel+_SurfaceLift+(surge+movement*.2)*_WaveAmplitude;
                o.positionWS = p;
                o.positionCS = TransformWorldToHClip(p);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float mapDepth=max(0,_WaterLevel-Ground(input.positionWS.xz));
                float rawDepth=SampleSceneDepth(GetNormalizedScreenSpaceUV(input.positionCS));
                float sceneEye=LinearEyeDepth(rawDepth,_ZBufferParams);
                float waterEye=-TransformWorldToView(input.positionWS).z;
                float contactGap=max(0,sceneEye-waterEye);
                float viewDistance=distance(input.positionWS,_WorldSpaceCameraPos);
                // Actual rendered terrain defines contact, not the reduced bed map.
                // The latter remains the fallback under sky, preserving the horizon fix.
                float hasBed=step(sceneEye,_ProjectionParams.z*.999);
                float depth=lerp(mapDepth,contactGap*abs(view.y),hasBed*(1-smoothstep(250,700,viewDistance)));
                float2 surface = input.positionWS.xz-_MapCenter.xz;
                clip(min(_MapSize2.x*.5-abs(surface.x),_MapSize2.y*.5-abs(surface.y)));
                float2 dx=ddx(surface),dy=ddy(surface),footprint=abs(dx)+abs(dy);
                float2 slope;
                float motion=SurfaceMotion(surface,footprint,slope);
                float detailFade=1-smoothstep(350,1800,viewDistance);
                slope*=detailFade*smoothstep(.05,1,depth);
                half3 normal=normalize(float3(-slope.x,1,-slope.y));
                Light light = GetMainLight();
                half diffuse = saturate(dot(normal,light.direction));
                // At distance the water column dominates the seabed tint. A coarse
                // offshore depth cliff must not draw a polygonal colour boundary.
                float tintDepth=lerp(depth,_DepthColorDistance*2,smoothstep(300,1000,viewDistance));
                half3 color = lerp(_ShallowColor.rgb,_DeepColor.rgb,saturate(tintDepth/max(0.1,_DepthColorDistance)));
                color *= .82 + diffuse * .18;
                color *= 1+motion*_RippleHighlights*.65*detailFade;
                half fresnel = 1-saturate(abs(view.y)); fresnel *= fresnel; fresnel *= fresnel;
                color = lerp(color,_SkyColor.rgb,fresnel*0.45);
                half sun = saturate(dot(reflect(-light.direction,normal),view));
                color += light.color * pow(sun,24) * _GlintStrength * .05 * detailFade;
                float contactWidth=max(.3,_FoamDepth*.35/max(.2,abs(view.y)));
                float shore=1-smoothstep(0,contactWidth*2,contactGap);
                shore*=shore;
                half foam = 0;
                [branch] if (shore > .001)
                {
                    float t=_Time.y*_WaveSpeed;
                    float breakup=FoamNoise(surface*.22+float2(t*.11,-t*.07));
                    float wash=.65+.35*sin(t*.72+FoamNoise(surface*.018)*4);
                    float foamFilter=(1-smoothstep(.5,2.5,max(footprint.x,footprint.y)))*(1-smoothstep(350,900,viewDistance));
                    foam=shore*smoothstep(.28,.7,breakup)*wash*_FoamStrength*1.3*foamFilter;
                }
                color = lerp(color,_FoamColor.rgb,foam);
                half opacity = lerp(_ShallowOpacity,_DeepOpacity,1-exp2(-depth/max(0.1,_AbsorptionDepth)));
                // Beer-Lambert transmission along the viewing ray. The old vertical
                // opacity left the sky/seabed visible even across kilometres of sea.
                // Use the mean water plane, so animated ripple normals cannot make
                // transparency flicker or expose a strip of sky near the horizon.
                float pathLength = rcp(max(0.02,abs(view.y)));
                opacity = 1-pow(saturate(1-opacity),pathLength);
                opacity = lerp(opacity,1,foam);
                // Fade into the real beach over a world-sized contact zone. This
                // removes the hard staircase even at shallow viewing angles.
                opacity*=lerp(smoothstep(0,max(.8,fwidth(contactGap)*1.5),contactGap),1,smoothstep(400,900,viewDistance));
                return half4(MixFog(color,input.fog),opacity);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
