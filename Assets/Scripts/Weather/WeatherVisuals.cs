using System.Collections;
using UnityEngine;
using FarmingSystem.Core;
using FarmingSystem.World;

namespace FarmingSystem.Weather
{
    // 날씨 연출 (WeatherManager와 같은 오브젝트에 붙임)
    // - 빗방울: 카메라 위에서 떨어지는 파티클. 비는 곧게, 태풍은 굵고 많이 + 바람 방향으로 비스듬히
    // - 흐린 하늘: SkyController가 Overcast 값을 읽어 하늘·햇빛을 회색빛으로 어둡게 (비 0.75 / 태풍 1)
    // - 나무 흔들림: 곡면 셰이더(Curved Lit)에 바람 값을 보냄 (재질의 Wind Sway를 올린 나무·풀만 흔들림)
    // - 번개(태풍): 불규칙하게 번쩍 + 잠시 뒤 천둥소리 + 화면 흔들림
    // - 날아다니는 잎·파편(태풍): 바람 방향으로 화면을 가로질러 펄럭이며 날아감 (회오리 바람 기둥은 WeatherTornado)
    // - 눈(겨울): 겨울엔 비 대신 눈송이가 천천히 흩날리며 내림 (태풍이면 눈보라). 눈 파티클은 없으면 자동으로 만듦, 빗소리도 꺼짐
    // - 소리: 빗소리·바람 소리는 날씨 세기에 맞춰 서서히 커지고 작아짐 (소리 파일은 직접 넣기)
    // 날씨가 바뀌면 모든 값이 몇 초에 걸쳐 부드럽게 넘어감
    public class WeatherVisuals : MonoBehaviour
    {
        // SkyController 등에서 읽는 값 (0~1)
        public static float Overcast { get; private set; }
        /// <summary>태풍 정도 0~1 (회오리 바람 기둥 등이 읽음)</summary>
        public static float Storm { get; private set; }
        /// <summary>바람 방향 (xz, 길이 1)</summary>
        public static Vector2 WindDirection { get; private set; } = Vector2.right;
        public static float LightningFlash { get; private set; }
        /// <summary>지금 내리는 게 눈인지 (겨울 + 비/태풍). 다른 연출에서 읽어 쓸 수 있음</summary>
        public static bool IsSnowing { get; private set; }

        [Header("연결")]
        [Tooltip("빗방울 파티클 (날씨 시스템 만들기 도구가 만들어 줌)")]
        [SerializeField] private ParticleSystem rain;
        [Tooltip("번개 빛 (Directional Light, 평소엔 세기 0)")]
        [SerializeField] private Light lightningLight;
        [Tooltip("따라갈 카메라 (비우면 메인 카메라)")]
        [SerializeField] private Transform followCamera;

        [Header("빗방울")]
        [Tooltip("비 올 때 초당 빗방울 수")]
        [SerializeField] private float rainRate = 1500f;
        [Tooltip("태풍일 때 초당 빗방울 수")]
        [SerializeField] private float typhoonRate = 4000f;
        [Tooltip("빗방울이 생기는 높이 (카메라 위 m)")]
        [SerializeField] private float rainHeight = 14f;
        [Tooltip("태풍 때 빗방울이 옆으로 날리는 속도 (m/s)")]
        [SerializeField] private float typhoonSlant = 9f;

        [Header("눈 (겨울엔 비 대신)")]
        [Tooltip("겨울에 비 대신 눈 내리기")]
        [SerializeField] private bool snowInWinter = true;
        [Tooltip("눈송이 파티클 (비우면 자동으로 만듦)")]
        [SerializeField] private ParticleSystem snow;
        [Tooltip("눈 올 때 초당 눈송이 수")]
        [SerializeField] private float snowRate = 450f;
        [Tooltip("겨울 태풍(눈보라)일 때 초당 눈송이 수")]
        [SerializeField] private float blizzardRate = 1600f;
        [Tooltip("눈송이 떨어지는 속도 (m/s, 최소~최대)")]
        [SerializeField] private Vector2 snowFallSpeed = new Vector2(1.0f, 1.8f);
        [Tooltip("눈송이 크기 (m, 최소~최대)")]
        [SerializeField] private Vector2 snowSize = new Vector2(0.06f, 0.16f);
        [Tooltip("눈이 생기는 범위 (카메라 둘레 반지름 m)")]
        [SerializeField] private float snowRadius = 22f;
        [Tooltip("눈이 생기는 높이 (카메라 위 m)")]
        [SerializeField] private float snowHeight = 6f;
        [Tooltip("눈보라 때 옆으로 날리는 속도 (m/s)")]
        [SerializeField] private float blizzardSlant = 7f;

        [Header("흐림 정도 (0 ~ 1)")]
        [SerializeField, Range(0f, 1f)] private float rainOvercast = 0.75f;
        [SerializeField, Range(0f, 1f)] private float typhoonOvercast = 1f;

        [Header("바람 (나무 흔들림)")]
        [SerializeField, Range(0f, 2f)] private float clearWind = 0.15f;
        [SerializeField, Range(0f, 2f)] private float rainWind = 0.35f;
        [SerializeField, Range(0f, 2f)] private float typhoonWind = 1f;
        [Tooltip("흔들리는 빠르기 (맑음 → 태풍으로 갈수록 이 값의 2배까지)")]
        [SerializeField] private float windSpeed = 1.6f;
        [Tooltip("바람 방향 (도, 0 = +X 쪽으로 붐)")]
        [SerializeField, Range(0f, 360f)] private float windDirection = 30f;

        [Header("날아다니는 잎·파편 (태풍)")]
        [Tooltip("잎·파편 파티클 (날씨 시스템 만들기 / 허리케인 연출 추가 도구가 만들어 줌)")]
        [SerializeField] private ParticleSystem debris;
        [Tooltip("태풍일 때 초당 개수")]
        [SerializeField] private float debrisRate = 60f;
        [Tooltip("바람에 날리는 속도 (m/s)")]
        [SerializeField] private float debrisSpeed = 14f;

        [Header("번개 (태풍)")]
        [SerializeField] private Vector2 lightningInterval = new Vector2(5f, 14f);
        [SerializeField] private float lightningIntensity = 2.5f;
        [Tooltip("번쩍인 뒤 천둥이 들리기까지 (초)")]
        [SerializeField] private Vector2 thunderDelay = new Vector2(0.3f, 1.5f);
        [Tooltip("천둥 칠 때 화면 흔들림 세기 (m)")]
        [SerializeField] private float thunderShake = 0.12f;
        [Tooltip("태풍 동안 바람에 화면이 계속 살짝 흔들리는 세기 (m, 0 = 끔)")]
        [SerializeField] private float typhoonWobble = 0.015f;

        [Header("소리 (파일은 직접 넣기 - 없으면 조용히 넘어감)")]
        [SerializeField] private AudioSource rainAudio;
        [SerializeField] private AudioSource windAudio;
        [SerializeField] private AudioSource thunderAudio;
        [SerializeField] private AudioClip[] thunderClips;
        [SerializeField, Range(0f, 1f)] private float rainVolume = 0.6f;
        [SerializeField, Range(0f, 1f)] private float windVolume = 0.7f;

        [Header("부드럽게 바뀌는 빠르기")]
        [SerializeField] private float blendSpeed = 0.5f;

        private float rainAmount, windAmount, overcast, storm, snowMix;
        private Material snowMaterial;
        private Texture2D snowTexture;
        private Coroutine lightningRoutine;
        private WeatherCameraShake shake;

        private static readonly int WindParamsId = Shader.PropertyToID("_WindParams");

        private void OnDisable()
        {
            Storm = 0f;
            Overcast = 0f;
            LightningFlash = 0f;
            Shader.SetGlobalVector(WindParamsId, Vector4.zero);
            IsSnowing = false;
        }

        private void OnDestroy()
        {
            if (snowMaterial != null) Destroy(snowMaterial);
            if (snowTexture != null) Destroy(snowTexture);
        }

        private void Update()
        {
            WeatherType w = WeatherManager.Instance != null ? WeatherManager.Instance.CurrentWeather : WeatherType.Clear;

            // 목표값
            float tRain = w == WeatherType.Typhoon ? 1f : w == WeatherType.Rain ? 0.6f : 0f;
            float tWind = w == WeatherType.Typhoon ? typhoonWind : w == WeatherType.Rain ? rainWind : clearWind;
            float tOver = w == WeatherType.Typhoon ? typhoonOvercast : w == WeatherType.Rain ? rainOvercast : 0f;
            float tStorm = w == WeatherType.Typhoon ? 1f : 0f;

            float k = 1f - Mathf.Exp(-blendSpeed * Time.deltaTime);
            rainAmount = Mathf.Lerp(rainAmount, tRain, k);
            windAmount = Mathf.Lerp(windAmount, tWind, k);
            overcast = Mathf.Lerp(overcast, tOver, k);
            storm = Mathf.Lerp(storm, tStorm, k);
            // 겨울이면 비 → 눈 (계절이 바뀔 때도 부드럽게)
            bool winter = snowInWinter && CurrentLookSeason() == Season.Winter;
            snowMix = Mathf.Lerp(snowMix, winter ? 1f : 0f, k);
            IsSnowing = winter && rainAmount > 0.05f;
            Overcast = overcast;
            Storm = storm;
            WindDirection = WindDir();

            UpdateRain();
            UpdateSnow();
            UpdateDebris();
            UpdateWind();
            UpdateAudio();
            UpdateLightning();
        }

        private void LateUpdate()
        {
            // 빗방울은 카메라를 따라다님 (떨어지는 건 월드 기준이라 걸어도 자연스러움)
            Transform cam = followCamera != null ? followCamera : (Camera.main != null ? Camera.main.transform : null);
            if (rain != null && cam != null) rain.transform.position = cam.position + Vector3.up * rainHeight;
            if (snow != null && cam != null) snow.transform.position = cam.position + Vector3.up * snowHeight;
            // 잎·파편은 바람이 불어오는 쪽(카메라 뒤 바람 위쪽)에서 생겨서 화면을 가로질러 날아감
            if (debris != null && cam != null)
            {
                Vector2 d = WindDir();
                debris.transform.position = cam.position - new Vector3(d.x, 0f, d.y) * 18f + Vector3.up * 2f;
            }
        }

        private void UpdateRain()
        {
            if (rain == null) return;
            var emission = rain.emission;
            float rate = Mathf.Lerp(0f, rainRate, Mathf.Clamp01(rainAmount / 0.6f)) + (typhoonRate - rainRate) * storm;
            rate *= 1f - snowMix; // 겨울엔 비 대신 눈
            emission.rateOverTime = Mathf.Max(0f, rate);

            // 태풍일수록 바람 방향으로 비스듬히
            var vel = rain.velocityOverLifetime;
            vel.enabled = true;
            Vector2 dir = WindDir();
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(dir.x * typhoonSlant * storm);
            vel.y = new ParticleSystem.MinMaxCurve(0f);
            vel.z = new ParticleSystem.MinMaxCurve(dir.y * typhoonSlant * storm);

            if (rate > 1f && !rain.isPlaying) rain.Play();
            else if (rate <= 1f && rain.isPlaying && rain.particleCount == 0) rain.Stop();
        }

        // 화면에 보이는 계절 (블록·나무 모습 기준) → PV에서 Shift+F8로 모습만 겨울로 바꿔도 눈이 옴
        // 블록 계절 도구가 없으면 게임 시계의 계절
        private static Season CurrentLookSeason()
        {
            if (SeasonalBlockSwapper.Instance != null) return SeasonalBlockSwapper.Instance.CurrentSeason;
            return GameClock.Instance != null ? GameClock.Instance.CurrentSeason : Season.Spring;
        }

        private void UpdateSnow()
        {
            float rate = (Mathf.Lerp(0f, snowRate, Mathf.Clamp01(rainAmount / 0.6f)) + (blizzardRate - snowRate) * storm) * snowMix;
            if (snow == null)
            {
                if (rate <= 1f) return; // 눈이 필요할 때 처음 한 번 만듦
                snow = BuildSnow();
                if (snow == null) return;
            }
            var emission = snow.emission;
            emission.rateOverTime = Mathf.Max(0f, rate);

            // 바람 쪽으로 살짝 흘러가고, 눈보라면 비스듬히 세게
            Vector2 dir = WindDir();
            float side = 0.4f + blizzardSlant * storm;
            var vel = snow.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(dir.x * side - 0.3f, dir.x * side + 0.3f);
            vel.y = new ParticleSystem.MinMaxCurve(-snowFallSpeed.y * (1f + storm), -snowFallSpeed.x * (1f + storm));
            vel.z = new ParticleSystem.MinMaxCurve(dir.y * side - 0.3f, dir.y * side + 0.3f);

            if (rate > 1f && !snow.isPlaying) snow.Play();
            else if (rate <= 1f && snow.isPlaying && snow.particleCount == 0) snow.Stop();
        }

        // 눈송이 파티클 만들기: 카메라 위 넓은 판에서 생겨 천천히 흔들리며 떨어짐
        private ParticleSystem BuildSnow()
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) { Debug.LogWarning("[날씨] URP 파티클 셰이더를 못 찾아서 눈을 끔"); snowInWinter = false; return null; }

            // 눈송이 그림: 가장자리가 부드러운 하얀 동그라미
            const int N = 32;
            snowTexture = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Weather_Snowflake" };
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                    float a = Mathf.Clamp01((1f - Mathf.Sqrt(u * u + v * v)) / 0.5f);
                    snowTexture.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            snowTexture.Apply();

            snowMaterial = new Material(sh) { name = "Weather_Snow" };
            snowMaterial.SetTexture("_BaseMap", snowTexture);
            snowMaterial.SetFloat("_Surface", 1f);
            snowMaterial.SetFloat("_Blend", 0f); // 보통 반투명
            snowMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            snowMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            snowMaterial.SetFloat("_ZWrite", 0f);
            snowMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            snowMaterial.SetOverrideTag("RenderType", "Transparent");
            snowMaterial.renderQueue = 3000;

            var go = new GameObject("Snow");
            go.transform.SetParent(transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(9f, 13f); // 카메라 위에서 땅까지 닿을 만큼
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(snowSize.x, snowSize.y);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.95f), new Color(0.88f, 0.94f, 1f, 0.8f));
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = 8000;
            main.gravityModifier = 0f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(snowRadius * 2f, 0.5f, snowRadius * 2f);

            // 팔랑팔랑 흔들림
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.5f;
            noise.frequency = 0.35f;
            noise.scrollSpeed = 0.2f;
            noise.quality = ParticleSystemNoiseQuality.Low;

            // 생길 때·사라질 때 서서히
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = snowMaterial;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        private void UpdateDebris()
        {
            if (debris == null) return;
            var emission = debris.emission;
            float rate = debrisRate * storm;
            emission.rateOverTime = rate;

            Vector2 dir = WindDir();
            float speed = debrisSpeed * Mathf.Max(storm, 0.3f);
            var vel = debris.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(dir.x * speed * 0.8f, dir.x * speed * 1.2f);
            vel.y = new ParticleSystem.MinMaxCurve(-1f, 2.5f);
            vel.z = new ParticleSystem.MinMaxCurve(dir.y * speed * 0.8f, dir.y * speed * 1.2f);

            if (rate > 0.5f && !debris.isPlaying) debris.Play();
            else if (rate <= 0.5f && debris.isPlaying && debris.particleCount == 0) debris.Stop();
        }

        private void UpdateWind()
        {
            Vector2 dir = WindDir();
            float speed = windSpeed * (1f + storm);
            Shader.SetGlobalVector(WindParamsId, new Vector4(windAmount, speed, dir.x, dir.y));
        }

        private Vector2 WindDir()
        {
            float r = windDirection * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
        }

        private void UpdateAudio()
        {
            if (rainAudio != null)
            {
                rainAudio.volume = rainVolume * Mathf.Clamp01(rainAmount) * (1f - snowMix); // 눈은 소리 없이
                Loop(rainAudio, rainAudio.volume > 0.01f);
            }
            if (windAudio != null)
            {
                windAudio.volume = windVolume * storm;
                Loop(windAudio, windAudio.volume > 0.01f);
            }
        }

        private static void Loop(AudioSource a, bool on)
        {
            if (a.clip == null) return;
            a.loop = true;
            if (on && !a.isPlaying) a.Play();
            else if (!on && a.isPlaying) a.Stop();
        }

        private void UpdateLightning()
        {
            if (storm > 0.5f && lightningRoutine == null) lightningRoutine = StartCoroutine(LightningLoop());

            // 태풍 동안 바람에 화면이 계속 살짝 흔들림
            if (typhoonWobble > 0f && storm > 0.05f) { WeatherCameraShake s = GetShake(); if (s != null) s.SetWobble(typhoonWobble * storm); }
            else if (shake != null) shake.SetWobble(0f);
        }

        private IEnumerator LightningLoop()
        {
            while (storm > 0.5f)
            {
                yield return new WaitForSeconds(Random.Range(lightningInterval.x, lightningInterval.y));
                if (storm <= 0.5f) break;
                yield return Flash();
                StartCoroutine(Thunder());
            }
            lightningRoutine = null;
        }

        // 번쩍-번쩍 두 번
        private IEnumerator Flash()
        {
            float[] curve = { 1f, 0.2f, 0.8f, 0.4f, 0.1f, 0f };
            for (int i = 0; i < curve.Length; i++)
            {
                SetFlash(curve[i]);
                yield return new WaitForSeconds(0.06f);
            }
            SetFlash(0f);
        }

        private void SetFlash(float v)
        {
            LightningFlash = v;
            if (lightningLight != null)
            {
                lightningLight.enabled = v > 0.001f;
                lightningLight.intensity = v * lightningIntensity;
            }
        }

        private IEnumerator Thunder()
        {
            yield return new WaitForSeconds(Random.Range(thunderDelay.x, thunderDelay.y));
            if (thunderAudio != null && thunderClips != null && thunderClips.Length > 0)
            {
                AudioClip c = thunderClips[Random.Range(0, thunderClips.Length)];
                if (c != null) thunderAudio.PlayOneShot(c);
            }
            if (thunderShake > 0f) { WeatherCameraShake s = GetShake(); if (s != null) s.Shake(thunderShake, 0.6f); }
        }

        // 메인 카메라에 흔들림 부품이 없으면 붙임
        private WeatherCameraShake GetShake()
        {
            if (shake == null && Camera.main != null)
            {
                shake = Camera.main.GetComponent<WeatherCameraShake>();
                if (shake == null) shake = Camera.main.gameObject.AddComponent<WeatherCameraShake>();
            }
            return shake;
        }
    }
}