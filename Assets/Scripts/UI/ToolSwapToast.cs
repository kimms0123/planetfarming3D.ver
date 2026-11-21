using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FarmingSystem.Core;
using FarmingSystem.Inventory;
using FarmingSystem.Save;

namespace FarmingSystem.UI
{
    // 도구를 바꾸면 핫바 바로 위 가운데에 "[아이콘] 튼튼한 호미  87/120"이 잠깐 떴다 사라짐
    // 설치 필요 없음 - 게임이 시작되면 저절로 생김. 기존 스크립트는 안 고침 (InventoryManager.OnToolChanged만 구독)
    //
    // - 연달아 바꾸면 줄이 쌓이지 않고 같은 자리 내용만 바뀜 (휠을 굴려도 한 줄)
    // - 내구도가 있는 도구는 남은 횟수도 표시, 얼마 안 남으면 빨간 글씨
    // - 시작 화면·불러오기 중에는 안 뜸
    // - 획득 알림(ItemToast)과 같은 판 모양·글꼴·색, 같은 방식으로 아래에서 살짝 올라오며 나타남
    // - 메뉴(폰)·가방·상점·대화 창이 열려 있으면 안 뜸 (창 안에서 도구칸을 바꿀 때 가리지 않게)
    public class ToolSwapToast : MonoBehaviour
    {
        private const float ShowSeconds = 1.4f;
        private const float FadeIn = 0.1f, FadeOut = 0.3f;
        private const float BottomY = 150f;   // 화면 아래에서 (1920x1080 기준) - 핫바 바로 위
        private const float LowDurability = 0.2f; // 이 비율 밑이면 빨간 글씨

        private static readonly Color Fill = new Color(0.97f, 0.96f, 0.92f, 0.95f);
        private static readonly Color Outline = new Color32(0x5B, 0x4A, 0x3A, 0xFF);
        private static readonly Color TextColor = new Color32(0x4A, 0x3D, 0x31, 0xFF);
        private static readonly Color SubColor = new Color32(0x7A, 0x6A, 0x58, 0xFF);
        private static readonly Color WarnColor = new Color32(0xC0, 0x3A, 0x2E, 0xFF);

        private static ToolSwapToast instance;
        private InventoryManager hooked;
        private RectTransform panel;
        private CanvasGroup group;
        private Image icon;
        private TextMeshProUGUI label;
        private float age = 99f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (instance != null) return;
            var go = new GameObject("ToolSwapToast");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<ToolSwapToast>();
        }

        private void Update()
        {
            // InventoryManager가 늦게 생기거나 바뀌어도 다시 연결
            InventoryManager inv = InventoryManager.Instance;
            if (inv != hooked)
            {
                if (hooked != null) hooked.OnToolChanged -= HandleToolChanged;
                hooked = inv;
                if (hooked != null) hooked.OnToolChanged += HandleToolChanged;
            }

            if (group == null) return;
            age += Time.unscaledDeltaTime;
            float a = age < FadeIn ? age / FadeIn
                    : age < FadeIn + ShowSeconds ? 1f
                    : 1f - (age - FadeIn - ShowSeconds) / FadeOut;
            group.alpha = Mathf.Clamp01(a);
            float pop = Mathf.Clamp01(1f - age / 0.15f);
            panel.localScale = Vector3.one * (1f + 0.06f * pop);
            // 아래에서 살짝 올라오며 나타남 (획득 알림이 옆에서 밀려 들어오는 것과 같은 느낌)
            float rise = 1f - Mathf.Clamp01(age / 0.18f);
            panel.anchoredPosition = new Vector2(0f, BottomY - 16f * rise * rise);
            if (Covered()) { group.alpha = 0f; age = 99f; }
        }

        private void OnDestroy()
        {
            if (hooked != null) hooked.OnToolChanged -= HandleToolChanged;
            if (instance == this) instance = null;
        }

        // 다른 창이 열려 있으면 안 띄움 (획득 알림과 같은 기준)
        private bool Covered() => TitleScreen.IsShowing || UIInputBlocker.IsBlocked || PhoneMenu.IsOpen
                                  || (hooked != null && hooked.IsInventoryOpen);

        private void HandleToolChanged(ToolType type)
        {
            if (hooked == null || Covered()) return;
            if (panel == null) Build();

            ToolData tool = hooked.CurrentToolData;
            InventorySlotData slot = hooked.GetSlot(SlotArea.Tool, hooked.CurrentToolIndex);

            string name = tool != null ? tool.itemName : "맨손";
            string text = name;
            if (tool != null && tool.maxDurability > 0 && slot != null && !slot.IsEmpty)
            {
                bool low = slot.durability <= Mathf.CeilToInt(tool.maxDurability * LowDurability);
                string col = ColorUtility.ToHtmlStringRGB(low ? WarnColor : SubColor);
                text += $"  <size=80%><color=#{col}>{slot.durability}/{tool.maxDurability}</color></size>";
            }
            label.text = text;
            icon.sprite = tool != null ? tool.icon : null;
            icon.enabled = icon.sprite != null;

            // 글자 길이에 맞춰 판 크기 조절
            label.ForceMeshUpdate();
            float textW = Mathf.Ceil(label.preferredWidth);
            float iconW = icon.enabled ? 52f : 0f;
            panel.sizeDelta = new Vector2(24f + iconW + textW + 24f, 60f);
            label.rectTransform.anchoredPosition = new Vector2(24f + iconW, 0f);
            age = 0f;
        }

        private void Build()
        {
            var cgo = new GameObject("ToolSwapToastCanvas", typeof(Canvas), typeof(CanvasScaler));
            cgo.transform.SetParent(transform, false);
            Canvas canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 600; // 획득 알림과 같은 층 (메뉴·시작 화면 아래)
            var scaler = cgo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var pgo = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            pgo.transform.SetParent(cgo.transform, false);
            panel = (RectTransform)pgo.transform;
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0f);
            panel.pivot = new Vector2(0.5f, 0f);
            panel.anchoredPosition = new Vector2(0f, BottomY);
            Image bg = pgo.GetComponent<Image>();
            bg.sprite = MakePanelSprite();
            bg.type = Image.Type.Sliced;
            bg.color = Fill;
            bg.raycastTarget = false;
            group = pgo.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            var igo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            igo.transform.SetParent(panel, false);
            var irt = (RectTransform)igo.transform;
            irt.anchorMin = irt.anchorMax = new Vector2(0f, 0.5f);
            irt.pivot = new Vector2(0f, 0.5f);
            irt.anchoredPosition = new Vector2(18f, 0f);
            irt.sizeDelta = new Vector2(44f, 44f);
            icon = igo.GetComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            var lgo = new GameObject("Label", typeof(RectTransform));
            lgo.transform.SetParent(panel, false);
            label = lgo.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset font = UIFont.FindKorean();
            if (font != null) label.font = font;
            label.fontSize = 28f;
            label.color = TextColor;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;
            var lrt = label.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0f, 0.5f);
            lrt.pivot = new Vector2(0f, 0.5f);
            lrt.sizeDelta = new Vector2(600f, 50f);
        }

        // 획득 알림(ItemToast)과 같은 둥근 사각형 + 갈색 테두리
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
                    float d = R - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    Color c = Color.Lerp(Outline, Color.white, Mathf.Clamp01(d - Border + 0.5f));
                    c.a = Mathf.Clamp01(d + 0.5f);
                    px[y * S + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(R + 2, R + 2, R + 2, R + 2));
        }
    }
}
