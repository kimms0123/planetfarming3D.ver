using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using FarmingSystem.Building;
using FarmingSystem.Core;
using FarmingSystem.Economy;
using FarmingSystem.Inventory;
using FarmingSystem.Player;
using FarmingSystem.Rhythm;
using FarmingSystem.Save;
using Random = UnityEngine.Random;

namespace FarmingSystem.UI
{
    // 게임 중 메뉴 (모동숲 스마트폰처럼 - 우리는 폰 드는 모션이 없어서 캐릭터 앞에 홀로그램으로 띄움)
    //  - Esc: 열기/닫기 (가방·상점·대화·리듬게임·건물 설치 중엔 그 창이 먼저 닫히고, 메뉴는 안 열림)
    //  - 열면: 카메라가 캐릭터 앞으로 와서 캐릭터를 화면 오른쪽에, 왼쪽에 홀로그램 패널이 켜짐 (게임 시간 멈춤, 게임 화면 UI 숨김)
    //          + 캐릭터 앞 가슴 높이에도 반투명 직사각형 홀로그램 창이 같이 켜짐 (캐릭터가 그 창을 보는 느낌)
    //  - 앱: 설정(소리·마우스 감도·상하 반전·전체 화면) / 조작법 / 도감(작물 - CropCollectionWindow) / 게임 끄기(저장 후 종료)
    //  - 마우스: 올리면 고르고 클릭 / 키보드: 방향키(WASD) + Enter(Space·E), Esc = 뒤로·닫기
    // 설치 필요 없음 (게임이 시작되면 저절로 생김). 앱 아이콘·색을 바꾸려면 씬의 아무 오브젝트에 PhoneMenu를 붙이고 Inspector에서
    // 새 앱을 추가하려면 BuildHome()의 앱 목록에 한 줄 추가
    public class PhoneMenu : MonoBehaviour
    {
        [Header("앱 아이콘 (비우면 코드로 그린 기본 아이콘)")]
        [SerializeField] private Sprite settingsIcon;
        [SerializeField] private Sprite controlsIcon;
        [SerializeField] private Sprite collectionIcon;
        [SerializeField] private Sprite quitIcon;

        [Header("홀로그램")]
        [SerializeField] private Color holoColor = new Color(0.35f, 0.85f, 1f, 1f);
        [Tooltip("패널 위치 (화면 가운데 기준, 1920x1080)")]
        [SerializeField] private Vector2 panelPos = new Vector2(-420f, 10f);
        [SerializeField] private Vector2 panelSize = new Vector2(540f, 780f);

        [Header("동작")]
        [Tooltip("메뉴를 여는 동안 게임 시간 멈춤")]
        [SerializeField] private bool pauseTime = true;
        [Tooltip("열 때 카메라가 캐릭터 앞으로 와서 캐릭터를 화면 오른쪽에 보여 줌")]
        [SerializeField] private bool frameCamera = true;

        [Header("캐릭터 앞 홀로그램 화면 (게임 속에 떠 있는 작은 창)")]
        [SerializeField] private bool showWorldScreen = true;
        [Tooltip("창 크기 - 캐릭터 키에 대한 비율 (가로, 세로). 0.2 = 키의 20%")]
        [SerializeField] private Vector2 worldScreenSize = new Vector2(0.2f, 0.28f);
        [Tooltip("캐릭터 앞 거리 - 키에 대한 비율")]
        [SerializeField] private float worldScreenDistance = 0.3f;
        [Tooltip("창 가운데 높이 - 키에 대한 비율 (0 = 발, 0.5 = 허리~가슴, 1 = 머리 끝)")]
        [SerializeField, Range(0f, 1f)] private float worldScreenHeight = 0.4f;
        [Tooltip("캐릭터 얼굴 쪽으로 기울이는 각도 (도)")]
        [SerializeField] private float worldScreenTilt = 20f;

        public static bool IsOpen { get; private set; }
        public static PhoneMenu Instance { get; private set; }

        private enum Page { Home, Settings, Controls, Quit }

        private class App { public string name; public RectTransform rt; public Image tile, glyph; public bool locked; public Action open; }
        private class Row { public RectTransform rt; public Image highlight; public Action<int> step; public Action refresh; }

        private Canvas canvas;
        private TMP_FontAsset font;
        private RectTransform panel;
        private CanvasGroup panelGroup;
        private RawImage scan;
        private Canvas worldScreen;          // 캐릭터 앞에 떠 있는 작은 홀로그램 창
        private CanvasGroup worldGroup;
        private RawImage worldScan;
        private float worldPx = 0.001f;     // 창 1px이 몇 m인지 (캐릭터 키에 맞춰 정함)
        private const float WorldW = 300f, WorldH = 420f; // 창 그림 크기 (px)
        private TextMeshProUGUI statusLeft, statusTime, statusRight, title;
        private readonly Dictionary<Page, CanvasGroup> pages = new Dictionary<Page, CanvasGroup>();
        private readonly List<App> apps = new List<App>();
        private readonly List<Row> rows = new List<Row>();
        private Page page;
        private int appIndex, rowIndex, quitIndex;
        private RectTransform[] quitButtons;
        private bool busy, blockedLastFrame;
        private Sprite rounded, ring, gear, pad, power, book;
        private CropCollectionWindow collection; // 도감 창 (처음 열 때 만듦)

        // 연 동안 잠깐 꺼 둔 것
        private PlayerController player;
        private bool playerWasOn, clockWasPaused;
        private readonly List<Canvas> hiddenCanvases = new List<Canvas>();
        private ThirdPersonCameraRig rig;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindAnyObjectByType<PhoneMenu>() != null) return;
            new GameObject("PhoneMenu").AddComponent<PhoneMenu>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (IsOpen) { UIInputBlocker.Unblock(this); IsOpen = false; }
        }

        // ───────────────────────── 열기·닫기 ─────────────────────────

        private void Update()
        {
            bool esc = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
            if (!IsOpen)
            {
                // 다른 창이 Esc로 닫히는 그 프레임에 메뉴가 같이 열리지 않게, 지난 프레임에도 막혀 있지 않았을 때만
                bool canOpen = !UIInputBlocker.IsBlocked && !blockedLastFrame && !TitleScreen.IsShowing
                               && !(BuildingPlacer.Instance != null && BuildingPlacer.Instance.IsPlacing)
                               && !(HarvestRhythmGame.Instance != null && HarvestRhythmGame.Instance.IsPlaying)
                               && !(InventoryManager.Instance != null && InventoryManager.Instance.IsInventoryOpen);
                blockedLastFrame = UIInputBlocker.IsBlocked;
                if (esc && canOpen && !busy) StartCoroutine(Open());
                return;
            }
            if (busy) return;

            UpdateStatus();
            AnimateHolo();
            if (collection != null && collection.IsOpen)
            {
                if (!collection.Tick(esc)) StartCoroutine(BackFromCollection()); // Esc·뒤로 → 폰으로
                return;
            }
            switch (page)
            {
                case Page.Home: HomeInput(esc); break;
                case Page.Settings: SettingsInput(esc); break;
                case Page.Controls: if (esc || Confirm()) ShowPage(Page.Home); break;
                case Page.Quit: QuitInput(esc); break;
            }
            AnimateSelection();
        }

        private IEnumerator Open()
        {
            busy = true;
            IsOpen = true;
            if (canvas == null) Build();
            UIInputBlocker.Block(this);

            player = FindAnyObjectByType<PlayerController>();
            playerWasOn = player != null && player.enabled;
            if (playerWasOn) player.enabled = false;
            if (pauseTime && GameClock.Instance != null) { clockWasPaused = GameClock.Instance.Paused; GameClock.Instance.Paused = true; }
            foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
                if (c != canvas && c.isRootCanvas && c.enabled) { c.enabled = false; hiddenCanvases.Add(c); }
            if (frameCamera) FrameCamera();
            PlaceWorldScreen();

            canvas.enabled = true;
            appIndex = 0;
            ShowPageNow(Page.Home);
            UpdateStatus();
            // 홀로그램 켜지기: 세로로 펼쳐지며 깜빡
            for (float t = 0f; t < 0.32f; t += Time.unscaledDeltaTime)
            {
                float k = t / 0.32f;
                float s = 1f + 0.08f * Mathf.Sin(k * Mathf.PI); // 살짝 넘쳤다 돌아옴
                panel.localScale = new Vector3(Mathf.Lerp(0.9f, 1f, k), Mathf.SmoothStep(0.02f, 1f, k) * s, 1f);
                SetWorldScreen(Mathf.SmoothStep(0.02f, 1f, k) * s, panelGroup.alpha);
                panelGroup.alpha = Mathf.Min(1f, k * 1.6f) * (Random.value > 0.15f ? 1f : 0.5f);
                yield return null;
            }
            panel.localScale = Vector3.one;
            SetWorldScreen(1f, 1f);
            panelGroup.alpha = 1f;
            busy = false;
        }

        private IEnumerator Close()
        {
            busy = true;
            for (float t = 0f; t < 0.2f; t += Time.unscaledDeltaTime)
            {
                float k = t / 0.2f;
                panel.localScale = new Vector3(Mathf.Lerp(1f, 1.05f, k), Mathf.Lerp(1f, 0.02f, k * k), 1f);
                SetWorldScreen(Mathf.Lerp(1f, 0.02f, k * k), 1f - k);
                panelGroup.alpha = 1f - k;
                yield return null;
            }
            if (worldScreen != null) worldScreen.gameObject.SetActive(false);
            canvas.enabled = false;
            if (frameCamera && rig != null) rig.EndShot();
            foreach (Canvas c in hiddenCanvases) if (c != null) c.enabled = true;
            hiddenCanvases.Clear();
            if (pauseTime && GameClock.Instance != null) GameClock.Instance.Paused = clockWasPaused;
            if (playerWasOn && player != null) player.enabled = true;
            UIInputBlocker.Unblock(this);
            IsOpen = false;
            blockedLastFrame = true; // 닫은 Esc로 바로 다시 열리지 않게
            busy = false;
        }

        // 카메라: 캐릭터 앞에서 캐릭터를 화면 오른쪽에 (왼쪽이 홀로그램 자리)
        private void FrameCamera()
        {
            rig = FindAnyObjectByType<ThirdPersonCameraRig>();
            if (rig == null || player == null) return;
            Transform p = player.transform;
            Vector3 f = p.forward; f.y = 0f;
            if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
            f.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, f);          // 캐릭터의 오른쪽 = 카메라에서 보면 왼쪽
            Vector3 look = p.position + Vector3.up * 1.05f + right * 0.85f;
            Vector3 pos = p.position + f * 3.3f + Vector3.up * 1.5f + right * 0.6f;
            if (Physics.Linecast(p.position + Vector3.up * 1.2f, pos, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore) && !hit.transform.IsChildOf(p))
                pos = hit.point - (pos - p.position).normalized * 0.3f; // 벽·나무에 묻히지 않게
            rig.BeginShot(pos, Quaternion.LookRotation(look - pos, Vector3.up));
        }

        // ───────────────────────── 캐릭터 앞 홀로그램 창 ─────────────────────────

        // 캐릭터 앞 가슴 높이에 반투명 직사각형 창 (캐릭터 쪽을 보며 얼굴 쪽으로 살짝 기울어짐, 앞뒤 어느 쪽에서 봐도 보임)
        private void PlaceWorldScreen()
        {
            if (!showWorldScreen || player == null) return;
            if (worldScreen == null) BuildWorldScreen();
            Transform p = player.transform;
            Vector3 f = p.forward; f.y = 0f;
            if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
            f.Normalize();
            // 캐릭터 키 (모델 크기로 잼) → 높이·거리·크기를 키에 맞춤 (캐릭터 기준점이 발이 아니어도 맞게)
            float top = float.MinValue, bottom = float.MaxValue;
            foreach (Renderer r in p.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r.transform.IsChildOf(worldScreen.transform)) continue;
                top = Mathf.Max(top, r.bounds.max.y);
                bottom = Mathf.Min(bottom, r.bounds.min.y);
            }
            float height = top > bottom ? top - bottom : 1.6f;
            if (top <= bottom) bottom = p.position.y;
            worldPx = height * worldScreenSize.x / WorldW;
            Transform t = worldScreen.transform;
            t.SetParent(p, true);
            t.position = new Vector3(p.position.x, bottom, p.position.z) + f * (height * worldScreenDistance) + Vector3.up * (height * worldScreenHeight);
            ((RectTransform)t).sizeDelta = new Vector2(WorldW, WorldW * worldScreenSize.y / Mathf.Max(0.01f, worldScreenSize.x));
            // 화면 앞면이 캐릭터를 보게 + 위쪽을 캐릭터 쪽으로 기울임
            t.rotation = Quaternion.LookRotation(f, Vector3.up) * Quaternion.Euler(worldScreenTilt, 0f, 0f); // 화면 면(뒤쪽)이 캐릭터 얼굴 쪽을 보게
            worldScreen.gameObject.SetActive(true);
            SetWorldScreen(0.02f, 0f);
        }

        private void SetWorldScreen(float scaleY, float alpha)
        {
            if (worldScreen == null || !worldScreen.gameObject.activeSelf) return;
            Transform parent = worldScreen.transform.parent;
            float px = worldPx / (parent != null ? Mathf.Max(0.0001f, parent.lossyScale.x) : 1f); // 캐릭터 크기와 상관없이 정한 크기로
            worldScreen.transform.localScale = new Vector3(px, px * Mathf.Max(0.02f, scaleY), px);
            worldGroup.alpha = alpha;
        }

        private void BuildWorldScreen()
        {
            var go = new GameObject("HoloScreen", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            worldScreen = go.GetComponent<Canvas>();
            worldScreen.renderMode = RenderMode.WorldSpace;
            worldGroup = go.GetComponent<CanvasGroup>();
            worldGroup.blocksRaycasts = false;
            var rt = (RectTransform)go.transform;
            Vector2 size = new Vector2(WorldW, WorldH);
            rt.sizeDelta = size;

            Img(Rect("Glow", rt, new Vector2(0.5f, 0.5f), Vector2.zero, size + new Vector2(50f, 50f)), rounded, new Color(holoColor.r, holoColor.g, holoColor.b, 0.12f));
            Img(Stretch(Rect("Fill", rt, Vector2.one * 0.5f, Vector2.zero, Vector2.zero)), rounded, new Color(holoColor.r * 0.5f, holoColor.g * 0.8f, holoColor.b, 0.35f));
            var scanRt = Stretch(Rect("Scanlines", rt, Vector2.one * 0.5f, Vector2.zero, Vector2.zero), 8f);
            worldScan = scanRt.gameObject.AddComponent<RawImage>();
            worldScan.texture = ScanTexture();
            worldScan.color = new Color(holoColor.r, holoColor.g, holoColor.b, 0.35f);
            Img(Stretch(Rect("Border", rt, Vector2.one * 0.5f, Vector2.zero, Vector2.zero)), ring, new Color(holoColor.r, holoColor.g, holoColor.b, 1f));

            // 작은 앱 칸 (화면에 뭔가 떠 있는 느낌)
            float cell = size.x * 0.24f, gap = size.x * 0.06f;
            for (int i = 0; i < 4; i++)
            {
                Vector2 pos = new Vector2((i % 3 - 1) * (cell + gap), size.y * 0.12f - (i / 3) * (cell + gap));
                Img(Rect("App", rt, new Vector2(0.5f, 0.5f), pos, new Vector2(cell, cell)), rounded, new Color(1f, 1f, 1f, 0.35f));
            }
            Img(Rect("Bar", rt, new Vector2(0.5f, 1f), new Vector2(0f, -size.y * 0.09f), new Vector2(size.x * 0.55f, size.y * 0.035f)), rounded, new Color(1f, 1f, 1f, 0.5f));
            foreach (Graphic g in go.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;
            go.SetActive(false);
        }

        // ───────────────────────── 입력 ─────────────────────────

        private static bool KeyDown(Key k) => Keyboard.current != null && Keyboard.current[k].wasPressedThisFrame;
        private static bool Up() => KeyDown(Key.UpArrow) || KeyDown(Key.W);
        private static bool Down() => KeyDown(Key.DownArrow) || KeyDown(Key.S);
        private static bool Left() => KeyDown(Key.LeftArrow) || KeyDown(Key.A);
        private static bool Right() => KeyDown(Key.RightArrow) || KeyDown(Key.D);
        private static bool Confirm() => KeyDown(Key.Enter) || KeyDown(Key.NumpadEnter) || KeyDown(Key.Space) || KeyDown(Key.E);
        private const int Columns = 3;

        private void HomeInput(bool esc)
        {
            if (esc) { StartCoroutine(Close()); return; }
            if (Left()) appIndex = Mathf.Max(0, appIndex - 1);
            else if (Right()) appIndex = Mathf.Min(apps.Count - 1, appIndex + 1);
            else if (Up()) appIndex = Mathf.Max(0, appIndex - Columns);
            else if (Down()) appIndex = Mathf.Min(apps.Count - 1, appIndex + Columns);
            else if (Confirm()) OpenApp(appIndex);
            title.text = apps[appIndex].name;
        }

        private void OpenApp(int i)
        {
            if (busy || page != Page.Home || i < 0 || i >= apps.Count) return;
            appIndex = i;
            if (apps[i].locked) { StartCoroutine(Shake(apps[i].rt)); return; }
            apps[i].open?.Invoke();
        }

        private void SettingsInput(bool esc)
        {
            if (esc) { ShowPage(Page.Home); return; }
            if (Up()) rowIndex = (rowIndex + rows.Count - 1) % rows.Count;
            else if (Down()) rowIndex = (rowIndex + 1) % rows.Count;
            else if (Left()) rows[rowIndex].step?.Invoke(-1);
            else if (Right() || Confirm()) rows[rowIndex].step?.Invoke(1);
        }

        private void QuitInput(bool esc)
        {
            if (esc) { ShowPage(Page.Home); return; }
            if (Left() || Up()) quitIndex = 0;
            else if (Right() || Down()) quitIndex = 1;
            else if (Confirm()) { if (quitIndex == 0) QuitGame(); else ShowPage(Page.Home); }
        }

        private static void QuitGame()
        {
            if (SaveManager.Instance != null && SaveManager.Instance.IsPlaying) SaveManager.Instance.Save(force: true);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ───────────────────────── 도감 ─────────────────────────

        // 폰 패널이 접히고 → 큰 도감 창이 켜짐
        private IEnumerator OpenCollection()
        {
            busy = true;
            if (collection == null) collection = new CropCollectionWindow(canvas.transform, font, holoColor, rounded, ring);
            for (float t = 0f; t < 0.15f; t += Time.unscaledDeltaTime)
            {
                float k = t / 0.15f;
                panel.localScale = new Vector3(Mathf.Lerp(1f, 1.04f, k), Mathf.Lerp(1f, 0.02f, k * k), 1f);
                yield return null;
            }
            panel.gameObject.SetActive(false);
            panel.localScale = Vector3.one;
            collection.Open();
            busy = false;
        }

        // 도감 창이 다 닫힌 뒤 → 폰 패널이 다시 펼쳐짐
        private IEnumerator BackFromCollection()
        {
            busy = true;
            panel.gameObject.SetActive(true);
            for (float t = 0f; t < 0.2f; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / 0.2f);
                panel.localScale = new Vector3(Mathf.Lerp(0.95f, 1f, k), Mathf.Lerp(0.02f, 1f, k), 1f);
                yield return null;
            }
            panel.localScale = Vector3.one;
            busy = false;
        }

        // ───────────────────────── 화면 전환 ─────────────────────────

        private void ShowPage(Page p) { if (!busy) StartCoroutine(SwitchPage(p)); }

        private IEnumerator SwitchPage(Page p)
        {
            busy = true;
            CanvasGroup a = pages[page], b = pages[p];
            for (float t = 0f; t < 0.12f; t += Time.unscaledDeltaTime) { a.alpha = 1f - t / 0.12f; yield return null; }
            ShowPageNow(p);
            for (float t = 0f; t < 0.15f; t += Time.unscaledDeltaTime)
            {
                b.alpha = t / 0.15f;
                ((RectTransform)b.transform).anchoredPosition = new Vector2(Mathf.Lerp(24f, 0f, t / 0.15f), 0f);
                yield return null;
            }
            b.alpha = 1f;
            ((RectTransform)b.transform).anchoredPosition = Vector2.zero;
            busy = false;
        }

        private void ShowPageNow(Page p)
        {
            page = p;
            foreach (var kv in pages)
            {
                bool on = kv.Key == p;
                kv.Value.alpha = on ? 1f : 0f;
                kv.Value.blocksRaycasts = kv.Value.interactable = on;
            }
            title.text = p == Page.Home ? apps[appIndex].name : p == Page.Settings ? "설정" : p == Page.Controls ? "조작법" : "게임 끄기";
            if (p == Page.Settings) { rowIndex = 0; foreach (Row r in rows) r.refresh?.Invoke(); }
            if (p == Page.Quit) quitIndex = 1; // 실수로 끄지 않게 '취소'가 먼저
        }

        // ───────────────────────── 애니메이션 ─────────────────────────

        private void UpdateStatus()
        {
            GameClock c = GameClock.Instance;
            if (c != null)
            {
                int h = c.CurrentHour;
                statusTime.text = $"{(h < 12 ? "AM" : "PM")} {(h % 12 == 0 ? 12 : h % 12):00}:{c.CurrentMinute:00}";
                statusLeft.text = $"{GameClock.GetSeasonDisplayName(c.CurrentSeason)} {c.DayOfSeason}일";
            }
            statusRight.text = CurrencyManager.Instance != null ? $"{CurrencyManager.Instance.CurrentBells:N0}벨" : "";
        }

        private void AnimateHolo()
        {
            if (scan != null) scan.uvRect = new Rect(0f, Time.unscaledTime * 0.06f % 1f, 1f, panelSize.y / 6f); // 위로 흐르는 줄무늬
            panelGroup.alpha = 0.94f + 0.06f * Mathf.PerlinNoise(Time.unscaledTime * 6f, 0.3f);                   // 살짝 지직
            if (worldScreen != null && worldScreen.gameObject.activeSelf)
            {
                worldScan.uvRect = new Rect(0f, Time.unscaledTime * 0.25f % 1f, 1f, 18f);
                worldGroup.alpha = 0.85f + 0.15f * Mathf.PerlinNoise(Time.unscaledTime * 9f, 0.7f);
            }
        }

        private void AnimateSelection()
        {
            float k = 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime);
            for (int i = 0; i < apps.Count; i++)
            {
                bool sel = page == Page.Home && i == appIndex;
                float s = Mathf.Lerp(apps[i].rt.localScale.x, sel ? 1.1f : 1f, k);
                apps[i].rt.localScale = new Vector3(s, s, 1f);
            }
            for (int i = 0; i < rows.Count; i++)
            {
                Color c = rows[i].highlight.color;
                c.a = Mathf.Lerp(c.a, page == Page.Settings && i == rowIndex ? 0.16f : 0f, k);
                rows[i].highlight.color = c;
            }
            if (quitButtons != null)
                for (int i = 0; i < quitButtons.Length; i++)
                {
                    float s = Mathf.Lerp(quitButtons[i].localScale.x, page == Page.Quit && i == quitIndex ? 1.08f : 1f, k);
                    quitButtons[i].localScale = new Vector3(s, s, 1f);
                }
        }

        private IEnumerator Shake(RectTransform rt)
        {
            Vector2 p0 = rt.anchoredPosition;
            for (float t = 0f; t < 0.3f; t += Time.unscaledDeltaTime)
            {
                rt.anchoredPosition = p0 + new Vector2(Mathf.Sin(t * 60f) * 8f * (1f - t / 0.3f), 0f);
                yield return null;
            }
            rt.anchoredPosition = p0;
        }

        // ───────────────────────── 만들기 ─────────────────────────

        private Color Ink => Color.Lerp(holoColor, Color.white, 0.75f);

        private void Build()
        {
            font = UIFont.FindKorean();
            rounded = RoundRect(128, 40f, false);
            ring = RoundRect(128, 40f, true);
            gear = Icon(IconGear);
            pad = Icon(IconPad);
            power = Icon(IconPower);
            book = Icon(IconBook);

            var go = new GameObject("PhoneMenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 700;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            if (FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));

            // 패널 (바깥 은은한 빛 → 반투명 바탕 → 줄무늬 → 테두리)
            panel = Rect("Hologram", go.transform, new Vector2(0.5f, 0.5f), panelPos, panelSize);
            panelGroup = panel.gameObject.AddComponent<CanvasGroup>();
            Img(Rect("Glow", panel, new Vector2(0.5f, 0.5f), Vector2.zero, panelSize + new Vector2(60f, 60f)), rounded, new Color(holoColor.r, holoColor.g, holoColor.b, 0.07f));
            Img(Stretch(Rect("Fill", panel, Vector2.one * 0.5f, Vector2.zero, Vector2.zero)), rounded, new Color(holoColor.r * 0.4f, holoColor.g * 0.6f, holoColor.b * 0.8f, 0.55f));
            var scanRt = Stretch(Rect("Scanlines", panel, Vector2.one * 0.5f, Vector2.zero, Vector2.zero), 10f);
            scan = scanRt.gameObject.AddComponent<RawImage>();
            scan.texture = ScanTexture();
            scan.color = new Color(holoColor.r, holoColor.g, holoColor.b, 0.12f);
            scan.raycastTarget = false;
            Img(Stretch(Rect("Border", panel, Vector2.one * 0.5f, Vector2.zero, Vector2.zero)), ring, new Color(holoColor.r, holoColor.g, holoColor.b, 0.9f));

            // 위쪽 상태 줄 + 제목
            statusLeft = Text(panel, "", 22f, new Vector2(-165f, panelSize.y * 0.5f - 52f), new Vector2(160f, 34f), TextAlignmentOptions.Left);
            statusTime = Text(panel, "", 24f, new Vector2(0f, panelSize.y * 0.5f - 52f), new Vector2(200f, 34f), TextAlignmentOptions.Center);
            statusRight = Text(panel, "", 22f, new Vector2(165f, panelSize.y * 0.5f - 52f), new Vector2(160f, 34f), TextAlignmentOptions.Right);
            foreach (var t in new[] { statusLeft, statusTime, statusRight }) t.alpha = 0.7f;
            title = Text(panel, "", 40f, new Vector2(0f, panelSize.y * 0.5f - 130f), new Vector2(460f, 56f), TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;

            BuildHome();
            BuildSettings();
            BuildControls();
            BuildQuit();
            canvas.enabled = false;
        }

        private CanvasGroup NewPage(Page p)
        {
            var rt = Stretch(Rect(p.ToString(), panel, Vector2.one * 0.5f, Vector2.zero, Vector2.zero));
            var g = rt.gameObject.AddComponent<CanvasGroup>();
            pages[p] = g;
            if (p != Page.Home) BackButton(rt);
            return g;
        }

        private void BuildHome()
        {
            CanvasGroup g = NewPage(Page.Home);
            // 앱 목록 (새 앱은 여기에 한 줄 추가)
            AddApp(g.transform, "설정", settingsIcon != null ? settingsIcon : gear, new Color(0.35f, 0.8f, 0.85f), false, () => ShowPage(Page.Settings));
            AddApp(g.transform, "조작법", controlsIcon != null ? controlsIcon : pad, new Color(0.55f, 0.6f, 1f), false, () => ShowPage(Page.Controls));
            AddApp(g.transform, "도감", collectionIcon != null ? collectionIcon : book, new Color(0.45f, 0.9f, 0.55f), false, () => StartCoroutine(OpenCollection()));
            AddApp(g.transform, "게임 끄기", quitIcon != null ? quitIcon : power, new Color(1f, 0.5f, 0.55f), false, () => ShowPage(Page.Quit));

            // 페이지 점 (앱이 많아지면 페이지를 늘릴 자리)
            var dot = Img(Rect("PageDot", g.transform, new Vector2(0.5f, 0f), new Vector2(0f, 46f), new Vector2(16f, 16f)), rounded, Ink);
            dot.raycastTarget = false;
        }

        private void AddApp(Transform parent, string appName, Sprite icon, Color color, bool locked, Action open)
        {
            int i = apps.Count;
            float size = 128f, gap = 28f;
            Vector2 pos = new Vector2((i % Columns - 1) * (size + gap), 120f - (i / Columns) * (size + gap + 26f));
            var rt = Rect(appName, parent, new Vector2(0.5f, 0.5f), pos, new Vector2(size, size));
            Image tile = Img(rt, rounded, new Color(color.r, color.g, color.b, locked ? 0.15f : 0.4f));
            Img(Stretch(Rect("Ring", rt, Vector2.one * 0.5f, Vector2.zero, Vector2.zero)), ring, new Color(color.r, color.g, color.b, locked ? 0.35f : 0.95f)).raycastTarget = false;
            Image glyph = Img(Rect("Icon", rt, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size * 0.62f, size * 0.62f)), icon, new Color(1f, 1f, 1f, locked ? 0.35f : 1f));
            glyph.preserveAspect = true;
            glyph.raycastTarget = false;
            var app = new App { name = appName, rt = rt, tile = tile, glyph = glyph, locked = locked, open = open };
            apps.Add(app);
            Hover(rt.gameObject, () => { if (page == Page.Home && !busy) { appIndex = i; title.text = appName; } }, () => OpenApp(i));
        }

        private void BuildSettings()
        {
            CanvasGroup g = NewPage(Page.Settings);
            float y = 150f;
            AddSlider(g.transform, "전체 소리", y, () => GameSettings.MasterVolume, v => GameSettings.MasterVolume = v, 0f, 1f, 0.1f);
            AddSlider(g.transform, "마우스 감도", y -= 100f, () => GameSettings.MouseSensitivity, v => GameSettings.MouseSensitivity = v, 0.2f, 3f, 0.1f);
            AddToggle(g.transform, "카메라 상하 반전", y -= 100f, () => GameSettings.InvertY, v => GameSettings.InvertY = v);
            AddToggle(g.transform, "전체 화면", y -= 100f, () => GameSettings.Fullscreen, v => GameSettings.Fullscreen = v);
        }

        private Row AddRow(Transform parent, string label, float y)
        {
            var rt = Rect(label, parent, new Vector2(0.5f, 0.5f), new Vector2(0f, y), new Vector2(panelSize.x - 50f, 84f));
            Image hl = Img(rt, rounded, new Color(1f, 1f, 1f, 0f));
            Text(rt, label, 26f, new Vector2(0f, 18f), new Vector2(panelSize.x - 90f, 36f), TextAlignmentOptions.Left);
            var r = new Row { rt = rt, highlight = hl };
            int index = rows.Count;
            Hover(rt.gameObject, () => { if (page == Page.Settings) rowIndex = index; }, null);
            rows.Add(r);
            return r;
        }

        private void AddSlider(Transform parent, string label, float y, Func<float> get, Action<float> set, float min, float max, float step)
        {
            Row r = AddRow(parent, label, y);
            var area = Rect("Slider", r.rt, new Vector2(0.5f, 0.5f), new Vector2(-40f, -18f), new Vector2(330f, 26f));
            Img(Stretch(Rect("Bar", area, Vector2.one * 0.5f, Vector2.zero, Vector2.zero), 0f, 9f), rounded, new Color(1f, 1f, 1f, 0.2f));
            var fillArea = Stretch(Rect("Fill Area", area, Vector2.one * 0.5f, Vector2.zero, Vector2.zero), 0f, 9f);
            var fill = Stretch(Rect("Fill", fillArea, Vector2.one * 0.5f, Vector2.zero, Vector2.zero));
            Img(fill, rounded, holoColor);
            var handleArea = Stretch(Rect("Handle Area", area, Vector2.one * 0.5f, Vector2.zero, Vector2.zero), 12f, 0f);
            var handle = Rect("Handle", handleArea, Vector2.zero, Vector2.zero, new Vector2(26f, 0f));
            handle.anchorMin = new Vector2(0f, 0f);
            handle.anchorMax = new Vector2(0f, 1f);
            Image hi = Img(handle, rounded, Color.white);

            Slider s = area.gameObject.AddComponent<Slider>();
            s.fillRect = fill;
            s.handleRect = handle;
            s.targetGraphic = hi;
            s.minValue = min;
            s.maxValue = max;
            TextMeshProUGUI value = Text(r.rt, "", 24f, new Vector2(190f, -18f), new Vector2(90f, 34f), TextAlignmentOptions.Right);
            bool syncing = false;
            r.refresh = () => { syncing = true; s.value = get(); value.text = $"{Mathf.RoundToInt(get() * 100f)}%"; syncing = false; };
            s.onValueChanged.AddListener(v => { if (syncing) return; set(v); value.text = $"{Mathf.RoundToInt(v * 100f)}%"; });
            r.step = d => { set(Mathf.Clamp(get() + d * step, min, max)); r.refresh(); };
            r.refresh();
        }

        private void AddToggle(Transform parent, string label, float y, Func<bool> get, Action<bool> set)
        {
            Row r = AddRow(parent, label, y);
            var box = Rect("Toggle", r.rt, new Vector2(0.5f, 0.5f), new Vector2(110f, -18f), new Vector2(200f, 40f));
            Image bi = Img(box, rounded, new Color(1f, 1f, 1f, 0.12f));
            TextMeshProUGUI t = Text(box, "", 24f, Vector2.zero, box.sizeDelta, TextAlignmentOptions.Center);
            Button b = box.gameObject.AddComponent<Button>();
            b.targetGraphic = bi;
            r.refresh = () => { bool on = get(); t.text = on ? "<  켜짐  >" : "<  꺼짐  >"; t.alpha = on ? 1f : 0.6f; };
            r.step = d => { set(!get()); r.refresh(); };
            b.onClick.AddListener(() => r.step(1));
            r.refresh();
        }

        private void BuildControls()
        {
            CanvasGroup g = NewPage(Page.Controls);
            string[,] keys =
            {
                { "W A S D", "이동" }, { "Shift", "달리기" }, { "마우스", "카메라 돌리기" }, { "Alt", "마우스 커서 보이기" },
                { "왼쪽 클릭", "밭 갈기 · 씨앗 심기 · 물 주기 · 수확" }, { "E", "말 걸기 · 수확 · 잠자기" }, { "Tab", "가방" },
                { "1 ~ 0", "핫바 고르기" }, { "마우스 휠", "도구 바꾸기" }, { "Esc", "메뉴 열기 · 닫기" },
            };
            float y = 200f;
            for (int i = 0; i < keys.GetLength(0); i++, y -= 50f)
            {
                var key = Rect("Key", g.transform, new Vector2(0.5f, 0.5f), new Vector2(-150f, y), new Vector2(150f, 38f));
                Img(key, rounded, new Color(1f, 1f, 1f, 0.14f));
                Text(key, keys[i, 0], 20f, Vector2.zero, key.sizeDelta, TextAlignmentOptions.Center).fontStyle = FontStyles.Bold;
                Text(g.transform, keys[i, 1], 21f, new Vector2(80f, y), new Vector2(290f, 38f), TextAlignmentOptions.Left);
            }
        }

        private void BuildQuit()
        {
            CanvasGroup g = NewPage(Page.Quit);
            Text(g.transform, "저장하고 게임을 끌까요?", 30f, new Vector2(0f, 70f), new Vector2(460f, 60f), TextAlignmentOptions.Center);
            Text(g.transform, "지금까지 한 것은 자동으로 저장돼요", 21f, new Vector2(0f, 20f), new Vector2(460f, 40f), TextAlignmentOptions.Center).alpha = 0.7f;
            quitButtons = new RectTransform[2];
            string[] labels = { "끄기", "취소" };
            for (int i = 0; i < 2; i++)
            {
                int index = i;
                var b = Rect(labels[i], g.transform, new Vector2(0.5f, 0.5f), new Vector2(i == 0 ? -100f : 100f, -80f), new Vector2(170f, 64f));
                Img(b, rounded, i == 0 ? new Color(1f, 0.5f, 0.55f, 0.45f) : new Color(1f, 1f, 1f, 0.15f));
                Text(b, labels[i], 28f, Vector2.zero, b.sizeDelta, TextAlignmentOptions.Center).fontStyle = FontStyles.Bold;
                Hover(b.gameObject, () => quitIndex = index, () => { if (index == 0) QuitGame(); else ShowPage(Page.Home); });
                quitButtons[i] = b;
            }
        }

        private void BackButton(RectTransform pageRt)
        {
            var b = Rect("Back", pageRt, new Vector2(0f, 1f), new Vector2(70f, -130f), new Vector2(90f, 50f));
            Img(b, rounded, new Color(1f, 1f, 1f, 0f));
            Text(b, "<", 40f, Vector2.zero, b.sizeDelta, TextAlignmentOptions.Center);
            Hover(b.gameObject, null, () => ShowPage(Page.Home));
        }

        // ───────────────────────── UI 도우미 ─────────────────────────

        private static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        private static RectTransform Stretch(RectTransform rt, float padX = 0f, float padY = -1f)
        {
            if (padY < 0f) padY = padX;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padX, padY);
            rt.offsetMax = new Vector2(-padX, -padY);
            return rt;
        }

        private static Image Img(RectTransform rt, Sprite sprite, Color color)
        {
            Image img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            if (sprite != null && sprite.border.sqrMagnitude > 0f) img.type = Image.Type.Sliced;
            return img;
        }

        private TextMeshProUGUI Text(Transform parent, string text, float size, Vector2 pos, Vector2 box, TextAlignmentOptions align)
        {
            var rt = Rect("Text", parent, new Vector2(0.5f, 0.5f), pos, box);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = Ink;
            t.alignment = align;
            t.raycastTarget = false;
            return t;
        }

        private static void Hover(GameObject go, Action enter, Action click)
        {
            EventTrigger et = go.AddComponent<EventTrigger>();
            AddTrigger(et, EventTriggerType.PointerEnter, enter);
            AddTrigger(et, EventTriggerType.PointerClick, click);
        }

        private static void AddTrigger(EventTrigger et, EventTriggerType type, Action a)
        {
            if (a == null) return;
            var e = new EventTrigger.Entry { eventID = type };
            e.callback.AddListener(_ => a());
            et.triggers.Add(e);
        }

        // ───────────────────────── 코드로 그리는 그림 ─────────────────────────

        // 둥근 사각형 (ringOnly = 테두리 4px만). 9-slice로 늘려도 모서리 유지
        private static Sprite RoundRect(int size, float radius, bool ringOnly)
        {
            const float line = 4f;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float a = Cover(x + 0.5f, y + 0.5f, 0f, size, radius);
                    if (ringOnly) a = Mathf.Clamp01(a - Cover(x + 0.5f, y + 0.5f, line, size - line, radius - line));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        // (px, py)가 [min, max] 정사각형 + 모서리 반지름 r 안에 얼마나 들어가는지 (0~1, 가장자리 부드럽게)
        private static float Cover(float px, float py, float min, float max, float r)
        {
            float cx = Mathf.Clamp(px, min + r, max - r), cy = Mathf.Clamp(py, min + r, max - r);
            float d = Vector2.Distance(new Vector2(px, py), new Vector2(cx, cy));
            float inBox = Mathf.Clamp01(Mathf.Min(Mathf.Min(px - min, max - px), Mathf.Min(py - min, max - py)) + 0.5f);
            return Mathf.Min(inBox, Mathf.Clamp01(r - d + 0.5f));
        }

        private static Texture2D ScanTexture()
        {
            var tex = new Texture2D(1, 6, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            for (int y = 0; y < 6; y++) tex.SetPixel(0, y, new Color(1f, 1f, 1f, y < 2 ? 1f : 0f));
            tex.Apply();
            return tex;
        }

        // 아이콘: (x, y) ∈ [-1, 1] → 칠할지. 4x4로 나눠 찍어서 가장자리를 부드럽게
        private static Sprite Icon(Func<float, float, bool> inside)
        {
            const int N = 128;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int hit = 0;
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                            if (inside((x + (sx + 0.5f) / 4f) / N * 2f - 1f, (y + (sy + 0.5f) / 4f) / N * 2f - 1f)) hit++;
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(hit * 255 / 16));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        }

        private static bool IconGear(float x, float y)
        {
            float r = Mathf.Sqrt(x * x + y * y), a = Mathf.Atan2(y, x);
            float tooth = Mathf.Repeat(a, Mathf.PI * 2f / 8f) < Mathf.PI / 8f ? 0.86f : 0.68f;
            return r < tooth && r > 0.3f;
        }

        private static bool IconPad(float x, float y)
        {
            bool body = RoundBox(x, y + 0.05f, 0.85f, 0.42f, 0.35f) || Circle(x + 0.55f, y + 0.25f, 0.33f) || Circle(x - 0.55f, y + 0.25f, 0.33f);
            bool cross = (Mathf.Abs(x + 0.45f) < 0.07f && Mathf.Abs(y - 0.05f) < 0.22f) || (Mathf.Abs(y - 0.05f) < 0.07f && Mathf.Abs(x + 0.45f) < 0.22f);
            bool buttons = Circle(x - 0.42f, y - 0.15f, 0.09f) || Circle(x - 0.58f, y - 0.0f, 0.09f) || Circle(x - 0.26f, y - 0.0f, 0.09f) || Circle(x - 0.42f, y + 0.15f, 0.09f);
            return body && !cross && !buttons;
        }

        private static bool IconPower(float x, float y)
        {
            float r = Mathf.Sqrt(x * x + y * y);
            bool arc = r < 0.78f && r > 0.58f && !(y > 0f && Mathf.Abs(x) < 0.32f);
            bool bar = Mathf.Abs(x) < 0.1f && y > -0.05f && y < 0.9f;
            return arc || bar;
        }

        private static bool IconBook(float x, float y)
        {
            bool left = RoundBox(x + 0.42f, y, 0.4f, 0.72f, 0.08f);
            bool right = RoundBox(x - 0.42f, y, 0.4f, 0.72f, 0.08f);
            bool lines = Mathf.Abs(y - 0.3f) < 0.04f || Mathf.Abs(y - 0.05f) < 0.04f || Mathf.Abs(y + 0.2f) < 0.04f;
            return (left || right) && !(lines && Mathf.Abs(x) > 0.15f && Mathf.Abs(x) < 0.7f);
        }

        private static bool Circle(float x, float y, float r) => x * x + y * y < r * r;

        private static bool RoundBox(float x, float y, float hx, float hy, float r)
        {
            float qx = Mathf.Abs(x) - hx + r, qy = Mathf.Abs(y) - hy + r;
            return Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f)) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r < 0f;
        }
    }
}