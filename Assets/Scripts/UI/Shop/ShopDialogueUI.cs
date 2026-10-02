using UnityEngine;

namespace FarmingSystem.UI.Shop
{
    /// <summary>
    /// 상인과 대화 시작 시 뜨는 선택지(구매/판매/아무것도 아니다) 창.
    /// 각 버튼의 OnClick()은 인스펙터에서 이 스크립트의 메서드에 직접 연결한다.
    /// </summary>
    public class ShopDialogueUI : MonoBehaviour
    {
        [SerializeField] private GameObject dialogueRoot;
        [SerializeField] private ShopBuyPanelUI buyPanel;
        [SerializeField] private ShopSellPanelUI sellPanel;

        public void OpenDialogue()
        {
            if (dialogueRoot != null) dialogueRoot.SetActive(true);
            Debug.Log("[상점] 대화 시작");
        }

        public void CloseDialogue()
        {
            if (dialogueRoot != null) dialogueRoot.SetActive(false);
        }

        /// <summary>"구매" 버튼 OnClick에 연결</summary>
        public void OnChooseBuy()
        {
            CloseDialogue();
            buyPanel?.Open();
            Debug.Log("[상점] 구매 선택");
        }

        /// <summary>"판매" 버튼 OnClick에 연결</summary>
        public void OnChooseSell()
        {
            CloseDialogue();
            sellPanel?.Open();
            Debug.Log("[상점] 판매 선택");
        }

        /// <summary>"아무것도 아니다" 버튼 OnClick에 연결 - 플레이 화면으로 복귀</summary>
        public void OnChooseNothing()
        {
            CloseDialogue();
            Debug.Log("[상점] 대화 종료 (아무것도 안 함)");
        }

        /// <summary>구매 창의 닫기 버튼 OnClick에 연결</summary>
        public void CloseBuyPanel()
        {
            buyPanel?.Close();
        }

        /// <summary>판매 창의 닫기 버튼 OnClick에 연결</summary>
        public void CloseSellPanel()
        {
            sellPanel?.Close();
        }
    }
}