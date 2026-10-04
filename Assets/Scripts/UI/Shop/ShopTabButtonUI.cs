using System;
using UnityEngine;
using UnityEngine.UI;
using FarmingSystem.Economy;

namespace FarmingSystem.UI.Shop
{
    /// <summary>구매 창의 탭 버튼 하나. Tab 값만 인스펙터에서 정해주면 된다.</summary>
    public class ShopTabButtonUI : MonoBehaviour
    {
        public ShopTab tab;
        [SerializeField] private Button button;
        [Tooltip("선택된 탭일 때만 켜지는 배경")]
        [SerializeField] private GameObject activeVisual;

        public void Init(Action<ShopTab> onClick)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => onClick?.Invoke(tab));
        }

        public void SetActive(bool on)
        {
            if (activeVisual != null) activeVisual.SetActive(on);
        }
    }
}