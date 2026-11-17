using UnityEngine;
using FarmingSystem.Core;
using FarmingSystem.Inventory;

namespace FarmingSystem.Fishing
{
    public enum FishHabitat
    {
        [InspectorName("바다")] Sea,
        [InspectorName("강")] River,
        [InspectorName("어디든")] Any,
    }

    public enum FishRarity
    {
        [InspectorName("흔함")] Common,
        [InspectorName("보통")] Uncommon,
        [InspectorName("희귀")] Rare,
        [InspectorName("전설")] Legendary,
    }

    // 물고기 1종 (엑셀 '물고기데이터.xlsx' → Tools → 행성농사일지 → 물고기 → 시트에서 가져오기로 만들고 고침)
    //  - 물가에 그림자로 헤엄치다가 낚싯대로 잡으면 가방에 들어가고 도감에 등록
    //  - 나오는 조건: 장소(바다·강) + 계절 + 시간 / 희귀할수록 덜 나옴
    [CreateAssetMenu(fileName = "Fish_New", menuName = "Farming/Fish Data")]
    public class FishData : ItemData
    {
        [Header("나오는 곳·때")]
        public FishHabitat habitat = FishHabitat.Any;
        [Tooltip("나오는 계절 (비우면 모든 계절)")]
        public Season[] seasons = new Season[0];
        [Tooltip("나오기 시작하는 시각 (0~23). 시작 = 끝이면 하루 종일")]
        [Range(0, 23)] public int startHour = 0;
        [Tooltip("나오지 않게 되는 시각 (0~23). 시작보다 작으면 밤을 넘김 (예: 19 → 4 = 저녁 7시~새벽 4시)")]
        [Range(0, 23)] public int endHour = 0;

        [Header("모습·희귀도")]
        [Tooltip("물속 그림자 크기 (1 = 아주 작음 ~ 6 = 아주 큼)")]
        [Range(1, 6)] public int shadowSize = 2;
        public FishRarity rarity = FishRarity.Common;

        [Header("값·난이도")]
        [Tooltip("개당 판매가 (벨)")]
        public int basePrice = 30;
        [Tooltip("찌가 쑥 들어간 뒤 클릭해야 하는 시간 (초). 짧을수록 어려움")]
        [Range(0.25f, 1.5f)] public float biteWindow = 0.8f;

        private void OnValidate()
        {
            category = ItemCategory.Fish;
            maxStack = Mathf.Max(1, maxStack);
        }

        /// <summary>이 계절에 나오는지</summary>
        public bool InSeason(Season s)
        {
            if (seasons == null || seasons.Length == 0) return true;
            foreach (Season x in seasons) if (x == s) return true;
            return false;
        }

        /// <summary>이 시각에 나오는지</summary>
        public bool InTime(int hour)
        {
            if (startHour == endHour) return true;
            return startHour < endHour ? hour >= startHour && hour < endHour : hour >= startHour || hour < endHour;
        }

        /// <summary>뽑힐 확률 비중 (흔함 60 / 보통 25 / 희귀 10 / 전설 2)</summary>
        public float Weight => rarity == FishRarity.Common ? 60f : rarity == FishRarity.Uncommon ? 25f : rarity == FishRarity.Rare ? 10f : 2f;

        public static string HabitatName(FishHabitat h) => h == FishHabitat.Sea ? "바다" : h == FishHabitat.River ? "강" : "바다·강";
        public static string RarityName(FishRarity r) => r == FishRarity.Common ? "흔함" : r == FishRarity.Uncommon ? "보통" : r == FishRarity.Rare ? "희귀" : "전설";

        public string TimeText => startHour == endHour ? "하루 종일" : $"{startHour}시 ~ {endHour}시";

        public string SeasonText
        {
            get
            {
                if (seasons == null || seasons.Length == 0 || seasons.Length >= 4) return "모든 계절";
                string[] n = { "봄", "여름", "가을", "겨울" };
                var parts = new System.Collections.Generic.List<string>();
                foreach (Season s in seasons) if (!parts.Contains(n[(int)s])) parts.Add(n[(int)s]);
                return string.Join("·", parts);
            }
        }
    }
}
