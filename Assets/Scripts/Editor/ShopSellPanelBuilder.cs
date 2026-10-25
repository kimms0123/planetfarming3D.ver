#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FarmingSystem.UI.Shop;

namespace FarmingSystem.EditorTools
{
    // 상점 "팔 때" 창 자동 생성 도구
    // 메뉴: Tools → 행성농사일지 → 상점 판매 창 자동 생성
    // 하는 일:
    //  1) 판매 슬롯 프리팹(ShopSellSlot)과 선택 목록 한 줄 프리팹(SellCartRow)을 Assets/Prefabs/UI/Shop에 만듦
    //  2) 구매 창과 같은 Canvas 아래에 SellPanel(1300x700)을 만들고 ShopSellPanelUI의 모든 칸을 자동 연결
    //  3) 씬의 ShopDialogueUI에 Sell Panel을 자동 연결
    // 레이아웃 (구매 창과 같은 구성): 왼쪽 = 상인 캐릭터(3D 초상화 RawImage + 대사 말풍선)
    //   오른쪽 위 = 오늘의 특별 상품 / 가운데 = 내 인벤토리 30칸(10칸 x 3줄) / 아래 = 선택한 물품 목록 + 예상 금액 + 버튼
    // 색은 핫바 UI 시안(크림·갈색) 톤. 이미지는 유니티 기본 둥근 사각형이라, 디자이너 이미지가 나오면 Source Image만 바꾸면 됨
    // 이미 판매 창이 있으면 지우고 다시 만들지 물어봄
    public static class ShopSellPanelBuilder
    {
        private const string PrefabFolder = "Assets/Prefabs/UI/Shop";

        private static readonly Color Cream = Hex("F6EDD5");
        private static readonly Color CreamDark = Hex("E3D3AE");
        private static readonly Color Tan = Hex("D7C69F");
        private static readonly Color Brown = Hex("C7A588");
        private static readonly Color Border = Hex("B98C74");
        private static readonly Color TextBrown = Hex("5C4030");
        private static readonly Color Green = Hex("6BBF59");
        private static readonly Color Red = Hex("D9776B");
        private static readonly Color White = Hex("FFFDF6");

        private static Sprite rounded;
        private static TMP_FontAsset font;

        [MenuItem("Tools/행성농사일지/상점 판매 창 자동 생성")]
        private static void Build()
        {
            rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

            Canvas canvas = FindCanvas();
            if (canvas == null)
            {
                EditorUtility.DisplayDialog("판매 창 생성", "씬에서 Canvas를 찾지 못했어요. Canvas를 하나 선택하고 다시 실행해 주세요.", "확인");
                return;
            }
            font = FindFont(canvas);

            ShopSellPanelUI existing = Object.FindAnyObjectByType<ShopSellPanelUI>(FindObjectsInactive.Include);
            if (existing != null)
            {
                bool replace = EditorUtility.DisplayDialog("판매 창 생성",
                    $"이미 판매 창({existing.name})이 있어요. 지우고 새로 만들까요?\n(Ctrl+Z로 되돌릴 수 있어요)", "새로 만들기", "취소");
                if (!replace) return;
                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            EnsureFolder(PrefabFolder);
            ShopSellSlotUI slotPrefab = BuildSlotPrefab();
            SellCartRowUI rowPrefab = BuildRowPrefab();
            ShopSellPanelUI panel = BuildPanel(canvas.transform, slotPrefab, rowPrefab);

            ConnectDialogue(panel);

            EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            Selection.activeGameObject = panel.gameObject;
            Debug.Log($"[판매 창 생성] 완료 - Canvas '{canvas.name}' 아래 SellPanel, 프리팹은 {PrefabFolder}");
        }

        // ---------- 찾기 ----------

        // 구매 창이 있는 Canvas를 우선 사용 (같은 화면 구성)
        private static Canvas FindCanvas()
        {
            ShopBuyPanelUI buy = Object.FindAnyObjectByType<ShopBuyPanelUI>(FindObjectsInactive.Include);
            if (buy != null)
            {
                Canvas c = buy.GetComponentInParent<Canvas>(true);
                if (c != null) return c.rootCanvas;
            }
            if (Selection.activeGameObject != null)
            {
                Canvas c = Selection.activeGameObject.GetComponentInParent<Canvas>(true);
                if (c != null) return c.rootCanvas;
            }
            return Object.FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);
        }

        // 이미 쓰고 있는 한글 폰트를 그대로 사용 (없으면 TMP 기본 폰트)
        private static TMP_FontAsset FindFont(Canvas canvas)
        {
            foreach (TextMeshProUGUI t in canvas.GetComponentsInChildren<TextMeshProUGUI>(true))
                if (t.font != null) return t.font;
            return TMP_Settings.defaultFontAsset;
        }

        private static void ConnectDialogue(ShopSellPanelUI panel)
        {
            ShopDialogueUI dialogue = Object.FindAnyObjectByType<ShopDialogueUI>(FindObjectsInactive.Include);
            if (dialogue == null)
            {
                Debug.LogWarning("[판매 창 생성] ShopDialogueUI를 못 찾아서 연결은 직접 해주세요 (Sell Panel 칸에 SellPanel 끌어넣기)");
                return;
            }
            var so = new SerializedObject(dialogue);
            so.FindProperty("sellPanel").objectReferenceValue = panel;
            so.ApplyModifiedProperties();
            Debug.Log($"[판매 창 생성] {dialogue.name}의 Sell Panel에 자동 연결");
        }

        // ---------- 판매 슬롯 프리팹 (84 x 96, 10칸 x 3줄) ----------
        // 아이콘 / 이름(등급 표시 포함) / 오른쪽 위 수량 / 아래 단가 / 선택 테두리 / 특가 배지
        private static ShopSellSlotUI BuildSlotPrefab()
        {
            GameObject root = NewUI("ShopSellSlot", null);
            Size(root, 84f, 96f);
            Image bg = AddImage(root, CreamDark);
            bg.raycastTarget = true; // 클릭 받기
            CanvasGroup group = root.AddComponent<CanvasGroup>();

            // 선택 테두리 (선택 목록에 담기면 켜짐): 초록 판 위에 안쪽 판을 덮어 테두리만 보이게
            GameObject frame = NewUI("SelectedFrame", root.transform);
            Stretch(frame, 4f);
            AddImage(frame, Green);
            GameObject frameInner = NewUI("Inner", frame.transform);
            Stretch(frameInner, -7f);
            AddImage(frameInner, Hex("EAF5E2"));

            GameObject content = NewUI("Content", root.transform);
            Stretch(content, 0f);

            GameObject iconGO = NewUI("Icon", content.transform);
            Place(iconGO, new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(44f, 44f));
            Image icon = AddImage(iconGO, Color.white, false);
            icon.preserveAspect = true;

            TextMeshProUGUI qty = AddText(content.transform, "Quantity", "x0", 14f, TextBrown, TextAlignmentOptions.TopRight);
            Place(qty.gameObject, new Vector2(1f, 1f), new Vector2(-24f, -11f), new Vector2(44f, 18f));
            qty.fontStyle = FontStyles.Bold;

            TextMeshProUGUI nameText = AddText(content.transform, "Name", "작물", 12f, TextBrown, TextAlignmentOptions.Center);
            Place(nameText.gameObject, new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(80f, 18f));
            nameText.overflowMode = TextOverflowModes.Ellipsis;

            TextMeshProUGUI price = AddText(content.transform, "Price", "0벨", 13f, Hex("3E7A34"), TextAlignmentOptions.Center);
            Place(price.gameObject, new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(80f, 18f));
            price.fontStyle = FontStyles.Bold;

            GameObject badge = NewUI("SpecialBadge", root.transform);
            Place(badge, new Vector2(0f, 1f), new Vector2(19f, -9f), new Vector2(34f, 16f));
            AddImage(badge, Red);
            TextMeshProUGUI badgeText = AddText(badge.transform, "Text", "특가", 11f, White, TextAlignmentOptions.Center);
            Stretch(badgeText.gameObject, 0f);
            badgeText.fontStyle = FontStyles.Bold;
            badge.SetActive(false);

            ShopSellSlotUI ui = root.AddComponent<ShopSellSlotUI>();
            var so = new SerializedObject(ui);
            so.FindProperty("contentRoot").objectReferenceValue = content;
            so.FindProperty("itemIcon").objectReferenceValue = icon;
            so.FindProperty("itemNameText").objectReferenceValue = nameText;
            so.FindProperty("quantityText").objectReferenceValue = qty;
            so.FindProperty("priceText").objectReferenceValue = price;
            so.FindProperty("canvasGroup").objectReferenceValue = group;
            so.FindProperty("selectedFrame").objectReferenceValue = frame;
            so.FindProperty("specialBadge").objectReferenceValue = badge;
            so.ApplyModifiedPropertiesWithoutUndo();

            return SavePrefab(root, "ShopSellSlot").GetComponent<ShopSellSlotUI>();
        }

        // ---------- 선택 목록 한 줄 프리팹 (높이 72) ----------
        // 아이콘 / 이름(등급) + @단가 / [-] 수량 [+] / 합계 / [X]
        private static SellCartRowUI BuildRowPrefab()
        {
            GameObject root = NewUI("SellCartRow", null);
            Size(root, 430f, 72f);
            AddImage(root, White);
            LayoutElement le = root.AddComponent<LayoutElement>();
            le.preferredHeight = 72f;
            le.minHeight = 72f;

            GameObject iconGO = NewUI("Icon", root.transform);
            Place(iconGO, new Vector2(0f, 0.5f), new Vector2(36f, 0f), new Vector2(52f, 52f));
            Image icon = AddImage(iconGO, Color.white, false);
            icon.preserveAspect = true;

            TextMeshProUGUI nameText = AddText(root.transform, "Name", "작물", 17f, TextBrown, TextAlignmentOptions.MidlineLeft);
            Place(nameText.gameObject, new Vector2(0f, 0.5f), new Vector2(140f, 12f), new Vector2(136f, 26f));
            nameText.fontStyle = FontStyles.Bold;
            nameText.overflowMode = TextOverflowModes.Ellipsis;

            TextMeshProUGUI unit = AddText(root.transform, "UnitPrice", "@ 0벨", 14f, Hex("8A6E58"), TextAlignmentOptions.MidlineLeft);
            Place(unit.gameObject, new Vector2(0f, 0.5f), new Vector2(140f, -14f), new Vector2(136f, 22f));

            Button minus = MakeButton(root.transform, "Minus", "-", Tan, new Vector2(34f, 34f), 22f);
            Place(minus.gameObject, new Vector2(0f, 0.5f), new Vector2(232f, 0f), new Vector2(34f, 34f));

            TMP_InputField input = MakeInputField(root.transform);
            Place(input.gameObject, new Vector2(0f, 0.5f), new Vector2(278f, 0f), new Vector2(52f, 34f));

            Button plus = MakeButton(root.transform, "Plus", "+", Tan, new Vector2(34f, 34f), 22f);
            Place(plus.gameObject, new Vector2(0f, 0.5f), new Vector2(324f, 0f), new Vector2(34f, 34f));

            TextMeshProUGUI total = AddText(root.transform, "LineTotal", "0벨", 17f, Hex("3E7A34"), TextAlignmentOptions.MidlineRight);
            Place(total.gameObject, new Vector2(1f, 0.5f), new Vector2(-78f, 0f), new Vector2(80f, 30f));
            total.fontStyle = FontStyles.Bold;

            Button remove = MakeButton(root.transform, "Remove", "X", Red, new Vector2(30f, 30f), 16f);
            Place(remove.gameObject, new Vector2(1f, 0.5f), new Vector2(-22f, 0f), new Vector2(30f, 30f));

            SellCartRowUI ui = root.AddComponent<SellCartRowUI>();
            var so = new SerializedObject(ui);
            so.FindProperty("itemIcon").objectReferenceValue = icon;
            so.FindProperty("itemNameText").objectReferenceValue = nameText;
            so.FindProperty("unitPriceText").objectReferenceValue = unit;
            so.FindProperty("lineTotalText").objectReferenceValue = total;
            so.FindProperty("quantityInput").objectReferenceValue = input;
            so.FindProperty("minusButton").objectReferenceValue = minus;
            so.FindProperty("plusButton").objectReferenceValue = plus;
            so.FindProperty("removeButton").objectReferenceValue = remove;
            so.ApplyModifiedPropertiesWithoutUndo();

            return SavePrefab(root, "SellCartRow").GetComponent<SellCartRowUI>();
        }

        // ---------- 판매 창 본체 (1300 x 700) ----------
        private static ShopSellPanelUI BuildPanel(Transform canvas, ShopSellSlotUI slotPrefab, SellCartRowUI rowPrefab)
        {
            GameObject panel = NewUI("SellPanel", canvas);
            Undo.RegisterCreatedObjectUndo(panel, "판매 창 생성");
            Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1300f, 700f));
            AddImage(panel, Border).raycastTarget = true; // 뒤쪽 클릭 막기

            GameObject inner = NewUI("Inner", panel.transform);
            Stretch(inner, -6f);
            AddImage(inner, Cream);

            // 왼쪽: 상인 캐릭터 영역 (구매 창과 같은 구성: 3D 상인 RawImage + 대사 말풍선)
            GameObject character = NewUI("CharacterArea", inner.transform);
            PlaceTL(character, 18f, 18f, 300f, 652f);
            AddImage(character, CreamDark);

            GameObject portraitGO = NewUI("CharacterPortrait", character.transform);
            PlaceTL(portraitGO, 10f, 10f, 280f, 400f);
            RawImage portrait = portraitGO.AddComponent<RawImage>();
            portrait.raycastTarget = false;
            portrait.texture = FindPortraitTexture();
            if (portrait.texture == null) portrait.color = new Color(1f, 1f, 1f, 0.25f);

            GameObject bubble = NewUI("DialogueBubble", character.transform);
            PlaceTL(bubble, 10f, 422f, 280f, 220f);
            AddImage(bubble, White);
            TextMeshProUGUI line = AddText(bubble.transform, "DialogueBubbleText", "어떤 작물을 팔고 싶어?", 22f, TextBrown, TextAlignmentOptions.TopLeft);
            Stretch(line.gameObject, -16f, -14f);
            line.textWrappingMode = TextWrappingModes.Normal;

            // 오른쪽 위: 오늘의 특별 상품
            GameObject special = NewUI("TodaysSpecial", inner.transform);
            PlaceTL(special, 338f, 18f, 932f, 60f);
            AddImage(special, Hex("FBE3C8"));
            GameObject sIconGO = NewUI("Icon", special.transform);
            Place(sIconGO, new Vector2(0f, 0.5f), new Vector2(34f, 0f), new Vector2(46f, 46f));
            Image sIcon = AddImage(sIconGO, Color.white, false);
            sIcon.preserveAspect = true;
            TextMeshProUGUI sText = AddText(special.transform, "Text", "오늘의 특별 상품", 20f, TextBrown, TextAlignmentOptions.MidlineLeft);
            Stretch(sText.gameObject, 0f);
            ((RectTransform)sText.transform).offsetMin = new Vector2(70f, 0f);
            ((RectTransform)sText.transform).offsetMax = new Vector2(-16f, 0f);

            // 가운데: 내 인벤토리 30칸 (핫바처럼 10칸 x 3줄)
            TextMeshProUGUI invTitle = AddText(inner.transform, "InventoryTitle",
                "내 인벤토리   <size=70%><color=#8A6E58>좌클릭 1개 · Shift+클릭 10개 · 우클릭 전부</color></size>",
                22f, TextBrown, TextAlignmentOptions.MidlineLeft);
            PlaceTL(invTitle.gameObject, 342f, 90f, 920f, 32f);
            invTitle.fontStyle = FontStyles.Bold;

            Transform slotContent = MakeScroll(inner.transform, "InventoryScroll", 338f, 126f, 932f, 328f, out _);
            GridLayoutGroup grid = slotContent.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(84f, 96f);
            grid.spacing = new Vector2(7f, 8f);
            grid.padding = new RectOffset(10, 10, 10, 10);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 10;
            AddFitter(slotContent.gameObject);

            // 아래 왼쪽: 선택한 물품
            TextMeshProUGUI cartTitle = AddText(inner.transform, "CartTitle", "선택한 물품", 22f, TextBrown, TextAlignmentOptions.MidlineLeft);
            PlaceTL(cartTitle.gameObject, 342f, 464f, 600f, 30f);
            cartTitle.fontStyle = FontStyles.Bold;

            Transform cartContent = MakeScroll(inner.transform, "CartScroll", 338f, 498f, 612f, 172f, out _);
            VerticalLayoutGroup vlg = cartContent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6f;
            vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            AddFitter(cartContent.gameObject);

            // 아래 오른쪽: 예상 판매 금액 + 버튼
            GameObject totalBox = NewUI("ExpectedTotal", inner.transform);
            PlaceTL(totalBox, 966f, 464f, 304f, 60f);
            AddImage(totalBox, CreamDark);
            TextMeshProUGUI totalLabel = AddText(totalBox.transform, "Label", "예상 판매 금액", 18f, TextBrown, TextAlignmentOptions.MidlineLeft);
            Stretch(totalLabel.gameObject, -14f, 0f);
            TextMeshProUGUI totalText = AddText(totalBox.transform, "Value", "0벨", 26f, Hex("3E7A34"), TextAlignmentOptions.MidlineRight);
            Stretch(totalText.gameObject, -14f, 0f);
            totalText.fontStyle = FontStyles.Bold;

            Button clearAll = MakeButton(inner.transform, "ClearAllButton", "전부 빼기", Brown, new Vector2(148f, 46f), 18f);
            PlaceTL(clearAll.gameObject, 966f, 534f, 148f, 46f);
            Button bargain = MakeButton(inner.transform, "BargainButton", "흥정 (준비 중)", Tan, new Vector2(148f, 46f), 16f);
            PlaceTL(bargain.gameObject, 1122f, 534f, 148f, 46f);
            Button cancel = MakeButton(inner.transform, "CancelButton", "취소", Red, new Vector2(148f, 80f), 22f);
            PlaceTL(cancel.gameObject, 966f, 590f, 148f, 80f);
            Button sell = MakeButton(inner.transform, "SellNowButton", "바로 판매", Green, new Vector2(148f, 80f), 22f);
            PlaceTL(sell.gameObject, 1122f, 590f, 148f, 80f);

            // 스크립트 연결
            ShopSellPanelUI ui = panel.AddComponent<ShopSellPanelUI>();
            var so = new SerializedObject(ui);
            so.FindProperty("panelRoot").objectReferenceValue = panel;
            so.FindProperty("merchantLineText").objectReferenceValue = line;
            so.FindProperty("slotContainer").objectReferenceValue = slotContent;
            so.FindProperty("slotPrefab").objectReferenceValue = slotPrefab;
            so.FindProperty("cartContainer").objectReferenceValue = cartContent;
            so.FindProperty("cartRowPrefab").objectReferenceValue = rowPrefab;
            so.FindProperty("expectedTotalText").objectReferenceValue = totalText;
            so.FindProperty("clearAllButton").objectReferenceValue = clearAll;
            so.FindProperty("sellNowButton").objectReferenceValue = sell;
            so.FindProperty("bargainButton").objectReferenceValue = bargain;
            so.FindProperty("cancelButton").objectReferenceValue = cancel;
            so.FindProperty("specialItemIcon").objectReferenceValue = sIcon;
            so.FindProperty("specialItemText").objectReferenceValue = sText;
            so.ApplyModifiedPropertiesWithoutUndo();

            panel.transform.SetAsLastSibling();
            panel.SetActive(false); // 평소엔 꺼져 있다가 대화창에서 [판매] 누르면 열림
            return ui;
        }

        // 구매 창에서 만든 상인 초상화 Render Texture(Portrait_RT)를 찾아 판매 창에도 같이 씀
        // 같은 Render Texture는 여러 RawImage에 동시에 띄울 수 있어서 카메라를 하나 더 만들 필요 없음
        private static Texture FindPortraitTexture()
        {
            ShopBuyPanelUI buy = Object.FindAnyObjectByType<ShopBuyPanelUI>(FindObjectsInactive.Include);
            if (buy != null)
                foreach (RawImage raw in buy.GetComponentsInChildren<RawImage>(true))
                    if (raw.texture != null) return raw.texture;

            foreach (string guid in AssetDatabase.FindAssets("Portrait_RT t:RenderTexture"))
            {
                var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(AssetDatabase.GUIDToAssetPath(guid));
                if (rt != null) return rt;
            }
            Debug.LogWarning("[판매 창 생성] Portrait_RT를 못 찾았어요. SellPanel → CharacterArea → CharacterPortrait의 Texture에 직접 넣어주세요");
            return null;
        }

        // ---------- UI 부품 ----------

        private static GameObject NewUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        private static Image AddImage(GameObject go, Color color, bool roundedSprite = true)
        {
            Image img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            if (roundedSprite && rounded != null)
            {
                img.sprite = rounded;
                img.type = Image.Type.Sliced;
            }
            return img;
        }

        private static TextMeshProUGUI AddText(Transform parent, string name, string text, float size, Color color, TextAlignmentOptions align)
        {
            GameObject go = NewUI(name, parent);
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            return t;
        }

        private static Button MakeButton(Transform parent, string name, string label, Color color, Vector2 size, float fontSize)
        {
            GameObject go = NewUI(name, parent);
            Size(go, size.x, size.y);
            Image img = AddImage(go, color);
            img.raycastTarget = true;
            Button b = go.AddComponent<Button>();
            b.targetGraphic = img;
            ColorBlock cb = b.colors;
            cb.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            cb.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            cb.disabledColor = new Color(0.75f, 0.75f, 0.75f, 0.55f);
            b.colors = cb;
            TextMeshProUGUI t = AddText(go.transform, "Text", label, fontSize, White, TextAlignmentOptions.Center);
            Stretch(t.gameObject, 0f);
            t.fontStyle = FontStyles.Bold;
            return b;
        }

        private static TMP_InputField MakeInputField(Transform parent)
        {
            GameObject go = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources { inputField = rounded, background = rounded });
            go.name = "QuantityInput";
            go.transform.SetParent(parent, false);
            go.layer = LayerMask.NameToLayer("UI");
            TMP_InputField input = go.GetComponent<TMP_InputField>();
            input.contentType = TMP_InputField.ContentType.IntegerNumber;
            input.characterLimit = 4;
            foreach (TextMeshProUGUI t in go.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (font != null) t.font = font;
                t.fontSize = 18f;
                t.alignment = TextAlignmentOptions.Center;
                t.color = TextBrown;
            }
            if (input.placeholder is TextMeshProUGUI ph) ph.text = "";
            return input;
        }

        // 세로 스크롤 영역. 반환값 = 내용물이 들어갈 Content
        private static Transform MakeScroll(Transform parent, string name, float left, float top, float width, float height, out ScrollRect scroll)
        {
            GameObject go = NewUI(name, parent);
            PlaceTL(go, left, top, width, height);
            Image bg = AddImage(go, Brown);
            bg.raycastTarget = true; // 휠 스크롤 받기
            go.AddComponent<RectMask2D>();

            GameObject content = NewUI("Content", go.transform);
            RectTransform crt = (RectTransform)content.transform;
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = new Vector2(0f, height);

            scroll = go.AddComponent<ScrollRect>();
            scroll.content = crt;
            scroll.viewport = (RectTransform)go.transform;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            return content.transform;
        }

        private static void AddFitter(GameObject go)
        {
            ContentSizeFitter f = go.AddComponent<ContentSizeFitter>();
            f.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private static void Size(GameObject go, float w, float h) => ((RectTransform)go.transform).sizeDelta = new Vector2(w, h);

        private static void Place(GameObject go, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        // 부모 왼쪽 위 기준 위치 (left, top은 아래·오른쪽으로 양수)
        private static void PlaceTL(GameObject go, float left, float top, float width, float height)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(left, -top);
            rt.sizeDelta = new Vector2(width, height);
        }

        private static void Stretch(GameObject go, float inset) => Stretch(go, inset, inset);

        // inset이 음수면 부모보다 안쪽으로 들어감 (-6 = 6픽셀 안쪽)
        private static void Stretch(GameObject go, float insetX, float insetY)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(-insetX, -insetY);
            rt.offsetMax = new Vector2(insetX, insetY);
        }

        private static GameObject SavePrefab(GameObject root, string name)
        {
            string path = $"{PrefabFolder}/{name}.prefab";
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return asset;
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out Color c);
            return c;
        }
    }
}
#endif