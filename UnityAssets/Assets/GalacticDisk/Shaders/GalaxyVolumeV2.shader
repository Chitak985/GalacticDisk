Shader "GalacticDisk/VolumeV2"
{
    Properties
    {
        _OpacityMultiplier ("Opacity multiplier", Float) = 1
        _CloudField ("Cloud density", 3D) = "" {}
        _DetailNoise ("Shared detail noise", 3D) = "" {}
        _DiffuseIntensity ("Diffuse disk intensity", Float) = 1
        _BulgeIntensity ("Bulge intensity", Float) = 1
        _ArmIntensity ("Arm intensity", Float) = 1
        _ArmVariation ("Arm variation", Float) = 0.18
        _BranchStrength ("Arm branch strength", Float) = 0.35
        _FilamentStrength ("Dust filament strength", Float) = 0.65
        _DustDetailFrequency ("Dust detail frequency", Float) = 80
        _GrainStrength ("Stellar grain strength", Float) = 0.35
        _GrainFrequency ("Stellar grain frequency", Float) = 360
    }
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
            float _AnalyticBulge;
            float _InterarmFraction, _ArmConcentration, _BulgeFraction, _ScaleLength;
            float3 _BulgeScale;
            float4 _ColorBulge, _ColorArms, _ColorInterarm, _ColorHighlight;
            float _HighlightChance, _Intensity, _DustOpacity, _NoiseFrequency, _Seed;
            float _DiffuseIntensity, _BulgeIntensity, _ArmIntensity, _ArmVariation, _BranchStrength, _StructureSeed;
            float _FilamentStrength, _DustDetailFrequency, _GrainStrength, _GrainFrequency;
            float4 _ArmPhaseA[12], _ArmPhaseB[12];
            sampler3D _DetailNoise;
            float _LogarithmicArms, _ArmPitchAngle, _ArmStartRadius, _SecondaryArmStrength, _ArmIrregularity;
            float _BarFraction;
            float4 _BarScale, _DustColor, _NebulaColor, _YoungStarColor;
            float _DustScattering, _NebulaIntensity, _YoungStarIntensity;
            sampler3D _CloudField;
            float _Exposure, _UseSceneDepth;
            float4x4 _GalaxyToView;
            float3 _GalaxyCameraPosition, _GalaxyViewDirection;
            float _GalaxyWorldScale;
            float _OpacityMultiplier;
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
            float angularDistance(float a) { return abs(frac(a / 6.2831853 + 0.5) - 0.5) * 6.2831853; }
            float variedArms(float r, float angle, float winding, float width)
            {
                float3 sn=sin(r*float3(11,27,53)), cs=cos(r*float3(11,27,53));
                float sum=0;
                [loop] for(int armIndex=0;armIndex<12;armIndex++)
                {
                    if(armIndex >= _ArmCount) break;
                    float4 a=_ArmPhaseA[armIndex], b=_ArmPhaseB[armIndex];
                    float variation=0.55*(sn.x*a.y+cs.x*a.x)+0.3*(sn.y*a.w+cs.y*a.z)+0.15*(sn.z*b.y+cs.z*b.x);
                    float center=winding+armIndex*6.2831853/max(1.0,_ArmCount)+
                        _ArmVariation*variation*smoothstep(_ArmStartRadius*0.6,0.8,r);
                    float d=angularDistance(angle-center);
                    float amplitude=lerp(1.0,_SecondaryArmStrength,frac(armIndex*0.5)*2.0);
                    amplitude*=1.0+_ArmVariation*0.6*(sn.x*a.y+cs.x*a.x);
                    sum+=exp(-0.5*d*d/(width*width))*amplitude;
                    if(_BranchStrength > 0)
                    {
                        float branchCenter=center+b.w*0.45*smoothstep(b.z,0.95,r);
                        float branchWeight=_BranchStrength*smoothstep(b.z,b.z+0.16,r)*(1.0-smoothstep(0.82,1.0,r));
                        d=angularDistance(angle-branchCenter);
                        sum+=branchWeight*amplitude*exp(-0.5*d*d/(width*width*0.6));
                    }
                }
                return sum;
            }
            float filament(float3 p, float footprint)
            {
                float lod=log2(max(1.0,footprint*_DustDetailFrequency));
                float3 q=p/_GalaxyRadius*(_DustDetailFrequency/64.0)+_StructureSeed*float3(0.017,0.031,0.043);
                float4 coarse=tex3Dlod(_DetailNoise,float4(q*0.27, max(0.0,lod-1.8)));
                float4 medium=tex3Dlod(_DetailNoise,float4(q+(coarse.gbr-0.5)*0.045,lod));
                float fine=tex3Dlod(_DetailNoise,float4(q*2.17+0.137,lod+1.1)).b;
                float ridge=pow(saturate(1.0-abs(medium.r*2.0-1.0)),5.0);
                float threads=0.12+ridge*(0.35+fine*1.3+coarse.a*0.6);
                return lerp(1.0,threads,_FilamentStrength*(1.0-smoothstep(0.5,3.0,lod)));
            }
            float2 grainHash(float2 p)
            {
                float3 h=frac(float3(p.xyx)*float3(0.1031,0.1030,0.0973)+_StructureSeed*0.01337);
                h+=dot(h,h.yzx+33.33);
                return frac((h.xx+h.yz)*h.zy);
            }
            float starGrain(float2 q,float2 dx,float2 dy)
            {
                float2 baseCell=floor(q-0.5); float sum=0;
                // Convolve with the pixel footprint in both directions so
                // inclined disks do not turn star glints into stretched stripes.
                float a=0.012+0.35*(dx.x*dx.x+dy.x*dy.x);
                float b=0.35*(dx.x*dx.y+dy.x*dy.y);
                float c=0.012+0.35*(dx.y*dx.y+dy.y*dy.y);
                float determinant=max(0.000001,a*c-b*b);
                [unroll] for(int x=0;x<2;x++)
                [unroll] for(int z=0;z<2;z++)
                {
                    float2 cell=baseCell+float2(x,z), random=grainHash(cell);
                    float2 offset=q-(cell+0.15+random*0.7);
                    float distance=max(0.0,(c*offset.x*offset.x-2.0*b*offset.x*offset.y+a*offset.y*offset.y)/determinant);
                    sum+=exp(-distance)*min(1.0,0.012/sqrt(determinant))*pow(random.x,6.0);
                }
                return sum;
            }
            void density(float3 p, float footprint, out float3 emission, out float3 extinction)
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
                if (_FilamentStrength > 0) dust *= filament(p,footprint);
                    emission += dust * _DustColor.rgb * _DustColor.a * _DustScattering * 2.0;
                    float3 tint = _DustColor.rgb / max(0.001, max(_DustColor.r, max(_DustColor.g, _DustColor.b)));
                    extinction = dust * _DustOpacity * _DustColor.a * (1.0 - tint * 0.55);
                    extinction += fog * _ColorInterarm.a * 0.15;
                    return;
                }
                // Pure bulge/bar galaxies use no cloud channels. Keep their
                // original Gaussian emission/extinction without a 3D lookup.
                [branch] if (_AnalyticBulge > 0.5)
                {
                    float3 scaled = p / max(_BulgeScale, float3(0.001, 0.001, 0.001));
                    float bulge = exp(-0.5 * dot(scaled, scaled)) * _BulgeFraction * (1.0 - _BarFraction) * 2.0;
                    float sn = sin(_BarScale.w), cs = cos(_BarScale.w);
                    float3 barPosition = float3(cs * p.x + sn * p.z, p.y, -sn * p.x + cs * p.z) / max(_BarScale.xyz, 0.001);
                    float bar = exp(-0.5 * dot(barPosition, barPosition)) * _BulgeFraction * _BarFraction * 2.2;
                    emission = _ColorBulge.rgb * _ColorBulge.a * (bulge + bar) * _Intensity * _BulgeIntensity;
                    extinction = bulge * _ColorBulge.a * 0.15;
                    return;
                }
                float r = length(p.xz) / _GalaxyRadius;
                float rim = 1.0 - smoothstep(0.65, 1.0, r);
                float height = max(_DiskThickness * 0.22, _GalaxyRadius * 0.00015);
                float vertical = exp(-0.5 * p.y * p.y / (height * height));
                float radial = exp(-r / _ScaleLength) * rim;
                float winding = _LogarithmicArms > 0.5 ?
                    log(max(r, _ArmStartRadius) / _ArmStartRadius) / tan(_ArmPitchAngle) * _ArmTightness :
                    r * _ArmTightness * 6.2831853;
                winding += _BarScale.w + _ArmIrregularity * (sin(r * 19.0) + 0.5 * sin(r * 47.0));
                float phase = atan2(p.z, p.x) - winding;
                float width = max(0.003, _ArmWidth * lerp(0.6, 0.05, _ArmConcentration));
                float arm = variedArms(r,atan2(p.z,p.x),winding,width);
                if (_LogarithmicArms > 0.5) arm *= smoothstep(_ArmStartRadius * 0.65, _ArmStartRadius * 1.1, r);
                float armWeight = _ArmCount > 0.5 ? 1.0 - _InterarmFraction : 0.0;
                float4 cloud = tex3Dlod(_CloudField, float4(p / (2.0 * _Extents) + 0.5, 0));
                float n = cloud.a;
                float structure = 0.65 + n * 0.6;
                float disk = radial * vertical * (1.0 - _BulgeFraction);
                float arms = disk * arm * armWeight * structure * 3.2;
                float between = disk * (0.75 + (1.0 - armWeight) * 0.65) * lerp(0.92, 1.08, n) * 1.4;
                float3 scaled = p / max(_BulgeScale, float3(0.001, 0.001, 0.001));
                float bulge = exp(-0.5 * dot(scaled, scaled)) * _BulgeFraction * (1.0 - _BarFraction) * 2.0;
                float sn = sin(_BarScale.w), cs = cos(_BarScale.w);
                float3 barPosition = float3(cs * p.x + sn * p.z, p.y, -sn * p.x + cs * p.z) / max(_BarScale.xyz, 0.001);
                float bar = exp(-0.5 * dot(barPosition, barPosition)) * _BulgeFraction * _BarFraction * 2.2;
                float highlight = _HighlightChance > 0.0 ? smoothstep(1.0 - sqrt(_HighlightChance) * 0.6, 1.0, n) * armWeight : 0.0;
                float4 armColor = lerp(_ColorArms, _ColorHighlight, highlight);
                emission = (_ColorBulge.rgb * _ColorBulge.a * (bulge + bar) * _BulgeIntensity +
                    armColor.rgb * armColor.a * arms * _ArmIntensity +
                    _ColorInterarm.rgb * _ColorInterarm.a * between * _DiffuseIntensity) * _Intensity;
                emission += cloud.g * _NebulaColor.rgb * _NebulaColor.a * _NebulaIntensity * 12.0;
                emission += cloud.b * _YoungStarColor.rgb * _YoungStarColor.a * _YoungStarIntensity * 6.0;
                float dust = cloud.r * rim;
                if (_FilamentStrength > 0) dust *= filament(p,footprint);
                emission += dust * _DustColor.rgb * _DustColor.a * _DustScattering * 2.0;
                float3 dustTint = _DustColor.rgb / max(0.001, max(_DustColor.r, max(_DustColor.g, _DustColor.b)));
                extinction = dust * _DustOpacity * _DustColor.a * (1.0 - dustTint * 0.55);
                extinction += (bulge * _ColorBulge.a + arms * armColor.a + between * _ColorInterarm.a) * 0.15;
            }
            float4 frag(v2f i) : SV_Target
            {
                float3 rayGalaxy = normalize(i.world * 2.0 * _Extents - _GalaxyCameraPosition);
                float rayFootprint=max(length(ddx(rayGalaxy)),length(ddy(rayGalaxy)));
                float orthoFootprint=max(length(ddx(i.world*2.0*_Extents)),length(ddy(i.world*2.0*_Extents)))/_GalaxyRadius;
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
                    float3 samplePosition=p*2.0*_Extents;
                    float footprint=max(ds*0.5,unity_OrthoParams.w > 0.5 ? orthoFootprint :
                        rayFootprint*length(samplePosition-_GalaxyCameraPosition)/_GalaxyRadius);
                    density(samplePosition, footprint, emissive, absorption);
                    float3 segment = exp(-absorption * ds);
                    float3 integral = (1.0 - segment) / max(absorption, 0.00001);
                    integral = lerp(ds, integral, step(0.0001, absorption));
                    light += transmission * emissive * integral;
                    transmission *= segment;
                    p += rd * stepT;
                }
                if (_GrainStrength > 0)
                {
                    // Stable grain in galaxy coordinates. Fade inside the disk,
                    // at grazing angles and when cells become smaller than pixels.
                    float safeY=(rayGalaxy.y<0?-1.0:1.0)*max(0.03,abs(rayGalaxy.y));
                    float planeT=-originGalaxy.y/safeY;
                    float2 plane=(originGalaxy+rayGalaxy*planeT).xz/_GalaxyRadius;
                    float2 q=plane*_GrainFrequency;
                    float2 dx=ddx(q), dy=ddy(q);
                    float pixel=max(length(dx),length(dy));
                    float visible=smoothstep(0.03,0.2,abs(rayGalaxy.y))*(1.0-smoothstep(0.8,2.5,pixel));
                    visible*=smoothstep(1.0,2.0,abs(_GalaxyCameraPosition.y)/max(0.001,_DiskThickness*0.22));
                    visible*=step(0.0,planeT);
                    if(visible > 0.0001)
                    {
                        float grain=starGrain(q,dx,dy)+0.5*starGrain(q*1.73+19.17,dx*1.73,dy*1.73);
                        light*=max(0.0,1.0+_GrainStrength*visible*(grain-0.008)*32.0);
                    }
                }
                light *= _Exposure;
                light = saturate((light * (2.51 * light + 0.03)) / (light * (2.43 * light + 0.59) + 0.14));
                return float4(light * _OpacityMultiplier,
                    saturate((1.0 - dot(transmission, float3(0.333333, 0.333333, 0.333333))) * _OpacityMultiplier));
            }
            ENDCG
        }
    }
    Fallback Off
}
