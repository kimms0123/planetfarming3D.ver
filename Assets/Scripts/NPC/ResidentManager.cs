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
        }

        [Header("주민 집")]
        [Tooltip("주민 집으로 칠 건물들 (이 건물이 다 지어지면 주민이 이사 옴)")]
        [SerializeField] private List<BuildingData> houseBuildings = new List<BuildingData>();
        [Tooltip("공사가 끝나고 며칠 뒤에 이사 올지 (0 = 공사가 끝난 날 바로)")]
        [SerializeField] private int moveInDelayDays = 0;

        [Header("주민")]
        [Tooltip("이사 올 수 있는 주민들. 비우면 손님을 뺀 씬의 NPC 전부")]
        [SerializeField] private List<NPCDialogue> residents = new List<NPCDialogue>();
        [Tooltip("이사 왔을 때 알림 ({0} = 주민 이름)")]
        [SerializeField] private string moveInMessage = "{0}이(가) 섬으로 이사 왔어요!";

        [Header("손님 (집 없이 스스로 찾아옴)")]
        [SerializeField] private List<Visitor> visitors = new List<Visitor>();

        private readonly Dictionary<PlacedBuilding, NPCDialogue> livesIn = new Dictionary<PlacedBuilding, NPCDialogue>();
        private readonly HashSet<NPCDialogue> arrived = new HashSet<NPCDialogue>();
        private float checkTimer;
        private bool warnedNoResidents;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (residents.Count == 0) AutoFillResidents();
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
            foreach (Visitor v in visitors) if (v.npc != null) v.npc.gameObject.SetActive(false);
            livesIn.Clear();
            arrived.Clear();
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
                Arrive(v, notify);
            }
        }

        private void Arrive(Visitor v, bool notify)
        {
            arrived.Add(v.npc);
            v.npc.gameObject.SetActive(true);
            if (notify)
            {
                Debug.Log($"[손님] {v.npc.name} 도착");
                if (!string.IsNullOrEmpty(v.arriveMessage)) IslandNotice.Show(v.arriveMessage, this);
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
                if (v != null) v.npc.gameObject.SetActive(true);
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