using UnityEngine;
using TMPro;
using FarmingSystem.Economy;

namespace FarmingSystem.UI.Shop
{
    /// <summary>
    /// "소지금" 텍스트 표시. 구매/판매 창 양쪽에 하나씩 붙이면 된다.
    /// </summary>
    public class CurrencyDisplayUI : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI bellsText;
        private bool isSubscribed = false;
        private int shownBells = int.MinValue; // 화면에 지금 보이는 금액

        private void OnEnable()
        {
            TrySubscribe();
            Refresh();
        }

        private void Start()
        {
            TrySubscribe();
            Refresh();
        }

        private void OnDisable()
        {
            if (CurrencyManager.Instance != null)
                CurrencyManager.Instance.OnBellsChanged -= HandleChanged;
            isSubscribed = false; // 창을 닫았다 다시 열면 다시 구독하도록 (안 그러면 두 번째부터 금액이 안 바뀜)
        }

        // 안전장치: 이벤트를 놓쳐도(저장 불러오기 등) 금액이 다르면 바로 고침
        private void LateUpdate()
        {
            if (!isSubscribed) TrySubscribe();
            if (CurrencyManager.Instance != null && CurrencyManager.Instance.CurrentBells != shownBells) Refresh();
        }

        private void TrySubscribe()
        {
            if (isSubscribed) return;
            if (CurrencyManager.Instance == null) return;

            CurrencyManager.Instance.OnBellsChanged += HandleChanged;
            isSubscribed = true;
        }

        private void HandleChanged(int amount) => Refresh();

        private void Refresh()
        {
            if (bellsText == null || CurrencyManager.Instance == null) return;
            shownBells = CurrencyManager.Instance.CurrentBells;
            bellsText.text = $"{shownBells}벨";
        }
    }
}