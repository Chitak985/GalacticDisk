Shader "GalacticDisk/Distant"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "DisableBatching"="True" }
        Blend One OneMinusSrcAlpha
        Cull Off ZWrite Off ZTest LEqual
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _DotPosition;
            float2 _DotSize;
            float4 _DotColor;
            float _DotOpacity;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata v)
            {
                v2f o;
                float4 depth = mul(UNITY_MATRIX_P,float4(0,0,-_DotPosition.z,1));
                o.vertex = float4(_DotPosition.xy + v.vertex.xy*_DotSize,depth.z/depth.w,1);
                o.vertex.y *= sign(UNITY_MATRIX_P[1][1])*sign(_DotPosition.w);
                o.uv = v.uv*2-1;
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float r2 = dot(i.uv,i.uv);
                float glow = exp(-3*r2)*(1-smoothstep(0.65,1,r2))*_DotOpacity;
                return float4(_DotColor.rgb*glow,saturate(_DotColor.a*glow));
            }
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _DotProjectionY;
            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            v2f vert(appdata v)
            {
                v2f o;
                float4 depth = mul(UNITY_MATRIX_P,float4(0,0,-v.vertex.z,1));
                o.vertex = float4(v.vertex.xy,depth.z/depth.w,1);
                o.vertex.y *= sign(UNITY_MATRIX_P[1][1])*sign(_DotProjectionY);
                o.color = v.color; o.uv = v.uv;
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float r2 = dot(i.uv,i.uv);
                float glow = exp(-3*r2)*(1-smoothstep(0.65,1,r2));
                return float4(i.color.rgb*glow,saturate(i.color.a*glow));
            }
            ENDCG
        }
    }
    Fallback Off
}
