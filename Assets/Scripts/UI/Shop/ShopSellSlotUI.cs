using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using FarmingSystem.Inventory;

namespace FarmingSystem.UI.Shop
{
    /// <summary>
    /// 판매 창에 보이는 인벤토리 슬롯 1칸. 슬롯 인덱스는 InventoryManager의 슬롯 인덱스와 동일하다.
    /// 좌클릭: 선택 목록에 1개 추가 (Shift = 10개) / 우클릭: 전량 추가. 실제 판매는 [바로 판매]에서.
    /// </summary>
    public class ShopSellSlotUI : MonoBehaviour, IPointerClickHandler
    {
        [Header("UI 요소")]
        [Tooltip("빈 슬롯이면 통째로 꺼지는 내용물 묶음")]
        [SerializeField] private GameObject contentRoot;
        [SerializeField] private Image itemIcon;
        [SerializeField] private TextMeshProUGUI itemNameText;
        [SerializeField] private TextMeshProUGUI quantityText;
        [SerializeField] private TextMeshProUGUI priceText;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private GameObject selectedFrame;
        [Tooltip("선택 사항. 오늘의 특별 상품이면 켜지는 표시")]
        [SerializeField] private GameObject specialBadge;
        [SerializeField, Range(0f, 1f)] private float disabledAlpha = 0.4f;

        private int slotIndex = -1;
        private bool clickable = false;
        private Action<int, int> onAdd; // (슬롯 인덱스, 추가 수량)

        public void SetSlot(int index, InventorySlotData slot, bool sellable, float unitPrice,
                            bool isSpecial, int inCart, Action<int, int> onAdd)
        {
            slotIndex = index;
            this.onAdd = onAdd;

            bool empty = slot == null || slot.IsEmpty;
            contentRoot.SetActive(!empty);
            if (selectedFrame != null) selectedFrame.SetActive(!empty && inCart > 0);
            if (specialBadge != null) specialBadge.SetActive(!empty && isSpecial);
            canvasGroup.alpha = 1f;
            clickable = false;
            if (empty) return;

            int remaining = slot.quantity - inCart;

            itemIcon.enabled = slot.item.icon != null;
            itemIcon.sprite = slot.item.icon;
            itemNameText.text = slot.item.itemName + ShopUIUtil.QualityMark(slot.quality);
            quantityText.text = $"x{remaining}";
            priceText.text = sellable ? ShopUIUtil.UnitPrice(unitPrice) : "판매 불가";

            canvasGroup.alpha = sellable ? 1f : disabledAlpha;
            clickable = sellable && remaining > 0;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!clickable || onAdd == null) return;

            if (eventData.button == PointerEventData.InputButton.Right)
                onAdd(slotIndex, int.MaxValue);
            else if (eventData.button == PointerEventData.InputButton.Left)
                onAdd(slotIndex, ShopUIUtil.Step());
        }
    }
}