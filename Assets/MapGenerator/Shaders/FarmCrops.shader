Shader "TacticalMaps/Agriculture/Rice"
{
    Properties { _BaseColor("Base color", Color)=(.4,.5,.16,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off ZWrite On
        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        StructuredBuffer<float4> _CropPatches, _CropShapes, _CropStyles;
        StructuredBuffer<uint> _VisibleCrops;
        TEXTURE2D(_CropHeight0); SAMPLER(sampler_CropHeight0);
        TEXTURE2D(_CropHeight1); TEXTURE2D(_CropHeight2);
        float4 _CropRect0,_CropRect1,_CropRect2,_CropVertical0,_CropVertical1,_CropVertical2,_CropRanges;
        float Ground(float2 p)
        {
            int l=all(p>=_CropRect0.xy)&&all(p<_CropRect0.xy+_CropRect0.zw)?0:all(p>=_CropRect1.xy)&&all(p<_CropRect1.xy+_CropRect1.zw)?1:2;
            float4 r=l==0?_CropRect0:l==1?_CropRect1:_CropRect2;
            float4 v=l==0?_CropVertical0:l==1?_CropVertical1:_CropVertical2;
            float2 uv=((p-r.xy)/r.zw*(v.z-1)+.5)/v.z;
            float h=l==0?SAMPLE_TEXTURE2D_LOD(_CropHeight0,sampler_CropHeight0,uv,0).r:l==1?SAMPLE_TEXTURE2D_LOD(_CropHeight1,sampler_CropHeight0,uv,0).r:SAMPLE_TEXTURE2D_LOD(_CropHeight2,sampler_CropHeight0,uv,0).r;
            return v.x+h*2*v.y;
        }
        struct A { float3 p:POSITION; float3 n:NORMAL; float2 uv:TEXCOORD0; float2 root:TEXCOORD1; uint instance:SV_InstanceID; };
        struct V { float4 p:SV_POSITION; float3 world:TEXCOORD0; half3 normal:TEXCOORD1; half3 color:TEXCOORD2; float fade:TEXCOORD3; float fog:TEXCOORD4; };
        float Hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
        V Vert(A a)
        {
            V o; float4 patch=_CropPatches[_VisibleCrops[a.instance]]; float4 style=_CropStyles[(uint)patch.w];
            float2 axis=float2(cos(style.x),sin(style.x)), side=float2(-axis.y,axis.x);
            float2 root=patch.xz+axis*a.root.x+side*a.root.y;
            float noise=Hash(root); float height=lerp(.65,1.0,noise)*(style.y>2.5?.58:1);
            float3 p=a.p; p.y*=height;
            float2 local=p.xz-a.root;
            float2 xz=root+axis*local.x+side*local.y;
            xz+=float2(.7,.3)*sin(_Time.y*1.6+root.x*.17+root.y*.11)*.11*a.uv.y*a.uv.y;
            o.world=float3(xz.x,Ground(root)+p.y,xz.y);
            o.p=TransformWorldToHClip(o.world);
            o.normal=normalize(float3(axis.x*a.n.x+side.x*a.n.z,a.n.y,axis.y*a.n.x+side.y*a.n.z));
            half3 green=lerp(SRGBToLinear(half3(.25,.38,.115)),SRGBToLinear(half3(.43,.54,.21)),style.z);
            half3 gold=lerp(SRGBToLinear(half3(.49,.43,.17)),SRGBToLinear(half3(.66,.57,.28)),style.z);
            o.color=lerp(style.y>1.5&&style.y<2.5?gold:green,SRGBToLinear(half3(.69,.59,.29)),a.uv.y>.8&&style.y>1.5&&style.y<2.5?.7:0)*lerp(.8,1.15,noise)*lerp(.7,1,a.uv.y);
            float d=distance(patch.xyz,_WorldSpaceCameraPos);
            float nearFade=smoothstep(_CropRanges.x-12,_CropRanges.x+12,d);
            o.fade=_CropRanges.z<.5?1-nearFade:nearFade;
            o.fade*=1-smoothstep(_CropRanges.y*.75,_CropRanges.y,d);
            o.fog=ComputeFogFactor(o.p.z); return o;
        }
        half4 Frag(V i):SV_Target
        {
            float rank=Hash(floor(i.p.xy));
            // Complementary pixel ranks replace close plants with the simplified patch.
            if(_CropRanges.z<.5)clip(i.fade-rank);else clip(rank-(1-i.fade));
            Light sun=GetMainLight(TransformWorldToShadowCoord(i.world));
            half diffuse=.4+.6*saturate(abs(dot(normalize(i.normal),sun.direction)));
            half3 color=i.color*(SampleSH(half3(0,1,0))+sun.color*diffuse*sun.shadowAttenuation);
            return half4(MixFog(color,i.fog),1);
        }
        ENDHLSL
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            ENDHLSL
        }
        Pass
        {
            Tags { "LightMode"="DepthOnly" }
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}
