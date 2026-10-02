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
            bellsText.text = $"{CurrencyManager.Instance.CurrentBells}벨";
        }
    }
}