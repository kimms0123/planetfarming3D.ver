using UnityEngine;
using FarmingSystem.Inventory;
using FarmingSystem.Audio;

namespace FarmingSystem.Farming
{
    public enum FertilizerType
    {
        [InspectorName("성장 (빨리 자람)")] Growth,
        [InspectorName("품질 (등급 +1)")] Quality,
        [InspectorName("수확량 (개수 조금 더)")] Yield,
    }

    // 비료 아이템
    //  쓰는 법: 핫바에서 비료를 고르고 → 갈아 놓은 빈 밭을 클릭해서 뿌림 → 그 위에 씨앗을 심으면 그 작물에 효과
    //  - 한 칸에 비료 하나, 작물 한 번(수확할 때까지)만 효과. 수확하면 비료도 사라짐
    //  - 성장: 물을 준 날마다 Growth Chance 확률로 하루치 더 자람 (0.1 = 평균 10% 빨리)
    //          ※ 작물은 '하루' 단위로 자라서 3일짜리 작물에 딱 10%를 줄일 수는 없음 → 확률로 평균을 맞춤
    //  - 품질: 수확 등급이 Quality Steps 단계 올라감 (쓰레기→보통→좋음→최고, 최고 위로는 안 올라감)
    //  - 수확량: 수확 개수가 Yield Bonus 만큼 늘어남 (0.15 = 15% 더)
    //  새 비료는 Project 창 우클릭 → Create → Farming → Fertilizer Data, 상점 구매 목록(Buy Catalog)에 넣으면 '비료' 탭에 나옴
    [CreateAssetMenu(fileName = "Fertilizer_New", menuName = "Farming/Fertilizer Data")]
    public class FertilizerData : ItemData
    {
        /// <summary>수확량 비료가 올릴 수 있는 최대치 (가방 자리 미리 확인용)</summary>
        public const float MaxYieldBonus = 0.5f;

        [Header("비료 효과")]
        public FertilizerType type = FertilizerType.Growth;
        [Tooltip("성장 비료: 물 준 날마다 이 확률로 하루치 더 자람 (0.1 = 평균 10% 빨리)")]
        [Range(0f, 1f)] public float growthChance = 0.1f;
        [Tooltip("품질 비료: 수확 등급이 몇 단계 올라가는지")]
        [Range(1, 3)] public int qualitySteps = 1;
        [Tooltip("수확량 비료: 수확 개수가 얼마나 늘어나는지 (0.15 = 15% 더)")]
        [Range(0f, MaxYieldBonus)] public float yieldBonus = 0.15f;

        [Header("밭에 뿌린 모습")]
        [Tooltip("뿌린 밭에 보이는 알갱이 색")]
        public Color pelletColor = new Color(0.55f, 0.85f, 0.4f);

        private void OnValidate()
        {
            category = ItemCategory.Fertilizer;
        }

        // ───────────────────────── 쓰기 (FarmActionController가 부름) ─────────────────────────

        /// <summary>핫바에서 지금 고른 비료 (비료가 아니면 null)</summary>
        public static FertilizerData Selected
        {
            get
            {
                InventoryManager inv = InventoryManager.Instance;
                if (inv == null) return null;
                InventorySlotData slot = inv.GetSlot(inv.SelectedHotbarIndex);
                return slot != null && !slot.IsEmpty ? slot.item as FertilizerData : null;
            }
        }

        /// <summary>지금 고른 비료를 이 밭에 뿌릴 수 있는지 (갈아 놓은 빈 밭 + 아직 비료 없음)</summary>
        public static bool CanUseOn(FarmTile tile) => tile != null && Selected != null && tile.CanFertilize;

        /// <summary>지금 고른 비료를 이 밭에 뿌리고 1개 씀</summary>
        public static bool UseOn(FarmTile tile)
        {
            InventoryManager inv = InventoryManager.Instance;
            FertilizerData f = Selected;
            if (inv == null || f == null || tile == null || !tile.ApplyFertilizer(f)) return false;
            inv.RemoveFromSlot(inv.SelectedHotbarIndex, 1);
            GameAudio.PlayAt(SoundId.Fertilize, tile.transform.position);
            return true;
        }
    }
}