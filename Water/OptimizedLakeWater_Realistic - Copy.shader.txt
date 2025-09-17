Shader "Optimized/Legacy/OptimizedLakeWater" {
Properties {
    _WaterBaseColor ("Water Base Color (Shallow/Surface)", Color) = (0.1, 0.2, 0.25, 0.75)
    _WaterDeepColor ("Water Deep Absorb Color", Color) = (0.01, 0.02, 0.03, 0.9)
    _AbsorptionDepth ("Absorption Max Depth", Range(1.0, 30.0)) = 8.0
    _SpecularColor ("Specular Color ", Color) = (0.7, 0.7, 0.7, 1.0)
    _Shininess ("Shininess ", Range(10.0, 150.0)) = 40.0
    _FresnelPower ("Fresnel Power ", Range(2.0, 8.0)) = 5.0
    _NormalStrength ("Wave Normal Strength ", Range(0.01, 0.5)) = 0.1

    _ReflectionCubemap ("Reflection Cubemap (Sky) ", CUBE) = "_Skybox" {}
    _ReflectionStrength ("Reflection Strength ", Range(0.0, 0.7)) = 0.25
    _ReflectionMipBias ("Reflection MIP Bias ", Range(0, 2)) = 1.0

    _WaveSet1Params_Optimized ("Wave 1 (A,k,S,Q) ", Vector) = (0.1, 10.0, 0.5, 0.1)
    _WaveSet1Dir_Optimized ("Wave 1 Dir (Dx,Dz) ", Vector) = (1.0, 0.0, 0, 0)
    _WaveSet2Params_Optimized ("Wave 2 (A,k,S,Q) ", Vector) = (0.05, 20.0, 0.3, 0.05)
    _WaveSet2Dir_Optimized ("Wave 2 Dir (Dx,Dz) ", Vector) = (0.7, 0.7, 0, 0)

    // Shoreline Foam - now fully procedural
    _ShoreFoamColor ("Shore Foam Color ", Color) = (0.8, 0.8, 0.8, 1.0)
    _ShoreFoamDepthThreshold ("Shore Foam Min Depth ", Range(0.01, 0.5)) = 0.05
    _ShoreFoamBlendDistance ("Shore Foam Blend Distance ", Range(0.1, 2.0)) = 0.5
    _ShoreFoamNoiseParams ("Shore Foam Noise (Scale, SpeedU, SpeedV, Density) ", Vector) = (15.0, 0.02, 0.015, 0.5)

    // Interaction Effects
    _InteractionEvent0_Optimized ("Interaction 0 (WorldX, WorldZ, Radius, TimeAlive) ", Vector) = (0,0,-1,999) 
    _InteractionEvent1_Optimized ("Interaction 1 (WorldX, WorldZ, Radius, TimeAlive) ", Vector) = (0,0,-1,999)
    _InteractionFoamColor ("Interaction Foam Color ", Color) = (0.95, 0.95, 0.95, 1.0)
    _InteractionEffectMaxTime ("Interaction Max Time (s) ", Float) = 1.5
    _InteractionFoamPower ("Interaction Foam Power ", Range(1,4)) = 2.0
    _InteractionNormalDisturbPower ("Interaction Normal Disturb ", Range(0,0.05)) = 0.01

    // Baked SSS Lighting
    _BakedLightSSSColor ("Baked SSS Light Color ", Color) = (0.1, 0.15, 0.2, 1.0)
    _SSSStrength ("SSS Strength ", Range(0,1)) = 0.3

    _ShaderTime ("Shader Time (Internal) ", Float) = 0
}

SubShader {
    Tags { "RenderType"="Transparent" "Queue"="Transparent" "LightMode"="ForwardBase" "IgnoreProjector"="True" }
    LOD 100
    ZWrite Off
    Blend SrcAlpha OneMinusSrcAlpha

    Pass {
        CGPROGRAM
        #pragma vertex vert
        #pragma fragment frag
        #pragma multi_compile_fwdbase 
        #pragma target 3.0 

        #include "UnityCG.cginc"
        #include "AutoLight.cginc" 
        #include "Lighting.cginc"   

        #define PI_H 3.14159265h 
        #define F0_WATER 0.02h  

        #define NUM_SHADER_WAVE_SETS 2
        #define MAX_SHADER_INTERACTION_EVENTS 2 

        // Procedural noise functions (replaces texture sampling)
        float hash(float2 p) {
            return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
        }

        float perlinNoise(float2 p) {
            float2 i = floor(p);
            float2 f = frac(p);
            float2 u = f * f * (3.0 - 2.0 * f);
            
            float a = hash(i);
            float b = hash(i + float2(1.0, 0.0));
            float c = hash(i + float2(0.0, 1.0));
            float d = hash(i + float2(1.0, 1.0));
            
            return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
        }

        float fbm(float2 p) {
            float total = 0.0;
            float freq = 1.0;
            float amp = 1.0;
            float sum = 0.0;
            for (int i = 0; i < 4; i++) {
                total += perlinNoise(p * freq) * amp;
                sum += amp;
                amp *= 0.5;
                freq *= 2.0;
            }
            return total / sum;
        }

        struct WaveInputOpt {
            float4 params;
            float2 dir;    
        };

        uniform float4 _WaveSet1Params_Optimized, _WaveSet2Params_Optimized;
        uniform float4 _WaveSet1Dir_Optimized, _WaveSet2Dir_Optimized;
        
        uniform float4 _InteractionEvent0_Optimized, _InteractionEvent1_Optimized;
        #if MAX_SHADER_INTERACTION_EVENTS > 2
            uniform float4 _InteractionEvent2_Optimized;
        #endif

        uniform half _NormalStrength;
        uniform samplerCUBE _ReflectionCubemap;
        uniform half4 _WaterBaseColor, _WaterDeepColor, _SpecularColor;
        uniform half _AbsorptionDepth, _Shininess, _FresnelPower, _ReflectionStrength, _ReflectionMipBias;

        uniform half4 _ShoreFoamColor, _ShoreFoamNoiseParams; 
        uniform half _ShoreFoamDepthThreshold, _ShoreFoamBlendDistance;

        uniform half4 _InteractionFoamColor;
        uniform half _InteractionEffectMaxTime, _InteractionFoamPower, _InteractionNormalDisturbPower;
        
        uniform half4 _BakedLightSSSColor;
        uniform half _SSSStrength;

        uniform sampler2D _CameraDepthTexture;
        uniform float _ShaderTime; 

        struct appdata_custom {
            float4 vertex : POSITION;
            #if defined(LIGHTMAP_ON) || defined(DYNAMICLIGHTMAP_ON)
                float2 texcoord1 : TEXCOORD1;
            #endif
        };

        struct v2f_custom {
            float4 pos : SV_POSITION;
            float3 worldPos_wavy : TEXCOORD0; 
            half3 normal_world : TEXCOORD1; 
            float4 projPos : TEXCOORD2;
            half3 viewDir_world : TEXCOORD3;
            SHADOW_COORDS(4)
            #if defined(LIGHTMAP_ON) || defined(DYNAMICLIGHTMAP_ON)
                float2 lmapUV : TEXCOORD5;
            #endif
        };

        void get_gerstner_wave_v(WaveInputOpt w, float2 p_xz, float time, 
                                 inout float3 totalDisplacement, inout half2 sum_dY_dXZ_contrib) {
            if (w.params.x < 0.001h || w.params.y < 0.001h) return;

            half D_dot_P = dot(w.dir, (half2)p_xz); 
            half angle_arg = w.params.y * D_dot_P + time * w.params.z; 
            
            half s_val, c_val;
            sincos(angle_arg, s_val, c_val);

            half QAk = w.params.w * w.params.x;
            totalDisplacement.x += QAk * w.dir.x * c_val;
            totalDisplacement.z += QAk * w.dir.y * c_val;
            totalDisplacement.y += w.params.x * s_val;
            
            half k_amp_cos = w.params.y * w.params.x * c_val;
            sum_dY_dXZ_contrib.x += w.dir.x * k_amp_cos;
            sum_dY_dXZ_contrib.y += w.dir.y * k_amp_cos;
        }
        
        v2f_custom vert(appdata_custom v) {
            v2f_custom o;

            WaveInputOpt localWaves[NUM_SHADER_WAVE_SETS];
            localWaves[0].params = _WaveSet1Params_Optimized; localWaves[0].dir = _WaveSet1Dir_Optimized.xy;
            localWaves[1].params = _WaveSet2Params_Optimized; localWaves[1].dir = _WaveSet2Dir_Optimized.xy;

            float3 totalDisplacement = float3(0,0,0);
            half2 sum_dY_dXZ = half2(0,0);            

            float3 originalWorldPos = mul(unity_ObjectToWorld, v.vertex).xyz; 

            [unroll]
            for (int k = 0; k < NUM_SHADER_WAVE_SETS; ++k) {
                get_gerstner_wave_v(localWaves[k], originalWorldPos.xz, _ShaderTime, totalDisplacement, sum_dY_dXZ);
            }
            
            o.worldPos_wavy = originalWorldPos + float3(totalDisplacement.x, totalDisplacement.y, totalDisplacement.z);
            o.pos = UnityWorldToClipPos(o.worldPos_wavy);

            o.normal_world = normalize(half3(-sum_dY_dXZ.x * _NormalStrength, 1.0h, -sum_dY_dXZ.y * _NormalStrength));
            
            o.projPos = ComputeScreenPos(o.pos); 
            o.viewDir_world = (half3)normalize(_WorldSpaceCameraPos.xyz - o.worldPos_wavy.xyz);
            
            TRANSFER_SHADOW(o);

            #if defined(LIGHTMAP_ON) || defined(DYNAMICLIGHTMAP_ON)
                o.lmapUV = v.texcoord1.xy * unity_LightmapST.xy + unity_LightmapST.zw;
            #endif
            return o;
        }

        void ProcessInteractionEvents_f(float2 pixelWorldXZ, inout half3 normalW, out half interactionFoamVal) {
            interactionFoamVal = 0.0h;
            half2 totalNormalOffsetLocal = half2(0,0); 

            float4 currentEvents[MAX_SHADER_INTERACTION_EVENTS];
            currentEvents[0] = _InteractionEvent0_Optimized;
            currentEvents[1] = _InteractionEvent1_Optimized;
            #if MAX_SHADER_INTERACTION_EVENTS > 2
                currentEvents[2] = _InteractionEvent2_Optimized;
            #endif

            [unroll]
            for(int j = 0; j < MAX_SHADER_INTERACTION_EVENTS; ++j) {
                float4 eventData = currentEvents[j];
                if (eventData.z < 0.01h) continue;
                half timeSinceEvent = (half)eventData.w; 
                half eventStrength = saturate(1.0h - timeSinceEvent / (half)_InteractionEffectMaxTime);
                if (eventStrength < 0.01h) continue;
                
                half distToEventSqr = dot(pixelWorldXZ - eventData.xy, pixelWorldXZ - eventData.xy);
                half eventRadiusSqr = eventData.z * eventData.z;
                
                if (distToEventSqr < eventRadiusSqr) {
                    half distToEvent = sqrt(distToEventSqr);
                    half effectFalloff = saturate(1.0h - distToEvent / eventData.z); 
                    interactionFoamVal += pow(effectFalloff, _InteractionFoamPower) * eventStrength;

                    if (_InteractionNormalDisturbPower > 0.001h && distToEvent > 0.001h) { 
                        half2 dirToPixelNorm = (pixelWorldXZ - (half2)eventData.xy) / distToEvent; 
                        half rippleStrength = effectFalloff * eventStrength * _InteractionNormalDisturbPower;
                        totalNormalOffsetLocal += dirToPixelNorm * rippleStrength;
                    }
                }
            }
            interactionFoamVal = saturate(interactionFoamVal);
            if (dot(totalNormalOffsetLocal, totalNormalOffsetLocal) > 0.00001h) { 
                normalW.xz += totalNormalOffsetLocal; 
                normalW = normalize(normalW); 
            }
        }

        fixed4 frag(v2f_custom i) : SV_Target {
            half3 normalW = i.normal_world; 
            half3 viewDirW = i.viewDir_world; 

            float sceneRawDepth = SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.projPos));
            float sceneLinearEyeDepth = LinearEyeDepth(sceneRawDepth);
            float waterSurfaceLinearEyeDepth = LinearEyeDepth(i.pos.z); 
            
            half waterPixelDepth = (half)max(0.001, sceneLinearEyeDepth - waterSurfaceLinearEyeDepth);
            half absorbFac = saturate(waterPixelDepth / _AbsorptionDepth);
            half3 waterBodyCol = lerp((half3)_WaterBaseColor.rgb, (half3)_WaterDeepColor.rgb, absorbFac);
            
            // --- PROCEDURAL FOAM NOISE (REPLACES TEXTURE SAMPLING) ---
            half2 foamUV = (half2)i.worldPos_wavy.xz * _ShoreFoamNoiseParams.x * 0.1h + 
                           _ShaderTime * (half2)_ShoreFoamNoiseParams.yz; 
            // Generate noise procedurally instead of using texture
            half foamNoiseVal = fbm(foamUV) * _ShoreFoamNoiseParams.w; 

            half shoreFoamFac = 1.0h - saturate((waterPixelDepth - _ShoreFoamDepthThreshold) / _ShoreFoamBlendDistance);
            shoreFoamFac = saturate(shoreFoamFac * foamNoiseVal);

            half interactionFoamFac = 0.0h;
            ProcessInteractionEvents_f((half2)i.worldPos_wavy.xz, normalW, interactionFoamFac);

            // --- Lighting ---
            half3 lightDirW = normalize((half3)_WorldSpaceLightPos0.xyz); 
            
            fixed atten = SHADOW_ATTENUATION(i);
            
            half NdotL = saturate(dot(normalW, lightDirW));
            half3 diffuseLight = (half3)_LightColor0.rgb * NdotL;

            half3 halfwayDirW = normalize(lightDirW + viewDirW);
            half specAngle = saturate(dot(normalW, halfwayDirW));
            half specTerm = pow(specAngle, _Shininess); 
            half3 specularLight = (half3)_SpecularColor.rgb * specTerm; 

            half3 ambient = ShadeSH9(half4(normalW, 1.0h));
            #if defined(LIGHTMAP_ON) || defined(DYNAMICLIGHTMAP_ON)
                ambient += DecodeLightmap(UNITY_SAMPLE_TEX2D(unity_Lightmap, i.lmapUV));
            #endif
            
            // --- Baked SSS ---
            half sssFactor = saturate(dot(normalW, -lightDirW)) * (1.0h - saturate(dot(normalW, viewDirW))); 
            sssFactor *= (1.0h - absorbFac);
            half3 sssEffect = (half3)_BakedLightSSSColor.rgb * sssFactor * _SSSStrength * (half3)_LightColor0.rgb; 

            half3 litWaterCol = (diffuseLight + ambient) * waterBodyCol + specularLight + sssEffect;
            litWaterCol *= atten;

            // --- Reflection ---
            half3 reflectVec = reflect(-viewDirW, normalW);
            half3 reflectionCol = texCUBElod(_ReflectionCubemap, float4(reflectVec, _ReflectionMipBias)).rgb;
            
            // --- Fresnel ---
            half NdotV = saturate(dot(normalW, viewDirW)); 
            half fresnelTerm = F0_WATER + (1.0h - F0_WATER) * pow(1.0h - NdotV, _FresnelPower);

            half3 finalCol = lerp(litWaterCol, reflectionCol * _ReflectionStrength, fresnelTerm);

            // --- Apply Foam ---
            half totalFoamFac = saturate(shoreFoamFac + interactionFoamFac); 
            half4 foamColToUse4 = _ShoreFoamColor;
             
            if (interactionFoamFac > 0.01h) {
                half blendFactor = saturate(interactionFoamFac / (totalFoamFac + 0.00001h));
                foamColToUse4 = lerp(_ShoreFoamColor, _InteractionFoamColor, blendFactor);
            }

            finalCol = lerp(finalCol, foamColToUse4.rgb, totalFoamFac);
            
            half waterAlpha = lerp(_WaterBaseColor.a, _WaterDeepColor.a, absorbFac); 
            half finalAlpha = saturate(waterAlpha + totalFoamFac * foamColToUse4.a); 

            return fixed4(finalCol, finalAlpha);
        }
        ENDCG
    }
}
Fallback "Transparent/VertexLit"
}