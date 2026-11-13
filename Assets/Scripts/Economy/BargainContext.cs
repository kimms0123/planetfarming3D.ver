using System.Collections.Generic;
using UnityEngine;
using FarmingSystem.Core;
using FarmingSystem.Farming;
using FarmingSystem.Inventory;
using FarmingSystem.Weather;

namespace FarmingSystem.Economy
{
    // 흥정 한 번의 상황 정보 (흥정 모델에 넘겨주는 값)
    // 수확 리듬게임의 HarvestContext와 같은 방식: 원래 값들 + 0~1로 맞춘 특징값(Features)
    // FCM 군집 / RBFN 예측 모델은 Features 배열만 입력으로 쓰면 됨 (순서는 FeatureNames)
    public class BargainContext
    {
        // 특징값 이름 (Features 순서). 바꾸면 RuleBasedBargainModel 가중치·학습 데이터 순서도 같이 바꿔야 함
        public static readonly string[] FeatureNames =
        {
            "제안 배율",        // 0: (제안 금액 ÷ 원래 금액 - 1). 1.0배 = 0, 2.0배 이상 = 1
            "거래 규모",        // 1: 원래 금액이 클수록 1에 가까움 (5,000벨 ≈ 0.5)
            "평균 품질",        // 2: 쓰레기 0, 보통 0.33, 좋음 0.66, 최상 1 (금액 비중으로 평균)
            "오늘의 특별 상품", // 3: 금액 중 오늘의 특별 상품 비중
            "제철 작물",        // 4: 금액 중 지금 계절에 자라는 작물 비중
            "이번 방문 시도",   // 5: 이번에 상점 창을 열고 몇 번째 흥정인지 (1번째 0, 4번째 이상 1)
            "누적 성공률",      // 6: 지금까지 흥정 성공 비율 (기록 없으면 0.5)
            "궂은 날씨",        // 7: 비 0.5, 태풍 1, 맑음 0
        };

        public long Offer;          // 플레이어가 부른 금액
        public long BaseTotal;      // 원래 판매 금액
        public float OfferRatio;    // Offer ÷ BaseTotal
        public int AttemptThisVisit;
        public readonly List<Line> Lines = new List<Line>();
        public float[] Features = new float[FeatureNames.Length];

        /// <summary>담은 물품 한 줄</summary>
        public struct Line
        {
            public ItemData Item;
            public int Quantity;
            public ItemQuality Quality;
            public long Total;
            public bool TodaysSpecial;
        }

        public static BargainContext Create(long offer, long baseTotal, IEnumerable<Line> lines, int attemptThisVisit)
        {
            var c = new BargainContext
            {
                Offer = offer,
                BaseTotal = baseTotal,
                OfferRatio = baseTotal > 0 ? offer / (float)baseTotal : 999f,
                AttemptThisVisit = attemptThisVisit,
            };
            if (lines != null) c.Lines.AddRange(lines);
            c.BuildFeatures();
            return c;
        }

        private void BuildFeatures()
        {
            float sum = 0f, quality = 0f, special = 0f, inSeason = 0f;
            Season? season = GameClock.Instance != null ? GameClock.Instance.CurrentSeason : (Season?)null;
            foreach (Line l in Lines)
            {
                float w = Mathf.Max(1f, l.Total);
                sum += w;
                quality += w * QualityValue(l.Quality);
                if (l.TodaysSpecial) special += w;
                if (season.HasValue && l.Item is CropData crop && crop.CanGrowIn(season.Value)) inSeason += w;
            }
            if (sum <= 0f) sum = 1f;

            Features[0] = Mathf.Clamp01(OfferRatio - 1f);
            Features[1] = Mathf.Clamp01(BaseTotal / (BaseTotal + 5000f));
            Features[2] = quality / sum;
            Features[3] = special / sum;
            Features[4] = inSeason / sum;
            Features[5] = Mathf.Clamp01((AttemptThisVisit - 1) / 3f);
            Features[6] = BargainHistory.SuccessRate;
            WeatherManager wm = WeatherManager.Instance;
            Features[7] = wm == null ? 0f : wm.IsTyphoon ? 1f : wm.IsRaining ? 0.5f : 0f;
        }

        private static float QualityValue(ItemQuality q)
        {
            switch (q)
            {
                case ItemQuality.Trash: return 0f;
                case ItemQuality.Good: return 0.66f;
                case ItemQuality.Perfect: return 1f;
                default: return 0.33f;
            }
        }
    }

    /// <summary>흥정 결과 누적 (게임을 켜 둔 동안). 나중에 저장 기능이 생기면 Successes/Attempts만 저장하면 됨</summary>
    public static class BargainHistory
    {
        public static int Attempts;
        public static int Successes;
        public static float SuccessRate => Attempts > 0 ? Successes / (float)Attempts : 0.5f;

        public static void Record(bool accepted)
        {
            Attempts++;
            if (accepted) Successes++;
        }
    }
}