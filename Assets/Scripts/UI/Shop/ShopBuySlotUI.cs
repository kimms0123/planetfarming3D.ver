using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FarmingSystem.Economy;

namespace FarmingSystem.UI.Shop
{
    /// <summary>
    /// 구매 목록 1칸. 클릭하면 "선택"만 되고,
    /// 실제 구매는 하단 상세창에서 수량을 정한 뒤 [구매] 버튼으로 한다.
    /// </summary>
    public class ShopBuySlotUI : MonoBehaviour
    {
        [Header("UI 요소")]
        [SerializeField] private Image itemIcon;
        [SerializeField] private TextMeshProUGUI itemNameText;
        [SerializeField] private TextMeshProUGUI priceText;
        [Tooltip("선택 사항. 재고 제한 상품일 때만 '재고 n' / '품절' 표시")]
        [SerializeField] private TextMeshProUGUI stockText;
        [SerializeField] private GameObject selectedFrame;
        [SerializeField] private Button button;
        [Tooltip("선택 사항. 품절이면 흐리게")]
        [SerializeField] private CanvasGroup canvasGroup;

        public int CatalogIndex { get; private set; } = -1;

        public void SetEntry(int catalogIndex, ShopItemEntry entry, Action<ShopBuySlotUI> onClick)
        {
            CatalogIndex = catalogIndex;

            itemIcon.enabled = entry.item.icon != null;
            itemIcon.sprite = entry.item.icon;
            itemNameText.text = entry.item.itemName;
            priceText.text = ShopUIUtil.Money(entry.price);
            RefreshStock(entry);
            SetSelected(false);

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => onClick?.Invoke(this));
        }

        public void RefreshStock(ShopItemEntry entry)
        {
            if (stockText != null)
            {
                stockText.gameObject.SetActive(entry.stock >= 0);
                stockText.text = entry.stock == 0 ? "품절" : $"재고 {entry.stock}";
            }
            if (canvasGroup != null)
                canvasGroup.alpha = entry.stock == 0 ? 0.4f : 1f;
        }

        public void SetSelected(bool on)
        {
            if (selectedFrame != null) selectedFrame.SetActive(on);
        }
    }
}