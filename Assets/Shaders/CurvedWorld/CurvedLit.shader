// 곡면 월드(동숲·포코피아처럼 지평선이 둥글게 휘는) URP 셰이더
// - URP Lit 재질에서 바꿔 끼워도 텍스처(Base Map)·색(Base Color)·알파 자르기·양면 설정이 그대로 이어짐 (속성 이름이 같음)
// - 휘는 정도는 씬의 CurvedWorldController가 모든 재질에 한꺼번에 알려줌 (조절기가 없거나 꺼지면 평평)
// - 빛: 해(메인 라이트) + 그림자 + 주변광 + 추가 조명 + 안개 + SSAO 지원. 반짝임 없는 무광 (블록·나무 스타일에 맞춤)
// - 그림자를 만드는 패스도 똑같이 휘어서 그림자가 물체와 어긋나지 않음
// - 바람: 재질의 Wind Sway를 올리면 날씨 시스템의 바람에 맞춰 나무·풀이 흔들림 (태풍 때 강하게)
Shader "FarmingSystem/Curved Lit"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map (텍스처)", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color (색)", Color) = (1, 1, 1, 1)
        _Smoothness("Smoothness (반짝임, 0 = 무광)", Range(0, 1)) = 0
        [Toggle(_ALPHATEST_ON)] _AlphaClip("Alpha Clipping (투명한 부분 잘라내기 - 잎 등)", Float) = 0
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull (Off = 양면)", Float) = 2
        [Toggle] _CurveIgnore("Curve Ignore (켜면 이 재질만 안 휨)", Float) = 0
        [Toggle] _UseVertexColor("Use Vertex Color (메시에 칠해진 색 곱하기 - 물 밑바닥 깊이 색 등)", Float) = 0
        _WindSway("Wind Sway (바람에 흔들리는 정도 - 나무 잎·풀만 0.3~1, 블록은 0)", Range(0, 2)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" "UniversalMaterialType" = "SimpleLit" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Smoothness;
            half _Cutoff;
            half _CurveIgnore;
            half _WindSway;
            half _UseVertexColor;
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        // CurvedWorldController가 매 프레임 넣어주는 값 (모든 재질 공통)
        float4 _CurveOrigin;    // 휘는 기준점 (보통 카메라 위치) - 여기서 멀어질수록 아래로 내려감
        float4 _CurveForward;   // 앞 방향 (xz). 앞뒤로 멀어지는 만큼 휨 (동숲처럼 굴러가는 통나무 느낌)
        float _CurveStrength;   // 휘는 세기 (0 = 평평)
        float _CurveSide;       // 옆으로도 휘는 정도 (0 = 앞뒤로만 휨 / 1 = 사방으로 둥근 행성)
        float _CurveFlat;       // 기준점에서 이 거리(m)까지는 전혀 안 휨 → 가까운 산·나무는 원근감 그대로, 먼 지평선만 휨

        // 날씨 시스템(WeatherVisuals)이 넣어주는 바람 값: x = 세기(맑음 0.15 / 비 0.35 / 태풍 1), y = 흔들리는 빠르기, zw = 바람 방향(xz)
        float4 _WindParams;

        // 바람 흔들림: 물체 밑동(피벗)보다 높은 부분일수록 바람 방향으로 더 많이 흔들림 (나무 꼭대기가 가장 많이)
        // Wind Sway가 0인 재질(블록 등)은 그대로. 모든 패스가 같이 써서 그림자·깊이도 같이 흔들림
        float3 ApplyWind(float3 positionWS)
        {
            if (_WindSway <= 0.0 || _WindParams.x <= 0.0) return positionWS;
            float3 originWS = TransformObjectToWorld(float3(0, 0, 0));
            float h = max(positionWS.y - originWS.y, 0.0);
            float phase = dot(originWS.xz, float2(0.37, 0.71));           // 나무마다 흔들리는 박자를 다르게
            float t = _Time.y * _WindParams.y;
            float sway = sin(t + phase) * 0.6 + sin(t * 2.3 + phase * 1.7) * 0.25 + 0.5; // 바람 쪽으로 기운 채 흔들림
            positionWS.xz += _WindParams.zw * (sway * _WindParams.x * _WindSway * h * h * 0.04);
            return positionWS;
        }

        // 월드 좌표를 휜 좌표로: 기준점에서의 거리² 만큼 아래로 (바람 흔들림도 여기서 같이 적용)
        float3 CurveWorld(float3 positionWS)
        {
            positionWS = ApplyWind(positionWS);
            if (_CurveIgnore > 0.5) return positionWS;
            float2 d = positionWS.xz - _CurveOrigin.xz;
            float2 f = _CurveForward.xz;
            float z = dot(d, f);                       // 앞뒤 거리
            float x = d.x * f.y - d.y * f.x;           // 옆 거리
            // Side = 1이면 둥근 거리 → 카메라를 어느 쪽으로 돌려도 똑같이 휨 (기준점이 플레이어일 때)
            float r = max(sqrt(z * z + _CurveSide * x * x) - _CurveFlat, 0); // 평평한 구간 이후의 거리
            positionWS.y -= r * r * _CurveStrength;
            return positionWS;
        }

        half4 SampleAlbedo(float2 uv)
        {
            half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
            #if defined(_ALPHATEST_ON)
                clip(c.a - _Cutoff);
            #endif
            return c;
        }
        ENDHLSL

        // ---------------- 화면에 그리기 ----------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _FORWARD_PLUS _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ALPHATEST_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                half   fogFactor  : TEXCOORD3;
                half4  color      : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                float3 ws = CurveWorld(TransformObjectToWorld(v.positionOS.xyz));
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 albedo = SampleAlbedo(i.uv);
                albedo.rgb *= lerp(half3(1, 1, 1), i.color.rgb, _UseVertexColor); // 정점 색 (켠 재질만)
                float3 n = normalize(i.normalWS);

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.positionCS = i.positionCS;
                inputData.normalWS = n;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.fogCoord = i.fogFactor;
                inputData.bakedGI = SampleSH(n);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo.rgb;
                s.alpha = 1;
                s.smoothness = _Smoothness;
                s.specular = _Smoothness.xxx * 0.2;
                s.occlusion = 1;
                s.normalTS = half3(0, 0, 1);

                half4 color = UniversalFragmentBlinnPhong(inputData, s);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return half4(color.rgb, 1);
            }
            ENDHLSL
        }

        // ---------------- 그림자 만들기 (휜 모양 그대로) ----------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma shader_feature_local _ALPHATEST_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings shadowVert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 ws = CurveWorld(TransformObjectToWorld(v.positionOS.xyz));
                float3 nws = TransformObjectToWorldNormal(v.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDir = normalize(_LightPosition - ws);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(ws, nws, lightDir));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs;
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 shadowFrag(Varyings i) : SV_Target
            {
                SampleAlbedo(i.uv);
                return 0;
            }
            ENDHLSL
        }

        // ---------------- 깊이 (물 셰이더의 물가 거품·깊이 색, SSAO 등에 필요) ----------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex depthVert
            #pragma fragment depthFrag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ALPHATEST_ON

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings depthVert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformWorldToHClip(CurveWorld(TransformObjectToWorld(v.positionOS.xyz)));
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half depthFrag(Varyings i) : SV_Target
            {
                SampleAlbedo(i.uv);
                return i.positionCS.z;
            }
            ENDHLSL
        }

        // ---------------- 깊이 + 방향 (SSAO 등) ----------------
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex dnVert
            #pragma fragment dnFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma shader_feature_local _ALPHATEST_ON

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
            };

            Varyings dnVert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformWorldToHClip(CurveWorld(TransformObjectToWorld(v.positionOS.xyz)));
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 dnFrag(Varyings i) : SV_Target
            {
                SampleAlbedo(i.uv);
                float3 n = normalize(i.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = PackNormalOctQuadEncode(n);
                    float2 rg = saturate(oct * 0.5 + 0.5);
                    return half4(PackFloat2To888(rg), 0);
                #else
                    return half4(n, 0);
                #endif
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
