using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace FarmingSystem.UI
{
    /// <summary>
    /// 슬롯 1칸의 표시(아이콘, 수량, 선택 상태)를 담당.
    /// 핫바와 전체 인벤토리 창 둘 다 이 컴포넌트를 그대로 재사용한다.
    /// 선택 표시는 두 가지 방식 중 하나로 동작한다:
    /// 1) normalSlotSprite/selectedSlotSprite가 있으면 배경 스프라이트 자체를 교체 (지금 UI 에셋처럼
    ///    "일반 슬롯"과 "선택된 슬롯"이 서로 다른 그림인 경우)
    /// 2) 없으면 selectedHighlight 오브젝트를 켜고 끄는 방식 (테두리 오버레이 등)
    /// </summary>
    public class ItemSlotUI : MonoBehaviour
    {
        [Header("UI 요소")]
        [SerializeField] private Image background;
        [SerializeField] private Image itemIcon;
        [SerializeField] private TextMeshProUGUI quantityText;
        [SerializeField] private GameObject selectedHighlight; // 방식 2번용 (선택 사항)

        [Header("배경 스프라이트 교체 방식 (선택 사항)")]
        [SerializeField] private Sprite normalSlotSprite;
        [SerializeField] private Sprite selectedSlotSprite;

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

            if (background != null && normalSlotSprite != null && selectedSlotSprite != null)
                background.sprite = isSelected ? selectedSlotSprite : normalSlotSprite;
        }
    }
}