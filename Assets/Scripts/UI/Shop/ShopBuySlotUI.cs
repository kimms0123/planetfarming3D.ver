using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using FarmingSystem.Economy;

namespace FarmingSystem.UI.Shop
{
    /// <summary>
    /// 구매 목록 1칸. 좌클릭 = 1개 구매, 우클릭 = 10개 구매.
    /// </summary>
    public class ShopBuySlotUI : MonoBehaviour, IPointerClickHandler
    {
        [Header("UI 요소")]
        [SerializeField] private Image itemIcon;
        [SerializeField] private TextMeshProUGUI itemNameText;
        [SerializeField] private TextMeshProUGUI priceText;

        private int catalogIndex = -1;

        public void SetEntry(int index, ShopItemEntry entry)
        {
            catalogIndex = index;

            if (entry == null || entry.item == null)
            {
                if (itemIcon != null) itemIcon.enabled = false;
                if (itemNameText != null) itemNameText.text = "";
                if (priceText != null) priceText.text = "";
                return;
            }

            if (itemIcon != null)
            {
                itemIcon.enabled = entry.item.icon != null;
                itemIcon.sprite = entry.item.icon;
            }
            if (itemNameText != null)
                itemNameText.text = entry.item.itemName;
            if (priceText != null)
                priceText.text = entry.stock < 0 ? $"{entry.price}벨" : $"{entry.price}벨 (재고 {entry.stock})";
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (catalogIndex < 0 || ShopManager.Instance == null) return;

            int quantity = eventData.button == PointerEventData.InputButton.Right ? 10 : 1;
            bool success = ShopManager.Instance.TryBuy(catalogIndex, quantity);

            if (success)
                Debug.Log($"[ShopBuySlotUI] 슬롯 {catalogIndex}번 구매 클릭 처리 (수량 {quantity})");
        }
    }
}