using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using FarmingSystem.Inventory;

namespace FarmingSystem.UI
{
    // 인벤토리 창의 칸 하나 (가방 칸 / 도구칸 공용)
    // - 아이콘·수량·등급 별·단축키 번호·선택 테두리를 보여줌
    // - 마우스: 왼쪽 끌기 = 옮기기, 클릭 = 핫바 칸이면 그 칸 선택 / 도구칸이면 그 도구 들기, 올리기 = 아래 설명줄에 정보
    // - 실제 옮기기·설명 표시는 InventoryWindowUI가 처리 (이 칸은 알려주기만)
    public class InventorySlotView : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
    {
        [Header("칸 안 구성 (프리팹에서 연결)")]
        [SerializeField] private Image background;
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text quantityText;
        [Tooltip("상급·최상급일 때 보이는 별")]
        [SerializeField] private Image gradeBadge;
        [Tooltip("핫바 줄의 단축키 번호 (1~9, 0)")]
        [SerializeField] private TMP_Text hotkeyText;
        [Tooltip("선택된 핫바 칸 / 지금 든 도구 표시")]
        [SerializeField] private GameObject selectedFrame;

        [Header("등급 별 색")]
        [SerializeField] private Color perfectColor = Color.white;                       // 별 원래 색 (노랑)
        [SerializeField] private Color goodColor = new Color(0.75f, 0.85f, 1f, 1f);      // 은색 느낌

        public SlotArea Area { get; private set; }
        public int Index { get; private set; }

        private InventoryWindowUI window;
        private bool hasItem;

        public void Setup(InventoryWindowUI owner, SlotArea area, int index, string hotkey)
        {
            window = owner;
            Area = area;
            Index = index;
            if (hotkeyText != null)
            {
                hotkeyText.gameObject.SetActive(!string.IsNullOrEmpty(hotkey));
                hotkeyText.text = hotkey;
            }
        }

        // 칸 내용 그리기
        public void Show(InventorySlotData data, bool selected)
        {
            hasItem = data != null && !data.IsEmpty;
            if (icon != null)
            {
                icon.enabled = hasItem && data.item.icon != null;
                icon.sprite = hasItem ? data.item.icon : null;
                icon.color = Color.white;
            }
            if (quantityText != null)
            {
                bool showQty = hasItem && data.quantity > 1;
                quantityText.gameObject.SetActive(showQty);
                if (showQty) quantityText.text = data.quantity.ToString();
            }
            if (gradeBadge != null)
            {
                bool star = hasItem && (data.quality == ItemQuality.Perfect || data.quality == ItemQuality.Good);
                gradeBadge.gameObject.SetActive(star);
                if (star) gradeBadge.color = data.quality == ItemQuality.Perfect ? perfectColor : goodColor;
            }
            if (selectedFrame != null) selectedFrame.SetActive(selected);
            ShowDurability(data);
        }

        // 도구 내구도 막대 (칸 아래쪽, 닳는 도구만). 처음 필요할 때 만듦
        private RectTransform durabilityBar;
        private Image durabilityFill;

        private void ShowDurability(InventorySlotData data)
        {
            ToolData tool = hasItem ? data.item as ToolData : null;
            bool show = tool != null && tool.maxDurability > 0;
            if (!show) { if (durabilityBar != null) durabilityBar.gameObject.SetActive(false); return; }

            if (durabilityBar == null)
            {
                var bg = new GameObject("DurabilityBar", typeof(RectTransform), typeof(Image));
                bg.transform.SetParent(transform, false);
                durabilityBar = (RectTransform)bg.transform;
                durabilityBar.anchorMin = new Vector2(0.14f, 0.09f);
                durabilityBar.anchorMax = new Vector2(0.86f, 0.16f);
                durabilityBar.offsetMin = durabilityBar.offsetMax = Vector2.zero;
                Image bgImg = bg.GetComponent<Image>();
                bgImg.color = new Color(0.2f, 0.16f, 0.13f, 0.75f);
                bgImg.raycastTarget = false;

                var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
                fill.transform.SetParent(durabilityBar, false);
                var frt = (RectTransform)fill.transform;
                frt.anchorMin = Vector2.zero;
                frt.anchorMax = Vector2.one;
                frt.offsetMin = frt.offsetMax = Vector2.zero;
                durabilityFill = fill.GetComponent<Image>();
                durabilityFill.raycastTarget = false;
            }
            durabilityBar.gameObject.SetActive(true);
            float k = Mathf.Clamp01(data.durability / (float)tool.maxDurability);
            ((RectTransform)durabilityFill.transform).anchorMax = new Vector2(k, 1f);
            durabilityFill.color = k > 0.5f ? new Color(0.45f, 0.8f, 0.4f) : k > 0.2f ? new Color(0.95f, 0.78f, 0.3f) : new Color(0.9f, 0.35f, 0.3f);
        }

        // 끌고 있는 동안 원래 칸 아이콘을 흐리게
        public void SetDragging(bool dragging)
        {
            if (icon != null && icon.enabled) icon.color = dragging ? new Color(1f, 1f, 1f, 0.35f) : Color.white;
        }

        public Sprite IconSprite => icon != null ? icon.sprite : null;

        public void OnPointerEnter(PointerEventData e) => window?.HandleHover(this, true);
        public void OnPointerExit(PointerEventData e) => window?.HandleHover(this, false);

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || e.dragging) return;
            window?.HandleClick(this);
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || !hasItem) return;
            window?.BeginDrag(this, e);
        }

        public void OnDrag(PointerEventData e) => window?.Drag(e);
        public void OnEndDrag(PointerEventData e) => window?.EndDrag(e);
        public void OnDrop(PointerEventData e) => window?.Drop(this);
    }
}