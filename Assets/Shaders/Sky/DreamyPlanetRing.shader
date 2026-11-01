// 몽환 행성 고리
// - Quad(사각판)에 씌우면 가운데가 뚫린 반투명 파스텔 고리를 그림
// - 안쪽 → 바깥쪽으로 색 2개가 섞이고, 가는 줄무늬 여러 겹이 은은하게 보임
// - 가장자리가 부드럽게 사라짐 (딱딱한 선 없음), 앞뒤 양면 다 보임
// - 행성 뒤로 가는 부분은 행성에 자연스럽게 가려짐
// - SkyController가 알려주는 하늘 색에 살짝 물들고, 밤에는 은은하게 빛남
Shader "FarmingSystem/Dreamy Planet Ring"
{
    Properties
    {
        _InnerColor("Inner Color (안쪽 색)", Color) = (0.85, 0.8, 1.0, 1)
        _OuterColor("Outer Color (바깥 색)", Color) = (1.0, 0.82, 0.9, 1)
        _InnerRadius("Inner Radius (뚫린 구멍 크기, 0~1)", Range(0.1, 0.95)) = 0.5
        _EdgeSoftness("Edge Softness (가장자리 부드러움)", Range(0.01, 0.3)) = 0.08
        _Opacity("Opacity (불투명도)", Range(0, 1)) = 0.65
        _StripeCount("Stripe Count (줄무늬 개수)", Range(0, 40)) = 14
        _StripeStrength("Stripe Strength (줄무늬 진하기)", Range(0, 1)) = 0.45
        _Seed("Seed (무늬 바꾸기)", Range(0, 100)) = 3
        _Brightness("Brightness (밝기)", Range(0.2, 2)) = 1
        _Haze("Haze (하늘 색에 물드는 정도)", Range(0, 1)) = 0.25
        _NightGlow("Night Glow (밤에 빛나는 정도)", Range(0, 2)) = 0.4
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "DreamyRing"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _InnerColor, _OuterColor;
                half _InnerRadius, _EdgeSoftness, _Opacity, _StripeCount, _StripeStrength, _Seed;
                half _Brightness, _Haze, _NightGlow;
            CBUFFER_END

            float _SkyNight;
            float4 _SkyHorizonColor;
            float4 _SkyTopColor;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            float Hash1(float x)
            {
                return frac(sin(x * 127.1 + _Seed * 311.7) * 43758.5453);
            }

            // 1D 노이즈 (반지름 방향 줄무늬 굵기를 들쭉날쭉하게)
            float Noise1(float x)
            {
                float i = floor(x);
                float f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(Hash1(i), Hash1(i + 1.0), f);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float r = length(i.uv - 0.5) * 2.0;               // 0 = 중심, 1 = 판 가장자리
                float band = saturate((r - _InnerRadius) / max(1.0 - _InnerRadius, 0.001)); // 고리 안 위치 0~1

                // 고리 모양 (안쪽 구멍, 바깥 가장자리 부드럽게)
                float mask = smoothstep(_InnerRadius, _InnerRadius + _EdgeSoftness, r)
                           * (1.0 - smoothstep(1.0 - _EdgeSoftness, 1.0, r));

                // 가는 줄무늬 여러 겹
                float stripes = Noise1(band * _StripeCount) * 0.6 + Noise1(band * _StripeCount * 2.7 + 5.0) * 0.4;
                float alpha = mask * _Opacity * lerp(1.0, stripes * 1.4, _StripeStrength);

                half3 col = lerp(_InnerColor.rgb, _OuterColor.rgb, band);
                col *= lerp(1.0, 0.8 + stripes * 0.4, _StripeStrength);
                col *= _Brightness;

                half3 skyCol = lerp(_SkyHorizonColor.rgb, _SkyTopColor.rgb, 0.4);
                col = lerp(col, skyCol, _Haze * _SkyHorizonColor.a);
                col += col * _NightGlow * _SkyNight;

                return half4(col, saturate(alpha));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
