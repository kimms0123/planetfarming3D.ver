using System;
using System.Collections;
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
    ///
    /// 흥정 (창 아래 "예상 판매 금액" 칸에서 바로 진행 - 화면 전환 없음)
    /// 1. [흥정하기] → 예상 판매 금액 자리가 금액 입력 칸으로 바뀜 (버튼: [제안하기] / [흥정 취소])
    /// 2. 원하는 금액 입력 → [제안하기] 또는 Enter
    /// 3. 상인이 고민 (제안 금액이 바들바들 떨림)
    /// 4. 수락: 금액이 제안한 값까지 쭉 바뀜 → [바로 판매] 때 그 금액을 받음
    ///    거절: 원래 금액 그대로 (담은 물품을 바꾸기 전까지 다시 흥정 불가)
    /// 수락 확률은 흥정 모델(BargainModel)이 정함 - 연출은 그대로 두고 판단만 바꿔 끼우는 구조
    ///  - Bargain Model 칸이 비어 있으면: 기본 규칙 (Safe Ratio 110%까지 항상 수락 → Max Ratio 150%로 갈수록 줄어듦 → 그 이상 거절)
    ///  - 나중에 FCM/RBFN 모델(BargainModel 상속 에셋)을 만들어 그 칸에 넣으면 끝 (이 파일은 안 고쳐도 됨)
    ///  - 모델 입력 = BargainContext (제안 배율, 거래 규모, 품질, 특별 상품, 제철, 시도 횟수, 누적 성공률, 날씨)
    ///  - Log Bargains를 켜면 흥정마다 특징값 + 결과가 CSV로 쌓임 → 학습 데이터
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

        [Header("흥정")]
        [Tooltip("끄면 흥정하기 버튼이 비활성")]
        [SerializeField] private bool bargainEnabled = true;
        [Tooltip("흥정 판단 모델 (BargainModel 에셋). 비우면 아래 Safe/Max Ratio 기본 규칙\n나중에 FCM/RBFN 모델 에셋을 여기에 넣으면 됨")]
        [SerializeField] private BargainModel bargainModel;
        [Tooltip("흥정할 때마다 특징값과 결과를 CSV로 저장 (FCM/RBFN 학습 데이터용)")]
        [SerializeField] private bool logBargains = false;
        [Tooltip("원래 금액의 이 배율까지는 항상 수락 (1.1 = 10% 더까지) - Bargain Model이 비어 있을 때만 씀")]
        [SerializeField, Range(1f, 2f)] private float safeRatio = 1.1f;
        [Tooltip("원래 금액의 이 배율 이상은 항상 거절 (1.5 = 50% 더) - Bargain Model이 비어 있을 때만 씀")]
        [SerializeField, Range(1.01f, 3f)] private float maxRatio = 1.5f;
        [Tooltip("PV 촬영용: 켜면 얼마를 불러도 항상 수락")]
        [SerializeField] private bool alwaysAccept = false;
        [Tooltip("상인이 고민하는 시간 (초)")]
        [SerializeField] private float bargainThinkTime = 1.4f;
        [Tooltip("금액이 바뀌는 시간 (초)")]
        [SerializeField] private float bargainCountTime = 0.9f;
        [SerializeField] private string bargainAskLine = "얼마를 원하는데?";
        [SerializeField] private string bargainThinkLine = "흐음";
        [Tooltip("{0} 자리에 수락한 금액이 들어간다")]
        [SerializeField] private string bargainAcceptLine = "좋아, {0}에 해 줄게!";
        [SerializeField] private string bargainCheapLine = "오히려 고맙지! 그 가격에 사 갈게.";
        [SerializeField] private string bargainRejectLine = "에이, 그 가격은 안 되지~ 원래 가격으로 하자.";
        [SerializeField] private string bargainInvalidLine = "금액을 숫자로 말해 줘.";
        [SerializeField] private Color bargainColor = new Color(1f, 0.72f, 0.15f, 1f);
        [Header("버튼 글자")]
        [SerializeField] private string bargainLabel = "흥정하기";
        [SerializeField] private string offerLabel = "제안하기";
        [SerializeField] private string cancelBargainLabel = "흥정 취소";

        /// <summary>플레이어가 [취소]로 창을 닫았을 때 발행</summary>
        public event Action Closed;

        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

        /// <summary>선택 목록 (슬롯 인덱스 -> 수량)</summary>
        public IReadOnlyDictionary<int, int> Cart => cart;

        private readonly Dictionary<int, int> cart = new Dictionary<int, int>();
        private readonly List<int> cartOrder = new List<int>(); // 담은 순서대로 표시
        private readonly List<SellCartRowUI> spawnedRows = new List<SellCartRowUI>();
        private ShopSellSlotUI[] slotUIs;
        private bool initialized = false;
        private bool isSubscribed = false;

        // 흥정 상태 (담은 물품이 바뀌면 처음부터)
        private enum BargainState { None, Typing, Thinking, Done }
        private BargainState bargainState = BargainState.None;
        private long dealTotal = -1;     // 수락된 금액 (-1 = 흥정 안 함/거절)
        private long lastBaseTotal;
        private int bargainAttempts;     // 이번에 창을 열고 몇 번 흥정했는지 (모델 입력)
        private Coroutine bargainRoutine;
        private TMP_InputField bargainInput;
        private TextMeshProUGUI bargainButtonText, clearAllButtonText;
        private string clearAllOriginalLabel;
        private Color totalBaseColor = Color.white;
        private bool totalColorSaved;

        private bool Busy => bargainState == BargainState.Typing || bargainState == BargainState.Thinking;

        private void Init()
        {
            if (initialized) return;
            if (InventoryManager.Instance == null) return;
            initialized = true;

            slotUIs = new ShopSellSlotUI[InventoryManager.Instance.TotalSlotCount];
            for (int i = 0; i < slotUIs.Length; i++)
                slotUIs[i] = Instantiate(slotPrefab, slotContainer);

            clearAllButton.onClick.AddListener(OnClickClearAll);
            sellNowButton.onClick.AddListener(OnClickSellNow);
            cancelButton.onClick.AddListener(Cancel);
            bargainButton.onClick.AddListener(OnClickBargain);

            bargainButtonText = bargainButton.GetComponentInChildren<TextMeshProUGUI>(true);
            clearAllButtonText = clearAllButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (clearAllButtonText != null) clearAllOriginalLabel = clearAllButtonText.text;
            SetBargainLabels();
        }

        public void Open()
        {
            Init();
            bargainAttempts = 0;
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
            ExitBargain();
            Unsubscribe();
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        /// <summary>플레이어의 취소 ([취소] 버튼, Esc). 흥정 입력 중이면 흥정만 취소, 아니면 창을 닫고 대화 선택지로</summary>
        public void Cancel()
        {
            if (bargainState == BargainState.Typing)
            {
                ExitBargain();
                SetLine(greetLine);
                Refresh();
                return;
            }
            if (bargainState == BargainState.Thinking) return; // 상인이 고민하는 중엔 기다림
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
            if (Busy) return; // 흥정 중엔 목록 고정
            InventorySlotData slot = InventoryManager.Instance.GetSlot(slotIndex);
            if (slot == null || slot.IsEmpty) return;

            cart.TryGetValue(slotIndex, out int current);
            int max = slot.quantity;
            int next = amount >= max - current ? max : current + amount; // int.MaxValue가 와도 오버플로 없음
            SetCartQuantity(slotIndex, next);
        }

        private void SetCartQuantity(int slotIndex, int quantity)
        {
            if (Busy) return;
            ResetBargain(); // 물품이 바뀌면 흥정 다시

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

        // [전부 빼기] 버튼: 흥정 입력 중엔 [흥정 취소]로 동작
        private void OnClickClearAll()
        {
            if (bargainState == BargainState.Typing) { Cancel(); return; }
            ClearCart();
        }

        private void ClearCart()
        {
            ExitBargain();
            ResetBargain();
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
            lastBaseTotal = total;

            if (bargainState != BargainState.Thinking) ShowTotal();
            sellNowButton.interactable = cart.Count > 0 && !Busy;
            clearAllButton.interactable = (cart.Count > 0 && bargainState != BargainState.Thinking) || bargainState == BargainState.Typing;
            bargainButton.interactable = bargainEnabled && cart.Count > 0 &&
                                         (bargainState == BargainState.None || bargainState == BargainState.Typing);
            SetBargainLabels();
        }

        private void OnClickSellNow()
        {
            if (cart.Count == 0 || ShopManager.Instance == null || Busy) return;

            // 복사본을 넘김 - 판매 도중 OnInventoryChanged가 cart를 정리해도 안전
            int earned = ShopManager.Instance.TrySellBatch(new Dictionary<int, int>(cart));
            // 흥정이 수락됐으면 받은 금액을 흥정 금액에 맞춤 (차액만큼 더 주거나 덜 줌)
            if (earned > 0 && dealTotal >= 0 && CurrencyManager.Instance != null)
            {
                long diff = dealTotal - earned;
                if (diff > 0) CurrencyManager.Instance.AddBells((int)diff);
                else if (diff < 0) CurrencyManager.Instance.TrySpendBells((int)(-diff));
                earned = (int)dealTotal;
            }
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

        // ---------- 흥정 ----------

        private void ShowTotal()
        {
            SaveTotalColor();
            if (bargainState == BargainState.Typing) return; // 입력 칸이 보이는 중
            if (dealTotal >= 0)
            {
                expectedTotalText.text = $"{ShopUIUtil.Money(dealTotal)} <size=60%>(흥정 성공)</size>";
                expectedTotalText.color = bargainColor;
            }
            else
            {
                expectedTotalText.text = ShopUIUtil.Money(lastBaseTotal);
                expectedTotalText.color = totalBaseColor;
            }
        }

        private void SaveTotalColor()
        {
            if (totalColorSaved || expectedTotalText == null) return;
            totalBaseColor = expectedTotalText.color;
            totalColorSaved = true;
        }

        private void SetBargainLabels()
        {
            if (bargainButtonText != null) bargainButtonText.text = bargainState == BargainState.Typing ? offerLabel : bargainLabel;
            if (clearAllButtonText != null && clearAllOriginalLabel != null)
                clearAllButtonText.text = bargainState == BargainState.Typing ? cancelBargainLabel : clearAllOriginalLabel;
        }

        private void ResetBargain()
        {
            dealTotal = -1;
            if (bargainState == BargainState.Done) bargainState = BargainState.None;
        }

        // 입력 칸 닫고 흥정 중이던 것 정리
        private void ExitBargain()
        {
            if (bargainRoutine != null) StopCoroutine(bargainRoutine);
            bargainRoutine = null;
            if (bargainInput != null) bargainInput.gameObject.SetActive(false);
            if (expectedTotalText != null)
            {
                expectedTotalText.gameObject.SetActive(true);
                expectedTotalText.rectTransform.localScale = Vector3.one;
            }
            if (Busy) bargainState = BargainState.None;
        }

        private void OnClickBargain()
        {
            if (!bargainEnabled || cart.Count == 0) return;
            if (bargainState == BargainState.None) OpenBargainInput();
            else if (bargainState == BargainState.Typing) SubmitOffer();
        }

        // 1. 예상 판매 금액 자리를 금액 입력 칸으로
        private void OpenBargainInput()
        {
            EnsureBargainInput();
            if (bargainInput == null) return;
            bargainState = BargainState.Typing;
            expectedTotalText.gameObject.SetActive(false);
            bargainInput.gameObject.SetActive(true);
            bargainInput.text = "";
            ((TextMeshProUGUI)bargainInput.placeholder).text = $"{lastBaseTotal:N0}"; // 원래 금액이 흐리게 보이고, 그 위에 새 금액을 입력
            bargainInput.ActivateInputField();
            bargainInput.Select();
            SetLine(bargainAskLine);
            Refresh();
        }

        // 2. 제안 → 상인 고민 → 수락/거절
        private void SubmitOffer()
        {
            if (bargainState != BargainState.Typing || bargainInput == null) return;
            string digits = bargainInput.text.Replace(",", "").Trim();
            if (!long.TryParse(digits, out long offer) || offer <= 0)
            {
                SetLine(bargainInvalidLine);
                bargainInput.ActivateInputField();
                return;
            }
            bargainInput.gameObject.SetActive(false);
            expectedTotalText.gameObject.SetActive(true);
            if (!isActiveAndEnabled) { Decide(offer); Refresh(); return; }
            bargainRoutine = StartCoroutine(BargainSequence(offer));
        }

        // 흥정 판단: 상황 정보 만들기 → 모델이 수락 확률 계산 → 주사위 → 결과를 모델·기록에 알림
        // (연출은 BargainSequence가 담당. 판단 방식을 바꿀 땐 여기가 아니라 Bargain Model 에셋을 바꿈)
        private bool Decide(long offer)
        {
            bargainAttempts++;
            BargainContext ctx = BuildBargainContext(offer);
            float chance = alwaysAccept ? 1f
                : bargainModel != null ? Mathf.Clamp01(bargainModel.AcceptChance(ctx))
                : RuleBasedBargainModel.Compute(ctx, safeRatio, maxRatio);
            bool accepted = UnityEngine.Random.value <= chance;

            BargainHistory.Record(accepted);
            if (bargainModel != null) bargainModel.OnResult(ctx, accepted);
            if (logBargains) BargainLogger.Write(ctx, chance, accepted);

            dealTotal = accepted ? offer : -1;
            bargainState = BargainState.Done;
            return accepted;
        }

        // 지금 담은 물품으로 흥정 상황 정보 만들기
        private BargainContext BuildBargainContext(long offer)
        {
            var lines = new List<BargainContext.Line>();
            InventoryManager inventory = InventoryManager.Instance;
            ShopManager shop = ShopManager.Instance;
            if (inventory != null && shop != null)
            {
                foreach (int index in cartOrder)
                {
                    InventorySlotData slot = inventory.GetSlot(index);
                    if (slot == null || slot.IsEmpty || !cart.TryGetValue(index, out int qty)) continue;
                    lines.Add(new BargainContext.Line
                    {
                        Item = slot.item,
                        Quantity = qty,
                        Quality = slot.quality,
                        Total = shop.GetSellLineTotal(index, qty),
                        TodaysSpecial = shop.IsTodaysSpecial(slot.item),
                    });
                }
            }
            return BargainContext.Create(offer, lastBaseTotal, lines, bargainAttempts);
        }

        private IEnumerator BargainSequence(long offer)
        {
            bargainState = BargainState.Thinking;
            Refresh(); // 버튼 잠금
            SaveTotalColor();

            RectTransform totalRt = expectedTotalText.rectTransform;
            Vector2 basePos = totalRt.anchoredPosition;
            expectedTotalText.text = ShopUIUtil.Money(offer);
            expectedTotalText.color = totalBaseColor;

            // 3. 상인 고민: "흐음..." 점이 늘어나고 제안 금액이 바들바들 떨림
            float t = 0f;
            while (t < bargainThinkTime)
            {
                t += Time.unscaledDeltaTime;
                int dots = 1 + (int)(t / 0.35f) % 3;
                SetLine(bargainThinkLine + new string('.', dots));
                float shake = Mathf.Lerp(1f, 4f, t / bargainThinkTime);
                totalRt.anchoredPosition = basePos + new Vector2(Mathf.Sin(t * 60f) * shake, 0f);
                yield return null;
            }
            totalRt.anchoredPosition = basePos;

            bool accepted = Decide(offer);
            if (accepted)
            {
                // 4-1. 수락: 원래 금액 → 제안 금액으로 쭉 바뀌며 커졌다 돌아옴
                SetLine(offer <= lastBaseTotal ? bargainCheapLine : string.Format(bargainAcceptLine, ShopUIUtil.Money(offer)));
                t = 0f;
                while (t < bargainCountTime)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.Clamp01(t / bargainCountTime);
                    float e = 1f - (1f - k) * (1f - k);
                    long shown = (long)Mathf.Round(Mathf.Lerp(lastBaseTotal, offer, e));
                    expectedTotalText.text = ShopUIUtil.Money(shown);
                    expectedTotalText.color = Color.Lerp(totalBaseColor, bargainColor, e);
                    totalRt.localScale = Vector3.one * (1f + 0.3f * Mathf.Sin(k * Mathf.PI));
                    yield return null;
                }
                totalRt.localScale = Vector3.one;
            }
            else
            {
                // 4-2. 거절: 빨갛게 번쩍이며 고개 젓듯 흔들림 → 원래 금액
                SetLine(bargainRejectLine);
                t = 0f;
                while (t < 0.6f)
                {
                    t += Time.unscaledDeltaTime;
                    expectedTotalText.color = Color.Lerp(new Color(0.9f, 0.3f, 0.3f), totalBaseColor, t / 0.6f);
                    totalRt.anchoredPosition = basePos + new Vector2(Mathf.Sin(t * 50f) * 6f * (1f - t / 0.6f), 0f);
                    yield return null;
                }
                totalRt.anchoredPosition = basePos;
            }

            bargainRoutine = null;
            Refresh();
        }

        // 금액 입력 칸 (처음 흥정할 때 예상 판매 금액 글자 자리에 코드로 만듦 → 씬·프리팹은 건드리지 않음)
        private void EnsureBargainInput()
        {
            if (bargainInput != null || expectedTotalText == null) return;
            SaveTotalColor();
            RectTransform src = expectedTotalText.rectTransform;

            var go = new GameObject("BargainInput", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            var rt = (RectTransform)go.transform;
            rt.SetParent(src.parent, false);
            rt.anchorMin = src.anchorMin;
            rt.anchorMax = src.anchorMax;
            rt.pivot = src.pivot;
            rt.anchoredPosition = src.anchoredPosition;
            rt.sizeDelta = src.sizeDelta;
            rt.SetSiblingIndex(src.GetSiblingIndex() + 1);
            go.GetComponent<Image>().color = Color.clear; // 배경 없음 (클릭만 받음) → 원래 금액 글자와 똑같이 보이게

            // "벨" 글자 (원래 금액 표시처럼 숫자 뒤에 붙음)
            float unitWidth = expectedTotalText.fontSize * 1.1f;
            TextMeshProUGUI unit = MakeInputText("Unit", rt, totalBaseColor);
            unit.text = ShopUIUtil.Currency;
            unit.rectTransform.anchorMin = new Vector2(1f, 0f);
            unit.rectTransform.anchorMax = new Vector2(1f, 1f);
            unit.rectTransform.pivot = new Vector2(1f, 0.5f);
            unit.rectTransform.sizeDelta = new Vector2(unitWidth, 0f);
            unit.rectTransform.anchoredPosition = Vector2.zero;

            // 숫자 칸: 오른쪽 끝(벨 앞)에 붙음. 원래 금액 글자 크기 그대로
            var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            var areaRt = (RectTransform)area.transform;
            areaRt.SetParent(rt, false);
            areaRt.anchorMin = new Vector2(0.45f, 0f);
            areaRt.anchorMax = Vector2.one;
            areaRt.offsetMin = Vector2.zero;
            areaRt.offsetMax = new Vector2(-unitWidth, 0f);

            // 숫자 아래 얇은 밑줄 (입력 중이라는 표시만 살짝)
            var line = new GameObject("Underline", typeof(RectTransform), typeof(Image));
            var lineRt = (RectTransform)line.transform;
            lineRt.SetParent(rt, false);
            lineRt.anchorMin = new Vector2(0.55f, 0.18f);
            lineRt.anchorMax = new Vector2(1f, 0.18f);
            lineRt.sizeDelta = new Vector2(0f, 2f);
            lineRt.anchoredPosition = Vector2.zero;
            Image lineImg = line.GetComponent<Image>();
            lineImg.color = new Color(totalBaseColor.r, totalBaseColor.g, totalBaseColor.b, 0.35f);
            lineImg.raycastTarget = false;

            TextMeshProUGUI placeholder = MakeInputText("Placeholder", areaRt, new Color(totalBaseColor.r, totalBaseColor.g, totalBaseColor.b, 0.3f));
            TextMeshProUGUI text = MakeInputText("Text", areaRt, totalBaseColor);

            TMP_InputField input = go.GetComponent<TMP_InputField>();
            input.textViewport = areaRt;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.contentType = TMP_InputField.ContentType.IntegerNumber;
            input.characterLimit = 9;
            input.customCaretColor = true;
            input.caretColor = totalBaseColor;
            input.onSubmit.AddListener(_ => SubmitOffer()); // Enter
            go.SetActive(false);
            bargainInput = input;
        }

        private TextMeshProUGUI MakeInputText(string name, RectTransform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var t = go.GetComponent<TextMeshProUGUI>();
            t.font = expectedTotalText.font;
            t.fontSize = expectedTotalText.fontSize;
            t.fontStyle = expectedTotalText.fontStyle;
            t.color = color;
            t.alignment = TextAlignmentOptions.MidlineRight;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            return t;
        }
    }
}