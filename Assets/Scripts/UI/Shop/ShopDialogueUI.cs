using UnityEngine;
using UnityEngine.InputSystem;
using FarmingSystem.Core;
using FarmingSystem.Economy;

namespace FarmingSystem.UI.Shop
{
    /// <summary>
    /// 상인과의 거래 한 번(세션)을 관리한다.
    /// 대화 선택지(구매/판매/아무것도 아니다) -> 구매/판매 창 -> [취소]/Esc 시 선택지로 복귀.
    /// 세션이 열려 있는 동안 UIInputBlocker로 이동·농사·핫바·인벤토리 입력을 막는다.
    /// 선택지 버튼의 OnClick()은 인스펙터에서 OnChooseBuy / OnChooseSell / OnChooseNothing에 연결한다.
    /// </summary>
    public class ShopDialogueUI : MonoBehaviour
    {
        [SerializeField] private GameObject dialogueRoot;
        [SerializeField] private ShopBuyPanelUI buyPanel;
        [SerializeField] private ShopSellPanelUI sellPanel;

        public bool IsSessionOpen { get; private set; }

        private bool isSubscribedToShop = false;

        private void Awake()
        {
            if (buyPanel != null) buyPanel.Closed += ShowChoices;
            if (sellPanel != null) sellPanel.Closed += ShowChoices;
        }

        private void OnEnable() => TrySubscribeToShop();
        private void Start() => TrySubscribeToShop();

        private void OnDestroy()
        {
            if (buyPanel != null) buyPanel.Closed -= ShowChoices;
            if (sellPanel != null) sellPanel.Closed -= ShowChoices;
            if (ShopManager.Instance != null) ShopManager.Instance.OnShopAvailabilityChanged -= HandleShopAvailabilityChanged;
            UIInputBlocker.Unblock(this);
        }

        private void TrySubscribeToShop()
        {
            if (isSubscribedToShop || ShopManager.Instance == null) return;
            ShopManager.Instance.OnShopAvailabilityChanged += HandleShopAvailabilityChanged;
            isSubscribedToShop = true;
        }

        // 거래 도중 상인이 떠나면 세션 강제 종료
        private void HandleShopAvailabilityChanged(bool isOpen)
        {
            if (!isOpen && IsSessionOpen) EndSession();
        }

        private void Update()
        {
            if (!IsSessionOpen || Keyboard.current == null) return;
            if (!Keyboard.current.escapeKey.wasPressedThisFrame) return;

            if (buyPanel != null && buyPanel.IsOpen) buyPanel.Cancel();
            else if (sellPanel != null && sellPanel.IsOpen) sellPanel.Cancel();
            else EndSession();
        }

        /// <summary>ShopInteractable이 E키로 호출</summary>
        public void OpenDialogue()
        {
            if (IsSessionOpen) return;

            IsSessionOpen = true;
            UIInputBlocker.Block(this);
            ShowChoices();
            Debug.Log("[상점] 대화 시작");
        }

        public void CloseDialogue()
        {
            if (dialogueRoot != null) dialogueRoot.SetActive(false);
        }

        private void ShowChoices()
        {
            if (dialogueRoot != null) dialogueRoot.SetActive(true);
        }

        /// <summary>"구매" 버튼 OnClick에 연결</summary>
        public void OnChooseBuy()
        {
            CloseDialogue();
            if (buyPanel != null) buyPanel.Open();
            Debug.Log("[상점] 구매 선택");
        }

        /// <summary>"판매" 버튼 OnClick에 연결</summary>
        public void OnChooseSell()
        {
            CloseDialogue();
            if (sellPanel != null) sellPanel.Open();
            Debug.Log("[상점] 판매 선택");
        }

        /// <summary>"아무것도 아니다" 버튼 OnClick에 연결 - 플레이 화면으로 복귀</summary>
        public void OnChooseNothing()
        {
            EndSession();
            Debug.Log("[상점] 대화 종료 (아무것도 안 함)");
        }

        private void EndSession()
        {
            CloseDialogue();
            if (buyPanel != null) buyPanel.Close();
            if (sellPanel != null) sellPanel.Close();

            IsSessionOpen = false;
            UIInputBlocker.Unblock(this);
        }
    }
}