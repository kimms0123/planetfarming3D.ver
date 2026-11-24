using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FarmingSystem.Player;
using FarmingSystem.Save;

namespace FarmingSystem.UI
{
    // 씨앗 심기·밭 갈기·물 주기처럼 캐릭터가 잠깐 멈춰서 하는 동작 중에 머리 위로 진행 막대를 보여 줌
    // → 기다리는 동안 "얼마나 남았는지" 보여서 덜 답답하게
    // - 게임이 시작되면 저절로 생김 (씬에 놓을 필요 없음). PlayerController는 고치지 않고 동작 끝나는 시간만 읽음
    // - 짧은 동작(MinSeconds 미만)은 막대 없이 그냥 진행
    public class ActionProgressBar : MonoBehaviour
    {
        private const float MinSeconds = 0.6f;  // 이보다 짧은 동작엔 막대 안 띄움
        private const float Width = 150f, Height = 16f;
        private static readonly Color Back = new Color(0.08f, 0.1f, 0.14f, 0.7f);
        private static readonly Color FillColor = new Color(0.55f, 0.95f, 0.55f, 1f);

        private static readonly FieldInfo EndField = typeof(PlayerController).GetField("actionEndTime", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo PlantField = typeof(PlayerController).GetField("plantDuration", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo HoeField = typeof(PlayerController).GetField("hoeDuration", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo WaterField = typeof(PlayerController).GetField("waterDuration", BindingFlags.NonPublic | BindingFlags.Instance);

        private PlayerController player;
        private Canvas canvas;
        private RectTransform root;
        private Image fill;
        private TextMeshProUGUI label;
        private CanvasGroup group;
        private float startTime, endTime;
        private bool wasBusy;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindAnyObjectByType<ActionProgressBar>() != null) return;
            var go = new GameObject("[ActionProgressBar]");
            DontDestroyOnLoad(go);
            go.AddComponent<ActionProgressBar>();
        }

        private void LateUpdate()
        {
            if (EndField == null) { enabled = false; return; } // 팀 스크립트 이름이 바뀌면 조용히 끔
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (player == null) return;

            bool busy = player.IsBusy && !TitleScreen.IsShowing;
            if (busy && !wasBusy)
            {
                startTime = Time.time;
                endTime = (float)EndField.GetValue(player);
                if (endTime - startTime >= MinSeconds)
                {
                    Build();
                    label.text = ActionName(endTime - startTime);
                }
            }
            wasBusy = busy;

            if (group == null) return;
            float len = endTime - startTime;
            bool show = busy && len >= MinSeconds;
            group.alpha = Mathf.MoveTowards(group.alpha, show ? 1f : 0f, Time.unscaledDeltaTime * (show ? 10f : 6f));
            if (group.alpha <= 0f) return;

            float k = len > 0f ? Mathf.Clamp01((Time.time - startTime) / len) : 1f;
            fill.fillAmount = show ? k : 1f;
            Place();
        }

        // 동작 길이로 어떤 동작인지 맞힘 (심기·갈기·물 주기)
        private string ActionName(float duration)
        {
            if (Near(PlantField, duration)) return "씨앗 심는 중…";
            if (Near(HoeField, duration)) return "밭 가는 중…";
            if (Near(WaterField, duration)) return "물 주는 중…";
            return "";
        }

        private bool Near(FieldInfo f, float duration) => f != null && Mathf.Abs((float)f.GetValue(player) - duration) < 0.05f;

        // 캐릭터 머리 위 (화면 좌표)
        private void Place()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 sp = cam.WorldToScreenPoint(player.transform.position + Vector3.up * 2.4f);
            if (sp.z <= 0f) { group.alpha = 0f; return; }
            RectTransform cr = (RectTransform)canvas.transform;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(cr, sp, null, out Vector2 local))
                root.anchoredPosition = local;
        }

        private void Build()
        {
            if (canvas != null) return;
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 450;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            Sprite round = RoundSprite();
            root = Rect("Bar", transform, new Vector2(Width + 8f, Height + 8f));
            group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            var bg = root.gameObject.AddComponent<Image>();
            bg.sprite = round; bg.type = Image.Type.Sliced; bg.color = Back; bg.raycastTarget = false;

            RectTransform f = Rect("Fill", root, new Vector2(Width, Height));
            fill = f.gameObject.AddComponent<Image>();
            fill.sprite = round;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.color = FillColor;
            fill.raycastTarget = false;

            var lgo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            lgo.transform.SetParent(root, false);
            label = lgo.GetComponent<TextMeshProUGUI>();
            TMP_FontAsset font = UIFont.FindKorean();
            if (font != null) label.font = font;
            label.fontSize = 22f;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.outlineWidth = 0.25f;
            label.outlineColor = new Color32(30, 30, 40, 255);
            label.raycastTarget = false;
            label.rectTransform.sizeDelta = new Vector2(300f, 30f);
            label.rectTransform.anchoredPosition = new Vector2(0f, 24f);
        }

        private static RectTransform Rect(string n, Transform parent, Vector2 size)
        {
            var go = new GameObject(n, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            return rt;
        }

        private static Sprite RoundSprite()
        {
            const int S = 32; const float R = 8f;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, R, S - R), cy = Mathf.Clamp(y + 0.5f, R, S - R);
                    float a = Mathf.Clamp01(R - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy)) + 0.5f);
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(R, R, R, R));
        }
    }
}
