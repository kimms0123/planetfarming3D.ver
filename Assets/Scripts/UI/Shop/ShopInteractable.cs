using UnityEngine;
using UnityEngine.InputSystem;
using FarmingSystem.Core;
using FarmingSystem.Economy;
using FarmingSystem.Inventory;

namespace FarmingSystem.UI.Shop
{
    /// <summary>
    /// 상인 NPC(또는 우주선)에 붙이는 컴포넌트.
    /// 상점이 열려있고, 플레이어가 가까이 있고, 상인 쪽을 바라보고 있을 때 E를 누르면 대화(선택지) 창을 연다.
    /// "바라본다" = 카메라(시선)의 수평 방향과 상인 방향 사이 각도가 maxLookAngle 이내.
    /// </summary>
    public class ShopInteractable : MonoBehaviour
    {
        [SerializeField] private float interactRange = 2f;
        [SerializeField] private Transform player;
        [SerializeField] private ShopDialogueUI dialogueUI;
        [Tooltip("시선과 상인 방향 사이 허용 각도. 작을수록 정확히 바라봐야 함")]
        [SerializeField] private float maxLookAngle = 40f;
        [Tooltip("E를 눌렀는데 안 열리면 콘솔에 이유를 출력 (테스트용)")]
        [SerializeField] private bool logWhyNotOpening = true;

        /// <summary>지금 E를 누르면 대화가 시작되는 상태인지 (나중에 "E 대화하기" 안내 UI에 사용)</summary>
        public bool CanInteract { get; private set; }

        private void Update()
        {
            bool pressedE = Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
            string reason = GetBlockReason();
            CanInteract = reason == null;

            if (!pressedE) return;

            if (CanInteract)
            {
                dialogueUI.OpenDialogue();
                Debug.Log("[ShopInteractable] 상인과 대화 시작");
            }
            else if (logWhyNotOpening)
            {
                Debug.Log($"[ShopInteractable] E를 눌렀지만 상점이 안 열림 - 이유: {reason}");
            }
        }

        /// <summary>지금 대화를 시작할 수 없는 이유. 시작할 수 있으면 null.</summary>
        private string GetBlockReason()
        {
            if (ShopManager.Instance == null) return "씬에 ShopManager가 없음";
            if (!ShopManager.Instance.IsShopOpen) return "상인이 방문 중이 아님 (ShopManager의 디버그 메뉴나 Debug Open On Start로 열 수 있음)";
            if (player == null) return "ShopInteractable의 Player 칸이 비어 있음";
            if (dialogueUI == null) return "ShopInteractable의 Dialogue UI 칸이 비어 있음";
            if (UIInputBlocker.IsBlocked) return "이미 다른 UI가 열려 있음";
            if (InventoryManager.Instance != null && InventoryManager.Instance.IsInventoryOpen) return "인벤토리(Tab)가 열려 있음";

            float distance = Vector3.Distance(transform.position, player.position);
            if (distance > interactRange) return $"너무 멂 (거리 {distance:0.0}m, 허용 {interactRange}m)";

            float angle = GetLookAngle();
            if (angle > maxLookAngle) return $"상인을 바라보고 있지 않음 (각도 {angle:0}도, 허용 {maxLookAngle}도)";

            return null;
        }

        /// <summary>시선(카메라 수평 방향)과 "플레이어 → 상인" 방향 사이 각도</summary>
        private float GetLookAngle()
        {
            Camera cam = Camera.main;
            if (cam == null) return 0f;

            Vector3 view = cam.transform.forward;
            view.y = 0f;
            Vector3 toMerchant = transform.position - player.position;
            toMerchant.y = 0f;
            if (view.sqrMagnitude < 0.0001f || toMerchant.sqrMagnitude < 0.0001f) return 0f;

            return Vector3.Angle(view, toMerchant);
        }
    }
}