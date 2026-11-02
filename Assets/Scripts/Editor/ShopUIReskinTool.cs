#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FarmingSystem.UI.Shop;

namespace FarmingSystem.EditorTools
{
    // 상점 UI에 그래픽 팀 UI(UI_Blue 조각)를 입히는 도구
    // 메뉴: Tools → 행성농사일지 → UI → 새 UI 입히기 - 상점 대화창 / 판매 창 / 구매 창 / 상점 전부
    //
    // 하는 일
    // - Sprite Editor에서 이름 붙인 조각(Panel_Dialogue, Pill_Yellow ...)을 프로젝트에서 찾아 UI의 Image에 넣음
    // - Color를 흰색으로 (예전 색이 새 그림에 덧칠되지 않게), 9-slice 조각은 Sliced로
    // - 작은 칸에서 테두리가 너무 두꺼우면 Pixels Per Unit Multiplier를 자동으로 올림
    // - 새 그림에 테두리가 이미 있으니 예전 '안쪽 판'(Fill / Inner)은 숨김
    // - 밝은 바탕 위 글자는 진한 갈색으로
    //
    // 오브젝트는 이름이 아니라 스크립트(ShopConversationUI, ShopSellPanelUI, ShopBuyPanelUI)에 연결된 참조로 찾음
    // → 이름을 바꿨어도 대부분 찾아감. 못 찾은 건 건너뛰고 Console에 알려 줌
    //
    // 되돌리기
    // - 씬에 있는 UI: Ctrl+Z 한 번으로 전부 되돌아감
    // - 프리팹(판매 칸 ShopSellSlot, 판매 줄 SellCartRow, 구매 칸 ShopBuySlot): 바로 저장돼서 Ctrl+Z가 안 됨
    //   → 실행 전에 확인 창이 뜸. 걱정되면 프리팹을 Ctrl+D로 복사해 두고 실행
    public static class ShopUIReskinTool
    {
        // 밝은 바탕 위 글자색 (UI_Blue 테두리선과 어울리는 진한 갈색)
        private static readonly Color TextBrown = new Color32(0x5B, 0x4A, 0x3A, 0xFF);

        private static readonly string[] SpriteNames =
        {
            "Panel_Dialogue", "Window_Header", "Row_Plain", "Row_Icon",
            "Slot_Light", "Slot_Dark", "Slot_Big_Plain", "Frame_Select",
            "Pill_Yellow", "Pill_Grey", "Pill_Green", "Button_Rect_Grey",
            "Button_Round_White", "Button_Cancel", "Arrow_Down",
            "Icon_Star", "Bubble_White",
        };

        private static Dictionary<string, Sprite> sprites;
        private static bool editingPrefab; // 프리팹 편집 중엔 Undo 기록 안 함 (프리팹은 바로 저장되므로)
        private static StringBuilder log;
        private static int changed;

        // ───────────────────────── 메뉴 ─────────────────────────

        [MenuItem("Tools/행성농사일지/UI/새 UI 입히기 - 상점 대화창")]
        private static void MenuConversation() => Run(conversation: true, sell: false, buy: false);

        [MenuItem("Tools/행성농사일지/UI/새 UI 입히기 - 판매 창")]
        private static void MenuSell() => Run(conversation: false, sell: true, buy: false);

        [MenuItem("Tools/행성농사일지/UI/새 UI 입히기 - 구매 창")]
        private static void MenuBuy() => Run(conversation: false, sell: false, buy: true);

        [MenuItem("Tools/행성농사일지/UI/새 UI 입히기 - 상점 전부 (대화창 + 판매 + 구매)")]
        private static void MenuAll() => Run(conversation: true, sell: true, buy: true);

        private static void Run(bool conversation, bool sell, bool buy)
        {
            if (!LoadSprites()) return;

            if ((sell || buy) && !EditorUtility.DisplayDialog("새 UI 입히기",
                    "판매 칸·판매 줄·구매 칸 프리팹도 같이 바뀌어요.\n프리팹은 바로 저장돼서 Ctrl+Z로 되돌릴 수 없어요.\n(씬 UI는 Ctrl+Z로 되돌릴 수 있어요)\n\n계속할까요?",
                    "계속", "취소"))
                return;

            log = new StringBuilder();
            changed = 0;
            Undo.SetCurrentGroupName("새 UI 입히기");
            int group = Undo.GetCurrentGroup();

            if (conversation) ReskinConversation();
            if (sell) ReskinSellPanel();
            if (buy) ReskinBuyPanel();

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Debug.Log($"[UI 입히기] 끝 - {changed}곳 바꿈\n{log}");
        }

        // ───────────────────────── 상점 대화창 ─────────────────────────

        private static void ReskinConversation()
        {
            Section("상점 대화창");
            ShopConversationUI view = Object.FindAnyObjectByType<ShopConversationUI>(FindObjectsInactive.Include);
            if (view == null) { Warn("ShopConversationUI를 씬에서 못 찾음 → 건너뜀 (상점 대화창 만들기를 먼저 했는지 확인)"); return; }

            var so = new SerializedObject(view);
            var nameTag = so.FindProperty("nameTagObject").objectReferenceValue as GameObject;
            var nameLabel = so.FindProperty("nameLabel").objectReferenceValue as TMP_Text;
            var lineLabel = so.FindProperty("lineLabel").objectReferenceValue as TMP_Text;
            var next = so.FindProperty("nextIndicator").objectReferenceValue as Graphic;
            var choiceBox = so.FindProperty("choiceBox").objectReferenceValue as RectTransform;

            // 대화 상자 = 대사 글자의 부모
            if (lineLabel != null)
            {
                Image box = lineLabel.transform.parent != null ? lineLabel.transform.parent.GetComponent<Image>() : null;
                if (box != null)
                {
                    Apply(box, "Panel_Dialogue");
                    HideChild(box.transform, "Fill");
                }
                else Warn("대사 글자(Line)의 부모에 Image가 없음 → 대화 상자 건너뜀");
                TextColor(lineLabel);
            }
            else Warn("ShopConversationUI의 Line Label이 비어 있음");

            // ▼ 다음 표시
            if (next is Image nextImg)
            {
                Apply(nextImg, "Arrow_Down", allowSliced: false);
                nextImg.preserveAspect = true;
                RectTransform r = nextImg.rectTransform;
                Record(r, "새 UI 입히기");
                r.localRotation = Quaternion.identity; // 예전 화살표는 돌려서 ▼로 썼음. 새 그림은 원래 아래 방향
                if (r.sizeDelta.x < 36f) r.sizeDelta = new Vector2(36f, 38f);
            }

            // 이름표
            if (nameTag != null && nameTag.TryGetComponent(out Image tagImg)) Apply(tagImg, "Pill_Yellow");
            if (nameLabel != null) TextColor(nameLabel);

            // 선택지 상자
            if (choiceBox != null)
            {
                if (choiceBox.TryGetComponent(out Image choiceImg)) Apply(choiceImg, "Panel_Dialogue");
                HideChild(choiceBox, "Fill");

                // 선택지 줄: 평소엔 투명, 고르면 노란 알약
                foreach (ShopChoiceRowUI row in choiceBox.GetComponentsInChildren<ShopChoiceRowUI>(true))
                {
                    var rso = new SerializedObject(row);
                    var back = rso.FindProperty("background").objectReferenceValue as Image;
                    var label = rso.FindProperty("label").objectReferenceValue as TMP_Text;
                    var cursor = rso.FindProperty("cursor").objectReferenceValue as Graphic;
                    if (back != null) Apply(back, "Pill_Yellow", keepAlpha: true);
                    if (label != null) TextColor(label);
                    // 커서는 기존 삼각형(ShopArrow) 그대로 두고 색만 글자색과 맞춤
                    if (cursor != null)
                    {
                        Record(cursor, "새 UI 입히기");
                        cursor.color = TextBrown;
                    }
                }
            }

            // 고른 줄 색 = 흰색(알약 그림 그대로), 글자 = 갈색
            Record(view, "새 UI 입히기");
            so.Update();
            so.FindProperty("selectedColor").colorValue = Color.white;
            so.FindProperty("selectedTextColor").colorValue = TextBrown;
            so.FindProperty("textColor").colorValue = TextBrown;
            so.ApplyModifiedProperties();
            Note("ShopConversationUI: Selected Color = 흰색, Selected Text Color·Text Color = 갈색");
        }

        // ───────────────────────── 판매 창 ─────────────────────────

        private static void ReskinSellPanel()
        {
            Section("판매 창");
            ShopSellPanelUI ui = Object.FindAnyObjectByType<ShopSellPanelUI>(FindObjectsInactive.Include);
            if (ui == null) { Warn("ShopSellPanelUI를 씬에서 못 찾음 → 건너뜀"); return; }
            var so = new SerializedObject(ui);
            GameObject root = so.FindProperty("panelRoot").objectReferenceValue as GameObject ?? ui.gameObject;

            ReskinContentFrame(root.transform);
            ReskinBubble(so.FindProperty("merchantLineText").objectReferenceValue as TMP_Text);

            // 오늘의 특별 상품 줄 = 아이콘의 부모
            if (so.FindProperty("specialItemIcon").objectReferenceValue is Image sIcon && sIcon.transform.parent != null
                && sIcon.transform.parent.TryGetComponent(out Image special))
                Apply(special, "Pill_Yellow");

            // 예상 판매 금액 칸 = 금액 글자의 부모
            if (so.FindProperty("expectedTotalText").objectReferenceValue is TMP_Text total && total.transform.parent != null
                && total.transform.parent.TryGetComponent(out Image totalBox))
                Apply(totalBox, "Row_Plain");

            ButtonSkin(so, "clearAllButton", "Pill_Grey");
            ButtonSkin(so, "bargainButton", "Pill_Yellow");
            ButtonSkin(so, "cancelButton", "Pill_Grey");
            ButtonSkin(so, "sellNowButton", "Pill_Green");

            // 프리팹: 판매 칸
            if (so.FindProperty("slotPrefab").objectReferenceValue is ShopSellSlotUI slot)
                EditPrefab(slot.gameObject, prefabRoot =>
                {
                    var sso = new SerializedObject(prefabRoot.GetComponent<ShopSellSlotUI>());
                    if (prefabRoot.TryGetComponent(out Image bg)) Apply(bg, "Slot_Light");
                    if (sso.FindProperty("selectedFrame").objectReferenceValue is GameObject frame)
                    {
                        if (frame.TryGetComponent(out Image fImg)) { Apply(fImg, "Frame_Select"); fImg.fillCenter = false; }
                        HideChild(frame.transform, "Inner");
                    }
                    if (sso.FindProperty("specialBadge").objectReferenceValue is GameObject badge)
                    {
                        if (badge.TryGetComponent(out Image bImg)) { Apply(bImg, "Icon_Star", allowSliced: false); bImg.preserveAspect = true; }
                        HideChild(badge.transform, "Text");
                    }
                });
            else Warn("판매 창의 Slot Prefab이 비어 있음 → 판매 칸 건너뜀");

            // 프리팹: 선택한 물품 한 줄
            if (so.FindProperty("cartRowPrefab").objectReferenceValue is SellCartRowUI cartRow)
                EditPrefab(cartRow.gameObject, prefabRoot =>
                {
                    var cso = new SerializedObject(prefabRoot.GetComponent<SellCartRowUI>());
                    if (prefabRoot.TryGetComponent(out Image bg)) Apply(bg, "Row_Plain");
                    ButtonSkin(cso, "minusButton", "Button_Round_White", round: true);
                    ButtonSkin(cso, "plusButton", "Button_Round_White", round: true);
                    if (cso.FindProperty("removeButton").objectReferenceValue is Button remove)
                    {
                        if (remove.targetGraphic is Image rImg) { Apply(rImg, "Button_Cancel", allowSliced: false); rImg.preserveAspect = true; }
                        HideChild(remove.transform, "Text");
                    }
                    InputSkin(cso, "quantityInput");
                });
            else Warn("판매 창의 Cart Row Prefab이 비어 있음 → 판매 줄 건너뜀");
        }

        // ───────────────────────── 구매 창 ─────────────────────────

        private static void ReskinBuyPanel()
        {
            Section("구매 창");
            ShopBuyPanelUI ui = Object.FindAnyObjectByType<ShopBuyPanelUI>(FindObjectsInactive.Include);
            if (ui == null) { Warn("ShopBuyPanelUI를 씬에서 못 찾음 → 건너뜀"); return; }
            var so = new SerializedObject(ui);
            GameObject root = so.FindProperty("panelRoot").objectReferenceValue as GameObject ?? ui.gameObject;

            ReskinContentFrame(root.transform);
            ReskinBubble(so.FindProperty("merchantLineText").objectReferenceValue as TMP_Text);

            // 탭: 버튼 바탕 = 회색 알약, 고른 표시(activeVisual) = 노란 알약
            SerializedProperty tabs = so.FindProperty("tabs");
            for (int i = 0; tabs != null && i < tabs.arraySize; i++)
            {
                if (!(tabs.GetArrayElementAtIndex(i).objectReferenceValue is ShopTabButtonUI tab)) continue;
                var tso = new SerializedObject(tab);
                if (tso.FindProperty("button").objectReferenceValue is Button b && b.targetGraphic is Image tImg)
                {
                    Apply(tImg, "Pill_Grey");
                    foreach (TMP_Text t in b.GetComponentsInChildren<TMP_Text>(true)) TextColor(t);
                }
                if (tso.FindProperty("activeVisual").objectReferenceValue is GameObject active && active.TryGetComponent(out Image aImg))
                {
                    // 탭을 덮는 크기일 때만 노란 알약으로 (밑줄 같은 얇은 표시면 모양이 망가지니 건너뜀)
                    RectTransform tabRect = (RectTransform)tab.transform;
                    if (aImg.rectTransform.rect.height >= tabRect.rect.height * 0.6f) Apply(aImg, "Pill_Yellow");
                    else Warn($"탭 '{tab.name}'의 Active Visual이 얇은 표시(높이 {aImg.rectTransform.rect.height:0})라 건너뜀");
                }
            }

            ButtonSkin(so, "minusButton", "Button_Round_White", round: true);
            ButtonSkin(so, "plusButton", "Button_Round_White", round: true);
            ButtonSkin(so, "buyButton", "Pill_Green");
            ButtonSkin(so, "cancelButton", "Pill_Grey");
            InputSkin(so, "quantityInput");

            // 프리팹: 구매 칸 (가로로 긴 줄이면 Row_Icon, 네모 칸이면 Slot_Light)
            if (so.FindProperty("slotPrefab").objectReferenceValue is ShopBuySlotUI slot)
                EditPrefab(slot.gameObject, prefabRoot =>
                {
                    var sso = new SerializedObject(prefabRoot.GetComponent<ShopBuySlotUI>());
                    if (prefabRoot.TryGetComponent(out Image bg))
                    {
                        Rect r = ((RectTransform)prefabRoot.transform).rect;
                        LayoutElement le = prefabRoot.GetComponent<LayoutElement>();
                        float w = r.width > 1f ? r.width : (le != null ? le.preferredWidth : 0f);
                        float h = r.height > 1f ? r.height : (le != null ? le.preferredHeight : 0f);
                        bool row = w > 0f && h > 0f && w > h * 2f;
                        Apply(bg, row ? "Row_Plain" : "Slot_Light");
                        if (row) Note($"구매 칸이 가로로 긴 줄({w:0}x{h:0})이라 Row_Plain으로 넣음 (Row_Icon은 파란 칸 위치가 지금 아이콘 위치와 안 맞을 수 있음)");
                    }
                    if (sso.FindProperty("selectedFrame").objectReferenceValue is GameObject frame)
                    {
                        if (frame.TryGetComponent(out Image fImg)) { Apply(fImg, "Frame_Select"); fImg.fillCenter = false; }
                        HideChild(frame.transform, "Inner");
                    }
                });
            else Warn("구매 창의 Slot Prefab이 비어 있음 → 구매 칸 건너뜀");
        }

        // ───────────────────────── 공통 부품 ─────────────────────────

        // 오른쪽 품목 창 ContentFrame → Window_Header, 안쪽 판 Inner의 바탕만 끔 (안의 내용은 그대로 보이게)
        private static void ReskinContentFrame(Transform panelRoot)
        {
            Transform frame = FindDeep(panelRoot, "ContentFrame");
            if (frame == null) { Warn($"'{panelRoot.name}' 안에서 ContentFrame을 못 찾음 → 품목 창 바탕 건너뜀"); return; }
            if (frame.TryGetComponent(out Image img)) Apply(img, "Window_Header");
            Transform inner = frame.Find("Inner");
            if (inner != null && inner.TryGetComponent(out Image innerImg))
            {
                Record(innerImg, "새 UI 입히기");
                innerImg.enabled = false;
                Note($"{Path(inner)} 바탕 Image 끔 (오브젝트는 켜 둠)");
            }
        }

        // 상인 말풍선 = 대사 글자의 부모 Image (말풍선 꼬리가 찌그러지지 않게 비율 유지)
        private static void ReskinBubble(TMP_Text line)
        {
            if (line == null) { Warn("상인 대사(Merchant Line Text)가 비어 있음 → 말풍선 건너뜀"); return; }
            TextColor(line);
            Image bubble = line.transform.parent != null ? line.transform.parent.GetComponent<Image>() : null;
            if (bubble == null) { Warn($"'{Path(line.transform)}'의 부모에 Image가 없음 → 말풍선 건너뜀"); return; }
            Apply(bubble, "Bubble_White", allowSliced: false);
            bubble.preserveAspect = true;
            HideChild(bubble.transform, "Fill");
            HideChild(bubble.transform, "Inner");
        }

        private static void ButtonSkin(SerializedObject so, string field, string sprite, bool round = false)
        {
            if (!(so.FindProperty(field)?.objectReferenceValue is Button b)) { Warn($"{so.targetObject.name}의 {field}가 비어 있음 → 건너뜀"); return; }
            if (b.targetGraphic is Image img)
            {
                Apply(img, sprite, allowSliced: !round);
                if (round) img.preserveAspect = true;
            }
            foreach (TMP_Text t in b.GetComponentsInChildren<TMP_Text>(true)) TextColor(t);
        }

        private static void InputSkin(SerializedObject so, string field)
        {
            if (!(so.FindProperty(field)?.objectReferenceValue is TMP_InputField input)) return;
            if (input.TryGetComponent(out Image img)) Apply(img, "Button_Rect_Grey");
            if (input.textComponent != null) TextColor(input.textComponent);
        }

        // Image에 조각 넣기: 색 흰색, 9-slice 조각이면 Sliced, 작은 칸이면 테두리 배율 자동
        private static void Apply(Image img, string spriteName, bool allowSliced = true, bool keepAlpha = false)
        {
            if (img == null) return;
            if (!sprites.TryGetValue(spriteName, out Sprite sp) || sp == null) { Warn($"'{spriteName}' 조각이 없음 → {Path(img.transform)} 건너뜀"); return; }

            Record(img, "새 UI 입히기");
            img.sprite = sp;
            img.color = keepAlpha ? new Color(1f, 1f, 1f, img.color.a) : Color.white;

            bool sliced = allowSliced && sp.border.sqrMagnitude > 0f;
            img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            if (sliced)
            {
                img.fillCenter = true;
                img.pixelsPerUnitMultiplier = AutoMultiplier(img, sp);
            }
            changed++;
            Note($"{Path(img.transform)} ← {spriteName}" + (sliced ? $" (Sliced, 배율 {img.pixelsPerUnitMultiplier:0.#})" : ""));
        }

        // 칸 크기에 비해 테두리가 너무 크면 배율을 올려 테두리를 얇게
        private static float AutoMultiplier(Image img, Sprite sp)
        {
            Rect r = img.rectTransform.rect;
            LayoutElement le = img.GetComponent<LayoutElement>();
            float w = r.width > 1f ? r.width : (le != null && le.preferredWidth > 0f ? le.preferredWidth : 0f);
            float h = r.height > 1f ? r.height : (le != null && le.preferredHeight > 0f ? le.preferredHeight : 0f);
            if (w <= 0f || h <= 0f) return 1f;

            Vector4 b = sp.border; // x = 왼, y = 아래, z = 오른, w = 위
            float m = 1f;
            if (b.y <= 0f && b.w <= 0f)
            {
                // 알약 모양(위아래 경계 0): 높이에 맞춰 줄여야 둥근 끝이 동그랗게 유지됨
                m = Mathf.Max(m, sp.rect.height / h);
            }
            else
            {
                // 테두리가 칸의 절반 넘게 차지하지 않게
                m = Mathf.Max(m, (b.x + b.z) / (w * 0.5f), (b.y + b.w) / (h * 0.5f));
            }
            return Mathf.Round(m * 10f) / 10f;
        }

        private static void TextColor(TMP_Text t)
        {
            if (t == null) return;
            Record(t, "새 UI 입히기");
            t.color = TextBrown;
        }

        // 예전 '안쪽 판' 숨기기 (새 그림에 테두리가 이미 있음)
        private static void HideChild(Transform parent, string childName)
        {
            Transform c = parent.Find(childName);
            if (c == null || !c.gameObject.activeSelf) return;
            Record(c.gameObject, "새 UI 입히기");
            c.gameObject.SetActive(false);
            Note($"{Path(c)} 숨김");
        }

        // 프리팹 열어서 고치고 저장
        private static void EditPrefab(GameObject prefabAsset, System.Action<GameObject> edit)
        {
            string path = AssetDatabase.GetAssetPath(prefabAsset);
            if (string.IsNullOrEmpty(path)) { Warn($"'{prefabAsset.name}'이(가) 프리팹 에셋이 아님 → 건너뜀"); return; }
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            editingPrefab = true;
            try
            {
                Note($"[프리팹] {path}");
                edit(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                editingPrefab = false;
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ───────────────────────── 조각 찾기 ─────────────────────────

        // Sprite Editor에서 이름 붙인 조각을 프로젝트 전체에서 찾음 (UI_Blue가 어느 폴더에 있든 상관없음)
        private static bool LoadSprites()
        {
            sprites = new Dictionary<string, Sprite>();
            var wanted = new HashSet<string>(SpriteNames);
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/")) continue;
                foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (o is Sprite s && wanted.Contains(s.name) && !sprites.ContainsKey(s.name))
                        sprites[s.name] = s;
                }
                if (sprites.Count == wanted.Count) break;
            }

            var missing = new List<string>();
            foreach (string n in SpriteNames) if (!sprites.ContainsKey(n)) missing.Add(n);
            if (missing.Count == SpriteNames.Length)
            {
                EditorUtility.DisplayDialog("새 UI 입히기",
                    "UI 조각을 하나도 못 찾았어요.\nUI_Blue를 Sprite Editor에서 자르고 이름(Panel_Dialogue 등)을 붙인 뒤 Apply 했는지 확인해 주세요.", "확인");
                return false;
            }
            if (missing.Count > 0)
            {
                bool go = EditorUtility.DisplayDialog("새 UI 입히기",
                    "이 이름의 조각을 못 찾았어요 (이 조각이 들어갈 곳만 건너뛰어요):\n\n" + string.Join(", ", missing) +
                    "\n\nSprite Editor에서 이름 철자를 확인해 주세요.", "나머지만 진행", "취소");
                if (!go) return false;
            }
            return true;
        }

        // ───────────────────────── 작은 도우미 ─────────────────────────

        private static void Record(Object o, string _)
        {
            if (!editingPrefab && o != null) Undo.RecordObject(o, "새 UI 입히기");
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                Transform f = FindDeep(c, name);
                if (f != null) return f;
            }
            return null;
        }

        private static string Path(Transform t)
        {
            string p = t.name;
            while (t.parent != null && p.Length < 120) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }

        private static void Section(string title) => log.AppendLine($"── {title} ──");
        private static void Note(string msg) => log.AppendLine("  " + msg);
        private static void Warn(string msg) => log.AppendLine("  ⚠ " + msg);
    }
}
#endif