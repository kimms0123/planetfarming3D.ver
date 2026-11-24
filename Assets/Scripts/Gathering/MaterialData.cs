using UnityEngine;

namespace FarmingSystem.Inventory
{
    // 재료 아이템 (목재 등) - 상점·판매 포탈에서 정해진 값에 팔 수 있음
    // 만들기: Create → FarmingSystem → Material Data
    [CreateAssetMenu(fileName = "NewMaterialData", menuName = "FarmingSystem/Material Data")]
    public class MaterialData : ItemData
    {
        [Header("재료 전용")]
        [Tooltip("1개 팔 때 받는 벨")]
        [Min(1)] public int sellPrice = 6;

        private void OnValidate()
        {
            category = ItemCategory.Material;
        }
    }
}
