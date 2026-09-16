using System;
using UnityEngine;

namespace FarmingSystem.Inventory
{
    public enum ToolType
    {
        Hoe,
        WateringCan
    }

    [Serializable]
    public class InventorySlotData
    {
        public ItemData item;
        public int quantity;
        public ItemQuality quality = ItemQuality.Normal; // Crop 계열이 아닌 아이템은 의미 없음, 항상 Normal 취급

        public bool IsEmpty => item == null || quantity <= 0;
    }

    /// <summary>
    /// 스타듀밸리 방식 인벤토리: 전체 30칸(10칸 x 3줄) 중 앞 10칸(인덱스 0~9)이
    /// 곧 핫바다. 별도 배열이 아니라 완전히 같은 데이터라서, 핫바 UI와
    /// 전체 인벤토리 UI(Tab)는 항상 자동으로 동기화된다.
    /// </summary>
    public class InventoryManager : MonoBehaviour
    {
        public static InventoryManager Instance { get; private set; }

        [Header("도구 목록 (마우스 휠로 순환)")]
        [SerializeField] private ToolType[] ownedTools = { ToolType.Hoe, ToolType.WateringCan };
        [SerializeField] private int currentToolIndex = 0;

        [Header("전체 인벤토리")]
        [Tooltip("10칸 x 3줄 = 30칸. 인덱스 0~9가 핫바(숫자키 1~9,0)와 동일하다.")]
        [SerializeField] private int totalSlotCount = 30;
        [SerializeField] private int hotbarSize = 10;

        private InventorySlotData[] slots;

        [Header("핫바 선택 (0 ~ HotbarSize-1)")]
        [SerializeField] private int selectedHotbarIndex = 0;

        [Serializable]
        public class StartingItemEntry
        {
            [Tooltip("몇 번 슬롯에 넣을지 (0부터 시작, 0~9는 핫바, 10~29는 인벤토리 나머지 칸)")]
            public int slotIndex;
            public ItemData item;
            public int quantity = 1;
            public ItemQuality quality = ItemQuality.Normal;
        }

        [Header("테스트용 시작 아이템 (인스펙터에서 직접 배치)")]
        [SerializeField] private StartingItemEntry[] startingItems;

        public ToolType CurrentTool => ownedTools[currentToolIndex];
        public int CurrentToolIndex => currentToolIndex;
        public int ToolCount => ownedTools.Length;

        public int HotbarSize => hotbarSize;
        public int TotalSlotCount => totalSlotCount;
        public int SelectedHotbarIndex => selectedHotbarIndex;

        /// <summary>현재 핫바에서 선택된 슬롯의 아이템이 CropData(씨앗)일 때만 반환, 아니면 null</summary>
        public FarmingSystem.Farming.CropData CurrentSeed
        {
            get
            {
                InventorySlotData slot = GetSlot(selectedHotbarIndex);
                if (slot == null || slot.IsEmpty) return null;
                return slot.item as FarmingSystem.Farming.CropData;
            }
        }

        /// <summary>도구가 바뀔 때 발행 -> ToolSlotUI가 구독</summary>
        public event Action<ToolType> OnToolChanged;
        /// <summary>핫바 선택 슬롯이 바뀔 때 발행 -> HotbarUI가 구독</summary>
        public event Action<int> OnSelectedHotbarIndexChanged;
        /// <summary>인벤토리(30칸 아무 곳이나) 내용물이 바뀔 때 발행 -> HotbarUI/InventoryUI가 구독</summary>
        public event Action OnInventoryChanged;
        /// <summary>Tab 등으로 전체 인벤토리 창을 열고 닫을 때 발행 -> InventoryUI가 구독</summary>
        public event Action<bool> OnInventoryToggled;

        private bool isInventoryOpen = false;
        public bool IsInventoryOpen => isInventoryOpen;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            slots = new InventorySlotData[totalSlotCount];
            for (int i = 0; i < totalSlotCount; i++)
                slots[i] = new InventorySlotData();

            ApplyStartingItems();

            Debug.Log($"[InventoryManager] 초기화 완료. 전체 {totalSlotCount}칸, 핫바 {hotbarSize}칸, 도구 {ownedTools.Length}개");
        }

        /// <summary>인스펙터의 Starting Items 목록을 슬롯 배열에 반영한다 (테스트/디버그용).</summary>
        private void ApplyStartingItems()
        {
            if (startingItems == null) return;

            foreach (var entry in startingItems)
            {
                if (entry.item == null) continue;
                if (entry.slotIndex < 0 || entry.slotIndex >= slots.Length)
                {
                    Debug.LogWarning($"[InventoryManager] Starting Item '{entry.item.itemName}'의 slotIndex({entry.slotIndex})가 범위를 벗어남 (0~{slots.Length - 1})");
                    continue;
                }

                slots[entry.slotIndex].item = entry.item;
                slots[entry.slotIndex].quantity = entry.quantity;
                slots[entry.slotIndex].quality = entry.quality;
                Debug.Log($"[시작 아이템 배치] 슬롯 {entry.slotIndex + 1}번 - {entry.item.itemName} x{entry.quantity} ({entry.quality})");
            }
        }

        // ---------- 도구 ----------

        public void CycleTool(int direction)
        {
            if (ownedTools.Length == 0) return;

            currentToolIndex = (currentToolIndex + direction + ownedTools.Length) % ownedTools.Length;
            Debug.Log($"[도구 전환] 현재 도구: {CurrentTool}");
            OnToolChanged?.Invoke(CurrentTool);
        }

        // ---------- 핫바 선택 ----------

        public void SelectHotbarSlot(int index)
        {
            if (index < 0 || index >= hotbarSize) return;
            if (index == selectedHotbarIndex) return;

            selectedHotbarIndex = index;
            InventorySlotData slot = GetSlot(index);
            string itemName = (slot != null && !slot.IsEmpty) ? slot.item.itemName : "(비어있음)";
            Debug.Log($"[핫바 선택] 슬롯 {index + 1}번 선택됨 - 아이템: {itemName}");
            OnSelectedHotbarIndexChanged?.Invoke(selectedHotbarIndex);
        }

        // ---------- 인벤토리 열기/닫기 ----------

        public void ToggleInventory()
        {
            isInventoryOpen = !isInventoryOpen;
            Debug.Log($"[인벤토리] {(isInventoryOpen ? "열림" : "닫힘")}");
            OnInventoryToggled?.Invoke(isInventoryOpen);
        }

        // ---------- 슬롯 조회/조작 ----------

        public InventorySlotData GetSlot(int index)
        {
            if (slots == null || index < 0 || index >= slots.Length) return null;
            return slots[index];
        }

        public void SetSlot(int index, ItemData item, int quantity, ItemQuality quality = ItemQuality.Normal)
        {
            InventorySlotData slot = GetSlot(index);
            if (slot == null) return;

            slot.item = item;
            slot.quantity = quantity;
            slot.quality = quality;

            string name = item != null ? item.itemName : "(비움)";
            Debug.Log($"[슬롯 설정] {index + 1}번 슬롯에 {name} x{quantity} ({quality}) 배치됨");
            OnInventoryChanged?.Invoke();
        }

        /// <summary>
        /// 아이템을 인벤토리에 추가한다. 같은 아이템 + 같은 등급인 슬롯을 우선 채우고,
        /// 없으면 빈 슬롯을 찾아 새로 놓는다. 다 못 넣으면 남은 수량을 반환한다 (인벤토리 꽉 참).
        /// </summary>
        public int AddItem(ItemData item, int quantity, ItemQuality quality = ItemQuality.Normal)
        {
            if (item == null || quantity <= 0) return quantity;

            // 1) 같은 아이템 + 같은 등급 슬롯에 최대한 채우기
            for (int i = 0; i < slots.Length && quantity > 0; i++)
            {
                if (slots[i].item == item && slots[i].quality == quality && slots[i].quantity < item.maxStack)
                {
                    int canAdd = Mathf.Min(quantity, item.maxStack - slots[i].quantity);
                    slots[i].quantity += canAdd;
                    quantity -= canAdd;
                }
            }

            // 2) 빈 슬롯에 나머지 배치
            for (int i = 0; i < slots.Length && quantity > 0; i++)
            {
                if (slots[i].IsEmpty)
                {
                    int canAdd = Mathf.Min(quantity, item.maxStack);
                    slots[i].item = item;
                    slots[i].quantity = canAdd;
                    slots[i].quality = quality;
                    quantity -= canAdd;
                }
            }

            Debug.Log($"[아이템 획득] {item.itemName} ({quality}) 추가 시도, 못 넣은 수량: {quantity}");
            OnInventoryChanged?.Invoke();
            return quantity;
        }

        /// <summary>슬롯에서 수량만큼 제거. 부족하면 있는 만큼만 제거하고 실제 제거된 수량 반환.</summary>
        public int RemoveFromSlot(int index, int quantity)
        {
            InventorySlotData slot = GetSlot(index);
            if (slot == null || slot.IsEmpty) return 0;

            int removed = Mathf.Min(quantity, slot.quantity);
            slot.quantity -= removed;
            if (slot.quantity <= 0)
            {
                slot.item = null;
                slot.quantity = 0;
                slot.quality = ItemQuality.Normal;
            }

            OnInventoryChanged?.Invoke();
            return removed;
        }
    }
}