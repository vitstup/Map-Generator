Shader "TacticalMaps/Agriculture/Water"
{
    Properties
    {
        _ShallowColor("Shallow muddy water",Color)=(.34,.39,.28,1)
        _DeepColor("Deeper water",Color)=(.17,.29,.26,1)
        _SkyTint("Soft sky reflection",Color)=(.52,.65,.70,1)
        _RippleStrength("Low poly ripple strength",Range(0,.1))=.045
        _Opacity("Water opacity",Range(0,1))=.68
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
            Offset -1,-2
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _ShallowColor,_DeepColor,_SkyTint;
            half _RippleStrength,_Opacity;
            CBUFFER_END
            struct A { float3 p:POSITION;float4 color:COLOR; };
            struct V { float4 p:SV_POSITION;float3 world:TEXCOORD0;float fog:TEXCOORD1;half2 depth:TEXCOORD2; };
            V Vert(A a){V o;o.world=TransformObjectToWorld(a.p);o.p=TransformWorldToHClip(o.world);o.fog=ComputeFogFactor(o.p.z);o.depth=a.color.rg;return o;}
            half4 Frag(V i):SV_Target
            {
                float range=1-smoothstep(300,650,distance(i.world,_WorldSpaceCameraPos));clip(range-.01);
                // Triangular facets are stable in world space; no reflection camera, depth or opaque-color copy.
                float2 lattice=float2(i.world.x*.1+i.world.z*.057735,i.world.z*.11547);
                float2 cell=floor(lattice);float facet=step(1,frac(lattice.x)+frac(lattice.y));
                float seed=frac(sin(dot(cell,float2(127.1,311.7))+facet*74.7)*43758.5453);
                float wave=sin(i.world.x*.13+i.world.z*.09+_Time.y*.55);
                half3 normal=normalize(half3((seed-.5)*_RippleStrength,1,(wave*.4+seed-.5)*_RippleStrength));
                half3 view=normalize(_WorldSpaceCameraPos-i.world);half f=1-saturate(dot(view,normal));f=f*f*f;
                half depth=saturate(i.depth.x*2.5);half variation=lerp(.95,1.05,i.depth.y);
                Light sun=GetMainLight();half3 color=lerp(_ShallowColor.rgb,_DeepColor.rgb,depth*.75)*variation;
                color*=(.8+.2*saturate(dot(normal,sun.direction)))*lerp(.88,1.12,seed);
                color=lerp(color,_SkyTint.rgb,f*.55);
                half shine=pow(saturate(dot(normal,normalize(sun.direction+view))),48)*.16;
                half glint=smoothstep(.97,1,sin(i.world.x*.7+i.world.z*.3+_Time.y*.4)*sin(i.world.z*.8-i.world.x*.2+_Time.y*.25));
                color+=sun.color*shine+_SkyTint.rgb*glint*.07;
                return half4(MixFog(color,i.fog),lerp(_Opacity*.65,_Opacity,depth)*range);
            }
            ENDHLSL
        }
    }
}
