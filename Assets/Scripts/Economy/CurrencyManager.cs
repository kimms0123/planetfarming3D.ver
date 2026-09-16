using System;
using UnityEngine;

namespace FarmingSystem.Economy
{
    /// <summary>
    /// 게임 내 화폐(벨) 보유량을 관리하는 싱글톤.
    /// </summary>
    public class CurrencyManager : MonoBehaviour
    {
        public static CurrencyManager Instance { get; private set; }

        [Header("재화")]
        [SerializeField] private int currentBells = 500;

        public int CurrentBells => currentBells;

        /// <summary>보유액이 바뀔 때 (새 금액) 발행 -> UI가 구독</summary>
        public event Action<int> OnBellsChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            Debug.Log($"[CurrencyManager] 초기화 완료. 시작 보유액: {currentBells}벨");
        }

        public void AddBells(int amount)
        {
            if (amount <= 0) return;

            currentBells += amount;
            Debug.Log($"[재화 획득] +{amount}벨, 현재 보유액: {currentBells}벨");
            OnBellsChanged?.Invoke(currentBells);
        }

        /// <summary>지불 시도. 부족하면 false를 반환하고 아무 것도 차감하지 않는다.</summary>
        public bool TrySpendBells(int amount)
        {
            if (amount <= 0) return true;
            if (currentBells < amount)
            {
                Debug.Log($"[재화 부족] {amount}벨 필요, 현재 보유액: {currentBells}벨");
                return false;
            }

            currentBells -= amount;
            Debug.Log($"[재화 사용] -{amount}벨, 현재 보유액: {currentBells}벨");
            OnBellsChanged?.Invoke(currentBells);
            return true;
        }
    }
}