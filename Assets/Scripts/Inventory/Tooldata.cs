using UnityEngine;

namespace FarmingSystem.Inventory
{
    // 도구 아이템 (괭이·물뿌리개 등)
    // - 일반 아이템처럼 인벤토리에 들어가고, 끌어서 도구칸으로 옮길 수 있음
    // - 도구칸에 들어 있는 도구만 마우스 휠로 바꿔 들 수 있음
    // - Tool Type이 실제 동작을 정함 (괭이 = 밭 갈기, 물뿌리개 = 물 주기)
    // 만들기: Project 창 우클릭 → Create → FarmingSystem → Tool Data
    [CreateAssetMenu(fileName = "NewToolData", menuName = "FarmingSystem/Tool Data")]
    public class ToolData : ItemData
    {
        [Header("도구 전용")]
        [Tooltip("이 도구로 할 수 있는 동작")]
        public ToolType toolType = ToolType.Hoe;

        private void OnValidate()
        {
            // 도구는 항상 도구 분류, 한 칸에 1개
            category = ItemCategory.Tool;
            maxStack = 1;
        }
    }
}