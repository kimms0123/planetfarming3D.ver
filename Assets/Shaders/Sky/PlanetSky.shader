// 행성 하늘 스카이박스 (낮 = 연한 하늘색·분홍 / 밤 = 남보라·자주 + 별·성운)
// - 기본은 3색 그라데이션 (위 / 지평선 / 아래). 색은 SkyController가 시간대에 맞춰 부드럽게 바꿔 줌
// - Day Texture: 구름 등 낮 하늘 그림(큐브맵, 선택). 그라데이션 위에 색을 입혀 얹음 (Skybox Sampler 등)
// - Night Texture: 별·성운 그림(큐브맵, 선택). 밤에만 나타나고 천천히 돎 (Starfield Skybox, SpaceSkies 등)
// - 해: 씬의 메인 라이트(Directional Light) 방향에 해와 빛 번짐을 그림
// URP에서 Lighting 창(Window → Rendering → Lighting → Environment)의 Skybox Material에 이 재질을 넣어 사용
Shader "FarmingSystem/Planet Sky"
{
    Properties
    {
        [Header(Gradient)]
        _TopColor("Top Color (하늘 꼭대기)", Color) = (0.55, 0.75, 0.98, 1)
        _HorizonColor("Horizon Color (지평선)", Color) = (0.98, 0.78, 0.88, 1)
        _BottomColor("Bottom Color (지평선 아래)", Color) = (0.85, 0.75, 0.9, 1)
        _HorizonHeight("Horizon Height (지평선 높이)", Range(-0.5, 0.5)) = 0
        _HorizonSoftness("Horizon Softness (지평선 퍼짐)", Range(0.01, 1)) = 0.35

        [Header(Day Texture)]
        [NoScaleOffset] _DayTex("Day Texture (구름 등, 큐브맵)", Cube) = "black" {}
        _DayTexStrength("Day Texture Strength", Range(0, 1)) = 0
        _DayTexTint("Day Texture Tint", Color) = (1, 1, 1, 1)
        _DayTexUseGradient("Use Gradient Color (0 = 그림 색 그대로, 1 = 그림 밝기만 쓰고 색은 시간대 색)", Range(0, 1)) = 0.5
        _DayRotation("Day Rotation (도)", Range(0, 360)) = 0

        [Header(Night Texture)]
        [NoScaleOffset] _NightTex("Night Texture (별·성운, 큐브맵)", Cube) = "black" {}
        _NightStrength("Night Strength (0 = 낮, 1 = 밤)", Range(0, 1)) = 0
        _NightTint("Night Tint", Color) = (1, 0.9, 1, 1)
        _NightRotation("Night Rotation (도)", Range(0, 360)) = 0

        [Header(Sun)]
        _SunColor("Sun Color", Color) = (1, 0.95, 0.85, 1)
        _SunSize("Sun Size", Range(0, 0.2)) = 0.04
        _SunGlow("Sun Glow (빛 번짐)", Range(0, 2)) = 0.6
        _SunVisible("Sun Visible", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor, _HorizonColor, _BottomColor;
                half _HorizonHeight, _HorizonSoftness;
                half _DayTexStrength; half4 _DayTexTint; half _DayTexUseGradient; float _DayRotation;
                half _NightStrength; half4 _NightTint; float _NightRotation;
                half4 _SunColor; half _SunSize, _SunGlow, _SunVisible;
            CBUFFER_END

            TEXTURECUBE(_DayTex);   SAMPLER(sampler_DayTex);
            TEXTURECUBE(_NightTex); SAMPLER(sampler_NightTex);

            // _MainLightPosition (메인 라이트 = 해 방향)은 URP Core.hlsl 안에 이미 선언되어 있어서 따로 선언하지 않음

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dir        : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            // y축으로 돌리기 (하늘 그림 방향 맞추기·별 천천히 돌리기)
            float3 RotateY(float3 d, float degrees)
            {
                float r = radians(degrees);
                float s = sin(r), c = cos(r);
                return float3(c * d.x - s * d.z, d.y, s * d.x + c * d.z);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y - _HorizonHeight;

                // 3색 그라데이션: 지평선 → 위 / 지평선 → 아래
                half up = saturate(h / _HorizonSoftness);
                half down = saturate(-h / (_HorizonSoftness * 0.5));
                half3 col = lerp(_HorizonColor.rgb, _TopColor.rgb, smoothstep(0, 1, up));
                col = lerp(col, _BottomColor.rgb, smoothstep(0, 1, down));

                // 낮 하늘 그림 (구름 등)
                // - Use Gradient Color 0: 그림 색 그대로 (Tint로 색 조절)
                // - Use Gradient Color 1: 그림의 밝기(구름 모양)만 쓰고 색은 시간대 그라데이션 색 → 노을·아침 색이 그대로 유지됨
                half3 dayTex = SAMPLE_TEXTURECUBE(_DayTex, sampler_DayTex, RotateY(d, _DayRotation)).rgb;
                half luma = dot(dayTex, half3(0.299, 0.587, 0.114));
                half3 dayCol = lerp(dayTex * _DayTexTint.rgb, col * (0.5 + luma) * _DayTexTint.rgb, _DayTexUseGradient);
                col = lerp(col, dayCol, _DayTexStrength * (1 - _NightStrength));

                // 밤 하늘 (별·성운): 더해서 빛나게, 지평선 근처는 옅게
                half3 nightTex = SAMPLE_TEXTURECUBE(_NightTex, sampler_NightTex, RotateY(d, _NightRotation)).rgb;
                half horizonFade = saturate(h * 4 + 0.2);
                col += nightTex * _NightTint.rgb * _NightStrength * horizonFade;

                // 해 + 빛 번짐
                float3 sunDir = normalize(_MainLightPosition.xyz);
                float sd = dot(d, sunDir);
                half sun = smoothstep(1 - _SunSize * 0.02 - 0.0005, 1 - _SunSize * 0.02, sd);
                half glow = pow(saturate(sd), 64) * _SunGlow + pow(saturate(sd), 8) * _SunGlow * 0.25;
                col += _SunColor.rgb * (sun + glow) * _SunVisible * saturate(sunDir.y * 4 + 0.3);

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
