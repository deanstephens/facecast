// A projection screen showing someone's shared screen. The mesh's UVs run 0..1 across the 16:9 screen (u from the
// viewer's left to right, v up). The picture is fitted whole (_Fit: xy scale, zw offset, from VideoCrop.AspectFit),
// with dark bars where its shape differs from the screen's, because nobody wants the edges of a shared screen cut
// off. Until the picture arrives (_HasVideo fades it in) the screen glows softly in the sharer's colour (_Tint) with
// a slow sheen sweeping across, so it's clear something is coming. Unlit, like a projection in a dim room.
Shader "Facecast/Screen"
{
    Properties
    {
        _MainTex("Picture", 2D) = "black" {}
        _Fit("Fit (xy scale, zw offset)", Vector) = (1, 1, 0, 0)
        _HasVideo("Has Picture (0..1)", Range(0, 1)) = 0
        _Tint("Sharer's Colour", Color) = (0.35, 0.65, 0.95, 1)
        _Brightness("Picture Brightness", Float) = 0.95
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float4 _Fit;
            half _HasVideo;
            half4 _Tint;
            half _Brightness;
        CBUFFER_END

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float2 uv : TEXCOORD0;
        };
        ENDHLSL

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half fogFactor : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv;

                // Waiting for the picture: a soft glow in the sharer's colour, darker at the edges, with a sheen.
                half3 tint = _Tint.rgb;
                half edge = saturate(min(min(uv.x, 1.0 - uv.x) * 1.78, min(uv.y, 1.0 - uv.y)) * 6.0);
                half3 idle = lerp(half3(0.06h, 0.07h, 0.1h), tint * 0.45h + 0.05h, 0.35h + 0.25h * edge);
                float sweep = frac(_Time.y * 0.25) * 2.6 - 0.8;
                half sheen = exp(-pow((uv.x * 0.8 + uv.y * 0.45 - sweep) * 7.0, 2.0)) * 0.12h;
                idle += sheen;

                // The picture, fitted whole, with bars around it.
                float2 p = (uv - _Fit.zw) / _Fit.xy;
                half inside = step(0.0, p.x) * step(p.x, 1.0) * step(0.0, p.y) * step(p.y, 1.0);
                half3 picture = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, saturate(p)).rgb * _Brightness;
                picture = lerp(half3(0.03h, 0.03h, 0.04h), picture, inside);

                half3 col = lerp(idle, picture, _HasVideo);
                col = MixFog(col, i.fogFactor);
                return half4(col, 1);
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

            float4 depthVert(Attributes v) : SV_POSITION
            {
                return TransformObjectToHClip(v.positionOS.xyz);
            }

            half depthFrag(float4 positionCS : SV_POSITION) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
