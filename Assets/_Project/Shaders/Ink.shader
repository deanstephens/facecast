// Handwriting on sticky notes and the boards' titles (NoteInk builds the glyph quads). _MainTex is a TextCore font
// atlas holding signed distance fields: alpha 0.5 is a glyph's edge, so the edge stays crisp however close you get
// and softens gracefully far away. The ink colour comes from the vertex colour, lit like the Flat shader lights the
// paper under it (sun with shadows, sky and ground ambient) so writing in the shade darkens with its note. Drawn
// just in front of the paper, blended, with no depth writes.
Shader "Facecast/Ink"
{
    Properties
    {
        _MainTex("Font Atlas (SDF in alpha)", 2D) = "white" {}
        _Weight("Weight (edge shift)", Range(-0.2, 0.2)) = 0.04
        _Wrap("Light Wrap", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half _Weight;
                half _Wrap;
            CBUFFER_END

            half4 _FC_AmbientSky;
            half4 _FC_AmbientGround;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2;
                float2 uv : TEXCOORD3;
                half fogFactor : TEXCOORD4;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.uv = v.uv;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float d = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a;
                // About a pixel of antialiasing across the edge, whatever the scale on screen.
                float w = max(fwidth(d), 1e-4);
                half alpha = saturate((d - 0.5 + _Weight) / w + 0.5) * i.color.a;
                clip(alpha - 0.004h);

                half3 n = normalize(i.normalWS);
                half3 albedo = i.color.rgb;  // sRGB values, like the models' vertex colours
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    albedo = SRGBToLinear(albedo);
                #endif
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half wrapped = saturate((dot(n, mainLight.direction) + _Wrap) / (1.0h + _Wrap));
                half shadow = lerp(0.42h, 1.0h, mainLight.shadowAttenuation);
                half3 ambient = lerp(_FC_AmbientGround.rgb, _FC_AmbientSky.rgb, n.y * 0.5h + 0.5h);
                half3 color = albedo * (ambient + mainLight.color * wrapped * shadow);
                color = MixFog(color, i.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
