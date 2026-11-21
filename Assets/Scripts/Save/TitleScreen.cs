using System;
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
using FarmingSystem.Intro;
using FarmingSystem.NPC;
using Random = UnityEngine.Random;

namespace FarmingSystem.Save
{
    // 시작 화면 (게임 씬 안에서 바로 - 배경은 게임 화면 그대로)
    //  1. 게임을 켜면 검은 화면에서 서서히 밝아지며 (게임 시간은 멈춤, NPC는 계속 돌아다님)
    //     - 주민(NPC)이 있으면: 게임을 켤 때마다 무작위로 1명을 골라 가까이서 계속 따라다님
    //     - 주민이 없으면: 캐릭터가 시작하는 자리 근처 풍경을 가까이서
    //  2. 왼쪽 위 로고 + 왼쪽 메뉴 NEW / LOAD / OPTION / EXIT (▶ 화살표가 고른 메뉴를 가리킴)
    //     - 마우스를 올리면 고르고 클릭하면 실행 / 키보드 ↑↓(W S) + Enter(Space·E)
    //  3. NEW → 칸 고르기 없이 바로 새 게임 (첫 빈 칸에 저장, 인트로부터)
    //     3칸이 다 차 있으면: "새 게임을 하려면 저장 파일 1개를 삭제해야 해요" + 저장 파일 카드 → 지울 카드를 누르면 확인 후 지우고 새로 시작
    //     LOAD → 저장 칸 카드 3장 (할로우 나이트 프로필 화면처럼)
    //     - 저장이 있는 칸을 누르면 이어하기 (NEW·LOAD 둘 다), 빈 칸을 누르면 새 게임 (NEW에서만)
    //     - 카드 아래 "지우기": NEW에서는 "지우고 새로 시작할까요?" → 새로 시작 / LOAD에서는 "정말 지울까요?" → 지우기만
    //     - BACK / Esc = 메뉴로
    //  4. OPTION → 설정 창 (전체 소리·마우스 감도·카메라 상하 반전·전체 화면, 바로 적용·저장)
    //  5. 이어하기: 카메라가 캐릭터 쪽으로 날아가며 평소 카메라로 이어지고 바로 게임 시작
    //     새 게임: 안내 문구 → PV 오프닝(우주선 착륙 → 캐릭터 등장) → 시작 (NewGame_OpeningCamera가 있을 때)
    // 화면 UI는 코드로 만듦 (프리팹 필요 없음). 로고 이미지는 Logo 칸에 넣기 (비우면 글자 로고)
    // 만들기: Tools → 행성농사일지 → 저장 → 저장·시작 화면 만들기
    public class TitleScreen : MonoBehaviour
    {
        [Header("로고 (왼쪽 위)")]
        [Tooltip("로고 이미지 (PNG, Texture Type = Sprite). 비우면 글자로 '행성농사일지'")]
        [SerializeField] private Sprite logo;
        [Tooltip("로고 크기 (1920x1080 화면 기준)")]
        [SerializeField] private Vector2 logoBoxSize = new Vector2(780f, 250f);
        [Tooltip("로고 위치 (왼쪽 위 모서리에서)")]
        [SerializeField] private Vector2 logoOffset = new Vector2(60f, -110f);
        [SerializeField] private string titleText = "행성농사일지";
        [SerializeField] private string teamText = "우주개발인사본부";

        [Header("메뉴 (왼쪽)")]
        [SerializeField] private string newLabel = "NEW";
        [SerializeField] private string loadLabel = "LOAD";
        [SerializeField] private string optionLabel = "OPTION";
        [SerializeField] private string exitLabel = "EXIT";
        [Tooltip("메뉴 글자를 바꾸고 싶으면 (비우면 한글 기본 글꼴)")]
        [SerializeField] private TMP_FontAsset menuFont;
        [SerializeField] private float menuFontSize = 86f;
        [Tooltip("첫 메뉴(NEW) 위치 (왼쪽 위 모서리에서), 메뉴 사이 간격")]
        [SerializeField] private Vector2 menuOffset = new Vector2(190f, -445f);
        [SerializeField] private float menuSpacing = 148f;
        [SerializeField] private Color menuColor = Color.white;
        [SerializeField] private Color menuOutlineColor = new Color32(45, 40, 80, 255);

        [Header("저장 칸 카드")]
        [Tooltip("고르지 않은 카드 바탕 (기본: 반투명 검정)")]
        [SerializeField] private Color cardColor = new Color(0f, 0f, 0f, 0.55f);
        [Tooltip("고른 카드 바탕 (기본: 반투명 흰색)")]
        [SerializeField] private Color cardSelectedColor = new Color(1f, 1f, 1f, 0.38f);

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
        [Tooltip("시작 화면 동안 캐릭터를 숨김 (풍경·주민만)")]
        [SerializeField] private bool hidePlayer = true;
        [Tooltip("시작 화면 시각 (게임 시계를 이 시각으로 보여 줌, 음수면 그대로)")]
        [SerializeField] private float titleHour = 17.5f;

        [Header("새 게임 오프닝 (PV처럼)")]
        [Tooltip("우주선이 날아오기 전 검은 화면에 한 줄씩 나오는 안내 문구. {번호} = 새 게임마다 무작위 8자리 번호. 비우면 글자 없이 바로 시작")]
        [SerializeField, TextArea]
        private string[] openingCaptions =
        {
            "no.{번호}님께 알립니다.",
            "우주개발인사본부 [행성 이주 패키지 플랜] 함선은\n곧 목적지에 도착합니다.",
        };
        [Tooltip("문구 한 줄이 떠 있는 시간 (초)")]
        [SerializeField] private float captionSeconds = 3.5f;
        [Tooltip("오프닝 동안 위아래 검은 띠 (영화처럼). 끝나면 띠가 빠지며 게임 화면으로")]
        [SerializeField] private bool openingLetterbox = true;

        [Header("에디터 테스트")]
        [Tooltip("켜면 에디터에서 시작 화면을 건너뛰고 아래 칸으로 바로 시작 (빌드에선 항상 시작 화면)")]
        [SerializeField] private bool skipInEditor = false;
        [Tooltip("건너뛸 때 쓸 칸 (1~3). 저장이 있으면 이어하기, 없으면 새 게임")]
        [SerializeField, Range(1, 3)] private int editorSlot = 1;

        public static bool IsShowing { get; private set; }

        private enum State { Intro, Menu, Slots, Options, Starting }
        private enum SlotMode { New, Load, MakeRoom } // MakeRoom = 칸이 다 차서 새 게임을 하려면 하나를 지워야 할 때
        private State state;
        private SlotMode slotMode;

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
        private CanvasGroup uiRoot, logoGroup, menuGroup, cornerGroup, overlayGroup, slotGroup, optionGroup;
        private Sprite triangle, dot;

        // 메뉴
        private class MenuItem { public RectTransform rt; public TextMeshProUGUI text; public bool enabled; public Action act; }
        private readonly List<MenuItem> menu = new List<MenuItem>();
        private int menuIndex;
        private RectTransform menuArrow;

        // 저장 칸 카드
        private readonly RectTransform[] cards = new RectTransform[SaveManager.SlotCount];
        private readonly RectTransform[] cardContent = new RectTransform[SaveManager.SlotCount];
        private readonly RectTransform[] cardBelow = new RectTransform[SaveManager.SlotCount];
        private readonly Image[] cardGlow = new Image[SaveManager.SlotCount];
        private readonly List<Image>[] cardLines = new List<Image>[SaveManager.SlotCount];
        private TextMeshProUGUI slotTitle, slotSub;
        private RectTransform arrowL, arrowR;
        private int slotIndex, confirmDelete = -1, confirmOverwrite = -1;

        // 설정
        private class OptionRow { public RectTransform rt; public Image highlight; public Action<int> step; public Action refresh; }
        private readonly List<OptionRow> options = new List<OptionRow>();
        private int optionIndex;

        // 떠오르는 작은 빛 (카드·설정 화면 배경)
        private class Ember { public RectTransform rt; public Image img; public float speed, sway, phase, life, age; }
        private readonly List<Ember> embers = new List<Ember>();

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
            IntroShipLanding.OnPlayerAppear -= ShowPlayer;
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
            logoGroup.alpha = menuGroup.alpha = cornerGroup.alpha = 0f;
            for (float t = 0f; t < 1.8f; t += Time.unscaledDeltaTime)
            {
                fade.color = new Color(0f, 0f, 0f, 1f - Smooth(t / 1.6f));
                logoGroup.alpha = Smooth((t - 0.5f) / 0.8f);
                menuGroup.alpha = Smooth((t - 0.9f) / 0.8f);
                cornerGroup.alpha = Smooth((t - 1.1f) / 0.6f);
                yield return null;
            }
            fade.color = Color.clear;
            fade.raycastTarget = false;
            logoGroup.alpha = menuGroup.alpha = cornerGroup.alpha = 1f;
            menuGroup.blocksRaycasts = menuGroup.interactable = true;
            state = State.Menu;
        }

        private void Update()
        {
            if (!IsShowing) return;
            float dt = Time.unscaledDeltaTime;
            if (state != State.Starting) UpdateCamera(dt);
            AnimateMenu(dt);
            AnimateCards(dt);
            AnimateOptions(dt);
            UpdateEmbers(dt);

            switch (state)
            {
                case State.Menu: MenuInput(); break;
                case State.Slots: SlotInput(); break;
                case State.Options: OptionInput(); break;
            }
        }

        // 키 입력 (키보드·게임패드)
        private static bool Up() => KeyDown(UnityEngine.InputSystem.Key.UpArrow) || KeyDown(UnityEngine.InputSystem.Key.W) || Pad(p => p.dpad.up.wasPressedThisFrame);
        private static bool Down() => KeyDown(UnityEngine.InputSystem.Key.DownArrow) || KeyDown(UnityEngine.InputSystem.Key.S) || Pad(p => p.dpad.down.wasPressedThisFrame);
        private static bool Left() => KeyDown(UnityEngine.InputSystem.Key.LeftArrow) || KeyDown(UnityEngine.InputSystem.Key.A) || Pad(p => p.dpad.left.wasPressedThisFrame);
        private static bool Right() => KeyDown(UnityEngine.InputSystem.Key.RightArrow) || KeyDown(UnityEngine.InputSystem.Key.D) || Pad(p => p.dpad.right.wasPressedThisFrame);
        private static bool Confirm() => KeyDown(UnityEngine.InputSystem.Key.Enter) || KeyDown(UnityEngine.InputSystem.Key.NumpadEnter) || KeyDown(UnityEngine.InputSystem.Key.Space) || KeyDown(UnityEngine.InputSystem.Key.E) || Pad(p => p.buttonSouth.wasPressedThisFrame);
        private static bool Back() => KeyDown(UnityEngine.InputSystem.Key.Escape) || Pad(p => p.buttonEast.wasPressedThisFrame);
        private static bool KeyDown(UnityEngine.InputSystem.Key k) => Keyboard.current != null && Keyboard.current[k].wasPressedThisFrame;
        private static bool Pad(Func<Gamepad, bool> f) => Gamepad.current != null && f(Gamepad.current);

        // 화면 전환: 메뉴 ↔ 카드 ↔ 설정
        private IEnumerator Switch(State to)
        {
            State from = state;
            state = State.Intro; // 전환 중엔 입력 안 받음
            CanvasGroup a = GroupOf(from), b = GroupOf(to);
            bool overlayOn = to == State.Slots || to == State.Options;
            float o0 = overlayGroup.alpha, o1 = overlayOn ? 1f : 0f;
            float l0 = logoGroup.alpha, l1 = to == State.Menu ? 1f : 0f;
            if (a != null) a.blocksRaycasts = a.interactable = false;
            for (float t = 0f; t < 0.3f; t += Time.unscaledDeltaTime)
            {
                float k = Smooth(t / 0.3f);
                if (a != null) a.alpha = 1f - k;
                if (b != null) b.alpha = k;
                overlayGroup.alpha = Mathf.Lerp(o0, o1, k);
                logoGroup.alpha = Mathf.Lerp(l0, l1, k);
                if (b != null && b != menuGroup) ((RectTransform)b.transform).anchoredPosition = new Vector2(0f, Mathf.Lerp(-30f, 0f, k));
                yield return null;
            }
            if (a != null) a.alpha = 0f;
            if (b != null) { b.alpha = 1f; b.blocksRaycasts = b.interactable = true; }
            overlayGroup.alpha = o1;
            logoGroup.alpha = l1;
            state = to;
        }

        private CanvasGroup GroupOf(State s) => s == State.Menu ? menuGroup : s == State.Slots ? slotGroup : s == State.Options ? optionGroup : null;

        // ───────────────────────── 메뉴 ─────────────────────────

        private void MenuInput()
        {
            if (Up()) MoveMenu(-1);
            else if (Down()) MoveMenu(1);
            else if (Confirm()) RunMenu(menuIndex);
        }

        private void MoveMenu(int d)
        {
            for (int n = 0; n < menu.Count; n++)
            {
                menuIndex = (menuIndex + d + menu.Count) % menu.Count;
                if (menu[menuIndex].enabled) return;
            }
        }

        private void RunMenu(int i)
        {
            if (state != State.Menu || i < 0 || i >= menu.Count || !menu[i].enabled) return;
            menuIndex = i;
            menu[i].act?.Invoke();
        }

        // NEW: 칸 고르는 화면 없이 바로 새 게임 (첫 빈 칸에 저장, 인트로부터)
        // 3칸이 다 차 있으면 카드 화면에서 지울 칸을 고르게 함 (지우기 → 새로 시작)
        private void StartNewDirect()
        {
            for (int i = 0; i < SaveManager.SlotCount; i++)
                if (!SaveManager.HasSave(i)) { ChooseSlot(i, false); return; }
            OpenSlots(SlotMode.MakeRoom); // 다 찼으면: 저장 파일을 보여 주고 지울 것을 고르게
        }

        private void OpenSlots(SlotMode mode)
        {
            slotMode = mode;
            confirmDelete = confirmOverwrite = -1;
            slotIndex = FirstSlot();
            RefreshCards();
            StartCoroutine(Switch(State.Slots));
        }

        private void OpenOptions()
        {
            optionIndex = 0;
            foreach (OptionRow r in options) r.refresh?.Invoke();
            StartCoroutine(Switch(State.Options));
        }

        private void BackToMenu()
        {
            if (state != State.Slots && state != State.Options) return;
            RefreshMenu();
            StartCoroutine(Switch(State.Menu));
        }

        private void RefreshMenu()
        {
            bool any = false;
            for (int i = 0; i < SaveManager.SlotCount; i++) any |= SaveManager.HasSave(i);
            if (menu.Count > 1) menu[1].enabled = any; // LOAD: 저장이 하나도 없으면 흐리게
            foreach (MenuItem m in menu) m.text.alpha = m.enabled ? 1f : 0.35f;
            if (!menu[menuIndex].enabled) MoveMenu(1);
        }

        private void AnimateMenu(float dt)
        {
            if (menuArrow == null || menu.Count == 0) return;
            float k = 1f - Mathf.Exp(-14f * dt);
            for (int i = 0; i < menu.Count; i++)
            {
                float target = i == menuIndex ? 1.08f : 1f;
                float s = Mathf.Lerp(menu[i].rt.localScale.x, target, k);
                menu[i].rt.localScale = new Vector3(s, s, 1f);
            }
            Vector2 p = menuArrow.anchoredPosition;
            float y = Mathf.Lerp(p.y, menu[menuIndex].rt.anchoredPosition.y, k);
            float x = menuOffset.x - 70f + Mathf.Sin(Time.unscaledTime * 4f) * 6f; // 살짝 좌우로 까딱
            menuArrow.anchoredPosition = new Vector2(x, y);
        }

        // ───────────────────────── 저장 칸 카드 ─────────────────────────

        private int FirstSlot()
        {
            for (int i = 0; i < SaveManager.SlotCount; i++)
                if (slotMode == SlotMode.Load ? SaveManager.HasSave(i) : !SaveManager.HasSave(i)) return i;
            return 0;
        }

        private void SlotInput()
        {
            if (Back())
            {
                if (confirmDelete >= 0 || confirmOverwrite >= 0) { confirmDelete = confirmOverwrite = -1; RefreshCards(); }
                else BackToMenu();
                return;
            }
            if (confirmDelete >= 0 || confirmOverwrite >= 0)
            {
                if (Confirm())
                {
                    if (confirmDelete >= 0) DoDelete(confirmDelete);
                    else DoOverwrite(confirmOverwrite);
                }
                return;
            }
            if (Left()) slotIndex = (slotIndex + SaveManager.SlotCount - 1) % SaveManager.SlotCount;
            else if (Right()) slotIndex = (slotIndex + 1) % SaveManager.SlotCount;
            else if (Confirm()) OnCardClick(slotIndex);
        }

        private void OnCardClick(int slot)
        {
            if (state != State.Slots || confirmDelete >= 0 || confirmOverwrite >= 0) return;
            slotIndex = slot;
            bool exists = SaveManager.HasSave(slot);
            if (slotMode == SlotMode.MakeRoom)
            {
                if (exists) { confirmOverwrite = slot; RefreshCards(); } // 고른 저장 파일 지우고 새로 시작할지 확인
                return;
            }
            if (exists) ChooseSlot(slot, true);                          // 저장이 있는 칸 = 이어하기
            else if (slotMode == SlotMode.New) ChooseSlot(slot, false);  // 빈 칸 = 새 게임 (NEW에서만)
        }

        private void DoDelete(int slot)
        {
            SaveManager.DeleteSlot(slot);
            confirmDelete = -1;
            RefreshCards();
        }

        private void DoOverwrite(int slot)
        {
            SaveManager.DeleteSlot(slot);
            confirmOverwrite = -1;
            ChooseSlot(slot, false);
        }

        private void AnimateCards(float dt)
        {
            if (cards[0] == null) return;
            float k = 1f - Mathf.Exp(-12f * dt);
            for (int i = 0; i < cards.Length; i++)
            {
                bool sel = i == slotIndex;
                float s = Mathf.Lerp(cards[i].localScale.x, sel ? 1.04f : 1f, k);
                cards[i].localScale = new Vector3(s, s, 1f);
                cardGlow[i].color = Color.Lerp(cardGlow[i].color, sel ? cardSelectedColor : cardColor, k); // 기본 검정 ↔ 고르면 흰색
                foreach (Image line in cardLines[i])
                {
                    Color c = line.color;
                    c.a = Mathf.Lerp(c.a, sel ? 1f : 0.55f, k);
                    line.color = c;
                }
            }
            // 고른 카드 양옆 ◀ ▶
            RectTransform card = cards[slotIndex];
            float half = card.sizeDelta.x * 0.5f + 46f + Mathf.Sin(Time.unscaledTime * 4f) * 5f;
            Vector2 c0 = card.anchoredPosition + new Vector2(0f, card.sizeDelta.y * 0.5f);
            arrowL.anchoredPosition = Vector2.Lerp(arrowL.anchoredPosition, c0 + new Vector2(-half, 0f), k);
            arrowR.anchoredPosition = Vector2.Lerp(arrowR.anchoredPosition, c0 + new Vector2(half, 0f), k);
        }

        private void ChooseSlot(int slot, bool load)
        {
            if (state != State.Slots && state != State.Menu) return; // 메뉴의 NEW에서 바로 오기도 함
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
            IntroCameraPath opening = FindAnyObjectByType<IntroCameraPath>();
            if (!load && opening != null && opening.isActiveAndEnabled && opening.HasPoints) StartCoroutine(NewGameSequence(opening));
            else StartCoroutine(FlyIn());
        }

        // 새 게임: 화면 어둡게 → PV 오프닝 (카메라 경로 + 우주선 착륙 + 캐릭터 등장) → 마지막 구도에서 평소 카메라로 이어지며 시작
        // 씬에 오프닝(NewGame_OpeningCamera)이 없으면 이어하기처럼 카메라가 캐릭터에게 날아감
        private IEnumerator NewGameSequence(IntroCameraPath opening)
        {
            state = State.Starting;
            FarmingSystem.Audio.GameAudio.SetIntro(true); // 오프닝 배경음
            uiRoot.blocksRaycasts = false;
            fade.raycastTarget = true;
            for (float t = 0f; t < 0.8f; t += Time.unscaledDeltaTime)
            {
                float k = Smooth(t / 0.8f);
                uiRoot.alpha = 1f - k;
                fade.color = new Color(0f, 0f, 0f, k);
                yield return null;
            }
            fade.color = Color.black;

            // 위아래 검은 띠 + 검은 화면 안내 문구 (PV처럼)
            RectTransform barTop = null, barBottom = null;
            if (openingLetterbox) { barTop = Bar("LetterboxTop", 1f); barBottom = Bar("LetterboxBottom", 0f); }
            yield return Captions();

            // 화면이 검은 동안: 우주선을 건물로 놓고 하늘에 올려 둠 + 캐릭터 숨김
            IntroShipLanding landing = FindAnyObjectByType<IntroShipLanding>();
            if (landing != null) landing.Prepare();
            IntroShipLanding.OnPlayerAppear += ShowPlayer;

            // 카메라 경로 재생 (1번 점으로 바로 이동한 뒤 화면을 밝힘)
            Coroutine run = StartCoroutine(opening.Run());
            yield return null;
            StartCoroutine(FadeFromBlack());
            yield return run;

            IntroShipLanding.OnPlayerAppear -= ShowPlayer;
            fade.color = Color.clear;
            fade.raycastTarget = false;
            // 띠가 위아래로 빠지며 게임 화면으로 (그동안 카메라는 평소 카메라로 부드럽게 이어짐)
            if (barTop != null)
                for (float t = 0f; t < 0.7f; t += Time.unscaledDeltaTime)
                {
                    float k = Smooth(t / 0.7f);
                    barTop.anchoredPosition = new Vector2(0f, BarHeight * k);
                    barBottom.anchoredPosition = new Vector2(0f, -BarHeight * k);
                    yield return null;
                }
            FarmingSystem.Audio.GameAudio.SetIntro(false); // → 평소 배경음으로 넘어감
            ExitTitle(); // 카메라는 오프닝이 평소 카메라로 넘겨줌
        }

        private const float BarHeight = 120f; // 1080 기준 (화면의 약 11%)

        // 위(1)·아래(0) 검은 띠 - 맨 앞(검은 화면·문구 위)에 그림
        private RectTransform Bar(string barName, float y)
        {
            var go = new GameObject(barName, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(canvas.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, y);
            rt.anchorMax = new Vector2(1f, y);
            rt.pivot = new Vector2(0.5f, y);
            rt.sizeDelta = new Vector2(0f, BarHeight);
            rt.anchoredPosition = Vector2.zero;
            Image img = go.GetComponent<Image>();
            img.color = Color.black;
            img.raycastTarget = false;
            return rt;
        }

        // 검은 화면에 문구를 한 줄씩 (서서히 나타났다 사라짐)
        private IEnumerator Captions()
        {
            if (openingCaptions == null || openingCaptions.Length == 0) yield break;
            string id = UnityEngine.Random.Range(10000000, 99999999).ToString();
            TextMeshProUGUI t = Text(canvas.transform, "", 34f, Color.white, Vector2.zero, new Vector2(1600f, 140f));
            t.fontStyle = FontStyles.Italic;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, BarHeight + 140f);
            yield return Wait(0.8f);
            foreach (string line in openingCaptions)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                t.text = line.Replace("{번호}", id).Replace("\\n", "\n");
                for (float s = 0f; s < captionSeconds; s += Time.unscaledDeltaTime)
                {
                    float a = Mathf.Min(Smooth(s / 0.5f), Smooth((captionSeconds - s) / 0.5f));
                    t.color = new Color(1f, 1f, 1f, a);
                    yield return null;
                }
            }
            Destroy(t.gameObject);
            yield return Wait(0.6f);
        }

        private static IEnumerator Wait(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime) yield return null;
        }

        private IEnumerator FadeFromBlack()
        {
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime)
            {
                fade.color = new Color(0f, 0f, 0f, 1f - Smooth(t));
                yield return null;
            }
            fade.color = Color.clear;
            fade.raycastTarget = false;
        }


        // 카메라가 섬 위에서 캐릭터 쪽으로 날아감 → 평소 카메라로 이어짐
        private IEnumerator FlyIn()
        {
            state = State.Starting;
            uiRoot.blocksRaycasts = false;
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
                uiRoot.alpha = 1f - Mathf.Clamp01(t / 0.5f);
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


        // ───────────────────────── 설정 ─────────────────────────

        private void OptionInput()
        {
            if (Back()) { BackToMenu(); return; }
            if (options.Count == 0) return;
            if (Up()) optionIndex = (optionIndex + options.Count - 1) % options.Count;
            else if (Down()) optionIndex = (optionIndex + 1) % options.Count;
            else if (Left()) options[optionIndex].step?.Invoke(-1);
            else if (Right() || Confirm()) options[optionIndex].step?.Invoke(1);
        }

        private void AnimateOptions(float dt)
        {
            float k = 1f - Mathf.Exp(-12f * dt);
            for (int i = 0; i < options.Count; i++)
            {
                Color c = options[i].highlight.color;
                c.a = Mathf.Lerp(c.a, i == optionIndex ? 0.14f : 0f, k);
                options[i].highlight.color = c;
            }
        }

        // ───────────────────────── 떠오르는 빛 ─────────────────────────

        private void UpdateEmbers(float dt)
        {
            if (overlayGroup == null || overlayGroup.alpha <= 0.01f) return;
            foreach (Ember e in embers)
            {
                e.age += dt;
                if (e.age >= e.life) ResetEmber(e, false);
                float k = e.age / e.life;
                Vector2 p = e.rt.anchoredPosition;
                p.y += e.speed * dt;
                p.x += Mathf.Sin(Time.unscaledTime * 0.8f + e.phase) * e.sway * dt;
                e.rt.anchoredPosition = p;
                Color c = e.img.color;
                c.a = Mathf.Sin(k * Mathf.PI) * (0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 5f + e.phase));
                e.img.color = c;
            }
        }

        private void ResetEmber(Ember e, bool anywhere)
        {
            e.rt.anchoredPosition = new Vector2(Random.Range(-980f, 980f), anywhere ? Random.Range(-560f, 560f) : Random.Range(-620f, -480f));
            float size = Random.Range(4f, 11f);
            e.rt.sizeDelta = new Vector2(size, size);
            e.speed = Random.Range(25f, 70f);
            e.sway = Random.Range(10f, 40f);
            e.phase = Random.Range(0f, 10f);
            e.life = Random.Range(6f, 14f);
            e.age = anywhere ? Random.Range(0f, e.life) : 0f;
            Color[] tints = { new Color(1f, 0.75f, 0.55f), new Color(1f, 0.6f, 0.8f), new Color(0.8f, 0.75f, 1f) };
            e.img.color = tints[Random.Range(0, tints.Length)];
        }

        // ───────────────────────── 화면 UI 만들기 ─────────────────────────

        private void BuildUI()
        {
            font = UIFont.FindKorean();
            triangle = MakeTriangleSprite();
            dot = MakeDotSprite();
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

            uiRoot = Full("UI", go.transform);

            // 왼쪽 위 로고
            logoGroup = Full("Logo", uiRoot.transform);
            var logoRt = Rect("LogoImage", logoGroup.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), logoOffset, logoBoxSize);
            if (logo != null)
            {
                Image img = logoRt.gameObject.AddComponent<Image>();
                img.sprite = logo;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }
            else
            {
                TextMeshProUGUI t = Text(logoRt, titleText, 120f, new Color(1f, 0.93f, 0.55f), Vector2.zero, logoBoxSize);
                Stretch(t.rectTransform);
                t.alignment = TextAlignmentOptions.Left;
                t.fontStyle = FontStyles.Bold;
                t.outlineWidth = 0.22f;
                t.outlineColor = new Color32(60, 45, 30, 255);
            }

            BuildMenu();

            // 구석: 팀 이름 · 버전
            cornerGroup = Full("Corner", uiRoot.transform);
            var team = Text(cornerGroup.transform, $"{teamText}   v{Application.version}", 24f, new Color(1f, 1f, 1f, 0.75f), Vector2.zero, new Vector2(700f, 40f));
            Anchor(team.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 30f));
            team.alignment = TextAlignmentOptions.Right;
            team.outlineWidth = 0.2f;
            team.outlineColor = new Color32(20, 20, 40, 200);

            // 카드·설정 화면 뒤 어두운 막 + 떠오르는 빛
            overlayGroup = Full("Overlay", uiRoot.transform);
            overlayGroup.alpha = 0f;
            overlayGroup.blocksRaycasts = false;
            Image dim = overlayGroup.gameObject.AddComponent<Image>();
            dim.color = new Color(0.03f, 0.02f, 0.07f, 0.78f);
            dim.raycastTarget = false;
            for (int i = 0; i < 40; i++)
            {
                var er = Rect("Ember", overlayGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * 6f);
                Image ei = er.gameObject.AddComponent<Image>();
                ei.sprite = dot;
                ei.raycastTarget = false;
                var e = new Ember { rt = er, img = ei };
                ResetEmber(e, true);
                embers.Add(e);
            }

            BuildSlots();
            BuildOptions();
            RefreshMenu();

            // 맨 위 검은 화면 (처음에 서서히 밝아짐)
            var f = new GameObject("Fade", typeof(RectTransform), typeof(Image));
            f.transform.SetParent(go.transform, false);
            Stretch((RectTransform)f.transform);
            fade = f.GetComponent<Image>();
            fade.color = Color.black;
        }

        private void BuildMenu()
        {
            menuGroup = Full("Menu", uiRoot.transform);
            menuGroup.blocksRaycasts = menuGroup.interactable = false;
            string[] labels = { newLabel, loadLabel, optionLabel, exitLabel };
            Action[] acts = { StartNewDirect, () => OpenSlots(SlotMode.Load), OpenOptions, Quit };
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                var rt = Rect(labels[i], menuGroup.transform, new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                              new Vector2(menuOffset.x, menuOffset.y - i * menuSpacing), new Vector2(560f, menuSpacing * 0.85f));
                Image hit = rt.gameObject.AddComponent<Image>();
                hit.color = Color.clear; // 마우스가 닿는 영역
                TextMeshProUGUI t = Text(rt, labels[i], menuFontSize, menuColor, Vector2.zero, rt.sizeDelta);
                Stretch(t.rectTransform);
                if (menuFont != null) t.font = menuFont;
                t.alignment = TextAlignmentOptions.Left;
                t.fontStyle = FontStyles.Bold;
                t.outlineWidth = 0.18f;
                t.outlineColor = menuOutlineColor;
                Hover(rt.gameObject, () => { if (state == State.Menu && menu[index].enabled) menuIndex = index; },
                                     () => RunMenu(index));
                menu.Add(new MenuItem { rt = rt, text = t, enabled = true, act = acts[i] });
            }
            menuArrow = Rect("Arrow", menuGroup.transform, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f),
                             new Vector2(menuOffset.x - 70f, menuOffset.y), new Vector2(58f, 64f));
            Image a = menuArrow.gameObject.AddComponent<Image>();
            a.sprite = triangle;
            a.color = menuColor;
            a.raycastTarget = false;
            var outline = menuArrow.gameObject.AddComponent<Outline>();
            outline.effectColor = menuOutlineColor;
            outline.effectDistance = new Vector2(3f, -3f);
        }

        private void BuildSlots()
        {
            slotGroup = Full("Slots", uiRoot.transform);
            slotGroup.alpha = 0f;
            slotGroup.blocksRaycasts = slotGroup.interactable = false;

            slotTitle = Text(slotGroup.transform, "SELECT FARM", 70f, Color.white, new Vector2(0f, 455f), new Vector2(1200f, 90f));
            slotTitle.fontStyle = FontStyles.Bold;
            slotTitle.characterSpacing = 8f;
            Divider(slotGroup.transform, new Vector2(0f, 395f));
            slotSub = Text(slotGroup.transform, "", 30f, new Color(1f, 1f, 1f, 0.75f), new Vector2(0f, 335f), new Vector2(1400f, 90f)); // 두 줄까지

            Vector2 size = new Vector2(340f, 560f);
            for (int i = 0; i < SaveManager.SlotCount; i++)
            {
                int slot = i;
                var card = Rect($"Slot{i + 1}", slotGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f),
                                new Vector2((i - 1) * 460f, -290f), size);
                Image hit = card.gameObject.AddComponent<Image>();
                hit.color = Color.clear;
                Hover(card.gameObject, () => { if (state == State.Slots && confirmDelete < 0 && confirmOverwrite < 0) slotIndex = slot; },
                                       () => OnCardClick(slot));

                // 카드 바탕: 기본 검정, 고르면 흰색 (부드럽게 바뀜)
                var glow = Rect("Glow", card, Vector2.zero, Vector2.one * 0.5f, Vector2.zero, Vector2.zero);
                Stretch(glow);
                cardGlow[i] = glow.gameObject.AddComponent<Image>();
                cardGlow[i].color = cardColor;
                cardGlow[i].raycastTarget = false;

                // 테두리: 끊어진 선 + 모서리 장식 (할로우 나이트 느낌)
                cardLines[i] = new List<Image>();
                Line(i, card, new Vector2(0f, 1f), new Vector2(0f, -0f), new Vector2(size.x * 0.72f, 3f), new Vector2(0f, 1f));   // 위 (왼쪽부터)
                Line(i, card, new Vector2(1f, 0f), Vector2.zero, new Vector2(size.x * 0.72f, 3f), new Vector2(1f, 0f));          // 아래 (오른쪽부터)
                Line(i, card, new Vector2(0f, 1f), Vector2.zero, new Vector2(3f, size.y * 0.78f), new Vector2(0f, 1f));          // 왼쪽 (위부터)
                Line(i, card, new Vector2(1f, 0f), Vector2.zero, new Vector2(3f, size.y * 0.78f), new Vector2(1f, 0f));          // 오른쪽 (아래부터)
                Diamond(i, card, new Vector2(0f, 1f), 22f);
                Diamond(i, card, new Vector2(1f, 0f), 22f);
                Diamond(i, card, new Vector2(1f, 1f), 9f);
                Diamond(i, card, new Vector2(0f, 0f), 9f);

                cardContent[i] = Rect("Content", card, Vector2.zero, Vector2.one * 0.5f, Vector2.zero, Vector2.zero);
                Stretch(cardContent[i]);
                cardBelow[i] = Rect("Below", slotGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f),
                                    new Vector2((i - 1) * 460f, -310f), new Vector2(340f, 70f));
                cards[i] = card;
            }

            arrowL = Rect("ArrowL", slotGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34f, 64f));
            arrowR = Rect("ArrowR", slotGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34f, 64f));
            foreach (RectTransform a in new[] { arrowL, arrowR })
            {
                Image img = a.gameObject.AddComponent<Image>();
                img.sprite = triangle;
                img.raycastTarget = false;
            }
            arrowR.localScale = new Vector3(-1f, 1f, 1f); // 양옆 화살표가 고른 카드를 가리키게 (왼쪽 ▶ · 오른쪽 ◀)

            TextButton(slotGroup.transform, "BACK", new Vector2(0f, -470f), 42f, BackToMenu);
        }

        private void BuildOptions()
        {
            optionGroup = Full("Options", uiRoot.transform);
            optionGroup.alpha = 0f;
            optionGroup.blocksRaycasts = optionGroup.interactable = false;

            var title = Text(optionGroup.transform, "OPTION", 70f, Color.white, new Vector2(0f, 330f), new Vector2(1200f, 90f));
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 8f;
            Divider(optionGroup.transform, new Vector2(0f, 270f));

            float y = 200f;
            AddSlider("전체 소리", y, () => GameSettings.MasterVolume, v => GameSettings.MasterVolume = v, 0f, 1f, 0.1f, v => $"{Mathf.RoundToInt(v * 100f)}%");
            AddSlider("배경음", y -= 90f, () => GameSettings.BgmVolume, v => GameSettings.BgmVolume = v, 0f, 1f, 0.1f, v => $"{Mathf.RoundToInt(v * 100f)}%");
            AddSlider("효과음", y -= 90f, () => GameSettings.SfxVolume, v => GameSettings.SfxVolume = v, 0f, 1f, 0.1f, v => $"{Mathf.RoundToInt(v * 100f)}%");
            AddSlider("마우스 감도", y -= 90f, () => GameSettings.MouseSensitivity, v => GameSettings.MouseSensitivity = v, 0.2f, 3f, 0.1f, v => $"{Mathf.RoundToInt(v * 100f)}%");
            AddToggle("카메라 상하 반전", y -= 90f, () => GameSettings.InvertY, v => GameSettings.InvertY = v);
            AddToggle("전체 화면", y -= 90f, () => GameSettings.Fullscreen, v => GameSettings.Fullscreen = v);

            TextButton(optionGroup.transform, "BACK", new Vector2(0f, -390f), 42f, BackToMenu);
        }

        private OptionRow AddRow(string label, float y)
        {
            var row = Rect(label, optionGroup.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, y), new Vector2(1000f, 86f));
            Image hl = row.gameObject.AddComponent<Image>();
            hl.color = new Color(1f, 1f, 1f, 0f);
            var l = Text(row, label, 38f, Color.white, new Vector2(-250f, 0f), new Vector2(440f, 70f));
            l.alignment = TextAlignmentOptions.Left;
            var r = new OptionRow { rt = row, highlight = hl };
            int index = options.Count;
            Hover(row.gameObject, () => { if (state == State.Options) optionIndex = index; }, null);
            options.Add(r);
            return r;
        }

        private void AddSlider(string label, float y, Func<float> get, Action<float> set, float min, float max, float step, Func<float, string> format)
        {
            OptionRow r = AddRow(label, y);
            var area = Rect("Slider", r.rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(180f, 0f), new Vector2(380f, 30f));
            var bg = Rect("Bar", area, Vector2.zero, Vector2.one * 0.5f, Vector2.zero, Vector2.zero);
            Stretch(bg, 0f, 11f);
            Image bgi = bg.gameObject.AddComponent<Image>();
            bgi.color = new Color(1f, 1f, 1f, 0.25f);
            var fillArea = Rect("Fill Area", area, Vector2.zero, Vector2.one * 0.5f, Vector2.zero, Vector2.zero);
            Stretch(fillArea, 0f, 11f);
            var fill = Rect("Fill", fillArea, Vector2.zero, Vector2.one * 0.5f, Vector2.zero, Vector2.zero);
            Stretch(fill);
            Image fi = fill.gameObject.AddComponent<Image>();
            fi.color = new Color(1f, 0.85f, 0.5f);
            var handleArea = Rect("Handle Area", area, Vector2.zero, Vector2.one * 0.5f, Vector2.zero, Vector2.zero);
            Stretch(handleArea, 14f, 0f);
            var handle = Rect("Handle", handleArea, Vector2.zero, Vector2.one * 0.5f, Vector2.zero, new Vector2(30f, 0f));
            handle.anchorMin = new Vector2(0f, 0f);
            handle.anchorMax = new Vector2(0f, 1f);
            Image hi = handle.gameObject.AddComponent<Image>();
            hi.sprite = dot;
            hi.color = Color.white;

            Slider s = area.gameObject.AddComponent<Slider>();
            s.fillRect = fill;
            s.handleRect = handle;
            s.targetGraphic = hi;
            s.minValue = min;
            s.maxValue = max;
            s.direction = Slider.Direction.LeftToRight;
            var value = Text(r.rt, "", 34f, new Color(1f, 1f, 1f, 0.85f), new Vector2(430f, 0f), new Vector2(140f, 60f));
            value.alignment = TextAlignmentOptions.Right;

            bool syncing = false;
            r.refresh = () => { syncing = true; s.value = get(); value.text = format(get()); syncing = false; };
            s.onValueChanged.AddListener(v => { if (syncing) return; set(v); value.text = format(v); });
            r.step = d => { set(Mathf.Clamp(get() + d * step, min, max)); r.refresh(); };
            r.refresh();
        }

        private void AddToggle(string label, float y, Func<bool> get, Action<bool> set)
        {
            OptionRow r = AddRow(label, y);
            var box = Rect("Toggle", r.rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(250f, 0f), new Vector2(240f, 60f));
            Image bi = box.gameObject.AddComponent<Image>();
            bi.color = new Color(1f, 1f, 1f, 0.12f);
            var t = Text(box, "", 34f, Color.white, Vector2.zero, box.sizeDelta);
            Button b = box.gameObject.AddComponent<Button>();
            b.targetGraphic = bi;
            r.refresh = () =>
            {
                bool on = get();
                t.text = on ? "<   켜짐   >" : "<   꺼짐   >";
                t.color = on ? new Color(1f, 0.88f, 0.55f) : new Color(1f, 1f, 1f, 0.7f);
            };
            r.step = d => { set(!get()); r.refresh(); };
            b.onClick.AddListener(() => r.step(1));
            r.refresh();
        }

        // 칸 내용 새로 그리기
        private void RefreshCards()
        {
            slotSub.text = slotMode == SlotMode.Load ? "이어서 할 농장을 골라 주세요"
                         : slotMode == SlotMode.MakeRoom ? "저장 칸이 가득 찼어요. 새 게임을 하려면 저장 파일 1개를 삭제해야 해요\n<size=85%>삭제할 저장 파일을 골라 주세요</size>"
                         : "새로 시작할 칸을 골라 주세요";
            for (int i = 0; i < cards.Length; i++)
            {
                RectTransform content = cardContent[i], below = cardBelow[i];
                for (int c = content.childCount - 1; c >= 0; c--) Destroy(content.GetChild(c).gameObject);
                for (int c = below.childCount - 1; c >= 0; c--) Destroy(below.GetChild(c).gameObject);
                int slot = i;
                SlotSummary s = SaveManager.GetSummary(slot);

                // 번호 (왼쪽 아래 크게)
                var num = Text(content, $"{slot + 1}.", 80f, Color.white, Vector2.zero, new Vector2(150f, 100f));
                Anchor(num.rectTransform, Vector2.zero, Vector2.zero, new Vector2(30f, 20f));
                num.rectTransform.pivot = Vector2.zero;
                num.alignment = TextAlignmentOptions.BottomLeft;

                if (confirmDelete == slot || confirmOverwrite == slot)
                {
                    bool del = confirmDelete == slot;
                    Text(content, del ? "정말 지울까요?\n<size=70%>지운 농장은 되돌릴 수 없어요</size>"
                                      : "이 저장 파일을 삭제하고\n새로 시작할까요?\n<size=70%>삭제한 저장 파일은 되돌릴 수 없어요</size>",
                         34f, new Color(1f, 0.75f, 0.7f), new Vector2(0f, 60f), new Vector2(300f, 200f));
                    TextButton(content, del ? "지우기" : "새로 시작", new Vector2(0f, -70f), 36f, () => { if (del) DoDelete(slot); else DoOverwrite(slot); }, new Color(1f, 0.6f, 0.55f));
                    TextButton(content, "취소", new Vector2(0f, -135f), 32f, () => { confirmDelete = confirmOverwrite = -1; RefreshCards(); });
                }
                else if (s.exists)
                {
                    var info = Text(content, $"<size=130%>{DateText(s.day)}</size>\n\n{s.bells:N0}벨\n{PlayTime(s.playSeconds)}",
                                    32f, Color.white, new Vector2(0f, 90f), new Vector2(300f, 260f));
                    info.lineSpacing = -10f;
                    var when = Text(content, ShortDate(s.savedAt), 24f, new Color(1f, 1f, 1f, 0.65f), Vector2.zero, new Vector2(200f, 40f));
                    Anchor(when.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 34f));
                    when.alignment = TextAlignmentOptions.Right;
                    // 지우기 (LOAD 화면): "정말 지울까요?" → 지우기만 / 칸이 다 찬 새 게임 화면에서는 카드를 누르면 바로 고름
                    if (slotMode != SlotMode.MakeRoom)
                        TextButton(below, "지우기", Vector2.zero, 30f, () =>
                        {
                            if (state != State.Slots) return;
                            confirmDelete = slotMode == SlotMode.Load ? slot : -1;
                            confirmOverwrite = slotMode != SlotMode.Load ? slot : -1;
                            slotIndex = slot;
                            RefreshCards();
                        }, new Color(1f, 1f, 1f, 0.8f));
                }
                else
                {
                    bool canStart = slotMode == SlotMode.New;
                    Text(content, canStart ? "NEW GAME\n<size=60%>새 게임</size>" : "<size=80%>빈 칸</size>",
                         44f, new Color(1f, 1f, 1f, canStart ? 1f : 0.4f), new Vector2(0f, 40f), new Vector2(300f, 140f));
                }
            }
        }

        // ───────────────────────── UI 도우미 ─────────────────────────

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

        // 화면 전체를 덮는 묶음
        private CanvasGroup Full(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);
            Stretch((RectTransform)go.transform);
            return go.GetComponent<CanvasGroup>();
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        private static void Stretch(RectTransform rt, float padX = 0f, float padY = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padX, padY);
            rt.offsetMax = new Vector2(-padX, -padY);
        }

        private static void Anchor(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
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

        // 글자만 있는 버튼 (마우스를 올리면 밝아지고 커짐)
        private void TextButton(Transform parent, string label, Vector2 pos, float size, Action onClick, Color? color = null)
        {
            Color c = color ?? Color.white;
            var rt = Rect(label, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(300f, size * 1.5f));
            Image hit = rt.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            var t = Text(rt, label, size, new Color(c.r, c.g, c.b, c.a * 0.8f), Vector2.zero, rt.sizeDelta);
            Stretch(t.rectTransform);
            t.characterSpacing = 4f;
            Hover(rt.gameObject, () => { t.color = c; rt.localScale = Vector3.one * 1.08f; }, onClick,
                  () => { t.color = new Color(c.r, c.g, c.b, c.a * 0.8f); rt.localScale = Vector3.one; });
        }

        // 마우스 올림·클릭·나감
        private static void Hover(GameObject go, Action enter, Action click, Action exit = null)
        {
            EventTrigger et = go.AddComponent<EventTrigger>();
            AddTrigger(et, EventTriggerType.PointerEnter, enter);
            AddTrigger(et, EventTriggerType.PointerClick, click);
            AddTrigger(et, EventTriggerType.PointerExit, exit);
        }

        private static void AddTrigger(EventTrigger et, EventTriggerType type, Action a)
        {
            if (a == null) return;
            var e = new EventTrigger.Entry { eventID = type };
            e.callback.AddListener(_ => a());
            et.triggers.Add(e);
        }

        // 제목 아래 장식 줄 (가운데 마름모)
        private void Divider(Transform parent, Vector2 pos)
        {
            var line = Rect("Divider", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(560f, 3f));
            line.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.85f);
            var d = Rect("Gem", line, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18f, 18f));
            d.localRotation = Quaternion.Euler(0f, 0f, 45f);
            d.gameObject.AddComponent<Image>().color = Color.white;
        }

        private void Line(int card, RectTransform parent, Vector2 anchor, Vector2 pos, Vector2 size, Vector2 pivot)
        {
            var rt = Rect("Line", parent, anchor, pivot, pos, size);
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.55f);
            img.raycastTarget = false;
            cardLines[card].Add(img);
        }

        private void Diamond(int card, RectTransform parent, Vector2 anchor, float size)
        {
            var rt = Rect("Corner", parent, anchor, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            rt.localRotation = Quaternion.Euler(0f, 0f, 45f);
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.55f);
            img.raycastTarget = false;
            cardLines[card].Add(img);
        }

        // ▶ 모양 (코드로 그림 - 글꼴에 없어도 보이게)
        private static Sprite MakeTriangleSprite()
        {
            const int W = 64, H = 72;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f;
                    float halfH = H * 0.5f * (1f - fx / W);          // 오른쪽으로 갈수록 좁아짐
                    float d = Mathf.Min(halfH - Mathf.Abs(fy - H * 0.5f), fx); // 가장자리까지 거리
                    byte a = (byte)(Mathf.Clamp01(d * 0.9f) * 255f);
                    px[y * W + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
        }

        // 부드러운 동그라미 (떠오르는 빛·슬라이더 손잡이)
        private static Sprite MakeDotSprite()
        {
            const int N = 32;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(N * 0.5f, N * 0.5f));
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(N * 0.5f - d) * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
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