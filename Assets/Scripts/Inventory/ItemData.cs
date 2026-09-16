using UnityEngine;

namespace FarmingSystem.Inventory
{
    public enum ItemCategory
    {
        Seed,
        Crop,
        Tool,
        Material
    }

    /// <summary>
    /// 인벤토리에 들어갈 수 있는 모든 아이템의 공통 베이스.
    /// 씨앗(CropData)은 이걸 상속해서 작물 고유 데이터를 추가한다.
    /// 나중에 도구/재료 아이템도 이 클래스를 상속하면 같은 인벤토리 슬롯 시스템에 바로 들어온다.
    /// </summary>
    public class ItemData : ScriptableObject
    {
        [Header("공통 정보")]
        public string itemName = "New Item";
        public ItemCategory category = ItemCategory.Material;
        public Sprite icon;
        [Tooltip("한 슬롯에 겹쳐 쌓을 수 있는 최대 개수")]
        public int maxStack = 99;
    }
}