using UnityEngine;
using FarmingSystem.Inventory;

namespace FarmingSystem.UI
{
    /// <summary>
    /// 화면 하단에 항상 떠있는 핫바 10칸.
    /// InventoryManager의 슬롯 0~9번을 그대로 보여준다 (별도 데이터 아님, 항상 동기화됨).
    /// </summary>
    public class HotbarUI : MonoBehaviour
    {
        [Tooltip("씬에 미리 배치한 10개의 슬롯 UI를 순서대로(1~9, 0) 연결")]
        [SerializeField] private ItemSlotUI[] slots = new ItemSlotUI[10];

        private bool isSubscribed = false;

        private void OnEnable()
        {
            TrySubscribe();
        }

        private void Start()
        {
            TrySubscribe();
            RefreshAll();
        }

        private void OnDisable()
        {
            if (InventoryManager.Instance == null) return;

            InventoryManager.Instance.OnInventoryChanged -= RefreshAll;
            InventoryManager.Instance.OnSelectedHotbarIndexChanged -= HandleSelectedChanged;
        }

        private void TrySubscribe()
        {
            if (isSubscribed) return;
            if (InventoryManager.Instance == null) return;

            InventoryManager.Instance.OnInventoryChanged += RefreshAll;
            InventoryManager.Instance.OnSelectedHotbarIndexChanged += HandleSelectedChanged;
            isSubscribed = true;

            Debug.Log("[HotbarUI] InventoryManager 이벤트 구독 완료");
        }

        private void HandleSelectedChanged(int newIndex)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null)
                    slots[i].SetSelected(i == newIndex);
            }
        }

        private void RefreshAll()
        {
            if (InventoryManager.Instance == null) return;

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null) continue;

                var slotData = InventoryManager.Instance.GetSlot(i);
                Sprite icon = (slotData != null && !slotData.IsEmpty) ? slotData.item.icon : null;
                int quantity = slotData != null ? slotData.quantity : 0;

                slots[i].SetItem(icon, quantity);
                slots[i].SetSelected(i == InventoryManager.Instance.SelectedHotbarIndex);
            }

            Debug.Log("[HotbarUI] 전체 슬롯 갱신 완료");
        }
    }
}