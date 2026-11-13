using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using FarmingSystem.Core;
using FarmingSystem.Farming;
using FarmingSystem.Player;
using FarmingSystem.UI.Shop;
using FarmingSystem.Dialogue;

namespace FarmingSystem.NPC
{
    // 말 걸 수 있는 NPC (PV 컷 13 낚시 NPC 등)
    // - 플레이어가 가까이서 NPC를 바라보고 E → 상점과 같은 모양의 대화창으로 대사를 차례로 보여 줌
    //   (E / Enter / Space = 다음 대사, 마지막 대사엔 선택지를 붙일 수 있음)
    // - 대화하는 동안 카메라가 플레이어와 NPC를 옆에서 함께 잡아 줌 (끌 수 있음)
    // - 모델이 없으면 임시 모습(캡슐 몸 + 밀짚모자 + 낚싯대)을 자동으로 만들어 줌 → 모델은 "NPC 모델 적용" 도구로 넣기
    // - 처음 놓은 자리 둘레(Wander Radius)를 천천히 걸어 다님. 같은 높이의 땅만 걷고 물·절벽·장애물 앞에선 멈춰서 다른 곳으로
    //   Roam Mode = Free 이면 구역 없이 섬 땅(밭 칸) 위를 자유롭게 (물·나무·건물·급한 턱은 피함)
    //   플레이어가 가까이 오면 멈춰서 바라봄 (말 걸기 쉽게). 모델에 Animator가 있으면 "Speed"(걷기 속도), "IsTalking"(대화 중) 값을 넘김
    // 붙이는 곳: NPC 오브젝트 (물가에 빈 오브젝트를 놓고 이 스크립트만 붙여도 됨)
    public class NPCDialogue : MonoBehaviour
    {
        [Header("대사 모음 (넣으면 아래 Lines 대신 상황별 대사·퀘스트)")]
        [Tooltip("Dialogue Set 에셋. 처음 만남·시간·날씨·퀘스트 상태에 따라 다른 말을 함. 비우면 아래 Lines를 차례로")]
        [SerializeField] private DialogueSet dialogue;

        [Header("NPC")]
        [SerializeField] private string npcName = "낚시꾼";
        [Tooltip("차례로 나오는 대사")]
        [SerializeField, TextArea(2, 3)]
        private string[] lines =
        {
            "농사가 잘 안 풀린다고?",
            "낚시는 어때? 바다엔 태풍에도 끄떡없는 물고기가 잔뜩 있거든!",
        };
        [Tooltip("마지막 대사에 붙는 선택지 (비우면 선택지 없이 끝)")]
        [SerializeField] private string[] finalChoices = { "좋아요!", "다음에 할게요" };
        [Tooltip("선택지마다 NPC가 하는 대답 (선택지 순서대로, 비우면 대답 없이 끝)")]
        [SerializeField, TextArea(1, 2)]
        private string[] choiceReplies =
        {
            "좋아! 낚싯대가 생기면 다시 찾아오라고.",
            "언제든 물가로 오게나. 물고기는 기다려 주니까.",
        };

        [Header("말 걸기")]
        [SerializeField] private float interactRange = 2.5f;
        [Tooltip("카메라가 NPC 쪽을 이 각도 안으로 바라봐야 말을 걸 수 있음")]
        [SerializeField] private float maxLookAngle = 60f;
        [Tooltip("대화할 때 NPC가 플레이어 쪽으로 몸을 돌림")]
        [SerializeField] private bool turnToPlayer = true;

        [Header("대화 카메라 (모동숲처럼 플레이어 등 뒤 위에서 NPC를 내려다봄)")]
        [Tooltip("대화 중 카메라를 대화용 시점으로 바꿈")]
        [SerializeField] private bool twoShotCamera = true;
        [Tooltip("바라보는 점에서 카메라까지 거리 (m). 클수록 멀리서 넓게")]
        [SerializeField] private float camDistance = 5.5f;
        [Tooltip("내려다보는 각도 (도). 0 = 정면, 클수록 위에서 아래로")]
        [SerializeField, Range(0f, 70f)] private float camPitch = 32f;
        [Tooltip("플레이어 바로 뒤에서 옆으로 비켜선 각도 (도). 0이면 플레이어가 NPC를 가릴 수 있음")]
        [SerializeField, Range(0f, 80f)] private float camSideAngle = 30f;
        [Tooltip("바라보는 점 높이 (m, 땅 기준)")]
        [SerializeField] private float camLookHeight = 0.9f;
        [Tooltip("바라보는 점 위치: 0 = 플레이어, 1 = NPC")]
        [SerializeField, Range(0f, 1f)] private float camLookBias = 0.6f;
        [SerializeField] private float shotFov = 35f;

        [Header("돌아다니기")]
        [Tooltip("처음 놓은 자리 둘레를 걸어 다님")]
        [SerializeField] private bool wander = true;
        public enum RoamMode { Area, Free }
        [Tooltip("Area = 처음 자리 둘레(Wander Radius) 안에서만 (낚시꾼처럼) / Free = 구역 없이 섬 전체를 자유롭게")]
        [SerializeField] private RoamMode roamMode = RoamMode.Area;
        [Tooltip("Free일 때: 한 번에 걸어갈 거리 (m, 최소~최대)")]
        [SerializeField] private Vector2 freeStep = new Vector2(4f, 12f);
        [Tooltip("Free일 때: 걸으면서 오를 수 있는 높이 차 (m). 블록 한 칸 턱보다 작으면 턱은 못 넘음")]
        [SerializeField] private float stepHeight = 0.6f;
        [Tooltip("처음 자리에서 이 거리(m) 안에서만 돌아다님 (Scene 창에서 노란 원으로 보임)")]
        [SerializeField] private float wanderRadius = 5f;
        [SerializeField] private float walkSpeed = 1.1f;
        [Tooltip("한 곳에 도착하면 쉬는 시간 (초, 최소~최대 사이 무작위)")]
        [SerializeField] private Vector2 idleTime = new Vector2(2f, 5f);
        [Tooltip("처음 자리 땅 높이와 이만큼(m) 넘게 다르면 못 가는 곳 (물·절벽)")]
        [SerializeField] private float heightTolerance = 0.35f;
        [Tooltip("플레이어가 이 거리 안에 오면 멈춰서 바라봄")]
        [SerializeField] private float stopNearPlayer = 3f;

        [Header("임시 모습 (모델이 없을 때)")]
        [Tooltip("켜면 자식에 Renderer가 없을 때 임시 모습을 만듦")]
        [SerializeField] private bool makePlaceholder = true;
        [SerializeField] private Color bodyColor = new Color(0.35f, 0.55f, 0.75f);
        [SerializeField] private Color hatColor = new Color(0.93f, 0.8f, 0.45f);

        public bool CanTalk { get; private set; }
        public bool IsTalking => talking;

        private bool talking;
        private int lineIndex;
        private int startFrame;
        private int endFrame = -10;
        private ShopConversationUI view;
        private Transform player;
        private Quaternion baseRotation;

        // 돌아다니기 상태
        private Vector3 home;
        private float homeY;
        private bool homeReady;
        private Vector3 target;
        private bool hasTarget;
        private float idleTimer;
        private float walkTime;
        private Transform placeholderRoot;
        private Animator npcAnimator;

        private void Start()
        {
            baseRotation = transform.rotation;
            if (makePlaceholder && GetComponentInChildren<Renderer>() == null) BuildPlaceholder();
            npcAnimator = GetComponentInChildren<Animator>();
            home = transform.position;
            homeReady = false; // 처음 자리 높이를 재는 동안은 높이 검사 안 함
            homeY = SpawnGroundY(home, out float gy) ? gy : home.y;
            homeReady = true;
            transform.position = new Vector3(home.x, homeY, home.z);
            home = transform.position;
            idleTimer = Random.Range(idleTime.x, idleTime.y);
        }

        /// <summary>다른 곳으로 옮기기 (주민이 새 집 앞으로 이사 올 때). 그 자리가 새 '처음 자리'가 됨</summary>
        public void Relocate(Vector3 pos, Quaternion rot)
        {
            transform.SetPositionAndRotation(pos, rot);
            baseRotation = rot;
            homeReady = false;
            homeY = SpawnGroundY(pos, out float gy) ? gy : pos.y;
            homeReady = true;
            transform.position = new Vector3(pos.x, homeY, pos.z);
            home = transform.position;
            hasTarget = false;
            idleTimer = Random.Range(idleTime.x, idleTime.y);
        }

        // 처음 자리의 땅 높이: 땅 속에 놓였어도 찾을 수 있게 훨씬 위(30m)에서 아래로
        //  섬 땅(밭 칸)을 먼저 찾고, 없으면 처음 맞는 것 (나무 위 등에 서지 않게)
        private bool SpawnGroundY(Vector3 p, out float y)
        {
            y = p.y;
            RaycastHit[] hits = Physics.RaycastAll(new Vector3(p.x, p.y + 30f, p.z), Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            bool any = false;
            float first = p.y;
            foreach (RaycastHit h in hits)
            {
                if (h.transform.IsChildOf(transform)) continue;
                if (player != null && h.transform.IsChildOf(player)) continue;
                if (h.collider.GetComponentInParent<NPCDialogue>() != null || h.collider.GetComponentInParent<PlayerController>() != null) continue;
                if (h.collider.GetComponentInParent<FarmTile>() != null) { y = h.point.y; return true; }
                if (!any) { any = true; first = h.point.y; }
            }
            if (any) { y = first; return true; }
            return false;
        }

        private void Update()
        {
            if (player == null)
            {
                PlayerController pc = FindAnyObjectByType<PlayerController>();
                if (pc != null) player = pc.transform;
                if (player == null) return;
            }

            if (talking)
            {
                // 대화창이 다른 이유로 닫혔으면 대화 끝
                if (Time.frameCount > startFrame + 1 && (view == null || !view.IsVisible) && !waitingNext) EndTalk();
                waitingNext = false;
                if (turnToPlayer) FacePlayer(Time.deltaTime);
                return;
            }

            CanTalk = CheckCanTalk();
            Keyboard kb = Keyboard.current;
            // 대화를 끝낸 E 키가 바로 다시 대화를 시작하지 않게 잠깐 쉼
            if (CanTalk && kb != null && kb.eKey.wasPressedThisFrame && Time.frameCount > endFrame + 2) { SetWalking(false); StartTalk(); return; }

            if (wander) UpdateWander(Time.deltaTime);
            else if (turnToPlayer) transform.rotation = Quaternion.Slerp(transform.rotation, baseRotation, 1f - Mathf.Exp(-4f * Time.deltaTime));
        }

        private bool CheckCanTalk()
        {
            if (UIInputBlocker.IsBlocked) return false;
            if (dialogue == null && (lines == null || lines.Length == 0)) return false;
            Vector3 to = transform.position - player.position;
            to.y = 0f;
            if (to.magnitude > interactRange) return false;
            Camera cam = Camera.main;
            if (cam == null) return true;
            Vector3 f = cam.transform.forward;
            f.y = 0f;
            return Vector3.Angle(f, to) <= maxLookAngle;
        }

        // ---------- 대화 ----------

        private bool waitingNext; // 다음 대사를 띄우는 프레임엔 창이 잠깐 닫혀 있어서 대화 끝으로 오해하지 않게

        public void StartTalk()
        {
            if (talking) return;
            view = FindAnyObjectByType<ShopConversationUI>(FindObjectsInactive.Include);
            if (view == null)
            {
                // 상점 대화창이 없으면 새로 만듦 (상점과 같은 모양을 코드가 그림)
                view = new GameObject("NPCConversationUI").AddComponent<ShopConversationUI>();
            }
            // 이번에 할 말 고르기: 대사 모음이 있으면 상황에 맞는 묶음, 없으면 Lines
            entry = dialogue != null ? dialogue.Pick(name, lastEntryKey) : null;
            if (entry != null)
            {
                curLines = entry.lines;
                curChoices = new List<string>();
                foreach (DialogueChoice c in entry.choices) if (c != null && !string.IsNullOrEmpty(c.text)) curChoices.Add(c.text);
                lastEntryKey = entry.Key;
                if (QuestManager.Instance != null) QuestManager.Instance.MarkTalk(name, entry.Key, entry.onceOnly);
            }
            else
            {
                if (lines == null || lines.Length == 0) return; // 할 말이 없음
                curLines = lines;
                curChoices = finalChoices != null ? new List<string>(finalChoices) : new List<string>();
                if (QuestManager.Instance != null) QuestManager.Instance.MarkTalk(name, null, false);
            }

            talking = true;
            lineIndex = 0;
            startFrame = Time.frameCount;
            UIInputBlocker.Block(this);
            SetAnimBool("IsTalking", true);

            PlayerController pc = player.GetComponent<PlayerController>();
            if (pc != null) pc.FaceTowards(transform.position);
            if (twoShotCamera) BeginTwoShot();
            ShowLine();
        }

        // 이번 대화 내용
        private DialogueEntry entry;
        private string[] curLines;
        private List<string> curChoices;
        private string lastEntryKey;

        private string Speaker => dialogue != null && !string.IsNullOrEmpty(dialogue.npcName) ? dialogue.npcName : npcName;

        private string Say(string text) =>
            entry != null && QuestManager.Instance != null ? QuestManager.Instance.Format(text, entry.quest) : text;

        private void ShowLine()
        {
            bool last = lineIndex >= curLines.Length - 1;
            List<string> choices = last && curChoices != null && curChoices.Count > 0 ? curChoices : null;
            waitingNext = true;
            view.Show(Speaker, Say(curLines[lineIndex]), choices ?? new List<string>(), OnLineDone, choices != null ? choices.Count - 1 : -1);
        }

        private void OnLineDone(int choice)
        {
            bool last = lineIndex >= curLines.Length - 1;
            if (!last)
            {
                lineIndex++;
                ShowLine();
                return;
            }
            // 마지막 대사: 선택에 따른 행동(퀘스트) + 대답 한 줄 → 끝
            string reply = null;
            if (entry != null)
            {
                DialogueChoice c = ChoiceAt(choice);
                if (c != null)
                {
                    reply = c.reply;
                    QuestManager qm = QuestManager.Instance;
                    if (qm != null && entry.quest != null)
                    {
                        if (c.action == ChoiceAction.StartQuest) qm.StartQuest(entry.quest);
                        else if (c.action == ChoiceAction.TurnInQuest && !qm.TryTurnIn(entry.quest))
                            reply = "어라, 아직 다 모으지 못한 것 같아. ({have}/{amount})";
                    }
                }
            }
            else if (choice >= 0 && choiceReplies != null && choice < choiceReplies.Length) reply = choiceReplies[choice];

            if (!string.IsNullOrEmpty(reply))
            {
                lineIndex = int.MaxValue; // 대답 다음엔 끝
                waitingNext = true;
                view.Show(Speaker, Say(reply), new List<string>(), _ => EndTalk());
                return;
            }
            EndTalk();
        }

        // 화면 선택지 번호 → 대사 모음의 선택지 (글자가 빈 선택지는 화면에 안 나와서 건너뜀)
        private DialogueChoice ChoiceAt(int index)
        {
            if (index < 0 || entry == null) return null;
            int shown = -1;
            foreach (DialogueChoice c in entry.choices)
            {
                if (c == null || string.IsNullOrEmpty(c.text)) continue;
                if (++shown == index) return c;
            }
            return null;
        }

        private void EndTalk()
        {
            if (!talking) return;
            talking = false;
            endFrame = Time.frameCount;
            if (view != null && view.IsVisible) view.Hide();
            UIInputBlocker.Unblock(this);
            SetAnimBool("IsTalking", false);
            if (twoShotCamera && ThirdPersonCameraRig.Instance != null) ThirdPersonCameraRig.Instance.EndShot();
        }

        private void OnDisable()
        {
            if (talking) EndTalk();
        }

        // 모동숲 대화 시점: 플레이어 등 뒤 + 살짝 옆 + 위에서, 두 사람 사이(NPC 쪽)를 내려다봄 → 키가 달라도 둘 다 잘 보임
        private void BeginTwoShot()
        {
            ThirdPersonCameraRig rig = ThirdPersonCameraRig.Instance;
            Camera cam = Camera.main;
            if (rig == null || cam == null) return;
            Vector3 a = player.position, b = transform.position;
            Vector3 dir = b - a;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = -transform.forward;
            dir.Normalize();
            Vector3 look = Vector3.Lerp(a, b, camLookBias);
            look.y = Mathf.Min(a.y, b.y) + camLookHeight;

            Vector3 side = Vector3.Cross(Vector3.up, dir);
            // 지금 카메라와 가까운 쪽으로 비켜섬 (화면이 덜 휙 돌게)
            if (Vector3.Dot(side, cam.transform.position - a) < 0f) side = -side;
            float yaw = camSideAngle * Mathf.Deg2Rad, pitch = camPitch * Mathf.Deg2Rad;
            Vector3 back = (-dir * Mathf.Cos(yaw) + side * Mathf.Sin(yaw)) * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch);
            Vector3 pos = look + back * camDistance;
            rig.BeginShot(pos, Quaternion.LookRotation(look - pos, Vector3.up), shotFov);
        }

        private void FacePlayer(float dt)
        {
            Vector3 to = player.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.01f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), 1f - Mathf.Exp(-6f * dt));
        }

        // ---------- 돌아다니기 ----------

        private void UpdateWander(float dt)
        {
            // 플레이어가 가까이 오면 멈춰서 바라봄
            Vector3 toPlayer = player.position - transform.position;
            toPlayer.y = 0f;
            if (toPlayer.magnitude < stopNearPlayer)
            {
                SetWalking(false);
                if (turnToPlayer) FacePlayer(dt);
                return;
            }

            if (idleTimer > 0f)
            {
                idleTimer -= dt;
                SetWalking(false);
                return;
            }

            if (!hasTarget && !PickTarget())
            {
                idleTimer = 1f;
                return;
            }

            Vector3 pos = transform.position;
            Vector3 dir = target - pos;
            dir.y = 0f;
            float dist = dir.magnitude;
            if (dist < 0.15f) { Arrive(); return; }
            dir /= dist;

            // 앞쪽이 물·절벽이거나 장애물이 있으면 멈추고 다른 곳으로
            if (!TryGroundY(pos + dir * 0.5f, out _) || Blocked(pos, dir)) { Arrive(); return; }

            Vector3 next = pos + dir * Mathf.Min(dist, walkSpeed * dt);
            if (TryGroundY(next, out float y)) next.y = Mathf.Lerp(pos.y, y, 1f - Mathf.Exp(-15f * dt));
            transform.position = next;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 1f - Mathf.Exp(-6f * dt));
            SetWalking(true);
        }

        private void Arrive()
        {
            hasTarget = false;
            idleTimer = Random.Range(idleTime.x, idleTime.y);
            SetWalking(false);
        }

        private bool PickTarget()
        {
            if (roamMode == RoamMode.Free)
            {
                // 지금 자리에서 아무 방향으로 몇 m 떨어진 땅 (너무 높거나 낮은 곳은 빼고)
                Vector3 pos = transform.position;
                for (int i = 0; i < 16; i++)
                {
                    Vector2 r = Random.insideUnitCircle.normalized * Random.Range(freeStep.x, freeStep.y);
                    Vector3 p = pos + new Vector3(r.x, 0f, r.y);
                    if (!RawGround(p, out float y, out bool land) || !land || Mathf.Abs(y - pos.y) > 4f) continue;
                    target = new Vector3(p.x, y, p.z);
                    hasTarget = true;
                    return true;
                }
                return false;
            }
            for (int i = 0; i < 12; i++)
            {
                Vector2 r = Random.insideUnitCircle * wanderRadius;
                Vector3 p = home + new Vector3(r.x, 0f, r.y);
                if ((p - transform.position).sqrMagnitude < 1f) continue; // 너무 가까운 곳은 건너뜀
                if (!TryGroundY(p, out float y)) continue;
                target = new Vector3(p.x, y, p.z);
                hasTarget = true;
                return true;
            }
            return false;
        }

        // 걸을 수 있는 땅인지 + 높이
        //  Area: 처음 자리와 높이가 비슷한 땅만 (물·절벽이면 false)
        //  Free: 위에서 처음 맞는 게 밭 칸(섬 땅)이고, 지금 발 높이와 차이가 stepHeight 이하
        private bool TryGroundY(Vector3 p, out float y)
        {
            if (!RawGround(p, out y, out bool land)) return false;
            if (roamMode == RoamMode.Free) return land && (!homeReady || Mathf.Abs(y - transform.position.y) <= stepHeight);
            return !homeReady || Mathf.Abs(y - homeY) <= heightTolerance;
        }

        // 그 자리 위에서 처음 맞는 것 (NPC 자신·플레이어 제외). land = 밭 칸(섬 땅)인지
        private bool RawGround(Vector3 p, out float y, out bool land)
        {
            y = 0f;
            land = false;
            // Area: 예전과 같음 (3m 위에서 8m) / Free: 높이가 다른 곳도 재도록 더 위에서 길게
            bool free = roamMode == RoamMode.Free;
            float from = free ? Mathf.Max(p.y, transform.position.y) + 6f : p.y + 3f;
            RaycastHit[] hits = Physics.RaycastAll(new Vector3(p.x, from, p.z), Vector3.down, free ? 16f : 8f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit h in hits)
            {
                if (h.transform.IsChildOf(transform)) continue;
                if (player != null && h.transform.IsChildOf(player)) continue;
                y = h.point.y;
                land = h.collider.GetComponentInParent<FarmTile>() != null;
                return true;
            }
            return false;
        }

        // 몸 높이에서 앞을 막는 것 (나무·바위·높은 땅)
        private bool Blocked(Vector3 pos, Vector3 dir)
        {
            RaycastHit[] hits = Physics.RaycastAll(pos + Vector3.up * 0.6f, dir, 0.6f, ~0, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit h in hits)
            {
                if (h.transform.IsChildOf(transform)) continue;
                if (player != null && h.transform.IsChildOf(player)) continue;
                return true;
            }
            return false;
        }

        // 걷는 중 표시: 모델 Animator의 Speed 값 / 임시 모습은 통통 튀며 걷기
        private void SetWalking(bool walking)
        {
            if (npcAnimator != null && npcAnimator.runtimeAnimatorController != null)
                foreach (AnimatorControllerParameter prm in npcAnimator.parameters)
                    if (prm.name == "Speed" && prm.type == AnimatorControllerParameterType.Float)
                        npcAnimator.SetFloat("Speed", walking ? walkSpeed : 0f);

            if (placeholderRoot != null)
            {
                walkTime = walking ? walkTime + Time.deltaTime : 0f;
                float bob = walking ? Mathf.Abs(Mathf.Sin(walkTime * 9f)) * 0.07f : 0f;
                Vector3 lp = placeholderRoot.localPosition;
                lp.y = Mathf.Lerp(lp.y, bob, walking ? 1f : 1f - Mathf.Exp(-12f * Time.deltaTime));
                placeholderRoot.localPosition = lp;
            }
        }

        // 모델 Animator에 그 이름의 bool 값이 있을 때만 넘김 (말하기 모션이 없는 모델도 문제없게)
        private void SetAnimBool(string param, bool value)
        {
            if (npcAnimator == null || npcAnimator.runtimeAnimatorController == null) return;
            foreach (AnimatorControllerParameter prm in npcAnimator.parameters)
                if (prm.name == param && prm.type == AnimatorControllerParameterType.Bool)
                    npcAnimator.SetBool(param, value);
        }

        private void OnDrawGizmosSelected()
        {
            if (!wander || roamMode == RoamMode.Free) return;
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.8f);
            Vector3 c = Application.isPlaying ? home : transform.position;
            const int seg = 48;
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                Gizmos.DrawLine(c + new Vector3(Mathf.Cos(a0), 0.05f, Mathf.Sin(a0)) * wanderRadius,
                                c + new Vector3(Mathf.Cos(a1), 0.05f, Mathf.Sin(a1)) * wanderRadius);
            }
        }

        // ---------- 임시 모습 ----------

        private void BuildPlaceholder()
        {
            var root = new GameObject("Placeholder").transform;
            root.SetParent(transform, false);
            placeholderRoot = root;

            GameObject body = Part(PrimitiveType.Capsule, root, new Vector3(0f, 0.9f, 0f), new Vector3(0.7f, 0.9f, 0.7f), bodyColor, true);
            body.name = "Body";
            GameObject brim = Part(PrimitiveType.Cylinder, root, new Vector3(0f, 1.82f, 0f), new Vector3(1.0f, 0.03f, 1.0f), hatColor, false);
            brim.name = "HatBrim";
            GameObject top = Part(PrimitiveType.Cylinder, root, new Vector3(0f, 1.92f, 0f), new Vector3(0.5f, 0.12f, 0.5f), hatColor, false);
            top.name = "HatTop";
            // 낚싯대: 앞쪽으로 비스듬히
            GameObject rod = Part(PrimitiveType.Cylinder, root, new Vector3(0.3f, 1.3f, 0.7f), new Vector3(0.04f, 0.8f, 0.04f), new Color(0.45f, 0.3f, 0.18f), false);
            rod.name = "FishingRod";
            rod.transform.localRotation = Quaternion.Euler(55f, 0f, 0f);
            // 찌
            GameObject bob = Part(PrimitiveType.Sphere, root, new Vector3(0.3f, 0.2f, 2.0f), Vector3.one * 0.12f, new Color(0.95f, 0.3f, 0.25f), false);
            bob.name = "Bobber";
        }

        private static GameObject Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Color color, bool keepCollider)
        {
            GameObject g = GameObject.CreatePrimitive(type);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localScale = scale;
            if (!keepCollider)
            {
                Collider c = g.GetComponent<Collider>();
                if (c != null) Destroy(c);
            }
            Renderer r = g.GetComponent<Renderer>();
            Shader sh = Shader.Find("FarmingSystem/Curved Lit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh != null)
            {
                var m = new Material(sh);
                m.SetColor("_BaseColor", color);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0f);
                r.sharedMaterial = m;
            }
            return g;
        }
    }
}