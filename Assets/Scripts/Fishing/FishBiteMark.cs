using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FarmingSystem.Player;

namespace FarmingSystem.Fishing
{
    // 물고기가 찌를 물면 캐릭터 머리 위에 "!" 말풍선 (스타듀밸리·모동숲처럼) → 지금 클릭하면 잡힘
    //  - 톡 튀어나오며 커졌다가 제자리 + 살짝 떨림, 클릭하거나 놓치면 바로 사라짐
    //  - FishingManager.IsBiting만 읽음 (낚시 판정·시간은 그대로)
    // 설치 필요 없음 (게임이 시작되면 저절로 생김)
    public class FishBiteMark : MonoBehaviour
    {
        private const float HeadGap = 0.35f;     // 머리 꼭대기에서 위로 (m)
        private const float Size = 86f;          // 말풍선 크기 (1920x1080 기준 px)
        private static readonly Color BubbleColor = new Color(1f, 0.98f, 0.93f, 1f);
        private static readonly Color OutlineColor = new Color32(0x5B, 0x4A, 0x3A, 0xFF);
        private static readonly Color MarkColor = new Color32(0xE8, 0x3B, 0x3B, 0xFF);

        private static FishBiteMark instance;
        private Canvas canvas;
        private RectTransform canvasRect, bubble;
        private CanvasGroup group;
        private PlayerController player;
        private float shown;        // 보인 시간 (0 = 숨김)
        private bool wasBiting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (instance != null) return;
            var go = new GameObject("FishBiteMark");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<FishBiteMark>();
        }

        private void LateUpdate()
        {
            FishingManager fm = FishingManager.Instance;
            bool biting = fm != null && fm.isActiveAndEnabled && fm.IsBiting;
            if (biting && !wasBiting) shown = 0.0001f;
            if (!biting) shown = 0f;
            wasBiting = biting;

            if (shown <= 0f) { if (group != null) group.alpha = 0f; return; }
            if (canvas == null) Build();
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            Camera cam = Camera.main;
            if (player == null || cam == null) { group.alpha = 0f; return; }

            shown += Time.unscaledDeltaTime;
            Vector3 head = HeadTop(player.transform) + Vector3.up * HeadGap;
            Vector3 screen = cam.WorldToScreenPoint(head);
            if (screen.z <= 0f) { group.alpha = 0f; return; }
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local);

            // 톡! 튀어나옴 (0.18초, 살짝 넘쳤다 돌아옴) + 위로 살짝 + 좌우로 떨림
            float t = Mathf.Clamp01(shown / 0.18f);
            float pop = t < 1f ? Mathf.Sin(t * Mathf.PI * 0.5f) * (1f + 0.35f * Mathf.Sin(t * Mathf.PI)) : 1f;
            float shake = Mathf.Sin(shown * 55f) * 3f * Mathf.Clamp01(1f - (shown - 0.18f) * 2f);
            bubble.anchoredPosition = local + new Vector2(shake, 10f * (1f - t));
            bubble.localScale = Vector3.one * pop;
            group.alpha = 1f;
        }

        // 캐릭터 몸(메쉬)만으로 잰 머리 꼭대기 (낚싯줄·찌·효과는 뺌)
        private static Vector3 HeadTop(Transform p)
        {
            float top = float.MinValue;
            foreach (Renderer r in p.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !(r is SkinnedMeshRenderer || r is MeshRenderer)) continue;
                if (r.name.ToLowerInvariant().Contains("fish") || r.name.ToLowerInvariant().Contains("rod") || r.name.ToLowerInvariant().Contains("bobber")) continue;
                top = Mathf.Max(top, r.bounds.max.y);
            }
            if (top < p.position.y + 0.3f || top > p.position.y + 4f) top = p.position.y + 1.8f;
            return new Vector3(p.position.x, top, p.position.z);
        }

        private void Build()
        {
            var go = new GameObject("FishBiteMarkCanvas", typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 550; // 게임 화면 UI 위, 알림·메뉴 아래
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasRect = (RectTransform)go.transform;

            // 말풍선: 갈색 테두리 원 + 크림색 원 + 아래 꼬리 + 빨간 "!"
            var b = new GameObject("Bubble", typeof(RectTransform), typeof(CanvasGroup));
            b.transform.SetParent(go.transform, false);
            bubble = (RectTransform)b.transform;
            bubble.anchorMin = bubble.anchorMax = new Vector2(0.5f, 0.5f);
            bubble.pivot = new Vector2(0.5f, 0f); // 아래쪽(꼬리 끝)이 머리 위에 오게
            bubble.sizeDelta = new Vector2(Size, Size);
            group = b.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.alpha = 0f;

            Sprite circle = MakeCircle(128);
            Sprite tail = MakeTail(64);
            Img("Tail", bubble, tail, OutlineColor, new Vector2(0f, 2f), new Vector2(Size * 0.42f, Size * 0.34f), new Vector2(0.5f, 0f));
            Img("Outline", bubble, circle, OutlineColor, new Vector2(0f, Size * 0.2f), new Vector2(Size, Size), new Vector2(0.5f, 0f));
            Img("TailFill", bubble, tail, BubbleColor, new Vector2(0f, 7f), new Vector2(Size * 0.3f, Size * 0.25f), new Vector2(0.5f, 0f));
            Img("Fill", bubble, circle, BubbleColor, new Vector2(0f, Size * 0.2f + 5f), new Vector2(Size - 10f, Size - 10f), new Vector2(0.5f, 0f));

            var tgo = new GameObject("Mark", typeof(RectTransform));
            tgo.transform.SetParent(bubble, false);
            var mark = tgo.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset font = FarmingSystem.Save.UIFont.FindKorean();
            if (font != null) mark.font = font;
            mark.text = "!";
            mark.fontSize = Size * 0.78f;
            mark.fontStyle = FontStyles.Bold;
            mark.color = MarkColor;
            mark.alignment = TextAlignmentOptions.Center;
            mark.raycastTarget = false;
            var mrt = mark.rectTransform;
            mrt.anchorMin = mrt.anchorMax = new Vector2(0.5f, 0f);
            mrt.pivot = new Vector2(0.5f, 0.5f);
            mrt.anchoredPosition = new Vector2(0f, Size * 0.2f + Size * 0.5f);
            mrt.sizeDelta = new Vector2(Size, Size);
        }

        private static void Img(string name, Transform parent, Sprite sprite, Color color, Vector2 pos, Vector2 size, Vector2 pivot)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
        }

        private static Sprite MakeCircle(int n)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            float r = n * 0.5f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(r - d) * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        }

        // 아래를 가리키는 꼬리 (역삼각형)
        private static Sprite MakeTail(int n)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float half = (y + 0.5f) / n * 0.5f; // 아래(0)가 뾰족, 위가 넓음
                    float u = Mathf.Abs((x + 0.5f) / n - 0.5f);
                    float a = Mathf.Clamp01((half - u) * n);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
