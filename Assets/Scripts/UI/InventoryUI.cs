using UnityEngine;
using FarmingSystem.Inventory;

namespace FarmingSystem.UI
{
    /// <summary>
    /// Tab 키로 열리고 닫히는 전체 인벤토리 창 (10칸 x 3줄 = 30칸).
    /// 맨 앞 10칸(인덱스 0~9)은 화면 하단 HotbarUI와 완전히 같은 데이터라 항상 동기화된다.
    /// </summary>
    public class InventoryUI : MonoBehaviour
    {
        [Header("루트 오브젝트 (창 전체를 켜고 끄는 대상)")]
        [SerializeField] private GameObject panelRoot;

        [Tooltip("씬에 미리 배치한 30개의 슬롯 UI를 인덱스 0~29 순서대로 연결")]
        [SerializeField] private ItemSlotUI[] slots = new ItemSlotUI[30];

        private bool isSubscribed = false;

        private void OnEnable()
        {
            TrySubscribe();
        }

        private void Start()
        {
            TrySubscribe();
            RefreshAll();

            if (panelRoot != null)
                panelRoot.SetActive(InventoryManager.Instance != null && InventoryManager.Instance.IsInventoryOpen);
        }

        private void OnDisable()
        {
            if (InventoryManager.Instance == null) return;

            InventoryManager.Instance.OnInventoryChanged -= RefreshAll;
            InventoryManager.Instance.OnInventoryToggled -= HandleToggled;
        }

        private void TrySubscribe()
        {
            if (isSubscribed) return;
            if (InventoryManager.Instance == null) return;

            InventoryManager.Instance.OnInventoryChanged += RefreshAll;
            InventoryManager.Instance.OnInventoryToggled += HandleToggled;
            isSubscribed = true;

            Debug.Log("[InventoryUI] InventoryManager 이벤트 구독 완료");
        }

        private void HandleToggled(bool isOpen)
        {
            if (panelRoot != null)
                panelRoot.SetActive(isOpen);

            if (isOpen)
                RefreshAll();
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
            }

            Debug.Log("[InventoryUI] 전체 30칸 갱신 완료");
        }
    }
}