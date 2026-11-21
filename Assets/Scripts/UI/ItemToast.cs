using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FarmingSystem.Core;
using FarmingSystem.Farming;
using FarmingSystem.Inventory;
using FarmingSystem.Save;
using FarmingSystem.Audio;

namespace FarmingSystem.UI
{
    // 얻은 아이템 알림 (스타듀밸리처럼 화면 왼쪽 아래에 잠깐 떴다 사라짐)
    // 설치 필요 없음 - 게임이 시작되면 저절로 생김. 기존 스크립트는 안 고침
    //
    // 무엇이 뜨나
    //  - 가방에 들어온 아이템: [아이콘] 이름 ×개수   (수확·구매·퀘스트 보상 등 어디서 얻든)
    //    · 같은 아이템을 연달아 얻으면 새 줄이 아니라 개수가 올라가고 시간이 다시 참 (낫으로 여러 칸 수확해도 한 줄)
    //    · 작물은 등급(좋음·최고)이면 별 표시
    //  - 도감에 처음 등록된 작물: "도감 등록!" 줄
    //  ※ 가방 안에서 옮기기·팔기·쓰기는 안 뜸 (늘어난 것만). 저장 불러오기·새 게임 시작 때도 안 뜸
    //  - 다른 창(상점·대화·가방·메뉴)이 열려 있는 동안 얻은 건 가려지지 않게 숨겨 두었다가, 그 창을 닫으면 그때 보여 줌
    //
    // 왜 왼쪽 아래인가
    //  - 가운데 아래 = 핫바, 그 위 = 건물 안내·대화창 / 왼쪽 위 = 시계 / 가운데 위 = 섬 알림 / 오른쪽 = 대화 선택지·상점 선택지
    //  - 화면 가운데는 십자선으로 밭을 보는 자리라 가리면 안 됨
    //  → 비어 있고, 핫바(가방) 바로 옆이라 "가방에 들어갔다"는 뜻이 자연스럽게 읽히는 왼쪽 아래
    //  - 새 알림이 맨 아래(핫바 높이)에 생기고 먼저 뜬 건 위로 밀려 올라감, 최대 5줄
    public class ItemToast : MonoBehaviour
    {
        private const float ShowSeconds = 3f;       // 떠 있는 시간
        private const float FadeSeconds = 0.4f;
        private const int MaxToasts = 5;
        private const float RowHeight = 60f, RowGap = 8f;
        private static readonly Vector2 Corner = new Vector2(36f, 44f); // 화면 왼쪽 아래에서 (1920x1080 기준)

        private static readonly Color Fill = new Color(0.97f, 0.96f, 0.92f, 0.95f);
        private static readonly Color Outline = new Color32(0x5B, 0x4A, 0x3A, 0xFF);
        private static readonly Color TextColor = new Color32(0x4A, 0x3D, 0x31, 0xFF);
        private static readonly Color CountColor = new Color32(0x2F, 0x7D, 0x4F, 0xFF);
        private static readonly Color PerfectColor = new Color32(0xE8, 0xA8, 0x20, 0xFF);
        private static readonly Color GoodColor = new Color32(0x5C, 0x8E, 0xD8, 0xFF);
        private static readonly Color CollectionFill = new Color(0.9f, 0.97f, 0.9f, 0.96f);

        private class Toast
        {
            public string key;
            public ItemData item;
            public ItemQuality quality;
            public int count;
            public float age;       // 뜬 뒤 지난 시간
            public float punch;     // 개수가 오를 때 살짝 커지는 효과 (1 → 0)
            public RectTransform rt;
            public CanvasGroup group;
            public Image icon;
            public TextMeshProUGUI label;
            public float y;
            public bool held;       // 다른 창에 가려져서 아직 못 보여 준 알림
        }

        private static ItemToast instance;
        private Canvas canvas;
        private Sprite panelSprite;
        private TMP_FontAsset font;
        private readonly List<Toast> toasts = new List<Toast>(); // 0 = 가장 새 것 (맨 아래)
        private readonly Dictionary<string, int> owned = new Dictionary<string, int>();
        private readonly Dictionary<string, int> scratch = new Dictionary<string, int>();
        private readonly Dictionary<string, InventorySlotData> sample = new Dictionary<string, InventorySlotData>();
        private InventoryManager inventory;
        private bool snapshotReady, quiet;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (instance != null) return;
            var go = new GameObject("ItemToast");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<ItemToast>();
        }

        private void OnEnable() => CropCollection.OnRegistered += HandleCollection;

        private void OnDisable()
        {
            CropCollection.OnRegistered -= HandleCollection;
            if (inventory != null) inventory.OnInventoryChanged -= HandleInventoryChanged;
            inventory = null;
        }

        private void Update()
        {
            if (inventory == null && InventoryManager.Instance != null)
            {
                inventory = InventoryManager.Instance;
                inventory.OnInventoryChanged += HandleInventoryChanged;
                snapshotReady = false;
            }

            // 시작 화면이 떠 있는 동안(불러오기·새 게임 준비)은 알림 없이 가방 상태만 다시 기억
            // 상자에서 꺼낸 건 '얻은 것'이 아니라서 알림 없음 → 상자 창이 열려 있는 동안도 가방 상태만 다시 기억
            bool nowQuiet = TitleScreen.IsShowing || inventory == null || ContainerWindow.IsOpen;
            if (nowQuiet || quiet || !snapshotReady) { Count(owned); snapshotReady = inventory != null; }
            quiet = nowQuiet;

            Animate();
        }

        // ───────────────────────── 가방에 늘어난 것 찾기 ─────────────────────────

        private static string Key(ItemData item, ItemQuality q) => item.name + ":" + (int)q; // 에셋 이름 + 등급

        // 가방 + 도구칸의 아이템별 개수
        private void Count(Dictionary<string, int> into)
        {
            into.Clear();
            sample.Clear();
            if (inventory == null) return;
            foreach (SlotArea area in new[] { SlotArea.Main, SlotArea.Tool })
                for (int i = 0; i < inventory.GetSlotCount(area); i++)
                {
                    InventorySlotData s = inventory.GetSlot(area, i);
                    if (s == null || s.IsEmpty) continue;
                    string k = Key(s.item, s.quality);
                    into.TryGetValue(k, out int n);
                    into[k] = n + s.quantity;
                    sample[k] = s;
                }
        }

        private void HandleInventoryChanged()
        {
            if (!snapshotReady || quiet || TitleScreen.IsShowing) return;
            Count(scratch);
            foreach (var kv in scratch)
            {
                owned.TryGetValue(kv.Key, out int before);
                if (sample[kv.Key].item is FarmingSystem.Fishing.FishData) continue; // 물고기는 잡았을 때 창이 따로 뜸
                if (kv.Value > before) Show(sample[kv.Key].item, sample[kv.Key].quality, kv.Value - before);
            }
            owned.Clear();
            foreach (var kv in scratch) owned[kv.Key] = kv.Value;
        }

        private void HandleCollection(CropData crop)
        {
            if (crop == null || TitleScreen.IsShowing) return;
            Toast t = NewToast("collection:" + crop.name, crop, ItemQuality.Normal, true);
            t.label.text = $"<color=#{ColorUtility.ToHtmlStringRGB(CountColor)}><b>도감 등록!</b></color>  {crop.cropName}";
            Resize(t);
        }

        // ───────────────────────── 알림 줄 ─────────────────────────

        private void Show(ItemData item, ItemQuality quality, int amount)
        {
            GameAudio.Play(SoundId.ItemPickup);
            string key = Key(item, quality);
            Toast t = toasts.Find(x => x.key == key);
            if (t != null)
            {
                // 이미 떠 있으면 개수만 올리고 시간을 다시 채움
                t.count += amount;
                t.age = Mathf.Min(t.age, 0.2f);
                t.punch = 1f;
            }
            else
            {
                t = NewToast(key, item, quality, false);
                t.count = amount;
            }
            string star = item is CropData && (quality == ItemQuality.Perfect || quality == ItemQuality.Good)
                ? $" <color=#{ColorUtility.ToHtmlStringRGB(quality == ItemQuality.Perfect ? PerfectColor : GoodColor)}>★</color>" : "";
            t.label.text = $"{item.itemName}{star}  <color=#{ColorUtility.ToHtmlStringRGB(CountColor)}><b>×{t.count}</b></color>";
            Resize(t);
        }

        private Toast NewToast(string key, ItemData item, ItemQuality quality, bool collection)
        {
            if (canvas == null) Build();
            var go = new GameObject("Toast", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            go.transform.SetParent(canvas.transform, false);
            var t = new Toast { key = key, item = item, quality = quality, rt = (RectTransform)go.transform, group = go.GetComponent<CanvasGroup>() };
            t.rt.anchorMin = t.rt.anchorMax = Vector2.zero;
            t.rt.pivot = new Vector2(0f, 0f);
            t.rt.sizeDelta = new Vector2(240f, RowHeight);
            t.y = Corner.y;
            t.rt.anchoredPosition = new Vector2(Corner.x - 60f, t.y); // 왼쪽에서 살짝 밀려 들어옴
            t.group.alpha = 0f;
            t.group.blocksRaycasts = t.group.interactable = false;
            Image bg = go.GetComponent<Image>();
            bg.sprite = panelSprite;
            bg.type = Image.Type.Sliced;
            bg.color = collection ? CollectionFill : Fill;
            bg.raycastTarget = false;

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(go.transform, false);
            var irt = (RectTransform)iconGo.transform;
            irt.anchorMin = irt.anchorMax = new Vector2(0f, 0.5f);
            irt.pivot = new Vector2(0f, 0.5f);
            irt.anchoredPosition = new Vector2(12f, 0f);
            irt.sizeDelta = new Vector2(RowHeight - 16f, RowHeight - 16f);
            t.icon = iconGo.GetComponent<Image>();
            t.icon.sprite = item.icon;
            t.icon.enabled = item.icon != null;
            t.icon.preserveAspect = true;
            t.icon.raycastTarget = false;

            var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(go.transform, false);
            var trt = (RectTransform)textGo.transform;
            trt.anchorMin = new Vector2(0f, 0f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.offsetMin = new Vector2(item.icon != null ? RowHeight + 2f : 18f, 0f);
            trt.offsetMax = new Vector2(-16f, 0f);
            t.label = textGo.GetComponent<TextMeshProUGUI>();
            if (font != null) t.label.font = font;
            t.label.fontSize = 24f;
            t.label.color = TextColor;
            t.label.alignment = TextAlignmentOptions.MidlineLeft;
            t.label.textWrappingMode = TextWrappingModes.NoWrap;
            t.label.overflowMode = TextOverflowModes.Overflow;
            t.label.richText = true;
            t.label.raycastTarget = false;

            toasts.Insert(0, t);
            // 너무 많으면 가장 오래된 것부터 바로 사라지게
            for (int i = MaxToasts; i < toasts.Count; i++) toasts[i].age = Mathf.Max(toasts[i].age, ShowSeconds);
            return t;
        }

        // 글자 길이에 맞춰 줄 너비
        private static void Resize(Toast t)
        {
            float left = t.icon.enabled ? RowHeight + 2f : 18f;
            float w = Mathf.Clamp(left + t.label.GetPreferredValues(t.label.text).x + 22f, 180f, 560f);
            t.rt.sizeDelta = new Vector2(w, RowHeight);
        }

        private void Animate()
        {
            float dt = Time.unscaledDeltaTime;
            float k = 1f - Mathf.Exp(-14f * dt);

            // 다른 창이 열려 있으면 알림을 숨기고 시간도 멈춤 → 창을 닫으면 처음부터 다시 보여 줌
            bool covered = UIInputBlocker.IsBlocked || PhoneMenu.IsOpen || (inventory != null && inventory.IsInventoryOpen);
            if (covered)
            {
                foreach (Toast t in toasts)
                {
                    t.held = true;
                    t.group.alpha = 0f;
                }
                return;
            }

            for (int i = toasts.Count - 1; i >= 0; i--)
            {
                Toast t = toasts[i];
                if (t.held)
                {
                    t.held = false;
                    t.age = i < MaxToasts ? 0f : ShowSeconds; // 너무 많이 쌓였으면 새 것 5개만
                    t.rt.anchoredPosition = new Vector2(Corner.x - 60f, t.y); // 다시 왼쪽에서 밀려 들어옴
                }
                t.age += dt;
                if (t.age >= ShowSeconds + FadeSeconds) { Destroy(t.rt.gameObject); toasts.RemoveAt(i); continue; }

                float alpha = t.age < 0.2f ? t.age / 0.2f : t.age > ShowSeconds ? 1f - (t.age - ShowSeconds) / FadeSeconds : 1f;
                t.group.alpha = Mathf.Clamp01(alpha);
                t.y = Mathf.Lerp(t.y, Corner.y + i * (RowHeight + RowGap), k);
                float x = Mathf.Lerp(t.rt.anchoredPosition.x, Corner.x, k);
                t.rt.anchoredPosition = new Vector2(x, t.y);
                t.punch = Mathf.Max(0f, t.punch - dt * 5f);
                float s = 1f + 0.08f * t.punch;
                t.rt.localScale = new Vector3(s, s, 1f);
            }
        }

        // ───────────────────────── 만들기 ─────────────────────────

        private void Build()
        {
            font = UIFont.FindKorean();
            var go = new GameObject("ItemToastCanvas", typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 600; // 게임 화면 UI 위, 메뉴(폰)·시작 화면 아래
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            panelSprite = MakePanelSprite();
        }

        // 둥근 사각형 + 갈색 테두리 (아이템 설명 창과 같은 모양). 흰 바탕이라 색을 곱해 쓸 수 있음
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