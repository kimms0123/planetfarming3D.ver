using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using FarmingSystem.Core;
using FarmingSystem.Inventory;
using FarmingSystem.Player;
using FarmingSystem.UI;

namespace FarmingSystem.Rhythm
{
    // 리듬게임 한 판의 결과
    public class HarvestResult
    {
        public int Perfect, Good, Miss, MaxCombo, NoteCount;
        public float ScoreRatio;        // 0~1 (Perfect 100점, Good 60점 기준 최대 점수 대비)
        public float Difficulty;
        public int Tier;                // 0 = 쓰레기, 1 = 기본, 2 = 상급, 3 = 최상급
        public ItemQuality Quality = ItemQuality.Normal;
        public bool AllPerfect;
        public float YieldMultiplier = 1f; // 기대 수확량에 곱할 배율 (등급별)
        public bool Cancelled;

        // 기대 수확량에 배율을 곱한 실제 수확량 (최소 1개)
        public int GetYield(int expectedYield) => Mathf.Max(1, Mathf.RoundToInt(expectedYield * YieldMultiplier));

        // 이전 버전 호환용 이름
        public float Accuracy => ScoreRatio;
    }

    // 리듬 수확 시스템 (기획서 "리듬 수확 시스템" 기준)
    // - 작물 위에 판정 원이 고정되고, 큰 접근 원이 줄어들어 판정 원과 겹치는 순간 원 안의 방향키(↑↓←→)를 입력
    // - 판정: PERFECT / GOOD / MISS. 방향이 틀리면 MISS. 너무 일찍 누른 건 무시
    // - 판정마다 실제 3D 작물이 좌우로 흔들리고(CropReactionController), 마지막에 뽑히는 연출 후 결과창
    // - 결과: ALL PERFECT = 최상급, 그 외 점수 비율(Perfect 100점, Good 60점)로 상급 / 기본 / 쓰레기
    //         등급에 따라 수확량 배율도 달라짐 (최상급 1.2배 ~ 쓰레기 0.5배)
    // - 박자는 메트로놈 소리로 알려줌 (첫 노트 전 4박 카운트). 시간 기준은 오디오 시계라 프레임이 흔들려도 정확
    // - 난이도는 환경(HarvestContext) → 난이도 모델 → 패턴 생성기 순으로 결정
    // - 화면은 농장 화면을 그대로 두고 살짝만 어둡게 (흐름이 끊기지 않게)
    // - UI·소리·이미지는 전부 코드가 만듦. 씬에 빈 오브젝트 하나 + 이 스크립트만 있으면 동작
    public class HarvestRhythmGame : MonoBehaviour
    {
        public static HarvestRhythmGame Instance { get; private set; }

        [Header("난이도")]
        [Tooltip("환경 → 난이도 계산 모델. 비우면 기본 가중치 합 모델 사용. 나중에 FCM/RBFN 모델 에셋을 여기에 넣으면 됨")]
        [SerializeField] private HarvestDifficultyModel difficultyModel;
        [SerializeField, Range(0f, 1f)] private float minDifficulty = 0f;
        [SerializeField, Range(0f, 1f)] private float maxDifficulty = 1f;

        [Header("패턴 (쉬움 → 어려움)")]
        [SerializeField] private float minBpm = 80f;
        [SerializeField] private float maxBpm = 120f;
        [Tooltip("노트 수 (쉬운 작물 6개 ~ 어려운 작물 16개)")]
        [SerializeField] private int minNotes = 6;
        [SerializeField] private int maxNotes = 16;
        [Tooltip("접근 원이 판정 원까지 줄어드는 시간(초)")]
        [SerializeField] private float easyApproachTime = 1.4f;
        [SerializeField] private float hardApproachTime = 0.8f;

        [Header("판정 범위 (±초, 쉬움 → 어려움)")]
        [SerializeField] private float easyPerfectWindow = 0.08f;
        [SerializeField] private float hardPerfectWindow = 0.06f;
        [SerializeField] private float easyGoodWindow = 0.18f;
        [SerializeField] private float hardGoodWindow = 0.12f;
        [Tooltip("이보다 더 일찍 누르면 무시 (벌점 없음)")]
        [SerializeField] private float earlyIgnoreWindow = 0.3f;
        [Tooltip("소리와 화면이 어긋나게 느껴지면 조절 (+면 판정이 늦어짐, 블루투스 이어폰이면 0.1~0.2)")]
        [SerializeField] private float audioOffset = 0f;

        [Header("입력")]
        [Tooltip("방향키 외에 WASD로도 입력 가능")]
        [SerializeField] private bool allowWASD = true;

        [Header("결과 → 작물 등급 (최상급 / 상급 / 기본 / 쓰레기)")]
        [SerializeField] private int perfectPoints = 100;
        [SerializeField] private int goodPoints = 60;
        [Tooltip("켜면 최상급은 ALL PERFECT일 때만. 끄면 아래 최상급 점수 비율 이상이어도 최상급")]
        [SerializeField] private bool topGradeRequiresAllPerfect = true;
        [SerializeField, Range(0f, 1f)] private float topGradeRatio = 0.95f;
        [Tooltip("상급 / 기본 기준 (점수 비율). 기본 기준 아래는 쓰레기")]
        [SerializeField, Range(0f, 1f)] private float highGradeRatio = 0.8f;
        [SerializeField, Range(0f, 1f)] private float normalGradeRatio = 0.5f;

        [Header("결과 → 수확량 배율 (기대 수확량 x 배율, 최소 1개)")]
        [SerializeField] private float topGradeYield = 1.2f;
        [SerializeField] private float highGradeYield = 1f;
        [SerializeField] private float normalGradeYield = 0.8f;
        [SerializeField] private float trashGradeYield = 0.5f;

        [Header("연출")]
        [Tooltip("리듬게임 중 게임 시간(시계·이동·물리)을 멈춤")]
        [SerializeField] private bool pauseWorld = true;
        [SerializeField] private float resultShowSeconds = 2.5f;
        [SerializeField] private Color dimColor = new Color(0f, 0f, 0f, 0.15f);
        [Tooltip("판정 원 반지름 (1920x1080 기준)")]
        [SerializeField] private float judgeRadius = 85f;
        [Tooltip("켜면 접근 원을 한 번에 하나만 보여줌. 다음 원은 앞 노트 박자가 지난 뒤부터 줄어들기 시작 (노트 간격 = 줄어드는 시간)")]
        [SerializeField] private bool oneRingAtATime = true;
        [Tooltip("원 하나만 보일 때, 노트 간격이 아주 짧아도 원이 줄어드는 최소 시간(초). 너무 짧으면 원이 번쩍하고 사라짐")]
        [SerializeField] private float minRingTime = 0.3f;
        [Tooltip("접근 원이 처음 나타날 때 크기 (판정 원의 배수)")]
        [SerializeField] private float approachStartScale = 2.2f;
        [SerializeField] private Color judgeRingColor = new Color(1f, 0.35f, 0.45f, 0.95f);
        [SerializeField] private Color approachColor = new Color(0.45f, 0.95f, 1f, 0.95f);
        [SerializeField] private Color perfectColor = new Color(1f, 0.82f, 0.2f);
        [SerializeField] private Color goodColor = new Color(0.45f, 0.85f, 1f);
        [SerializeField] private Color missColor = new Color(1f, 0.42f, 0.42f);
        [Tooltip("위쪽 오른쪽 '남은 노트' 판 표시")]
        [SerializeField] private bool showRemainingNotes = false;
        [Tooltip("리듬게임 동안 숨길 UI (핫바·십자선은 자동으로 숨김. 그 외에 더 숨길 게 있으면 여기에 추가)")]
        [SerializeField] private GameObject[] hideDuringPlay;
        [Tooltip("한글이 □로 나오면 프로젝트에서 쓰는 한글 TMP 폰트 에셋을 넣기")]
        [SerializeField] private TMP_FontAsset font;

        [Header("소리 (비우면 코드가 만든 소리)")]
        [SerializeField, Range(0f, 1f)] private float metronomeVolume = 0.45f;
        [SerializeField, Range(0f, 1f)] private float sfxVolume = 0.7f;
        [SerializeField] private AudioClip tickClip;
        [SerializeField] private AudioClip accentTickClip;
        [SerializeField] private AudioClip perfectClip;
        [SerializeField] private AudioClip goodClip;
        [SerializeField] private AudioClip missClip;
        [SerializeField] private AudioClip popClip;

        [Header("카메라 강조 (기획서 4장: 화면 전환 없이 작물만 조금 강조)")]
        [Tooltip("리듬게임 동안 카메라가 작물 쪽으로 살짝 다가감")]
        [SerializeField] private bool focusCamera = true;
        [Tooltip("카메라가 바라보는 점을 플레이어에서 작물 쪽으로 얼마나 옮길지 (0 = 플레이어, 1 = 작물)")]
        [SerializeField, Range(0f, 1f)] private float focusBlend = 0.65f;
        [Tooltip("카메라 거리 배율 (0.6 = 평소 거리의 60%까지 다가감)")]
        [SerializeField, Range(0.3f, 1f)] private float focusDistanceScale = 0.6f;
        [Tooltip("강조 중 내려다보는 각도 (도). 음수면 지금 각도 유지")]
        [SerializeField] private float focusPitch = 28f;

        [Header("디자이너 UI 이미지 (비우면 코드가 그린 기본 모양을 씀)")]
        [Tooltip("방향키 아이콘 (Arrow_Up / Down / Left / Right). 4개 다 넣어야 적용")]
        [SerializeField] private Sprite arrowUpSprite;
        [SerializeField] private Sprite arrowDownSprite;
        [SerializeField] private Sprite arrowLeftSprite;
        [SerializeField] private Sprite arrowRightSprite;
        [Tooltip("방향키 패드 키 바탕 (Slot_Light)")]
        [SerializeField] private Sprite keySprite;
        [Tooltip("눌러야 할 키 바탕 (Slot_Dark). 비우면 키 바탕을 주황색으로 칠함")]
        [SerializeField] private Sprite keyHighlightSprite;
        [Tooltip("위쪽 '수확 목표'·'남은 노트' 판 (Panel_Long, 9-슬라이스 테두리 필요)")]
        [SerializeField] private Sprite hudPanelSprite;
        [Tooltip("진행 바 바탕 (Pill_Grey)")]
        [SerializeField] private Sprite progressBackSprite;
        [Tooltip("진행 바 채움 (Pill_Green)")]
        [SerializeField] private Sprite progressFillSprite;
        [Tooltip("아래 안내 문구 판 (Pill_Grey)")]
        [SerializeField] private Sprite guideSprite;
        [Tooltip("결과창 (Window_Header, 9-슬라이스 테두리 필요)")]
        [SerializeField] private Sprite resultWindowSprite;
        [Tooltip("작물 강조 모서리 (Corner_TL_A, 왼쪽 위 모양 1개만 넣으면 돌려서 4개로 씀)")]
        [SerializeField] private Sprite focusCornerSprite;

        [Header("디버그")]
        [Tooltip("키를 누를 때마다 콘솔에 판정 이유를 출력 (어떤 키 → 몇 번 노트 → 오차 몇 초 → 판정)")]
        [SerializeField] private bool logInput = true;
        [Tooltip("플레이 중 이 키를 누르면 테스트 플레이 시작 (게임 창에 포커스가 있어서 바로 키 입력 가능)")]
        [SerializeField] private Key debugHotkey = Key.F9;
        [SerializeField, Range(0f, 1f)] private float debugDifficulty = 0.4f;

        public bool IsPlaying { get; private set; }
        private readonly List<CanvasGroup> hiddenGroups = new List<CanvasGroup>();
        private readonly List<float> hiddenAlphas = new List<float>();
        private GameObject debugCrop;          // 테스트 플레이용 임시 작물
        private int keyPressCount;            // 이번 판에 들어온 방향키 입력 수 (진단용)
        private bool pausedWorldThisSession;  // 이번 판에 실제로 시간을 멈췄는지
        // 가장 잘했을 때의 수확량 배율 (가방 공간 미리 확인용)
        public float MaxYieldMultiplier => Mathf.Max(Mathf.Max(topGradeYield, highGradeYield), Mathf.Max(normalGradeYield, trashGradeYield));

        // ---------- 색 (보내준 UI 이미지 톤) ----------
        private static readonly Color Cream = new Color(1f, 0.98f, 0.93f, 0.96f);
        private static readonly Color CreamBorder = new Color(0.85f, 0.78f, 0.65f, 1f);
        private static readonly Color Brown = new Color(0.36f, 0.25f, 0.18f, 1f);
        private static readonly Color LeafGreen = new Color(0.25f, 0.68f, 0.32f, 1f);
        private static readonly Color BarBack = new Color(0.2f, 0.27f, 0.36f, 0.9f);
        private static readonly Color KeyColor = new Color(0.93f, 0.93f, 0.9f, 1f);
        private static readonly Color KeyHighlight = new Color(1f, 0.72f, 0.2f, 1f);
        private static readonly Color Gold = new Color(1f, 0.8f, 0.25f, 1f);

        // ---------- 진행 상태 ----------
        private enum Phase { Idle, Playing, Pulling, Result }
        private Phase phase = Phase.Idle;

        private class Ring
        {
            public Image Image;
            public int NoteIndex = -1;
        }

        private HarvestPattern pattern;
        private HarvestContext context;
        private HarvestResult result;
        private Action<HarvestResult> onFinished;
        private bool[] judged;
        private int currentIndex;              // 아직 판정 안 된 첫 노트
        private double startDsp;
        private double songTime;
        private int nextTickBeat;
        private int combo;
        private float phaseTimer;
        private float previousTimeScale = 1f;
        private int lastCountdown = int.MinValue;
        private CropReactionController reaction;
        private Transform cropTransform;
        private Vector3 judgeWorldPoint;        // 판정 원이 따라갈 작물 가운데 (월드 좌표)
        private ThirdPersonCameraRig focusedRig;
        private float judgePulse;              // 판정 원이 잠깐 커지는 연출
        private float keyFlash;                // 누른 키 반짝임
        private ArrowDirection keyFlashDir;

        // ---------- UI ----------
        private Canvas canvas;
        private RectTransform canvasRect, judgeRoot;
        private Image judgeRing, judgeGlow, arrowImage, arrowShadow, nextArrow;
        private readonly List<Ring> rings = new List<Ring>();
        private TextMeshProUGUI goalText, comboText, remainText, countdownText, popupText, guideText;
        private Image goalIcon, progressFill;
        private readonly Image[] progressStars = new Image[4];
        private readonly Image[] keyBacks = new Image[4];
        private readonly Image[] keyArrows = new Image[4];
        private readonly Image[] keyShadows = new Image[4];
        private readonly Image[] focusCorners = new Image[4];
        private float cornerAnim;              // 모서리가 바깥에서 안으로 모여드는 연출 (0 → 1)
        private bool CustomArrows => arrowUpSprite != null && arrowDownSprite != null && arrowLeftSprite != null && arrowRightSprite != null;
        private GameObject hudRoot, resultPanel;
        private TextMeshProUGUI resultTitle, resultBody;
        private readonly Image[] resultStars = new Image[4];
        private float popupLife;
        private Sprite discSprite, ringSprite, thickRingSprite, arrowSprite, starSprite, roundSprite, noteSprite;
        private readonly List<Texture2D> ownedTextures = new List<Texture2D>();

        // ---------- 소리 ----------
        private AudioSource[] tickSources;
        private int tickSourceIndex;
        private AudioSource sfxSource;
        private AudioClip genTick, genAccent, genPerfect, genGood, genMiss, genPop;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[HarvestRhythmGame] 씬에 두 개 이상 있음 - 이 오브젝트는 무시됨");
                enabled = false;
                return;
            }
            Instance = this;
            ResolveFont();
            BuildAudio();
            BuildUI();
            canvas.gameObject.SetActive(false);
        }

        // 폰트 정하기: Font 칸이 비어 있으면 씬에서 이미 쓰는 TMP 폰트(한글 폰트)를 찾아 씀
        // 넣은 폰트에 리듬게임에서 쓰는 글자가 없으면(정적 폰트) 콘솔에 빠진 글자와 해결 방법을 알려줌
        private void ResolveFont()
        {
            if (font == null)
            {
                foreach (TextMeshProUGUI t in FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (t.font == null || t.font.name.StartsWith("LiberationSans")) continue;
                    font = t.font;
                    break;
                }
                if (font != null) Debug.Log($"[HarvestRhythmGame] Font 칸이 비어 있어서 씬에서 쓰는 폰트 '{font.name}'를 자동으로 사용");
                else Debug.LogWarning("[HarvestRhythmGame] 한글 폰트를 못 찾았어요. 인스펙터 Font 칸에 한글 TMP 폰트 에셋을 넣어주세요");
            }

            if (font != null)
            {
                const string used = "준비수확목표남은노트개콤보원이판정과겹칠때방향키입력포기완료등급량최대점최상급상기본쓰레기정확도";
                // tryAddCharacter = true: Dynamic 폰트면 빠진 글자를 이 자리에서 바로 추가해 봄
                if (!font.HasCharacters(used, out uint[] missing, true, true) && missing != null && missing.Length > 0)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (uint code in missing) sb.Append(char.ConvertFromUtf32((int)code));
                    Debug.LogWarning($"[HarvestRhythmGame] 폰트 '{font.name}'에 없는 글자: {sb}\n" +
                                     "→ 폰트 에셋 선택 → Inspector의 Atlas Population Mode를 Dynamic으로 바꾸거나, Font Asset Creator로 한글 범위를 다시 만들어주세요");
                }
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (IsPlaying) Finish(true, false);
            foreach (Texture2D t in ownedTextures) if (t != null) Destroy(t);
        }

        // =====================================================================
        // 공개 API
        // =====================================================================

        // 수확 리듬게임 시작. crop = 작물 오브젝트 (흔들림 연출 대상, 없으면 worldAnchor 위치에 UI만)
        public bool Play(HarvestContext ctx, Transform crop, Vector3 worldAnchor, Action<HarvestResult> finished)
        {
            if (IsPlaying || ctx == null) return false;
            if (ctx.Features == null) ctx.BuildFeatures();

            float difficulty = difficultyModel != null
                ? difficultyModel.Evaluate(ctx.Features)
                : RuleBasedDifficultyModel.EvaluateDefault(ctx.Features);
            if (ctx.Crop != null) difficulty += ctx.Crop.harvestDifficultyOffset;
            difficulty = Mathf.Clamp(difficulty, minDifficulty, Mathf.Max(minDifficulty, maxDifficulty));

            Debug.Log($"[수확 리듬게임] {ctx.CropName} 난이도 {difficulty:0.00} ({ctx.Describe()})");
            return Begin(ctx, difficulty, crop, worldAnchor, finished);
        }

        // 이전 버전 호환 (작물 흔들림 없이)
        public bool Play(HarvestContext ctx, Vector3 worldAnchor, Action<HarvestResult> finished)
            => Play(ctx, null, worldAnchor, finished);

        [ContextMenu("디버그: 테스트 플레이")]
        private void DebugPlay()
        {
            if (!Application.isPlaying) { Debug.Log("[HarvestRhythmGame] 플레이 모드에서만 테스트 가능"); return; }
            var ctx = HarvestContext.CreateDebug(UnityEngine.Random.Range(int.MinValue, int.MaxValue));
            Camera cam = Camera.main;
            Vector3 anchor = cam != null ? cam.transform.position + cam.transform.forward * 6f : Vector3.zero;

            // 흔들림까지 확인할 수 있게 플레이어 앞 땅 위에 임시 작물(초록 캡슐)을 세움. 끝나면 지워짐
            Transform crop = SpawnDebugCrop(ref anchor);
            Begin(ctx, debugDifficulty, crop, anchor, r =>
                Debug.Log($"[리듬게임 테스트 결과] P{r.Perfect} G{r.Good} M{r.Miss} 점수 {r.ScoreRatio:P0} → {ItemQualityUtility.GetDisplayName(r.Quality)}, 수확량 x{r.YieldMultiplier}{(r.AllPerfect ? " ALL PERFECT" : "")}{(r.Cancelled ? " (포기)" : "")}"));
        }

        private Transform SpawnDebugCrop(ref Vector3 anchor)
        {
            if (debugCrop != null) Destroy(debugCrop);
            PlayerController player = FindAnyObjectByType<PlayerController>();
            Camera cam = Camera.main;
            if (player == null || cam == null) return null;

            Vector3 fwd = cam.transform.forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 0.001f ? fwd.normalized : player.transform.forward;
            Vector3 pos = player.transform.position + fwd * 2.5f;
            if (Physics.Raycast(pos + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore))
                pos = hit.point;

            debugCrop = new GameObject("TestCrop (리듬게임 테스트용)");
            debugCrop.transform.position = pos;
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(debugCrop.transform, false);
            body.transform.localScale = new Vector3(0.6f, 0.7f, 0.6f);
            body.transform.localPosition = new Vector3(0f, 0.7f, 0f); // 바닥이 땅에 닿게 (기울어질 때 뿌리 쪽이 고정)
            Renderer r = body.GetComponent<Renderer>();
            if (r != null) r.material.color = new Color(0.4f, 0.75f, 0.3f);

            anchor = pos + Vector3.up * 0.7f;
            return debugCrop.transform;
        }

        // 리듬게임 동안 핫바·십자선 등을 숨김 (CanvasGroup 투명도만 바꿔서, 스크립트 동작에는 영향 없음)
        private void HideGameplayUI()
        {
            hiddenGroups.Clear();
            hiddenAlphas.Clear();
            var targets = new List<GameObject>();
            foreach (HotbarUI h in FindObjectsByType<HotbarUI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) targets.Add(h.gameObject);
            foreach (CrosshairUI c in FindObjectsByType<CrosshairUI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) targets.Add(c.gameObject);
            if (hideDuringPlay != null) foreach (GameObject g in hideDuringPlay) if (g != null) targets.Add(g);

            foreach (GameObject g in targets)
            {
                if (g == null) continue;
                CanvasGroup cg = g.GetComponent<CanvasGroup>();
                if (cg == null) cg = g.AddComponent<CanvasGroup>();
                if (hiddenGroups.Contains(cg)) continue;
                hiddenGroups.Add(cg);
                hiddenAlphas.Add(cg.alpha);
                cg.alpha = 0f;
                cg.blocksRaycasts = false;
            }
        }

        private void ShowGameplayUI()
        {
            for (int i = 0; i < hiddenGroups.Count; i++)
            {
                if (hiddenGroups[i] == null) continue;
                hiddenGroups[i].alpha = hiddenAlphas[i];
                hiddenGroups[i].blocksRaycasts = true;
            }
            hiddenGroups.Clear();
            hiddenAlphas.Clear();
        }

        // =====================================================================
        // 진행
        // =====================================================================

        private bool Begin(HarvestContext ctx, float difficulty, Transform crop, Vector3 worldAnchor, Action<HarvestResult> finished)
        {
            var settings = new HarvestPatternGenerator.Settings
            {
                MinBpm = minBpm,
                MaxBpm = maxBpm,
                MinNotes = minNotes,
                MaxNotes = maxNotes,
                EasyApproach = easyApproachTime,
                HardApproach = hardApproachTime,
                EasyPerfect = easyPerfectWindow,
                HardPerfect = hardPerfectWindow,
                EasyGood = easyGoodWindow,
                HardGood = hardGoodWindow,
                Tempo = ctx.Crop != null ? ctx.Crop.harvestTempo : 1f,
                CropPattern = ctx.Crop != null ? ctx.Crop.harvestPattern : null,
                LeadInBeats = 4
            };
            pattern = HarvestPatternGenerator.Generate(difficulty, ctx.Seed, settings);
            context = ctx;
            onFinished = finished;
            result = new HarvestResult { Difficulty = difficulty, NoteCount = pattern.Notes.Count };
            judged = new bool[pattern.Notes.Count];
            currentIndex = 0;
            combo = 0;
            lastCountdown = int.MinValue;
            popupLife = 0f;
            judgePulse = 0f;
            keyFlash = 0f;

            cropTransform = crop;
            reaction = crop != null ? CropReactionController.Attach(crop.gameObject, ctx.Crop != null ? ctx.Crop.harvestShakeScale : 1f) : null;

            canvas.gameObject.SetActive(true);
            hudRoot.SetActive(true);
            resultPanel.SetActive(false);
            countdownText.text = "";
            popupText.text = "";
            Canvas.ForceUpdateCanvases();

            judgeWorldPoint = GetCropCenter(crop, worldAnchor);
            cornerAnim = 0f;
            judgeRoot.anchoredPosition = ProjectToCanvas(judgeWorldPoint);

            // 카메라가 작물 쪽으로 살짝 다가감 (게임 시간이 멈춰도 카메라는 실제 시간으로 움직임)
            focusedRig = focusCamera ? ThirdPersonCameraRig.Instance : null;
            if (focusedRig != null) focusedRig.BeginFocus(judgeWorldPoint, focusBlend, focusDistanceScale, focusPitch);
            goalIcon.sprite = ctx.Crop != null && ctx.Crop.icon != null ? ctx.Crop.icon : null;
            goalIcon.enabled = goalIcon.sprite != null;
            goalIcon.transform.parent.gameObject.SetActive(goalIcon.enabled); // 아이콘이 없으면 흰 동그라미도 숨김
            foreach (Ring r in rings) { r.Image.gameObject.SetActive(false); r.NoteIndex = -1; }
            RefreshHud();

            startDsp = AudioSettings.dspTime + 0.3;
            songTime = -0.3;
            nextTickBeat = 0;

            phase = Phase.Playing;
            IsPlaying = true;
            UIInputBlocker.Block(this);
            HideGameplayUI();
            keyPressCount = 0;
            pausedWorldThisSession = false;
            if (pauseWorld)
            {
                // Input System이 Fixed Update에서 입력을 처리하면, 시간을 멈추는 순간 키 입력도 같이 멈춰서 전부 MISS가 됨
                // → 그 경우엔 시간을 멈추지 않음 (플레이어 조작은 어차피 막혀 있음)
                if (InputSystem.settings != null && InputSystem.settings.updateMode == InputSettings.UpdateMode.ProcessEventsInFixedUpdate)
                {
                    Debug.LogWarning("[HarvestRhythmGame] Input System이 'Process Events In Fixed Update'라서 리듬게임 중 시간을 멈추지 않아요.\n" +
                                     "→ Edit > Project Settings > Input System Package > Update Mode를 'Process Events In Dynamic Update'로 바꾸면 시간 멈춤이 다시 적용돼요");
                }
                else
                {
                    previousTimeScale = Time.timeScale;
                    Time.timeScale = 0f;
                    pausedWorldThisSession = true;
                }
            }
            return true;
        }

        private void Update()
        {
            if (!IsPlaying)
            {
                // 플레이 중 F9 = 테스트 플레이 (게임 창에 포커스가 있는 상태라 바로 키 입력 가능)
                if (debugHotkey != Key.None && Keyboard.current != null && Keyboard.current[debugHotkey].wasPressedThisFrame)
                    DebugPlay();
                return;
            }
            float dt = Time.unscaledDeltaTime;

            UpdateSongTime();
            UpdateEffects(dt);

            // 카메라가 움직이는 동안에도 판정 원이 작물 위에 붙어 있도록 매 프레임 다시 계산
            if (phase != Phase.Result) judgeRoot.anchoredPosition = ProjectToCanvas(judgeWorldPoint);
            UpdateFocusCorners(dt);

            switch (phase)
            {
                case Phase.Playing:
                    ScheduleTicks();
                    UpdateCountdown();

                    if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                    {
                        if (reaction != null) reaction.Restore();
                        reaction = null;
                        Finish(true, true);
                        return;
                    }

                    HandleInput();
                    CheckLateMisses();
                    UpdateNoteVisuals();

                    if (currentIndex >= pattern.Notes.Count && songTime > pattern.LastNoteTime + 0.25)
                        StartPull();
                    break;

                case Phase.Pulling:
                    phaseTimer -= dt;
                    if (phaseTimer <= 0f) ShowResult();
                    break;

                case Phase.Result:
                    phaseTimer -= dt;
                    bool skip = Keyboard.current != null && (Keyboard.current.spaceKey.wasPressedThisFrame || Keyboard.current.enterKey.wasPressedThisFrame)
                                || Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
                    if (phaseTimer <= 0f || (skip && phaseTimer < resultShowSeconds - 0.5f))
                        Finish(false, true);
                    break;
            }
        }

        // 오디오 시계는 띄엄띄엄 갱신돼서 그대로 쓰면 원이 뚝뚝 끊겨 보임 → 실제 경과 시간으로 진행시키며 오디오 시계에 맞춰감
        private void UpdateSongTime()
        {
            double dspSong = AudioSettings.dspTime - startDsp - audioOffset;
            songTime += Time.unscaledDeltaTime;
            double diff = dspSong - songTime;
            if (Math.Abs(diff) > 0.05) songTime = dspSong;
            else songTime += diff * 0.1;
        }

        // 메트로놈: 곧 울릴 박자를 오디오 시계에 미리 예약
        private void ScheduleTicks()
        {
            double now = AudioSettings.dspTime;
            while (nextTickBeat <= pattern.TotalBeats)
            {
                double at = startDsp + nextTickBeat * pattern.BeatLength;
                if (at - now > 0.2) break;
                if (at >= now)
                {
                    bool accent = nextTickBeat % 4 == 0;
                    AudioSource src = tickSources[tickSourceIndex];
                    tickSourceIndex = (tickSourceIndex + 1) % tickSources.Length;
                    src.clip = accent ? Pick(accentTickClip, genAccent) : Pick(tickClip, genTick);
                    src.volume = metronomeVolume;
                    src.PlayScheduled(at);
                }
                nextTickBeat++;
            }
        }

        // 첫 노트 전 카운트: 준비 → 3 → 2 → 1
        private void UpdateCountdown()
        {
            int beat = (int)Math.Floor(songTime / pattern.BeatLength);
            if (beat == lastCountdown) return;
            lastCountdown = beat;
            int lead = pattern.LeadInBeats;
            if (beat < 0 || beat >= lead) countdownText.text = "";
            else countdownText.text = beat == 0 ? "준비" : (lead - beat).ToString();
        }

        private void HandleInput()
        {
            if (!TryReadDirection(out ArrowDirection pressed)) return;

            keyFlash = 0.15f;
            keyFlashDir = pressed;
            keyPressCount++;

            if (currentIndex >= pattern.Notes.Count) return;
            HarvestNote note = pattern.Notes[currentIndex];
            double offset = songTime - note.Time;
            if (offset < -earlyIgnoreWindow)
            {
                if (logInput) Debug.Log($"[리듬 입력] {Arrow(pressed)} 누름 → {note.Number}번 노트까지 {-offset:0.00}초 남음 (너무 일찍, 무시)");
                return;
            }

            double abs = Math.Abs(offset);
            int j;
            string reason;
            if (pressed != note.Direction) { j = 2; reason = "방향 틀림"; }
            else if (abs <= pattern.PerfectWindow) { j = 0; reason = ""; }
            else if (abs <= pattern.GoodWindow) { j = 1; reason = ""; }
            else { j = 2; reason = offset < 0 ? "빠름" : "늦음"; }

            if (logInput)
                Debug.Log($"[리듬 입력] {Arrow(pressed)} 누름 → {note.Number}번 노트({Arrow(note.Direction)}), 오차 {offset:+0.000;-0.000}초 " +
                          $"(Perfect ±{pattern.PerfectWindow:0.00} / Good ±{pattern.GoodWindow:0.00}) → {(j == 0 ? "PERFECT" : j == 1 ? "GOOD" : "MISS")} {reason}");
            Judge(currentIndex, j, reason);
        }

        private bool TryReadDirection(out ArrowDirection dir)
        {
            dir = ArrowDirection.Up;
            Keyboard kb = Keyboard.current;
            if (kb == null) return false;
            if (kb.upArrowKey.wasPressedThisFrame || (allowWASD && kb.wKey.wasPressedThisFrame)) { dir = ArrowDirection.Up; return true; }
            if (kb.downArrowKey.wasPressedThisFrame || (allowWASD && kb.sKey.wasPressedThisFrame)) { dir = ArrowDirection.Down; return true; }
            if (kb.leftArrowKey.wasPressedThisFrame || (allowWASD && kb.aKey.wasPressedThisFrame)) { dir = ArrowDirection.Left; return true; }
            if (kb.rightArrowKey.wasPressedThisFrame || (allowWASD && kb.dKey.wasPressedThisFrame)) { dir = ArrowDirection.Right; return true; }
            return false;
        }

        // 판정 범위를 지나도록 안 누르면 Miss
        private void CheckLateMisses()
        {
            while (currentIndex < pattern.Notes.Count && songTime - pattern.Notes[currentIndex].Time > pattern.GoodWindow)
            {
                if (logInput) Debug.Log($"[리듬 입력] {pattern.Notes[currentIndex].Number}번 노트({Arrow(pattern.Notes[currentIndex].Direction)}) 놓침 - 판정 시간 안에 키 입력 없음");
                Judge(currentIndex, 2, "놓침");
            }
        }

        // 0 = Perfect, 1 = Good, 2 = Miss
        private static string Arrow(ArrowDirection d) => d == ArrowDirection.Up ? "↑" : d == ArrowDirection.Down ? "↓" : d == ArrowDirection.Left ? "←" : "→";

        // 0 = Perfect, 1 = Good, 2 = Miss. reason = MISS 밑에 작게 보여줄 이유 (방향 틀림 / 빠름 / 늦음 / 놓침)
        private void Judge(int index, int j, string reason = "")
        {
            if (judged[index]) return;
            judged[index] = true;
            if (index == currentIndex) while (currentIndex < judged.Length && judged[currentIndex]) currentIndex++;

            if (j == 0) result.Perfect++;
            else if (j == 1) result.Good++;
            else result.Miss++;

            if (j == 2) combo = 0;
            else { combo++; result.MaxCombo = Mathf.Max(result.MaxCombo, combo); }

            // 연출: 판정 원 확대(Perfect 크게), 판정 문구, 효과음, 작물 흔들림
            judgePulse = j == 0 ? 1f : j == 1 ? 0.5f : 0f;
            popupText.text = j == 0 ? "PERFECT!" : j == 1 ? "GOOD"
                           : string.IsNullOrEmpty(reason) ? "MISS" : $"MISS\n<size=45%>{reason}</size>";
            popupText.color = j == 0 ? perfectColor : j == 1 ? goodColor : missColor;
            popupLife = 0.6f;
            sfxSource.PlayOneShot(j == 0 ? Pick(perfectClip, genPerfect) : j == 1 ? Pick(goodClip, genGood) : Pick(missClip, genMiss), sfxVolume);
            if (reaction != null) reaction.React(j, pattern.Notes[index].Direction); // 노트 방향을 따라 작물이 움직임

            RefreshHud();
        }

        private void StartPull()
        {
            phase = Phase.Pulling;
            foreach (AudioSource s in tickSources) s.Stop();
            foreach (Ring r in rings) r.Image.gameObject.SetActive(false);
            arrowImage.enabled = arrowShadow.enabled = nextArrow.enabled = false;
            countdownText.text = "";

            if (reaction != null)
            {
                reaction.Pull();
                phaseTimer = reaction.PullDuration + 0.15f;
            }
            else phaseTimer = 0.3f;

            sfxSource.PlayOneShot(Pick(popClip, genPop), sfxVolume);
        }

        private void ShowResult()
        {
            int n = Mathf.Max(1, result.NoteCount);
            int maxScore = n * perfectPoints;
            int score = result.Perfect * perfectPoints + result.Good * goodPoints;
            result.ScoreRatio = maxScore > 0 ? score / (float)maxScore : 0f;
            result.AllPerfect = result.Perfect == result.NoteCount && result.NoteCount > 0;

            // 등급: ALL PERFECT = 최상급 / 점수 비율로 상급·기본 / 그 아래 쓰레기
            bool top = result.AllPerfect || (!topGradeRequiresAllPerfect && result.ScoreRatio >= topGradeRatio);
            if (top) result.Tier = 3;
            else if (result.ScoreRatio >= highGradeRatio) result.Tier = 2;
            else if (result.ScoreRatio >= normalGradeRatio) result.Tier = 1;
            else result.Tier = 0;
            result.Quality = ItemQualityUtility.FromRank(result.Tier);
            result.YieldMultiplier = result.Tier == 3 ? topGradeYield : result.Tier == 2 ? highGradeYield
                                   : result.Tier == 1 ? normalGradeYield : trashGradeYield;

            for (int i = 0; i < resultStars.Length; i++)
                resultStars[i].color = i <= result.Tier ? Gold : new Color(0.75f, 0.72f, 0.65f, 0.6f);

            int baseYield = context != null && context.Crop != null ? context.Crop.expectedYield : 0;
            int finalYield = baseYield > 0 ? result.GetYield(baseYield) : 0;
            float pP = result.Perfect / (float)n, pG = result.Good / (float)n, pM = result.Miss / (float)n;
            Color gradeColor = result.Tier == 3 ? Gold : result.Tier == 2 ? goodColor : result.Tier == 1 ? Brown : new Color(0.5f, 0.5f, 0.5f);

            resultTitle.text = result.AllPerfect ? "ALL PERFECT!" : "수확 완료!";
            resultTitle.color = result.AllPerfect ? Gold : Brown;
            resultBody.text =
                $"<size=120%><b>{context?.CropName}</b></size>\n" +
                $"\n\n등급  <b><color=#{ColorUtility.ToHtmlStringRGB(gradeColor)}>{ItemQualityUtility.GetDisplayName(result.Quality)}</color></b>   ·   수확량  <b>{finalYield}개</b>\n" +
                $"<color=#{ColorUtility.ToHtmlStringRGB(perfectColor * 0.85f)}>Perfect {pP * 100f:0}%</color>   " +
                $"<color=#{ColorUtility.ToHtmlStringRGB(goodColor * 0.8f)}>Good {pG * 100f:0}%</color>   " +
                $"<color=#{ColorUtility.ToHtmlStringRGB(missColor * 0.9f)}>Miss {pM * 100f:0}%</color>\n" +
                $"최대 콤보 {result.MaxCombo}   ·   점수 {result.ScoreRatio * 100f:0}%";

            hudRoot.SetActive(false);
            resultPanel.SetActive(true);
            phase = Phase.Result;
            phaseTimer = resultShowSeconds;
        }

        private void Finish(bool cancelled, bool invokeCallback)
        {
            if (!IsPlaying) return;
            IsPlaying = false;
            phase = Phase.Idle;
            result.Cancelled = cancelled;

            foreach (AudioSource s in tickSources) if (s != null) s.Stop();
            if (canvas != null) canvas.gameObject.SetActive(false);

            UIInputBlocker.Unblock(this);
            ShowGameplayUI();
            if (debugCrop != null) { Destroy(debugCrop); debugCrop = null; }
            if (focusedRig != null) focusedRig.EndFocus();
            focusedRig = null;
            if (pausedWorldThisSession) Time.timeScale = previousTimeScale <= 0f ? 1f : previousTimeScale;
            pausedWorldThisSession = false;

            // 진단: 키 입력이 한 번도 안 들어왔는데 끝났으면 원인 안내
            if (!cancelled && keyPressCount == 0 && result.NoteCount > 0)
                Debug.LogWarning("[HarvestRhythmGame] 이번 판에 방향키 입력이 한 번도 안 들어왔어요 (그래서 전부 MISS).\n" +
                                 "→ 1) Game 창을 클릭해서 포커스를 준 뒤 시작했는지 (인스펙터 메뉴로 시작하면 키보드가 Inspector에 가 있음. 대신 플레이 중 F9로 시작해 보세요)\n" +
                                 "→ 2) Edit > Project Settings > Player > Active Input Handling이 'Input System Package' 또는 'Both'인지");

            if (cancelled) Debug.Log($"[수확 리듬게임] {context?.CropName} 포기 - 작물은 그대로 남아 있음");
            else Debug.Log($"[수확 리듬게임] 결과 P{result.Perfect} G{result.Good} M{result.Miss}, 점수 {result.ScoreRatio:P0} → {ItemQualityUtility.GetDisplayName(result.Quality)}, 수확량 x{result.YieldMultiplier}");

            reaction = null;
            cropTransform = null;
            Action<HarvestResult> cb = onFinished;
            onFinished = null;
            if (invokeCallback) cb?.Invoke(result);
        }

        // =====================================================================
        // 화면 갱신
        // =====================================================================

        // 접근 원: 다가오는 노트마다 하나씩, 판정 원을 향해 줄어듦 (먼저 올 노트가 진하게, 뒤 노트는 흐리게)
        // 이 노트의 접근 원이 줄어드는 시간
        // 원 하나만 모드: 앞 노트와의 간격만큼 (앞 노트 박자가 지나는 순간 나타나서 이 노트 박자에 딱 닿음)
        private float NoteApproach(int i)
        {
            float approach = pattern.ApproachTime;
            if (!oneRingAtATime || i <= 0) return approach;
            float gap = (float)(pattern.Notes[i].Time - pattern.Notes[i - 1].Time);
            return Mathf.Clamp(gap, Mathf.Min(minRingTime, approach), approach);
        }

        private void UpdateNoteVisuals()
        {
            float approach = pattern.ApproachTime;
            int ringIdx = 0;
            for (int i = currentIndex; i < pattern.Notes.Count && ringIdx < rings.Count; i++)
            {
                if (judged[i]) continue;
                // 원 하나만 모드: 지금 노트(아직 안 친 첫 노트)만 표시
                if (oneRingAtATime && i != currentIndex) break;

                float a = NoteApproach(i);
                double t = pattern.Notes[i].Time - songTime;
                if (t > a) break;

                Ring r = rings[ringIdx++];
                r.NoteIndex = i;
                float progress = Mathf.Clamp01((float)(t / a));
                float appear = Mathf.Clamp01((float)((a - t) / (a * 0.15f)));
                r.Image.gameObject.SetActive(true);
                r.Image.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, approachStartScale, progress);
                Color c = approachColor;
                c.a *= appear * (i == currentIndex ? 1f : 0.4f);
                r.Image.color = c;
            }
            for (; ringIdx < rings.Count; ringIdx++) rings[ringIdx].Image.gameObject.SetActive(false);

            // 판정 원 안의 방향키 = 지금 눌러야 할 노트, 옆의 작은 화살표 = 다음 노트
            bool hasCurrent = currentIndex < pattern.Notes.Count && pattern.Notes[currentIndex].Time - songTime <= NoteApproach(currentIndex);
            arrowImage.enabled = hasCurrent;
            arrowShadow.enabled = hasCurrent && !CustomArrows; // 디자이너 화살표는 자체 테두리·빛이 있어서 그림자 안 씀
            if (hasCurrent) SetArrow(arrowImage, pattern.Notes[currentIndex].Direction);
            if (hasCurrent && !CustomArrows) arrowShadow.rectTransform.localEulerAngles = arrowImage.rectTransform.localEulerAngles;
            int next = currentIndex + 1;
            bool hasNext = hasCurrent && next < pattern.Notes.Count && pattern.Notes[next].Time - songTime <= approach * 1.5f;
            nextArrow.enabled = hasNext;
            if (hasNext) SetArrow(nextArrow, pattern.Notes[next].Direction);

            // 방향키 패드: 눌러야 할 키를 주황색으로
            for (int k = 0; k < 4; k++)
            {
                bool expected = hasCurrent && (int)pattern.Notes[currentIndex].Direction == k;
                bool flashing = keyFlash > 0f && (int)keyFlashDir == k;
                if (keySprite != null)
                {
                    // 디자이너 키: 눌러야 할 키는 Slot_Dark로 바꾸고(없으면 주황 틴트), 누른 키는 살짝 눌림
                    keyBacks[k].sprite = expected && keyHighlightSprite != null ? keyHighlightSprite : keySprite;
                    keyBacks[k].color = expected && keyHighlightSprite == null ? KeyHighlight : Color.white;
                }
                else keyBacks[k].color = flashing ? Color.white : expected ? KeyHighlight : KeyColor;
                keyBacks[k].rectTransform.localScale = Vector3.one * (flashing ? 0.92f : expected ? 1.08f : 1f);
                keyArrows[k].color = CustomArrows ? new Color(1f, 1f, 1f, expected ? 1f : 0.45f)
                                                  : expected ? Color.white : new Color(0.35f, 0.35f, 0.35f);
            }

            // 진행 바: 첫 노트 ~ 마지막 노트 구간
            double span = Math.Max(0.01, pattern.LastNoteTime - pattern.FirstNoteTime + pattern.BeatLength);
            float prog = Mathf.Clamp01((float)((songTime - pattern.FirstNoteTime + pattern.BeatLength) / span));
            progressFill.fillAmount = prog;
            for (int s = 0; s < progressStars.Length; s++)
                progressStars[s].color = prog >= (s + 1) / (float)progressStars.Length - 0.001f ? Gold : new Color(0.3f, 0.4f, 0.6f, 1f);
        }

        private void UpdateEffects(float dt)
        {
            // 판정 원 확대 후 복귀
            judgePulse = Mathf.Max(0f, judgePulse - dt * 4f);
            float s = 1f + 0.18f * judgePulse;
            judgeRing.rectTransform.localScale = Vector3.one * s;
            Color g = perfectColor;
            g.a = 0.6f * judgePulse;
            judgeGlow.color = g;
            judgeGlow.rectTransform.localScale = Vector3.one * (1f + 0.5f * (1f - judgePulse));

            keyFlash = Mathf.Max(0f, keyFlash - dt);

            if (popupLife > 0f)
            {
                popupLife -= dt;
                Color c = popupText.color;
                c.a = Mathf.Clamp01(popupLife / 0.25f);
                popupText.color = c;
                popupText.rectTransform.anchoredPosition = new Vector2(0f, judgeRadius + 55f + (0.6f - popupLife) * 40f);
                popupText.rectTransform.localScale = Vector3.one * (1f + Mathf.Clamp01((popupLife - 0.45f) / 0.15f) * 0.3f);
            }
        }

        private void RefreshHud()
        {
            int done = result.Perfect + result.Good + result.Miss;
            goalText.text = $"<size=60%><color=#{ColorUtility.ToHtmlStringRGB(LeafGreen)}>수확 목표</color></size>\n{result.Perfect + result.Good} / {pattern.Notes.Count}개";
            comboText.text = $"<size=40%>COMBO</size>\n{combo:00}";
            if (remainText != null) remainText.text = $"<size=60%>남은 노트</size>\n{pattern.Notes.Count - done}개";
        }

        // 작물 모델의 가운데 (월드 좌표). 모델이 없으면 fallback 위치
        private static Vector3 GetCropCenter(Transform crop, Vector3 fallback)
        {
            if (crop == null) return fallback;
            bool found = false;
            Bounds b = new Bounds(crop.position, Vector3.zero);
            foreach (Renderer r in crop.GetComponentsInChildren<Renderer>())
            {
                if (!found) { b = r.bounds; found = true; }
                else b.Encapsulate(r.bounds);
            }
            return found ? b.center : crop.position;
        }

        // 월드 위치 → 캔버스 위치. 화면 밖으로 넘치지 않게 안쪽으로 당김 (위쪽 HUD·아래쪽 방향키 자리 피함)
        private Vector2 ProjectToCanvas(Vector3 world)
        {
            Vector2 local = Vector2.zero;
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 screen = cam.WorldToScreenPoint(world);
                if (screen.z > 0f) RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out local);
            }

            Vector2 half = canvasRect.rect.size * 0.5f;
            float r0 = judgeRadius * approachStartScale + 20f;
            float maxX = Mathf.Max(0f, half.x - r0);
            float top = half.y - r0 - 150f;    // 위쪽 HUD 자리
            float bottom = -half.y + r0 + 300f; // 아래쪽 방향키·안내 자리
            return new Vector2(Mathf.Clamp(local.x, -maxX, maxX), Mathf.Clamp(local.y, Mathf.Min(bottom, top), Mathf.Max(bottom, top)));
        }

        private static float DirToAngle(ArrowDirection d)
        {
            switch (d)
            {
                case ArrowDirection.Down: return 180f;
                case ArrowDirection.Left: return 90f;
                case ArrowDirection.Right: return -90f;
                default: return 0f;
            }
        }

        private static AudioClip Pick(AudioClip custom, AudioClip generated) => custom != null ? custom : generated;

        // =====================================================================
        // UI 생성 (보내준 이미지 배치 기준, 1920x1080)
        // =====================================================================

        private void BuildUI()
        {
            discSprite = MakeCircleSprite(256, 0f, 0f);
            ringSprite = MakeCircleSprite(256, 0.05f, 0f);
            thickRingSprite = MakeCircleSprite(256, 0.12f, 0.35f);
            arrowSprite = MakeArrowSprite(128);
            starSprite = MakeStarSprite(128);
            roundSprite = MakeRoundedSprite(64, 24);
            noteSprite = MakeNoteSprite(128);

            var canvasGO = new GameObject("HarvestRhythmCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, false);
            canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasRect = (RectTransform)canvasGO.transform;

            // 농장 화면을 살짝만 어둡게 (뒤 UI 클릭도 막음)
            Image dim = CreateImage("Dim", canvasRect, null, dimColor);
            Stretch(dim.rectTransform);
            dim.raycastTarget = true;

            hudRoot = new GameObject("HUD", typeof(RectTransform));
            hudRoot.transform.SetParent(canvasRect, false);
            Stretch((RectTransform)hudRoot.transform);
            Transform hud = hudRoot.transform;

            // ---- 판정 원 (작물 위) ----
            judgeRoot = new GameObject("JudgeRoot", typeof(RectTransform)).GetComponent<RectTransform>();
            judgeRoot.SetParent(hud, false);
            SetAnchored(judgeRoot, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * judgeRadius * 2f);

            judgeGlow = CreateImage("Glow", judgeRoot, discSprite, Color.clear);
            Stretch(judgeGlow.rectTransform);
            Image inner = CreateImage("Inner", judgeRoot, discSprite, new Color(1f, 0.55f, 0.55f, 0.28f));
            Stretch(inner.rectTransform);
            judgeRing = CreateImage("JudgeRing", judgeRoot, thickRingSprite, judgeRingColor);
            Stretch(judgeRing.rectTransform);

            arrowShadow = CreateImage("ArrowShadow", judgeRoot, arrowSprite, new Color(0.55f, 0.15f, 0.15f, 0.9f));
            SetAnchored(arrowShadow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -4f), Vector2.one * judgeRadius * 1.25f);
            arrowImage = CreateImage("Arrow", judgeRoot, arrowSprite, Color.white);
            SetAnchored(arrowImage.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * judgeRadius * 1.15f);

            nextArrow = CreateImage("NextArrow", judgeRoot, arrowSprite, new Color(1f, 1f, 1f, 0.6f));
            SetAnchored(nextArrow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(judgeRadius + 45f, -judgeRadius + 10f), Vector2.one * 42f);

            for (int i = 0; i < 4; i++)
            {
                Image ring = CreateImage("ApproachRing", judgeRoot, ringSprite, approachColor);
                Stretch(ring.rectTransform);
                ring.gameObject.SetActive(false);
                rings.Add(new Ring { Image = ring });
            }

            popupText = CreateText("Judgement", judgeRoot, 54f, FontStyles.Bold);
            SetAnchored(popupText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, judgeRadius + 55f), new Vector2(420f, 80f));

            countdownText = CreateText("Countdown", judgeRoot, 96f, FontStyles.Bold);
            SetAnchored(countdownText.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(400f, 160f));

            // ---- 왼쪽 위: 수확 목표 ----
            Image goalPanel = CreatePanel("GoalPanel", hud, new Vector2(0f, 1f), new Vector2(230f, -95f), new Vector2(380f, 120f), Cream);
            Image iconBack = CreateImage("IconBack", goalPanel.rectTransform, discSprite, new Color(1f, 1f, 1f, 1f));
            SetAnchored(iconBack.rectTransform, new Vector2(0f, 0.5f), new Vector2(65f, 0f), Vector2.one * 96f);
            goalIcon = CreateImage("Icon", iconBack.rectTransform, null, Color.white);
            goalIcon.preserveAspect = true;
            SetAnchored(goalIcon.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * 72f);
            goalText = CreateText("Goal", goalPanel.rectTransform, 46f, FontStyles.Bold);
            goalText.color = Brown;
            goalText.outlineWidth = 0f;
            SetAnchored(goalText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(55f, 0f), new Vector2(250f, 110f));

            // ---- 위 가운데: 음악 진행 바 + 별 ----
            Image barBack = CreatePanel("ProgressBar", hud, new Vector2(0.5f, 1f), new Vector2(-60f, -85f), new Vector2(680f, 64f), BarBack);
            Image note = CreateImage("NoteIcon", barBack.rectTransform, discSprite, new Color(0.3f, 0.36f, 0.45f, 1f));
            SetAnchored(note.rectTransform, new Vector2(0f, 0.5f), new Vector2(34f, 0f), Vector2.one * 56f);
            Image noteGlyph = CreateImage("Glyph", note.rectTransform, noteSprite, Color.white);
            SetAnchored(noteGlyph.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * 36f);
            Image fillBack = CreatePanel("FillBack", barBack.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(25f, 0f), new Vector2(590f, 34f), new Color(0.12f, 0.16f, 0.22f, 1f));
            progressFill = CreateImage("Fill", fillBack.rectTransform, roundSprite, new Color(0.3f, 0.85f, 0.45f, 1f));
            Stretch(progressFill.rectTransform);
            progressFill.type = Image.Type.Filled;
            progressFill.fillMethod = Image.FillMethod.Horizontal;
            progressFill.fillAmount = 0f;
            for (int s = 0; s < progressStars.Length; s++)
            {
                progressStars[s] = CreateImage("Star", fillBack.rectTransform, starSprite, Color.gray);
                float x = (s + 1) / (float)progressStars.Length;
                SetAnchored(progressStars[s].rectTransform, new Vector2(x, 0.5f), new Vector2(-24f, 0f), Vector2.one * 44f);
            }

            // ---- 콤보 ----
            comboText = CreateText("Combo", hud, 82f, FontStyles.Bold);
            comboText.color = Gold;
            comboText.outlineColor = new Color32(90, 55, 20, 255);
            comboText.outlineWidth = 0.25f;
            comboText.lineSpacing = -30f;
            SetAnchored(comboText.rectTransform, new Vector2(0.5f, 1f), new Vector2(390f, -95f), new Vector2(220f, 150f));

            // ---- 오른쪽 위: 남은 노트 ----
            Image remainPanel = CreatePanel("RemainPanel", hud, new Vector2(1f, 1f), new Vector2(-210f, -95f), new Vector2(340f, 120f), Cream);
            remainPanel.gameObject.SetActive(showRemainingNotes);
            Image remainGlyph = CreateImage("Glyph", remainPanel.rectTransform, noteSprite, new Color(0.55f, 0.35f, 0.9f));
            SetAnchored(remainGlyph.rectTransform, new Vector2(0f, 0.5f), new Vector2(65f, 0f), Vector2.one * 70f);
            remainText = CreateText("Remain", remainPanel.rectTransform, 46f, FontStyles.Bold);
            remainText.color = Brown;
            remainText.outlineWidth = 0f;
            SetAnchored(remainText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(40f, 0f), new Vector2(230f, 110f));

            // ---- 아래: 방향키 패드 ----
            var pad = new GameObject("KeyPad", typeof(RectTransform)).GetComponent<RectTransform>();
            pad.SetParent(hud, false);
            SetAnchored(pad, new Vector2(0.5f, 0f), new Vector2(0f, 205f), new Vector2(270f, 180f));
            Vector2[] keyPos = { new Vector2(0f, 44f), new Vector2(0f, -44f), new Vector2(-88f, -44f), new Vector2(88f, -44f) };
            for (int k = 0; k < 4; k++)
            {
                keyShadows[k] = CreatePanel("KeyShadow", pad, new Vector2(0.5f, 0.5f), keyPos[k] + new Vector2(0f, -6f), Vector2.one * 76f, new Color(0.45f, 0.42f, 0.4f, 0.9f));
                keyBacks[k] = CreatePanel("Key", pad, new Vector2(0.5f, 0.5f), keyPos[k], Vector2.one * 76f, KeyColor);
                keyArrows[k] = CreateImage("Arrow", keyBacks[k].rectTransform, arrowSprite, Color.gray);
                SetAnchored(keyArrows[k].rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * 42f);
                SetArrow(keyArrows[k], (ArrowDirection)k);
                if (keySprite != null)
                {
                    keyBacks[k].sprite = keySprite;
                    keyBacks[k].type = Image.Type.Simple;
                    keyBacks[k].color = Color.white;
                    keyShadows[k].enabled = false; // 디자이너 키는 테두리가 있어서 그림자 안 씀
                    SetAnchored(keyArrows[k].rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * 48f);
                }
            }

            // ---- 아래: 안내 문구 ----
            Image guide = CreatePanel("Guide", hud, new Vector2(0.5f, 0f), new Vector2(0f, 75f), new Vector2(760f, 84f), Cream);
            guideText = CreateText("GuideText", guide.rectTransform, 36f, FontStyles.Bold);
            guideText.color = Brown;
            guideText.outlineWidth = 0f;
            guideText.text = "원이 <color=#E8476A>판정 원</color>과 겹칠 때 방향키 입력!   <size=70%><color=#9A8B7A>Esc 포기</color></size>";
            Stretch(guideText.rectTransform);

            // ---- 결과창 ----
            Image panelBorder = CreatePanel("ResultPanel", canvasRect, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640f, 460f), CreamBorder);
            resultPanel = panelBorder.gameObject;
            Image panel = CreatePanel("Inner", panelBorder.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620f, 440f), Cream);
            resultTitle = CreateText("Title", panel.rectTransform, 60f, FontStyles.Bold);
            resultTitle.outlineWidth = 0f;
            SetAnchored(resultTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(600f, 80f));
            for (int i = 0; i < resultStars.Length; i++)
            {
                resultStars[i] = CreateImage("Star", panel.rectTransform, starSprite, Gold);
                SetAnchored(resultStars[i].rectTransform, new Vector2(0.5f, 1f), new Vector2((i - 1.5f) * 62f, -205f), Vector2.one * 54f);
            }
            resultBody = CreateText("Body", panel.rectTransform, 30f, FontStyles.Normal);
            resultBody.color = Brown;
            resultBody.outlineWidth = 0f;
            resultBody.textWrappingMode = TextWrappingModes.Normal;
            SetAnchored(resultBody.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -45f), new Vector2(580f, 300f));
            resultPanel.SetActive(false);

            // ---- 작물 강조 모서리 (판정 원 바깥 4귀퉁이) ----
            if (focusCornerSprite != null)
            {
                float[] rot = { 0f, -90f, 180f, 90f }; // 왼쪽 위 → 오른쪽 위 → 오른쪽 아래 → 왼쪽 아래
                for (int i = 0; i < 4; i++)
                {
                    focusCorners[i] = CreateImage("FocusCorner", judgeRoot, focusCornerSprite, Color.white);
                    focusCorners[i].preserveAspect = true;
                    SetAnchored(focusCorners[i].rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * 46f);
                    focusCorners[i].rectTransform.localEulerAngles = new Vector3(0f, 0f, rot[i]);
                    focusCorners[i].transform.SetAsFirstSibling();
                }
            }

            // ---- 디자이너 이미지 적용 ----
            Skin(goalPanel, hudPanelSprite);
            Skin(remainPanel, hudPanelSprite);
            if (Skin(barBack, progressBackSprite)) fillBack.color = Color.clear;
            if (progressFillSprite != null) { progressFill.sprite = progressFillSprite; progressFill.color = Color.white; }
            Skin(guide, guideSprite);
            if (Skin(panelBorder, resultWindowSprite))
            {
                panel.color = Color.clear;
                resultTitle.color = Color.white;
            }
        }

        // 디자이너 이미지가 있으면 바꿔 끼움. 9-슬라이스 테두리가 설정된 이미지면 크기가 바뀌어도 모서리가 안 늘어남
        private static bool Skin(Image img, Sprite sprite)
        {
            if (img == null || sprite == null) return false;
            img.sprite = sprite;
            img.type = sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            img.color = Color.white;

            // 판이 이미지 테두리보다 작으면 모서리끼리 겹쳐 찌그러지므로, 테두리가 판 안에 들어가도록 자동으로 줄임
            if (img.type == Image.Type.Sliced)
            {
                Vector2 size = img.rectTransform.sizeDelta;
                Vector4 bd = sprite.border; // x = 왼쪽, y = 아래, z = 오른쪽, w = 위
                float needX = size.x > 0f ? (bd.x + bd.z) / (size.x * 0.9f) : 1f;
                float needY = size.y > 0f ? (bd.y + bd.w) / (size.y * 0.9f) : 1f;
                img.pixelsPerUnitMultiplier = Mathf.Max(1f, Mathf.Max(needX, needY));
            }
            return true;
        }

        // 방향 아이콘 설정: 디자이너 화살표면 방향별 이미지, 아니면 기본 화살표를 돌려서 사용
        private void SetArrow(Image img, ArrowDirection dir)
        {
            if (CustomArrows)
            {
                img.sprite = dir == ArrowDirection.Up ? arrowUpSprite : dir == ArrowDirection.Down ? arrowDownSprite
                           : dir == ArrowDirection.Left ? arrowLeftSprite : arrowRightSprite;
                img.preserveAspect = true;
                img.rectTransform.localEulerAngles = Vector3.zero;
            }
            else
            {
                img.sprite = arrowSprite;
                img.rectTransform.localEulerAngles = new Vector3(0f, 0f, DirToAngle(dir));
            }
        }

        // 강조 모서리: 시작할 때 바깥에서 판정 원 쪽으로 모여들고, 숨쉬듯 살짝 움직임
        private void UpdateFocusCorners(float dt)
        {
            if (focusCorners[0] == null) return;
            cornerAnim = Mathf.MoveTowards(cornerAnim, 1f, dt * 3f);
            float ease = 1f - (1f - cornerAnim) * (1f - cornerAnim);
            float breathe = Mathf.Sin(Time.unscaledTime * 3f) * 4f;
            float d = judgeRadius * Mathf.Lerp(2.4f, 1.35f, ease) + breathe;
            Vector2[] dirs = { new Vector2(-1f, 1f), new Vector2(1f, 1f), new Vector2(1f, -1f), new Vector2(-1f, -1f) };
            for (int i = 0; i < 4; i++)
            {
                focusCorners[i].rectTransform.anchoredPosition = dirs[i] * d;
                Color c = Color.white;
                c.a = ease;
                focusCorners[i].color = c;
            }
        }

        private Image CreatePanel(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color color)
        {
            Image img = CreateImage(name, parent, roundSprite, color);
            img.type = Image.Type.Sliced;
            SetAnchored(img.rectTransform, anchor, pos, size);
            return img;
        }

        private static Image CreateImage(string name, Transform parent, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private TextMeshProUGUI CreateText(string name, Transform parent, float size, FontStyles style)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Color.white;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.outlineWidth = 0.2f;
            t.outlineColor = new Color32(60, 40, 25, 255);
            return t;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static void SetAnchored(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        // ---------- 이미지 생성 (폰트에 화살표·별 글자가 없어도 되도록 직접 그림) ----------

        private Texture2D NewTexture(int w, int h, string name)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = name
            };
            ownedTextures.Add(tex);
            return tex;
        }

        // ringThickness 0 = 꽉 찬 원, 0보다 크면 고리. glow > 0이면 고리 바깥으로 빛 번짐
        private Sprite MakeCircleSprite(int size, float ringThickness, float glow)
        {
            Texture2D tex = NewTexture(size, size, "RhythmCircle (generated)");
            var px = new Color32[size * size];
            float aa = 1.5f / size;
            float outerEdge = glow > 0f ? 0.98f - glow * 0.5f : 0.98f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float a = Mathf.Clamp01((outerEdge - r) / aa);
                    if (ringThickness > 0f) a = Mathf.Min(a, Mathf.Clamp01((r - (outerEdge - ringThickness)) / aa));
                    if (glow > 0f)
                    {
                        float dist = Mathf.Abs(r - (outerEdge - ringThickness * 0.5f)) - ringThickness * 0.5f;
                        float g = 1f - Mathf.Clamp01(dist / (glow * 0.45f));
                        a = Mathf.Max(a, g * g * 0.6f);
                    }
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        // 위쪽을 가리키는 굵은 화살표 (회전해서 4방향으로 사용)
        private Sprite MakeArrowSprite(int size)
        {
            Texture2D tex = NewTexture(size, size, "RhythmArrow (generated)");
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // 2x2 슈퍼샘플링으로 가장자리 부드럽게
                    int hit = 0;
                    for (int sy = 0; sy < 2; sy++)
                        for (int sx = 0; sx < 2; sx++)
                        {
                            float u = (x + 0.25f + sx * 0.5f) / size * 2f - 1f;
                            float v = (y + 0.25f + sy * 0.5f) / size * 2f - 1f;
                            bool shaft = Mathf.Abs(u) <= 0.2f && v >= -0.75f && v <= 0.1f;
                            bool head = v >= 0.0f && v <= 0.8f && Mathf.Abs(u) <= 0.62f * (0.8f - v) / 0.8f;
                            if (shaft || head) hit++;
                        }
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(hit * 255 / 4));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        // 음표 아이콘 (머리 + 기둥 + 꼬리)
        private Sprite MakeNoteSprite(int size)
        {
            Texture2D tex = NewTexture(size, size, "RhythmNote (generated)");
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int hit = 0;
                    for (int sy = 0; sy < 2; sy++)
                        for (int sx = 0; sx < 2; sx++)
                        {
                            float u = (x + 0.25f + sx * 0.5f) / size * 2f - 1f;
                            float v = (y + 0.25f + sy * 0.5f) / size * 2f - 1f;
                            float hx = (u + 0.22f) / 0.34f, hy = (v + 0.55f) / 0.26f;
                            bool head = hx * hx + hy * hy <= 1f;
                            bool stem = u >= 0.02f && u <= 0.14f && v >= -0.55f && v <= 0.8f;
                            bool flag = u >= 0.1f && u <= 0.5f && v <= 0.8f - (u - 0.1f) * 0.6f && v >= 0.5f - (u - 0.1f) * 0.6f;
                            if (head || stem || flag) hit++;
                        }
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(hit * 255 / 4));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        // 다섯 꼭지 별
        private Sprite MakeStarSprite(int size)
        {
            Texture2D tex = NewTexture(size, size, "RhythmStar (generated)");
            var poly = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float ang = Mathf.PI / 2f + i * Mathf.PI / 5f;
                float rad = i % 2 == 0 ? 0.95f : 0.42f;
                poly[i] = new Vector2(Mathf.Cos(ang) * rad, Mathf.Sin(ang) * rad - 0.05f);
            }
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int hit = 0;
                    for (int sy = 0; sy < 2; sy++)
                        for (int sx = 0; sx < 2; sx++)
                        {
                            float u = (x + 0.25f + sx * 0.5f) / size * 2f - 1f;
                            float v = (y + 0.25f + sy * 0.5f) / size * 2f - 1f;
                            if (InsidePolygon(poly, u, v)) hit++;
                        }
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(hit * 255 / 4));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static bool InsidePolygon(Vector2[] poly, float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if ((poly[i].y > y) != (poly[j].y > y) &&
                    x < (poly[j].x - poly[i].x) * (y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            }
            return inside;
        }

        // 둥근 사각형 (9-슬라이스: 크기를 바꿔도 모서리가 늘어나지 않음)
        private Sprite MakeRoundedSprite(int size, int radius)
        {
            Texture2D tex = NewTexture(size, size, "RhythmRounded (generated)");
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                    float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(radius - d + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                 SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        // =====================================================================
        // 소리 생성
        // =====================================================================

        private void BuildAudio()
        {
            tickSources = new AudioSource[4];
            for (int i = 0; i < tickSources.Length; i++) tickSources[i] = NewSource();
            sfxSource = NewSource();

            genTick = MakeTone("Tick", 820f, 820f, 0.06f, 70f, 0.7f);
            genAccent = MakeTone("TickAccent", 1320f, 1320f, 0.07f, 60f, 0.9f);
            genPerfect = MakeTone("Perfect", 1400f, 2100f, 0.12f, 30f, 0.7f);
            genGood = MakeTone("Good", 1000f, 1300f, 0.09f, 40f, 0.6f);
            genMiss = MakeTone("Miss", 260f, 180f, 0.12f, 30f, 0.5f);
            genPop = MakeTone("Pop", 380f, 1100f, 0.14f, 25f, 0.9f); // "뽁!" (음이 위로 튀는 소리)
        }

        private AudioSource NewSource()
        {
            AudioSource s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            s.ignoreListenerPause = true;
            return s;
        }

        // 짧은 효과음: fromHz → toHz로 음이 미끄러지는 사인파 + 빠르게 줄어드는 음량
        private static AudioClip MakeTone(string name, float fromHz, float toHz, float length, float decay, float gain)
        {
            int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;
            int count = Mathf.CeilToInt(rate * length);
            var data = new float[count];
            float phase = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)rate;
                float f = Mathf.Lerp(fromHz, toHz, t / length);
                phase += 2f * Mathf.PI * f / rate;
                float env = Mathf.Exp(-t * decay) * Mathf.Clamp01(t * 2000f);
                data[i] = Mathf.Sin(phase) * env * gain;
            }
            AudioClip clip = AudioClip.Create(name, count, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}