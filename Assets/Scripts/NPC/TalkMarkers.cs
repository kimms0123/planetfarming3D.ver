using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using FarmingSystem.Core;
using FarmingSystem.Dialogue;
using FarmingSystem.Economy;
using FarmingSystem.Save;
using FarmingSystem.UI.Shop;

namespace FarmingSystem.NPC
{
    // NPC·상인 머리 위 표시 (설치 필요 없음 - 게임이 시작되면 저절로 생김)
    //  - 말을 걸 수 있는 거리에서 바라보면: 캐릭터 옆에 작은 "E 대화하기"
    //  - NPC 말고도 IPromptSource로 등록한 것 (예: 우주선 "E 잠자기")
    //  - 줄 퀘스트가 있으면 노란 "!", 끝낼 수 있는 퀘스트가 있으면 초록 "?" (가까이 있을 때만, 살짝 위아래로 움직임)
    //    → NPC·상인의 대사 모음(Dialogue Set)에 퀘스트 묶음이 있을 때만 뜸
    //  - 대화·상점·리듬게임 등 다른 창이 열려 있거나 시작 화면에서는 안 보임
    // 끄고 싶으면 아래 ShowPrompt / ShowQuestMarks를 false로
    public class TalkMarkers : MonoBehaviour
    {
        public static bool ShowPrompt = true;
        public static bool ShowQuestMarks = true;
        private const float MarkDistance = 25f;   // 퀘스트 표시가 보이는 거리 (m)
        private const float RefreshSeconds = 0.5f; // NPC 목록·퀘스트 상태 다시 확인하는 간격

        private static TalkMarkers instance;

        /// <summary>NPC 말고도 "E ○○하기" 안내를 띄우고 싶은 것 (예: 우주선 잠자기). 지금 안내할 게 없으면 null을 돌려줌</summary>
        public interface IPromptSource
        {
            /// <summary>지금 E로 할 수 있으면 true + 안내 문구·띄울 위치(월드)</summary>
            bool TryGetPrompt(out string label, out Vector3 worldPos);
        }
        private static readonly List<IPromptSource> sources = new List<IPromptSource>();
        public static void Register(IPromptSource s) { if (s != null && !sources.Contains(s)) sources.Add(s); }
        public static void Unregister(IPromptSource s) => sources.Remove(s);
        private TextMeshProUGUI promptLabel;
        private string promptText = "대화하기";

        private class Target
        {
            public Transform t;
            public NPCDialogue npc;
            public ShopInteractable merchant;
            public float height = -1f;
            public float halfWidth = 0.4f;
            public QuestMark mark;
            public RectTransform icon;
            public TextMeshProUGUI iconText;
            public Image iconBg;
        }

        private readonly List<Target> targets = new List<Target>();
        private Canvas canvas;
        private RectTransform prompt;
        private CanvasGroup promptGroup;
        private TMP_FontAsset font;
        private Sprite circle, rounded;
        private float refreshTimer;
        private ShopDialogueUI shopDialogue;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (instance != null) return;
            var go = new GameObject("TalkMarkers");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<TalkMarkers>();
            SceneManager.sceneLoaded += (s, m) => { if (instance != null) instance.ResetTargets(); };
        }

        private void ResetTargets()
        {
            foreach (Target tg in targets) if (tg.icon != null) Destroy(tg.icon.gameObject);
            targets.Clear();
            shopDialogue = null;
            refreshTimer = 0f;
        }

        private void Update()
        {
            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer <= 0f) { refreshTimer = RefreshSeconds; Refresh(); }

            Camera cam = Camera.main;
            bool hidden = cam == null || TitleScreen.IsShowing || UIInputBlocker.IsBlocked;
            if (canvas == null) Build();

            // "E 대화하기"
            Target talk = null;
            if (!hidden && ShowPrompt)
                foreach (Target tg in targets)
                {
                    if (tg.t == null || !tg.t.gameObject.activeInHierarchy) continue;
                    bool can = tg.npc != null ? tg.npc.CanTalk && !tg.npc.IsTalking : tg.merchant != null && tg.merchant.CanInteract;
                    if (can) { talk = tg; break; }
                }
            // NPC가 없으면 다른 안내 (우주선 잠자기 등)
            bool extra = false;
            string extraLabel = null;
            Vector3 extraPos = Vector3.zero;
            if (talk == null && !hidden && ShowPrompt)
                foreach (IPromptSource src in sources)
                    if (src != null && src.TryGetPrompt(out extraLabel, out extraPos)) { extra = true; break; }

            promptGroup.alpha = Mathf.MoveTowards(promptGroup.alpha, talk != null || extra ? 1f : 0f, Time.unscaledDeltaTime * 8f);
            // 캐릭터 오른쪽 옆, 어깨~얼굴 높이에 붙임
            if (talk != null)
            {
                SetPromptText("대화하기");
                float h = Height(talk);
                Vector3 side = cam.transform.right * (talk.halfWidth + 0.15f);
                PlaceAt(prompt, cam, talk.t.position + Vector3.up * (h * 0.7f) + side);
            }
            else if (extra)
            {
                SetPromptText(extraLabel);
                PlaceAt(prompt, cam, extraPos);
            }

            // "!" "?"
            Vector3 eye = cam != null ? cam.transform.position : Vector3.zero;
            foreach (Target tg in targets)
            {
                bool show = !hidden && ShowQuestMarks && tg.mark != QuestMark.None && tg.t != null && tg.t.gameObject.activeInHierarchy
                            && (tg.t.position - eye).sqrMagnitude < MarkDistance * MarkDistance;
                if (show && tg.icon == null) MakeIcon(tg);
                if (tg.icon == null) continue;
                tg.icon.gameObject.SetActive(show);
                if (!show) continue;
                bool ready = tg.mark == QuestMark.Ready;
                tg.iconText.text = ready ? "?" : "!";
                tg.iconBg.color = ready ? new Color(0.45f, 0.85f, 0.45f) : new Color(1f, 0.82f, 0.25f);
                float bob = Mathf.Sin(Time.unscaledTime * 3f) * 0.08f;
                float above = Height(tg) + 0.45f + bob;
                PlaceAt(tg.icon, cam, tg.t.position + Vector3.up * above);
            }
        }

        // NPC 목록·퀘스트 상태 갱신
        private void Refresh()
        {
            targets.RemoveAll(tg =>
            {
                bool gone = tg.t == null;
                if (gone && tg.icon != null) Destroy(tg.icon.gameObject);
                return gone;
            });
            foreach (NPCDialogue n in FindObjectsByType<NPCDialogue>(FindObjectsInactive.Include))
                if (!targets.Exists(tg => tg.npc == n)) targets.Add(new Target { t = n.transform, npc = n });
            foreach (ShopInteractable m in FindObjectsByType<ShopInteractable>(FindObjectsInactive.Include))
                if (!targets.Exists(tg => tg.merchant == m)) targets.Add(new Target { t = m.transform, merchant = m });

            if (shopDialogue == null) shopDialogue = FindAnyObjectByType<ShopDialogueUI>(FindObjectsInactive.Include);
            foreach (Target tg in targets)
            {
                tg.mark = QuestMark.None;
                if (tg.npc != null && tg.npc.Dialogue != null) tg.mark = tg.npc.Dialogue.GetQuestMark(tg.npc.name);
                else if (tg.merchant != null && shopDialogue != null && shopDialogue.MerchantDialogue != null
                         && ShopManager.Instance != null && ShopManager.Instance.IsShopOpen)
                    tg.mark = shopDialogue.MerchantDialogue.GetQuestMark(shopDialogue.MerchantId);
            }
        }

        // 머리 높이 (처음 보일 때 모델 크기로 한 번 잼)
        private static float Height(Target tg)
        {
            if (tg.height > 0f) return tg.height;
            float top = float.MinValue;
            float half = 0f;
            Vector3 c = tg.t.position;
            foreach (Renderer r in tg.t.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r is ParticleSystemRenderer) continue;
                top = Mathf.Max(top, r.bounds.max.y);
                half = Mathf.Max(half, Mathf.Abs(r.bounds.max.x - c.x), Mathf.Abs(r.bounds.min.x - c.x), Mathf.Abs(r.bounds.max.z - c.z), Mathf.Abs(r.bounds.min.z - c.z));
            }
            tg.height = top > float.MinValue ? Mathf.Clamp(top - tg.t.position.y, 0.6f, 6f) : 2f;
            tg.halfWidth = half > 0f ? Mathf.Clamp(half * 0.6f, 0.2f, 1.2f) : 0.4f; // 몸 옆 (팔·꼬리 때문에 조금 줄임)
            return tg.height;
        }

        private static void PlaceAt(RectTransform rt, Camera cam, Vector3 world)
        {
            if (cam == null) return;
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) { rt.anchoredPosition = new Vector2(-9999f, -9999f); return; } // 카메라 뒤
            rt.position = new Vector3(sp.x, sp.y, 0f);
        }

        // ---------- 화면 UI ----------

        private void Build()
        {
            font = UIFont.FindKorean();
            // 유니티 기본 UI 그림은 실행 중엔 못 불러와서 코드로 직접 그림 (원 + 둥근 사각형)
            circle = MakeRoundSprite(64, 32f);
            rounded = MakeRoundSprite(32, 10f);

            var go = new GameObject("TalkMarkersCanvas", typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50; // 대화창·상점 창보다 아래
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = Mathf.Max(1f, Screen.height / 1080f);

            // "E 대화하기"
            var p = new GameObject("Prompt", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            p.transform.SetParent(go.transform, false);
            prompt = (RectTransform)p.transform;
            prompt.sizeDelta = new Vector2(112f, 30f);
            prompt.pivot = new Vector2(0f, 0.5f); // 왼쪽 끝이 캐릭터 옆에 붙음
            Image bg = p.GetComponent<Image>();
            bg.sprite = rounded;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.1f, 0.12f, 0.2f, 0.82f);
            bg.raycastTarget = false;
            promptGroup = p.GetComponent<CanvasGroup>();
            promptGroup.alpha = 0f;
            promptGroup.blocksRaycasts = false;

            var keyGo = new GameObject("Key", typeof(RectTransform), typeof(Image));
            keyGo.transform.SetParent(p.transform, false);
            var krt = (RectTransform)keyGo.transform;
            krt.anchorMin = krt.anchorMax = new Vector2(0f, 0.5f);
            krt.pivot = new Vector2(0f, 0.5f);
            krt.anchoredPosition = new Vector2(4f, 0f);
            krt.sizeDelta = new Vector2(22f, 22f);
            Image key = keyGo.GetComponent<Image>();
            key.sprite = rounded;
            key.type = Image.Type.Sliced;
            key.color = new Color(0.95f, 0.95f, 0.95f);
            key.raycastTarget = false;
            MakeText(keyGo.transform, "E", 15f, new Color(0.15f, 0.15f, 0.25f), FontStyles.Bold);

            var label = MakeText(p.transform, "대화하기", 15f, Color.white, FontStyles.Bold);
            promptLabel = label;
            var lrt = label.rectTransform;
            lrt.offsetMin = new Vector2(30f, 0f);
            lrt.offsetMax = new Vector2(-6f, 0f);
        }

        private void SetPromptText(string text)
        {
            if (promptLabel == null || string.IsNullOrEmpty(text) || text == promptText) return;
            promptText = text;
            promptLabel.text = text;
        }

        private void MakeIcon(Target tg)
        {
            if (canvas == null) Build();
            var go = new GameObject("QuestMark", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(canvas.transform, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(40f, 40f);
            rt.pivot = new Vector2(0.5f, 0f);
            Image img = go.GetComponent<Image>();
            img.sprite = circle;
            img.raycastTarget = false;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0.15f, 0.12f, 0.1f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);
            tg.icon = rt;
            tg.iconBg = img;
            tg.iconText = MakeText(go.transform, "!", 28f, new Color(0.2f, 0.15f, 0.1f), FontStyles.Bold);
        }

        // size×size 흰색 둥근 사각형 (radius = size/2 이면 원). 9-slice 테두리 = radius → 늘려도 모서리가 안 찌그러짐
        private static Sprite MakeRoundSprite(int size, float radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var px = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // 가장 가까운 모서리 원 중심까지의 거리로 가장자리를 부드럽게
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                    float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    byte a = (byte)(Mathf.Clamp01(radius - d + 0.5f) * 255f);
                    px[y * size + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply();
            float b = Mathf.Min(radius, half - 1f);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }

        private TextMeshProUGUI MakeText(Transform parent, string text, float size, Color color, FontStyles style)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var t = go.GetComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.fontStyle = style;
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false;
            return t;
        }
    }
}