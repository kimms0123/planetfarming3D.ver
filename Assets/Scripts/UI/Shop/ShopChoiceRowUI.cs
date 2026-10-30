using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace FarmingSystem.UI.Shop
{
    // 상점 대화창의 선택지 한 줄 (예: "구매하기")
    // - 배경(Background): 선택됐을 때만 진한 색으로 바뀜 (평소 색은 씬에서 정한 색 그대로)
    // - 글자(Label): 선택됐을 때 흰 글씨로
    // - 커서(Cursor): ▶ 표시, 선택된 줄에만 보임
    // 마우스를 올리면 Hovered, 클릭하면 Clicked 이벤트를 ShopConversationUI에 알림
    // 씬에서 줄 모양을 바꾸고 싶으면 이 오브젝트(와 자식)를 직접 고치면 됨. 줄이 모자라면 첫 줄을 복사해서 늘림
    public class ShopChoiceRowUI : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
    {
        [SerializeField] private Image background;
        [SerializeField] private TextMeshProUGUI label;
        [SerializeField] private Graphic cursor;

        public event Action<ShopChoiceRowUI> Hovered;
        public event Action<ShopChoiceRowUI> Clicked;

        private Color normalBackColor;
        private Color normalTextColor;
        private bool captured;

        public TextMeshProUGUI Label => label;

        // 코드로 줄을 만들 때 연결
        public void Setup(Image back, TextMeshProUGUI text, Graphic arrow)
        {
            background = back;
            label = text;
            cursor = arrow;
            captured = false;
            Capture();
        }

        private void Awake() => Capture();

        // 씬에서 정한 평소 색을 기억해 둠 (선택 해제할 때 되돌리려고)
        private void Capture()
        {
            if (captured) return;
            if (background != null) normalBackColor = background.color;
            if (label != null) normalTextColor = label.color;
            captured = true;
        }

        public void SetText(string text)
        {
            if (label != null) label.text = text;
        }

        public void SetSelected(bool selected, Color selectedBack, Color selectedText)
        {
            Capture();
            if (background != null) background.color = selected ? selectedBack : normalBackColor;
            if (label != null) label.color = selected ? selectedText : normalTextColor;
            if (cursor != null) cursor.enabled = selected;
        }

        public void OnPointerEnter(PointerEventData eventData) => Hovered?.Invoke(this);
        public void OnPointerClick(PointerEventData eventData) => Clicked?.Invoke(this);
    }
}