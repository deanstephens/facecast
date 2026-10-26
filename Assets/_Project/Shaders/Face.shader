// An avatar's face screen. The mesh's UVs run 0..1 across the 4:3 screen (u from the viewer's left to right, v up).
// With video (_HasVideo = 1) it shows the player's webcam, cropped to fill the screen (_VideoST: xy scale, zw
// offset, from VideoCrop.AspectFill). Without it (camera off, not connected yet, or a demo bot) it draws a friendly
// cartoon face in the player's colour that blinks, and whose mouth moves while the player speaks (_Speaking).
// Unlit: a screen gives off its own light, so faces stay readable in shade. A soft glass sheen and an inner rim
// that lights up in the player's colour while they speak finish the look. _Dither fades it with the avatar.
Shader "Facecast/Face"
{
    Properties
    {
        _MainTex("Video", 2D) = "black" {}
        _VideoST("Video Crop (xy scale, zw offset)", Vector) = (1, 1, 0, 0)
        _HasVideo("Has Video (0..1)", Range(0, 1)) = 0
        _Tint("Player Colour", Color) = (0.35, 0.65, 0.95, 1)
        _Speaking("Speaking (0..1)", Range(0, 1)) = 0
        _Seed("Blink Seed", Float) = 0
        _Brightness("Video Brightness", Float) = 1
        _Dither("Dither Fade (0 = solid, 1 = gone)", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float4 _VideoST;
            half _HasVideo;
            half4 _Tint;
            half _Speaking;
            float _Seed;
            half _Brightness;
            half _Dither;
        CBUFFER_END

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float2 uv : TEXCOORD0;
        };

        half DitherThreshold(float2 pixel)
        {
            const half m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
            uint2 p = uint2(pixel) % 4;
            return (m[p.y * 4 + p.x] + 0.5h) / 16.0h;
        }
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

            // Signed distance to a rounded box centred on the origin (half size b, corner radius r).
            float RoundBox(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
            }

            // The cartoon face: two glowing eyes that blink now and then, and a smile that opens as the player talks.
            half3 CartoonFace(float2 uv)
            {
                const float aspect = 4.0 / 3.0;
                float2 p = (uv - 0.5) * float2(aspect, 1.0);           // screen units, height 1
                half3 tint = _Tint.rgb;
                half3 bg = lerp(tint * 0.18h, tint * 0.42h, saturate(uv.y * 1.1h));

                // Blink for ~0.14 s every few seconds, at a different moment on every avatar.
                float t = _Time.y + _Seed * 7.31;
                float period = 3.3 + frac(_Seed * 1.618) * 1.8;
                float ph = frac(t / period) * period;
                half open = saturate(abs(ph - 0.5) / 0.07);
                open = max(open, 0.08h);

                float eyeH = 0.13 * open;
                float d = 1e5;
                for (int s = -1; s <= 1; s += 2)
                {
                    float2 e = p - float2(0.2 * s, 0.1);
                    d = min(d, RoundBox(e, float2(0.055, max(eyeH, 0.012)), min(0.055, max(eyeH, 0.012))));
                }
                half eyes = 1.0h - smoothstep(-0.004h, 0.004h, d);

                // Mouth: a smile arc that grows into an open oval with speech.
                float talk = _Speaking;
                float2 m = p - float2(0.0, -0.2);
                float smile = abs(length(float2(m.x, (m.y - 0.07) * 1.4)) - 0.13) - 0.016;
                smile = max(smile, m.y - 0.03);                         // keep only the bottom of the ring
                float oval = length(m / float2(0.11, 0.025 + 0.09 * talk)) - 1.0;
                float mouthD = lerp(smile, oval * 0.04, smoothstep(0.05, 0.3, talk));
                half mouth = 1.0h - smoothstep(-0.004h, 0.004h, mouthD);

                half3 ink = half3(0.97h, 0.98h, 1.0h);
                half3 col = bg;
                col = lerp(col, ink, eyes);
                col = lerp(col, ink, mouth);
                // Rosy cheeks.
                float cheeks = 0;
                for (int c = -1; c <= 1; c += 2)
                    cheeks = max(cheeks, 1.0 - smoothstep(0.0, 0.06, length(p - float2(0.33 * c, -0.08))));
                col = lerp(col, half3(1.0h, 0.55h, 0.6h), cheeks * 0.35h);
                return col;
            }

            half4 frag(Varyings i) : SV_Target
            {
                if (_Dither > 0.001h) clip(DitherThreshold(i.positionCS.xy) - _Dither);
                float2 uv = i.uv;

                half3 video = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv * _VideoST.xy + _VideoST.zw).rgb * _Brightness;
                half3 col = lerp(CartoonFace(uv), video, _HasVideo);

                // Glass: a faint diagonal sheen near the top and a soft darkening into the corners.
                float2 c = (uv - 0.5) * float2(4.0 / 3.0, 1.0);
                float edge = -RoundBox(c, float2(0.667, 0.5), 0.12);      // 0 at the rim, grows inwards
                half vignette = lerp(0.72h, 1.0h, smoothstep(0.0h, 0.09h, edge));
                col *= vignette;
                half sheen = smoothstep(0.05h, 0.0h, abs(uv.x * 0.55h + uv.y - 1.18h)) * smoothstep(0.35h, 0.75h, uv.y);
                col += sheen * 0.07h;

                // Speaking: the rim lights up in the player's colour.
                half rim = (1.0h - smoothstep(0.0h, 0.035h, edge)) * _Speaking;
                col = lerp(col, _Tint.rgb * 1.6h + 0.15h, rim * 0.85h);

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
                if (_Dither > 0.001h) clip(DitherThreshold(positionCS.xy) - _Dither);
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
