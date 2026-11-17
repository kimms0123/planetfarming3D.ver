using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FarmingSystem.Inventory;

namespace FarmingSystem.UI
{
    /// <summary>
    /// 핫바 옆에 별도로 있는 "현재 장착 도구" 표시 슬롯.
    /// - 지금 든 도구의 아이콘·이름 (도구 에셋의 Icon·Item Name. 아이콘이 없으면 Tool Icons에서 종류별 그림)
    /// - 닳는 도구면 칸 아래에 내구도 막대 + 이름 옆에 남은 횟수 (예: 튼튼한 호미 87/120)
    ///   닳지 않는 도구(기본 호미·물뿌리개)는 막대 없이 이름만
    /// 도구를 바꾸거나 쓸 때마다 바로 갱신됨.
    /// </summary>
    public class ToolSlotUI : MonoBehaviour
    {
        [Header("UI 요소")]
        [SerializeField] private Image toolIcon;
        [SerializeField] private TextMeshProUGUI toolNameText;

        [Header("도구별 아이콘 (도구 에셋에 Icon이 없을 때만 씀. ToolType 순서: 호미, 물뿌리개, 없음, 낫)")]
        [SerializeField] private Sprite[] toolIcons;

        [Header("내구도")]
        [Tooltip("이름 옆에 남은 횟수 숫자도 보여 줌 (예: 87/120)")]
        [SerializeField] private bool showDurabilityNumber = true;
        [Tooltip("내구도 막대 (비우면 이 칸 아래쪽에 자동으로 만듦)")]
        [SerializeField] private Image durabilityFill;

        private InventoryManager subscribedTo;
        private GameObject durabilityBar;

        private void OnEnable() => TrySubscribe();

        private void Start()
        {
            TrySubscribe();
            Refresh();
        }

        private void OnDisable()
        {
            if (subscribedTo != null)
            {
                subscribedTo.OnToolChanged -= HandleToolChanged;
                subscribedTo.OnInventoryChanged -= Refresh;
            }
            subscribedTo = null;
        }

        private void TrySubscribe()
        {
            if (subscribedTo != null || InventoryManager.Instance == null) return;
            subscribedTo = InventoryManager.Instance;
            subscribedTo.OnToolChanged += HandleToolChanged;
            subscribedTo.OnInventoryChanged += Refresh; // 내구도가 줄 때·도구가 부서질 때
            Refresh();
        }

        private void HandleToolChanged(ToolType tool) => Refresh();

        private void Refresh()
        {
            InventoryManager inv = InventoryManager.Instance;
            if (inv == null) return;
            ToolData tool = inv.CurrentToolData;
            InventorySlotData slot = inv.GetSlot(SlotArea.Tool, inv.CurrentToolIndex);
            bool wears = tool != null && tool.maxDurability > 0 && slot != null;
            int left = wears ? Mathf.Max(0, slot.durability) : 0;

            if (toolIcon != null)
            {
                Sprite icon = tool != null ? tool.icon : null;
                int index = (int)inv.CurrentTool;
                if (icon == null && toolIcons != null && index >= 0 && index < toolIcons.Length) icon = toolIcons[index];
                toolIcon.enabled = icon != null;
                toolIcon.sprite = icon;
            }

            if (toolNameText != null)
            {
                string label = tool != null ? tool.itemName : "";
                if (wears && showDurabilityNumber) label += $"  {left}/{tool.maxDurability}";
                toolNameText.text = label;
            }

            ShowBar(wears, wears ? left / (float)tool.maxDurability : 0f);
        }

        // 내구도 막대: 초록 → 노랑 → 빨강
        private void ShowBar(bool show, float ratio)
        {
            if (durabilityFill == null)
            {
                if (!show) return;
                BuildBar();
            }
            GameObject bar = durabilityBar != null ? durabilityBar : durabilityFill.gameObject;
            bar.SetActive(show);
            if (!show) return;
            ratio = Mathf.Clamp01(ratio);
            if (durabilityBar != null) ((RectTransform)durabilityFill.transform).anchorMax = new Vector2(ratio, 1f);
            else if (durabilityFill.type == Image.Type.Filled) durabilityFill.fillAmount = ratio;
            else durabilityFill.transform.localScale = new Vector3(ratio, 1f, 1f);
            durabilityFill.color = ratio > 0.5f ? new Color(0.45f, 0.8f, 0.4f) : ratio > 0.2f ? new Color(0.95f, 0.78f, 0.3f) : new Color(0.9f, 0.35f, 0.3f);
        }

        private void BuildBar()
        {
            durabilityBar = new GameObject("DurabilityBar", typeof(RectTransform), typeof(Image));
            durabilityBar.transform.SetParent(transform, false);
            var rt = (RectTransform)durabilityBar.transform;
            rt.anchorMin = new Vector2(0.12f, 0.06f);
            rt.anchorMax = new Vector2(0.88f, 0.13f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            Image bg = durabilityBar.GetComponent<Image>();
            bg.color = new Color(0.2f, 0.16f, 0.13f, 0.75f);
            bg.raycastTarget = false;

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(durabilityBar.transform, false);
            var frt = (RectTransform)fill.transform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = frt.offsetMax = Vector2.zero;
            durabilityFill = fill.GetComponent<Image>();
            durabilityFill.raycastTarget = false;
        }
    }
}