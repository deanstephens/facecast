// The shader every model uses. Colour comes from vertex colours (sRGB bytes written by the Blender pipeline);
// vertex alpha says how a corner is treated:
//   0      plain surface
//   0.25   tint: the grey vertex colour times _TeamColor (an avatar's chosen colour)
//   0.5    always emissive (lamp glass, hover rings)                         strength _EmissionStrength
//   1      signal glow, switched per renderer with _SignalGlow (an avatar's speaking lights)
// Lighting is a warm sun with real shadows, a sky/ground ambient and gently banded wrap lighting for a soft toy
// look. _Rim lifts avatars off the background, _Dither fades a renderer out with a screen-door pattern (an avatar
// you walk into) and _Noise mottles big flat areas (paving, lawn) in world space.
Shader "Facecast/Flat"
{
    Properties
    {
        _TeamColor("Avatar Colour", Color) = (1, 1, 1, 1)
        _Tint("Tint", Color) = (1, 1, 1, 1)
        _EmissionStrength("Emission Strength", Float) = 1.6
        _SignalGlow("Signal Glow (0..1, per renderer)", Float) = 0
        _Wrap("Light Wrap", Range(0, 1)) = 0.4
        _Bands("Light Banding", Range(0, 1)) = 0.35
        _AmbientScale("Ambient Scale", Range(0, 3)) = 1
        _Rim("Rim (rgb colour, a strength)", Color) = (0, 0, 0, 0)
        _Dither("Dither Fade (0 = solid, 1 = gone)", Range(0, 1)) = 0
        _Noise("Ground Noise", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _TeamColor;
            half4 _Tint;
            half _EmissionStrength;
            half _SignalGlow;
            half _Wrap;
            half _Bands;
            half _AmbientScale;
            half4 _Rim;
            half _Dither;
            half _Noise;
        CBUFFER_END

        half4 _FC_AmbientSky;
        half4 _FC_AmbientGround;

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        // 4x4 ordered dither threshold for screen-door fading.
        half DitherThreshold(float2 pixel)
        {
            const half m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
            uint2 p = uint2(pixel) % 4;
            return (m[p.y * 4 + p.x] + 0.5h) / 16.0h;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half Band(half a)
            {
                const half steps = 3.0h;
                half stepped = (floor(a * steps) + smoothstep(0.0h, 0.25h, frac(a * steps))) / steps;
                return lerp(a, stepped, _Bands);
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                if (_Dither > 0.001h) clip(DitherThreshold(i.positionCS.xy) - _Dither);

                half3 n = normalize(i.normalWS);
                half3 albedo = i.color.rgb;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    albedo = SRGBToLinear(albedo);
                #endif

                half a = i.color.a;
                half team = step(0.125h, a) * step(a, 0.375h);
                half alwaysGlow = step(0.375h, a) * step(a, 0.625h);
                half signalGlow = step(0.875h, a);
                albedo = lerp(albedo, albedo * _TeamColor.rgb, team);
                albedo *= _Tint.rgb;

                if (_Noise > 0.001h)
                {
                    // Break up big flat areas (grass, dirt) with soft world-space mottling.
                    float2 w = i.positionWS.xz;
                    half nz = ValueNoise(w * 0.35) * 0.6h + ValueNoise(w * 1.7) * 0.4h;
                    albedo *= 1.0h + (nz - 0.5h) * 0.32h * _Noise;
                }

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half ndl = dot(n, mainLight.direction);
                half wrapped = saturate((ndl + _Wrap) / (1.0h + _Wrap));
                half shadow = lerp(0.42h, 1.0h, mainLight.shadowAttenuation);
                half3 direct = mainLight.color * Band(wrapped) * shadow;

                half3 points = 0;
                #if defined(_ADDITIONAL_LIGHTS)
                    InputData inputData = (InputData)0;
                    inputData.positionWS = i.positionWS;
                    inputData.normalWS = n;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                    half4 shadowMask = half4(1, 1, 1, 1);
                    uint pixelLightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(pixelLightCount)
                        Light l = GetAdditionalLight(lightIndex, i.positionWS, shadowMask);
                        half pl = saturate((dot(n, l.direction) + _Wrap) / (1.0h + _Wrap));
                        points += l.color * saturate(l.distanceAttenuation) * pl;
                    LIGHT_LOOP_END
                #endif

                half3 ambient = lerp(_FC_AmbientGround.rgb, _FC_AmbientSky.rgb, n.y * 0.5h + 0.5h) * _AmbientScale;
                // Signal corners are white: dim and grey while quiet, the avatar's colour lit up while it speaks.
                half3 signal = lerp(half3(0.35h, 0.37h, 0.42h), _TeamColor.rgb * 2.5h + 0.4h, _SignalGlow);
                albedo = lerp(albedo, signal, signalGlow);
                half3 color = albedo * (ambient + direct + points) * (1.0h - signalGlow);
                half3 glow = albedo * (alwaysGlow * _EmissionStrength + signalGlow);
                color += glow;

                half rim = pow(1.0h - saturate(dot(n, normalize(GetWorldSpaceViewDir(i.positionWS)))), 3.0h);
                color += _Rim.rgb * _Rim.a * rim;

                color = MixFog(color, i.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            ShadowVaryings shadowVert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                ShadowVaryings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = positionCS;
                return o;
            }

            half4 shadowFrag(ShadowVaryings i) : SV_Target
            {
                if (_Dither > 0.5h) clip(-1);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex depthVert
            #pragma fragment depthFrag
            #pragma multi_compile_instancing

            float4 depthVert(Attributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                return TransformObjectToHClip(v.positionOS.xyz);
            }

            half depthFrag(float4 positionCS : SV_POSITION) : SV_Target
            {
                if (_Dither > 0.001h) clip(DitherThreshold(positionCS.xy) - _Dither);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex dnVert
            #pragma fragment dnFrag
            #pragma multi_compile_instancing

            struct DNVaryings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
            };

            DNVaryings dnVert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                DNVaryings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 dnFrag(DNVaryings i) : SV_Target
            {
                if (_Dither > 0.001h) clip(DitherThreshold(i.positionCS.xy) - _Dither);
                return half4(normalize(i.normalWS), 0);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
