using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FarmingSystem.Core;
using FarmingSystem.Building;
using FarmingSystem.Economy;
using FarmingSystem.Save;

namespace FarmingSystem.NPC
{
    // 주민 관리 (모동숲 방식)
    //
    // 1) 주민: 집이 있어야 섬에 와서 지냄
    //    - 상점에서 주민 집 키트를 사서 지음 → 공사가 끝나면(건물마다 공사 기간 다름) 아직 안 온 주민 중 무작위 1명이 이사 옴
    //    - 이사 온 주민은 집 앞에 나타나서 원래 설정대로 돌아다님 (자유/구역)
    //    - 주민이 다 왔으면 새 집은 빈 집으로 남음
    // 2) 손님(물고기 NPC 등): 집 없이 정해진 때 스스로 찾아옴 (상인처럼)
    //    - Min Day 이후 + 돈이 Min Bells 이상이면 찾아옴, 돈이 모자라도 Max Day가 되면 찾아옴
    //    - 한 번 오면 계속 섬에 있음 (손님이 놓인 자리 = 낚시터 등 원래 씬에 놓아 둔 자리)
    //    - Ship을 넣으면 처음 올 때 그 우주선이 하늘에서 내려와 착륙하고, 손님이 우주선 앞에 나타남. 우주선은 그 자리에 계속 남아 손님의 집이 됨
    //      (우주선은 씬에서 서 있을 자리에 미리 놓아 두기 - 오기 전에는 숨겨짐)
    // 4) 밤에는 집으로 (모동숲처럼): Sleep Hour가 되면 주민은 자기 집, 손님은 자기 우주선 문 앞까지 걸어가서 들어가고 Wake Hour에 문 앞으로 나옴
    //    - 한꺼번에 사라지지 않게 NPC마다 0~Jitter Minutes 분씩 늦게 자고 늦게 일어남
    //    - 길이 막혀 못 가거나 Go Home Seconds가 지나면 그 자리에서 들어감. 상인은 여기 해당 없음 (방문일에만 오므로)
    // 5) 주민 집은 키트 하나로 통일: 어떤 주민이 이사 오든 같은 '주민 집 키트'로 짓고,
    //    이사 오는 순간 그 주민의 집 모델(Resident Houses에서 정한 것)로 바뀜. 정하지 않은 주민은 건물 데이터의 기본 모델
    // 3) 한 집에 한 명, 한 번 이사 온 주민은 그 집에 계속 삶 (교체 없음). 누가 어느 집에 사는지·온 손님은 저장 칸에 저장됨
    //
    // 준비: 씬에 이 컴포넌트를 하나 두고
    //  - House Buildings: 주민 집으로 칠 건물(BuildingData) 넣기
    //  - Residents: 이사 올 주민 NPC들 (씬에 놓아 둔 NPC 오브젝트). 비우면 손님을 뺀 씬의 NPC 전부
    //  - Visitors: 물고기 NPC 같은 손님과 오는 조건
    //  게임이 시작되면 주민·손님은 전부 숨겨졌다가 조건이 되면 나타남
    public class ResidentManager : MonoBehaviour
    {
        public static ResidentManager Instance { get; private set; }

        [System.Serializable]
        public class Visitor
        {
            public NPCDialogue npc;
            [Tooltip("이 날 이후부터 올 수 있음")]
            public int minDay = 5;
            [Tooltip("돈이 이만큼 있으면 Min Day에 바로 옴")]
            public int minBells = 1000;
            [Tooltip("돈이 모자라도 이 날이 되면 옴")]
            public int maxDay = 6;
            [Tooltip("왔을 때 화면 위에 뜨는 알림 (비우면 알림 없음)")]
            public string arriveMessage = "섬에 새로운 손님이 찾아왔어요!";

            [Header("타고 오는 우주선 (비우면 우주선 없이 그냥 나타남)")]
            [Tooltip("씬에서 서 있을 자리에 놓아 둔 우주선 오브젝트. 처음 올 때 하늘에서 내려오고 그 뒤로 계속 남음 (손님의 집)")]
            public GameObject ship;
            [Tooltip("우주선 문 앞 자리 (손님이 내리고, 밤에 들어가고, 아침에 나오는 곳). 비우면 우주선 앞쪽(파란 화살표 Z) Door Distance 만큼")]
            public Transform shipDoor;
            [Tooltip("Ship Door를 비웠을 때: 우주선 가운데에서 앞쪽으로 몇 m가 문 앞인지")]
            public float doorDistance = 4f;
            [Tooltip("착륙 연출: 이 높이(m)에서, 뒤쪽 이 거리(m)에서 내려옴")]
            public float flyHeight = 30f;
            public float flyDistance = 40f;
            [Tooltip("내려오는 시간 (초)")]
            public float flyTime = 4f;

            [System.NonSerialized] public Vector3 restPos;
            [System.NonSerialized] public Quaternion restRot;
            [System.NonSerialized] public bool restKnown;
        }

        [Header("주민 집")]
        [Tooltip("주민 집으로 칠 건물들 (이 건물이 다 지어지면 주민이 이사 옴)")]
        [SerializeField] private List<BuildingData> houseBuildings = new List<BuildingData>();

        [System.Serializable]
        public class ResidentHouse
        {
            [Tooltip("주민 NPC (씬의 NPC 오브젝트)")]
            public NPCDialogue npc;
            [Tooltip("이 주민이 이사 오면 집이 이 모델로 바뀜")]
            public GameObject housePrefab;
            [Tooltip("집 문이 앞(주민이 나오는 쪽)을 안 보면 90/180/270")]
            public float modelYaw = 0f;
        }

        [Tooltip("주민마다 자기 집 모델 (bird_house, bunny_house …). 비워 두면 모든 주민이 기본 모델")]
        [SerializeField] private List<ResidentHouse> residentHouses = new List<ResidentHouse>();
        [Tooltip("공사가 끝나고 며칠 뒤에 이사 올지 (0 = 공사가 끝난 날 바로)")]
        [SerializeField] private int moveInDelayDays = 0;

        [Header("주민")]
        [Tooltip("이사 올 수 있는 주민들. 비우면 손님을 뺀 씬의 NPC 전부")]
        [SerializeField] private List<NPCDialogue> residents = new List<NPCDialogue>();
        [Tooltip("이사 왔을 때 알림 ({0} = 주민 이름)")]
        [SerializeField] private string moveInMessage = "{0}이(가) 섬으로 이사 왔어요!";

        [Header("손님 (집 없이 스스로 찾아옴)")]
        [SerializeField] private List<Visitor> visitors = new List<Visitor>();

        [Header("밤에는 집으로 (주민 = 자기 집, 손님 = 자기 우주선)")]
        [SerializeField] private bool sleepAtNight = true;
        [Tooltip("이 시각이 되면 집으로 가기 시작 (0~23)")]
        [SerializeField, Range(0, 23)] private int sleepHour = 22;
        [Tooltip("이 시각이 되면 집에서 나옴 (0~23)")]
        [SerializeField, Range(0, 23)] private int wakeHour = 6;
        [Tooltip("NPC마다 이 분(分) 안에서 조금씩 늦게 자고 늦게 일어남 (한꺼번에 사라지지 않게)")]
        [SerializeField] private int jitterMinutes = 30;
        [Tooltip("집까지 걸어가는 최대 시간 (초). 넘으면 그 자리에서 들어감")]
        [SerializeField] private float goHomeSeconds = 45f;

        private readonly Dictionary<PlacedBuilding, NPCDialogue> livesIn = new Dictionary<PlacedBuilding, NPCDialogue>();
        private readonly HashSet<NPCDialogue> arrived = new HashSet<NPCDialogue>();
        private readonly HashSet<NPCDialogue> asleep = new HashSet<NPCDialogue>();   // 집에 들어가 있음 (숨김)
        private readonly HashSet<NPCDialogue> heading = new HashSet<NPCDialogue>();  // 집으로 걸어가는 중
        private readonly HashSet<NPCDialogue> landing = new HashSet<NPCDialogue>();  // 우주선이 내려오는 중 (아직 안 내림)
        private readonly List<Coroutine> landRoutines = new List<Coroutine>();
        private float checkTimer;
        private bool warnedNoResidents;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (residents.Count == 0) AutoFillResidents();
            foreach (Visitor v in visitors) RememberRest(v); // 우주선이 서 있을 자리 (숨기기 전에)
            HideAll();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void AutoFillResidents()
        {
            var visitorNpcs = new HashSet<NPCDialogue>();
            foreach (Visitor v in visitors) if (v.npc != null) visitorNpcs.Add(v.npc);
            foreach (NPCDialogue n in FindObjectsByType<NPCDialogue>(FindObjectsInactive.Include))
                if (!visitorNpcs.Contains(n)) residents.Add(n);
        }

        private void HideAll()
        {
            foreach (NPCDialogue n in residents) if (n != null) n.gameObject.SetActive(false);
            foreach (Coroutine c in landRoutines) if (c != null) StopCoroutine(c);
            landRoutines.Clear();
            foreach (Visitor v in visitors)
            {
                if (v.npc != null) v.npc.gameObject.SetActive(false);
                if (v.ship != null)
                {
                    RememberRest(v);
                    v.ship.transform.SetPositionAndRotation(v.restPos, v.restRot); // 내려오던 중이었으면 제자리로
                    v.ship.SetActive(false);
                }
            }
            livesIn.Clear();
            arrived.Clear();
            asleep.Clear();
            heading.Clear();
            landing.Clear();
        }

        private static void RememberRest(Visitor v)
        {
            if (v.restKnown || v.ship == null) return;
            v.restPos = v.ship.transform.position;
            v.restRot = v.ship.transform.rotation;
            v.restKnown = true;
        }

        // 1초마다 확인 (시작 화면에선 안 함)
        private void Update()
        {
            if (TitleScreen.IsShowing) return;
            if (SaveManager.Instance != null && !SaveManager.Instance.IsPlaying) return;
            checkTimer -= Time.unscaledDeltaTime;
            if (checkTimer > 0f) return;
            checkTimer = 1f;
            CheckHouses(true);
            CheckVisitors(true);
            UpdateSleep(false);
        }

        // ───────────────────────── 밤에는 집으로 ─────────────────────────

        // 지금이 그 NPC가 집에 있을 시간인지 (NPC마다 몇 분씩 늦게)
        private bool IsSleepTime(NPCDialogue npc)
        {
            GameClock clock = GameClock.Instance;
            if (!sleepAtNight || clock == null || sleepHour == wakeHour) return false;
            int offset = jitterMinutes > 0 ? (Mathf.Abs(npc.name.GetHashCode()) % (jitterMinutes + 1)) : 0;
            int now = clock.CurrentHour * 60 + clock.CurrentMinute;
            int start = (sleepHour * 60 + offset) % 1440, end = (wakeHour * 60 + offset) % 1440;
            return start < end ? now >= start && now < end : now >= start || now < end;
        }

        // 섬에 와 있는 NPC마다: 밤이면 집으로, 아침이면 문 앞으로. instant = 걷는 연출 없이 바로 (불러오기 직후)
        private void UpdateSleep(bool instant)
        {
            foreach (var kv in livesIn)
                if (kv.Key != null && kv.Value != null) ApplySleep(kv.Value, DoorPosition(kv.Key), kv.Key.transform.position, instant);
            foreach (Visitor v in visitors)
            {
                if (v.npc == null || !arrived.Contains(v.npc) || landing.Contains(v.npc)) continue;
                Vector3 door = VisitorDoor(v);
                ApplySleep(v.npc, door, v.ship != null ? v.restPos : door - v.npc.transform.forward, instant);
            }
        }

        private void ApplySleep(NPCDialogue npc, Vector3 door, Vector3 homeCenter, bool instant)
        {
            bool night = IsSleepTime(npc);
            if (night)
            {
                if (asleep.Contains(npc) || heading.Contains(npc)) return;
                if (instant || !npc.gameObject.activeInHierarchy)
                {
                    asleep.Add(npc);
                    npc.gameObject.SetActive(false);
                    return;
                }
                if (npc.IsTalking) return; // 대화가 끝난 뒤에
                heading.Add(npc);
                npc.GoHome(door, goHomeSeconds, () =>
                {
                    heading.Remove(npc);
                    asleep.Add(npc);
                    npc.gameObject.SetActive(false);
                });
                return;
            }

            if (heading.Remove(npc)) npc.CancelGoHome(); // 가는 도중에 아침이 됨 (잠자기로 시간이 넘어감)
            if (asleep.Remove(npc))
            {
                npc.gameObject.SetActive(true);
                npc.PlaceAt(door, Quaternion.LookRotation(Flat(door - homeCenter, Vector3.forward), Vector3.up), !instant);
            }
        }

        // 손님 집 문 앞: 우주선이 있으면 그 문 앞, 없으면 처음 놓인 자리
        private static Vector3 VisitorDoor(Visitor v)
        {
            if (v.shipDoor != null) return v.shipDoor.position;
            if (v.ship != null) return v.restPos + v.restRot * Vector3.forward * v.doorDistance;
            return v.npc.HomePosition;
        }

        // ───────────────────────── 주민 ─────────────────────────

        private void CheckHouses(bool notify)
        {
            BuildingSystem bs = BuildingSystem.Instance;
            GameClock clock = GameClock.Instance;
            if (bs == null || clock == null) return;
            foreach (PlacedBuilding b in bs.Buildings)
            {
                if (b == null || b.Data == null || !houseBuildings.Contains(b.Data)) continue;
                if (b.State != PlacedBuilding.BuildState.Complete || livesIn.ContainsKey(b)) continue;
                if (clock.CurrentDay < b.CompleteDay + moveInDelayDays) continue;

                NPCDialogue who = PickNewResident();
                if (who == null)
                {
                    if (!warnedNoResidents) Debug.Log("[주민] 새 집이 지어졌지만 이사 올 주민이 더 없어요 (빈 집으로 남음)");
                    warnedNoResidents = true;
                    return;
                }
                MoveIn(who, b, notify);
            }
        }

        private NPCDialogue PickNewResident()
        {
            var free = new List<NPCDialogue>();
            foreach (NPCDialogue n in residents)
                if (n != null && !livesIn.ContainsValue(n)) free.Add(n);
            return free.Count > 0 ? free[Random.Range(0, free.Count)] : null;
        }

        private void MoveIn(NPCDialogue npc, PlacedBuilding house, bool notify)
        {
            livesIn[house] = npc;
            ResidentHouse look = residentHouses.Find(r => r != null && r.npc == npc && r.housePrefab != null);
            if (look != null) house.SetModel(look.housePrefab, look.modelYaw); // 그 주민의 집 모델로
            Vector3 door = DoorPosition(house);
            Quaternion face = Quaternion.LookRotation(Flat(door - house.transform.position, Vector3.forward), Vector3.up);
            npc.gameObject.SetActive(true);
            npc.Relocate(door, face);
            if (notify)
            {
                Debug.Log($"[주민] {npc.name}이(가) {house.Data.displayName}에 이사 옴");
                if (!string.IsNullOrEmpty(moveInMessage)) IslandNotice.Show(string.Format(moveInMessage, DisplayName(npc)), this);
                if (SaveManager.Instance != null) SaveManager.Instance.Save(); // 이사 오자마자 저장 (그 집 주인이 바로 확정)
            }
        }

        // 집 앞 (건물이 바라보는 쪽으로 반 칸 + 한 칸)
        private static Vector3 DoorPosition(PlacedBuilding house)
        {
            BuildingSystem bs = BuildingSystem.Instance;
            float cell = bs != null ? bs.CellSize : 2f;
            Vector2Int fp = BuildingSystem.RotatedFootprint(house.Data, house.Rotation);
            Vector3 fwd = Quaternion.Euler(0f, house.Rotation * 90f, 0f) * Vector3.forward;
            float depth = (house.Rotation % 2 == 0 ? fp.y : fp.x) * cell;
            return house.transform.position + fwd * (depth * 0.5f + cell);
        }

        // ───────────────────────── 손님 ─────────────────────────

        private void CheckVisitors(bool notify)
        {
            GameClock clock = GameClock.Instance;
            if (clock == null) return;
            int bells = CurrencyManager.Instance != null ? CurrencyManager.Instance.CurrentBells : 0;
            foreach (Visitor v in visitors)
            {
                if (v.npc == null || arrived.Contains(v.npc)) continue;
                bool due = clock.CurrentDay >= v.maxDay || (clock.CurrentDay >= v.minDay && bells >= v.minBells);
                if (!due) continue;
                if (notify && sleepAtNight && IsNightForArrival(clock)) continue; // 밤에는 안 옴 → 다음 날 낮에
                Arrive(v, notify);
            }
        }

        // 손님은 아침에 다들 일어난 뒤 ~ 잘 시간 2시간 전 사이에만 처음 찾아옴
        private bool IsNightForArrival(GameClock clock)
        {
            int now = clock.CurrentHour * 60 + clock.CurrentMinute;
            int from = (wakeHour * 60 + jitterMinutes + 30) % 1440, to = (sleepHour * 60 - 120 + 1440) % 1440;
            bool day = from < to ? now >= from && now < to : now >= from || now < to;
            return !day;
        }

        private void Arrive(Visitor v, bool notify)
        {
            arrived.Add(v.npc);
            if (notify)
            {
                Debug.Log($"[손님] {v.npc.name} 도착");
                if (!string.IsNullOrEmpty(v.arriveMessage)) IslandNotice.Show(v.arriveMessage, this);
            }
            if (v.ship == null) { v.npc.gameObject.SetActive(true); return; }

            RememberRest(v);
            if (notify) { landRoutines.Add(StartCoroutine(Land(v))); return; } // 처음 올 때: 우주선이 내려오고 → 손님이 내림

            // 불러오기: 우주선은 이미 서 있고 손님도 섬에 있음
            v.ship.transform.SetPositionAndRotation(v.restPos, v.restRot);
            v.ship.SetActive(true);
            v.npc.gameObject.SetActive(true);
        }

        // 우주선이 하늘 뒤쪽에서 내려와 제자리에 앉고, 손님이 문 앞에 나타남
        private IEnumerator Land(Visitor v)
        {
            landing.Add(v.npc);
            Vector3 fwd = v.restRot * Vector3.forward;
            Vector3 from = v.restPos + Vector3.up * v.flyHeight - fwd * v.flyDistance;
            v.ship.transform.SetPositionAndRotation(from, v.restRot);
            v.ship.SetActive(true);
            float time = Mathf.Max(0.1f, v.flyTime);
            for (float t = 0f; t < time; t += Time.deltaTime)
            {
                float k = 1f - Mathf.Pow(1f - t / time, 3f); // 처음엔 빠르게, 땅 가까이선 천천히
                v.ship.transform.position = Vector3.Lerp(from, v.restPos, k);
                yield return null;
            }
            v.ship.transform.position = v.restPos;
            yield return new WaitForSeconds(0.8f);

            Vector3 door = VisitorDoor(v);
            v.npc.gameObject.SetActive(true);
            v.npc.PlaceAt(door, Quaternion.LookRotation(Flat(door - v.restPos, Vector3.forward), Vector3.up), true);
            landing.Remove(v.npc);
            if (SaveManager.Instance != null) SaveManager.Instance.Save();
        }

        // 테스트: 게임 실행 중 Inspector에서 ResidentManager 오른쪽 위 ⋮ 메뉴 → 이 항목
        //  조건(날짜·돈·시간)과 상관없이 손님이 지금 바로 옴. 이미 와 있던 손님도 우주선 착륙부터 다시 보여 줌
        [ContextMenu("테스트: 손님 지금 오게 하기 (착륙 연출)")]
        private void DebugArriveNow()
        {
            if (!Application.isPlaying) { Debug.Log("[손님] 게임을 실행한 상태에서 눌러 주세요"); return; }
            foreach (Visitor v in visitors)
            {
                if (v.npc == null || landing.Contains(v.npc)) continue;
                asleep.Remove(v.npc);
                if (heading.Remove(v.npc)) v.npc.CancelGoHome();
                v.npc.gameObject.SetActive(false);
                arrived.Remove(v.npc);
                Arrive(v, true);
            }
        }

        // ───────────────────────── 저장·이어하기·새 게임·시작 화면 ─────────────────────────

        public void Capture(SaveData d)
        {
            d.residents.Clear();
            foreach (var kv in livesIn)
            {
                if (kv.Key == null || kv.Value == null || kv.Key.Cells.Count == 0) continue;
                Vector2Int c = kv.Key.Cells[0];
                d.residents.Add(new ResidentSave { npc = kv.Value.name, houseX = c.x, houseY = c.y });
            }
            d.visitorsArrived.Clear();
            foreach (NPCDialogue n in arrived) if (n != null) d.visitorsArrived.Add(n.name);
        }

        /// <summary>이어하기 직후 (집이 다 놓인 뒤): 저장된 그대로 각 집에 같은 주민, 온 손님 (알림 없이)</summary>
        public void OnGameLoaded(SaveData d)
        {
            HideAll();
            warnedNoResidents = false;
            BuildingSystem bs = BuildingSystem.Instance;
            foreach (ResidentSave r in d.residents)
            {
                NPCDialogue npc = residents.Find(n => n != null && n.name == r.npc);
                PlacedBuilding house = null;
                if (bs != null)
                    foreach (PlacedBuilding b in bs.Buildings)
                        if (b != null && b.Cells.Count > 0 && b.Cells[0] == new Vector2Int(r.houseX, r.houseY)) { house = b; break; }
                if (npc == null || house == null) { Debug.LogWarning($"[주민] 저장된 주민 '{r.npc}' 또는 그 집을 못 찾았어요"); continue; }
                if (livesIn.ContainsKey(house) || livesIn.ContainsValue(npc)) continue; // 한 집 한 명
                MoveIn(npc, house, false);
            }
            foreach (string name in d.visitorsArrived)
            {
                Visitor v = visitors.Find(x => x.npc != null && x.npc.name == name);
                if (v != null) Arrive(v, false);
            }
            UpdateSleep(true); // 밤에 저장한 게임이면 다들 집에 들어가 있는 상태로
            // 저장 뒤에 완성된 집이 있으면 Update에서 평소처럼 새 주민이 이사 옴 (알림과 함께)
        }

        /// <summary>새 게임: 주민·손님 모두 없는 상태로</summary>
        public void ResetForNewGame()
        {
            HideAll();
            warnedNoResidents = false;
        }

        /// <summary>시작 화면: 가장 최근 저장에 사는 주민·온 손님을 미리 보여 줌 → 시작 화면 카메라가 그중 1명을 따라다님</summary>
        public void PreviewLatestSave()
        {
            SaveData d = SaveManager.ReadLatest();
            if (d == null) return;
            foreach (ResidentSave r in d.residents)
            {
                NPCDialogue npc = residents.Find(n => n != null && n.name == r.npc);
                if (npc != null) npc.gameObject.SetActive(true);
            }
            foreach (string name in d.visitorsArrived)
            {
                Visitor v = visitors.Find(x => x.npc != null && x.npc.name == name);
                if (v == null) continue;
                v.npc.gameObject.SetActive(true);
                if (v.ship != null) v.ship.SetActive(true);
            }
        }

        // ───────────────────────── 기타 ─────────────────────────

        private static string DisplayName(NPCDialogue npc)
        {
            string n = npc.name;
            int cut = n.IndexOfAny(new[] { '_', '(' });
            return cut > 0 ? n.Substring(0, cut).Trim() : n;
        }

        private static Vector3 Flat(Vector3 v, Vector3 fallback)
        {
            v.y = 0f;
            return v.sqrMagnitude < 0.0001f ? fallback : v.normalized;
        }
    }

    /// <summary>화면 위쪽에 잠깐 뜨는 알림 ("주민이 이사 왔어요!")</summary>
    public static class IslandNotice
    {
        private static CanvasGroup group;
        private static TextMeshProUGUI label;
        private static MonoBehaviour runner;
        private static Coroutine routine;

        /// <summary>host = 알림 애니메이션을 돌릴 오브젝트 (씬에 있는 아무 컴포넌트)</summary>
        public static void Show(string text, MonoBehaviour host)
        {
            if (host == null) return;
            if (group == null) Build();
            if (routine != null && runner != null) runner.StopCoroutine(routine);
            runner = host;
            label.text = text;
            routine = runner.StartCoroutine(Play());
        }

        private static IEnumerator Play()
        {
            for (float t = 0f; t < 0.3f; t += Time.unscaledDeltaTime) { group.alpha = t / 0.3f; yield return null; }
            group.alpha = 1f;
            for (float t = 0f; t < 3f; t += Time.unscaledDeltaTime) yield return null;
            for (float t = 0f; t < 0.6f; t += Time.unscaledDeltaTime) { group.alpha = 1f - t / 0.6f; yield return null; }
            group.alpha = 0f;
            routine = null;
        }

        private static void Build()
        {
            var canvasGo = new GameObject("IslandNoticeCanvas", typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 850;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var box = new GameObject("Notice", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            box.transform.SetParent(canvasGo.transform, false);
            var rt = (RectTransform)box.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -150f);
            rt.sizeDelta = new Vector2(760f, 76f);
            box.GetComponent<Image>().color = new Color(0.08f, 0.1f, 0.18f, 0.8f);
            group = box.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(box.transform, false);
            var trt = (RectTransform)textGo.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            label = textGo.GetComponent<TextMeshProUGUI>();
            TMP_FontAsset font = UIFont.FindKorean();
            if (font != null) label.font = font;
            label.fontSize = 32f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
        }
    }
}