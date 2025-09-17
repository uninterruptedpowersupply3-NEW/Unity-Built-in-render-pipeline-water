Shader "PBR/LakeWater_Detailed"
{
    Properties
    {
        // --- Base Water & Translucency ---
        _WaterBaseColor ("Water Color (Shallow)", Color) = (0.05, 0.1, 0.12, 1.0)
        _WaterDeepColor ("Water Color (Deep)", Color) = (0.01, 0.02, 0.03, 1.0)
        _AbsorptionDepth ("Absorption Depth", Range(1.0, 50.0)) = 12.0
        _SSSColor("Subsurface Scatter Color", Color) = (0.1, 0.3, 0.25, 1.0)
        _SSSStrength("SSS Strength", Range(0, 5)) = 1.5

        // --- Surface & Reflection ---
        _SpecularColor ("Specular Color", Color) = (1,1,1,1)
        _Smoothness ("Smoothness", Range(0.0, 1.0)) = 0.9
        
        // --- Normals (Macro + Micro) ---
        _NormalStrength ("Wave Normal Strength (Macro)", Range(0.0, 1.0)) = 0.15
        _DetailNormalMap("Detail Normal Map (Tiling)", 2D) = "bump" {}
        _DetailTiling ("Detail Tiling", Float) = 10.0
        _DetailStrength ("Detail Strength (Micro)", Range(0.0, 1.0)) = 0.4
        _DetailPanningSpeed ("Detail Panning Speed (U,V)", Vector) = (0.02, 0.015, 0, 0)
        _DetailFadeDistance("Detail Fade Distance", Range(0.1, 20.0)) = 7.0

        // --- Domain Warping & Noise ---
        _NoiseTexture("Noise Texture (Tiling Grayscale)", 2D) = "gray" {}
        _WarpStrength ("Domain Warp Strength", Range(0.0, 20.0)) = 5.0
        _WarpTiling ("Domain Warp Tiling", Float) = 0.1

        // --- Gerstner Waves (Match C# Manager) ---
        _WaveSet1Params ("Wave 1 (A, k, S, Q)", Vector) = (0.4, 0.897, 1.2, 0.8)
        _WaveSet1Dir ("Wave 1 Dir (Dx, Dz)", Vector) = (1.0, 0.2, 0, 0)
        _WaveSet2Params ("Wave 2 (A, k, S, Q)", Vector) = (0.3, 1.795, 1.5, 0.8)
        _WaveSet2Dir ("Wave 2 Dir (Dx, Dz)", Vector) = (0.7, 0.7, 0, 0)
        _WaveSet3Params ("Wave 3 (A, k, S, Q)", Vector) = (0.08, 4.188, 2.0, 0.9)
        _WaveSet3Dir ("Wave 3 Dir (Dx, Dz)", Vector) = (1.0, -0.8, 0, 0)
        _WaveSet4Params ("Wave 4 (A, k, S, Q)", Vector) = (0.05, 6.981, 2.2, 0.9)
        _WaveSet4Dir ("Wave 4 Dir (Dx, Dz)", Vector) = (0.3, -0.5, 0, 0)

        // --- Foam ---
        _CrestFoamThreshold("Crest Foam Threshold", Range(0.1, 2.0)) = 0.6
        _CrestFoamStrength("Crest Foam Strength", Range(0.0, 1.0)) = 0.8
        _ShoreFoamColor ("Shore Foam Color", Color) = (0.9, 0.9, 0.9, 1.0)
        _ShoreFoamMinDepth ("Shore Foam Min Depth", Range(0.01, 1.0)) = 0.15
        _ShoreFoamBlendDist ("Shore Foam Blend Distance", Range(0.1, 5.0)) = 1.5
        _FoamTilingAndSpeed ("Foam Tiling & Speed", Vector) = (1.0, 0.03, 0.025, 0.7) // X: Tiling, YZ: Speed, W: Density
        _FoamDistortionStrength("Foam Distortion", Range(0, 0.1)) = 0.02

        // --- Interaction Effects ---
        _InteractionEvent0 ("Interaction 0", Vector) = (0,0,-1,999)
        _InteractionEvent1 ("Interaction 1", Vector) = (0,0,-1,999)
        // ... (rest of interaction properties)

        [HideInInspector] _ShaderTime ("Shader Time", Float) = 0
        [HideInInspector] _WaterOrigin ("Water Origin", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        LOD 300
        
        CGPROGRAM
        #pragma surface surf WaterLighting vertex:vert alpha:fade
        #pragma target 3.0
        #include "UnityPBSLighting.cginc"

        half4 _SpecularColor, _SSSColor;
        half _Smoothness, _SSSStrength; // <-- FIXED: _SSSStrength is now declared

        // Custom lighting model (same as before)
        half4 LightingWaterLighting (SurfaceOutputStandardSpecular s, half3 lightDir, half3 viewDir, half atten) {
            s.Normal = normalize(s.Normal);
            half3 h = normalize(lightDir + viewDir);
            half NdotL = saturate(dot(s.Normal, lightDir));
            half NdotH = saturate(dot(s.Normal, h));
            half3 backLight = saturate(dot(s.Normal, -lightDir));
            half3 sss = pow(backLight, 4.0) * s.Occlusion * _SSSStrength;
            half3 sssColor = _SSSColor.rgb * s.Albedo * sss * _LightColor0.rgb;
            float specPower = exp2(s.Smoothness * 11.0) + 2.0;
            half3 spec = atten * _SpecularColor.rgb * pow(NdotH, specPower) * _LightColor0.rgb;
            half4 c;
            c.rgb = (s.Albedo * _LightColor0.rgb * NdotL + sssColor) * atten + spec;
            c.a = s.Alpha;
            return c;
        }

        struct Input { float3 worldPos; float4 screenPos; float3 viewDir; };
        
        float4 _WaveSet1Params, _WaveSet2Params, _WaveSet3Params, _WaveSet4Params;
        float4 _WaveSet1Dir, _WaveSet2Dir, _WaveSet3Dir, _WaveSet4Dir;
        float _ShaderTime, _WarpStrength, _WarpTiling;
        float4 _WaterOrigin;
        sampler2D _NoiseTexture;
        float4 _NoiseTexture_ST;

        void GerstnerWave(in float2 p_xz, in float4 p, in float2 dir, in float time, out float3 disp, out float2 norm) {
            float k = p.y, amp = p.x, speed = p.z, Q = p.w;
            if (amp < 0.001) { disp = 0; norm = 0; return; }
            float s, c; sincos(k * dot(dir, p_xz) + time * speed, s, c);
            disp = float3(Q * amp * dir.x * c, amp * s, Q * amp * dir.y * c);
            norm = (k * amp * c) * dir;
        }

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
            float3 relPos = worldPos - _WaterOrigin.xyz;
            
            float2 warp_uv = relPos.xz * _WarpTiling;
            float warp_noise = (tex2Dlod(_NoiseTexture, float4(warp_uv, 0, 0)).r - 0.5) * 2.0;
            float2 warpOffset = warp_noise * _WarpStrength;
            float2 warpedXZ = relPos.xz + warpOffset;
            
            float3 d1,d2,d3,d4; float2 n_dummy;
            GerstnerWave(warpedXZ, _WaveSet1Params, _WaveSet1Dir.xy, _ShaderTime, d1, n_dummy);
            GerstnerWave(warpedXZ, _WaveSet2Params, _WaveSet2Dir.xy, _ShaderTime, d2, n_dummy);
            GerstnerWave(warpedXZ, _WaveSet3Params, _WaveSet3Dir.xy, _ShaderTime, d3, n_dummy);
            GerstnerWave(warpedXZ, _WaveSet4Params, _WaveSet4Dir.xy, _ShaderTime, d4, n_dummy);
            
            float3 dispWorldPos = worldPos + d1 + d2 + d3 + d4;
            v.vertex.xyz = mul(unity_WorldToObject, float4(dispWorldPos, 1.0)).xyz;
            o.worldPos = dispWorldPos;
        }

        sampler2D _CameraDepthTexture, _DetailNormalMap;
        float4 _WaterBaseColor, _WaterDeepColor, _DetailNormalMap_ST;
        float _AbsorptionDepth, _NormalStrength, _DetailFadeDistance, _DetailTiling, _DetailStrength;
        float2 _DetailPanningSpeed;
        float4 _ShoreFoamColor, _FoamTilingAndSpeed;
        float _ShoreFoamMinDepth, _ShoreFoamBlendDist, _CrestFoamThreshold, _CrestFoamStrength, _FoamDistortionStrength;
        // ... (interaction properties)

        void surf(Input IN, inout SurfaceOutputStandardSpecular o)
        {
            float3 relPos = IN.worldPos - _WaterOrigin.xyz;
            float distToCam = distance(_WorldSpaceCameraPos, IN.worldPos);
            
            // --- Color & Depth ---
            float sceneRawDepth = SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(IN.screenPos));
            float sceneLinearEyeDepth = LinearEyeDepth(sceneRawDepth);
            float waterSurfaceLinearEyeDepth = IN.screenPos.w;
            float waterPixelDepth = max(0.001, sceneLinearEyeDepth - waterSurfaceLinearEyeDepth);
            float absorbFac = saturate(waterPixelDepth / _AbsorptionDepth);
            half3 waterBodyCol = lerp(_WaterBaseColor.rgb, _WaterDeepColor.rgb, absorbFac);
            
            // --- Domain Warping for fragment stage ---
            float2 warp_uv = relPos.xz * _WarpTiling;
            float warp_noise = (tex2D(_NoiseTexture, warp_uv).r - 0.5) * 2.0;
            float2 warpOffset = warp_noise * _WarpStrength;
            float2 warpedXZ = relPos.xz + warpOffset;

            // --- Gerstner Normals ---
            float3 d1,d2,d3,d4; float2 n1,n2,n3,n4;
            GerstnerWave(warpedXZ, _WaveSet1Params, _WaveSet1Dir.xy, _ShaderTime, d1, n1);
            GerstnerWave(warpedXZ, _WaveSet2Params, _WaveSet2Dir.xy, _ShaderTime, d2, n2);
            GerstnerWave(warpedXZ, _WaveSet3Params, _WaveSet3Dir.xy, _ShaderTime, d3, n3);
            GerstnerWave(warpedXZ, _WaveSet4Params, _WaveSet4Dir.xy, _ShaderTime, d4, n4);
            float2 total_dY_dXZ = n1 + n2 + n3 + n4;
            float total_dY = d1.y + d2.y + d3.y + d4.y;
            half3 gerstnerNormal = normalize(half3(-total_dY_dXZ.x * _NormalStrength, 1.0, -total_dY_dXZ.y * _NormalStrength));

            // --- Detail Normals ---
            float2 uv1 = IN.worldPos.xz * (_DetailTiling * 0.1) + _ShaderTime * _DetailPanningSpeed.xy;
            float2 uv2 = IN.worldPos.xz * (_DetailTiling * 0.1) * 1.3 - _ShaderTime * _DetailPanningSpeed.xy * 0.8;
            half3 detail1 = UnpackNormal(tex2D(_DetailNormalMap, uv1));
            half3 detail2 = UnpackNormal(tex2D(_DetailNormalMap, uv2));
            half3 detailNormal = normalize(half3((detail1.xy + detail2.xy), detail1.z * detail2.z));

            // --- Final Normal Blending ---
            float detailFade = saturate(distToCam / _DetailFadeDistance);
            half3 finalNormal = normalize(lerp(gerstnerNormal, normalize(gerstnerNormal + detailNormal), _DetailStrength * detailFade));
            // ... (interaction normal disturbance here) ...
            
            // --- Foam Calculations ---
            float2 foamDistortion = finalNormal.xy * _FoamDistortionStrength;
            float2 foamUV1 = warpedXZ * _FoamTilingAndSpeed.x + _ShaderTime * _FoamTilingAndSpeed.yz + foamDistortion;
            float2 foamUV2 = warpedXZ * _FoamTilingAndSpeed.x * 0.7 - _ShaderTime * _FoamTilingAndSpeed.yz * 0.8 + foamDistortion;
            float foamNoise = lerp(tex2D(_NoiseTexture, foamUV1).r, tex2D(_NoiseTexture, foamUV2).r, 0.5) * _FoamTilingAndSpeed.w;
            float shoreFoam = (1.0 - saturate((waterPixelDepth - _ShoreFoamMinDepth) / _ShoreFoamBlendDist)) * foamNoise;
            float crestFoam = saturate((total_dY - _CrestFoamThreshold) / (1.0 - _CrestFoamThreshold)) * _CrestFoamStrength * (foamNoise > 0.5 ? 1 : 0);
            // ... (interaction foam here) ...
            float totalFoam = saturate(shoreFoam + crestFoam /*+ interactionFoam*/);
            
            // --- Final PBR Properties ---
            o.Albedo = lerp(waterBodyCol, _ShoreFoamColor.rgb, totalFoam);
            o.Specular = _SpecularColor.rgb;
            o.Smoothness = lerp(_Smoothness, 0.1, totalFoam);
            o.Normal = finalNormal;
            o.Occlusion = 1.0 - absorbFac; 
            o.Alpha = lerp(_WaterBaseColor.a, _WaterDeepColor.a, absorbFac);
            o.Alpha = saturate(o.Alpha + totalFoam);
        }
        ENDCG
    }
    FallBack "Transparent/VertexLit"
}