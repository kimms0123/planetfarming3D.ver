using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

namespace FarmingSystem.UI.Shop
{
    // 상인 대화창 (포켓몬·동숲 느낌)
    // - 화면 아래 대화 상자: 말하는 사람 이름표 + 한 글자씩 나오는 대사 + 다 나오면 깜빡이는 ▼
    // - 오른쪽 선택지 상자: ▶ 커서가 가리키는 줄이 진하게 강조됨
    // - 조작: ↑↓ / W S 로 고르기, E·Enter·Space 로 결정 (대사가 나오는 중이면 한 번 누르면 대사를 바로 다 보여줌)
    //         마우스를 올리면 그 줄로 커서 이동, 클릭하면 결정
    //
    // 두 가지 방식:
    // 1) 씬에 직접 배치 (권장): Tools → 행성농사일지 → 상점 대화창 만들기 → Canvas 안에 대화창이 생기고 자동 연결됨
    //    → 위치·크기·이미지·색·글꼴을 씬에서 마음대로 고칠 수 있음 (핫바처럼)
    // 2) 아래 "씬에 배치한 대화창" 칸이 비어 있으면, 실행할 때 기본 모양을 코드로 만들어 씀
    public class ShopConversationUI : MonoBehaviour
    {
        [Header("씬에 배치한 대화창 (상점 대화창 만들기 도구가 자동으로 채움)")]
        [Tooltip("대화창 전체 (보였다 숨겼다 하는 오브젝트)")]
        [SerializeField] private GameObject viewRoot;
        [Tooltip("이름표 (말하는 사람 이름이 없으면 숨김)")]
        [SerializeField] private GameObject nameTagObject;
        [SerializeField] private TextMeshProUGUI nameLabel;
        [Tooltip("대사 글자")]
        [SerializeField] private TextMeshProUGUI lineLabel;
        [Tooltip("대사가 다 나오면 깜빡이는 ▼ (선택지가 없을 때만)")]
        [SerializeField] private Graphic nextIndicator;
        [Tooltip("선택지 상자. 안에 있는 ShopChoiceRowUI 줄들을 위에서부터 씀 (모자라면 첫 줄을 복사)")]
        [SerializeField] private RectTransform choiceBox;

        [Header("선택 강조 색")]
        [Tooltip("선택된 줄 배경색")]
        [SerializeField] private Color selectedColor = new Color(0.25f, 0.27f, 0.32f, 1f);
        [SerializeField] private Color selectedTextColor = Color.white;

        [Header("대사")]
        [Tooltip("1초에 나오는 글자 수 (0 = 한 번에 다 보임)")]
        [SerializeField] private float charsPerSecond = 40f;

        [Header("자동 생성 모양 (씬에 배치한 대화창이 없을 때만)")]
        [Tooltip("한글 TMP 폰트. 비우면 씬에서 쓰는 폰트를 자동으로 찾음")]
        [SerializeField] private TMP_FontAsset font;
        [SerializeField] private Color boxColor = new Color(1f, 1f, 1f, 0.97f);
        [SerializeField] private Color borderColor = new Color(0.25f, 0.27f, 0.32f, 1f);
        [SerializeField] private Color textColor = new Color(0.2f, 0.2f, 0.24f, 1f);
        [SerializeField] private Color nameTagColor = new Color(0.98f, 0.72f, 0.3f, 1f);
        [SerializeField] private float textSize = 40f;

        private GameObject builtCanvas; // 코드로 만든 경우 (지울 때 같이 지움)
        private readonly List<ShopChoiceRowUI> rows = new List<ShopChoiceRowUI>();
        private readonly List<Texture2D> ownedTextures = new List<Texture2D>();

        private Action<int> onChoose;
        private int cancelIndex = -1;
        private int activeCount;
        private int selected;
        private float typed;          // 지금까지 보여준 글자 수 (소수점 포함)
        private int lineLength;
        private int shownFrame = -1;  // 창을 연 프레임 (대화를 연 E 키가 바로 선택으로 먹히지 않게)

        public bool IsVisible => viewRoot != null && viewRoot.activeSelf;

        private void Awake()
        {
            if (viewRoot == null) BuildDefaultUI();
            CollectRows();
            viewRoot.SetActive(false);
        }

        private void OnDestroy()
        {
            if (builtCanvas != null) Destroy(builtCanvas);
            foreach (Texture2D t in ownedTextures) if (t != null) Destroy(t);
        }

        // 대사 + 선택지 보여주기
        // cancelIndex = Esc 등으로 취소할 때 고를 선택지 번호 (예: "괜찮아요")
        public void Show(string speaker, string line, IList<string> choices, Action<int> onChoose, int cancelIndex = -1)
        {
            this.onChoose = onChoose;
            this.cancelIndex = cancelIndex;
            viewRoot.SetActive(true);
            shownFrame = Time.frameCount;

            if (nameTagObject != null) nameTagObject.SetActive(!string.IsNullOrEmpty(speaker));
            if (nameLabel != null) nameLabel.text = speaker ?? "";

            lineLabel.text = line ?? "";
            lineLabel.ForceMeshUpdate();
            lineLength = lineLabel.textInfo.characterCount;
            typed = charsPerSecond > 0f ? 0f : lineLength;
            lineLabel.maxVisibleCharacters = Mathf.FloorToInt(typed);

            SetupRows(choices);
            selected = 0;
            RefreshRows();
            if (choiceBox != null) choiceBox.gameObject.SetActive(false); // 대사가 다 나온 뒤에 선택지 표시
            if (nextIndicator != null) nextIndicator.gameObject.SetActive(false);
            if (typed >= lineLength) OnLineFinished();
        }

        public void Hide()
        {
            if (viewRoot != null) viewRoot.SetActive(false);
            onChoose = null;
        }

        // Esc 등으로 취소 (취소용 선택지가 있으면 그걸 고른 것으로)
        public void Cancel()
        {
            if (cancelIndex >= 0 && cancelIndex < activeCount) Choose(cancelIndex);
            else Hide();
        }

        private void Update()
        {
            if (!IsVisible) return;
            Keyboard kb = Keyboard.current;

            // 한 글자씩
            bool typing = typed < lineLength;
            if (typing)
            {
                typed = Mathf.Min(lineLength, typed + charsPerSecond * Time.unscaledDeltaTime);
                lineLabel.maxVisibleCharacters = Mathf.FloorToInt(typed);
                if (typed >= lineLength) OnLineFinished();
            }

            if (kb == null || Time.frameCount == shownFrame) return;
            bool confirm = kb.eKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame ||
                           kb.spaceKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;

            if (typing)
            {
                // 대사 나오는 중에 누르면 바로 다 보여줌
                if (confirm)
                {
                    typed = lineLength;
                    lineLabel.maxVisibleCharacters = lineLength;
                    OnLineFinished();
                }
                return;
            }

            if (activeCount == 0) return;
            if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) Move(-1);
            if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) Move(1);
            if (confirm) Choose(selected);
        }

        private void LateUpdate()
        {
            // 깜빡이는 ▼
            if (nextIndicator != null && nextIndicator.gameObject.activeInHierarchy)
            {
                Color c = nextIndicator.color;
                c.a = 0.4f + 0.6f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3f));
                nextIndicator.color = c;
            }
        }

        private void OnLineFinished()
        {
            if (choiceBox != null) choiceBox.gameObject.SetActive(activeCount > 0);
            if (nextIndicator != null) nextIndicator.gameObject.SetActive(activeCount == 0);
        }

        private void Move(int delta)
        {
            selected = (selected + delta + activeCount) % activeCount;
            RefreshRows();
        }

        private void Choose(int index)
        {
            if (index < 0 || index >= activeCount) return;
            Action<int> cb = onChoose;
            Hide();
            cb?.Invoke(index);
        }

        private void RefreshRows()
        {
            for (int i = 0; i < activeCount; i++)
                rows[i].SetSelected(i == selected, selectedColor, selectedTextColor);
        }

        // ---------- 선택지 줄 ----------

        // 선택지 상자 안의 줄들을 위에서부터 모음
        private void CollectRows()
        {
            rows.Clear();
            if (choiceBox == null) return;
            foreach (Transform child in choiceBox)
            {
                ShopChoiceRowUI row = child.GetComponent<ShopChoiceRowUI>();
                if (row == null) continue;
                Hook(row);
                rows.Add(row);
            }
            if (rows.Count == 0) Debug.LogWarning("[ShopConversationUI] 선택지 상자 안에 ShopChoiceRowUI 줄이 없어요");
        }

        private void Hook(ShopChoiceRowUI row)
        {
            row.Hovered += r => { int i = rows.IndexOf(r); if (i >= 0 && i < activeCount && typed >= lineLength) { selected = i; RefreshRows(); } };
            row.Clicked += r => { int i = rows.IndexOf(r); if (i >= 0 && i < activeCount && typed >= lineLength) Choose(i); };
        }

        // 선택지 수만큼 줄을 켬 (모자라면 첫 줄을 복사해서 늘림, 남는 줄은 숨김)
        private void SetupRows(IList<string> choices)
        {
            int count = choices != null ? choices.Count : 0;
            if (rows.Count > 0 && rows.Count < count) rows[0].SetSelected(false, selectedColor, selectedTextColor); // 선택 색이 복사되지 않게
            while (rows.Count < count && rows.Count > 0)
            {
                ShopChoiceRowUI copy = Instantiate(rows[0], rows[0].transform.parent);
                copy.name = $"Choice{rows.Count}";
                Hook(copy);
                rows.Add(copy);
            }
            activeCount = Mathf.Min(count, rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                bool used = i < activeCount;
                rows[i].gameObject.SetActive(used);
                if (used) rows[i].SetText(choices[i]);
            }
        }

        // =====================================================================
        // 씬에 배치한 대화창이 없을 때: 기본 모양을 코드로 만듦
        // =====================================================================

        private void BuildDefaultUI()
        {
            ResolveFont();
            Sprite round = MakeRoundedSprite(64, 24);
            Sprite arrow = MakeArrowSprite(48);

            // 씬 맨 위(부모 없음)에 따로 만듦: 다른 Canvas 안에 들어가면 그 Canvas의 작은 칸 기준으로 배치돼서 가운데에 작게 뜸
            builtCanvas = new GameObject("ShopConversationCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = builtCanvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            var scaler = builtCanvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            viewRoot = builtCanvas;

            var parts = ShopConversationLayout.Build(builtCanvas.transform, round, arrow, font, boxColor, borderColor, textColor, nameTagColor, textSize);
            nameTagObject = parts.nameTag;
            nameLabel = parts.nameLabel;
            lineLabel = parts.lineLabel;
            nextIndicator = parts.next;
            choiceBox = parts.choiceBox;
        }

        private void ResolveFont()
        {
            if (font != null) return;
            foreach (TextMeshProUGUI t in FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include))
            {
                if (t.font == null || t.font.name.StartsWith("LiberationSans")) continue;
                font = t.font;
                break;
            }
            if (font == null) Debug.LogWarning("[ShopConversationUI] 한글 폰트를 못 찾았어요. Font 칸에 한글 TMP 폰트 에셋을 넣어주세요");
        }

        private Texture2D NewTexture(int size, string name)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = name
            };
            ownedTextures.Add(tex);
            return tex;
        }

        private Sprite MakeRoundedSprite(int size, int radius)
        {
            Texture2D tex = NewTexture(size, "ShopRounded (generated)");
            tex.SetPixels32(ShopConversationLayout.RoundedPixels(size, radius));
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                 SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        private Sprite MakeArrowSprite(int size)
        {
            Texture2D tex = NewTexture(size, "ShopArrow (generated)");
            tex.SetPixels32(ShopConversationLayout.ArrowPixels(size));
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }

    // 대화창 기본 모양 만들기 (실행 중 자동 생성과, 에디터의 "상점 대화창 만들기" 도구가 같이 씀)
    public static class ShopConversationLayout
    {
        public struct Parts
        {
            public RectTransform root;
            public GameObject nameTag;
            public TextMeshProUGUI nameLabel;
            public TextMeshProUGUI lineLabel;
            public Graphic next;
            public RectTransform choiceBox;
        }

        public static readonly string[] SampleChoices = { "구매하기", "판매하기", "괜찮아요" };

        // parent 아래에 화면 전체 크기의 "ShopConversation"을 만들고 그 안에 대화 상자·선택지 상자를 배치
        public static Parts Build(Transform parent, Sprite round, Sprite arrow, TMP_FontAsset font,
                                  Color boxColor, Color borderColor, Color textColor, Color nameTagColor, float textSize)
        {
            var parts = new Parts();

            var rootGo = new GameObject("ShopConversation", typeof(RectTransform));
            rootGo.transform.SetParent(parent, false);
            var root = rootGo.GetComponent<RectTransform>();
            Stretch(root);
            parts.root = root;

            // ---- 대화 상자 (화면 아래 가운데) ----
            RectTransform box = MakeBox("DialogueBox", root, round, boxColor, borderColor);
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0f);
            box.pivot = new Vector2(0.5f, 0f);
            box.sizeDelta = new Vector2(1500f, 230f);
            box.anchoredPosition = new Vector2(0f, 40f);

            TextMeshProUGUI line = MakeText("Line", box, font, textSize, textColor, TextAlignmentOptions.TopLeft);
            line.text = "어서와! 오늘은 무엇을 도와줄까?";
            RectTransform lr = line.rectTransform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(70f, 40f); lr.offsetMax = new Vector2(-70f, -50f);
            parts.lineLabel = line;

            // 다음 표시 ▼ (화살표 그림을 아래로 돌림)
            Image next = MakeImage("Next", box, arrow, textColor);
            next.raycastTarget = false;
            RectTransform nr = next.rectTransform;
            nr.anchorMin = nr.anchorMax = new Vector2(1f, 0f);
            nr.sizeDelta = new Vector2(30f, 30f);
            nr.anchoredPosition = new Vector2(-60f, 45f);
            nr.localRotation = Quaternion.Euler(0f, 0f, -90f);
            parts.next = next;

            // 이름표 (대화 상자 왼쪽 위에 걸침)
            Image tag = MakeImage("NameTag", box, round, nameTagColor);
            RectTransform tr = tag.rectTransform;
            tr.anchorMin = tr.anchorMax = new Vector2(0f, 1f);
            tr.pivot = new Vector2(0f, 0.5f);
            tr.sizeDelta = new Vector2(220f, 64f);
            tr.anchoredPosition = new Vector2(50f, 0f);
            TextMeshProUGUI name = MakeText("Name", tr, font, textSize * 0.8f, Color.white, TextAlignmentOptions.Center);
            name.text = "상인";
            name.fontStyle = FontStyles.Bold;
            Stretch(name.rectTransform);
            parts.nameTag = tag.gameObject;
            parts.nameLabel = name;

            // ---- 선택지 상자 (대화 상자 오른쪽 위) ----
            RectTransform choice = MakeBox("ChoiceBox", root, round, boxColor, borderColor);
            choice.anchorMin = choice.anchorMax = new Vector2(0.5f, 0f);
            choice.pivot = new Vector2(1f, 0f);
            choice.anchoredPosition = new Vector2(750f, 290f);
            choice.sizeDelta = new Vector2(380f, 100f);
            var layout = choice.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 22, 22);
            layout.spacing = 6f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            var fitter = choice.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            parts.choiceBox = choice;

            for (int i = 0; i < SampleChoices.Length; i++)
                MakeRow(choice, i, round, arrow, font, textSize, textColor).SetText(SampleChoices[i]);

            return parts;
        }

        private static ShopChoiceRowUI MakeRow(RectTransform parent, int index, Sprite round, Sprite arrow,
                                               TMP_FontAsset font, float textSize, Color textColor)
        {
            Image back = MakeImage($"Choice{index}", parent, round, new Color(0f, 0f, 0f, 0f)); // 평소엔 투명
            RectTransform row = back.rectTransform;
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 70f;

            Image cursor = MakeImage("Cursor", row, arrow, textColor);
            cursor.raycastTarget = false;
            RectTransform cr = cursor.rectTransform;
            cr.anchorMin = cr.anchorMax = new Vector2(0f, 0.5f);
            cr.sizeDelta = new Vector2(26f, 26f);
            cr.anchoredPosition = new Vector2(26f, 0f);

            TextMeshProUGUI label = MakeText("Label", row, font, textSize * 0.9f, textColor, TextAlignmentOptions.MidlineLeft);
            RectTransform lr = label.rectTransform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(56f, 0f); lr.offsetMax = new Vector2(-16f, 0f);

            var rowUI = row.gameObject.AddComponent<ShopChoiceRowUI>();
            rowUI.Setup(back, label, cursor);
            return rowUI;
        }

        // 테두리 + 흰 바탕 상자 (바깥 = 테두리 색, 안쪽 Fill = 바탕색)
        private static RectTransform MakeBox(string name, Transform parent, Sprite round, Color fill, Color border)
        {
            Image outer = MakeImage(name, parent, round, border);
            Image inner = MakeImage("Fill", outer.rectTransform, round, fill);
            RectTransform ir = inner.rectTransform;
            ir.anchorMin = Vector2.zero; ir.anchorMax = Vector2.one;
            ir.offsetMin = new Vector2(5f, 5f); ir.offsetMax = new Vector2(-5f, -5f);
            inner.raycastTarget = false;
            inner.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            return outer.rectTransform;
        }

        private static Image MakeImage(string name, Transform parent, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            if (sprite != null && sprite.border.sqrMagnitude > 0f) img.type = Image.Type.Sliced;
            return img;
        }

        private static TextMeshProUGUI MakeText(string name, Transform parent, TMP_FontAsset font, float size, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            return t;
        }

        private static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
        }

        // 둥근 사각형 그림 (흰색, 9-슬라이스용)
        public static Color32[] RoundedPixels(int size, int radius)
        {
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                    float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float a = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            return px;
        }

        // 오른쪽을 가리키는 삼각형 ▶ 그림
        public static Color32[] ArrowPixels(int size)
        {
            var px = new Color32[size * size];
            float pad = size * 0.12f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f - pad) / (size - pad * 2f);
                    float v = Mathf.Abs((y + 0.5f) / size - 0.5f) * 2f;
                    float edge = (1f - u) - v;
                    float a = u < 0f || u > 1f ? 0f : Mathf.Clamp01(edge * size * 0.5f + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            return px;
        }
    }
}