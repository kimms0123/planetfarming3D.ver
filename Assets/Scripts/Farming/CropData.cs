using UnityEngine;
using FarmingSystem.Core;
using FarmingSystem.Inventory;

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
        public Season season = Season.Spring;

        [Header("성장")]
        [Tooltip("총 재배일 (작물 DB 시트 기준)")]
        public int totalGrowthDays = 3;

        [Tooltip("성장 단계별로 보여줄 모델. 배열 순서대로 씨앗->새싹->...->수확 직전")]
        public GameObject[] growthStagePrefabs;

        [Header("수확")]
        [Tooltip("기대 수확량 (기본 개수, Perfect 비율에 따라 배율 적용은 리듬게임 시스템에서 처리)")]
        public int expectedYield = 8;

        [Tooltip("기준 판매가 (작물 DB 시트: (총재배일 x 작물계수) / 기대수확량)")]
        public float basePrice = 1f;

        /// <summary>
        /// 기존 코드(FarmTile, CropInstance, FarmActionController)가 cropData.cropName으로 참조하던 부분을
        /// 그대로 쓸 수 있도록 ItemData.itemName의 별칭으로 제공.
        /// </summary>
        public string cropName => itemName;

        /// <summary>현재 성장일수를 기준으로 몇 번째 비주얼 스테이지인지 계산 (Day0=Stage0, 1:1 매칭)</summary>
        public int GetStageIndex(int currentGrowthDay)
        {
            if (growthStagePrefabs == null || growthStagePrefabs.Length == 0) return -1;

            int stage = Mathf.Clamp(currentGrowthDay, 0, growthStagePrefabs.Length - 1);
            return stage;
        }
    }
}