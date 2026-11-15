using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using FarmingSystem.Core;
using FarmingSystem.Economy;
using FarmingSystem.Farming;
using FarmingSystem.Inventory;
using FarmingSystem.UI.Shop;

namespace FarmingSystem.UI
{
    // 아이템 칸에 마우스를 올리면 마우스 옆에 작은 설명 창 (스타듀밸리 느낌)
    // 설치 필요 없음 - 게임이 시작되면 저절로 생김. 기존 스크립트는 하나도 안 고침
    //  - 인벤토리 칸(InventorySlotView), 상점 판매 칸(ShopSellSlotUI), 상점 구매 칸(ShopBuySlotUI)에서 동작
    //  - 내용
    //    · 작물·씨앗: 이름 / 계절 / 특징 (작물 설명의 첫 줄)
    //    · 그 외: 이름 / 종류 / 설명의 첫 줄
    //  - 마우스를 따라다니고 화면 끝에서는 반대쪽으로 넘어감. 끌거나 클릭 중일 때는 숨김
    //  - 글꼴은 마우스가 올라간 칸이 쓰는 한글 폰트를 그대로 가져다 씀
    public class ItemTooltip : MonoBehaviour
    {
        private const float MaxWidth = 340f;          // 설명 줄바꿈 너비 (1920x1080 기준 px)
        private const float MinWidth = 160f;
        private const float Pad = 16f;
        private const float Gap = 4f;
        private static readonly Vector2 MouseOffset = new Vector2(22f, -22f);

        private static readonly Color Fill = new Color(0.97f, 0.96f, 0.92f, 0.97f);
        private static readonly Color Outline = new Color32(0x5B, 0x4A, 0x3A, 0xFF);
        private static readonly Color NameColor = new Color32(0x5B, 0x4A, 0x3A, 0xFF);
        private static readonly Color SubColor = new Color32(0x9A, 0x86, 0x70, 0xFF);
        private static readonly Color DescColor = new Color32(0x4A, 0x3D, 0x31, 0xFF);

        private static ItemTooltip instance;
        private static readonly FieldInfo SellSlotIndexField = typeof(ShopSellSlotUI).GetField("slotIndex", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly string[] SeasonNames = { "봄", "여름", "가을", "겨울" };

        private Canvas canvas;
        private RectTransform canvasRect, panel, divider;
        private TMP_Text nameText, subText, descText;
        private TMP_FontAsset currentFont;
        private readonly List<RaycastResult> hits = new List<RaycastResult>();
        private PointerEventData pointer;
        private EventSystem pointerOwner;
        private ItemData shownItem;
        private ItemQuality shownQuality;
        private bool visible;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (instance != null) return;
            var go = new GameObject("ItemTooltip");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<ItemTooltip>();
        }

        private void Awake() => Build();

        private void Update()
        {
            ItemData item = null;
            ItemQuality quality = ItemQuality.Normal;
            TMP_FontAsset font = null;
            bool found = Mouse.current != null && !Mouse.current.leftButton.isPressed && FindHovered(out item, out quality, out font);

            if (!found)
            {
                if (visible) SetVisible(false);
                return;
            }
            if (font != null && font != currentFont) ApplyFont(font);
            if (!visible || item != shownItem || quality != shownQuality) ShowItem(item, quality);
            if (!visible) SetVisible(true);
            Follow(Mouse.current.position.ReadValue());
        }

        // ───────────────────────── 마우스 아래 칸 찾기 ─────────────────────────

        private bool FindHovered(out ItemData item, out ItemQuality quality, out TMP_FontAsset font)
        {
            item = null; quality = ItemQuality.Normal; font = null;
            EventSystem es = EventSystem.current;
            if (es == null) return false;
            if (pointer == null || pointerOwner != es) { pointer = new PointerEventData(es); pointerOwner = es; }
            pointer.position = Mouse.current.position.ReadValue();
            hits.Clear();
            es.RaycastAll(pointer, hits);
            if (hits.Count == 0) return false;

            GameObject top = hits[0].gameObject; // 맨 위에 있는 것만 (뒤에 가려진 칸은 무시)
            InventorySlotData data = null;
            Component slot = null;

            InventorySlotView invSlot = top.GetComponentInParent<InventorySlotView>();
            if (invSlot != null && InventoryManager.Instance != null)
            {
                slot = invSlot;
                data = InventoryManager.Instance.GetSlot(invSlot.Area, invSlot.Index);
            }

            if (slot == null && SellSlotIndexField != null)
            {
                ShopSellSlotUI sell = top.GetComponentInParent<ShopSellSlotUI>();
                if (sell != null && InventoryManager.Instance != null)
                {
                    slot = sell;
                    data = InventoryManager.Instance.GetSlot((int)SellSlotIndexField.GetValue(sell));
                }
            }

            if (slot != null) { if (data != null && !data.IsEmpty) { item = data.item; quality = data.quality; } }
            else
            {
                ShopBuySlotUI buy = top.GetComponentInParent<ShopBuySlotUI>();
                if (buy != null && ShopManager.Instance != null && buy.CatalogIndex >= 0 && buy.CatalogIndex < ShopManager.Instance.BuyCatalog.Count)
                {
                    slot = buy;
                    item = ShopManager.Instance.BuyCatalog[buy.CatalogIndex].item;
                }
            }
            if (item == null) return false;

            // 글꼴: 그 칸의 글자 → 없으면 같은 캔버스의 아무 글자
            TMP_Text any = slot.GetComponentInChildren<TMP_Text>(true);
            if (any == null || any.font == null)
            {
                Canvas c = slot.GetComponentInParent<Canvas>();
                if (c != null) any = c.rootCanvas.GetComponentInChildren<TMP_Text>(true);
            }
            if (any != null) font = any.font;
            return true;
        }

        // ───────────────────────── 내용 ─────────────────────────

        private void ShowItem(ItemData item, ItemQuality quality)
        {
            shownItem = item;
            shownQuality = quality;
            nameText.text = item.itemName;

            // 작물·씨앗: 이름 / 계절 / 특징(설명 첫 줄)만
            CropData crop = item as CropData;
            if (crop == null && item is SeedData seed) crop = seed.resultCrop;
            if (crop != null)
            {
                subText.text = Seasons(crop);
                string feature = FirstLine(item.description);           // 씨앗에 자기 설명이 있으면 그것
                if (feature.Length == 0) feature = FirstLine(crop.description); // 없으면 작물의 특징
                descText.text = feature;
                Layout();
                return;
            }

            // 그 외 (도구·재료·건축 키트): 이름 / 종류 / 설명 첫 줄
            subText.text = CategoryName(item);
            descText.text = FirstLine(item.description);
            Layout();
        }

        private static string CategoryName(ItemData item)
        {
            if (item is FarmingSystem.Building.BuildingKitData) return "건축 키트";
            switch (item.category)
            {
                case ItemCategory.Seed: return "씨앗";
                case ItemCategory.Crop: return "작물";
                case ItemCategory.Tool: return "도구";
                case ItemCategory.Fertilizer: return "비료";
                default: return "재료";
            }
        }

        private static string FirstLine(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            int n = s.IndexOf('\n');
            return (n >= 0 ? s.Substring(0, n) : s).Trim();
        }

        private static string Seasons(CropData c)
        {
            var list = new List<string> { SeasonNames[(int)c.season] };
            if (c.additionalSeasons != null)
                foreach (Season s in c.additionalSeasons)
                    if (!list.Contains(SeasonNames[(int)s])) list.Add(SeasonNames[(int)s]);
            return list.Count == 4 ? "모든 계절" : string.Join("·", list);
        }

        // 글자 크기에 맞춰 창 크기 계산
        private void Layout()
        {
            float inner = MaxWidth - Pad * 2f;
            Vector2 nameSize = nameText.GetPreferredValues(nameText.text, inner, 0f);
            Vector2 subSize = subText.GetPreferredValues(subText.text, inner, 0f);
            bool hasDesc = descText.text.Length > 0;
            Vector2 descSize = hasDesc ? descText.GetPreferredValues(descText.text, inner, 0f) : Vector2.zero;

            float w = Mathf.Clamp(Mathf.Max(nameSize.x, subSize.x, descSize.x) + Pad * 2f, MinWidth, MaxWidth);
            float cw = w - Pad * 2f;
            nameSize.y = nameText.GetPreferredValues(nameText.text, cw, 0f).y;
            if (hasDesc) descSize.y = descText.GetPreferredValues(descText.text, cw, 0f).y;

            float y = -Pad;
            Place(nameText.rectTransform, y, cw, nameSize.y); y -= nameSize.y + Gap * 0.5f;
            Place(subText.rectTransform, y, cw, subSize.y); y -= subSize.y;
            divider.gameObject.SetActive(hasDesc);
            descText.gameObject.SetActive(hasDesc);
            if (hasDesc)
            {
                y -= Gap * 2f;
                Place(divider, y, cw, 2f); y -= 2f + Gap * 2f;
                Place(descText.rectTransform, y, cw, descSize.y); y -= descSize.y;
            }
            panel.sizeDelta = new Vector2(w, -y + Pad);
        }

        private static void Place(RectTransform rt, float y, float w, float h)
        {
            rt.anchoredPosition = new Vector2(Pad, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        // 마우스 옆에, 화면 밖으로 나가면 반대쪽으로
        private void Follow(Vector2 screen)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local)) return;
            Vector2 size = panel.sizeDelta;
            Rect area = canvasRect.rect;
            Vector2 pos = local + MouseOffset;
            if (pos.x + size.x > area.xMax - 8f) pos.x = local.x - MouseOffset.x - size.x;
            if (pos.y - size.y < area.yMin + 8f) pos.y = local.y - MouseOffset.y + size.y;
            pos.x = Mathf.Clamp(pos.x, area.xMin + 8f, Mathf.Max(area.xMin + 8f, area.xMax - 8f - size.x));
            pos.y = Mathf.Clamp(pos.y, Mathf.Min(area.yMax - 8f, area.yMin + 8f + size.y), area.yMax - 8f);
            panel.anchoredPosition = pos;
        }

        private void SetVisible(bool on)
        {
            visible = on;
            canvas.enabled = on;
            if (!on) shownItem = null;
        }

        private void ApplyFont(TMP_FontAsset font)
        {
            currentFont = font;
            nameText.font = subText.font = descText.font = font;
            if (visible && shownItem != null) Layout();
        }

        // ───────────────────────── 만들기 ─────────────────────────

        private void Build()
        {
            var cgo = new GameObject("ItemTooltipCanvas", typeof(Canvas), typeof(CanvasScaler));
            cgo.transform.SetParent(transform, false);
            canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 900; // 인벤토리·상점 위
            canvas.enabled = false;
            var scaler = cgo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasRect = (RectTransform)cgo.transform;

            panel = NewRect("Panel", canvasRect);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0f, 1f);
            Image bg = panel.gameObject.AddComponent<Image>();
            bg.sprite = MakePanelSprite();
            bg.type = Image.Type.Sliced;
            bg.color = Color.white;
            bg.raycastTarget = false;

            nameText = NewText("Name", 24f, NameColor, FontStyles.Bold);
            subText = NewText("Category", 17f, SubColor, FontStyles.Normal);
            descText = NewText("Description", 19f, DescColor, FontStyles.Normal);
            descText.lineSpacing = 6f;

            divider = NewRect("Divider", panel);
            TopLeft(divider);
            Image line = divider.gameObject.AddComponent<Image>();
            line.color = new Color(Outline.r, Outline.g, Outline.b, 0.25f);
            line.raycastTarget = false;

            if (TMP_Settings.defaultFontAsset != null) ApplyFont(TMP_Settings.defaultFontAsset);
        }

        private TMP_Text NewText(string name, float size, Color color, FontStyles style)
        {
            RectTransform rt = NewRect(name, panel);
            TopLeft(rt);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.fontSize = size;
            t.color = color;
            t.fontStyle = style;
            t.alignment = TextAlignmentOptions.TopLeft;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            t.richText = true;
            return t;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void TopLeft(RectTransform rt)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
        }

        // 둥근 사각형 + 갈색 테두리 (9칸 늘이기)
        private static Sprite MakePanelSprite()
        {
            const int S = 48, R = 14;
            const float Border = 3f;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, R, S - R), cy = Mathf.Clamp(y + 0.5f, R, S - R);
                    float d = R - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy)); // 가장자리까지 거리
                    float a = Mathf.Clamp01(d + 0.5f);
                    float inside = Mathf.Clamp01(d - Border + 0.5f);
                    Color c = Color.Lerp(Outline, Fill, inside);
                    c.a *= a;
                    px[y * S + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(R + 2, R + 2, R + 2, R + 2));
        }
    }
}