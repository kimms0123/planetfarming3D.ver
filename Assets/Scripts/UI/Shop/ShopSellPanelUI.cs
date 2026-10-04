using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FarmingSystem.Inventory;
using FarmingSystem.Economy;
using FarmingSystem.Farming;

namespace FarmingSystem.UI.Shop
{
    /// <summary>
    /// "팔 때" 창. 인벤토리 30칸을 보여주고, 클릭한 작물을 "선택한 물품" 목록에 담아
    /// [바로 판매]로 한 번에 판다. 오늘의 특별 상품도 함께 표시한다.
    /// 흥정하기 버튼은 자리만 있고 아직 비활성.
    /// </summary>
    public class ShopSellPanelUI : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private TextMeshProUGUI merchantLineText;

        [Header("내 인벤토리")]
        [SerializeField] private Transform slotContainer;
        [SerializeField] private ShopSellSlotUI slotPrefab;

        [Header("선택한 물품")]
        [SerializeField] private Transform cartContainer;
        [SerializeField] private SellCartRowUI cartRowPrefab;
        [SerializeField] private TextMeshProUGUI expectedTotalText;
        [SerializeField] private Button clearAllButton;
        [SerializeField] private Button sellNowButton;
        [SerializeField] private Button bargainButton;
        [SerializeField] private Button cancelButton;

        [Header("오늘의 특별 상품 표시 (선택 사항)")]
        [SerializeField] private Image specialItemIcon;
        [SerializeField] private TextMeshProUGUI specialItemText;

        [Header("대사")]
        [SerializeField] private string greetLine = "어떤 작물을 팔고 싶어?";
        [Tooltip("{0} 자리에 받은 금액이 들어간다")]
        [SerializeField] private string soldLine = "고마워! {0}에 사 갈게.";
        [SerializeField] private string failedLine = "음... 지금은 거래하기 어렵겠는걸.";

        /// <summary>플레이어가 [취소]로 창을 닫았을 때 발행</summary>
        public event Action Closed;

        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

        /// <summary>흥정 화면을 붙일 때 넘겨받을 선택 목록 (슬롯 인덱스 -> 수량)</summary>
        public IReadOnlyDictionary<int, int> Cart => cart;

        private readonly Dictionary<int, int> cart = new Dictionary<int, int>();
        private readonly List<int> cartOrder = new List<int>(); // 담은 순서대로 표시
        private readonly List<SellCartRowUI> spawnedRows = new List<SellCartRowUI>();
        private ShopSellSlotUI[] slotUIs;
        private bool initialized = false;
        private bool isSubscribed = false;

        private void Init()
        {
            if (initialized) return;
            if (InventoryManager.Instance == null) return;
            initialized = true;

            slotUIs = new ShopSellSlotUI[InventoryManager.Instance.TotalSlotCount];
            for (int i = 0; i < slotUIs.Length; i++)
                slotUIs[i] = Instantiate(slotPrefab, slotContainer);

            clearAllButton.onClick.AddListener(ClearCart);
            sellNowButton.onClick.AddListener(OnClickSellNow);
            cancelButton.onClick.AddListener(Cancel);
            bargainButton.interactable = false; // TODO: 흥정 화면 붙일 때 활성화
        }

        public void Open()
        {
            Init();
            if (panelRoot != null) panelRoot.SetActive(true);
            Subscribe();

            SetLine(greetLine);
            RefreshSpecialItem();
            ClearCart();
            Debug.Log("[ShopSellPanelUI] 판매 창 열림");
        }

        /// <summary>창만 닫는다 (대화창이 거래를 끝낼 때 호출). Closed 이벤트는 보내지 않음.</summary>
        public void Close()
        {
            Unsubscribe();
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        /// <summary>플레이어의 취소 ([취소] 버튼, Esc). 창을 닫고 대화 선택지로 돌아간다.</summary>
        public void Cancel()
        {
            Close();
            Closed?.Invoke();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (isSubscribed || InventoryManager.Instance == null) return;
            InventoryManager.Instance.OnInventoryChanged += HandleInventoryChanged;
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed) return;
            if (InventoryManager.Instance != null)
                InventoryManager.Instance.OnInventoryChanged -= HandleInventoryChanged;
            isSubscribed = false;
        }

        // ---------- 선택 목록 ----------

        private void AddToCart(int slotIndex, int amount)
        {
            InventorySlotData slot = InventoryManager.Instance.GetSlot(slotIndex);
            if (slot == null || slot.IsEmpty) return;

            cart.TryGetValue(slotIndex, out int current);
            int max = slot.quantity;
            int next = amount >= max - current ? max : current + amount; // int.MaxValue가 와도 오버플로 없음
            SetCartQuantity(slotIndex, next);
        }

        private void SetCartQuantity(int slotIndex, int quantity)
        {
            InventorySlotData slot = InventoryManager.Instance.GetSlot(slotIndex);
            int max = slot != null ? slot.quantity : 0;
            quantity = Mathf.Clamp(quantity, 0, max);

            if (quantity == 0)
            {
                cart.Remove(slotIndex);
                cartOrder.Remove(slotIndex);
            }
            else
            {
                if (!cart.ContainsKey(slotIndex)) cartOrder.Add(slotIndex);
                cart[slotIndex] = quantity;
            }
            Refresh();
        }

        private void ClearCart()
        {
            cart.Clear();
            cartOrder.Clear();
            Refresh();
        }

        // ---------- 화면 갱신 ----------

        private void Refresh()
        {
            if (!initialized || ShopManager.Instance == null) return;

            InventoryManager inventory = InventoryManager.Instance;
            ShopManager shop = ShopManager.Instance;

            for (int i = 0; i < slotUIs.Length; i++)
            {
                InventorySlotData slot = inventory.GetSlot(i);
                cart.TryGetValue(i, out int inCart);
                bool sellable = ShopManager.IsSellable(slot);
                slotUIs[i].SetSlot(i, slot, sellable, shop.GetSellUnitPrice(slot),
                                   sellable && shop.IsTodaysSpecial(slot.item), inCart, AddToCart);
            }

            foreach (SellCartRowUI row in spawnedRows)
                Destroy(row.gameObject);
            spawnedRows.Clear();

            long total = 0;
            foreach (int index in cartOrder)
            {
                int slotIndex = index;
                InventorySlotData slot = inventory.GetSlot(slotIndex);
                int quantity = cart[slotIndex];
                int lineTotal = shop.GetSellLineTotal(slotIndex, quantity);

                SellCartRowUI row = Instantiate(cartRowPrefab, cartContainer);
                row.Setup(slot, quantity, shop.GetSellUnitPrice(slot), lineTotal,
                          q => SetCartQuantity(slotIndex, q),
                          () => SetCartQuantity(slotIndex, 0));
                spawnedRows.Add(row);
                total += lineTotal;
            }

            expectedTotalText.text = ShopUIUtil.Money(total);
            sellNowButton.interactable = cart.Count > 0;
            clearAllButton.interactable = cart.Count > 0;
        }

        private void OnClickSellNow()
        {
            if (cart.Count == 0 || ShopManager.Instance == null) return;

            // 복사본을 넘김 - 판매 도중 OnInventoryChanged가 cart를 정리해도 안전
            int earned = ShopManager.Instance.TrySellBatch(new Dictionary<int, int>(cart));
            SetLine(earned >= 0 ? string.Format(soldLine, ShopUIUtil.Money(earned)) : failedLine);
            ClearCart();
        }

        /// <summary>인벤토리가 바뀌면 선택 목록을 실제 보유량에 맞춰 정리</summary>
        private void HandleInventoryChanged()
        {
            foreach (int slotIndex in cartOrder.ToArray())
            {
                InventorySlotData slot = InventoryManager.Instance.GetSlot(slotIndex);
                if (!ShopManager.IsSellable(slot))
                {
                    cart.Remove(slotIndex);
                    cartOrder.Remove(slotIndex);
                }
                else if (cart[slotIndex] > slot.quantity)
                {
                    cart[slotIndex] = slot.quantity;
                }
            }
            Refresh();
        }

        private void RefreshSpecialItem()
        {
            CropData special = ShopManager.Instance != null ? ShopManager.Instance.TodaysSpecialCrop : null;

            if (specialItemIcon != null)
            {
                specialItemIcon.enabled = special != null && special.icon != null;
                if (special != null) specialItemIcon.sprite = special.icon;
            }
            if (specialItemText != null)
            {
                specialItemText.text = special != null
                    ? $"오늘의 특별 상품: {special.cropName} (판매가 x{ShopManager.Instance.SpecialSellBonusMultiplier})"
                    : "오늘은 특별 상품이 없습니다";
            }
        }

        private void SetLine(string line)
        {
            if (merchantLineText != null) merchantLineText.text = line;
        }
    }
}