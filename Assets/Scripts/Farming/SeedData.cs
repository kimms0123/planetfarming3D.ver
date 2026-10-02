using UnityEngine;
using FarmingSystem.Inventory;

namespace FarmingSystem.Farming
{
    /// <summary>
    /// 심을 수 있는 씨앗 아이템. CropData(수확물)와 완전히 별도의 아이템이라,
    /// 같은 작물이어도 "씨앗 상태"와 "수확된 상태"가 인벤토리에서 저절로 구분된다.
    /// 작물이 늘어날 때마다 이 에셋 하나 + CropData 하나만 새로 만들면 되고 코드는 안 건드려도 된다.
    /// </summary>
    [CreateAssetMenu(fileName = "NewSeedData", menuName = "FarmingSystem/Seed Data")]
    public class SeedData : ItemData
    {
        [Tooltip("이 씨앗을 심으면 자라나는 작물")]
        public CropData resultCrop;

        private void OnEnable()
        {
            category = ItemCategory.Seed;
        }
    }
}