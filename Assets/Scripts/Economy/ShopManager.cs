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

    /// <summary>상점 구매 탭. 아이템의 ItemCategory로 자동 분류된다.</summary>
    public enum ShopTab { Seed, Fertilizer, Tool, Etc }

    public enum BuyResult { Success, ShopClosed, InvalidItem, OutOfStock, NotEnoughMoney, NoInventorySpace }

    /// <summary>
    /// 3~4일 랜덤 간격으로 상인(우주선)이 방문하는 상점 시스템.
    /// GameClock의 날짜 변화를 구독해서 방문 여부를 판정하고,
    /// 방문 중일 때만 구매/판매가 가능하다.
    /// 방문할 때마다 "오늘의 특별 상품"(판매 보너스가 붙는 작물)을 하나 무작위로 정한다.
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

        [Header("구매 목록 (탭은 아이템의 Category로 자동 분류)")]
        [SerializeField] private List<ShopItemEntry> buyCatalog = new List<ShopItemEntry>();

        [Header("오늘의 특별 상품 (판매 보너스)")]
        [Tooltip("방문할 때마다 이 목록 중 하나를 무작위로 골라 판매 보너스를 준다. 비워두면 특별 상품 없음")]
        [SerializeField] private CropData[] possibleSpecialCrops;
        [SerializeField] private float specialSellBonusMultiplier = 1.5f;

        private int nextVisitDay;
        private int visitEndsOnDay = -1;
        private bool isShopOpen = false;
        private CropData todaysSpecialCrop;

        public bool IsShopOpen => isShopOpen;
        public IReadOnlyList<ShopItemEntry> BuyCatalog => buyCatalog;
        public CropData TodaysSpecialCrop => todaysSpecialCrop;
        public float SpecialSellBonusMultiplier => specialSellBonusMultiplier;

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
            isSubscribed = false;
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
            PickTodaysSpecialCrop();
            Debug.Log($"[상점] Day {day} - 상인이 도착했습니다! (Day {visitEndsOnDay}까지 머무름)");
            OnShopAvailabilityChanged?.Invoke(true);
        }

        private void CloseShop(int day)
        {
            isShopOpen = false;
            todaysSpecialCrop = null;
            Debug.Log($"[상점] Day {day} - 상인이 떠났습니다.");
            OnShopAvailabilityChanged?.Invoke(false);
            ScheduleNextVisit(day);
        }

        private void PickTodaysSpecialCrop()
        {
            if (possibleSpecialCrops == null || possibleSpecialCrops.Length == 0)
            {
                todaysSpecialCrop = null;
                return;
            }

            todaysSpecialCrop = possibleSpecialCrops[UnityEngine.Random.Range(0, possibleSpecialCrops.Length)];
            Debug.Log($"[상점] 오늘의 특별 상품: {todaysSpecialCrop.cropName} (판매가 x{specialSellBonusMultiplier})");
        }

        private void ScheduleNextVisit(int fromDay)
        {
            int interval = UnityEngine.Random.Range(minVisitInterval, maxVisitInterval + 1);
            nextVisitDay = fromDay + interval;
            Debug.Log($"[상점] 다음 방문 예정일: Day {nextVisitDay} (지금으로부터 {interval}일 후)");
        }

        // ---------- 구매 ----------

        public static ShopTab GetTab(ItemData item)
        {
            if (item == null) return ShopTab.Etc;

            switch (item.category)
            {
                case ItemCategory.Seed: return ShopTab.Seed;
                case ItemCategory.Fertilizer: return ShopTab.Fertilizer;
                case ItemCategory.Tool: return ShopTab.Tool;
                default: return ShopTab.Etc;
            }
        }

        /// <summary>해당 탭에 속하는 구매 목록 인덱스들 (인스펙터 목록 순서 유지)</summary>
        public List<int> GetCatalogIndicesByTab(ShopTab tab)
        {
            var result = new List<int>();
            for (int i = 0; i < buyCatalog.Count; i++)
            {
                ShopItemEntry entry = buyCatalog[i];
                if (entry != null && entry.item != null && GetTab(entry.item) == tab)
                    result.Add(i);
            }
            return result;
        }

        public ShopItemEntry GetEntry(int catalogIndex)
        {
            if (catalogIndex < 0 || catalogIndex >= buyCatalog.Count) return null;
            return buyCatalog[catalogIndex];
        }

        /// <summary>지금 살 수 있는 최대 수량 = 소지금, 인벤토리 공간, 재고 중 가장 작은 값</summary>
        public int GetMaxBuyable(int catalogIndex)
        {
            ShopItemEntry entry = GetEntry(catalogIndex);
            if (entry == null || entry.item == null) return 0;

            int byMoney = entry.price <= 0
                ? int.MaxValue
                : (CurrencyManager.Instance != null ? CurrencyManager.Instance.CurrentBells / entry.price : 0);
            int bySpace = InventoryManager.Instance != null ? InventoryManager.Instance.GetAddableAmount(entry.item) : 0;
            int byStock = entry.stock < 0 ? int.MaxValue : entry.stock;

            return Mathf.Min(byMoney, Mathf.Min(bySpace, byStock));
        }

        /// <summary>구매 목록의 catalogIndex번째 아이템을 quantity개 구매 시도.</summary>
        public BuyResult TryBuy(int catalogIndex, int quantity)
        {
            if (!isShopOpen)
            {
                Debug.Log("[구매 실패] 지금은 상인이 없음");
                return BuyResult.ShopClosed;
            }

            ShopItemEntry entry = GetEntry(catalogIndex);
            if (entry == null || entry.item == null || quantity <= 0) return BuyResult.InvalidItem;
            if (CurrencyManager.Instance == null || InventoryManager.Instance == null) return BuyResult.InvalidItem;

            if (entry.stock >= 0 && entry.stock < quantity)
            {
                Debug.Log($"[구매 실패] {entry.item.itemName} 재고 부족 (남은 재고 {entry.stock})");
                return BuyResult.OutOfStock;
            }

            long totalPrice = (long)entry.price * quantity;
            if (totalPrice > CurrencyManager.Instance.CurrentBells)
            {
                Debug.Log($"[구매 실패] {entry.item.itemName} x{quantity} ({totalPrice}벨) - 재화 부족");
                return BuyResult.NotEnoughMoney;
            }

            // 공간을 결제보다 먼저 확인 - 돈만 빠지고 아이템이 덜 들어오는 상황 방지
            if (InventoryManager.Instance.GetAddableAmount(entry.item) < quantity)
            {
                Debug.Log($"[구매 실패] {entry.item.itemName} x{quantity} - 인벤토리 공간 부족");
                return BuyResult.NoInventorySpace;
            }

            if (!CurrencyManager.Instance.TrySpendBells((int)totalPrice)) return BuyResult.NotEnoughMoney;

            InventoryManager.Instance.AddItem(entry.item, quantity);
            if (entry.stock > 0) entry.stock -= quantity;

            Debug.Log($"[구매 성공] {entry.item.itemName} x{quantity} ({totalPrice}벨 지불)");
            return BuyResult.Success;
        }

        // ---------- 판매 ----------

        /// <summary>지금은 작물(CropData)만 판매 가능</summary>
        public static bool IsSellable(InventorySlotData slot)
        {
            return slot != null && !slot.IsEmpty && slot.item is CropData;
        }

        public bool IsTodaysSpecial(ItemData item)
        {
            return item != null && item == todaysSpecialCrop;
        }

        /// <summary>개당 판매가(소수 포함) = basePrice x 등급 배율 x (오늘의 특별 상품이면 보너스)</summary>
        public float GetSellUnitPrice(InventorySlotData slot)
        {
            if (!IsSellable(slot)) return 0f;

            CropData crop = (CropData)slot.item;
            float price = crop.basePrice * ItemQualityUtility.GetPriceMultiplier(slot.quality);
            if (todaysSpecialCrop == crop) price *= specialSellBonusMultiplier;
            return price;
        }

        /// <summary>
        /// 슬롯 하나에서 quantity개 팔 때 받는 금액.
        /// 단가 x 수량을 한 번에 반올림해서, basePrice가 소수여도 단가가 깎이지 않게 한다.
        /// </summary>
        public int GetSellLineTotal(int slotIndex, int quantity)
        {
            InventorySlotData slot = InventoryManager.Instance != null ? InventoryManager.Instance.GetSlot(slotIndex) : null;
            if (!IsSellable(slot) || quantity <= 0) return 0;

            int sellQuantity = Mathf.Min(quantity, slot.quantity);
            return Mathf.RoundToInt(GetSellUnitPrice(slot) * sellQuantity);
        }

        /// <summary>
        /// 여러 슬롯을 한 번에 판매. key = 인벤토리 슬롯 인덱스, value = 판매 수량.
        /// 하나라도 검증에 실패하면 아무것도 팔지 않고 -1을 반환한다. 성공하면 받은 벨.
        /// </summary>
        public int TrySellBatch(IReadOnlyDictionary<int, int> cart)
        {
            if (!isShopOpen)
            {
                Debug.Log("[판매 실패] 지금은 상인이 없음");
                return -1;
            }
            if (InventoryManager.Instance == null || cart == null || cart.Count == 0) return -1;

            InventoryManager inventory = InventoryManager.Instance;
            long total = 0;

            // 1) 전부 검증하고 금액 계산 (아직 아무것도 제거하지 않음)
            foreach (KeyValuePair<int, int> pair in cart)
            {
                InventorySlotData slot = inventory.GetSlot(pair.Key);
                if (pair.Value <= 0 || !IsSellable(slot) || slot.quantity < pair.Value)
                {
                    Debug.Log($"[판매 실패] 슬롯 {pair.Key + 1}번 검증 실패 - 판매 취소");
                    return -1;
                }
                total += GetSellLineTotal(pair.Key, pair.Value);
            }

            // 2) 실제 제거 + 지급
            foreach (KeyValuePair<int, int> pair in cart)
                inventory.RemoveFromSlot(pair.Key, pair.Value);

            CurrencyManager.Instance?.AddBells((int)total);
            Debug.Log($"[판매 성공] {cart.Count}개 슬롯 일괄 판매 -> {total}벨 획득");
            return (int)total;
        }
    }
}