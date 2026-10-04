using UnityEngine;
using FarmingSystem.Inventory;

namespace FarmingSystem.Farming
{
    /// <summary>
    /// ���� �� �ִ� ���� ������. CropData(��Ȯ��)�� ������ ������ �������̶�,
    /// ���� �۹��̾ "���� ����"�� "��Ȯ�� ����"�� �κ��丮���� ������ ���еȴ�.
    /// �۹��� �þ ������ �� ���� �ϳ� + CropData �ϳ��� ���� ����� �ǰ� �ڵ�� �� �ǵ���� �ȴ�.
    /// </summary>
    [CreateAssetMenu(fileName = "NewSeedData", menuName = "FarmingSystem/Seed Data")]
    public class SeedData : ItemData
    {
        [Tooltip("�� ������ ������ �ڶ󳪴� �۹�")]
        public CropData resultCrop;

        private void OnEnable()
        {
            category = ItemCategory.Seed;
        }
    }
}