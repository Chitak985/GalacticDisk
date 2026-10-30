Shader "GalacticDisk/Volume"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "DisableBatching"="True" }
        // Premultiplied light plus transmittance. Back faces also work from inside the box.
        Blend One OneMinusSrcAlpha
        Cull Front ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float3 _Extents;
            float _GalaxyRadius, _DiskThickness, _ArmCount, _ArmTightness, _ArmWidth;
            float _IsRing, _InnerRingRadius;
            float _InterarmFraction, _ArmConcentration, _BulgeFraction, _ScaleLength;
            float3 _BulgeScale;
            float4 _ColorBulge, _ColorArms, _ColorInterarm, _ColorHighlight;
            float _HighlightChance, _Intensity, _DustOpacity, _NoiseFrequency, _Seed;
            float _LogarithmicArms, _ArmPitchAngle, _ArmStartRadius, _SecondaryArmStrength, _ArmIrregularity;
            float _BarFraction;
            float4 _BarScale, _DustColor, _NebulaColor, _YoungStarColor;
            float _DustScattering, _NebulaIntensity, _YoungStarIntensity;
            sampler3D _CloudField;
            float _Exposure, _UseSceneDepth;
            float4x4 _GalaxyToView;
            float3 _GalaxyCameraPosition, _GalaxyViewDirection;
            float _GalaxyWorldScale;
            int _Steps;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            struct v2f { float4 vertex : SV_POSITION; float3 world : TEXCOORD0; float4 screen : TEXCOORD1; };

            v2f vert(float4 vertex : POSITION)
            {
                v2f o;
                float3 view = mul(_GalaxyToView, float4(vertex.xyz * 2.0 * _Extents, 1)).xyz;
                o.vertex = mul(UNITY_MATRIX_P, float4(view, 1));
                if (unity_OrthoParams.w > 0.5) o.vertex.xy *= _GalaxyWorldScale;
                // Scaled-space galaxies can extend beyond KSP's camera far plane.
                #if defined(UNITY_REVERSED_Z)
                    o.vertex.z = o.vertex.w * 0.000001;
                #else
                    o.vertex.z = o.vertex.w * 0.999999;
                #endif
                o.world = vertex.xyz;
                o.screen = ComputeScreenPos(o.vertex);
                return o;
            }
            float hash(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }
            // Periodic nearest-arm distance in radians. Matches the stellar winding law.
            float armDistance(float angle)
            {
                float count = max(1.0, _ArmCount);
                return abs(frac(angle * count / 6.2831853 + 0.5) - 0.5) * 6.2831853 / count;
            }
            void density(float3 p, out float3 emission, out float3 extinction)
            {
                if (_IsRing > 0.5)
                {
                    float radius = length(p.xz);
                    emission = 0; extinction = 0;
                    if (radius < _InnerRingRadius || radius > _GalaxyRadius) return;
                    float height = max(_DiskThickness * 0.22, _GalaxyRadius * 0.00015);
                    float vertical = exp(-0.5 * p.y * p.y / (height * height));
                    float rim = 1.0 - smoothstep(_GalaxyRadius - (_GalaxyRadius - _InnerRingRadius) * 0.1, _GalaxyRadius, radius);
                    float radial = exp(-(radius - _InnerRingRadius) / (_GalaxyRadius * _ScaleLength)) * rim;
                    float4 cloud = tex3Dlod(_CloudField, float4(p / (2.0 * _Extents) + 0.5, 0));
                    float fog = radial * vertical * lerp(0.8, 1.4, cloud.a);
                    emission = _ColorInterarm.rgb * _ColorInterarm.a * fog * _Intensity;
                    float dust = cloud.r * rim;
                    emission += dust * _DustColor.rgb * _DustColor.a * _DustScattering * 2.0;
                    float3 tint = _DustColor.rgb / max(0.001, max(_DustColor.r, max(_DustColor.g, _DustColor.b)));
                    extinction = dust * _DustOpacity * _DustColor.a * (1.0 - tint * 0.55);
                    extinction += fog * _ColorInterarm.a * 0.15;
                    return;
                }
                float r = length(p.xz) / _GalaxyRadius;
                float rim = 1.0 - smoothstep(0.82, 1.0, r);
                float height = max(_DiskThickness * 0.22, _GalaxyRadius * 0.00015);
                float vertical = exp(-0.5 * p.y * p.y / (height * height));
                float radial = exp(-r / _ScaleLength) * rim;
                float winding = _LogarithmicArms > 0.5 ?
                    log(max(r, _ArmStartRadius) / _ArmStartRadius) / tan(_ArmPitchAngle) * _ArmTightness :
                    r * _ArmTightness * 6.2831853;
                winding += _BarScale.w + _ArmIrregularity * (sin(r * 19.0) + 0.5 * sin(r * 47.0));
                float phase = atan2(p.z, p.x) - winding;
                float width = max(0.003, _ArmWidth * lerp(0.6, 0.05, _ArmConcentration));
                float d = armDistance(phase);
                float nearest = floor(phase * max(1.0, _ArmCount) / 6.2831853 + 0.5);
                nearest = frac(nearest / max(1.0, _ArmCount)) * max(1.0, _ArmCount);
                float alternate = step(0.5, frac(nearest * 0.5) * 2.0);
                float arm = exp(-0.5 * d * d / (width * width)) * lerp(1.0, _SecondaryArmStrength, alternate);
                if (_LogarithmicArms > 0.5) arm *= smoothstep(_ArmStartRadius * 0.65, _ArmStartRadius * 1.1, r);
                float armWeight = _ArmCount > 0.5 ? 1.0 - _InterarmFraction : 0.0;
                float4 cloud = tex3Dlod(_CloudField, float4(p / (2.0 * _Extents) + 0.5, 0));
                float n = cloud.a;
                float structure = 0.2 + pow(saturate((n - 0.15) * 1.6), 2.0) * 1.5;
                float disk = radial * vertical * (1.0 - _BulgeFraction);
                float arms = disk * arm * armWeight * structure * 6.0;
                float between = disk * (0.75 + (1.0 - armWeight) * 0.65) * lerp(0.8, 1.4, n);
                float3 scaled = p / max(_BulgeScale, float3(0.001, 0.001, 0.001));
                float bulge = exp(-0.5 * dot(scaled, scaled)) * _BulgeFraction * (1.0 - _BarFraction) * 2.0;
                float sn = sin(_BarScale.w), cs = cos(_BarScale.w);
                float3 barPosition = float3(cs * p.x + sn * p.z, p.y, -sn * p.x + cs * p.z) / max(_BarScale.xyz, 0.001);
                float bar = exp(-0.5 * dot(barPosition, barPosition)) * _BulgeFraction * _BarFraction * 2.2;
                float highlight = _HighlightChance > 0.0 ? smoothstep(1.0 - sqrt(_HighlightChance) * 0.6, 1.0, n) * armWeight : 0.0;
                float4 armColor = lerp(_ColorArms, _ColorHighlight, highlight);
                emission = (_ColorBulge.rgb * _ColorBulge.a * (bulge + bar) +
                    armColor.rgb * armColor.a * arms +
                    _ColorInterarm.rgb * _ColorInterarm.a * between) * _Intensity;
                emission += cloud.g * _NebulaColor.rgb * _NebulaColor.a * _NebulaIntensity * 12.0;
                emission += cloud.b * _YoungStarColor.rgb * _YoungStarColor.a * _YoungStarIntensity * 6.0;
                float dust = cloud.r * rim;
                emission += dust * _DustColor.rgb * _DustColor.a * _DustScattering * 2.0;
                float3 dustTint = _DustColor.rgb / max(0.001, max(_DustColor.r, max(_DustColor.g, _DustColor.b)));
                extinction = dust * _DustOpacity * _DustColor.a * (1.0 - dustTint * 0.55);
                extinction += (bulge * _ColorBulge.a + arms * armColor.a + between * _ColorInterarm.a) * 0.15;
            }
            float4 frag(v2f i) : SV_Target
            {
                float3 rayGalaxy = normalize(i.world * 2.0 * _Extents - _GalaxyCameraPosition);
                float3 originGalaxy = _GalaxyCameraPosition;
                if (unity_OrthoParams.w > 0.5)
                {
                    rayGalaxy = normalize(_GalaxyViewDirection);
                    float3 samplePoint = i.world * 2.0 * _Extents;
                    originGalaxy = samplePoint - rayGalaxy * dot(samplePoint - _GalaxyCameraPosition, rayGalaxy);
                }
                float3 ro = originGalaxy / (2.0 * _Extents);
                float3 rd = normalize(rayGalaxy / (2.0 * _Extents));
                float3 safe = lerp(-1.0, 1.0, step(0.0, rd)) * max(abs(rd), 0.000001);
                float3 t0 = (-0.5 - ro) / safe, t1 = (0.5 - ro) / safe;
                float3 nearV = min(t0, t1), farV = max(t0, t1);
                float nearT = max(0.0, max(nearV.x, max(nearV.y, nearV.z)));
                float farT = min(farV.x, min(farV.y, farV.z));

                // Stop at opaque scene geometry when the camera supplies a depth texture.
                float rawDepth = SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screen));
                float eyeDepth = LinearEyeDepth(rawDepth);
                float3 viewDirection = mul((float3x3)_GalaxyToView, rayGalaxy);
                float distanceToOpaque = (eyeDepth / _GalaxyWorldScale) / max(0.0001, -viewDirection.z);
                float3 opaqueGalaxy = _GalaxyCameraPosition + rayGalaxy * distanceToOpaque;
                if (_UseSceneDepth > 0.5 && eyeDepth > _ProjectionParams.y * 1.001 &&
                    eyeDepth < _ProjectionParams.z * 0.999 && unity_OrthoParams.w < 0.5)
                    farT = min(farT, dot(opaqueGalaxy / (2.0 * _Extents) - ro, rd));
                if (farT <= nearT) return 0;

                int steps = clamp(_Steps, 32, 256);
                float stepT = (farT - nearT) / steps;
                float ds = length(rd * 2.0 * _Extents) * stepT / _GalaxyRadius;
                float jitter = hash(float3(floor(i.vertex.xy), 0.123));
                float3 p = ro + rd * (nearT + stepT * (0.25 + 0.5 * jitter));
                float3 light = 0;
                float3 transmission = 1.0;
                [loop] for (int s = 0; s < 256; s++)
                {
                    if (s >= steps || max(transmission.r, max(transmission.g, transmission.b)) < 0.008) break;
                    float3 emissive; float3 absorption;
                    density(p * 2.0 * _Extents, emissive, absorption);
                    float3 segment = exp(-absorption * ds);
                    float3 integral = (1.0 - segment) / max(absorption, 0.00001);
                    integral = lerp(ds, integral, step(0.0001, absorption));
                    light += transmission * emissive * integral;
                    transmission *= segment;
                    p += rd * stepT;
                }
                light *= _Exposure;
                light = saturate((light * (2.51 * light + 0.03)) / (light * (2.43 * light + 0.59) + 0.14));
                return float4(light, 1.0 - dot(transmission, float3(0.333333, 0.333333, 0.333333)));
            }
            ENDCG
        }
    }
    Fallback Off
}
