using UnityEngine;
using FarmingSystem.Core;
using FarmingSystem.Farming;

namespace FarmingSystem.Rhythm
{
    // 수확 리듬게임의 "환경 입력값" 묶음
    // 수확하는 순간의 작물·시간·계절 상태를 모아서, 난이도 모델이 읽을 숫자 배열(Features, 모두 0~1)로 바꾼다
    // 나중에 FCM/RBFN 같은 모델을 붙일 때도 이 Features 배열을 그대로 입력으로 쓰면 된다
    // 새 환경 요소(날씨, 비료 등)가 생기면 FeatureNames와 BuildFeatures에 한 줄씩 추가
    public class HarvestContext
    {
        // 특징값 이름 (인스펙터·로그 표시용, 순서가 Features 배열 순서와 같아야 함)
        public static readonly string[] FeatureNames =
        {
            "작물 복잡도",     // 0: 재배일이 길수록 어려움
            "방치 일수",       // 1: 다 자란 뒤 오래 내버려둘수록 어려움
            "시간대",          // 2: 아침 쉬움 → 밤 어려움
            "계절 적합도",     // 3: 주 계절이면 0, 추가 계절(억지로 키운 계절)이면 0.5
            "계절 진행도",     // 4: 계절 말에 가까울수록 어려움 (곧 시들 시기)
            "날씨"             // 5: 날씨 시스템이 생기면 연결 (지금은 0)
        };

        public CropData Crop;
        public string CropName = "테스트 작물";
        public int GrowthDays;
        public int DaysSinceReady;
        public TimeBlock TimeBlock = TimeBlock.Morning;
        public bool InMainSeason = true;
        public float SeasonProgress;   // 0 = 계절 첫날, 1 = 마지막 날
        public float Weather;          // 0 = 맑음 ~ 1 = 폭풍 (예약)
        public int Seed;               // 같은 작물·같은 날이면 같은 패턴이 나오게 하는 씨드

        public float[] Features { get; private set; }

        // 실제 밭 타일에서 환경 값을 읽어 만든다
        public static HarvestContext Build(FarmTile tile)
        {
            var ctx = new HarvestContext();
            CropInstance crop = tile != null ? tile.CurrentCrop : null;

            if (crop != null && crop.Data != null)
            {
                ctx.Crop = crop.Data;
                ctx.CropName = crop.Data.cropName;
                ctx.GrowthDays = crop.Data.totalGrowthDays;
                ctx.DaysSinceReady = crop.DaysSinceReady;

                if (SeasonUtil.TryGetCurrentSeason(out Season now))
                    ctx.InMainSeason = crop.Data.season == now;
            }

            int day = 0;
            if (GameClock.Instance != null)
            {
                GameClock clock = GameClock.Instance;
                ctx.TimeBlock = clock.GetCurrentTimeBlock();
                ctx.SeasonProgress = clock.DaysPerSeason > 1 ? (clock.DayOfSeason - 1) / (float)(clock.DaysPerSeason - 1) : 0f;
                day = clock.CurrentDay;
            }

            Vector2Int grid = tile != null ? tile.GridPosition : Vector2Int.zero;
            ctx.Seed = Hash(grid.x, grid.y, day, ctx.CropName);
            ctx.BuildFeatures();
            return ctx;
        }

        // 디버그 테스트용 (작물 없이 바로 실행)
        public static HarvestContext CreateDebug(int seed)
        {
            var ctx = new HarvestContext { CropName = "테스트 작물", GrowthDays = 4, Seed = seed };
            ctx.BuildFeatures();
            return ctx;
        }

        public void BuildFeatures()
        {
            Features = new[]
            {
                Mathf.Clamp01(GrowthDays / 12f),
                Mathf.Clamp01(DaysSinceReady / 6f),
                TimeBlockDifficulty(TimeBlock),
                InMainSeason ? 0f : 0.5f,
                Mathf.Clamp01(SeasonProgress),
                Mathf.Clamp01(Weather)
            };
        }

        private static float TimeBlockDifficulty(TimeBlock block)
        {
            switch (block)
            {
                case TimeBlock.Morning: return 0f;
                case TimeBlock.Midday: return 0.2f;
                case TimeBlock.Afternoon: return 0.4f;
                case TimeBlock.Evening: return 0.6f;
                case TimeBlock.Night: return 0.85f;
                default: return 1f; // 새벽
            }
        }

        private static int Hash(int x, int z, int day, string name)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + x;
                h = h * 31 + z;
                h = h * 31 + day;
                h = h * 31 + (name != null ? name.GetHashCode() : 0);
                return h;
            }
        }

        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < Features.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(FeatureNames[i]).Append(' ').Append(Features[i].ToString("0.00"));
            }
            return sb.ToString();
        }
    }
}