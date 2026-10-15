Shader "TacticalMaps/HeightPreview"
{
    Properties
    {
        [PerRendererData] _MainTex ("Height", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Exposure ("Preview exposure", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            sampler2D _MainTex; float4 _Color; float _Exposure;
            v2f vert(appdata v) { v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color*_Color; return o; }
            fixed4 frag(v2f i) : SV_Target { float h=saturate(tex2D(_MainTex,i.uv).r*_Exposure); return float4(h,h,h,1)*i.color; }
            ENDCG
        }
    }
}
