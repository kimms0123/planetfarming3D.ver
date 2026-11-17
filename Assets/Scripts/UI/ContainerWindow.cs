using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using FarmingSystem.Building;
using FarmingSystem.Core;
using FarmingSystem.Economy;
using FarmingSystem.Farming;
using FarmingSystem.Inventory;
using FarmingSystem.Save;

namespace FarmingSystem.UI
{
    // 보관 상자·판매 포탈 창 (스타듀밸리 상자처럼 위 = 상자 칸, 아래 = 내 가방)
    //  ┌ 보관 상자 ───────────────────────── X ┐
    //  │ [30칸: 10 x 3]                          │
    //  │ 안내 줄 (포탈: 예상 수입 · 내일 아침 정산) │
    //  │ 가방                                     │
    //  │ [40칸: 10 x 4, 첫 줄 = 핫바]             │
    //  └──────────────────────────────────────────┘
    //  - 칸 클릭 = 그 칸 전부 반대쪽으로 / 우클릭 = 1개만
    //  - 판매 포탈에는 작물·씨앗만 들어감 (상점에서 팔 수 있는 것)
    //  - Esc 또는 E = 닫기. 열려 있는 동안 이동·농사는 멈춤 (커서 보임)
    //  PlacedObjects가 Open()을 부름. 처음 열 때 화면을 만듦
    public class ContainerWindow : MonoBehaviour
    {
        private const int Columns = 10;
        private const float Cell = 84f, Gap = 8f, Pad = 40f;

        private static readonly Color PanelFill = new Color(0.97f, 0.95f, 0.89f, 0.98f);
        private static readonly Color Brown = new Color32(0x5B, 0x4A, 0x3A, 0xFF);
        private static readonly Color SlotFill = new Color(0.93f, 0.88f, 0.78f, 1f);
        private static readonly Color PortalFill = new Color(0.86f, 0.9f, 0.98f, 1f);
        private static readonly Color MoneyColor = new Color32(0x2F, 0x7D, 0x4F, 0xFF);
        private static readonly Color WarnColor = new Color32(0xC0, 0x3A, 0x2E, 0xFF);

        public static bool IsOpen { get; private set; }

        private static ContainerWindow instance;
        private Canvas canvas;
        private TMP_FontAsset font;
        private Sprite rounded;
        private TextMeshProUGUI title, info;
        private ContainerSlotView[] boxViews, bagViews;
        private InventorySlotData[] box;
        private string key;
        private bool portal;
        private Action onClosed;
        private int openedFrame;
        private float warnUntil;
        private InventoryManager subscribed;

        /// <summary>창 열기 (name = 제목, key = ContainerStorage 열쇠, portal = 판매 포탈인지, closed = 닫힐 때)</summary>
        public static void Open(string name, string key, bool portal, Action closed)
        {
            if (instance == null)
            {
                var go = new GameObject("ContainerWindow");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<ContainerWindow>();
                instance.Build();
            }
            instance.Show(name, key, portal, closed);
        }

        private void Show(string name, string k, bool isPortal, Action closed)
        {
            key = k;
            box = ContainerStorage.Get(k);
            portal = isPortal;
            onClosed = closed;
            title.text = name;
            foreach (ContainerSlotView v in boxViews) v.baseColor = portal ? PortalFill : SlotFill;
            canvas.enabled = true;
            IsOpen = true;
            openedFrame = Time.frameCount;
            UIInputBlocker.Block(this);
            if (InventoryManager.Instance != null && subscribed != InventoryManager.Instance)
            {
                subscribed = InventoryManager.Instance;
                subscribed.OnInventoryChanged += RefreshAll;
            }
            RefreshAll();
        }

        private void Close()
        {
            if (!IsOpen) return;
            canvas.enabled = false;
            IsOpen = false;
            UIInputBlocker.Unblock(this);
            Action cb = onClosed;
            onClosed = null;
            cb?.Invoke();
        }

        private void OnDestroy()
        {
            if (subscribed != null) subscribed.OnInventoryChanged -= RefreshAll;
            UIInputBlocker.Unblock(this);
            IsOpen = false;
        }

        private void Update()
        {
            if (!IsOpen) return;
            Keyboard kb = Keyboard.current;
            bool close = kb != null && (kb.escapeKey.wasPressedThisFrame || (kb.eKey.wasPressedThisFrame && Time.frameCount != openedFrame));
            if (close || TitleScreen.IsShowing) { Close(); return; }
            if (portal) UpdateInfo();
        }

        // ───────────────────────── 옮기기 ─────────────────────────

        // 상자 칸 클릭 → 가방으로
        private void ClickBox(int i, bool one)
        {
            InventorySlotData s = box[i];
            InventoryManager inv = InventoryManager.Instance;
            if (s.IsEmpty || inv == null) return;
            int amount = one ? 1 : s.quantity;
            int notAdded = inv.AddItemKeepDurability(s.item, amount, s.quality, s.durability);
            int moved = amount - notAdded;
            if (moved <= 0) { Warn("가방에 자리가 없어요"); return; }
            s.quantity -= moved;
            if (s.quantity <= 0) s.Clear();
            AfterMove();
        }

        // 가방 칸 클릭 → 상자로
        private void ClickBag(int i, bool one)
        {
            InventoryManager inv = InventoryManager.Instance;
            InventorySlotData s = inv != null ? inv.GetSlot(SlotArea.Main, i) : null;
            if (s == null || s.IsEmpty) return;
            if (portal && !ShopManager.IsSellable(s)) { Warn("판매 포탈에는 작물과 씨앗만 넣을 수 있어요"); return; }
            int amount = one ? 1 : s.quantity;
            int moved = ContainerStorage.PutInto(box, s.item, amount, s.quality, s.durability);
            if (moved <= 0) { Warn(portal ? "포탈이 가득 찼어요" : "상자가 가득 찼어요"); return; }
            inv.RemoveFromSlot(i, moved); // 가방이 바뀌었다는 알림 → RefreshAll
            AfterMove();
        }

        private void AfterMove()
        {
            if (portal && PlacedObjects.Instance != null) PlacedObjects.Instance.RefreshPortalValues();
            RefreshAll();
        }

        private void Warn(string text)
        {
            info.text = $"<color=#{ColorUtility.ToHtmlStringRGB(WarnColor)}>{text}</color>";
            warnUntil = Time.unscaledTime + 1.6f;
        }

        // ───────────────────────── 화면 갱신 ─────────────────────────

        private void RefreshAll()
        {
            if (!IsOpen || box == null) return;
            for (int i = 0; i < boxViews.Length; i++) boxViews[i].Show(i < box.Length ? box[i] : null);
            InventoryManager inv = InventoryManager.Instance;
            for (int i = 0; i < bagViews.Length; i++)
            {
                InventorySlotData s = inv != null ? inv.GetSlot(SlotArea.Main, i) : null;
                bagViews[i].gameObject.SetActive(s != null);
                bagViews[i].Show(s);
            }
            if (Time.unscaledTime >= warnUntil) UpdateInfo();
        }

        private void UpdateInfo()
        {
            if (Time.unscaledTime < warnUntil) return;
            if (!portal) { info.text = "클릭: 전부 옮기기   ·   우클릭: 1개만"; return; }
            int value = PlacedObjects.ValueOf(box);
            info.text = value > 0
                ? $"내일 아침에 팔려요   ·   예상 수입 <color=#{ColorUtility.ToHtmlStringRGB(MoneyColor)}><b>{value:N0}벨</b></color>"
                : "작물·씨앗을 넣어 두면 내일 아침에 팔려요";
        }

        // ───────────────────────── 만들기 ─────────────────────────

        private void Build()
        {
            font = UIFont.FindKorean();
            rounded = MakeRounded();
            var cgo = new GameObject("ContainerCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            cgo.transform.SetParent(transform, false);
            canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 650; // 게임 화면 UI·알림 위, 폰 메뉴·아이템 설명 아래
            canvas.enabled = false;
            var scaler = cgo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // 뒤 어둡게 (클릭이 게임으로 새지 않게)
            RectTransform dim = Rect("Dim", cgo.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            dim.offsetMin = dim.offsetMax = Vector2.zero;
            dim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);

            float gridW = Columns * Cell + (Columns - 1) * Gap;
            float boxH = 3 * Cell + 2 * Gap, bagH = 4 * Cell + 3 * Gap;
            float h = 28f + 48f + 14f + boxH + 46f + 8f + 36f + bagH + 32f;
            RectTransform panel = Rect("Panel", cgo.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(gridW + Pad * 2f, h));
            Image pbg = panel.gameObject.AddComponent<Image>();
            pbg.sprite = rounded;
            pbg.type = Image.Type.Sliced;
            pbg.color = PanelFill;
            Outline(panel, 4f);

            float y = 28f;
            title = Text(panel, "", 32f, FontStyles.Bold, Pad, y, gridW - 60f, 48f, TextAlignmentOptions.MidlineLeft);
            RectTransform close = TopLeft(Rect("Close", panel, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(48f, 48f)), Pad + gridW - 48f, y);
            Image cbg = close.gameObject.AddComponent<Image>();
            cbg.sprite = rounded;
            cbg.type = Image.Type.Sliced;
            cbg.color = SlotFill;
            close.gameObject.AddComponent<Button>().onClick.AddListener(Close);
            TextMeshProUGUI x = Text(close, "X", 26f, FontStyles.Bold, 0f, 0f, 48f, 48f, TextAlignmentOptions.Center);
            x.raycastTarget = false;
            y += 48f + 14f;

            boxViews = Grid(panel, ContainerStorage.Size, y, (i, right) => ClickBox(i, right), false);
            y += boxH + 6f;
            info = Text(panel, "", 22f, FontStyles.Normal, Pad, y, gridW, 40f, TextAlignmentOptions.MidlineLeft);
            y += 40f + 8f;

            // 구분선 + "가방"
            RectTransform line = TopLeft(Rect("Line", panel, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(gridW, 2f)), Pad, y);
            line.gameObject.AddComponent<Image>().color = new Color(Brown.r, Brown.g, Brown.b, 0.25f);
            Text(panel, "가방", 24f, FontStyles.Bold, Pad, y + 4f, 200f, 32f, TextAlignmentOptions.MidlineLeft);
            y += 36f;

            int bagCount = InventoryManager.Instance != null ? InventoryManager.Instance.GetSlotCount(SlotArea.Main) : 40;
            bagViews = Grid(panel, Mathf.Max(bagCount, 40), y, (i, right) => ClickBag(i, right), true);
        }

        private ContainerSlotView[] Grid(RectTransform panel, int count, float top, Action<int, bool> click, bool hotbarNumbers)
        {
            var views = new ContainerSlotView[count];
            for (int i = 0; i < count; i++)
            {
                int c = i % Columns, r = i / Columns;
                RectTransform rt = TopLeft(Rect("Slot" + i, panel, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(Cell, Cell)), Pad + c * (Cell + Gap), top + r * (Cell + Gap));
                ContainerSlotView v = rt.gameObject.AddComponent<ContainerSlotView>();
                int index = i;
                v.Build(rounded, font, hotbarNumbers && r == 0 ? ((c + 1) % 10).ToString() : null);
                v.onClick = right => click(index, right);
                views[i] = v;
            }
            return views;
        }

        // ───────────────────────── 도우미 ─────────────────────────

        private static RectTransform Rect(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        // 패널 왼쪽 위에서 (x, y) 떨어진 자리
        private static RectTransform TopLeft(RectTransform rt, float x, float y)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            return rt;
        }

        private TextMeshProUGUI Text(RectTransform parent, string text, float size, FontStyles style, float x, float y, float w, float h, TextAlignmentOptions align)
        {
            RectTransform rt = TopLeft(Rect("Text", parent, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(w, h)), x, y);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = Brown;
            t.alignment = align;
            t.richText = true;
            t.raycastTarget = false;
            return t;
        }

        private void Outline(RectTransform target, float width)
        {
            RectTransform o = Rect("Outline", target, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            o.offsetMin = new Vector2(-width, -width);
            o.offsetMax = new Vector2(width, width);
            o.SetAsFirstSibling();
            Image img = o.gameObject.AddComponent<Image>();
            img.sprite = rounded;
            img.type = Image.Type.Sliced;
            img.color = Brown;
            img.raycastTarget = false;
        }

        // 흰 둥근 사각형 (색을 곱해 씀, 9칸 늘이기)
        public static Sprite MakeRounded()
        {
            const int S = 48, R = 14;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, R, S - R), cy = Mathf.Clamp(y + 0.5f, R, S - R);
                    float d = R - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    px[y * S + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(d + 0.5f));
                }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(R + 2, R + 2, R + 2, R + 2));
        }
    }

    // 상자 창의 한 칸 (아이콘·개수·등급 별·내구도 막대). 아이템 설명(ItemTooltip)도 이 칸을 읽음
    public class ContainerSlotView : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public Action<bool> onClick; // true = 우클릭
        public Color baseColor = new Color(0.93f, 0.88f, 0.78f, 1f);
        public InventorySlotData Data { get; private set; }

        private Image bg, icon, barBg, barFill;
        private TextMeshProUGUI qty, star, nameFallback;
        private bool hover;

        public void Build(Sprite rounded, TMP_FontAsset font, string hotkey)
        {
            bg = gameObject.AddComponent<Image>();
            bg.sprite = rounded;
            bg.type = Image.Type.Sliced;
            bg.color = baseColor;

            icon = Child("Icon", new Vector2(0.14f, 0.14f), new Vector2(0.86f, 0.86f)).gameObject.AddComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            nameFallback = Label(font, 15f, new Vector2(0.06f, 0.2f), new Vector2(0.94f, 0.8f), TextAlignmentOptions.Center); // 아이콘이 없을 때 이름
            qty = Label(font, 20f, new Vector2(0.4f, 0.02f), new Vector2(0.94f, 0.4f), TextAlignmentOptions.BottomRight);
            qty.fontStyle = FontStyles.Bold;
            star = Label(font, 20f, new Vector2(0.05f, 0.6f), new Vector2(0.5f, 0.97f), TextAlignmentOptions.TopLeft);
            if (hotkey != null)
            {
                TextMeshProUGUI k = Label(font, 14f, new Vector2(0.6f, 0.68f), new Vector2(0.94f, 0.97f), TextAlignmentOptions.TopRight);
                k.text = hotkey;
                k.alpha = 0.5f;
            }

            barBg = Child("Bar", new Vector2(0.14f, 0.07f), new Vector2(0.86f, 0.14f)).gameObject.AddComponent<Image>();
            barBg.color = new Color(0.2f, 0.16f, 0.13f, 0.75f);
            barBg.raycastTarget = false;
            RectTransform fill = Child("Fill", Vector2.zero, Vector2.one, barBg.transform);
            barFill = fill.gameObject.AddComponent<Image>();
            barFill.raycastTarget = false;
        }

        public void Show(InventorySlotData s)
        {
            Data = s != null && !s.IsEmpty ? s : null;
            bg.color = hover ? SlotHoverColor : baseColor;
            bool has = Data != null;
            icon.enabled = has && Data.item.icon != null;
            if (icon.enabled) icon.sprite = Data.item.icon;
            nameFallback.text = has && Data.item.icon == null ? Data.item.itemName : "";
            qty.text = has && Data.quantity > 1 ? Data.quantity.ToString() : "";
            star.text = has && Data.item is CropData && Data.quality == ItemQuality.Perfect ? "<color=#E8A820>★</color>"
                      : has && Data.item is CropData && Data.quality == ItemQuality.Good ? "<color=#5C8ED8>★</color>" : "";

            ToolData tool = has ? Data.item as ToolData : null;
            bool bar = tool != null && tool.maxDurability > 0;
            barBg.gameObject.SetActive(bar);
            if (bar)
            {
                float k = Mathf.Clamp01(Data.durability / (float)tool.maxDurability);
                ((RectTransform)barFill.transform).anchorMax = new Vector2(k, 1f);
                barFill.color = k > 0.5f ? new Color(0.45f, 0.8f, 0.4f) : k > 0.2f ? new Color(0.95f, 0.78f, 0.3f) : new Color(0.9f, 0.35f, 0.3f);
            }
        }

        private static readonly Color SlotHoverColor = new Color(1f, 0.93f, 0.7f, 1f);

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left) onClick?.Invoke(false);
            else if (e.button == PointerEventData.InputButton.Right) onClick?.Invoke(true);
        }

        public void OnPointerEnter(PointerEventData e) { hover = true; bg.color = SlotHoverColor; }
        public void OnPointerExit(PointerEventData e) { hover = false; bg.color = baseColor; }

        private RectTransform Child(string name, Vector2 aMin, Vector2 aMax, Transform parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent != null ? parent : transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        private TextMeshProUGUI Label(TMP_FontAsset font, float size, Vector2 aMin, Vector2 aMax, TextAlignmentOptions align)
        {
            var t = Child("Text", aMin, aMax).gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.fontSize = size;
            t.color = new Color32(0x4A, 0x3D, 0x31, 0xFF);
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.richText = true;
            t.raycastTarget = false;
            return t;
        }
    }
}
