using System;
using System.Collections.Generic;
using UnityEngine;
using FarmingSystem.Core;
using FarmingSystem.Farming;

namespace FarmingSystem.Weather
{
    // 날씨 종류 (순서 주의: 새 날씨(바람·눈 등)는 항상 맨 끝에 추가)
    public enum WeatherType
    {
        Clear,
        Rain,
        Typhoon
    }

    // 날씨 관리자 (씬에 하나)
    // - 날짜마다 계절별 확률로 날씨가 정해짐 (같은 날짜는 언제 물어봐도 같은 날씨 → 다른 시스템과 순서가 꼬이지 않음)
    // - 비·태풍: 갈아 둔 밭에 저절로 물이 줌 (비 오는 동안 새로 간 밭도)
    // - 태풍: 그날은 밭 갈기·씨앗 심기 불가, 상인이 오지 않음(다음 날로 미뤄짐), 태풍이 지나간 다음 날 아침 작물 일부가 시듦
    // - 연출(빗방울·흐린 하늘·나무 흔들림·번개·소리)은 WeatherVisuals가 담당
    // - 디버그: 컴포넌트 ⋮ 메뉴 또는 Force Weather로 오늘 날씨를 바로 바꿀 수 있음 (PV 촬영용)
    public class WeatherManager : MonoBehaviour
    {
        public static WeatherManager Instance { get; private set; }

        [Serializable]
        public class SeasonChance
        {
            [Range(0f, 1f)] public float rain;
            [Range(0f, 1f)] public float typhoon;
        }

        [Header("계절별 확률 (하루 단위)")]
        [SerializeField] private SeasonChance spring = new SeasonChance { rain = 0.25f, typhoon = 0f };
        [SerializeField] private SeasonChance summer = new SeasonChance { rain = 0.25f, typhoon = 0.08f };
        [SerializeField] private SeasonChance autumn = new SeasonChance { rain = 0.2f, typhoon = 0.05f };
        [SerializeField] private SeasonChance winter = new SeasonChance { rain = 0.1f, typhoon = 0f };
        [Tooltip("첫날은 항상 맑음")]
        [SerializeField] private bool firstDayClear = true;
        [Tooltip("태풍이 이틀 연속 오지 않게 (다음 날은 비로 바뀜)")]
        [SerializeField] private bool noTyphoonTwiceInRow = true;
        [Tooltip("날씨 무작위 씨드 (바꾸면 날씨 순서가 달라짐)")]
        [SerializeField] private int seed = 12345;

        [Header("비 효과")]
        [Tooltip("비·태풍인 날 갈아 둔 밭에 저절로 물 주기")]
        [SerializeField] private bool rainWatersCrops = true;
        [Tooltip("비 오는 동안 새로 간 밭을 확인하는 간격 (초)")]
        [SerializeField] private float rainWaterCheckInterval = 1.5f;

        [Header("태풍 효과")]
        [Tooltip("태풍 날 밭 갈기·씨앗 심기 막기")]
        [SerializeField] private bool typhoonBlocksFarming = true;
        [Tooltip("태풍 날 상인이 오지 않음 (방문이 다음 날로 미뤄짐)")]
        [SerializeField] private bool typhoonDelaysMerchant = true;
        [Tooltip("태풍이 지나간 다음 날 아침, 밭 작물 하나하나가 시들 확률")]
        [SerializeField, Range(0f, 1f)] private float typhoonWitherChance = 0.3f;

        [Header("디버그 / 촬영")]
        [Tooltip("Auto가 아니면 오늘 날씨를 이걸로 고정 (실행 중에 바꿔도 바로 적용)")]
        [SerializeField] private ForceMode forceWeather = ForceMode.Auto;
        public enum ForceMode { Auto, Clear, Rain, Typhoon }

        public event Action<WeatherType> OnWeatherChanged;

        public WeatherType CurrentWeather { get; private set; } = WeatherType.Clear;
        public bool IsRaining => CurrentWeather == WeatherType.Rain || CurrentWeather == WeatherType.Typhoon;
        public bool IsTyphoon => CurrentWeather == WeatherType.Typhoon;

        // 다른 스크립트에서 쉽게 쓰는 확인용 (날씨 시스템이 없으면 항상 false)
        public static bool IsTyphoonNow => Instance != null && Instance.IsTyphoon;
        public static bool FarmingBlockedNow => Instance != null && Instance.IsTyphoon && Instance.typhoonBlocksFarming;
        public static bool MerchantBlockedOn(int day) => Instance != null && Instance.typhoonDelaysMerchant && Instance.GetWeatherForDay(day) == WeatherType.Typhoon;

        /// <summary>날씨 → 0~1 값 (맑음 0, 비 0.4, 태풍 1). 리듬게임 등 환경 입력값으로 쓸 때</summary>
        public float WeatherValue => CurrentWeather == WeatherType.Typhoon ? 1f : CurrentWeather == WeatherType.Rain ? 0.4f : 0f;

        private readonly Dictionary<int, WeatherType> overrides = new Dictionary<int, WeatherType>();
        private int lastDay = -1;
        private ForceMode lastForce = ForceMode.Auto;
        private FarmTile[] tiles;
        private float waterTimer;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // 날짜 변화는 매 프레임 직접 확인 (GameClock 이벤트 순서와 상관없이 밭이 하루 정리를 끝낸 뒤에 처리됨)
        private void Update()
        {
            GameClock clock = GameClock.Instance;
            if (clock == null) return;

            int day = clock.CurrentDay;
            if (day != lastDay)
            {
                int previous = lastDay;
                lastDay = day;
                if (previous != -1 && day == previous + 1 && GetWeatherForDay(previous) == WeatherType.Typhoon)
                    ApplyTyphoonDamage();
                SetCurrent(GetWeatherForDay(day), "새 날");
            }

            if (forceWeather != lastForce)
            {
                lastForce = forceWeather;
                if (forceWeather != ForceMode.Auto) SetWeatherToday((WeatherType)(forceWeather - 1));
                else { overrides.Remove(day); SetCurrent(GetWeatherForDay(day), "자동으로 되돌림"); }
            }

            // 비 오는 동안 갈아 둔 밭에 물
            if (rainWatersCrops && IsRaining)
            {
                waterTimer -= Time.deltaTime;
                if (waterTimer <= 0f)
                {
                    waterTimer = rainWaterCheckInterval;
                    WaterTilledTiles();
                }
            }
        }

        // ---------- 날씨 정하기 ----------

        /// <summary>그 날짜의 날씨 (직접 정한 날씨가 있으면 그것, 없으면 계절별 확률로 정해진 값 - 같은 날은 항상 같음)</summary>
        public WeatherType GetWeatherForDay(int day)
        {
            if (overrides.TryGetValue(day, out WeatherType w)) return w;
            WeatherType rolled = RollForDay(day);
            if (noTyphoonTwiceInRow && rolled == WeatherType.Typhoon && day > 1 && RollForDay(day - 1) == WeatherType.Typhoon)
                rolled = WeatherType.Rain;
            return rolled;
        }

        private WeatherType RollForDay(int day)
        {
            if (firstDayClear && day <= 1) return WeatherType.Clear;
            SeasonChance c = ChanceFor(SeasonOfDay(day));
            float r = Hash01(day);
            if (r < c.typhoon) return WeatherType.Typhoon;
            if (r < c.typhoon + c.rain) return WeatherType.Rain;
            return WeatherType.Clear;
        }

        // GameClock과 같은 규칙: 계절 길이(DaysPerSeason)마다 봄 → 여름 → 가을 → 겨울
        private static Season SeasonOfDay(int day)
        {
            int perSeason = GameClock.Instance != null ? Mathf.Max(1, GameClock.Instance.DaysPerSeason) : 27;
            return (Season)(((day - 1) / perSeason) % 4);
        }

        private SeasonChance ChanceFor(Season s)
        {
            switch (s)
            {
                case Season.Spring: return spring;
                case Season.Summer: return summer;
                case Season.Autumn: return autumn;
                default: return winter;
            }
        }

        // 날짜 → 0~1 사이 고정된 무작위 값
        private float Hash01(int day)
        {
            unchecked
            {
                uint h = (uint)day * 0x9E3779B1u ^ (uint)seed * 0x85EBCA6Bu;
                h ^= h >> 16; h *= 0x7FEB352Du;
                h ^= h >> 15; h *= 0x846CA68Bu;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        /// <summary>오늘 날씨를 직접 바꾸기 (스토리 이벤트·PV 촬영·디버그)</summary>
        public void SetWeatherToday(WeatherType type)
        {
            int day = GameClock.Instance != null ? GameClock.Instance.CurrentDay : 1;
            overrides[day] = type;
            SetCurrent(type, "직접 설정");
        }

        /// <summary>특정 날짜 날씨를 미리 정해 두기 (예: 스토리상 10일에 태풍)</summary>
        public void SetWeatherForDay(int day, WeatherType type) => overrides[day] = type;

        private void SetCurrent(WeatherType type, string reason)
        {
            bool changed = type != CurrentWeather;
            CurrentWeather = type;
            Debug.Log($"[날씨] Day {lastDay} - {DisplayName(type)} ({reason})");
            if (IsRaining) { tiles = null; waterTimer = 0f; } // 바로 한 번 물 주기
            if (changed) OnWeatherChanged?.Invoke(type);
        }

        public static string DisplayName(WeatherType t)
        {
            switch (t)
            {
                case WeatherType.Rain: return "비";
                case WeatherType.Typhoon: return "태풍";
                default: return "맑음";
            }
        }

        // ---------- 비·태풍 효과 ----------

        private void WaterTilledTiles()
        {
            if (tiles == null) tiles = FindObjectsByType<FarmTile>(FindObjectsInactive.Exclude);
            int watered = 0;
            foreach (FarmTile t in tiles)
            {
                if (t == null || t.State != TileState.Tilled || t.IsWatered) continue;
                if (t.Water()) watered++;
            }
            if (watered > 0) Debug.Log($"[날씨] 비가 밭 {watered}칸에 물을 줬어요");
        }

        private void ApplyTyphoonDamage()
        {
            if (typhoonWitherChance <= 0f) return;
            FarmTile[] all = FindObjectsByType<FarmTile>(FindObjectsInactive.Exclude);
            int damaged = 0;
            var rng = new System.Random(seed + lastDay * 31);
            foreach (FarmTile t in all)
            {
                CropInstance crop = t != null ? t.CurrentCrop : null;
                if (crop == null || crop.IsWithered) continue;
                if (rng.NextDouble() < typhoonWitherChance)
                {
                    crop.Wither();
                    damaged++;
                }
            }
            Debug.Log($"[태풍 피해] 지난밤 태풍으로 작물 {damaged}개가 시들었어요");
        }

        // ---------- 디버그 메뉴 (컴포넌트 ⋮) ----------

        [ContextMenu("오늘 날씨: 맑음")] private void DebugClear() => SetWeatherToday(WeatherType.Clear);
        [ContextMenu("오늘 날씨: 비")] private void DebugRain() => SetWeatherToday(WeatherType.Rain);
        [ContextMenu("오늘 날씨: 태풍")] private void DebugTyphoon() => SetWeatherToday(WeatherType.Typhoon);

        [ContextMenu("앞으로 30일 날씨 미리보기 (Console)")]
        private void DebugPreview()
        {
            int start = GameClock.Instance != null ? GameClock.Instance.CurrentDay : 1;
            var sb = new System.Text.StringBuilder("[날씨 미리보기]\n");
            for (int d = start; d < start + 30; d++) sb.AppendLine($"Day {d} ({SeasonOfDay(d)}): {DisplayName(GetWeatherForDay(d))}");
            Debug.Log(sb.ToString());
        }
    }
}