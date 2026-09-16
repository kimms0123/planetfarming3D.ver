using System;
using System.Collections.Generic;
using UnityEngine;
using FarmingSystem.Core;
using FarmingSystem.Inventory;
using FarmingSystem.Farming;

namespace FarmingSystem.Economy
{
    [Serializable]
    public class ShopItemEntry
    {
        public ItemData item;
        public int price;
        [Tooltip("-1이면 무제한 재고")]
        public int stock = -1;
    }

    /// <summary>
    /// 3~4일 랜덤 간격으로 상인(우주선)이 방문하는 상점 시스템.
    /// GameClock의 날짜 변화를 구독해서 방문 여부를 판정하고,
    /// 방문 중일 때만 구매/판매가 가능하다.
    /// </summary>
    public class ShopManager : MonoBehaviour
    {
        public static ShopManager Instance { get; private set; }

        [Header("방문 주기")]
        [Tooltip("다음 방문까지 최소/최대 대기일 (예: 3~4일)")]
        [SerializeField] private int minVisitInterval = 3;
        [SerializeField] private int maxVisitInterval = 4;
        [Tooltip("한 번 방문하면 며칠간 머무는지")]
        [SerializeField] private int visitDurationDays = 1;

        [Header("구매 목록 (씨앗 등, 나중에 도구/기타 추가 가능)")]
        [SerializeField] private List<ShopItemEntry> buyCatalog = new List<ShopItemEntry>();

        private int nextVisitDay;
        private int visitEndsOnDay = -1;
        private bool isShopOpen = false;

        public bool IsShopOpen => isShopOpen;
        public IReadOnlyList<ShopItemEntry> BuyCatalog => buyCatalog;

        /// <summary>상점이 열리거나 닫힐 때 발행 -> 상인/우주선 등장 연출, 상점 UI가 구독</summary>
        public event Action<bool> OnShopAvailabilityChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnEnable()
        {
            TrySubscribe();
        }

        private void Start()
        {
            TrySubscribe();

            if (GameClock.Instance != null)
                ScheduleNextVisit(GameClock.Instance.CurrentDay);
        }

        private void OnDisable()
        {
            if (GameClock.Instance != null)
                GameClock.Instance.OnDayChanged -= HandleDayChanged;
        }

        private bool isSubscribed = false;

        private void TrySubscribe()
        {
            if (isSubscribed) return;
            if (GameClock.Instance == null) return;

            GameClock.Instance.OnDayChanged += HandleDayChanged;
            isSubscribed = true;
            Debug.Log("[ShopManager] GameClock 이벤트 구독 완료");
        }

        private void HandleDayChanged(int day, Season season)
        {
            if (!isShopOpen && day >= nextVisitDay)
            {
                OpenShop(day);
            }
            else if (isShopOpen && day > visitEndsOnDay)
            {
                CloseShop(day);
            }
        }

        private void OpenShop(int day)
        {
            isShopOpen = true;
            visitEndsOnDay = day + visitDurationDays - 1;
            Debug.Log($"[상점] Day {day} - 상인이 도착했습니다! (Day {visitEndsOnDay}까지 머무름)");
            OnShopAvailabilityChanged?.Invoke(true);
        }

        private void CloseShop(int day)
        {
            isShopOpen = false;
            Debug.Log($"[상점] Day {day} - 상인이 떠났습니다.");
            OnShopAvailabilityChanged?.Invoke(false);
            ScheduleNextVisit(day);
        }

        private void ScheduleNextVisit(int fromDay)
        {
            int interval = UnityEngine.Random.Range(minVisitInterval, maxVisitInterval + 1);
            nextVisitDay = fromDay + interval;
            Debug.Log($"[상점] 다음 방문 예정일: Day {nextVisitDay} (지금으로부터 {interval}일 후)");
        }

        // ---------- 구매 ----------

        /// <summary>구매 목록의 index번째 아이템을 quantity개 구매 시도.</summary>
        public bool TryBuy(int catalogIndex, int quantity)
        {
            if (!isShopOpen)
            {
                Debug.Log("[구매 실패] 지금은 상인이 없음");
                return false;
            }
            if (catalogIndex < 0 || catalogIndex >= buyCatalog.Count)
            {
                Debug.Log("[구매 실패] 잘못된 상품 인덱스");
                return false;
            }

            ShopItemEntry entry = buyCatalog[catalogIndex];
            if (entry.stock == 0)
            {
                Debug.Log($"[구매 실패] {entry.item.itemName} 재고 없음");
                return false;
            }

            int buyQuantity = entry.stock < 0 ? quantity : Mathf.Min(quantity, entry.stock);
            int totalPrice = entry.price * buyQuantity;

            if (CurrencyManager.Instance == null || !CurrencyManager.Instance.TrySpendBells(totalPrice))
            {
                Debug.Log($"[구매 실패] {entry.item.itemName} x{buyQuantity} ({totalPrice}벨) - 재화 부족");
                return false;
            }

            int notAdded = InventoryManager.Instance != null ? InventoryManager.Instance.AddItem(entry.item, buyQuantity) : buyQuantity;
            if (entry.stock > 0)
                entry.stock -= (buyQuantity - notAdded);

            Debug.Log($"[구매 성공] {entry.item.itemName} x{buyQuantity - notAdded} ({totalPrice}벨 지불)");
            return true;
        }

        // ---------- 판매 ----------

        /// <summary>
        /// 인벤토리 slotIndex에 있는 작물을 quantity개 판매 시도.
        /// 가격 = CropData.basePrice x 등급 배율 x 수량.
        /// </summary>
        public bool TrySell(int slotIndex, int quantity)
        {
            if (!isShopOpen)
            {
                Debug.Log("[판매 실패] 지금은 상인이 없음");
                return false;
            }
            if (InventoryManager.Instance == null) return false;

            InventorySlotData slot = InventoryManager.Instance.GetSlot(slotIndex);
            if (slot == null || slot.IsEmpty)
            {
                Debug.Log("[판매 실패] 빈 슬롯");
                return false;
            }

            CropData cropData = slot.item as CropData;
            if (cropData == null)
            {
                Debug.Log($"[판매 실패] {slot.item.itemName}은 판매 가능한 작물이 아님");
                return false;
            }

            int sellQuantity = Mathf.Min(quantity, slot.quantity);
            float multiplier = ItemQualityUtility.GetPriceMultiplier(slot.quality);
            int totalPrice = Mathf.RoundToInt(cropData.basePrice * multiplier * sellQuantity);

            int removed = InventoryManager.Instance.RemoveFromSlot(slotIndex, sellQuantity);
            CurrencyManager.Instance?.AddBells(totalPrice);

            Debug.Log($"[판매 성공] {cropData.cropName} x{removed} ({slot.quality}, 배율 {multiplier}) -> {totalPrice}벨 획득");
            return true;
        }
    }
}