using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FarmingSystem.Inventory;

namespace FarmingSystem.UI
{
    /// <summary>
    /// 핫바 옆에 별도로 있는 "현재 장착 도구" 표시 슬롯.
    /// InventoryManager.OnToolChanged를 구독해서 아이콘/텍스트를 갱신한다.
    /// 아이콘 에셋이 아직 없다면 toolIcons를 비워두고 이름 텍스트만 표시해도 동작한다.
    /// </summary>
    public class ToolSlotUI : MonoBehaviour
    {
        [Header("UI 요소")]
        [SerializeField] private Image toolIcon;
        [SerializeField] private TextMeshProUGUI toolNameText; // 아이콘 에셋 준비 전까지 임시로 이름 텍스트 표시용

        [Header("도구별 아이콘 (ToolType enum 순서와 동일하게 배열)")]
        [SerializeField] private Sprite[] toolIcons;

        private bool isSubscribed = false;

        private void OnEnable()
        {
            TrySubscribe();
        }

        private void Start()
        {
            TrySubscribe();
            if (InventoryManager.Instance != null)
                Refresh(InventoryManager.Instance.CurrentTool);
        }

        private void OnDisable()
        {
            if (InventoryManager.Instance == null) return;
            InventoryManager.Instance.OnToolChanged -= Refresh;
        }

        private void TrySubscribe()
        {
            if (isSubscribed) return;
            if (InventoryManager.Instance == null) return;

            InventoryManager.Instance.OnToolChanged += Refresh;
            isSubscribed = true;

            Debug.Log("[ToolSlotUI] InventoryManager 이벤트 구독 완료");
        }

        private void Refresh(ToolType tool)
        {
            int index = (int)tool;

            if (toolIcon != null)
            {
                Sprite icon = (toolIcons != null && index >= 0 && index < toolIcons.Length) ? toolIcons[index] : null;
                toolIcon.enabled = icon != null;
                toolIcon.sprite = icon;
            }

            if (toolNameText != null)
                toolNameText.text = GetDisplayName(tool);

            Debug.Log($"[ToolSlotUI] 표시 갱신: {tool}");
        }

        private string GetDisplayName(ToolType tool)
        {
            switch (tool)
            {
                case ToolType.Hoe: return "호미";
                case ToolType.WateringCan: return "물뿌리개";
                default: return tool.ToString();
            }
        }
    }
}