using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using FarmingSystem.Core;
using FarmingSystem.Save;
using FarmingSystem.UI;
using FarmingSystem.Audio;

namespace FarmingSystem.Fishing
{
    // 물고기를 잡았을 때 뜨는 창 + 낚시 안내 문구 (FishingManager가 부름, 처음 쓸 때 저절로 생김)
    //  ┌──────────────────────────────────────┐
    //  │ [그림]   NEW!                          │
    //  │          별송사리를 잡았다!             │
    //  │          강 · 모든 계절 · 흔함           │
    //  │          설명 …                        │
    //  │          판매가 20벨                    │
    //  └──────────────────────────────────────┘
    //  - 클릭·E·Space 또는 4초 뒤 닫힘. 떠 있는 동안 다른 조작은 멈춤
    //  - 그림은 물고기 데이터의 Icon (없으면 물고기 모양 실루엣)
    public class FishCatchPopup : MonoBehaviour
    {
        private const float AutoClose = 4f;
        private static readonly Color PanelFill = new Color(0.97f, 0.95f, 0.89f, 0.98f);
        private static readonly Color Brown = new Color32(0x5B, 0x4A, 0x3A, 0xFF);
        private static readonly Color Ink = new Color32(0x4A, 0x3D, 0x31, 0xFF);

        private static FishCatchPopup instance;
        private Canvas canvas;
        private RectTransform panel;
        private CanvasGroup panelGroup;
        private Image icon;
        private TextMeshProUGUI newTag, title, sub, desc, price;
        private TextMeshProUGUI hint;
        private float shownAt, hintUntil;
        private bool showing;
        private Sprite fishShape;

        private static FishCatchPopup Get()
        {
            if (instance == null)
            {
                var go = new GameObject("FishCatchPopup");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<FishCatchPopup>();
                instance.Build();
            }
            return instance;
        }

        /// <summary>잡은 물고기 창 (isNew = 도감에 처음 등록, released = 가방이 가득 차서 놓아줌)</summary>
        public static void Show(FishData fish, bool isNew, bool released)
        {
            FishCatchPopup p = Get();
            p.icon.sprite = fish.icon != null ? fish.icon : p.fishShape;
            p.icon.color = fish.icon != null ? Color.white : new Color(0.25f, 0.4f, 0.55f);
            p.newTag.gameObject.SetActive(isNew);
            p.title.text = $"{fish.itemName}{Particle(fish.itemName)} 잡았다!";
            string rarityColor = fish.rarity == FishRarity.Legendary ? "#D9822B" : fish.rarity == FishRarity.Rare ? "#3F7FC1" : fish.rarity == FishRarity.Uncommon ? "#4E8A4A" : "#8A7A66";
            p.sub.text = $"{FishData.HabitatName(fish.habitat)}  ·  {fish.SeasonText}  ·  <color={rarityColor}>{FishData.RarityName(fish.rarity)}</color>";
            p.desc.text = fish.description;
            p.price.text = released
                ? "<color=#C03A2E>가방이 가득 차서 놓아줬어요</color>"
                : $"판매가 <b>{fish.basePrice:N0}벨</b>";
            p.canvas.enabled = true;
            p.panel.gameObject.SetActive(true);
            GameAudio.PlayJingle(fish.rarity >= FishRarity.Rare ? SoundId.FishResultRare : SoundId.FishResult); // 따단
            p.shownAt = Time.unscaledTime;
            if (!p.showing) UIInputBlocker.Block(p);
            p.showing = true;
        }

        /// <summary>화면 아래쪽에 잠깐 뜨는 안내 문구</summary>
        public static void Hint(string text)
        {
            FishCatchPopup p = Get();
            if (p.hint.text != text || Time.unscaledTime > p.hintUntil) GameAudio.Play(SoundId.Notice, 0.6f); // 같은 문구 반복은 조용히
            p.canvas.enabled = true;
            p.hint.text = text;
            p.hintUntil = Time.unscaledTime + 2f;
        }

        // 받침이 있으면 '을', 없으면 '를'
        private static string Particle(string word)
        {
            if (string.IsNullOrEmpty(word)) return "을(를)";
            char c = word[word.Length - 1];
            if (c < 0xAC00 || c > 0xD7A3) return "을(를)";
            return (c - 0xAC00) % 28 != 0 ? "을" : "를";
        }

        private void Update()
        {
            float hintAlpha = Mathf.Clamp01((hintUntil - Time.unscaledTime) / 0.4f);
            hint.alpha = hintAlpha;

            if (showing)
            {
                float t = Time.unscaledTime - shownAt;
                float k = Mathf.Clamp01(t / 0.25f);
                float s = Mathf.LerpUnclamped(0.7f, 1f, 1f + 2.2f * Mathf.Pow(k - 1f, 3f) + 1.2f * Mathf.Pow(k - 1f, 2f)); // 살짝 튕기며 커짐
                panel.localScale = new Vector3(s, s, 1f);
                panelGroup.alpha = k;
                bool close = t > 0.35f && ((Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                    || (Keyboard.current != null && (Keyboard.current.eKey.wasPressedThisFrame || Keyboard.current.spaceKey.wasPressedThisFrame || Keyboard.current.escapeKey.wasPressedThisFrame)));
                if (close || t > AutoClose)
                {
                    showing = false;
                    panel.gameObject.SetActive(false);
                    UIInputBlocker.Unblock(this);
                }
            }
            if (!showing && hintAlpha <= 0f) canvas.enabled = false;
        }

        private void OnDestroy() => UIInputBlocker.Unblock(this);

        // ───────────────────────── 만들기 ─────────────────────────

        private void Build()
        {
            TMP_FontAsset font = UIFont.FindKorean();
            Sprite rounded = ContainerWindow.MakeRounded();
            fishShape = MakeFishShape();
            var cgo = new GameObject("FishCanvas", typeof(Canvas), typeof(CanvasScaler));
            cgo.transform.SetParent(transform, false);
            canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 640;
            var scaler = cgo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // 잡은 물고기 창
            panel = R("Panel", cgo.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(820f, 330f));
            panelGroup = panel.gameObject.AddComponent<CanvasGroup>();
            panelGroup.blocksRaycasts = false;
            RectTransform outline = R("Outline", panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(828f, 338f));
            Img(outline, rounded, Brown);
            Img(R("Fill", panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820f, 330f)), rounded, PanelFill);

            RectTransform box = R("IconBox", panel, new Vector2(0f, 0.5f), new Vector2(160f, 0f), new Vector2(240f, 240f));
            Img(box, rounded, new Color(0.88f, 0.93f, 0.95f));
            icon = Img(R("Icon", box, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200f, 200f)), null, Color.white);
            icon.preserveAspect = true;

            float x = 310f, w = 470f;
            newTag = Txt(panel, font, "NEW! 도감에 등록됐어요", 20f, new Color32(0xE0, 0x6C, 0x2A, 0xFF), x, 34f, w, 28f);
            newTag.fontStyle = FontStyles.Bold;
            title = Txt(panel, font, "", 38f, Ink, x, 64f, w, 54f);
            title.fontStyle = FontStyles.Bold;
            title.enableAutoSizing = true;
            title.fontSizeMin = 26f;
            title.fontSizeMax = 38f;
            sub = Txt(panel, font, "", 22f, new Color32(0x8A, 0x7A, 0x66, 0xFF), x, 122f, w, 30f);
            desc = Txt(panel, font, "", 22f, Ink, x, 162f, w, 96f);
            desc.textWrappingMode = TextWrappingModes.Normal;
            desc.lineSpacing = 6f;
            price = Txt(panel, font, "", 22f, new Color32(0x2F, 0x7D, 0x4F, 0xFF), x, 266f, w, 32f);
            panel.gameObject.SetActive(false);

            // 안내 문구 (핫바 위)
            hint = Txt(cgo.GetComponent<RectTransform>(), font, "", 26f, Color.white, 0f, 0f, 900f, 40f);
            RectTransform hrt = hint.rectTransform;
            hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0f);
            hrt.pivot = new Vector2(0.5f, 0f);
            hrt.anchoredPosition = new Vector2(0f, 170f);
            hint.alignment = TextAlignmentOptions.Center;
            hint.outlineWidth = 0.25f;
            hint.outlineColor = new Color32(40, 30, 20, 255);
            hint.alpha = 0f;
            canvas.enabled = false;
        }

        private static RectTransform R(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        private static Image Img(RectTransform rt, Sprite sprite, Color color)
        {
            Image img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            if (sprite != null && sprite.border.sqrMagnitude > 0f) img.type = Image.Type.Sliced;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static TextMeshProUGUI Txt(RectTransform parent, TMP_FontAsset font, string text, float size, Color color, float x, float y, float w, float h)
        {
            RectTransform rt = R("Text", parent, new Vector2(0f, 1f), new Vector2(x, -y), new Vector2(w, h));
            rt.pivot = new Vector2(0f, 1f);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAlignmentOptions.TopLeft;
            t.richText = true;
            t.raycastTarget = false;
            return t;
        }

        // 아이콘이 없을 때 쓰는 물고기 모양 (타원 몸통 + 삼각 꼬리)
        private static Sprite MakeFishShape()
        {
            const int W = 128, H = 128;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = (x - 54f) / 40f, v = (y - 64f) / 24f;
                    float body = 1f - (u * u + v * v);
                    float tx = (x - 92f) / 26f, ty = Mathf.Abs(y - 64f) / 26f;
                    float tail = (tx >= 0f && tx <= 1f && ty <= tx) ? 1f : 0f;
                    float a = Mathf.Clamp01(Mathf.Max(body * 12f, tail));
                    float eye = ((x - 30f) * (x - 30f) + (y - 70f) * (y - 70f)) < 20f ? 1f : 0f;
                    px[y * W + x] = eye > 0f ? new Color(0.1f, 0.1f, 0.1f, a) : new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}