using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using FarmingSystem.Core;
using FarmingSystem.Economy;
using FarmingSystem.Farming;
using FarmingSystem.Fishing;
using FarmingSystem.Inventory;
using FarmingSystem.Save;

namespace FarmingSystem.UI
{
    // 도감 창 (Esc 폰 → 도감 앱). PhoneMenu가 만들고, 열려 있는 동안 매 프레임 Tick()을 불러 줌
    //  ┌──────────────────────────────────────────────────────────┐
    //  │ <  도감   [작물] [물고기 · 준비 중]               등록 12 / 40 │
    //  │ ┌ 칸 목록 (7줄, 스크롤) ──────┐ ┌ 상세 ─────────────────┐ │
    //  │ │ No.01 No.02 ...            │ │ 큰 아이콘  No. 이름 계절  │ │
    //  │ │ (안 모은 작물 = 검은 실루엣) │ │ 성장일 수확량 판매가 씨앗 │ │
    //  │ │                            │ │ 특징 / 장단점 / 이야기   │ │
    //  │ └────────────────────────────┘ └───────────────────────┘ │
    //  └──────────────────────────────────────────────────────────┘
    //  - 순서: 계절(봄→겨울) → 2차 작물 → 1차 작물 → 코드 순. 번호(No.)는 이 순서
    //  - 정보는 작물 데이터(= 엑셀에서 가져온 값): 이름·계절·성장일·수확량·판매가·씨앗 가격·특징·장단점·이야기
    //  - 마우스: 올리면 보기, 휠 = 스크롤 / 키보드: 방향키(WASD)로 고르기, Esc = 폰으로 돌아가기
    //  - 탭: 작물 / 물고기 (탭 클릭 또는 Q·E·Tab 키). 물고기는 처음 잡으면 등록 (FishCollection)
    public class CropCollectionWindow
    {
        private const int Columns = 7;
        private const float Cell = 112f, CellGap = 14f;
        private static readonly Vector2 WindowSize = new Vector2(1560f, 860f);
        private static readonly string[] SeasonNames = { "봄", "여름", "가을", "겨울" };
        private static readonly Color[] SeasonColors =
        {
            new Color(1f, 0.62f, 0.78f), new Color(0.5f, 0.92f, 0.52f), new Color(1f, 0.66f, 0.32f), new Color(0.62f, 0.86f, 1f),
        };
        private static readonly Color Silhouette = new Color(0.02f, 0.05f, 0.1f, 0.92f);

        private class Entry
        {
            public CropData crop;   // 작물 탭
            public FishData fish;   // 물고기 탭
            public ItemData Item => crop != null ? (ItemData)crop : fish;
            public int number;
            public RectTransform rt;
            public Image icon, ring, dot;
            public TextMeshProUGUI no;
        }

        public bool IsOpen { get; private set; }

        private readonly Color holo, ink;
        private readonly TMP_FontAsset font;
        private readonly Sprite rounded, ringSprite;
        private readonly RectTransform root;
        private readonly CanvasGroup group;
        private readonly List<Entry> entries = new List<Entry>();
        private RectTransform content, viewport;
        private ScrollRect scroll;
        private TextMeshProUGUI progress, emptyNote;
        // 상세
        private Image bigIcon;
        private TextMeshProUGUI dNo, dName, dSeason, dBody;
        private TextMeshProUGUI[] statValues, statLabels;
        private bool fishTab;                       // false = 작물, true = 물고기
        private Image[] tabFills, tabRings;
        private TextMeshProUGUI[] tabTexts;
        private static readonly string[] CropStats = { "성장일", "수확량", "판매가", "씨앗 가격" };
        private static readonly string[] FishStats = { "장소", "시간", "크기", "판매가" };
        private int selected;
        private float anim;      // 0 → 1 켜짐, 닫을 땐 1 → 0
        private bool closing;

        public CropCollectionWindow(Transform parent, TMP_FontAsset font, Color holo, Sprite rounded, Sprite ring)
        {
            this.font = font;
            this.holo = holo;
            ink = Color.Lerp(holo, Color.white, 0.75f);
            this.rounded = rounded;
            ringSprite = ring;

            root = Rect("CropCollection", parent, new Vector2(0.5f, 0.5f), Vector2.zero, WindowSize);
            group = root.gameObject.AddComponent<CanvasGroup>();
            Build();
            root.gameObject.SetActive(false);
        }

        // ───────────────────────── 열기·닫기 ─────────────────────────

        public void Open()
        {
            Refresh();
            root.gameObject.SetActive(true);
            root.SetAsLastSibling();
            anim = 0f;
            closing = false;
            IsOpen = true;
        }

        // 지금 탭의 목록·진행도·상세를 다시 그림
        private void Refresh()
        {
            RebuildIfNeeded();
            foreach (Entry e in entries) ShowCell(e);
            int total = entries.Count, got = 0;
            foreach (Entry e in entries) if (Got(e)) got++;
            progress.text = $"등록  <b>{got}</b> / {total}";
            emptyNote.text = fishTab ? "물고기 데이터를 찾지 못했어요.\n(물고기 → 시트에서 가져오기를 한 번 실행해 주세요)"
                                     : "작물 데이터를 찾지 못했어요.\n(작물 관리 → 목록 정리를 한 번 실행해 주세요)";
            emptyNote.gameObject.SetActive(total == 0);
            for (int i = 0; i < statLabels.Length; i++) statLabels[i].text = (fishTab ? FishStats : CropStats)[i];
            for (int i = 0; i < 2; i++)
            {
                bool on = (i == 1) == fishTab;
                tabFills[i].color = new Color(holo.r, holo.g, holo.b, on ? 0.4f : 0.08f);
                tabRings[i].color = new Color(holo.r, holo.g, holo.b, on ? 0.95f : 0.3f);
                tabTexts[i].fontStyle = on ? FontStyles.Bold : FontStyles.Normal;
                tabTexts[i].alpha = on ? 1f : 0.45f;
            }
            Select(Mathf.Clamp(selected, 0, Mathf.Max(0, total - 1)), true);
        }

        private void SetTab(bool fish)
        {
            if (fishTab == fish) return;
            fishTab = fish;
            selected = 0;
            Refresh();
        }

        private static bool Got(Entry e) => e.crop != null ? CropCollection.IsRegistered(e.crop) : FishCollection.IsRegistered(e.fish);

        /// <summary>매 프레임 (PhoneMenu가 부름). 다 닫히면 false</summary>
        public bool Tick(bool esc)
        {
            float dt = Time.unscaledDeltaTime;
            anim = Mathf.Clamp01(anim + (closing ? -dt / 0.15f : dt / 0.25f));
            float k = Mathf.SmoothStep(0f, 1f, anim);
            root.localScale = new Vector3(Mathf.Lerp(0.94f, 1f, k), Mathf.Lerp(0.04f, 1f, k), 1f);
            group.alpha = k * (0.95f + 0.05f * Mathf.PerlinNoise(Time.unscaledTime * 6f, 0.5f));
            group.blocksRaycasts = group.interactable = !closing && anim >= 1f;

            if (closing)
            {
                if (anim > 0f) return true;
                root.gameObject.SetActive(false);
                IsOpen = false;
                return false;
            }
            if (anim < 1f) return true;

            if (esc || Pressed(Key.Backspace) || (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame)) { closing = true; return true; }
            if (Pressed(Key.Q) || Pressed(Key.E) || Pressed(Key.Tab)) SetTab(!fishTab); // 탭 바꾸기
            if (entries.Count > 0)
            {
                int to = selected;
                if (Pressed(Key.RightArrow) || Pressed(Key.D)) to++;
                if (Pressed(Key.LeftArrow) || Pressed(Key.A)) to--;
                if (Pressed(Key.DownArrow) || Pressed(Key.S)) to += Columns;
                if (Pressed(Key.UpArrow) || Pressed(Key.W)) to -= Columns;
                if (to != selected) Select(Mathf.Clamp(to, 0, entries.Count - 1), true);
            }

            // 고른 칸 테두리
            float s = 1f - Mathf.Exp(-14f * dt);
            for (int i = 0; i < entries.Count; i++)
            {
                Color c = entries[i].ring.color;
                c.a = Mathf.Lerp(c.a, i == selected ? 1f : 0.25f, s);
                entries[i].ring.color = c;
                float sc = Mathf.Lerp(entries[i].rt.localScale.x, i == selected ? 1.06f : 1f, s);
                entries[i].rt.localScale = new Vector3(sc, sc, 1f);
            }
            return true;
        }

        private static bool Pressed(Key k) => Keyboard.current != null && Keyboard.current[k].wasPressedThisFrame;

        // ───────────────────────── 목록 ─────────────────────────

        private static List<CropData> AllCrops()
        {
            var list = new List<CropData>();
            if (SaveManager.Instance != null)
                foreach (ItemData it in SaveManager.Instance.Items)
                    if (it is CropData c && !list.Contains(c)) list.Add(c);
            if (list.Count == 0) // 저장 목록이 비어 있으면 불러와진 작물 데이터 전부
                foreach (CropData c in Resources.FindObjectsOfTypeAll<CropData>())
                    if (!list.Contains(c)) list.Add(c);
            list.Sort((a, b) =>
            {
                int s = ((int)a.season).CompareTo((int)b.season);
                if (s != 0) return s;
                int g = IsOld(a).CompareTo(IsOld(b)); // 2차 먼저
                return g != 0 ? g : string.CompareOrdinal(a.name, b.name);
            });
            return list;
        }

        private static bool IsOld(CropData c) => c.name.StartsWith("Crop_old_");

        // 물고기: 바다 → 강 → 어디든, 그 안에서 에셋 이름(Fish_F01 …) 순
        private static List<FishData> AllFish()
        {
            var list = new List<FishData>();
            if (SaveManager.Instance != null)
                foreach (ItemData it in SaveManager.Instance.Items)
                    if (it is FishData f && !list.Contains(f)) list.Add(f);
            if (list.Count == 0)
                foreach (FishData f in Resources.FindObjectsOfTypeAll<FishData>())
                    if (!list.Contains(f)) list.Add(f);
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return list;
        }

        private void RebuildIfNeeded()
        {
            var items = new List<ItemData>();
            if (fishTab) foreach (FishData f in AllFish()) items.Add(f);
            else foreach (CropData c in AllCrops()) items.Add(c);
            bool same = items.Count == entries.Count;
            for (int i = 0; same && i < items.Count; i++) same = entries[i].Item == items[i];
            if (same) return;

            foreach (Entry e in entries) UnityEngine.Object.Destroy(e.rt.gameObject);
            entries.Clear();
            for (int i = 0; i < items.Count; i++) entries.Add(MakeCell(items[i], i));
            content.anchoredPosition = Vector2.zero;
        }

        private Entry MakeCell(ItemData item, int index)
        {
            var e = new Entry { crop = item as CropData, fish = item as FishData, number = index + 1 };
            e.rt = Rect("No" + e.number, content, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(Cell, Cell));
            Image bg = Img(e.rt, rounded, new Color(1f, 1f, 1f, 0.07f));
            bg.raycastTarget = true;
            e.ring = Img(Stretch(Rect("Ring", e.rt, Vector2.one * 0.5f, Vector2.zero, Vector2.zero)), ringSprite, new Color(holo.r, holo.g, holo.b, 0.25f));
            e.ring.raycastTarget = false;
            e.icon = Img(Rect("Icon", e.rt, new Vector2(0.5f, 0.5f), new Vector2(0f, -6f), new Vector2(Cell * 0.66f, Cell * 0.66f)), item.icon, Color.white);
            e.icon.preserveAspect = true;
            e.icon.raycastTarget = false;
            e.no = Text(e.rt, $"{e.number:00}", 17f, new Vector2(-Cell * 0.5f + 26f, Cell * 0.5f - 16f), new Vector2(44f, 22f), TextAlignmentOptions.Left);
            e.no.alpha = 0.7f;
            Color dotColor = e.crop != null ? SeasonColors[(int)e.crop.season]
                           : e.fish.seasons != null && e.fish.seasons.Length > 0 && e.fish.seasons.Length < 4 ? SeasonColors[(int)e.fish.seasons[0]] : Color.white;
            e.dot = Img(Rect("Season", e.rt, new Vector2(0.5f, 0.5f), new Vector2(Cell * 0.5f - 16f, Cell * 0.5f - 16f), new Vector2(12f, 12f)), rounded, dotColor);
            e.dot.raycastTarget = false;
            int i = index;
            var hover = e.rt.gameObject.AddComponent<CollectionCellHover>(); // EventTrigger는 휠을 먹어서 스크롤이 안 됨 → 전용
            hover.onEnter = hover.onClick = () => Select(i, false);
            return e;
        }

        private void ShowCell(Entry e)
        {
            bool got = Got(e);
            e.icon.sprite = e.Item.icon;
            e.icon.enabled = e.Item.icon != null;
            e.icon.color = got ? Color.white : Silhouette;
        }

        private void Select(int index, bool scrollTo)
        {
            if (entries.Count == 0) { ShowDetail(null); return; }
            selected = index;
            ShowDetail(entries[index]);
            if (scrollTo) ScrollTo(index);
        }

        // 고른 칸이 보이게 스크롤
        private void ScrollTo(int index)
        {
            Canvas.ForceUpdateCanvases();
            float rowH = Cell + CellGap;
            int row = index / Columns;
            float top = row * rowH, bottom = top + Cell + 16f; // 위아래 여백(8) 포함
            float y = content.anchoredPosition.y, view = viewport.rect.height;
            if (top < y) y = top;
            else if (bottom > y + view) y = bottom - view;
            y = Mathf.Clamp(y, 0f, Mathf.Max(0f, content.rect.height - view));
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, y);
            scroll.velocity = Vector2.zero;
        }

        // ───────────────────────── 상세 ─────────────────────────

        private void ShowDetail(Entry e)
        {
            if (e == null)
            {
                bigIcon.enabled = false;
                dNo.text = dName.text = dSeason.text = "";
                foreach (var v in statValues) v.text = "-";
                dBody.text = "";
                return;
            }
            bool got = Got(e);
            bigIcon.sprite = e.Item.icon;
            bigIcon.enabled = e.Item.icon != null;
            bigIcon.color = got ? Color.white : Silhouette;
            dNo.text = $"No.{e.number:00}";

            if (!got)
            {
                dName.text = "???";
                dSeason.text = "???";
                foreach (var v in statValues) v.text = "?";
                dBody.text = e.fish != null ? "<alpha=#AA>아직 잡지 않은 물고기예요.\n낚싯대로 잡으면 도감에 등록돼요."
                                            : "<alpha=#AA>아직 수확하지 않은 작물이에요.\n직접 길러서 수확하면 도감에 등록돼요.";
                return;
            }
            if (e.fish != null) { ShowFish(e.fish); return; }
            CropData c = e.crop;

            dName.text = c.cropName;
            dSeason.text = SeasonText(c);
            statValues[0].text = $"{c.totalGrowthDays}일";
            statValues[1].text = $"{c.expectedYield}개";
            statValues[2].text = $"{Mathf.RoundToInt(c.basePrice):N0}벨";
            int seedPrice = SeedPrice(c);
            statValues[3].text = seedPrice >= 0 ? $"{seedPrice:N0}벨" : "-";

            string[] lines = (c.description ?? "").Split('\n');
            string head = ColorUtility.ToHtmlStringRGB(holo);
            var sb = new System.Text.StringBuilder();
            string[] titles = { "특징", "장단점", "이야기" };
            for (int i = 0; i < 3; i++)
            {
                string body = i < lines.Length ? lines[i].Trim() : "";
                if (i == 2 && lines.Length > 3) body = string.Join(" ", lines, 2, lines.Length - 2).Trim();
                if (body.Length == 0) continue;
                if (sb.Length > 0) sb.Append("\n\n");
                sb.Append($"<b><color=#{head}>{titles[i]}</color></b>\n{body}");
            }
            dBody.text = sb.Length > 0 ? sb.ToString() : "<alpha=#AA>설명이 아직 없어요.";
        }

        private void ShowFish(FishData f)
        {
            dName.text = f.itemName;
            string season = f.SeasonText;
            if (f.seasons != null && f.seasons.Length > 0 && f.seasons.Length < 4)
            {
                var parts = new List<string>();
                foreach (Season s in f.seasons) parts.Add($"<color=#{ColorUtility.ToHtmlStringRGB(SeasonColors[(int)s])}>{SeasonNames[(int)s]}</color>");
                season = string.Join("  ·  ", parts);
            }
            dSeason.text = season;
            statValues[0].text = FishData.HabitatName(f.habitat);
            statValues[1].text = f.startHour == f.endHour ? "종일" : $"{f.startHour}~{f.endHour}시";
            statValues[2].text = new string('●', f.shadowSize);
            statValues[3].text = $"{f.basePrice:N0}벨";
            string head = ColorUtility.ToHtmlStringRGB(holo);
            string body = string.IsNullOrWhiteSpace(f.description) ? "<alpha=#AA>설명이 아직 없어요." : f.description.Trim();
            dBody.text = $"<b><color=#{head}>설명</color></b>\n{body}\n\n<b><color=#{head}>희귀도</color></b>\n{FishData.RarityName(f.rarity)}";
        }

        private static string SeasonText(CropData c)
        {
            var list = new List<int> { (int)c.season };
            if (c.additionalSeasons != null)
                foreach (Season s in c.additionalSeasons) if (!list.Contains((int)s)) list.Add((int)s);
            if (list.Count == 4) return "모든 계절";
            var parts = new List<string>();
            foreach (int s in list) parts.Add($"<color=#{ColorUtility.ToHtmlStringRGB(SeasonColors[s])}>{SeasonNames[s]}</color>");
            return string.Join("  ·  ", parts);
        }

        // 상점에서 파는 이 작물의 씨앗 가격 (없으면 -1)
        private static int SeedPrice(CropData crop)
        {
            if (ShopManager.Instance == null) return -1;
            foreach (ShopItemEntry entry in ShopManager.Instance.BuyCatalog)
                if (entry != null && entry.item is SeedData seed && seed.resultCrop == crop) return entry.price;
            return -1;
        }

        // ───────────────────────── 만들기 ─────────────────────────

        private void Build()
        {
            float w = WindowSize.x, h = WindowSize.y;
            Img(Rect("Glow", root, new Vector2(0.5f, 0.5f), Vector2.zero, WindowSize + new Vector2(70f, 70f)), rounded, new Color(holo.r, holo.g, holo.b, 0.07f)).raycastTarget = false;
            Img(Stretch(Rect("Fill", root, Vector2.one * 0.5f, Vector2.zero, Vector2.zero)), rounded, new Color(holo.r * 0.3f, holo.g * 0.45f, holo.b * 0.65f, 0.8f)).raycastTarget = true;
            Img(Stretch(Rect("Border", root, Vector2.one * 0.5f, Vector2.zero, Vector2.zero)), ringSprite, new Color(holo.r, holo.g, holo.b, 0.9f)).raycastTarget = false;

            // 위쪽: 뒤로 · 제목 · 탭 · 진행도
            var back = Rect("Back", root, new Vector2(0f, 1f), new Vector2(64f, -58f), new Vector2(70f, 56f));
            Img(back, rounded, new Color(1f, 1f, 1f, 0f));
            Text(back, "<", 40f, Vector2.zero, back.sizeDelta, TextAlignmentOptions.Center);
            Hover(back.gameObject, null, () => { if (!closing) closing = true; });

            TextMeshProUGUI title = Text(root, "도감", 42f, new Vector2(-w * 0.5f + 170f, h * 0.5f - 58f), new Vector2(160f, 60f), TextAlignmentOptions.Left);
            title.fontStyle = FontStyles.Bold;
            tabFills = new Image[2];
            tabRings = new Image[2];
            tabTexts = new TextMeshProUGUI[2];
            Tab(0, "작물", new Vector2(-w * 0.5f + 360f, h * 0.5f - 58f), () => SetTab(false));
            Tab(1, "물고기", new Vector2(-w * 0.5f + 530f, h * 0.5f - 58f), () => SetTab(true));
            progress = Text(root, "", 28f, new Vector2(w * 0.5f - 190f, h * 0.5f - 58f), new Vector2(300f, 50f), TextAlignmentOptions.Right);
            Img(Rect("Line", root, new Vector2(0.5f, 1f), new Vector2(0f, -112f), new Vector2(w - 60f, 2f)), null, new Color(holo.r, holo.g, holo.b, 0.35f)).raycastTarget = false;

            // 왼쪽: 칸 목록 (스크롤)
            float gridW = Columns * Cell + (Columns - 1) * CellGap;
            var area = Rect("List", root, new Vector2(0f, 1f), Vector2.zero, new Vector2(gridW + 24f, h - 170f));
            area.pivot = new Vector2(0f, 1f);
            area.anchoredPosition = new Vector2(34f, -136f);
            scroll = area.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            scroll.inertia = true;
            viewport = Stretch(Rect("Viewport", area, Vector2.one * 0.5f, Vector2.zero, Vector2.zero));
            viewport.gameObject.AddComponent<RectMask2D>();
            Image vpHit = viewport.gameObject.AddComponent<Image>(); // 휠이 칸 사이에서도 먹게
            vpHit.color = new Color(0f, 0f, 0f, 0f);
            content = Rect("Content", viewport, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(gridW, 0f));
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(Cell, Cell);
            grid.spacing = new Vector2(CellGap, CellGap);
            grid.padding = new RectOffset(12, 12, 8, 8);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = Columns;
            grid.childAlignment = TextAnchor.UpperLeft;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;
            emptyNote = Text(area, "작물 데이터를 찾지 못했어요.\n(작물 관리 → 목록 정리를 한 번 실행해 주세요)", 24f, Vector2.zero, new Vector2(gridW, 120f), TextAlignmentOptions.Center);
            emptyNote.rectTransform.anchorMin = emptyNote.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);

            // 오른쪽: 상세
            float dw = w - (gridW + 24f) - 34f - 60f;
            var d = Rect("Detail", root, new Vector2(1f, 1f), Vector2.zero, new Vector2(dw, h - 170f));
            d.pivot = new Vector2(1f, 1f);
            d.anchoredPosition = new Vector2(-34f, -136f);
            Img(d, rounded, new Color(1f, 1f, 1f, 0.06f)).raycastTarget = false;

            var iconBox = Rect("IconBox", d, new Vector2(0f, 1f), new Vector2(24f + 100f, -24f - 100f), new Vector2(200f, 200f));
            Img(iconBox, rounded, new Color(1f, 1f, 1f, 0.08f)).raycastTarget = false;
            Img(Stretch(Rect("Ring", iconBox, Vector2.one * 0.5f, Vector2.zero, Vector2.zero)), ringSprite, new Color(holo.r, holo.g, holo.b, 0.6f)).raycastTarget = false;
            bigIcon = Img(Rect("Icon", iconBox, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(150f, 150f)), null, Color.white);
            bigIcon.preserveAspect = true;
            bigIcon.raycastTarget = false;

            float tx = 24f + 200f + 24f, tw = dw - tx - 24f;
            dNo = TopLeftText(d, 22f, tx, 34f, tw, 30f);
            dNo.alpha = 0.7f;
            dName = TopLeftText(d, 40f, tx, 66f, tw, 56f);
            dName.fontStyle = FontStyles.Bold;
            dName.enableAutoSizing = true;
            dName.fontSizeMin = 26f;
            dName.fontSizeMax = 40f;
            dSeason = TopLeftText(d, 26f, tx, 132f, tw, 40f);

            // 숫자 4칸
            string[] labels = CropStats;
            statValues = new TextMeshProUGUI[labels.Length];
            statLabels = new TextMeshProUGUI[labels.Length];
            float bw = (dw - 48f - 3f * 10f) / 4f;
            for (int i = 0; i < labels.Length; i++)
            {
                var box = Rect(labels[i], d, new Vector2(0f, 1f), Vector2.zero, new Vector2(bw, 84f));
                box.pivot = new Vector2(0f, 1f);
                box.anchoredPosition = new Vector2(24f + i * (bw + 10f), -246f);
                Img(box, rounded, new Color(1f, 1f, 1f, 0.08f)).raycastTarget = false;
                statLabels[i] = Text(box, labels[i], 19f, new Vector2(0f, 18f), new Vector2(bw, 28f), TextAlignmentOptions.Center);
                statLabels[i].alpha = 0.7f;
                statValues[i] = Text(box, "-", 27f, new Vector2(0f, -14f), new Vector2(bw, 36f), TextAlignmentOptions.Center);
                statValues[i].fontStyle = FontStyles.Bold;
            }

            dBody = TopLeftText(d, 22f, 24f, 356f, dw - 48f, (h - 170f) - 356f - 20f);
            dBody.lineSpacing = 8f;
            dBody.textWrappingMode = TextWrappingModes.Normal;
            dBody.overflowMode = TextOverflowModes.Ellipsis;
        }

        private Sprite tabFill, tabRing; // 탭: 모서리를 덜 둥글게 (네모에 가깝게)

        // 탭 버튼 (클릭 = 그 탭으로, 색은 Refresh에서)
        private void Tab(int index, string label, Vector2 pos, Action click)
        {
            if (tabFill == null) { tabFill = SquareSprite(32, 7f, false); tabRing = SquareSprite(32, 7f, true); }
            var t = Rect(label, root, new Vector2(0.5f, 0.5f), pos, new Vector2(150f, 50f));
            tabFills[index] = Img(t, tabFill, new Color(holo.r, holo.g, holo.b, 0.08f));
            tabFills[index].raycastTarget = true;
            tabRings[index] = Img(Stretch(Rect("Ring", t, Vector2.one * 0.5f, Vector2.zero, Vector2.zero)), tabRing, new Color(holo.r, holo.g, holo.b, 0.3f));
            tabRings[index].raycastTarget = false;
            tabTexts[index] = Text(t, label, 24f, Vector2.zero, t.sizeDelta, TextAlignmentOptions.Center);
            Hover(t.gameObject, null, click);
        }

        // 모서리 반지름 r인 사각형 (ringOnly = 테두리 3px만). 9칸 늘이기라 크기와 상관없이 모서리 유지
        private static Sprite SquareSprite(int size, float r, bool ringOnly)
        {
            const float line = 3f;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float a = Cover(x + 0.5f, y + 0.5f, 0f, size, r);
                    if (ringOnly) a = Mathf.Clamp01(a - Cover(x + 0.5f, y + 0.5f, line, size - line, Mathf.Max(0.5f, r - line)));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            float b = r + 2f;
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }

        private static float Cover(float px, float py, float min, float max, float r)
        {
            float cx = Mathf.Clamp(px, min + r, max - r), cy = Mathf.Clamp(py, min + r, max - r);
            float d = Vector2.Distance(new Vector2(px, py), new Vector2(cx, cy));
            float inBox = Mathf.Clamp01(Mathf.Min(Mathf.Min(px - min, max - px), Mathf.Min(py - min, max - py)) + 0.5f);
            return Mathf.Min(inBox, Mathf.Clamp01(r - d + 0.5f));
        }

        // ───────────────────────── UI 도우미 ─────────────────────────

        private static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        private static RectTransform Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        private static Image Img(RectTransform rt, Sprite sprite, Color color)
        {
            Image img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            if (sprite != null && sprite.border.sqrMagnitude > 0f) img.type = Image.Type.Sliced;
            return img;
        }

        private TextMeshProUGUI Text(Transform parent, string text, float size, Vector2 pos, Vector2 box, TextAlignmentOptions align)
        {
            var rt = Rect("Text", parent, new Vector2(0.5f, 0.5f), pos, box);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = ink;
            t.alignment = align;
            t.raycastTarget = false;
            t.richText = true;
            return t;
        }

        private TextMeshProUGUI TopLeftText(Transform parent, float size, float left, float top, float w, float h)
        {
            TextMeshProUGUI t = Text(parent, "", size, Vector2.zero, new Vector2(w, h), TextAlignmentOptions.TopLeft);
            RectTransform rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(left, -top);
            return t;
        }

        private static void Hover(GameObject go, Action enter, Action click)
        {
            EventTrigger et = go.AddComponent<EventTrigger>();
            Add(et, EventTriggerType.PointerEnter, enter);
            Add(et, EventTriggerType.PointerClick, click);
        }

        private static void Add(EventTrigger et, EventTriggerType type, Action a)
        {
            if (a == null) return;
            var e = new EventTrigger.Entry { eventID = type };
            e.callback.AddListener(_ => a());
            et.triggers.Add(e);
        }
    }

    // 도감 칸: 마우스 올리기·클릭만 받음 (휠은 스크롤로 넘어가게)
    public class CollectionCellHover : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
    {
        public Action onEnter, onClick;
        public void OnPointerEnter(PointerEventData e) => onEnter?.Invoke();
        public void OnPointerClick(PointerEventData e) => onClick?.Invoke();
    }
}