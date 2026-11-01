// 몽환 행성 (하늘 배경용 행성 표면)
// - 파스텔 색 3개가 물결치는 띠 무늬로 섞임 (가스 행성 느낌). 띠가 천천히 흘러감
// - 해/달 반대쪽은 까맣지 않고 연보라 그림자로 부드럽게 (낮에도 밤에도 파스텔 유지)
// - 테두리가 은은하게 빛남 (림 라이트) → 대기가 감싼 듯한 몽환적인 느낌
// - SkyController가 알려주는 하늘 색에 살짝 물들고(Haze), 밤에는 스스로 은은하게 빛남(Night Glow)
// - 안개·조명 세기의 영향을 받지 않음 → 아주 멀리 있어도 뿌옇거나 까맣게 안 됨
// 사용: Tools → 행성농사일지 → 하늘 → 몽환 행성 만들기 (자동으로 이 재질을 만들어 붙여 줌)
Shader "FarmingSystem/Dreamy Planet"
{
    Properties
    {
        [Header(Colors)]
        _ColorA("Color A (띠 색 1)", Color) = (1.0, 0.78, 0.86, 1)
        _ColorB("Color B (띠 색 2)", Color) = (0.78, 0.74, 1.0, 1)
        _ColorC("Color C (띠 색 3)", Color) = (0.72, 0.92, 1.0, 1)

        [Header(Bands)]
        _BandCount("Band Count (띠 개수)", Range(0.5, 12)) = 3.5
        _BandWarp("Band Warp (띠 물결 세기)", Range(0, 3)) = 0.9
        _WarpScale("Warp Scale (물결 크기, 클수록 잘게)", Range(0.5, 8)) = 2.2
        _BandSoftness("Band Softness (띠 경계 부드러움)", Range(0.05, 1)) = 0.6
        _FlowSpeed("Flow Speed (띠 흐르는 속도)", Range(0, 0.2)) = 0.02
        _Seed("Seed (무늬 바꾸기)", Range(0, 100)) = 7

        [Header(Light)]
        _ShadowColor("Shadow Color (어두운 쪽 색)", Color) = (0.55, 0.5, 0.85, 1)
        _ShadowStrength("Shadow Strength (어두운 쪽 진하기)", Range(0, 1)) = 0.45
        _LightWrap("Light Wrap (밝은 쪽이 감싸는 정도)", Range(0, 1)) = 0.6

        [Header(Rim Glow)]
        _RimColor("Rim Color (테두리 빛)", Color) = (1.0, 0.85, 0.95, 1)
        _RimPower("Rim Power (클수록 테두리만 얇게)", Range(0.5, 8)) = 2.5
        _RimIntensity("Rim Intensity", Range(0, 3)) = 0.9

        [Header(Sky Blend)]
        _Brightness("Brightness (전체 밝기)", Range(0.2, 2)) = 1
        _Haze("Haze (하늘 색에 물드는 정도)", Range(0, 1)) = 0.25
        _NightGlow("Night Glow (밤에 스스로 빛나는 정도)", Range(0, 2)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "DreamyPlanet"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorA, _ColorB, _ColorC;
                half _BandCount, _BandWarp, _WarpScale, _BandSoftness, _FlowSpeed, _Seed;
                half4 _ShadowColor; half _ShadowStrength, _LightWrap;
                half4 _RimColor; half _RimPower, _RimIntensity;
                half _Brightness, _Haze, _NightGlow;
            CBUFFER_END

            // SkyController가 넣어주는 하늘 정보 (없으면 0 → 영향 없음)
            float _SkyNight;
            float4 _SkyHorizonColor;
            float4 _SkyTopColor;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 posOS      : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            // 간단한 3D 노이즈 (외부 파일 없이 여기서 다 계산 → 폴더를 옮겨도 안 깨짐)
            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash(i + float3(0, 0, 0)), Hash(i + float3(1, 0, 0)), f.x),
                                 lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x),
                                 lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            float Fbm(float3 p)
            {
                float v = 0.0, a = 0.5;
                for (int k = 0; k < 4; k++)
                {
                    v += Noise(p) * a;
                    p = p * 2.03 + 11.7;
                    a *= 0.5;
                }
                return v;
            }

            // 색 3개를 고리처럼 이어 부드럽게 섞기 (A → B → C → A)
            half3 Palette(float t)
            {
                t = frac(t) * 3.0;
                float s = _BandSoftness;
                half3 ab = lerp(_ColorA.rgb, _ColorB.rgb, smoothstep(0.5 - s * 0.5, 0.5 + s * 0.5, saturate(t)));
                half3 bc = lerp(_ColorB.rgb, _ColorC.rgb, smoothstep(0.5 - s * 0.5, 0.5 + s * 0.5, saturate(t - 1.0)));
                half3 ca = lerp(_ColorC.rgb, _ColorA.rgb, smoothstep(0.5 - s * 0.5, 0.5 + s * 0.5, saturate(t - 2.0)));
                return t < 1.0 ? ab : (t < 2.0 ? bc : ca);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.posOS = normalize(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 p = i.posOS;
                float time = _Time.y * _FlowSpeed;

                // 띠 무늬: 위아래(Y) 방향 띠 + 노이즈로 물결치게
                float3 q = p * _WarpScale + float3(_Seed, _Seed * 0.37, time * 3.0);
                float warp = Fbm(q) - 0.5;
                float t = p.y * _BandCount * 0.5 + warp * _BandWarp + time;
                half3 col = Palette(t);

                // 작은 얼룩으로 살짝 질감 (너무 매끈하지 않게)
                float speck = Fbm(p * _WarpScale * 4.0 + _Seed * 1.7 + time) - 0.5;
                col *= 1.0 + speck * 0.12;

                // 해/달 쪽은 밝게, 반대쪽은 연보라로 부드럽게 (빛 세기는 안 씀 → 밤에도 안 까매짐)
                float3 n = normalize(i.normalWS);
                float3 l = normalize(_MainLightPosition.xyz);
                float lit = saturate((dot(n, l) + _LightWrap) / (1.0 + _LightWrap));
                lit = smoothstep(0.0, 1.0, lit);
                half3 shadowCol = col * lerp(half3(1, 1, 1), _ShadowColor.rgb, _ShadowStrength);
                col = lerp(shadowCol, col, lit);

                // 테두리 빛
                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                float fres = pow(1.0 - saturate(dot(n, viewDir)), _RimPower);
                col += _RimColor.rgb * fres * _RimIntensity;

                col *= _Brightness;

                // 하늘 색에 살짝 물들기 (SkyController가 있을 때만 - alpha가 0이면 영향 없음)
                half3 skyCol = lerp(_SkyHorizonColor.rgb, _SkyTopColor.rgb, 0.4);
                col = lerp(col, skyCol, _Haze * _SkyHorizonColor.a);

                // 밤에 은은하게 빛남
                col += col * _NightGlow * _SkyNight;

                return half4(col, 1);
            }
            ENDHLSL
        }

        // 깊이만 쓰는 패스 (URP 깊이 텍스처에 행성이 들어가도록)
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorA, _ColorB, _ColorC;
                half _BandCount, _BandWarp, _WarpScale, _BandSoftness, _FlowSpeed, _Seed;
                half4 _ShadowColor; half _ShadowStrength, _LightWrap;
                half4 _RimColor; half _RimPower, _RimIntensity;
                half _Brightness, _Haze, _NightGlow;
            CBUFFER_END

            float4 vert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback Off
}
