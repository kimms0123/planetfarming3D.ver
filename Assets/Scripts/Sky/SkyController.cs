using System;
using UnityEngine;
using FarmingSystem.Core;

namespace FarmingSystem.World
{
    // 하늘 조절기 (씬에 하나)
    // GameClock의 시각(시·분)에 맞춰 하늘 색·별·해·조명을 하루 동안 부드럽게 바꿔 줌
    // - 낮: 연한 하늘색 + 분홍 지평선 / 노을: 주황·분홍 / 밤: 남색·보라 + 별·성운
    // - 해(Directional Light)가 아침에 동쪽에서 떠서 저녁에 서쪽으로 지고, 밤엔 달빛(푸르고 어두운 빛)으로 바뀜
    // - 주변광(Ambient)과 안개 색도 하늘 색에 맞춰 같이 바꿈 → 맵 전체 분위기가 시간대에 맞게 변함
    // - 잠자기 등으로 시간이 확 바뀌어도 몇 초에 걸쳐 자연스럽게 넘어감
    // - 실행 안 해도 Preview Hour로 시간대별 모습을 미리 볼 수 있음
    [ExecuteAlways]
    public class SkyController : MonoBehaviour
    {
        // 한 시각의 하늘 모습 (이 시각들 사이는 자동으로 섞임)
        [Serializable]
        public class SkyKey
        {
            [Tooltip("시각 (0 ~ 24). 6.5 = 오전 6시 30분")]
            [Range(0f, 24f)] public float hour;
            [Header("하늘")]
            public Color top = Color.blue;
            public Color horizon = Color.white;
            public Color bottom = Color.gray;
            [Tooltip("별·성운 세기 (0 = 안 보임, 1 = 밤하늘 최대)")]
            [Range(0f, 1f)] public float stars;
            [Tooltip("해 원반 보이기 (밤엔 0)")]
            [Range(0f, 1f)] public float sunDisc = 1f;
            [Header("조명")]
            public Color lightColor = Color.white;
            [Range(0f, 3f)] public float lightIntensity = 1f;
            [Header("주변광 (그림자 쪽 밝기)")]
            public Color ambientSky = Color.gray;
            public Color ambientEquator = Color.gray;
            public Color ambientGround = Color.gray;
            [Header("안개")]
            public Color fog = Color.gray;

            public SkyKey() { }

            public SkyKey(float hour, Color top, Color horizon, Color bottom, float stars, float sunDisc,
                          Color lightColor, float lightIntensity, Color ambientSky, Color ambientEquator, Color ambientGround, Color fog)
            {
                this.hour = hour; this.top = top; this.horizon = horizon; this.bottom = bottom;
                this.stars = stars; this.sunDisc = sunDisc;
                this.lightColor = lightColor; this.lightIntensity = lightIntensity;
                this.ambientSky = ambientSky; this.ambientEquator = ambientEquator; this.ambientGround = ambientGround;
                this.fog = fog;
            }
        }

        // 화면에 실제로 보이는 값 (목표값을 향해 부드럽게 따라감)
        private struct SkyState
        {
            public Color top, horizon, bottom, lightColor, ambientSky, ambientEquator, ambientGround, fog;
            public float stars, sunDisc, lightIntensity;

            public static SkyState Lerp(SkyState a, SkyState b, float t)
            {
                SkyState s;
                s.top = Color.Lerp(a.top, b.top, t);
                s.horizon = Color.Lerp(a.horizon, b.horizon, t);
                s.bottom = Color.Lerp(a.bottom, b.bottom, t);
                s.lightColor = Color.Lerp(a.lightColor, b.lightColor, t);
                s.ambientSky = Color.Lerp(a.ambientSky, b.ambientSky, t);
                s.ambientEquator = Color.Lerp(a.ambientEquator, b.ambientEquator, t);
                s.ambientGround = Color.Lerp(a.ambientGround, b.ambientGround, t);
                s.fog = Color.Lerp(a.fog, b.fog, t);
                s.stars = Mathf.Lerp(a.stars, b.stars, t);
                s.sunDisc = Mathf.Lerp(a.sunDisc, b.sunDisc, t);
                s.lightIntensity = Mathf.Lerp(a.lightIntensity, b.lightIntensity, t);
                return s;
            }

            public static SkyState From(SkyKey k)
            {
                SkyState s;
                s.top = k.top; s.horizon = k.horizon; s.bottom = k.bottom;
                s.lightColor = k.lightColor; s.lightIntensity = k.lightIntensity;
                s.ambientSky = k.ambientSky; s.ambientEquator = k.ambientEquator; s.ambientGround = k.ambientGround;
                s.fog = k.fog; s.stars = k.stars; s.sunDisc = k.sunDisc;
                return s;
            }
        }

        [Header("연결")]
        [Tooltip("하늘 재질 (셰이더: FarmingSystem/Planet Sky). 비우면 Lighting 창에 들어 있는 Skybox 재질을 사용")]
        [SerializeField] private Material skyMaterial;
        [Tooltip("해 역할 Directional Light. 비우면 씬에서 자동으로 찾음")]
        [SerializeField] private Light sunLight;

        [Header("시간")]
        [Tooltip("켜면 GameClock의 시각을 따라감 (실행 중에만). 끄면 아래 Preview Hour 고정")]
        [SerializeField] private bool followGameClock = true;
        [Tooltip("미리보기 시각 (실행 안 할 때, 또는 Follow Game Clock을 껐을 때 사용)")]
        [SerializeField, Range(0f, 24f)] private float previewHour = 12f;
        [Tooltip("시간이 확 바뀔 때(잠자기 등) 새 하늘로 넘어가는 빠르기. 클수록 빨리 바뀜 (1 = 약 3초)")]
        [SerializeField, Range(0.2f, 10f)] private float transitionSpeed = 1f;

        [Header("해 · 달 움직임")]
        [Tooltip("해 뜨는 시각")]
        [SerializeField, Range(3f, 9f)] private float sunriseHour = 6f;
        [Tooltip("해 지는 시각")]
        [SerializeField, Range(16f, 21f)] private float sunsetHour = 19f;
        [Tooltip("한낮에 해가 가장 높이 올라가는 각도 (90 = 머리 위). 낮을수록 그림자가 길게 늘어짐")]
        [SerializeField, Range(10f, 90f)] private float sunMaxElevation = 60f;
        [Tooltip("밤에 달이 가장 높이 올라가는 각도")]
        [SerializeField, Range(10f, 90f)] private float moonMaxElevation = 45f;
        [Tooltip("해가 지나가는 방향 (도). 0 = 동쪽(+X 쪽 하늘)에서 떠서 서쪽으로 짐. 맵이 예쁘게 보이는 방향으로 돌리면 됨")]
        [SerializeField, Range(0f, 360f)] private float sunPathYaw = 0f;
        [Tooltip("지평선 근처에서 빛을 약하게 (해 뜨고 질 때 빛 방향이 바뀌는 순간을 티 안 나게)")]
        [SerializeField, Range(0f, 30f)] private float horizonFadeDegrees = 8f;

        [Header("달 원반")]
        [Tooltip("밤하늘에 달 원반을 그림 (해 원반 대신)")]
        [SerializeField] private bool showMoonDisc = true;
        [SerializeField] private Color moonDiscColor = new Color(0.85f, 0.85f, 1f, 1f);
        [SerializeField, Range(0f, 0.2f)] private float moonDiscSize = 0.03f;
        [SerializeField, Range(0f, 1f)] private float moonDiscBrightness = 0.7f;
        [Tooltip("낮 해 원반 색 · 크기")]
        [SerializeField] private Color sunDiscColor = new Color(1f, 0.95f, 0.85f, 1f);
        [SerializeField, Range(0f, 0.2f)] private float sunDiscSize = 0.04f;

        [Header("별 회전")]
        [Tooltip("밤하늘 별·성운이 천천히 도는 속도 (도/초, 실행 중에만)")]
        [SerializeField, Range(0f, 5f)] private float starSpinSpeed = 0.3f;

        [Header("같이 바꿀 것")]
        [Tooltip("주변광(Environment Lighting)을 3색(Gradient)으로 바꾸고 시간대에 맞춰 색을 바꿈")]
        [SerializeField] private bool controlAmbient = true;
        [Tooltip("안개 색을 지평선 색에 맞춰 바꿈 (안개를 쓰는 경우)")]
        [SerializeField] private bool controlFog = true;
        [Tooltip("해 빛 색·밝기를 바꿈")]
        [SerializeField] private bool controlLight = true;
        [Tooltip("해 빛 방향(그림자 방향)을 시간에 맞춰 돌림")]
        [SerializeField] private bool controlLightDirection = true;

        [Header("시간대별 하늘 (시각 순서 상관없음)")]
        [SerializeField] private SkyKey[] keys = DefaultKeys();

        private Material runtimeMaterial;   // 실행 중엔 복사본을 써서 원본 재질 에셋이 바뀌지 않게
        private SkyState current;
        private bool hasCurrent;
        private Quaternion currentLightRotation;
        private float starRotation;

        private static readonly int TopId = Shader.PropertyToID("_TopColor");
        private static readonly int HorizonId = Shader.PropertyToID("_HorizonColor");
        private static readonly int BottomId = Shader.PropertyToID("_BottomColor");
        private static readonly int NightId = Shader.PropertyToID("_NightStrength");
        private static readonly int NightRotId = Shader.PropertyToID("_NightRotation");
        private static readonly int SunColorId = Shader.PropertyToID("_SunColor");
        private static readonly int SunSizeId = Shader.PropertyToID("_SunSize");
        private static readonly int SunVisibleId = Shader.PropertyToID("_SunVisible");
        private static readonly int SkyNightId = Shader.PropertyToID("_SkyNight");
        private static readonly int SkyHorizonId = Shader.PropertyToID("_SkyHorizonColor");
        private static readonly int SkyTopId = Shader.PropertyToID("_SkyTopColor");

        // 지금 하늘이 따르는 시각 (0 ~ 24)
        public float CurrentHour
        {
            get
            {
                if (Application.isPlaying && followGameClock && GameClock.Instance != null)
                    return GameClock.Instance.CurrentHour + GameClock.Instance.CurrentMinute / 60f;
                return previewHour;
            }
        }

        // 미리보기 시각을 코드에서 바꾸기 (PV 촬영 등)
        public float PreviewHour
        {
            get => previewHour;
            set => previewHour = Mathf.Repeat(value, 24f);
        }

        public bool FollowGameClock
        {
            get => followGameClock;
            set => followGameClock = value;
        }

        private void OnEnable()
        {
            hasCurrent = false;
            Apply(true);
        }

        private void OnValidate()
        {
            hasCurrent = false;
            Apply(true);
        }

        private void OnDisable()
        {
            // 실행이 끝나면 원래 재질로 되돌림
            if (runtimeMaterial != null)
            {
                if (RenderSettings.skybox == runtimeMaterial && skyMaterial != null) RenderSettings.skybox = skyMaterial;
                if (Application.isPlaying) Destroy(runtimeMaterial); else DestroyImmediate(runtimeMaterial);
                runtimeMaterial = null;
            }
        }

        private void Update()
        {
            Apply(!Application.isPlaying);
        }

        private void Apply(bool instant)
        {
            if (keys == null || keys.Length == 0) return;
            Material mat = GetMaterial();
            if (sunLight == null) sunLight = FindSun();

            float hour = CurrentHour;
            SkyState target = Evaluate(hour);
            Quaternion targetRotation = LightRotation(hour, out bool isDay, out float elevation);

            // 지평선 근처에선 빛을 약하게 (해 ↔ 달 방향이 바뀌는 순간을 가림)
            float fade = horizonFadeDegrees <= 0f ? 1f : Mathf.Lerp(0.25f, 1f, Mathf.Clamp01(elevation / horizonFadeDegrees));
            target.lightIntensity *= fade;

            if (instant || !hasCurrent)
            {
                current = target;
                currentLightRotation = targetRotation;
                hasCurrent = true;
            }
            else
            {
                float t = 1f - Mathf.Exp(-transitionSpeed * Time.deltaTime);
                current = SkyState.Lerp(current, target, t);
                // 빛 방향이 반대편으로 넘어갈 때(해 → 달)는 바로 바꿈 (하늘을 가로지르며 도는 것 방지)
                if (Quaternion.Angle(currentLightRotation, targetRotation) > 60f) currentLightRotation = targetRotation;
                else currentLightRotation = Quaternion.Slerp(currentLightRotation, targetRotation, t);
            }

            if (Application.isPlaying) starRotation = Mathf.Repeat(starRotation + starSpinSpeed * Time.deltaTime, 360f);

            // 하늘 재질
            if (mat != null)
            {
                mat.SetColor(TopId, current.top);
                mat.SetColor(HorizonId, current.horizon);
                mat.SetColor(BottomId, current.bottom);
                mat.SetFloat(NightId, current.stars);
                if (Application.isPlaying) mat.SetFloat(NightRotId, starRotation);

                // 낮엔 해 원반, 밤엔 달 원반 (빛 방향에 그려짐)
                if (isDay)
                {
                    mat.SetColor(SunColorId, sunDiscColor);
                    mat.SetFloat(SunSizeId, sunDiscSize);
                    mat.SetFloat(SunVisibleId, current.sunDisc);
                }
                else
                {
                    mat.SetColor(SunColorId, moonDiscColor);
                    mat.SetFloat(SunSizeId, moonDiscSize);
                    mat.SetFloat(SunVisibleId, showMoonDisc ? moonDiscBrightness : 0f);
                }
            }

            // 해 빛
            if (sunLight != null)
            {
                if (controlLight)
                {
                    sunLight.color = current.lightColor;
                    sunLight.intensity = current.lightIntensity;
                }
                if (controlLightDirection) sunLight.transform.rotation = currentLightRotation;
            }

            // 주변광
            if (controlAmbient)
            {
                if (RenderSettings.ambientMode != UnityEngine.Rendering.AmbientMode.Trilight)
                    RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = current.ambientSky;
                RenderSettings.ambientEquatorColor = current.ambientEquator;
                RenderSettings.ambientGroundColor = current.ambientGround;
            }

            // 안개
            if (controlFog) RenderSettings.fogColor = current.fog;

            // 몽환 행성 셰이더 등에서 쓰는 하늘 정보 (밤 정도, 하늘 색) → 행성이 하늘 색에 물들고 밤에 은은하게 빛남
            Shader.SetGlobalFloat(SkyNightId, current.stars);
            Shader.SetGlobalColor(SkyHorizonId, current.horizon);
            Shader.SetGlobalColor(SkyTopId, current.top);
        }

        // 시각에 맞는 하늘: 앞뒤 키 두 개를 찾아 섞음 (자정을 넘어가도 이어짐)
        private SkyState Evaluate(float hour)
        {
            hour = Mathf.Repeat(hour, 24f);
            SkyKey prev = null, next = null;
            float prevDist = float.MaxValue, nextDist = float.MaxValue;
            foreach (SkyKey k in keys)
            {
                if (k == null) continue;
                float back = Mathf.Repeat(hour - k.hour, 24f);   // 이 키가 몇 시간 전인지
                float fwd = Mathf.Repeat(k.hour - hour, 24f);    // 이 키가 몇 시간 뒤인지
                if (back < prevDist) { prevDist = back; prev = k; }
                if (fwd < nextDist) { nextDist = fwd; next = k; }
            }
            if (prev == null) return default;
            if (next == null || prev == next || prevDist + nextDist <= 0.0001f) return SkyState.From(prev);
            float t = prevDist / (prevDist + nextDist);
            t = t * t * (3f - 2f * t); // 부드럽게
            return SkyState.Lerp(SkyState.From(prev), SkyState.From(next), t);
        }

        // 해(낮) 또는 달(밤)의 방향
        private Quaternion LightRotation(float hour, out bool isDay, out float elevation)
        {
            hour = Mathf.Repeat(hour, 24f);
            float dayLength = Mathf.Max(0.1f, sunsetHour - sunriseHour);
            float progress;   // 0 = 동쪽 지평선, 1 = 서쪽 지평선
            float maxElevation;
            if (hour >= sunriseHour && hour <= sunsetHour)
            {
                isDay = true;
                progress = (hour - sunriseHour) / dayLength;
                maxElevation = sunMaxElevation;
            }
            else
            {
                isDay = false;
                float nightLength = 24f - dayLength;
                float sinceSunset = Mathf.Repeat(hour - sunsetHour, 24f);
                progress = sinceSunset / nightLength;
                maxElevation = moonMaxElevation;
            }
            elevation = Mathf.Sin(progress * Mathf.PI) * maxElevation;
            // 동쪽(+X)에서 서쪽(-X)으로: yaw -90 → +90 (빛이 비추는 방향 기준)
            float yaw = sunPathYaw - 90f + progress * 180f;
            return Quaternion.Euler(Mathf.Max(elevation, 1f), yaw, 0f);
        }

        private Material GetMaterial()
        {
            Material source = skyMaterial != null ? skyMaterial : RenderSettings.skybox;
            if (source == null) return null;
            if (skyMaterial == null && source != runtimeMaterial) skyMaterial = source;
            if (!Application.isPlaying) return skyMaterial; // 편집 중엔 원본 재질에 바로 미리보기

            if (runtimeMaterial == null)
            {
                runtimeMaterial = new Material(skyMaterial) { name = skyMaterial.name + " (Runtime)" };
            }
            if (RenderSettings.skybox != runtimeMaterial) RenderSettings.skybox = runtimeMaterial;
            return runtimeMaterial;
        }

        private static Light FindSun()
        {
            if (RenderSettings.sun != null) return RenderSettings.sun;
            foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type == LightType.Directional) return l;
            }
            return null;
        }

        // 미리보기 메뉴 (컴포넌트 ⋮ 메뉴에서 선택)
        [ContextMenu("미리보기: 새벽 (5:30)")] private void PreviewDawn() => SetPreview(5.5f);
        [ContextMenu("미리보기: 아침 (8:00)")] private void PreviewMorning() => SetPreview(8f);
        [ContextMenu("미리보기: 한낮 (12:00)")] private void PreviewMidday() => SetPreview(12f);
        [ContextMenu("미리보기: 오후 (16:00)")] private void PreviewAfternoon() => SetPreview(16f);
        [ContextMenu("미리보기: 노을 (18:30)")] private void PreviewSunset() => SetPreview(18.5f);
        [ContextMenu("미리보기: 밤 (23:00)")] private void PreviewNight() => SetPreview(23f);

        private void SetPreview(float h)
        {
            previewHour = h;
            hasCurrent = false;
            Apply(true);
        }

        [ContextMenu("시간대별 하늘을 기본값으로 되돌리기")]
        private void ResetKeys()
        {
            keys = DefaultKeys();
            hasCurrent = false;
            Apply(true);
        }

        private static Color C(float r, float g, float b) => new Color(r, g, b, 1f);

        // 기본 하늘: 파스텔 낮 (하늘색·분홍) / 남보라 밤
        private static SkyKey[] DefaultKeys()
        {
            Color nightTop = C(0.05f, 0.05f, 0.20f), nightHorizon = C(0.30f, 0.16f, 0.42f), nightBottom = C(0.12f, 0.08f, 0.22f);
            Color moon = C(0.55f, 0.60f, 1.00f);
            return new[]
            {
                // 한밤
                new SkyKey(0f,    nightTop, nightHorizon, nightBottom, 1f, 0f,
                           moon, 0.25f, C(0.20f, 0.20f, 0.42f), C(0.24f, 0.17f, 0.36f), C(0.10f, 0.08f, 0.16f), C(0.20f, 0.14f, 0.32f)),
                // 새벽 직전 (아직 밤)
                new SkyKey(4.5f,  C(0.08f, 0.08f, 0.26f), C(0.40f, 0.22f, 0.48f), nightBottom, 0.9f, 0f,
                           moon, 0.25f, C(0.22f, 0.22f, 0.44f), C(0.28f, 0.20f, 0.38f), C(0.10f, 0.08f, 0.16f), C(0.26f, 0.18f, 0.36f)),
                // 동틀 녘: 분홍 지평선
                new SkyKey(6.2f,  C(0.42f, 0.50f, 0.85f), C(1.00f, 0.68f, 0.74f), C(0.80f, 0.58f, 0.72f), 0.15f, 1f,
                           C(1.00f, 0.75f, 0.70f), 0.6f, C(0.55f, 0.55f, 0.75f), C(0.80f, 0.62f, 0.68f), C(0.30f, 0.25f, 0.30f), C(0.92f, 0.70f, 0.78f)),
                // 아침: 연한 하늘색 + 분홍빛
                new SkyKey(8.5f,  C(0.55f, 0.78f, 0.98f), C(0.98f, 0.82f, 0.90f), C(0.86f, 0.80f, 0.92f), 0f, 1f,
                           C(1.00f, 0.95f, 0.90f), 1.0f, C(0.68f, 0.78f, 0.95f), C(0.85f, 0.78f, 0.85f), C(0.38f, 0.35f, 0.36f), C(0.92f, 0.84f, 0.92f)),
                // 한낮: 맑은 하늘색
                new SkyKey(12f,   C(0.45f, 0.72f, 0.98f), C(0.82f, 0.90f, 1.00f), C(0.82f, 0.86f, 0.95f), 0f, 1f,
                           C(1.00f, 0.98f, 0.95f), 1.15f, C(0.70f, 0.82f, 1.00f), C(0.80f, 0.85f, 0.92f), C(0.40f, 0.38f, 0.36f), C(0.84f, 0.90f, 1.00f)),
                // 오후: 하늘색 + 분홍 지평선
                new SkyKey(16f,   C(0.50f, 0.70f, 0.96f), C(0.98f, 0.80f, 0.88f), C(0.86f, 0.78f, 0.90f), 0f, 1f,
                           C(1.00f, 0.90f, 0.85f), 1.0f, C(0.68f, 0.76f, 0.95f), C(0.86f, 0.76f, 0.82f), C(0.38f, 0.34f, 0.35f), C(0.94f, 0.82f, 0.90f)),
                // 노을: 주황·분홍
                new SkyKey(18.4f, C(0.44f, 0.44f, 0.80f), C(1.00f, 0.60f, 0.55f), C(0.76f, 0.50f, 0.62f), 0f, 1f,
                           C(1.00f, 0.62f, 0.48f), 0.75f, C(0.55f, 0.50f, 0.75f), C(0.88f, 0.58f, 0.58f), C(0.30f, 0.22f, 0.26f), C(0.95f, 0.64f, 0.62f)),
                // 해 진 뒤: 보라빛
                new SkyKey(19.8f, C(0.15f, 0.12f, 0.38f), C(0.58f, 0.30f, 0.60f), C(0.26f, 0.15f, 0.35f), 0.55f, 0f,
                           moon, 0.3f, C(0.26f, 0.22f, 0.46f), C(0.38f, 0.24f, 0.44f), C(0.12f, 0.09f, 0.18f), C(0.36f, 0.22f, 0.44f)),
                // 밤: 남색·보라 + 별
                new SkyKey(21.5f, nightTop, nightHorizon, nightBottom, 1f, 0f,
                           moon, 0.25f, C(0.20f, 0.20f, 0.42f), C(0.24f, 0.17f, 0.36f), C(0.10f, 0.08f, 0.16f), C(0.20f, 0.14f, 0.32f)),
            };
        }
    }
}