Shader "GalacticDisk/Particles"
{
    Properties
    {
        _MainTex ("Particle texture", 2D) = "white" {}
        _TintColor ("Tint", Color) = (1,1,1,1)
        _OpacityMultiplier ("Opacity multiplier", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "DisableBatching"="True" }
        Blend One OneMinusSrcAlpha
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _TintColor;
            float4x4 _GalaxyToView;
            float _GalaxyWorldScale;
            float _OpacityMultiplier;
            float2 _ParticleSizeLimits;
            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float2 size : TEXCOORD1; };
            struct v2f { float4 vertex : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            v2f vert(appdata v)
            {
                v2f o;
                float3 view = mul(_GalaxyToView, v.vertex).xyz;
                // Match ParticleSystemRenderer's limits, measured against the
                // larger viewport dimension, including close-up particles.
                float projectedSize = abs(UNITY_MATRIX_P[1][1]) * v.size.x * 0.5;
                projectedSize *= unity_OrthoParams.w > 0.5 ? _GalaxyWorldScale : 1.0 / max(abs(view.z), 1e-20);
                float aspect = max(_ScreenParams.x, _ScreenParams.y) / max(1.0, _ScreenParams.y);
                float limited = clamp(projectedSize, _ParticleSizeLimits.x * aspect, _ParticleSizeLimits.y * aspect);
                float sizeScale = projectedSize > 1e-20 ? limited / projectedSize : 1.0;
                view.xy += (v.uv * 2.0 - 1.0) * v.size.x * sizeScale * 0.5;
                o.vertex = mul(UNITY_MATRIX_P, float4(view, 1));
                if (unity_OrthoParams.w > 0.5) o.vertex.xy *= _GalaxyWorldScale;
                o.vertex.z = UNITY_MATRIX_P[2][2] * view.z + UNITY_MATRIX_P[2][3] / _GalaxyWorldScale;
                if (-view.z >= _ProjectionParams.z / _GalaxyWorldScale)
                {
                    #if defined(UNITY_REVERSED_Z)
                        o.vertex.z = o.vertex.w * 0.000001;
                    #else
                        o.vertex.z = o.vertex.w * 0.999999;
                    #endif
                }
                o.color = v.color * _TintColor; o.uv = v.uv;
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float4 color = tex2D(_MainTex, i.uv) * i.color;
                color.a = saturate(color.a * _OpacityMultiplier);
                color.rgb *= color.a;
                return color;
            }
            ENDCG
        }
    }
    Fallback Off
}
