using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using FarmingSystem.Core;
using FarmingSystem.Farming;
using FarmingSystem.Player;
using FarmingSystem.Inventory;
using FarmingSystem.NPC;

namespace FarmingSystem.Save
{
    // 시작 화면 (게임 씬 안에서 바로 - 모동숲 방식 + 큰 로고)
    //  1. 게임을 켜면 검은 화면에서 서서히 밝아지며 (게임 시간은 멈춤, NPC는 계속 돌아다님)
    //     - 주민(NPC)이 있으면: 게임을 켤 때마다 무작위로 1명을 골라 가까이서 계속 따라다님
    //     - 주민이 없으면: 캐릭터가 시작하는 자리 근처 풍경을 가까이서 (모동숲처럼, 멀리 전경 X)
    //  2. 위쪽에 큰 로고(고정), 아래에 "아무 키나 눌러 시작하세요"
    //  3. 아무 키 → 저장 칸 3개 (이어하기 / 새 게임 / 지우기)
    //  4. 칸을 고르면 카메라가 캐릭터 쪽으로 날아가며 평소 카메라로 이어지고 바로 게임 시작
    // 화면 UI는 코드로 만듦 (프리팹 필요 없음). 로고 이미지는 Logo 칸에 넣기 (비우면 글자 로고)
    // 만들기: Tools → 행성농사일지 → 저장 → 저장·시작 화면 만들기
    public class TitleScreen : MonoBehaviour
    {
        [Header("로고")]
        [Tooltip("로고 이미지 (PNG, Texture Type = Sprite). 비우면 글자로 '행성농사일지'")]
        [SerializeField] private Sprite logo;
        [SerializeField] private Vector2 logoSize = new Vector2(1000f, 460f);
        [SerializeField] private string titleText = "행성농사일지";
        [SerializeField] private string subtitleText = "Planet Farming Diary";
        [SerializeField] private string teamText = "우주개발인사본부";

        [Header("배경 카메라 - 주민이 있으면 주민 1명을 따라다님")]
        [Tooltip("주민(NPC)이 이 수 이상 있으면 게임을 켤 때마다 무작위로 1명을 골라 계속 따라다님. 적으면 가까운 섬 풍경")]
        [SerializeField] private int minResidentsToFollow = 1;
        [Tooltip("이 중에서 1명을 고름. 비우면 씬에 있는 NPC 전부 중에서")]
        [SerializeField] private NPCDialogue[] residents;
        [Tooltip("주민과 카메라 거리 (m)")]
        [SerializeField] private float residentDistance = 5.5f;
        [Tooltip("카메라 높이 (m, 주민 발 기준)")]
        [SerializeField] private float residentHeight = 2.4f;
        [Tooltip("주민 주위를 천천히 도는 속도 (도/초)")]
        [SerializeField] private float residentDrift = 3f;

        [Header("배경 카메라 - 주민이 없을 때 (가까운 섬 풍경)")]
        [Tooltip("보여 줄 곳 (이 오브젝트가 바라보는 쪽을 보여 줌). 비우면 캐릭터가 시작하는 자리")]
        [SerializeField] private Transform sceneryPoint;
        [SerializeField] private float sceneryDistance = 10f;
        [SerializeField] private float sceneryHeight = 3.2f;
        [Tooltip("좌우로 천천히 흔들리는 각도 (도)")]
        [SerializeField] private float sceneryWander = 15f;
        [Tooltip("시작 화면 동안 캐릭터를 숨김 (모동숲처럼 풍경·주민만)")]
        [SerializeField] private bool hidePlayer = true;
        [Tooltip("시작 화면 시각 (게임 시계를 이 시각으로 보여 줌, 음수면 그대로)")]
        [SerializeField] private float titleHour = 17.5f;

        [Header("칸 고르는 창 꾸미기 (메뉴가 UI_Blue 조각으로 자동 채움, 비우면 단색)")]
        [SerializeField] private Sprite panelSprite;
        [SerializeField] private Sprite buttonSprite;
        [SerializeField] private Sprite dangerButtonSprite;

        [Header("에디터 테스트")]
        [Tooltip("켜면 에디터에서 시작 화면을 건너뛰고 아래 칸으로 바로 시작 (빌드에선 항상 시작 화면)")]
        [SerializeField] private bool skipInEditor = false;
        [Tooltip("건너뛸 때 쓸 칸 (1~3). 저장이 있으면 이어하기, 없으면 새 게임")]
        [SerializeField, Range(1, 3)] private int editorSlot = 1;

        public static bool IsShowing { get; private set; }

        private enum State { Intro, PressKey, Slots, Starting }
        private State state;

        private ThirdPersonCameraRig rig;
        private readonly List<Behaviour> disabled = new List<Behaviour>();
        private readonly List<Canvas> hiddenCanvases = new List<Canvas>();
        // 배경 카메라
        private Transform followTarget;   // 이번에 고른 주민 (없으면 풍경)
        private float camYaw, sceneryYaw;
        private Vector3 sceneryCenter, camPos, camLook;
        private readonly List<Renderer> hiddenPlayer = new List<Renderer>();
        private int savedHour = -1, savedMinute;

        // UI
        private Canvas canvas;
        private TMP_FontAsset font;
        private Image fade;
        private RectTransform logoRt;
        private CanvasGroup promptGroup, slotGroup, logoGroup, cornerGroup;
        private readonly RectTransform[] cards = new RectTransform[SaveManager.SlotCount];
        private int confirmDelete = -1;

        private void Start()
        {
            rig = FindAnyObjectByType<ThirdPersonCameraRig>();
            if (Application.isEditor && skipInEditor) { QuickStart(editorSlot - 1); return; }
            EnterTitle();
        }

        // ───────────────────────── 시작 화면 켜기·끄기 ─────────────────────────

        private void EnterTitle()
        {
            IsShowing = true;
            UIInputBlocker.Block(this); // 농사·카메라 마우스 회전 막음 + 마우스 커서 보임
            if (GameClock.Instance != null)
            {
                GameClock.Instance.Paused = true;
                if (titleHour >= 0f)
                {
                    savedHour = GameClock.Instance.CurrentHour;
                    savedMinute = GameClock.Instance.CurrentMinute;
                    GameClock.Instance.LoadTime(GameClock.Instance.CurrentDay, Mathf.FloorToInt(titleHour), Mathf.FloorToInt(titleHour % 1f * 60f));
                }
            }

            // 캐릭터 조작·가방 단축키 끄기
            Disable(FindAnyObjectByType<PlayerController>());
            Disable(FindAnyObjectByType<InventoryInputHandler>());

            // 게임 화면 UI(핫바·시계 등) 숨기기
            foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
                if (c.isRootCanvas && c.enabled) { c.enabled = false; hiddenCanvases.Add(c); }

            BuildUI();
            SetupCamera();
            StartCoroutine(Intro());
        }

        private void Disable(Behaviour b)
        {
            if (b == null || !b.enabled) return;
            b.enabled = false;
            disabled.Add(b);
        }

        private void ExitTitle()
        {
            ShowPlayer();
            foreach (Behaviour b in disabled) if (b != null) b.enabled = true;
            disabled.Clear();
            foreach (Canvas c in hiddenCanvases) if (c != null) c.enabled = true;
            hiddenCanvases.Clear();
            if (GameClock.Instance != null) GameClock.Instance.Paused = false;
            UIInputBlocker.Unblock(this);
            IsShowing = false;
            if (canvas != null) Destroy(canvas.gameObject);
        }

        private void OnDestroy()
        {
            if (IsShowing) { UIInputBlocker.Unblock(this); IsShowing = false; }
        }

        private void QuickStart(int slot)
        {
            SaveManager sm = SaveManager.Instance;
            if (sm == null) return;
            if (SaveManager.HasSave(slot) && sm.LoadGame(slot)) return;
            sm.StartNewGame(slot);
        }

        // ───────────────────────── 흐름 ─────────────────────────

        private IEnumerator Intro()
        {
            state = State.Intro;
            promptGroup.alpha = 0f;
            logoGroup.alpha = 0f;
            cornerGroup.alpha = 0f;
            for (float t = 0f; t < 1.6f; t += Time.unscaledDeltaTime)
            {
                fade.color = new Color(0f, 0f, 0f, 1f - Smooth(t / 1.6f));
                logoGroup.alpha = Smooth((t - 0.6f) / 1f);
                yield return null;
            }
            fade.color = Color.clear;
            fade.raycastTarget = false;
            logoGroup.alpha = 1f;
            cornerGroup.alpha = 1f;
            state = State.PressKey;
        }

        private void Update()
        {
            if (!IsShowing) return;
            float dt = Time.unscaledDeltaTime;

            // 배경 카메라
            if (state != State.Starting) UpdateCamera(dt);

            // 로고: 흔들림 없이 고정 (칸 고르는 창이 나올 때만 살짝 위로 부드럽게 이동)
            if (logoRt != null)
            {
                float y = Mathf.Lerp(logoRt.anchoredPosition.y, LogoY, 1f - Mathf.Exp(-8f * dt));
                logoRt.anchoredPosition = new Vector2(0f, y);
                logoRt.localRotation = Quaternion.identity;
            }

            if (state == State.PressKey)
            {
                promptGroup.alpha = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 3f);
                if (AnyKeyDown()) StartCoroutine(ShowSlots(true));
            }
            else if (state == State.Slots)
            {
                Keyboard kb = Keyboard.current;
                if (kb != null && kb.escapeKey.wasPressedThisFrame)
                {
                    if (confirmDelete >= 0) { confirmDelete = -1; RefreshCards(); }
                    else StartCoroutine(ShowSlots(false));
                }
            }
        }

        private float LogoY => state == State.Slots || state == State.Starting ? -60f : -110f;

        private bool AnyKeyDown()
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject() &&
                Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) return false; // 버튼 클릭은 제외
            if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame && !Keyboard.current.escapeKey.wasPressedThisFrame) return true;
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) return true;
            if (Gamepad.current != null && (Gamepad.current.buttonSouth.wasPressedThisFrame || Gamepad.current.startButton.wasPressedThisFrame)) return true;
            return false;
        }

        private IEnumerator ShowSlots(bool show)
        {
            state = show ? State.Slots : State.PressKey;
            confirmDelete = -1;
            if (show) RefreshCards();
            slotGroup.blocksRaycasts = show;
            slotGroup.interactable = show;
            float from = slotGroup.alpha, to = show ? 1f : 0f;
            for (float t = 0f; t < 0.3f; t += Time.unscaledDeltaTime)
            {
                float k = Smooth(t / 0.3f);
                slotGroup.alpha = Mathf.Lerp(from, to, k);
                promptGroup.alpha = show ? 1f - k : k;
                ((RectTransform)slotGroup.transform).anchoredPosition = new Vector2(0f, Mathf.Lerp(-40f, 0f, show ? k : 1f - k));
                yield return null;
            }
            slotGroup.alpha = to;
        }

        private void ChooseSlot(int slot, bool load)
        {
            if (state != State.Slots) return;
            SaveManager sm = SaveManager.Instance;
            bool ok;
            if (sm == null) ok = true;
            else if (load) ok = sm.LoadGame(slot);
            else
            {
                if (savedHour >= 0 && GameClock.Instance != null) // 새 게임은 원래 시작 시각으로
                    GameClock.Instance.LoadTime(GameClock.Instance.CurrentDay, savedHour, savedMinute);
                sm.StartNewGame(slot);
                ok = true;
            }
            if (!ok) { RefreshCards(); return; }
            StartCoroutine(FlyIn());
        }

        // 카메라가 섬 위에서 캐릭터 쪽으로 날아감 → 평소 카메라로 이어짐
        private IEnumerator FlyIn()
        {
            state = State.Starting;
            slotGroup.blocksRaycasts = false;
            Vector3 p0 = camPos, l0 = camLook;
            ShowPlayer();
            PlayerController player = FindAnyObjectByType<PlayerController>();
            Vector3 target = player != null ? player.transform.position : camLook;
            Vector3 back = player != null ? -player.transform.forward : (p0 - camLook).normalized;
            back.y = 0f;
            if (back.sqrMagnitude < 0.01f) back = Vector3.back;
            back.Normalize();
            Vector3 p1 = target + back * 9f + Vector3.up * 6f, l1 = target + Vector3.up * 1f;

            const float dur = 2.2f;
            for (float t = 0f; t < dur; t += Time.unscaledDeltaTime)
            {
                float k = Smooth(t / dur);
                float fadeUi = Mathf.Clamp01(t / 0.5f);
                slotGroup.alpha = 1f - fadeUi;
                logoGroup.alpha = 1f - fadeUi;
                cornerGroup.alpha = 1f - fadeUi;
                // 위로 살짝 떠오르며 이동 (포물선)
                Vector3 pos = Vector3.Lerp(p0, p1, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 6f;
                Vector3 look = Vector3.Lerp(l0, l1, Smooth(Mathf.Clamp01(k * 1.3f)));
                Shot(pos, look);
                yield return null;
            }
            if (rig != null) rig.EndShot(); // 평소 카메라로 부드럽게
            ExitTitle();
        }

        // ───────────────────────── 배경 카메라 ─────────────────────────

        private void SetupCamera()
        {
            // 가장 최근 저장에 사는 주민을 미리 보여 줌 (주민 집 시스템이 있을 때)
            if (ResidentManager.Instance != null) ResidentManager.Instance.PreviewLatestSave();

            PlayerController player = FindAnyObjectByType<PlayerController>();
            if (hidePlayer && player != null)
                foreach (Renderer r in player.GetComponentsInChildren<Renderer>())
                    if (r.enabled) { r.enabled = false; hiddenPlayer.Add(r); }

            // 따라다닐 주민: 게임을 켤 때마다 무작위로 1명
            var candidates = new List<Transform>();
            if (residents != null && residents.Length > 0)
            { foreach (NPCDialogue n in residents) if (n != null && n.gameObject.activeInHierarchy) candidates.Add(n.transform); }
            else
            { foreach (NPCDialogue n in FindObjectsByType<NPCDialogue>(FindObjectsInactive.Exclude)) candidates.Add(n.transform); }
            followTarget = candidates.Count >= Mathf.Max(1, minResidentsToFollow) ? candidates[Random.Range(0, candidates.Count)] : null;
            if (followTarget != null)
            {
                camYaw = FrontYaw(followTarget);
                Debug.Log($"[시작 화면] 이번 주민: {followTarget.name}");
            }

            // 주민이 없을 때 풍경 자리: 지정한 곳, 없으면 캐릭터 시작 자리 (그 자리가 바라보는 쪽을 보여 줌)
            Transform p = sceneryPoint != null ? sceneryPoint : player != null ? player.transform : null;
            sceneryCenter = p != null ? p.position : Vector3.zero;
            sceneryYaw = p != null ? p.eulerAngles.y + 180f : 0f;
            UpdateCamera(0f);
        }

        // 주민 얼굴이 보이게 앞쪽 비스듬히
        private static float FrontYaw(Transform t) => t.eulerAngles.y + 35f;

        private void UpdateCamera(float dt)
        {
            Vector3 look, dir;
            float dist, h;
            Transform target = followTarget;
            if (target != null)
            {
                camYaw += residentDrift * dt;
                look = target.position + Vector3.up * 1f;
                dir = Quaternion.Euler(0f, camYaw, 0f) * Vector3.forward;
                dist = residentDistance;
                h = residentHeight - 1f;
            }
            else
            {
                float yaw = sceneryYaw + Mathf.Sin(Time.unscaledTime * 0.08f) * sceneryWander;
                look = sceneryCenter + Vector3.up * 1.2f;
                dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                dist = sceneryDistance;
                h = sceneryHeight - 1.2f;
            }
            Vector3 pos = Clear(look, look + dir * dist + Vector3.up * h, target);
            camPos = pos;
            camLook = look;
            Shot(pos, look);
        }

        // 카메라가 언덕·나무 속에 묻히지 않게 (바라보는 점 → 카메라 사이를 막는 게 있으면 그 앞으로, 땅보다 위로)
        private Vector3 Clear(Vector3 look, Vector3 pos, Transform target)
        {
            Vector3 d = pos - look;
            float len = d.magnitude;
            if (len > 0.01f)
            {
                float best = len;
                foreach (RaycastHit hit in Physics.SphereCastAll(look, 0.35f, d / len, len, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (target != null && hit.transform.IsChildOf(target)) continue;
                    if (hit.collider.GetComponentInParent<NPCDialogue>() != null || hit.collider.GetComponentInParent<PlayerController>() != null) continue;
                    if (hit.distance > 0f && hit.distance < best) best = hit.distance;
                }
                if (best < len) pos = look + d / len * Mathf.Max(1.5f, best - 0.2f);
            }
            if (Physics.Raycast(pos + Vector3.up * 30f, Vector3.down, out RaycastHit g, 60f, ~0, QueryTriggerInteraction.Ignore))
                pos.y = Mathf.Max(pos.y, g.point.y + 1.2f);
            return pos;
        }

        private void ShowPlayer()
        {
            foreach (Renderer r in hiddenPlayer) if (r != null) r.enabled = true;
            hiddenPlayer.Clear();
        }

        private void Shot(Vector3 pos, Vector3 look)
        {
            Quaternion rot = Quaternion.LookRotation(look - pos, Vector3.up);
            if (rig != null) rig.BeginShot(pos, rot);
            else if (Camera.main != null) Camera.main.transform.SetPositionAndRotation(pos, rot);
        }

        // ───────────────────────── 화면 UI ─────────────────────────

        private void BuildUI()
        {
            font = UIFont.FindKorean();
            var go = new GameObject("TitleScreenCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 800;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            if (FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));

            // 로고
            logoGroup = Group("Logo", go.transform, new Vector2(0.5f, 1f), new Vector2(0f, LogoY), logoSize);
            logoRt = (RectTransform)logoGroup.transform;
            if (logo != null)
            {
                Image img = logoGroup.gameObject.AddComponent<Image>();
                img.sprite = logo;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }
            else
            {
                TextMeshProUGUI t = Text(logoGroup.transform, titleText, 150f, Color.white, Vector2.zero, logoSize);
                t.fontStyle = FontStyles.Bold;
                t.outlineWidth = 0.18f;
                t.outlineColor = new Color32(40, 50, 90, 255);
                Text(logoGroup.transform, subtitleText, 40f, new Color(1f, 1f, 1f, 0.9f), new Vector2(0f, -150f), new Vector2(900f, 60f));
            }

            // 아무 키나
            promptGroup = Group("Prompt", go.transform, new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(900f, 70f));
            TextMeshProUGUI prompt = Text(promptGroup.transform, "아무 키나 눌러 시작하세요", 40f, Color.white, Vector2.zero, new Vector2(900f, 70f));
            prompt.outlineWidth = 0.2f;
            prompt.outlineColor = new Color32(20, 25, 45, 200);

            // 구석: 팀 이름 · 버전 · 나가기
            cornerGroup = Group("Corner", go.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1920f, 1080f));
            var team = Text(cornerGroup.transform, teamText, 26f, new Color(1f, 1f, 1f, 0.8f), new Vector2(-760f, -505f), new Vector2(400f, 40f));
            team.alignment = TextAlignmentOptions.Left;
            var ver = Text(cornerGroup.transform, "v" + Application.version, 24f, new Color(1f, 1f, 1f, 0.7f), new Vector2(820f, -505f), new Vector2(200f, 40f));
            ver.alignment = TextAlignmentOptions.Right;
            Button quit = MakeButton(cornerGroup.transform, "나가기", new Vector2(830f, 495f), new Vector2(160f, 56f), buttonSprite, new Color(0.15f, 0.18f, 0.3f, 0.75f));
            quit.onClick.AddListener(Quit);

            // 저장 칸 3개
            slotGroup = Group("Slots", go.transform, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(1500f, 520f));
            ((RectTransform)slotGroup.transform).pivot = new Vector2(0.5f, 0f);
            slotGroup.alpha = 0f;
            slotGroup.blocksRaycasts = false;
            slotGroup.interactable = false;
            for (int i = 0; i < SaveManager.SlotCount; i++)
            {
                var card = new GameObject($"Slot{i + 1}", typeof(RectTransform), typeof(Image));
                card.transform.SetParent(slotGroup.transform, false);
                var rt = (RectTransform)card.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.sizeDelta = new Vector2(440f, 300f);
                rt.anchoredPosition = new Vector2((i - 1) * 480f, 120f);
                Image bg = card.GetComponent<Image>();
                StyleImage(bg, panelSprite, new Color(0.97f, 0.95f, 0.88f, 0.96f));
                cards[i] = rt;
            }
            var back = Text(slotGroup.transform, "Esc  뒤로", 26f, new Color(1f, 1f, 1f, 0.85f), Vector2.zero, new Vector2(400f, 40f));
            var brt = back.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0f);
            brt.anchoredPosition = new Vector2(0f, 70f);

            // 맨 위 검은 화면 (처음에 서서히 밝아짐)
            var f = new GameObject("Fade", typeof(RectTransform), typeof(Image));
            f.transform.SetParent(go.transform, false);
            var frt = (RectTransform)f.transform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = frt.offsetMax = Vector2.zero;
            fade = f.GetComponent<Image>();
            fade.color = Color.black;
        }

        // 칸 내용 새로 그리기
        private void RefreshCards()
        {
            for (int i = 0; i < cards.Length; i++)
            {
                RectTransform card = cards[i];
                for (int c = card.childCount - 1; c >= 0; c--) Destroy(card.GetChild(c).gameObject);
                int slot = i;
                SlotSummary s = SaveManager.GetSummary(slot);
                Color ink = new Color(0.2f, 0.18f, 0.3f);

                var head = Text(card, $"{slot + 1}번 농장", 34f, ink, new Vector2(0f, 110f), new Vector2(400f, 50f));
                head.fontStyle = FontStyles.Bold;

                if (confirmDelete == slot)
                {
                    Text(card, "정말 지울까요?\n<size=75%>지운 농장은 되돌릴 수 없어요</size>", 30f, new Color(0.75f, 0.2f, 0.2f), new Vector2(0f, 20f), new Vector2(400f, 110f));
                    MakeButton(card, "지우기", new Vector2(-95f, -100f), new Vector2(170f, 60f), dangerButtonSprite, new Color(0.85f, 0.35f, 0.3f))
                        .onClick.AddListener(() => { SaveManager.DeleteSlot(slot); confirmDelete = -1; RefreshCards(); });
                    MakeButton(card, "취소", new Vector2(95f, -100f), new Vector2(170f, 60f), buttonSprite, new Color(0.55f, 0.58f, 0.68f))
                        .onClick.AddListener(() => { confirmDelete = -1; RefreshCards(); });
                }
                else if (s.exists)
                {
                    Text(card, $"{DateText(s.day)}\n{s.bells:N0}벨 · {PlayTime(s.playSeconds)}\n<size=75%><color=#7a7590>마지막 저장 {ShortDate(s.savedAt)}</color></size>",
                         28f, ink, new Vector2(0f, 20f), new Vector2(400f, 130f));
                    MakeButton(card, "이어하기", new Vector2(-60f, -100f), new Vector2(240f, 64f), buttonSprite, new Color(0.35f, 0.62f, 0.4f))
                        .onClick.AddListener(() => ChooseSlot(slot, true));
                    MakeButton(card, "지우기", new Vector2(140f, -100f), new Vector2(120f, 64f), dangerButtonSprite, new Color(0.8f, 0.45f, 0.4f))
                        .onClick.AddListener(() => { confirmDelete = slot; RefreshCards(); });
                }
                else
                {
                    Text(card, "<color=#9a95aa>빈 칸</color>", 30f, ink, new Vector2(0f, 20f), new Vector2(400f, 110f));
                    MakeButton(card, "새 게임", new Vector2(0f, -100f), new Vector2(260f, 64f), buttonSprite, new Color(0.35f, 0.5f, 0.75f))
                        .onClick.AddListener(() => ChooseSlot(slot, false));
                }
            }
        }

        private static string DateText(int day)
        {
            int per = GameClock.Instance != null ? Mathf.Max(1, GameClock.Instance.DaysPerSeason) : 27;
            int year = (day - 1) / (per * 4) + 1;
            int season = ((day - 1) / per) % 4;
            int dayOf = (day - 1) % per + 1;
            string[] names = { "봄", "여름", "가을", "겨울" };
            return $"{year}년차 {names[season]} {dayOf}일";
        }

        private static string PlayTime(float seconds)
        {
            int m = Mathf.FloorToInt(seconds / 60f);
            return m >= 60 ? $"{m / 60}시간 {m % 60}분" : $"{Mathf.Max(1, m)}분";
        }

        private static string ShortDate(string s) => !string.IsNullOrEmpty(s) && s.Length >= 16 ? s.Substring(5).Replace('-', '/') : s;

        private CanvasGroup Group(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, anchor.y);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return go.GetComponent<CanvasGroup>();
        }

        private TextMeshProUGUI Text(Transform parent, string text, float size, Color color, Vector2 pos, Vector2 box)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
            var t = go.GetComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false;
            return t;
        }

        private Button MakeButton(Transform parent, string label, Vector2 pos, Vector2 size, Sprite sprite, Color tint)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            Image img = go.GetComponent<Image>();
            StyleImage(img, sprite, sprite != null ? Color.white : tint);
            if (sprite != null) img.color = Color.Lerp(Color.white, tint, 0.35f);
            Button b = go.GetComponent<Button>();
            ColorBlock cb = b.colors;
            cb.highlightedColor = new Color(1.1f, 1.1f, 1.1f);
            cb.pressedColor = new Color(0.85f, 0.85f, 0.85f);
            b.colors = cb;
            TextMeshProUGUI t = Text(go.transform, label, Mathf.Min(30f, size.y * 0.5f), sprite != null ? new Color(0.2f, 0.18f, 0.3f) : Color.white, Vector2.zero, size);
            t.fontStyle = FontStyles.Bold;
            return b;
        }

        private static void StyleImage(Image img, Sprite sprite, Color color)
        {
            img.sprite = sprite;
            img.color = color;
            if (sprite != null && sprite.border.sqrMagnitude > 0f) img.type = Image.Type.Sliced;
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private static float Smooth(float k) { k = Mathf.Clamp01(k); return k * k * (3f - 2f * k); }
    }
}