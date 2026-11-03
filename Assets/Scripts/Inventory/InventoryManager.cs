using System;
using UnityEngine;

namespace FarmingSystem.Inventory
{
    // 순서 주의: 새 값은 항상 맨 끝에 추가 (None = 도구칸이 비어 있어 든 도구가 없음)
    public enum ToolType
    {
        Hoe,
        WateringCan,
        None
    }

    // 칸이 어느 구역에 있는지 (가방 30칸 / 도구칸)
    public enum SlotArea
    {
        Main,
        Tool
    }

    [Serializable]
    public class InventorySlotData
    {
        public ItemData item;
        public int quantity;
        public ItemQuality quality = ItemQuality.Normal; // Crop 계열이 아닌 아이템은 의미 없음, 항상 Normal 취급

        public bool IsEmpty => item == null || quantity <= 0;

        public void Clear()
        {
            item = null;
            quantity = 0;
            quality = ItemQuality.Normal;
        }

        public void CopyFrom(InventorySlotData other)
        {
            item = other.item;
            quantity = other.quantity;
            quality = other.quality;
        }
    }

    /// <summary>
    /// 스타듀밸리 방식 인벤토리: 가방 30칸(10칸 x 3줄) 중 앞 10칸(인덱스 0~9)이
    /// 곧 핫바다. 별도 배열이 아니라 완전히 같은 데이터라서, 핫바 UI와
    /// 전체 인벤토리 UI(Tab)는 항상 자동으로 동기화된다.
    ///
    /// 도구칸: 가방과 따로 있는 칸(기본 4칸). 도구(ToolData)만 들어갈 수 있고,
    /// 마우스 휠은 도구칸에 들어 있는 도구만 순환한다. 도구는 가방 ↔ 도구칸을 끌어서 옮길 수 있다.
    ///
    /// 씨앗(SeedData)과 수확물(CropData)은 서로 다른 ScriptableObject 타입이라
    /// 인벤토리 로직이 둘을 구분하기 위한 별도 플래그가 필요 없다 - 타입 자체가 구분 기준이다.
    /// </summary>
    public class InventoryManager : MonoBehaviour
    {
        public static InventoryManager Instance { get; private set; }

        [Header("도구칸 (마우스 휠은 이 칸에 들어 있는 도구만 순환)")]
        [Tooltip("도구칸 개수")]
        [SerializeField] private int toolSlotCount = 4;
        [Tooltip("시작할 때 도구칸에 앞에서부터 넣을 도구 (ToolData 에셋: 괭이, 물뿌리개 ...)")]
        [SerializeField] private ToolData[] startingTools;
        [SerializeField] private int currentToolSlot = 0;

        [Header("전체 인벤토리")]
        [Tooltip("10칸 x 3줄 = 30칸. 인덱스 0~9가 핫바(숫자키 1~9,0)와 동일하다.")]
        [SerializeField] private int totalSlotCount = 30;
        [SerializeField] private int hotbarSize = 10;

        private InventorySlotData[] slots;
        private InventorySlotData[] toolSlots;
        private ToolType lastAnnouncedTool = ToolType.None;

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

        // ---------- 도구 ----------

        /// <summary>지금 손에 든 도구 종류 (도구칸이 비었거나 선택 칸이 비면 None)</summary>
        public ToolType CurrentTool
        {
            get
            {
                ToolData t = CurrentToolData;
                return t != null ? t.toolType : ToolType.None;
            }
        }

        /// <summary>지금 손에 든 도구 아이템 (아이콘·이름 표시용)</summary>
        public ToolData CurrentToolData
        {
            get
            {
                InventorySlotData s = GetSlot(SlotArea.Tool, currentToolSlot);
                return s != null && !s.IsEmpty ? s.item as ToolData : null;
            }
        }

        /// <summary>선택된 도구칸 번호 (0부터)</summary>
        public int CurrentToolIndex => currentToolSlot;

        /// <summary>도구칸에 들어 있는 도구 개수</summary>
        public int ToolCount
        {
            get
            {
                int n = 0;
                if (toolSlots != null) foreach (InventorySlotData s in toolSlots) if (!s.IsEmpty) n++;
                return n;
            }
        }

        public int ToolSlotCount => toolSlotCount;

        // ---------- 가방·핫바 ----------

        public int HotbarSize => hotbarSize;
        public int TotalSlotCount => totalSlotCount;
        public int SelectedHotbarIndex => selectedHotbarIndex;

        /// <summary>
        /// 현재 핫바에서 선택된 슬롯의 아이템이 SeedData(씨앗)일 때만 반환, 아니면 null.
        /// 수확물(CropData)이 선택되어 있으면 심을 수 없으므로 여기서 자연스럽게 걸러진다.
        /// </summary>
        public FarmingSystem.Farming.SeedData CurrentSeed
        {
            get
            {
                InventorySlotData slot = GetSlot(selectedHotbarIndex);
                if (slot == null || slot.IsEmpty) return null;
                return slot.item as FarmingSystem.Farming.SeedData;
            }
        }

        public event Action<ToolType> OnToolChanged;
        public event Action<int> OnSelectedHotbarIndexChanged;
        public event Action OnInventoryChanged;
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

            toolSlots = new InventorySlotData[Mathf.Max(0, toolSlotCount)];
            for (int i = 0; i < toolSlots.Length; i++)
                toolSlots[i] = new InventorySlotData();

            ApplyStartingTools();
            ApplyStartingItems();
            FixToolSelection(announce: false);
            lastAnnouncedTool = CurrentTool;

            Debug.Log($"[InventoryManager] 초기화 완료. 가방 {totalSlotCount}칸, 핫바 {hotbarSize}칸, 도구칸 {toolSlots.Length}칸 (도구 {ToolCount}개, 든 도구: {CurrentTool})");
            if (ToolCount == 0)
                Debug.LogWarning("[InventoryManager] 도구칸이 비어 있어요. 괭이·물뿌리개를 쓰려면 ToolData 에셋을 만들어 Starting Tools에 넣어 주세요 (Create → FarmingSystem → Tool Data)");
        }

        private void ApplyStartingTools()
        {
            if (startingTools == null) return;
            int slot = 0;
            foreach (ToolData tool in startingTools)
            {
                if (tool == null) continue;
                if (slot >= toolSlots.Length)
                {
                    Debug.LogWarning($"[InventoryManager] 도구칸({toolSlots.Length}칸)이 모자라서 '{tool.itemName}'을 가방에 넣음");
                    AddToMainOnly(tool, 1, ItemQuality.Normal);
                    continue;
                }
                toolSlots[slot].item = tool;
                toolSlots[slot].quantity = 1;
                toolSlots[slot].quality = ItemQuality.Normal;
                slot++;
            }
        }

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

        /// <summary>마우스 휠: 도구칸에 들어 있는 도구만 순서대로 (빈 칸은 건너뜀)</summary>
        public void CycleTool(int direction)
        {
            if (toolSlots == null || toolSlots.Length == 0 || ToolCount == 0) return;
            int step = direction >= 0 ? 1 : -1;
            int i = currentToolSlot;
            for (int n = 0; n < toolSlots.Length; n++)
            {
                i = (i + step + toolSlots.Length) % toolSlots.Length;
                if (!toolSlots[i].IsEmpty)
                {
                    if (i == currentToolSlot) return;
                    currentToolSlot = i;
                    Debug.Log($"[도구 전환] 도구칸 {i + 1}번 - {CurrentTool}");
                    AnnounceTool(force: true);
                    return;
                }
            }
        }

        /// <summary>도구칸 클릭 등으로 특정 칸의 도구 들기 (빈 칸이면 무시)</summary>
        public void SelectToolSlot(int index)
        {
            InventorySlotData s = GetSlot(SlotArea.Tool, index);
            if (s == null || s.IsEmpty || index == currentToolSlot) return;
            currentToolSlot = index;
            AnnounceTool(force: true);
        }

        // 지금 선택한 도구칸이 비었으면 도구가 있는 칸으로 옮김
        private void FixToolSelection(bool announce)
        {
            if (toolSlots == null || toolSlots.Length == 0) return;
            currentToolSlot = Mathf.Clamp(currentToolSlot, 0, toolSlots.Length - 1);
            if (toolSlots[currentToolSlot].IsEmpty)
            {
                for (int i = 0; i < toolSlots.Length; i++)
                    if (!toolSlots[i].IsEmpty) { currentToolSlot = i; break; }
            }
            if (announce) AnnounceTool(force: false);
        }

        private void AnnounceTool(bool force)
        {
            ToolType now = CurrentTool;
            if (!force && now == lastAnnouncedTool) return;
            lastAnnouncedTool = now;
            OnToolChanged?.Invoke(now);
        }

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

        public void ToggleInventory() => SetInventoryOpen(!isInventoryOpen);

        public void SetInventoryOpen(bool open)
        {
            if (open == isInventoryOpen) return;
            isInventoryOpen = open;
            Debug.Log($"[인벤토리] {(isInventoryOpen ? "열림" : "닫힘")}");
            OnInventoryToggled?.Invoke(isInventoryOpen);
        }

        // ---------- 칸 읽기·쓰기 ----------

        /// <summary>가방 칸 (0~29). 기존 코드(상점·핫바)는 이걸 그대로 씀</summary>
        public InventorySlotData GetSlot(int index)
        {
            if (slots == null || index < 0 || index >= slots.Length) return null;
            return slots[index];
        }

        /// <summary>구역을 지정해서 칸 가져오기 (가방 / 도구칸)</summary>
        public InventorySlotData GetSlot(SlotArea area, int index)
        {
            InventorySlotData[] arr = area == SlotArea.Tool ? toolSlots : slots;
            if (arr == null || index < 0 || index >= arr.Length) return null;
            return arr[index];
        }

        public int GetSlotCount(SlotArea area) => area == SlotArea.Tool ? (toolSlots?.Length ?? 0) : (slots?.Length ?? 0);

        /// <summary>이 구역에 이 아이템을 놓을 수 있는지 (도구칸은 도구만)</summary>
        public static bool CanPlace(SlotArea area, ItemData item)
        {
            if (item == null) return true;
            return area != SlotArea.Tool || item is ToolData;
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
        /// 칸 → 칸으로 옮기기 (인벤토리 창에서 끌어서 놓을 때)
        /// - 빈 칸으로: 그대로 옮김
        /// - 같은 아이템·같은 등급: 최대 개수까지 합치고 남은 건 원래 칸에
        /// - 다른 아이템: 서로 자리 바꿈
        /// - 도구칸에는 도구만 (자리 바꿈으로 도구칸에 도구가 아닌 게 들어가게 되는 경우도 막음)
        /// 실패하면 false와 이유(reason)를 돌려줌
        /// </summary>
        public bool TryMoveItem(SlotArea fromArea, int fromIndex, SlotArea toArea, int toIndex, out string reason)
        {
            reason = null;
            InventorySlotData from = GetSlot(fromArea, fromIndex);
            InventorySlotData to = GetSlot(toArea, toIndex);
            if (from == null || to == null) { reason = "잘못된 칸"; return false; }
            if (from == to || from.IsEmpty) return false;

            if (!CanPlace(toArea, from.item)) { reason = "도구칸에는 도구만 넣을 수 있어요"; return false; }

            // 같은 아이템이면 합치기
            if (!to.IsEmpty && to.item == from.item && to.quality == from.quality && from.item.maxStack > 1)
            {
                int room = from.item.maxStack - to.quantity;
                if (room <= 0) { reason = "이미 가득 찬 칸이에요"; return false; }
                int moved = Mathf.Min(room, from.quantity);
                to.quantity += moved;
                from.quantity -= moved;
                if (from.quantity <= 0) from.Clear();
                AfterSlotsChanged();
                return true;
            }

            // 자리 바꾸기 (빈 칸이면 그냥 옮기기)
            if (!to.IsEmpty && !CanPlace(fromArea, to.item)) { reason = "도구칸에는 도구만 넣을 수 있어요"; return false; }
            var temp = new InventorySlotData();
            temp.CopyFrom(to);
            to.CopyFrom(from);
            if (temp.IsEmpty) from.Clear(); else from.CopyFrom(temp);
            AfterSlotsChanged();
            return true;
        }

        private void AfterSlotsChanged()
        {
            FixToolSelection(announce: true);
            OnInventoryChanged?.Invoke();
        }

        /// <summary>
        /// 아이템을 인벤토리에 추가한다. 같은 "아이템 에셋 자체(item 참조)" + 같은 등급인 슬롯을
        /// 우선 채우고, 없으면 빈 슬롯을 찾아 새로 놓는다. 도구(ToolData)는 빈 도구칸에 먼저 넣는다.
        /// 반환값 = 공간이 없어서 못 넣은 수량.
        /// </summary>
        public int AddItem(ItemData item, int quantity, ItemQuality quality = ItemQuality.Normal)
        {
            if (item == null || quantity <= 0) return quantity;

            // 도구는 빈 도구칸부터
            if (item is ToolData && toolSlots != null)
            {
                for (int i = 0; i < toolSlots.Length && quantity > 0; i++)
                {
                    if (!toolSlots[i].IsEmpty) continue;
                    toolSlots[i].item = item;
                    toolSlots[i].quantity = 1;
                    toolSlots[i].quality = ItemQuality.Normal;
                    quantity--;
                }
            }

            quantity = AddToMainOnly(item, quantity, quality);

            Debug.Log($"[아이템 획득] {item.itemName} ({quality}) 추가 시도, 못 넣은 수량: {quantity}");
            FixToolSelection(announce: true);
            OnInventoryChanged?.Invoke();
            return quantity;
        }

        // 가방 30칸에만 넣기 (같은 아이템 먼저 채우고 빈 칸). 남은 수량 반환
        private int AddToMainOnly(ItemData item, int quantity, ItemQuality quality)
        {
            for (int i = 0; i < slots.Length && quantity > 0; i++)
            {
                if (slots[i].item == item && slots[i].quality == quality && slots[i].quantity < item.maxStack)
                {
                    int canAdd = Mathf.Min(quantity, item.maxStack - slots[i].quantity);
                    slots[i].quantity += canAdd;
                    quantity -= canAdd;
                }
            }

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
            return quantity;
        }

        /// <summary>
        /// 이 아이템(+등급)을 최대 몇 개까지 더 넣을 수 있는지 계산만 한다. 실제로 넣지는 않음.
        /// 상점 구매 가능 수량, 수확 전 공간 확인 등에 사용.
        /// </summary>
        public int GetAddableAmount(ItemData item, ItemQuality quality = ItemQuality.Normal)
        {
            if (item == null || slots == null) return 0;

            int room = 0;
            foreach (InventorySlotData slot in slots)
            {
                if (slot.IsEmpty) room += item.maxStack;
                else if (slot.item == item && slot.quality == quality) room += Mathf.Max(0, item.maxStack - slot.quantity);
            }
            if (item is ToolData && toolSlots != null)
                foreach (InventorySlotData slot in toolSlots) if (slot.IsEmpty) room += 1;
            return room;
        }

        public int RemoveFromSlot(int index, int quantity)
        {
            InventorySlotData slot = GetSlot(index);
            if (slot == null || slot.IsEmpty) return 0;

            int removed = Mathf.Min(quantity, slot.quantity);
            slot.quantity -= removed;
            if (slot.quantity <= 0) slot.Clear();

            OnInventoryChanged?.Invoke();
            return removed;
        }
    }
}