using UnityEngine;
using UnityEngine.InputSystem;
using FarmingSystem.Core;
using FarmingSystem.Economy;
using FarmingSystem.Inventory;

namespace FarmingSystem.UI.Shop
{
    /// <summary>
    /// 상인 NPC(또는 우주선)에 붙이는 컴포넌트.
    /// 상점이 열려있고 플레이어가 가까이 있을 때 E를 누르면 대화(선택지) 창을 연다.
    /// </summary>
    public class ShopInteractable : MonoBehaviour
    {
        [SerializeField] private float interactRange = 2f;
        [SerializeField] private Transform player;
        [SerializeField] private ShopDialogueUI dialogueUI;

        private void Update()
        {
            if (ShopManager.Instance == null || !ShopManager.Instance.IsShopOpen) return;
            if (player == null || Keyboard.current == null || dialogueUI == null) return;

            // 이미 상점/다른 UI가 열려 있거나, 인벤토리(Tab)를 보고 있을 때는 무시
            if (UIInputBlocker.IsBlocked) return;
            if (InventoryManager.Instance != null && InventoryManager.Instance.IsInventoryOpen) return;

            float distance = Vector3.Distance(transform.position, player.position);
            if (distance <= interactRange && Keyboard.current.eKey.wasPressedThisFrame)
            {
                dialogueUI.OpenDialogue();
                Debug.Log("[ShopInteractable] 상인과 대화 시작");
            }
        }
    }
}