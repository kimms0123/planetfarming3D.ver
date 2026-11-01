// 몽환 행성 빛무리 (행성 둘레로 퍼지는 부드러운 빛)
// - Quad(사각판)에 씌우면 항상 카메라를 바라보며 동그란 빛무리를 그림 (돌려도 원 모양 유지)
// - 행성 테두리에서 가장 밝고 바깥으로 갈수록 서서히 사라짐 → 대기에 감싸인 몽환적인 느낌
// - 밤에는 더 밝아짐 (SkyController의 밤 정도를 따라감)
// - 빛을 더하는 방식(Additive)이라 어두운 밤하늘에서 특히 예쁘게 보임
// - Quad 크기는 행성보다 크게 (몽환 행성 만들기 도구가 자동으로 맞춰 줌)
Shader "FarmingSystem/Dreamy Planet Halo"
{
    Properties
    {
        _GlowColor("Glow Color (빛무리 색)", Color) = (1.0, 0.8, 0.95, 1)
        _Intensity("Intensity (밝기)", Range(0, 3)) = 0.8
        _PlanetRadius("Planet Radius (판 크기 대비 행성 반지름, 0~1)", Range(0.1, 0.95)) = 0.62
        _Falloff("Falloff (클수록 빨리 사라짐)", Range(0.5, 8)) = 2.2
        _NightBoost("Night Boost (밤에 더 밝아지는 정도)", Range(0, 3)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "DreamyHalo"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _GlowColor;
                half _Intensity, _PlanetRadius, _Falloff, _NightBoost;
            CBUFFER_END

            float _SkyNight; // SkyController가 넣어 줌 (0 = 낮, 1 = 밤)

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

            Varyings vert(Attributes v)
            {
                Varyings o;
                // 판의 중심과 크기만 가져오고, 방향은 항상 카메라를 향하게 (빌보드)
                float3 center = TransformObjectToWorld(float3(0, 0, 0));
                float scale = length(TransformObjectToWorldDir(float3(1, 0, 0), false));
                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up = UNITY_MATRIX_V[1].xyz;
                float3 positionWS = center + (v.positionOS.x * right + v.positionOS.y * up) * scale;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // 판 중심에서의 거리 (0 = 중심, 1 = 판 가장자리)
                float r = length(i.uv - 0.5) * 2.0;
                // 행성 테두리(_PlanetRadius)에서 가장 밝고 바깥으로 사라짐
                float outside = saturate((r - _PlanetRadius) / max(1.0 - _PlanetRadius, 0.001));
                float glow = pow(1.0 - outside, _Falloff);
                glow *= smoothstep(1.0, 0.92, r);                 // 판 가장자리에서 딱 잘리지 않게
                glow *= smoothstep(_PlanetRadius * 0.7, _PlanetRadius, r); // 행성 안쪽은 비움
                half boost = 1.0 + _SkyNight * _NightBoost;
                return half4(_GlowColor.rgb * glow * _Intensity * boost, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
