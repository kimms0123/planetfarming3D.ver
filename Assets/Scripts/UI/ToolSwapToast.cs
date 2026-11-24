using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FarmingSystem.Core;
using FarmingSystem.Inventory;
using FarmingSystem.Save;

namespace FarmingSystem.UI
{
    // 지금 들고 있는 도구 표시: 핫바 왼쪽에 "[아이콘] 튼튼한 호미  87/120"이 계속 떠 있음 (Always Show)
    // 도구를 바꾸면 통 튀며 강조. alwaysShow를 끄면 예전처럼 핫바 위 가운데에 잠깐 떴다 사라짐
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
        private static readonly bool AlwaysShow = true;    // true = 계속 표시 (핫바 왼쪽), false = 바꿀 때만 잠깐 (핫바 위)
        private const float HotbarGap = 18f;      // 핫바 왼쪽 끝과의 간격

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
        private float shown;                       // 계속 표시 모드의 보이는 정도 (0~1)
        private RectTransform hotbarRect;           // 핫바 위치 (계속 표시 모드에서 그 왼쪽에 붙음)

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
                if (hooked != null) { hooked.OnToolChanged -= HandleToolChanged; hooked.OnInventoryChanged -= HandleInventoryChanged; }
                hooked = inv;
                if (hooked != null) { hooked.OnToolChanged += HandleToolChanged; hooked.OnInventoryChanged += HandleInventoryChanged; }
                if (AlwaysShow && hooked != null) { if (panel == null) Build(); Refresh(); age = 99f; }
            }

            if (group == null) return;
            if (AlwaysShow) { UpdateAlwaysShow(); return; }
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

        // 계속 표시: 창이 열려 있거나 시작 화면이면 숨김, 도구를 바꾸면 통 튐
        private void UpdateAlwaysShow()
        {
            age += Time.unscaledDeltaTime;
            bool visible = hooked != null && !Covered();
            shown = Mathf.MoveTowards(shown, visible ? 1f : 0f, Time.unscaledDeltaTime * 6f);
            group.alpha = shown;
            float pop = Mathf.Clamp01(1f - age / 0.25f);
            panel.localScale = Vector3.one * (1f + 0.12f * pop * pop);
            panel.anchoredPosition = AlwaysShowPosition();
        }

        // 핫바 왼쪽, 핫바와 같은 높이 (핫바를 못 찾으면 화면 왼쪽 아래)
        private Vector2 AlwaysShowPosition()
        {
            RectTransform cr = (RectTransform)panel.parent;
            if (hotbarRect == null)
            {
                HotbarUI hb = FindAnyObjectByType<HotbarUI>();
                if (hb != null) hotbarRect = hb.transform as RectTransform;
            }
            if (hotbarRect != null && hotbarRect.gameObject.activeInHierarchy && SlotsLeftMiddle(out Vector2 screen)
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(cr, screen, null, out Vector2 local))
            {
                // 판은 아래 가운데 기준 → 오른쪽 끝이 핫바 왼쪽 끝에, 세로 가운데가 핫바 가운데에 오게
                float x = local.x - HotbarGap - panel.sizeDelta.x * 0.5f;
                float y = local.y + cr.rect.height * 0.5f - panel.sizeDelta.y * 0.5f;
                return new Vector2(x, y);
            }
            return new Vector2(-cr.rect.width * 0.5f + 40f + panel.sizeDelta.x * 0.5f, 24f);
        }

        // 핫바 칸들 중 가장 왼쪽 칸의 왼쪽 가운데 (화면 좌표)
        private bool SlotsLeftMiddle(out Vector2 screen)
        {
            screen = Vector2.zero;
            ItemSlotUI[] slots = hotbarRect.GetComponentsInChildren<ItemSlotUI>();
            if (slots.Length == 0) return false;
            Canvas hc = hotbarRect.GetComponentInParent<Canvas>();
            Camera hcam = hc != null && hc.renderMode != RenderMode.ScreenSpaceOverlay ? hc.worldCamera : null;
            var corners = new Vector3[4];
            float minX = float.MaxValue, midY = 0f;
            foreach (ItemSlotUI sl in slots)
            {
                ((RectTransform)sl.transform).GetWorldCorners(corners); // 0 = 왼쪽 아래, 1 = 왼쪽 위
                Vector2 bl = RectTransformUtility.WorldToScreenPoint(hcam, corners[0]);
                Vector2 tl = RectTransformUtility.WorldToScreenPoint(hcam, corners[1]);
                if (bl.x < minX) { minX = bl.x; midY = (bl.y + tl.y) * 0.5f; }
            }
            screen = new Vector2(minX, midY);
            return true;
        }

        private void HandleInventoryChanged()
        {
            if (AlwaysShow && panel != null) Refresh(); // 내구도가 줄면 바로 반영
        }

        private void OnDestroy()
        {
            if (hooked != null) { hooked.OnToolChanged -= HandleToolChanged; hooked.OnInventoryChanged -= HandleInventoryChanged; }
            if (instance == this) instance = null;
        }

        // 다른 창이 열려 있으면 안 띄움 (획득 알림과 같은 기준)
        private bool Covered() => TitleScreen.IsShowing || UIInputBlocker.IsBlocked || PhoneMenu.IsOpen
                                  || (hooked != null && hooked.IsInventoryOpen);

        private void HandleToolChanged(ToolType type)
        {
            if (hooked == null) return;
            if (!AlwaysShow && Covered()) return;
            if (panel == null) Build();
            Refresh();
            age = 0f;
        }

        // 지금 도구 이름·아이콘·내구도로 내용 갱신
        private void Refresh()
        {
            if (hooked == null || panel == null) return;

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