Shader "Custom/ProceduralPBRWater" {
    Properties {
        // ### FBM Wave Generation Properties ###
        _FragmentSeed ("Seed", Float) = 0
        _FragmentSeedIter ("Seed Iteration", Float) = 1.2
        _FragmentFrequency ("Frequency", Float) = 1.0
        _FragmentFrequencyMult ("Frequency Multiplier", Float) = 1.18
        _FragmentAmplitude ("Amplitude", Float) = 1.0
        _FragmentAmplitudeMult ("Amplitude Multiplier", Float) = 0.82
        _FragmentInitialSpeed ("Initial Speed", Float) = 1.0
        _FragmentSpeedRamp ("Speed Ramp", Float) = 1.07
        _FragmentDrag ("Wave Drag", Range(0, 1)) = 0.38
        _FragmentMaxPeak ("Wave Peak Sharpness", Range(0, 1)) = 1.0
        _FragmentPeakOffset ("Wave Peak Offset", Range(0, 1)) = 1.0
        _FragmentWaveCount ("Wave Octaves", Range(1, 36)) = 12
        _FragmentHeight ("Overall Height", Float) = 1.0

        // ### PBR Lighting Properties (from FFTWater.shader) ###
        _SunIrradiance ("Sun Irradiance", Color) = (1,1,1,1)
        _Roughness ("Roughness", Range(0.01, 1)) = 0.1
        _ScatterColor ("Scatter Color", Color) = (0.02, 0.07, 0.17, 1)
        _BubbleColor ("Bubble Color", Color) = (1,1,1,1)
        _BubbleDensity ("Bubble Density", Range(0,1)) = 0.1
        _FoamColor ("Foam Color", Color) = (1,1,1,1)
        _WavePeakScatterStrength ("Wave Peak Scatter", Range(0, 10)) = 1.0
        _ScatterStrength ("Scatter Strength", Range(0, 10)) = 1.0
        _ScatterShadowStrength ("Scatter Shadow Strength", Range(0, 10)) = 1.0
        _EnvironmentLightStrength("Environment Reflection Strength", Range(0, 5)) = 1.0
    }
    SubShader {
        Tags { "LightMode" = "ForwardBase" "RenderType"="Opaque" }

        Pass {
            CGPROGRAM

            #pragma vertex vp
            #pragma fragment fp
            #pragma target 3.0 // Needed for advanced math

            #include "UnityPBSLighting.cginc"
            #include "AutoLight.cginc"

            // Structs
            struct v2f {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            // FBM Properties
            float _FragmentSeed, _FragmentSeedIter, _FragmentFrequency, _FragmentFrequencyMult, _FragmentAmplitude, _FragmentAmplitudeMult;
            float _FragmentInitialSpeed, _FragmentSpeedRamp, _FragmentDrag, _FragmentHeight, _FragmentMaxPeak, _FragmentPeakOffset;
            int _FragmentWaveCount;

            // PBR Properties
            float3 _SunIrradiance, _ScatterColor, _BubbleColor, _FoamColor;
            float _Roughness, _BubbleDensity, _WavePeakScatterStrength, _ScatterStrength, _ScatterShadowStrength, _EnvironmentLightStrength;
            
            samplerCUBE _EnvironmentMap;

            // This function procedurally generates wave height (in .x) and surface slope (in .yz)
            // using a sum of sine waves (fBM). It does not use any textures. [cite: 4386, 4387, 4388, 4389, 4390, 4391, 4392, 4393]
            float3 fragmentFBM(float3 v) {
                float f = _FragmentFrequency;
                float a = _FragmentAmplitude;
                float speed = _FragmentInitialSpeed;
                float seed = _FragmentSeed;
                float3 p = v;
                float h = 0.0f;
                float2 n = 0.0f;
                float amplitudeSum = 0.0f;
                
                for (int wi = 0; wi < _FragmentWaveCount; ++wi) {
                    float2 d = normalize(float2(cos(seed), sin(seed)));
                    float x = dot(d, p.xz) * f + _Time.y * speed;
                    float wave = a * exp(_FragmentMaxPeak * sin(x) - _FragmentPeakOffset);
                    float2 dw = f * d * (_FragmentMaxPeak * wave * cos(x));
                    
                    h += wave;
                    p.xz += -dw * a * _FragmentDrag;
                    n += dw;
                    
                    amplitudeSum += a;
                    f *= _FragmentFrequencyMult;
                    a *= _FragmentAmplitudeMult;
                    speed *= _FragmentSpeedRamp;
                    seed += _FragmentSeedIter;
                }
                
                float3 output = float3(h, n.x, n.y) / amplitudeSum;
                output.x *= _FragmentHeight;
                return output;
            }

            // Vertex Shader: Passes world position to the fragment shader.
            v2f vp(float4 vertex : POSITION) {
                v2f o;
                o.worldPos = mul(unity_ObjectToWorld, vertex);
                o.pos = UnityObjectToClipPos(vertex);
                return o;
            }

            // PBR helper functions from FFTWater.shader
            float SmithMaskingBeckmann(float3 H, float3 S, float roughness) {
                float hdots = max(0.001f, dot(H, S));
                float a = hdots / (roughness * sqrt(1.0f - hdots * hdots));
                float a2 = a * a;
                return a < 1.6f ? (1.0f - 1.259f * a + 0.396f * a2) / (3.535f * a + 2.181f * a2) : 0.0f;
            }

            float Beckmann(float ndoth, float roughness) {
                float exp_arg = (ndoth * ndoth - 1.0f) / (roughness * roughness * ndoth * ndoth);
                return exp(exp_arg) / (3.14 * roughness * roughness * pow(ndoth, 4));
            }

            // Fragment Shader
            float4 fp(v2f i) : SV_TARGET {
                // ### 1. Procedural Surface Generation ###
                // Calculate wave height and slope procedurally at this pixel.
                float3 fbm = fragmentFBM(i.worldPos);
                float height = fbm.x;
                float2 slopes = fbm.yz;
                
                // Construct the surface normal (meso-normal) from the procedural slopes.
                float3 mesoNormal = normalize(float3(-slopes.x, 1.0f, -slopes.y));

                // ### 2. PBR Lighting Calculation (from FFTWater.shader) ###
                float3 lightDir = -normalize(_WorldSpaceLightPos0.xyz);
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 halfwayDir = normalize(lightDir + viewDir);
                float3 macroNormal = float3(0, 1, 0); // The geometric plane is flat up.

                // --- Specular Lighting ---
                float a = _Roughness; // Roughness can be modified by foam later.
                float ndoth = max(0.0001f, dot(mesoNormal, halfwayDir));
                float viewMask = SmithMaskingBeckmann(halfwayDir, viewDir, a);
                float lightMask = SmithMaskingBeckmann(halfwayDir, lightDir, a);
                float G = rcp(1.0f + viewMask + lightMask); // Smith geometry term
                
                float eta = 1.33f; // Index of Refraction for water
                float R_bias = ((eta - 1) * (eta - 1)) / ((eta + 1) * (eta + 1));
                float numerator = pow(1.0f - dot(mesoNormal, viewDir), 5.0f * exp(-2.69f * a));
                float F = R_bias + (1.0f - R_bias) * numerator / (1.0f + 22.7f * pow(a, 1.5f)); // Fresnel term
                F = saturate(F);

                float D = Beckmann(ndoth, a); // Beckmann NDF
                float3 specular = _SunIrradiance * F * G * D;
                specular /= 4.0f * max(0.001f, dot(macroNormal, lightDir));
                specular *= dot(mesoNormal, lightDir);
                
                // --- Environment Reflections ---
                float3 envReflection = texCUBE(_EnvironmentMap, reflect(-viewDir, mesoNormal)).rgb;
                envReflection *= _EnvironmentLightStrength;

                // --- Subsurface Scattering Approximation ---
                float H = max(0.0f, height);
                float NdotL = dot(mesoNormal, lightDir);
                float k1 = _WavePeakScatterStrength * H * pow(dot(lightDir, -viewDir), 4.0) * pow(0.5 - 0.5 * dot(lightDir, mesoNormal), 3.0);
                float k2 = _ScatterStrength * pow(dot(viewDir, mesoNormal), 2.0);
                float k3 = _ScatterShadowStrength * NdotL;
                float k4 = _BubbleDensity;
                float3 scatter = (k1 + k2) * _ScatterColor * _SunIrradiance * rcp(1.0f + lightMask);
                scatter += k3 * _ScatterColor * _SunIrradiance + k4 * _BubbleColor * _SunIrradiance;
                
                // --- Final Combination ---
                float3 output = (1.0f - F) * scatter + specular + F * envReflection;
                
                // Add foam/tip color based on procedural height. A simple substitute for the FFT shader's Jacobian foam.
                // You could also use height to generate a foam value to lerp to _FoamColor for a stronger effect.
                output += _FoamColor * pow(saturate(height), 8.0f);

                return float4(max(0.0f, output), 1.0f);
            }

            ENDCG
        }
    }
}