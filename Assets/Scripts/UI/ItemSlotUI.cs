using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace FarmingSystem.UI
{
    /// <summary>
    /// 슬롯 1칸의 표시(아이콘, 수량, 선택 테두리)를 담당.
    /// 핫바와 전체 인벤토리 창 둘 다 이 컴포넌트를 그대로 재사용한다.
    /// </summary>
    public class ItemSlotUI : MonoBehaviour
    {
        [Header("UI 요소")]
        [SerializeField] private Image background;
        [SerializeField] private Image itemIcon;
        [SerializeField] private TextMeshProUGUI quantityText;
        [SerializeField] private GameObject selectedHighlight; // 선택됐을 때만 활성화 (핫바 전용, 인벤토리 칸은 비워둬도 됨)

        [Header("색상")]
        [SerializeField] private Color normalColor = new Color(1f, 1f, 1f, 0.5f);
        [SerializeField] private Color selectedColor = new Color(1f, 1f, 1f, 0.9f);

        public void SetItem(Sprite icon, int quantity)
        {
            if (itemIcon != null)
            {
                itemIcon.enabled = icon != null;
                itemIcon.sprite = icon;
            }

            if (quantityText != null)
            {
                bool showQuantity = icon != null && quantity > 1;
                quantityText.gameObject.SetActive(showQuantity);
                quantityText.text = quantity.ToString();
            }
        }

        public void SetSelected(bool isSelected)
        {
            if (selectedHighlight != null)
                selectedHighlight.SetActive(isSelected);

            if (background != null)
                background.color = isSelected ? selectedColor : normalColor;
        }
    }
}