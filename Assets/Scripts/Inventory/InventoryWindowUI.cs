using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using FarmingSystem.Core;
using FarmingSystem.Economy;
using FarmingSystem.Inventory;

namespace FarmingSystem.UI
{
    // 인벤토리 창 (Tab으로 열고 닫음, Esc로도 닫힘)
    //  ┌──────────────────────────────────────────────┐
    //  │ [캐릭터 3D + 이름·돈]  │ [핫바 줄 1~0 (노란 띠)]      │
    //  │                     │ [가방 2줄 x 10칸]            │
    //  │ [도구칸 4칸]         │ [아이템 이름·설명 줄]         │
    //  └──────────────────────────────────────────────┘
    // - 가방 첫 줄 = 핫바 (같은 데이터라 바꾸면 핫바에도 바로 반영)
    // - 끌어서 옮기기: 빈 칸이면 옮기고, 같은 아이템이면 합치고, 다르면 자리 바꿈. 도구칸에는 도구만
    // - 칸에 마우스를 올리면 아래 설명줄에 이름·등급·개수·설명
    // - 열려 있는 동안 이동·농사·카메라 조작을 막음 (UIInputBlocker)
    // - 이 오브젝트는 항상 켜 둠 (창 자체는 windowRoot를 켜고 끔)
    public class InventoryWindowUI : MonoBehaviour
    {
        [Header("창")]
        [Tooltip("켜고 끄는 창 전체 (어두운 배경 포함)")]
        [SerializeField] private GameObject windowRoot;
        [Tooltip("열릴 때 살짝 커지며 나타나는 판 (비우면 연출 없음)")]
        [SerializeField] private RectTransform windowPanel;

        [Header("칸 (씬에 미리 만들어 둔 칸을 순서대로 사용, 모자라면 프리팹으로 추가)")]
        [SerializeField] private InventorySlotView slotPrefab;
        [Tooltip("핫바 줄 (가방 0~9번 칸)")]
        [SerializeField] private Transform hotbarRow;
        [Tooltip("나머지 가방 (10번 칸부터)")]
        [SerializeField] private Transform bagGrid;
        [Tooltip("도구칸")]
        [SerializeField] private Transform toolGrid;

        [Header("정보")]
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private string playerName = "농부";
        [SerializeField] private TMP_Text moneyText;
        [Tooltip("마우스를 올린 아이템의 이름·설명")]
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private string emptyDescription = "아이템에 마우스를 올리면 설명이 나와요. 끌어서 옮길 수 있어요.";
        [SerializeField] private Color warningColor = new Color32(0xC8, 0x4B, 0x4B, 0xFF);

        [Header("끌기")]
        [Tooltip("끄는 동안 마우스를 따라다니는 아이콘")]
        [SerializeField] private Image dragGhost;

        [Header("연출")]
        [SerializeField, Range(0f, 0.5f)] private float openDuration = 0.15f;

        private readonly List<InventorySlotView> mainViews = new List<InventorySlotView>();
        private readonly List<InventorySlotView> toolViews = new List<InventorySlotView>();
        private InventoryManager inv;
        private bool subscribed;
        private bool isOpen;

        private InventorySlotView dragFrom;
        private InventorySlotView hovered;
        private Color descriptionColor;
        private Coroutine warnRoutine;
        private Coroutine openRoutine;
        private Canvas rootCanvas;

        private void Awake()
        {
            rootCanvas = GetComponentInParent<Canvas>();
            if (rootCanvas != null) rootCanvas = rootCanvas.rootCanvas;
            if (descriptionText != null) descriptionColor = descriptionText.color;
            if (dragGhost != null)
            {
                dragGhost.raycastTarget = false;
                dragGhost.gameObject.SetActive(false);
            }
            if (windowRoot != null) windowRoot.SetActive(false);
        }

        private void OnEnable() => TrySubscribe();
        private void Update() { if (!subscribed) TrySubscribe(); }

        private void OnDisable()
        {
            if (inv != null)
            {
                inv.OnInventoryToggled -= HandleToggled;
                inv.OnInventoryChanged -= Refresh;
                inv.OnSelectedHotbarIndexChanged -= HandleIndex;
                inv.OnToolChanged -= HandleTool;
            }
            if (CurrencyManager.Instance != null) CurrencyManager.Instance.OnBellsChanged -= HandleBells;
            subscribed = false;
            UIInputBlocker.Unblock(this);
        }

        // InventoryManager가 늦게 생겨도 연결되게
        private void TrySubscribe()
        {
            inv = InventoryManager.Instance;
            if (inv == null) return;
            inv.OnInventoryToggled += HandleToggled;
            inv.OnInventoryChanged += Refresh;
            inv.OnSelectedHotbarIndexChanged += HandleIndex;
            inv.OnToolChanged += HandleTool;
            if (CurrencyManager.Instance != null) CurrencyManager.Instance.OnBellsChanged += HandleBells;
            subscribed = true;
            BuildSlots();
            if (inv.IsInventoryOpen) HandleToggled(true);
        }

        private void HandleIndex(int _) => Refresh();
        private void HandleTool(ToolType _) => Refresh();
        private void HandleBells(int _) => RefreshMoney();

        // ---------- 칸 준비 ----------

        private void BuildSlots()
        {
            mainViews.Clear();
            toolViews.Clear();
            int hotbar = inv.HotbarSize;
            int total = inv.GetSlotCount(SlotArea.Main);
            int tools = inv.GetSlotCount(SlotArea.Tool);

            List<InventorySlotView> hotbarViews = Collect(hotbarRow, hotbar);
            List<InventorySlotView> bagViews = Collect(bagGrid, total - hotbar);
            mainViews.AddRange(hotbarViews);
            mainViews.AddRange(bagViews);
            toolViews.AddRange(Collect(toolGrid, tools));

            for (int i = 0; i < mainViews.Count; i++)
            {
                string key = i < hotbar ? (i == 9 ? "0" : (i + 1).ToString()) : null;
                mainViews[i].Setup(this, SlotArea.Main, i, key);
            }
            for (int i = 0; i < toolViews.Count; i++)
                toolViews[i].Setup(this, SlotArea.Tool, i, null);
        }

        // 부모 아래의 칸을 필요한 개수만큼 (남으면 숨기고, 모자라면 프리팹으로 추가)
        private List<InventorySlotView> Collect(Transform parent, int count)
        {
            var list = new List<InventorySlotView>();
            if (parent == null) return list;
            foreach (Transform c in parent)
            {
                InventorySlotView v = c.GetComponent<InventorySlotView>();
                if (v == null) continue;
                if (list.Count < count) { list.Add(v); v.gameObject.SetActive(true); }
                else v.gameObject.SetActive(false);
            }
            while (list.Count < count && slotPrefab != null)
                list.Add(Instantiate(slotPrefab, parent));
            if (list.Count < count) Debug.LogWarning($"[인벤토리 창] '{parent.name}'에 칸이 {count}개 필요한데 {list.Count}개뿐이에요 (Slot Prefab을 연결해 주세요)", this);
            return list;
        }

        // ---------- 열기·닫기 ----------

        private void HandleToggled(bool open)
        {
            isOpen = open;
            if (open)
            {
                UIInputBlocker.Block(this);
                if (windowRoot != null) windowRoot.SetActive(true);
                Refresh();
                RefreshMoney();
                if (nameText != null) nameText.text = playerName;
                SetDescription(null);
                if (openRoutine != null) StopCoroutine(openRoutine);
                if (windowPanel != null && openDuration > 0f) openRoutine = StartCoroutine(OpenAnim());
            }
            else
            {
                CancelDrag();
                UIInputBlocker.Unblock(this);
                if (windowRoot != null) windowRoot.SetActive(false);
            }
        }

        private IEnumerator OpenAnim()
        {
            CanvasGroup g = windowPanel.GetComponent<CanvasGroup>();
            if (g == null) g = windowPanel.gameObject.AddComponent<CanvasGroup>();
            float t = 0f;
            while (t < openDuration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / openDuration);
                float e = 1f - (1f - k) * (1f - k);
                windowPanel.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, e);
                g.alpha = e;
                yield return null;
            }
            windowPanel.localScale = Vector3.one;
            g.alpha = 1f;
            openRoutine = null;
        }

        /// <summary>닫기 버튼(Button OnClick)에 연결</summary>
        public void Close()
        {
            if (inv != null) inv.SetInventoryOpen(false);
        }

        // ---------- 그리기 ----------

        private void Refresh()
        {
            if (inv == null || !isOpen) return;
            for (int i = 0; i < mainViews.Count; i++)
                mainViews[i].Show(inv.GetSlot(SlotArea.Main, i), i == inv.SelectedHotbarIndex);
            for (int i = 0; i < toolViews.Count; i++)
            {
                InventorySlotData d = inv.GetSlot(SlotArea.Tool, i);
                toolViews[i].Show(d, i == inv.CurrentToolIndex && d != null && !d.IsEmpty);
            }
            if (hovered != null) SetDescription(hovered);
        }

        private void RefreshMoney()
        {
            if (moneyText == null) return;
            int bells = CurrencyManager.Instance != null ? CurrencyManager.Instance.CurrentBells : 0;
            moneyText.text = bells.ToString("N0");
        }

        // ---------- 마우스 ----------

        public void HandleHover(InventorySlotView v, bool enter)
        {
            if (enter) { hovered = v; SetDescription(v); }
            else if (hovered == v) { hovered = null; SetDescription(null); }
        }

        public void HandleClick(InventorySlotView v)
        {
            if (inv == null) return;
            if (v.Area == SlotArea.Main && v.Index < inv.HotbarSize) inv.SelectHotbarSlot(v.Index);
            else if (v.Area == SlotArea.Tool) inv.SelectToolSlot(v.Index);
        }

        public void BeginDrag(InventorySlotView from, PointerEventData e)
        {
            dragFrom = from;
            from.SetDragging(true);
            if (dragGhost != null)
            {
                dragGhost.sprite = from.IconSprite;
                dragGhost.enabled = dragGhost.sprite != null;
                dragGhost.gameObject.SetActive(true);
                dragGhost.transform.SetAsLastSibling();
                MoveGhost(e);
            }
        }

        public void Drag(PointerEventData e)
        {
            if (dragFrom != null) MoveGhost(e);
        }

        public void Drop(InventorySlotView to)
        {
            if (dragFrom == null || inv == null) return;
            if (!inv.TryMoveItem(dragFrom.Area, dragFrom.Index, to.Area, to.Index, out string reason) && !string.IsNullOrEmpty(reason))
                Warn(reason);
            // EndDrag가 뒤이어 호출되며 정리
        }

        public void EndDrag(PointerEventData e) => CancelDrag();

        private void CancelDrag()
        {
            if (dragFrom != null) dragFrom.SetDragging(false);
            dragFrom = null;
            if (dragGhost != null) dragGhost.gameObject.SetActive(false);
        }

        private void MoveGhost(PointerEventData e)
        {
            RectTransform parent = (RectTransform)dragGhost.transform.parent;
            Camera cam = rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? rootCanvas.worldCamera : null;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, e.position, cam, out Vector2 local))
                dragGhost.rectTransform.localPosition = local;
        }

        // ---------- 설명줄 ----------

        private void SetDescription(InventorySlotView v)
        {
            if (descriptionText == null || warnRoutine != null) return;
            descriptionText.color = descriptionColor;
            InventorySlotData d = v != null && inv != null ? inv.GetSlot(v.Area, v.Index) : null;
            if (d == null || d.IsEmpty)
            {
                descriptionText.text = v != null && v.Area == SlotArea.Tool ? "도구칸 - 여기 넣은 도구만 마우스 휠로 바꿔 들 수 있어요" : emptyDescription;
                return;
            }

            string grade = d.item is FarmingSystem.Farming.CropData ? $"  <size=80%>[{ItemQualityUtility.GetDisplayName(d.quality)}]</size>" : "";
            string count = d.quantity > 1 ? $"  x{d.quantity}" : "";
            string desc = string.IsNullOrEmpty(d.item.description) ? "" : $"\n<size=85%>{d.item.description}</size>";
            descriptionText.text = $"<b>{d.item.itemName}</b>{grade}{count}{desc}";
        }

        private void Warn(string message)
        {
            if (descriptionText == null) return;
            if (warnRoutine != null) StopCoroutine(warnRoutine);
            warnRoutine = StartCoroutine(WarnRoutine(message));
        }

        private IEnumerator WarnRoutine(string message)
        {
            descriptionText.color = warningColor;
            descriptionText.text = message;
            yield return new WaitForSecondsRealtime(1.5f);
            warnRoutine = null;
            SetDescription(hovered);
        }
    }
}