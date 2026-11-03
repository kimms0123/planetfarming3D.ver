using UnityEngine;
using FarmingSystem.Inventory;

namespace FarmingSystem.Building
{
    // 건물 키트 아이템 (동숲의 "집 키트"처럼 가방에 들어가는 아이템)
    // - 핫바에서 이 키트를 고르고 좌클릭하면 설치 모드가 시작됨
    // - 설치를 확정하면 키트 1개가 사라지고 그 자리에 공사장이 생김
    // - 상점 구매 목록에 넣어 팔 수도 있음 (일반 아이템과 같음)
    // 만들기: Project 창 우클릭 → Create → FarmingSystem → Building Kit
    [CreateAssetMenu(fileName = "NewBuildingKit", menuName = "FarmingSystem/Building Kit")]
    public class BuildingKitData : ItemData
    {
        [Header("건물 키트")]
        [Tooltip("이 키트로 지을 건물")]
        public BuildingData building;

        private void OnValidate()
        {
            maxStack = Mathf.Max(1, maxStack);
            if (building != null && string.IsNullOrEmpty(description)) description = building.description;
        }
    }
}