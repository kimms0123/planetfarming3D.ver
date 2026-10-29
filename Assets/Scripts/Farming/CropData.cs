using UnityEngine;
using FarmingSystem.Core;
using FarmingSystem.Inventory;
using FarmingSystem.Rhythm;

namespace FarmingSystem.Farming
{
    /// <summary>
    /// 작물 1종에 대한 데이터 정의. 작물 DB 시트 값을 그대로 옮겨 담는 용도.
    /// ItemData를 상속해서 인벤토리/핫바 슬롯 시스템에 그대로 들어갈 수 있다.
    /// 감자/무/딸기 등 작물마다 하나씩 에셋으로 생성해서 사용.
    /// </summary>
    [CreateAssetMenu(fileName = "NewCropData", menuName = "FarmingSystem/Crop Data")]
    public class CropData : ItemData
    {
        [Header("작물 전용 정보")]
        [Tooltip("이 작물을 심고 키울 수 있는 계절")]
        public Season season = Season.Spring;
        [Tooltip("여러 계절에 걸쳐 자라는 작물이면 추가 계절을 넣는다 (예: 봄+여름). 보통은 비워둠")]
        public Season[] additionalSeasons;

        [Header("성장")]
        [Tooltip("총 재배일 (작물 DB 시트 기준)")]
        public int totalGrowthDays = 3;

        [Tooltip("성장 단계별로 보여줄 모델. 배열 순서대로 씨앗->새싹->...->수확 직전")]
        public GameObject[] growthStagePrefabs;

        [Tooltip("수확했을 때 나오는 수확물 모델 (선택, 작물 모델의 4번째 단계). 나중에 뽑히는 연출·아이콘에 사용")]
        public GameObject harvestedPrefab;

        [Header("시듦")]
        [Tooltip("계절이 지나 시들었을 때 보여줄 모델 (선택). 비워두면 현재 모델을 갈색으로 칠해서 표현")]
        public GameObject witheredPrefab;

        [Header("수확")]
        [Tooltip("기대 수확량 (기본 개수, Perfect 비율에 따라 배율 적용은 리듬게임 시스템에서 처리)")]
        public int expectedYield = 8;

        [Tooltip("기준 판매가 (작물 DB 시트: (총재배일 x 작물계수) / 기대수확량)")]
        public float basePrice = 1f;

        [Header("수확 리듬게임")]
        [Tooltip("이 작물의 방향키 패턴 (예: 감자 ← → ← → ↓). 노트 수만큼 반복해서 사용. 비우면 랜덤")]
        public ArrowDirection[] harvestPattern;
        [Tooltip("템포 배율 (1 = 기본, 0.8 = 느림(호박), 1.2 = 빠름(당근))")]
        [Range(0.6f, 1.5f)] public float harvestTempo = 1f;
        [Tooltip("작물이 흔들리는 크기 배율 (1 = 기본, 1.5 = 크게(호박), 0.7 = 묵직하게(감자))")]
        [Range(0.3f, 2f)] public float harvestShakeScale = 1f;
        [Tooltip("작물 가치에 따른 난이도 보정 (일반 -0.15, 중급 0, 고급 +0.15, 희귀 +0.3)")]
        [Range(-0.5f, 0.5f)] public float harvestDifficultyOffset = 0f;

        /// <summary>
        /// 기존 코드(FarmTile, CropInstance, FarmActionController)가 cropData.cropName으로 참조하던 부분을
        /// 그대로 쓸 수 있도록 ItemData.itemName의 별칭으로 제공.
        /// </summary>
        public string cropName => itemName;

        /// <summary>이 계절에 심거나 키울 수 있는지</summary>
        public bool CanGrowIn(Season s)
        {
            if (season == s) return true;
            if (additionalSeasons != null)
                foreach (Season extra in additionalSeasons)
                    if (extra == s) return true;
            return false;
        }

        // 현재 성장일수 → 보여줄 모델 단계
        // 재배일과 모델 개수가 달라도 고르게 나눔. 마지막 모델(다 자란 모습)은 수확 가능한 날에만 보임
        //   예) 모델 3개(씨앗·새싹·다 자람), 재배일 5일 → 0~2일 씨앗, 3~4일 새싹, 5일 다 자람
        //   모델 개수 = 재배일 + 1이면 예전처럼 하루에 한 단계씩 (Day0 = 0단계, Day1 = 1단계 ...)
        public int GetStageIndex(int currentGrowthDay)
        {
            if (growthStagePrefabs == null || growthStagePrefabs.Length == 0) return -1;

            int last = growthStagePrefabs.Length - 1;
            if (last == 0) return 0;
            if (currentGrowthDay >= totalGrowthDays) return last;
            int days = Mathf.Max(1, totalGrowthDays);
            return Mathf.Clamp(currentGrowthDay * last / days, 0, last - 1);
        }
    }
}