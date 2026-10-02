using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FarmingSystem.Inventory;
using FarmingSystem.Economy;

namespace FarmingSystem.UI.Shop
{
    /// <summary>
    /// "팔 때" 창. 플레이어 인벤토리 30칸을 그대로 보여주고(판매 전용, 클릭하면 팔림),
    /// 오늘의 특별 상품(판매 보너스 작물)을 함께 표시한다.
    /// </summary>
    public class ShopSellPanelUI : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [Tooltip("씬에 미리 배치한 30개의 슬롯 UI를 인덱스 0~29 순서대로 연결 (InventoryUI의 슬롯과 별개로 판매창 전용으로 하나 더 필요)")]
        [SerializeField] private ShopSellSlotUI[] slots = new ShopSellSlotUI[30];

        [Header("오늘의 특별 상품 표시")]
        [SerializeField] private Image specialItemIcon;
        [SerializeField] private TextMeshProUGUI specialItemText;

        private bool isSubscribed = false;

        private void OnEnable()
        {
            TrySubscribe();
        }

        private void OnDisable()
        {
            if (InventoryManager.Instance != null)
                InventoryManager.Instance.OnInventoryChanged -= Refresh;
        }

        private void TrySubscribe()
        {
            if (isSubscribed) return;
            if (InventoryManager.Instance == null) return;

            InventoryManager.Instance.OnInventoryChanged += Refresh;
            isSubscribed = true;
        }

        public void Open()
        {
            if (panelRoot != null) panelRoot.SetActive(true);
            TrySubscribe();
            Refresh();
            Debug.Log("[ShopSellPanelUI] 판매 창 열림");
        }

        public void Close()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        private void Refresh()
        {
            if (InventoryManager.Instance == null) return;

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null) continue;
                slots[i].SetSlot(i, InventoryManager.Instance.GetSlot(i));
            }

            RefreshSpecialItem();
            Debug.Log("[ShopSellPanelUI] 판매 창 갱신 완료");
        }

        private void RefreshSpecialItem()
        {
            if (ShopManager.Instance != null && ShopManager.Instance.TodaysSpecialCrop != null)
            {
                var special = ShopManager.Instance.TodaysSpecialCrop;
                if (specialItemIcon != null)
                {
                    specialItemIcon.enabled = special.icon != null;
                    specialItemIcon.sprite = special.icon;
                }
                if (specialItemText != null)
                    specialItemText.text = $"오늘의 특별 상품: {special.cropName} (판매가 x{ShopManager.Instance.SpecialSellBonusMultiplier})";
            }
            else
            {
                if (specialItemIcon != null) specialItemIcon.enabled = false;
                if (specialItemText != null) specialItemText.text = "오늘은 특별 상품이 없습니다";
            }
        }
    }
}