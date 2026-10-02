using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using FarmingSystem.Inventory;
using FarmingSystem.Economy;

namespace FarmingSystem.UI.Shop
{
    /// <summary>
    /// 판매 창에 보이는 플레이어 인벤토리 슬롯 1칸. 좌클릭 = 1개 판매, 우클릭 = 전량 판매.
    /// 인벤토리 그 자체를 그대로 보여주는 것이므로, 슬롯 인덱스는 InventoryManager의 슬롯 인덱스와 동일하다.
    /// </summary>
    public class ShopSellSlotUI : MonoBehaviour, IPointerClickHandler
    {
        [Header("UI 요소")]
        [SerializeField] private Image itemIcon;
        [SerializeField] private TextMeshProUGUI quantityText;

        private int slotIndex = -1;

        public void SetSlot(int index, InventorySlotData slot)
        {
            slotIndex = index;

            if (slot == null || slot.IsEmpty)
            {
                if (itemIcon != null) itemIcon.enabled = false;
                if (quantityText != null) quantityText.gameObject.SetActive(false);
                return;
            }

            if (itemIcon != null)
            {
                itemIcon.enabled = slot.item.icon != null;
                itemIcon.sprite = slot.item.icon;
            }
            if (quantityText != null)
            {
                quantityText.gameObject.SetActive(true);
                quantityText.text = slot.quantity.ToString();
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (slotIndex < 0 || ShopManager.Instance == null || InventoryManager.Instance == null) return;

            InventorySlotData slot = InventoryManager.Instance.GetSlot(slotIndex);
            if (slot == null || slot.IsEmpty) return;

            int quantity = eventData.button == PointerEventData.InputButton.Right ? slot.quantity : 1;
            bool success = ShopManager.Instance.TrySell(slotIndex, quantity);

            if (success)
                Debug.Log($"[ShopSellSlotUI] 슬롯 {slotIndex}번 판매 클릭 처리 (수량 {quantity})");
        }
    }
}