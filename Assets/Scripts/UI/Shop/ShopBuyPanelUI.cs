using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FarmingSystem.Economy;
using FarmingSystem.Inventory;

namespace FarmingSystem.UI.Shop
{
    /// <summary>
    /// "살 때" 창. 탭(씨앗/비료/도구/기타)으로 구매 목록을 나눠 보여주고,
    /// 슬롯을 선택하면 하단 상세창에서 수량을 정해 구매한다.
    /// [취소]를 누르면 Closed 이벤트를 보내고, ShopDialogueUI가 대화 선택지로 되돌린다.
    /// </summary>
    public class ShopBuyPanelUI : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private TextMeshProUGUI merchantLineText;

        [Header("탭 / 목록")]
        [SerializeField] private List<ShopTabButtonUI> tabs = new List<ShopTabButtonUI>();
        [SerializeField] private Transform slotContainer;
        [SerializeField] private ShopBuySlotUI slotPrefab;

        [Header("상세 정보")]
        [SerializeField] private GameObject detailRoot;
        [SerializeField] private Image detailIcon;
        [SerializeField] private TextMeshProUGUI detailNameText;
        [SerializeField] private TextMeshProUGUI detailDescriptionText;
        [SerializeField] private TextMeshProUGUI totalPriceText;
        [SerializeField] private TMP_InputField quantityInput;
        [SerializeField] private Button minusButton;
        [SerializeField] private Button plusButton;
        [SerializeField] private Button buyButton;
        [SerializeField] private Button cancelButton;

        [Header("대사")]
        [SerializeField] private string greetLine = "오늘은 어떤 씨앗이 필요해?";
        [SerializeField] private string successLine = "좋은 선택이야!";
        [SerializeField] private string notEnoughMoneyLine = "돈이 조금 부족한 것 같은데?";
        [SerializeField] private string noSpaceLine = "가방에 공간이 없는 것 같아.";
        [SerializeField] private string outOfStockLine = "미안, 그건 다 팔렸어.";
        [SerializeField] private string shopClosedLine = "오늘은 이만 가봐야 해.";

        /// <summary>플레이어가 [취소]로 창을 닫았을 때 발행</summary>
        public event Action Closed;

        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

        private readonly List<ShopBuySlotUI> spawnedSlots = new List<ShopBuySlotUI>();
        private ShopBuySlotUI selectedSlot;
        private int quantity = 1;
        private bool initialized = false;
        private bool isSubscribed = false;

        private void Init()
        {
            if (initialized) return;
            initialized = true;

            foreach (ShopTabButtonUI tab in tabs)
                if (tab != null) tab.Init(SelectTab);

            minusButton.onClick.AddListener(() => SetQuantity(quantity - ShopUIUtil.Step()));
            plusButton.onClick.AddListener(() => SetQuantity(quantity + ShopUIUtil.Step()));
            quantityInput.onEndEdit.AddListener(text => SetQuantity(int.TryParse(text, out int v) ? v : 1));
            buyButton.onClick.AddListener(OnClickBuy);
            cancelButton.onClick.AddListener(Cancel);
        }

        public void Open()
        {
            Init();
            if (panelRoot != null) panelRoot.SetActive(true);
            Subscribe();

            SetLine(greetLine);
            SelectTab(ShopTab.Seed);
            Debug.Log("[ShopBuyPanelUI] 구매 창 열림");
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
            if (isSubscribed) return;
            if (CurrencyManager.Instance != null) CurrencyManager.Instance.OnBellsChanged += HandleBellsChanged;
            if (InventoryManager.Instance != null) InventoryManager.Instance.OnInventoryChanged += RefreshQuantity;
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed) return;
            if (CurrencyManager.Instance != null) CurrencyManager.Instance.OnBellsChanged -= HandleBellsChanged;
            if (InventoryManager.Instance != null) InventoryManager.Instance.OnInventoryChanged -= RefreshQuantity;
            isSubscribed = false;
        }

        private void HandleBellsChanged(int bells) => RefreshQuantity();

        private void SelectTab(ShopTab tab)
        {
            foreach (ShopTabButtonUI t in tabs)
                if (t != null) t.SetActive(t.tab == tab);

            foreach (ShopBuySlotUI slot in spawnedSlots)
                Destroy(slot.gameObject);
            spawnedSlots.Clear();
            selectedSlot = null;

            if (ShopManager.Instance == null) return;

            foreach (int index in ShopManager.Instance.GetCatalogIndicesByTab(tab))
            {
                ShopBuySlotUI slot = Instantiate(slotPrefab, slotContainer);
                slot.SetEntry(index, ShopManager.Instance.GetEntry(index), SelectSlot);
                spawnedSlots.Add(slot);
            }

            if (spawnedSlots.Count > 0) SelectSlot(spawnedSlots[0]);
            else if (detailRoot != null) detailRoot.SetActive(false);
        }

        private void SelectSlot(ShopBuySlotUI slot)
        {
            if (selectedSlot != null) selectedSlot.SetSelected(false);
            selectedSlot = slot;
            slot.SetSelected(true);

            ItemData item = ShopManager.Instance.GetEntry(slot.CatalogIndex).item;
            if (detailRoot != null) detailRoot.SetActive(true);
            detailIcon.enabled = item.icon != null;
            detailIcon.sprite = item.icon;
            detailNameText.text = item.itemName;
            detailDescriptionText.text = item.description;

            SetQuantity(1);
        }

        private void SetQuantity(int value)
        {
            if (selectedSlot == null || ShopManager.Instance == null) return;

            ShopItemEntry entry = ShopManager.Instance.GetEntry(selectedSlot.CatalogIndex);

            // 살 수 없는 상태여도 최소 1로 둬서, [구매]를 누르면 상인이 이유를 말해주게 한다
            int max = Mathf.Max(1, ShopManager.Instance.GetMaxBuyable(selectedSlot.CatalogIndex));
            quantity = Mathf.Clamp(value, 1, max);

            quantityInput.SetTextWithoutNotify(quantity.ToString());
            totalPriceText.text = ShopUIUtil.Money((long)entry.price * quantity);
            minusButton.interactable = quantity > 1;
            plusButton.interactable = quantity < max;
        }

        private void RefreshQuantity() => SetQuantity(quantity);

        private void OnClickBuy()
        {
            if (selectedSlot == null || ShopManager.Instance == null) return;

            BuyResult result = ShopManager.Instance.TryBuy(selectedSlot.CatalogIndex, quantity);
            switch (result)
            {
                case BuyResult.Success: SetLine(successLine); break;
                case BuyResult.NotEnoughMoney: SetLine(notEnoughMoneyLine); break;
                case BuyResult.NoInventorySpace: SetLine(noSpaceLine); break;
                case BuyResult.OutOfStock: SetLine(outOfStockLine); break;
                case BuyResult.ShopClosed: SetLine(shopClosedLine); break;
            }

            selectedSlot.RefreshStock(ShopManager.Instance.GetEntry(selectedSlot.CatalogIndex));
            RefreshQuantity();
        }

        private void SetLine(string line)
        {
            if (merchantLineText != null) merchantLineText.text = line;
        }
    }
}