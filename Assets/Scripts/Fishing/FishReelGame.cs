using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using FarmingSystem.Audio;
using FarmingSystem.Save;

namespace FarmingSystem.Fishing
{
    // 낚시 릴 감기 미니게임 (찌를 물고 클릭한 뒤 3~5초)
    // - 캐릭터 옆에 반원 게이지가 뜨고 바늘이 좌우로 흔들림 → 바늘이 초록 칸에 있을 때 클릭(또는 Space)하면 "철컥!" 한 번 감김
    // - 희귀할수록 초록 칸이 좁고 바늘이 빠르고 여러 번 감아야 함 (흔함 1번 · 보통 2번 · 희귀 3번 · 전설 3번)
    // - 2번 헛치거나 너무 오래 걸리면 물고기가 도망감. 오른쪽 클릭·WASD = 포기
    // - FishingManager가 Run()을 불러서 기다렸다가 LastSuccess를 봄 (씬에 놓을 필요 없음)
    public class FishReelGame : MonoBehaviour
    {
        public static bool LastSuccess { get; private set; }

        private const int MaxFails = 2;
        private const float TimeLimit = 10f;
        private static readonly Color Track = new Color(0.12f, 0.16f, 0.22f, 0.75f);
        private static readonly Color Zone = new Color(0.45f, 0.95f, 0.45f, 1f);
        private static readonly Color ZoneHit = new Color(1f, 0.9f, 0.3f, 1f);
        private static readonly Color Needle = new Color(1f, 0.98f, 0.92f, 1f);
        private static readonly Color PipOff = new Color(1f, 1f, 1f, 0.25f);
        private static readonly Color Fail = new Color(1f, 0.45f, 0.45f, 1f);

        private static FishReelGame instance;
        private Canvas canvas;
        private RectTransform root, needle, zoneRt;
        private Image zoneImg, trackImg;
        private Image[] pips = new Image[0];
        private TextMeshProUGUI title, popup, info;
        private Sprite ring, dot;
        private float popupLife;

        /// <summary>미니게임 한 판. 끝나면 LastSuccess에 결과. onPull(성공 여부)는 감을 때마다 (물보라·찌 연출용)</summary>
        public static IEnumerator Run(FishData fish, Transform follow, Action<bool> onPull = null)
        {
            if (instance == null)
            {
                var go = new GameObject("[FishReelGame]");
                instance = go.AddComponent<FishReelGame>();
            }
            yield return instance.Play(fish, follow, onPull);
        }

        private IEnumerator Play(FishData fish, Transform follow, Action<bool> onPull)
        {
            Build();
            FishRarity r = fish != null ? fish.rarity : FishRarity.Common;
            int pulls = r == FishRarity.Common ? 1 : r == FishRarity.Uncommon ? 2 : 3;
            float zoneWidth = r == FishRarity.Common ? 0.3f : r == FishRarity.Uncommon ? 0.24f : r == FishRarity.Rare ? 0.18f : 0.13f; // 반원 중 비율
            float speed = r == FishRarity.Common ? 0.9f : r == FishRarity.Uncommon ? 1.1f : r == FishRarity.Rare ? 1.35f : 1.6f;     // 초당 왕복 정도

            SetPips(pulls);
            int done = 0, fails = 0;
            float zoneStart = NewZone(zoneWidth, -1f);
            float phase = UnityEngine.Random.value * Mathf.PI * 2f;
            float elapsed = 0f, hitFlash = 0f, shake = 0f;
            bool success = false;
            title.text = "릴 감기!  <size=75%>초록 칸에서 클릭</size>";
            info.text = "";
            popup.text = "";
            canvas.enabled = true;
            Place(follow);
            root.localScale = Vector3.one * 0.6f;
            yield return null; // 찌를 문 순간의 클릭이 바로 감기로 들어가지 않게 한 프레임 쉼

            float appear = 0f;
            while (true)
            {
                float dt = Time.unscaledDeltaTime;
                elapsed += dt;
                appear = Mathf.MoveTowards(appear, 1f, dt * 6f);
                phase += dt * speed * Mathf.PI;
                float u = 0.5f - 0.5f * Mathf.Cos(phase); // 0(왼쪽) ~ 1(오른쪽), 양 끝에서 살짝 느려짐

                // 위치: 캐릭터 머리 오른쪽 위
                Place(follow);
                root.localScale = Vector3.one * (0.6f + 0.4f * EaseOut(appear));
                shake = Mathf.Max(0f, shake - dt * 4f);
                root.anchoredPosition += new Vector2(Mathf.Sin(elapsed * 60f) * 6f * shake, 0f);

                needle.localEulerAngles = new Vector3(0f, 0f, (0.5f - u) * 180f);
                hitFlash = Mathf.Max(0f, hitFlash - dt * 3f);
                zoneImg.color = Color.Lerp(Zone, ZoneHit, hitFlash);
                UpdatePopup(dt);

                if (Cancelled()) break;
                if (elapsed > TimeLimit) { info.text = "<color=#FF7777>시간 초과!</color>"; break; }

                if (Clicked())
                {
                    bool inZone = u >= zoneStart && u <= zoneStart + zoneWidth;
                    if (inZone)
                    {
                        done++;
                        hitFlash = 1f;
                        pips[done - 1].color = Zone;
                        Popup("철컥!", ZoneHit);
                        GameAudio.Play(SoundId.UIClick);
                        GameAudio.Play(SoundId.FishNibble);
                        onPull?.Invoke(true);
                        if (done >= pulls) { success = true; break; }
                        zoneStart = NewZone(zoneWidth, zoneStart); // 다음 칸은 다른 자리
                        speed *= 1.12f;                            // 감을수록 조금씩 빨라짐
                    }
                    else
                    {
                        fails++;
                        shake = 1f;
                        Popup(fails >= MaxFails ? "툭…" : "헛침!", Fail);
                        GameAudio.Play(SoundId.UIClose);
                        onPull?.Invoke(false);
                        info.text = $"<color=#FF8888>헛침 {fails}/{MaxFails}</color>";
                        if (fails >= MaxFails) break;
                    }
                }
                yield return null;
            }

            LastSuccess = success;
            // 잠깐 결과를 보여 주고 사라짐
            for (float t = 0f; t < 0.35f; t += Time.unscaledDeltaTime)
            {
                Place(follow);
                UpdatePopup(Time.unscaledDeltaTime);
                root.localScale = Vector3.one * (1f + (success ? 0.15f : -0.2f) * (t / 0.35f));
                yield return null;
            }
            canvas.enabled = false;
        }

        // 새 초록 칸 시작 위치 (0~1-폭). 이전 칸과 너무 가깝지 않게
        private float NewZone(float width, float previous)
        {
            float start = 0f;
            for (int i = 0; i < 6; i++)
            {
                start = UnityEngine.Random.Range(0.05f, 0.95f - width);
                if (previous < 0f || Mathf.Abs(start - previous) > 0.2f) break;
            }
            zoneImg.fillAmount = width * 0.5f;                       // 반원(=원의 절반) 중 width만큼
            zoneRt.localEulerAngles = new Vector3(0f, 0f, -start * 180f); // 왼쪽에서 시계 방향으로 start만큼 돌림
            return start;
        }

        private static bool Clicked()
        {
            return (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                   || (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame);
        }

        private static bool Cancelled()
        {
            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame) return true;
            Keyboard kb = Keyboard.current;
            return kb != null && (kb.wKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame);
        }

        private void Place(Transform follow)
        {
            Camera cam = Camera.main;
            RectTransform cr = (RectTransform)canvas.transform;
            Vector2 pos = new Vector2(260f, 120f);
            if (cam != null && follow != null)
            {
                Vector3 sp = cam.WorldToScreenPoint(follow.position + Vector3.up * 2.2f);
                if (sp.z > 0f && RectTransformUtility.ScreenPointToLocalPointInRectangle(cr, sp, null, out Vector2 local))
                    pos = local + new Vector2(190f, 30f);
            }
            Vector2 half = cr.rect.size * 0.5f - new Vector2(170f, 140f);
            pos.x = Mathf.Clamp(pos.x, -half.x, half.x);
            pos.y = Mathf.Clamp(pos.y, -half.y, half.y);
            root.anchoredPosition = pos;
        }

        private void Popup(string text, Color c)
        {
            popup.text = text;
            popup.color = c;
            popupLife = 0.6f;
        }

        private void UpdatePopup(float dt)
        {
            if (popupLife <= 0f) { popup.text = ""; return; }
            popupLife -= dt;
            Color c = popup.color;
            c.a = Mathf.Clamp01(popupLife / 0.25f);
            popup.color = c;
            popup.rectTransform.anchoredPosition = new Vector2(0f, 120f + (0.6f - popupLife) * 40f);
        }

        private static float EaseOut(float t) => 1f - (1f - t) * (1f - t) * (1f - t);

        // ───────────── 화면 만들기 ─────────────

        private void SetPips(int count)
        {
            foreach (Image p in pips) if (p != null) Destroy(p.gameObject);
            pips = new Image[count];
            float gap = 34f;
            for (int i = 0; i < count; i++)
            {
                RectTransform rt = NewRect("Pip", root);
                rt.anchoredPosition = new Vector2((i - (count - 1) * 0.5f) * gap, -28f);
                rt.sizeDelta = new Vector2(22f, 22f);
                pips[i] = Img(rt, dot, PipOff);
            }
        }

        private void Build()
        {
            if (canvas != null) return;
            DontDestroyOnLoad(gameObject);
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 600;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            ring = RingSprite(256, 0.36f);
            dot = RingSprite(64, 0f);
            TMP_FontAsset font = UIFont.FindKorean();
            const float size = 230f;

            root = NewRect("Reel", transform);
            root.sizeDelta = new Vector2(size, size);

            // 반원 바탕 (왼쪽 → 위 → 오른쪽)
            RectTransform back = NewRect("Track", root);
            back.sizeDelta = new Vector2(size + 16f, size + 16f);
            trackImg = Img(back, ring, Track);
            HalfFill(trackImg, 0.5f);

            // 초록 칸
            zoneRt = NewRect("Zone", root);
            zoneRt.sizeDelta = new Vector2(size, size);
            zoneImg = Img(zoneRt, ring, Zone);
            HalfFill(zoneImg, 0.1f);

            // 바늘 (가운데 아래가 축)
            needle = NewRect("Needle", root);
            needle.pivot = new Vector2(0.5f, 0f);
            needle.sizeDelta = new Vector2(8f, size * 0.52f);
            var nImg = needle.gameObject.AddComponent<Image>();
            nImg.color = Needle;
            nImg.raycastTarget = false;
            RectTransform hub = NewRect("Hub", root);
            hub.sizeDelta = new Vector2(26f, 26f);
            Img(hub, dot, Needle);

            title = Text(root, font, 26f);
            title.rectTransform.anchoredPosition = new Vector2(0f, size * 0.5f + 34f);
            info = Text(root, font, 22f);
            info.rectTransform.anchoredPosition = new Vector2(0f, -62f);
            popup = Text(root, font, 40f);
            popup.fontStyle = FontStyles.Bold;
            popup.outlineWidth = 0.2f;
            popup.outlineColor = new Color32(40, 30, 20, 255);
            title.outlineWidth = info.outlineWidth = 0.2f;
            title.outlineColor = info.outlineColor = new Color32(30, 30, 40, 255);
            canvas.enabled = false;
        }

        // 원 모양을 왼쪽(9시)에서 시계 방향으로 amount(0~1)만큼만 그림 → 0.5 = 위쪽 반원
        private static void HalfFill(Image img, float amount)
        {
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Radial360;
            img.fillOrigin = (int)Image.Origin360.Left;
            img.fillClockwise = true;
            img.fillAmount = amount;
        }

        private static RectTransform NewRect(string n, Transform parent)
        {
            var go = new GameObject(n, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            return rt;
        }

        private static Image Img(RectTransform rt, Sprite s, Color c)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = s;
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        private static TextMeshProUGUI Text(Transform parent, TMP_FontAsset font, float fontSize)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.fontSize = fontSize;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Color.white;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            t.rectTransform.sizeDelta = new Vector2(420f, 60f);
            return t;
        }

        // 고리(inner = 안쪽 구멍 반지름 비율, 0이면 꽉 찬 원). 가장자리 부드럽게
        private static Sprite RingSprite(int n, float inner)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            float c = (n - 1) * 0.5f, outerR = n * 0.5f - 1f, innerR = n * inner;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c));
                    float a = Mathf.Clamp01(outerR - d + 0.5f);
                    if (inner > 0f) a = Mathf.Min(a, Mathf.Clamp01(d - innerR + 0.5f));
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
