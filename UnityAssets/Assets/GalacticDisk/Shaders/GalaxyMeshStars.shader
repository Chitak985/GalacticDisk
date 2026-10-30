Shader "GalacticDisk/MeshStars"
{
    Properties { _Brightness ("Brightness", Float) = 1.7 }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" "DisableBatching"="True" }
        Blend One One
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _Brightness;
            float _GalaxyRadius, _DustOpacity;
            float3 _CloudExtents;
            float4 _DustColor;
            float4x4 _GalaxyToView;
            float3 _GalaxyCameraPosition;
            float _GalaxyWorldScale;
            sampler3D _CloudField;
            float _Exposure;
            int _DustSamples;
            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float2 size : TEXCOORD1; };
            struct v2f { float4 vertex : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            v2f vert(appdata v)
            {
                v2f o;
                float3 view = mul(_GalaxyToView, v.vertex).xyz;
                view.xy += (v.uv * 2.0 - 1.0) * v.size.x * 0.5;
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
                o.color = v.color;
                o.uv = v.uv;
                if (_DustSamples > 0 && _DustOpacity > 0 && _DustColor.a > 0)
                {
                    float3 p = v.vertex.xyz;
                    float3 camera = _GalaxyCameraPosition;
                    float3 delta = camera - p;
                    float distance = length(delta);
                    float3 direction = delta / max(distance, 0.00001);
                    float3 safe = lerp(-1.0, 1.0, step(0.0, direction)) * max(abs(direction), 0.00001);
                    float3 exits = max((-_CloudExtents - p) / safe, (_CloudExtents - p) / safe);
                    distance = min(distance, max(0, min(exits.x, min(exits.y, exits.z))));
                    float stepSize = distance / _DustSamples;
                    float column = 0;
                    [loop] for (int s = 0; s < 24; s++)
                    {
                        if (s >= _DustSamples) break;
                        float3 sample = p + direction * ((s + 0.5) * stepSize);
                        column += tex3Dlod(_CloudField, float4(sample / (2.0 * _CloudExtents) + 0.5, 0)).r;
                    }
                    float3 tint = _DustColor.rgb / max(0.001, max(_DustColor.r, max(_DustColor.g, _DustColor.b)));
                    float3 extinction = (1.0 - tint * 0.55) * _DustColor.a * _DustOpacity;
                    o.color.rgb *= exp(-column * stepSize / _GalaxyRadius * extinction);
                }
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float r2 = dot(i.uv * 2.0 - 1.0, i.uv * 2.0 - 1.0);
                float falloff = exp(-r2 * 9.0) + 0.12 * exp(-r2 * 3.5);
                falloff *= 1.0 - smoothstep(0.65, 1.0, r2);
                return float4(i.color.rgb * i.color.a * falloff * _Brightness * _Exposure, 0.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
