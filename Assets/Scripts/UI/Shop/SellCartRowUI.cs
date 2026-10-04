using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FarmingSystem.Inventory;

namespace FarmingSystem.UI.Shop
{
    /// <summary>"선택한 물품" 한 줄: 아이콘 / 이름 / [-] 수량 [+] / @단가 / 합계 / [X]</summary>
    public class SellCartRowUI : MonoBehaviour
    {
        [SerializeField] private Image itemIcon;
        [SerializeField] private TextMeshProUGUI itemNameText;
        [SerializeField] private TextMeshProUGUI unitPriceText;
        [SerializeField] private TextMeshProUGUI lineTotalText;
        [SerializeField] private TMP_InputField quantityInput;
        [SerializeField] private Button minusButton;
        [SerializeField] private Button plusButton;
        [SerializeField] private Button removeButton;

        // 판매 창이 갱신될 때마다 새로 생성되므로 리스너가 누적되지 않는다
        public void Setup(InventorySlotData slot, int quantity, float unitPrice, int lineTotal,
                          Action<int> setQuantity, Action remove)
        {
            itemIcon.enabled = slot.item.icon != null;
            itemIcon.sprite = slot.item.icon;
            itemNameText.text = slot.item.itemName + ShopUIUtil.QualityMark(slot.quality);
            unitPriceText.text = "@ " + ShopUIUtil.UnitPrice(unitPrice);
            lineTotalText.text = ShopUIUtil.Money(lineTotal);
            quantityInput.SetTextWithoutNotify(quantity.ToString());

            minusButton.interactable = quantity > 1;
            plusButton.interactable = quantity < slot.quantity;

            minusButton.onClick.AddListener(() => setQuantity(Mathf.Max(1, quantity - ShopUIUtil.Step())));
            plusButton.onClick.AddListener(() => setQuantity(quantity + ShopUIUtil.Step()));
            quantityInput.onEndEdit.AddListener(text =>
                setQuantity(int.TryParse(text, out int v) ? Mathf.Max(1, v) : quantity));
            removeButton.onClick.AddListener(() => remove());
        }
    }
}