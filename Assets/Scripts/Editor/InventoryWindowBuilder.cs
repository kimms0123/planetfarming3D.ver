#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FarmingSystem.Player;
using FarmingSystem.UI;

namespace FarmingSystem.EditorTools
{
    // 인벤토리 창 만들기
    // 메뉴: Tools → 행성농사일지 → UI → 인벤토리 창 만들기
    // 만드는 것
    // - 칸 프리팹: Assets/Prefabs/UI/Inventory/InventorySlot.prefab (아이콘·수량·등급 별·단축키·선택 테두리)
    // - 핫바가 있는 Canvas 안에 InventoryWindow (가운데 창)
    //     왼쪽 위: 캐릭터(플레이어 3D 모델) + 이름 + 돈 / 왼쪽 아래: 도구칸 4칸
    //     오른쪽: 핫바 줄(노란 띠, 1~0) + 가방 2줄 + 설명줄
    // - 캐릭터 전용 카메라(InventoryCharacterCamera)와 RenderTexture (Assets/UI/Inventory)
    // - 그래픽 팀 UI 조각(Panel_Dialogue, Slot_Light ...)을 이름으로 찾아 입힘. 없으면 단색으로 만듦
    // 만든 뒤 씬에서 위치·크기·색을 마음대로 바꾸면 됨 (다시 만들면 직접 고친 건 사라짐)
    public static class InventoryWindowBuilder
    {
        private const string PrefabFolder = "Assets/Prefabs/UI/Inventory";
        private const string AssetFolder = "Assets/UI/Inventory";
        private static readonly Color Brown = new Color32(0x5B, 0x4A, 0x3A, 0xFF);

        private static Dictionary<string, Sprite> sprites;
        private static TMP_FontAsset font;

        [MenuItem("Tools/행성농사일지/UI/인벤토리 창 만들기")]
        private static void Build()
        {
            Canvas canvas = FindCanvas();
            if (canvas == null)
            {
                EditorUtility.DisplayDialog("인벤토리 창 만들기", "화면용 Canvas를 찾지 못했어요. 핫바가 있는 게임 씬을 열고 다시 실행해 주세요.", "확인");
                return;
            }

            Transform existing = canvas.transform.Find("InventoryWindow");
            if (existing != null)
            {
                if (!EditorUtility.DisplayDialog("인벤토리 창 만들기",
                        $"'{canvas.name}' 안에 이미 InventoryWindow가 있어요.\n지우고 새로 만들까요? (직접 고친 모양은 사라져요)", "새로 만들기", "취소"))
                    return;
                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            LoadSprites();
            font = FindFont();
            EnsureFolder(PrefabFolder);
            EnsureFolder(AssetFolder);

            InventorySlotView slotPrefab = BuildSlotPrefab();

            // ── 뿌리 (항상 켜져 있음) ──
            GameObject root = NewUI("InventoryWindow", canvas.transform);
            Undo.RegisterCreatedObjectUndo(root, "인벤토리 창 만들기");
            Stretch(root);
            root.transform.SetAsLastSibling();

            // ── 켜고 끄는 창 ──
            GameObject content = NewUI("Content", root.transform);
            Stretch(content);

            GameObject dim = NewUI("Dim", content.transform);
            Stretch(dim);
            AddImage(dim, null, new Color(0.05f, 0.03f, 0.1f, 0.35f)).raycastTarget = true; // 뒤쪽 클릭 막기

            // 가운데 판 1480 x 580
            GameObject panel = NewUI("Panel", content.transform);
            Place(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(1480f, 580f));
            AddImage(panel, "Panel_Dialogue", new Color(0.85f, 0.88f, 0.86f, 1f), 1.5f).raycastTarget = true;
            panel.AddComponent<CanvasGroup>();

            TMP_Text title = AddText(panel.transform, "Title", "가방", 40f, Brown, TextAlignmentOptions.MidlineLeft);
            PlaceTL(title.gameObject, 40f, 18f, 300f, 54f);
            title.fontStyle = FontStyles.Bold;
            TMP_Text hint = AddText(panel.transform, "Hint", "Tab · Esc 닫기", 22f, Brown, TextAlignmentOptions.MidlineRight);
            Place(hint.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-100f, -26f), new Vector2(300f, 40f));

            GameObject close = NewUI("CloseButton", panel.transform);
            Place(close, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -20f), new Vector2(56f, 56f));
            Image closeImg = AddImage(close, "Button_Cancel", new Color(0.9f, 0.4f, 0.4f, 1f));
            closeImg.preserveAspect = true;
            Button closeBtn = close.AddComponent<Button>();
            closeBtn.targetGraphic = closeImg;

            // ── 왼쪽 위: 캐릭터 + 정보 ──
            GameObject charBox = NewUI("CharacterBox", panel.transform);
            PlaceTL(charBox, 32f, 80f, 400f, 300f);
            AddImage(charBox, "Slot_Big_Plain", new Color(0.93f, 0.95f, 0.94f, 1f), 3f);

            RenderTexture rt = MakeRenderTexture();
            GameObject view = NewUI("CharacterView", charBox.transform);
            RectTransform vr = (RectTransform)view.transform;
            vr.anchorMin = Vector2.zero; vr.anchorMax = Vector2.one;
            vr.offsetMin = new Vector2(12f, 72f); vr.offsetMax = new Vector2(-12f, -12f);
            RawImage raw = view.AddComponent<RawImage>();
            raw.texture = rt;
            raw.raycastTarget = true; // 끌어서 돌리기
            InventoryCharacterView charView = view.AddComponent<InventoryCharacterView>();

            TMP_Text nameText = AddText(charBox.transform, "Name", "농부", 30f, Brown, TextAlignmentOptions.MidlineLeft);
            Place(nameText.gameObject, Vector2.zero, Vector2.zero, new Vector2(24f, 16f), new Vector2(200f, 44f));
            nameText.fontStyle = FontStyles.Bold;
            GameObject coin = NewUI("CoinIcon", charBox.transform);
            Place(coin, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-176f, 20f), new Vector2(36f, 36f));
            AddImage(coin, "Icon_Coin", new Color(1f, 0.8f, 0.4f, 1f)).preserveAspect = true;
            TMP_Text money = AddText(charBox.transform, "Money", "0", 30f, Brown, TextAlignmentOptions.MidlineRight);
            Place(money.gameObject, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 16f), new Vector2(150f, 44f));
            money.fontStyle = FontStyles.Bold;

            // ── 왼쪽 아래: 도구칸 ──
            GameObject toolBox = NewUI("ToolBox", panel.transform);
            PlaceTL(toolBox, 32f, 396f, 400f, 152f);
            AddImage(toolBox, "Slot_Big_Plain", new Color(0.93f, 0.95f, 0.94f, 1f), 3f);
            TMP_Text toolLabel = AddText(toolBox.transform, "Label", "도구칸  <size=75%>마우스 휠로 바꾸기</size>", 22f, Brown, TextAlignmentOptions.MidlineLeft);
            PlaceTL(toolLabel.gameObject, 20f, 10f, 360f, 32f);
            toolLabel.fontStyle = FontStyles.Bold;
            Transform toolGrid = MakeGrid(toolBox.transform, "ToolGrid", 17f, 50f, 366f, 84f, 4, new Vector2(10f, 10f));
            AddSlots(slotPrefab, toolGrid, 4);

            // ── 오른쪽: 핫바 줄 + 가방 + 설명 ──
            GameObject bagBox = NewUI("BagBox", panel.transform);
            PlaceTL(bagBox, 456f, 80f, 992f, 468f);
            AddImage(bagBox, "Slot_Big_Plain", new Color(0.93f, 0.95f, 0.94f, 1f), 3f);

            GameObject band = NewUI("HotbarBand", bagBox.transform);
            PlaceTL(band, 14f, 14f, 964f, 150f);
            AddImage(band, "Slot_Big_Yellow", new Color(1f, 0.87f, 0.52f, 1f), 3f);
            TMP_Text hotbarLabel = AddText(band.transform, "Label", "핫바  <size=75%>숫자키 1~0</size>", 22f, Brown, TextAlignmentOptions.MidlineLeft);
            PlaceTL(hotbarLabel.gameObject, 26f, 8f, 400f, 34f);
            hotbarLabel.fontStyle = FontStyles.Bold;
            Transform hotbarRow = MakeGrid(band.transform, "HotbarRow", 26f, 52f, 912f, 84f, 10, new Vector2(8f, 10f));
            AddSlots(slotPrefab, hotbarRow, 10);

            Transform bagGrid = MakeGrid(bagBox.transform, "BagGrid", 40f, 182f, 912f, 178f, 10, new Vector2(8f, 10f));
            AddSlots(slotPrefab, bagGrid, 20);

            TMP_Text desc = AddText(bagBox.transform, "Description", "아이템에 마우스를 올리면 설명이 나와요.", 24f, Brown, TextAlignmentOptions.TopLeft);
            PlaceTL(desc.gameObject, 40f, 374f, 912f, 80f);
            desc.textWrappingMode = TextWrappingModes.Normal;
            desc.richText = true;

            // ── 끌 때 마우스를 따라다니는 아이콘 (창 맨 위) ──
            GameObject ghost = NewUI("DragGhost", content.transform);
            Place(ghost, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(64f, 64f));
            Image ghostImg = AddImage(ghost, null, Color.white);
            ghostImg.raycastTarget = false;
            ghostImg.preserveAspect = true;
            ghost.SetActive(false);

            // ── 스크립트 연결 ──
            InventoryWindowUI ui = root.AddComponent<InventoryWindowUI>();
            var so = new SerializedObject(ui);
            so.FindProperty("windowRoot").objectReferenceValue = content;
            so.FindProperty("windowPanel").objectReferenceValue = panel.transform;
            so.FindProperty("slotPrefab").objectReferenceValue = slotPrefab;
            so.FindProperty("hotbarRow").objectReferenceValue = hotbarRow;
            so.FindProperty("bagGrid").objectReferenceValue = bagGrid;
            so.FindProperty("toolGrid").objectReferenceValue = toolGrid;
            so.FindProperty("nameText").objectReferenceValue = nameText;
            so.FindProperty("moneyText").objectReferenceValue = money;
            so.FindProperty("descriptionText").objectReferenceValue = desc;
            so.FindProperty("dragGhost").objectReferenceValue = ghostImg;
            so.ApplyModifiedProperties();
            UnityEventTools.AddPersistentListener(closeBtn.onClick, ui.Close);

            // ── 캐릭터 카메라 ──
            string cameraNote = SetupCharacterCamera(charView, rt);

            content.SetActive(false); // 게임 시작 전엔 닫힌 상태로
            string oldNote = HandleOldInventoryUI();

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
            Debug.Log($"[인벤토리 창] '{canvas.name}' 안에 InventoryWindow를 만들었어요. 칸 프리팹: {PrefabFolder}/InventorySlot.prefab\n" +
                      "창 모양을 보려면 InventoryWindow → Content를 잠깐 켜서 보고 다시 끄세요 (게임 중엔 Tab으로 열림)." +
                      cameraNote + oldNote +
                      (font == null ? "\n⚠ 한글 폰트를 못 찾았어요. 글자들의 Font Asset을 한글 폰트로 바꿔 주세요" : ""));
        }

        // ───────────── 칸 프리팹 ─────────────

        private static InventorySlotView BuildSlotPrefab()
        {
            GameObject root = new GameObject("InventorySlot", typeof(RectTransform));
            root.layer = LayerMask.NameToLayer("UI");
            ((RectTransform)root.transform).sizeDelta = new Vector2(84f, 84f);
            Image bg = AddImage(root, "Slot_Light", new Color(0.86f, 0.9f, 0.88f, 1f), 3f);
            bg.raycastTarget = true; // 마우스 받기

            GameObject iconGo = NewUI("Icon", root.transform);
            Place(iconGo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56f, 56f));
            Image icon = AddImage(iconGo, null, Color.white);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.enabled = false;

            TMP_Text qty = AddText(root.transform, "Quantity", "99", 22f, Brown, TextAlignmentOptions.BottomRight);
            Place(qty.gameObject, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-8f, 4f), new Vector2(60f, 30f));
            qty.fontStyle = FontStyles.Bold;
            qty.gameObject.SetActive(false);

            GameObject grade = NewUI("Grade", root.transform);
            Place(grade, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-4f, -4f), new Vector2(26f, 26f));
            Image gradeImg = AddImage(grade, "Icon_Star", new Color(1f, 0.85f, 0.3f, 1f));
            gradeImg.preserveAspect = true;
            gradeImg.raycastTarget = false;
            grade.SetActive(false);

            TMP_Text hotkey = AddText(root.transform, "Hotkey", "1", 16f, Brown, TextAlignmentOptions.TopLeft);
            Place(hotkey.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f, -4f), new Vector2(24f, 24f));
            hotkey.gameObject.SetActive(false);

            // 선택 테두리 (칸보다 살짝 크게, 가운데 비움)
            GameObject frame = NewUI("SelectedFrame", root.transform);
            RectTransform fr = (RectTransform)frame.transform;
            fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one;
            fr.offsetMin = new Vector2(-8f, -8f); fr.offsetMax = new Vector2(8f, 8f);
            Image frameImg = AddImage(frame, "Frame_Select", new Color(0.95f, 0.75f, 0.3f, 1f), 4f);
            frameImg.fillCenter = sprites.ContainsKey("Frame_Select") ? false : true;
            if (!sprites.ContainsKey("Frame_Select")) frameImg.color = new Color(1f, 0.8f, 0.3f, 0.35f);
            frameImg.raycastTarget = false;
            frame.SetActive(false);

            InventorySlotView view = root.AddComponent<InventorySlotView>();
            var so = new SerializedObject(view);
            so.FindProperty("background").objectReferenceValue = bg;
            so.FindProperty("icon").objectReferenceValue = icon;
            so.FindProperty("quantityText").objectReferenceValue = qty;
            so.FindProperty("gradeBadge").objectReferenceValue = gradeImg;
            so.FindProperty("hotkeyText").objectReferenceValue = hotkey;
            so.FindProperty("selectedFrame").objectReferenceValue = frame;
            so.ApplyModifiedPropertiesWithoutUndo();

            string path = $"{PrefabFolder}/InventorySlot.prefab";
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved.GetComponent<InventorySlotView>();
        }

        private static void AddSlots(InventorySlotView prefab, Transform parent, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject, parent);
                go.name = $"Slot{i}";
            }
        }

        private static Transform MakeGrid(Transform parent, string name, float left, float top, float w, float h, int columns, Vector2 spacing)
        {
            GameObject g = NewUI(name, parent);
            PlaceTL(g, left, top, w, h);
            GridLayoutGroup grid = g.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(84f, 84f);
            grid.spacing = spacing;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            grid.childAlignment = TextAnchor.UpperLeft;
            return g.transform;
        }

        // ───────────── 캐릭터 카메라 ─────────────

        private static RenderTexture MakeRenderTexture()
        {
            string path = $"{AssetFolder}/InventoryCharacter_RT.renderTexture";
            RenderTexture rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(path);
            if (rt != null) return rt;
            // 캐릭터 칸(376 x 216) 비율에 맞춤, 투명 배경이 되도록 알파 있는 형식
            rt = new RenderTexture(752, 432, 24, RenderTextureFormat.ARGB32) { name = "InventoryCharacter_RT" };
            AssetDatabase.CreateAsset(rt, path);
            AssetDatabase.SaveAssets();
            return rt;
        }

        private static string SetupCharacterCamera(InventoryCharacterView view, RenderTexture rt)
        {
            PlayerController player = Object.FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include);

            GameObject old = GameObject.Find("InventoryCharacterCamera");
            if (old != null) Undo.DestroyObjectImmediate(old);

            GameObject camGo = new GameObject("InventoryCharacterCamera");
            Undo.RegisterCreatedObjectUndo(camGo, "인벤토리 캐릭터 카메라");
            Camera cam = camGo.AddComponent<Camera>();
            cam.targetTexture = rt;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f); // 투명 → 칸 바탕이 보임
            cam.fieldOfView = 25f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 50f;
            cam.depth = -50f;
            cam.enabled = false; // 창이 열릴 때만 켜짐

            string note = "";
            if (player != null)
            {
                int layer = player.gameObject.layer;
                cam.cullingMask = 1 << layer;
                if (layer == 0)
                    note = "\n⚠ 플레이어가 Default 레이어라 캐릭터 칸에 땅·나무도 같이 찍혀요. 플레이어(와 모델 자식들)를 Player 레이어로 바꾸면 캐릭터만 보여요";
            }
            else note = "\n⚠ 씬에서 PlayerController를 못 찾았어요. CharacterView의 Target에 플레이어를 직접 넣어 주세요";

            var so = new SerializedObject(view);
            so.FindProperty("portraitCamera").objectReferenceValue = cam;
            if (player != null) so.FindProperty("target").objectReferenceValue = player.transform;
            so.ApplyModifiedProperties();
            return note;
        }

        // 예전 인벤토리 창(InventoryUI)이 있으면 같이 열리지 않게 끌지 물어봄
        private static string HandleOldInventoryUI()
        {
            foreach (MonoBehaviour mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
            {
                if (mb == null || mb.GetType().Name != "InventoryUI") continue;
                GameObject go = mb.gameObject;
                bool shared = go.GetComponent<Canvas>() != null || go.GetComponent("HotbarUI") != null;
                if (shared)
                    return $"\n⚠ 예전 인벤토리(InventoryUI)가 '{go.name}'에 있어요. Canvas·핫바와 같은 오브젝트라 자동으로 끄지 않았어요. InventoryUI 컴포넌트 체크를 꺼 주세요";
                if (EditorUtility.DisplayDialog("인벤토리 창 만들기",
                        $"예전 인벤토리 창(InventoryUI)이 '{go.name}'에 있어요.\nTab을 누르면 두 창이 같이 열리니 예전 것을 끌까요?", "끄기", "그대로 두기"))
                {
                    Undo.RecordObject(go, "예전 인벤토리 끄기");
                    go.SetActive(false);
                    return $"\n예전 인벤토리 '{go.name}'을(를) 껐어요 (Ctrl+Z로 되돌리기 가능)";
                }
                return $"\n⚠ 예전 인벤토리 '{go.name}'이(가) 켜져 있어서 Tab을 누르면 같이 열려요";
            }
            return "";
        }

        // ───────────── 작은 도우미 ─────────────

        private static GameObject NewUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        private static void Stretch(GameObject go)
        {
            RectTransform r = (RectTransform)go.transform;
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
        }

        private static void Place(GameObject go, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            RectTransform r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = anchor;
            r.pivot = pivot;
            r.anchoredPosition = pos;
            r.sizeDelta = size;
        }

        // 부모 왼쪽 위에서 (left, top)만큼 떨어진 곳
        private static void PlaceTL(GameObject go, float left, float top, float w, float h)
            => Place(go, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(left, -top), new Vector2(w, h));

        // 조각이 있으면 그 그림(흰색), 없으면 단색
        private static Image AddImage(GameObject go, string spriteName, Color fallback, float multiplier = 1f)
        {
            Image img = go.AddComponent<Image>();
            img.raycastTarget = false;
            if (spriteName != null && sprites.TryGetValue(spriteName, out Sprite sp))
            {
                img.sprite = sp;
                img.color = Color.white;
                if (sp.border.sqrMagnitude > 0f)
                {
                    img.type = Image.Type.Sliced;
                    img.pixelsPerUnitMultiplier = multiplier;
                }
            }
            else img.color = fallback;
            return img;
        }

        private static TMP_Text AddText(Transform parent, string name, string text, float size, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            return t;
        }

        private static void LoadSprites()
        {
            string[] wanted = { "Panel_Dialogue", "Slot_Big_Plain", "Slot_Big_Yellow", "Slot_Light", "Frame_Select", "Icon_Star", "Icon_Coin", "Button_Cancel" };
            var set = new HashSet<string>(wanted);
            sprites = new Dictionary<string, Sprite>();
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/")) continue;
                foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (o is Sprite s && set.Contains(s.name) && !sprites.ContainsKey(s.name)) sprites[s.name] = s;
                if (sprites.Count == set.Count) break;
            }
            var missing = new List<string>();
            foreach (string n in wanted) if (!sprites.ContainsKey(n)) missing.Add(n);
            if (missing.Count > 0)
                Debug.LogWarning("[인벤토리 창] 이 UI 조각을 못 찾아서 단색으로 만들었어요: " + string.Join(", ", missing));
        }

        private static Canvas FindCanvas()
        {
            HotbarUI hotbar = Object.FindAnyObjectByType<HotbarUI>(FindObjectsInactive.Include);
            Canvas c = hotbar != null ? hotbar.GetComponentInParent<Canvas>(true) : null;
            if (c != null) return c.rootCanvas;
            foreach (Canvas any in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
                if (any.isRootCanvas && any.renderMode != RenderMode.WorldSpace) return any;
            return null;
        }

        private static TMP_FontAsset FindFont()
        {
            foreach (TextMeshProUGUI t in Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include))
                if (t.font != null && !t.font.name.StartsWith("LiberationSans")) return t.font;
            return null;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
#endif