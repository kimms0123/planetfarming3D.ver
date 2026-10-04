using UnityEngine;

namespace FarmingSystem.Inventory
{
    public enum ItemCategory
    {
        Seed,
        Crop,
        Tool,
        Material,
        Fertilizer // 새 값은 항상 맨 끝에 추가 (중간에 넣으면 기존 에셋의 카테고리 값이 밀림)
    }

    /// <summary>
    /// 인벤토리에 들어갈 수 있는 모든 아이템의 공통 베이스.
    /// 씨앗(SeedData)·수확물(CropData)이 이걸 상속하고,
    /// 나중에 도구/재료/비료 아이템도 이 클래스를 상속하면 같은 인벤토리 슬롯 시스템에 바로 들어온다.
    /// </summary>
    public class ItemData : ScriptableObject
    {
        [Header("공통 정보")]
        public string itemName = "New Item";
        public ItemCategory category = ItemCategory.Material;
        public Sprite icon;
        [Tooltip("한 슬롯에 겹쳐 쌓을 수 있는 최대 개수")]
        public int maxStack = 99;

        [Tooltip("상점 구매 상세창 등에 표시되는 설명")]
        [TextArea(2, 4)]
        public string description;
    }
}