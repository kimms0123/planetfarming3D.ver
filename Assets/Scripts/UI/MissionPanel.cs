using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using FarmingSystem.Core;
using FarmingSystem.Dialogue;
using FarmingSystem.Economy;
using FarmingSystem.Farming;
using FarmingSystem.Fishing;
using FarmingSystem.Inventory;
using FarmingSystem.NPC;
using FarmingSystem.Rhythm;
using FarmingSystem.Save;
using FarmingSystem.Weather;

namespace FarmingSystem.UI
{
    // 튜토리얼 임무창 (화면 오른쪽, 폰 메뉴와 같은 홀로그램 패널)
    // - 게임을 시작하면 자동으로 생김 (씬에 놓을 필요 없음). J = 접기/펼치기
    // - 다른 스크립트는 건드리지 않고, 게임 상태 변화를 지켜보다가 완료 처리
    //   (밭 칸 상태·가방 아이템 수·흥정 성공 횟수·도구 변경·날짜 변경)
    // - 완료하면 글자에 가운데 줄 + 회색 반투명. 진행은 저장 칸마다 따로 기억 (missions_1.json ~), 새 게임이면 처음부터
    // - 아래쪽 "부탁" 칸: NPC에게 받은 퀘스트(진행 중) + 모은 개수. 끝낼 수 있으면 노란색, 끝내면 가운데 줄 후 잠시 뒤 사라짐
    //   (퀘스트 진행은 QuestManager가 저장하므로 여기선 보여 주기만 함)
    public class MissionPanel : MonoBehaviour
    {
        private class Mission
        {
            public string id, text;
            public bool done;
            public TextMeshProUGUI label;
            public Image box, check;
            public float doneTime = -1f;
            public RectTransform row;
        }

        private readonly List<Mission> missions = new List<Mission>
        {
            new Mission { id = "tool_swap",    text = "마우스 휠로 도구 바꿔 보기" },
            new Mission { id = "till",         text = "괭이로 밭 갈기" },
            new Mission { id = "plant",        text = "갈린 밭에 씨앗 심기" },
            new Mission { id = "water",        text = "물뿌리개로 물 주기" },
            new Mission { id = "sleep",        text = "우주선에서 자고 일어나기" },   // 자야 작물이 자람
            new Mission { id = "harvest",      text = "작물 1개 길러서 수확하기" },
            new Mission { id = "harvest_good", text = "수확 리듬게임에서 상급 이상 받기" },
            new Mission { id = "bargain",      text = "상인과 흥정해서 판매하기" },
            new Mission { id = "fish",         text = "낚시로 물고기 잡기" },
        };

        private class QuestRow
        {
            public RectTransform rt;
            public TextMeshProUGUI label;
            public Image box, check;
        }
        private readonly List<QuestRow> questRows = new List<QuestRow>();
        private readonly Dictionary<QuestData, float> questDoneTime = new Dictionary<QuestData, float>(); // 끝낸 퀘스트를 잠시 더 보여 줌
        private readonly HashSet<QuestData> seenActive = new HashSet<QuestData>();
        private List<QuestData> allQuests;
        private string questSignature = "";
        private const float SubH = 38f;
        private const float QuestKeepSeconds = 8f;
        private static readonly Color ReadyColor = new Color(1f, 0.9f, 0.45f, 1f);

        private static readonly Color Holo = new Color(0.35f, 0.85f, 1f, 1f); // 폰 메뉴와 같은 색
        private static readonly Color DoneColor = new Color(0.75f, 0.8f, 0.85f, 0.45f);
        private const float Width = 430f, RowH = 46f, HeaderH = 64f;
        private const float WarmUp = 1.5f; // 칸을 불러온 직후 바뀌는 값은 무시

        // 상태
        private int activeSlot = -1;
        private float readyTime;
        private bool expanded = true;
        private float allDoneTime = -1f;

        // 지켜보는 값
        private FarmTile[] tiles;
        private int lastTilled, lastCrops, lastWatered, lastHarvest, lastGood, lastFish, lastBargain;
        private float pollTimer;
        private InventoryManager watchedInv;
        private GameClock watchedClock;

        // UI
        private Canvas canvas;
        private CanvasGroup group;
        private RectTransform panel, list;
        private TextMeshProUGUI header, hint, tutorialTitle, questTitle;
        private RawImage scan;
        private Sprite roundedSprite, ringSprite;
        private TMP_FontAsset uiFont;
        private float shownAlpha;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindAnyObjectByType<MissionPanel>() != null) return;
            var go = new GameObject("[MissionPanel]");
            DontDestroyOnLoad(go);
            go.AddComponent<MissionPanel>();
        }

        private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Unwatch();
        }

        // 시작 화면으로 돌아가면 씬을 다시 불러옴 → 처음 상태로
        private void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            Unwatch();
            activeSlot = -1;
            tiles = null;
            if (canvas != null) Destroy(canvas.gameObject);
            canvas = null;
        }

        private void Update()
        {
            SaveManager save = SaveManager.Instance;
            int slot = save != null ? save.CurrentSlot : -1;
            if (slot != activeSlot) Activate(slot);
            if (activeSlot < 0) { SetVisible(false); return; }

            if (Keyboard.current != null && Keyboard.current.jKey.wasPressedThisFrame && !UIInputBlocker.IsBlocked) SetExpanded(!expanded);

            bool hide = TitleScreen.IsShowing || UIInputBlocker.IsBlocked
                        || (HarvestRhythmGame.Instance != null && HarvestRhythmGame.Instance.IsPlaying);
            SetVisible(!hide);

            Rewatch();
            pollTimer -= Time.unscaledDeltaTime;
            if (pollTimer <= 0f) { pollTimer = 0.4f; Poll(); }

            AnimateRows();
            if (allDoneTime > 0f && expanded && Time.unscaledTime - allDoneTime > 4f)
            {
                if (VisibleQuests().Count == 0) SetExpanded(false); // 진행 중인 부탁이 있으면 펼친 채로
                allDoneTime = -2f;
            }
        }

        // ───────────── 칸 시작·저장 ─────────────

        private void Activate(int slot)
        {
            activeSlot = slot;
            Unwatch();
            if (slot < 0) return;

            foreach (Mission m in missions) { m.done = false; m.doneTime = -1f; }
            questDoneTime.Clear(); seenActive.Clear(); allQuests = null; questSignature = "";
            if (!IsNewGame(slot)) Load(slot);
            else { Debug.Log("[임무] 새 게임 - 임무를 처음부터"); SaveProgress(); }

            BuildUI();
            readyTime = Time.unscaledTime + WarmUp;
            allDoneTime = AllDone() ? -2f : -1f;
            expanded = true;
            SetExpanded(expanded);
            Refresh();
        }

        // 새 게임: 방금(몇 초 안에) 저장 파일이 만들어졌고 1일차
        private static bool IsNewGame(int slot)
        {
            try
            {
                string path = SaveManager.SlotPath(slot);
                bool fresh = File.Exists(path) && (DateTime.Now - File.GetLastWriteTime(path)).TotalSeconds < 5;
                return fresh && BargainHistory.Attempts == 0 && GameClock.Instance != null && GameClock.Instance.CurrentDay == 1;
            }
            catch { return false; }
        }

        private static string ProgressPath(int slot) => Path.Combine(Application.persistentDataPath, $"missions_{slot + 1}.json");

        [Serializable] private class Progress { public List<string> done = new List<string>(); }

        private void Load(int slot)
        {
            try
            {
                string path = ProgressPath(slot);
                if (!File.Exists(path)) return;
                var p = JsonUtility.FromJson<Progress>(File.ReadAllText(path));
                if (p == null) return;
                foreach (Mission m in missions) m.done = p.done.Contains(m.id);
            }
            catch (Exception e) { Debug.LogWarning("[임무] 불러오기 실패: " + e.Message); }
        }

        private void SaveProgress()
        {
            if (activeSlot < 0) return;
            try
            {
                var p = new Progress();
                foreach (Mission m in missions) if (m.done) p.done.Add(m.id);
                File.WriteAllText(ProgressPath(activeSlot), JsonUtility.ToJson(p));
            }
            catch (Exception e) { Debug.LogWarning("[임무] 저장 실패: " + e.Message); }
        }

        // ───────────── 완료 판정 ─────────────

        private void Rewatch()
        {
            if (watchedInv != InventoryManager.Instance)
            {
                if (watchedInv != null) watchedInv.OnToolChanged -= OnToolChanged;
                watchedInv = InventoryManager.Instance;
                if (watchedInv != null) watchedInv.OnToolChanged += OnToolChanged;
            }
            if (watchedClock != GameClock.Instance)
            {
                if (watchedClock != null) watchedClock.OnDayChanged -= OnDayChanged;
                watchedClock = GameClock.Instance;
                if (watchedClock != null) watchedClock.OnDayChanged += OnDayChanged;
            }
        }

        private void Unwatch()
        {
            if (watchedInv != null) watchedInv.OnToolChanged -= OnToolChanged;
            if (watchedClock != null) watchedClock.OnDayChanged -= OnDayChanged;
            watchedInv = null;
            watchedClock = null;
        }

        private bool Ready => activeSlot >= 0 && Time.unscaledTime >= readyTime;

        private void OnToolChanged(ToolType t) { if (Ready) Complete("tool_swap"); }
        private void OnDayChanged(int day, Season s) { if (Ready) Complete("sleep"); }

        private void Poll()
        {
            // 밭 칸 (갈린 칸·작물·물 준 칸 수)
            if (tiles == null || tiles.Length == 0) tiles = FindObjectsByType<FarmTile>(FindObjectsInactive.Exclude);
            int tilled = 0, crops = 0, watered = 0;
            foreach (FarmTile t in tiles)
            {
                if (t == null) continue;
                if (t.State == TileState.Tilled) tilled++;
                if (t.CurrentCrop != null) crops++;
                if (t.IsWatered) watered++;
            }

            // 가방 (수확물·상급 이상·물고기 수)
            int harvest = 0, good = 0, fish = 0;
            InventoryManager inv = InventoryManager.Instance;
            if (inv != null)
                for (int i = 0; i < inv.TotalSlotCount; i++)
                {
                    InventorySlotData s = inv.GetSlot(i);
                    if (s == null || s.IsEmpty) continue;
                    if (s.item is CropData)
                    {
                        harvest += s.quantity;
                        if (s.quality == ItemQuality.Good || s.quality == ItemQuality.Perfect) good += s.quantity;
                    }
                    else if (s.item is FishData) fish += s.quantity;
                }
            int bargain = BargainHistory.Successes;

            if (Ready)
            {
                bool fromChest = ContainerWindow.IsOpen; // 상자에서 꺼낸 건 제외
                bool raining = WeatherManager.Instance != null && WeatherManager.Instance.IsRaining;
                if (tilled > lastTilled) Complete("till");
                if (crops > lastCrops) Complete("plant");
                if (watered > lastWatered && !raining) Complete("water");
                if (harvest > lastHarvest && !fromChest) Complete("harvest");
                if (good > lastGood && !fromChest) Complete("harvest_good");
                if (fish > lastFish && !fromChest) Complete("fish");
                if (bargain > lastBargain) Complete("bargain");
            }
            PollQuests();
            lastTilled = tilled; lastCrops = crops; lastWatered = watered;
            lastHarvest = harvest; lastGood = good; lastFish = fish; lastBargain = bargain;
        }

        // 퀘스트: 진행 중인 것 + 방금 끝낸 것. 내용이 바뀌면 다시 그림
        private void PollQuests()
        {
            QuestManager qm = QuestManager.Instance;
            if (qm == null) return;
            if (allQuests == null || allQuests.Count == 0)
            {
                // QuestManager가 들고 있는 퀘스트들. 진행 상태는 이름으로 기억하므로 같은 이름은 하나만 (복사본·중복 에셋이 두 줄로 뜨지 않게)
                allQuests = new List<QuestData>();
                var names = new HashSet<string>();
                foreach (QuestData q in Resources.FindObjectsOfTypeAll<QuestData>())
                    if (q != null && names.Add(q.name)) allQuests.Add(q);
            }
            foreach (QuestData q in allQuests)
            {
                if (q == null) continue;
                QuestState st = qm.GetState(q);
                if (st == QuestState.Active) seenActive.Add(q);
                else if (st == QuestState.Completed && seenActive.Remove(q)) questDoneTime[q] = Time.unscaledTime; // 지금 막 끝냄
            }
            var sb = new System.Text.StringBuilder();
            foreach (QuestData q in VisibleQuests()) sb.Append(q.name).Append(qm.GetState(q)).Append(qm.Have(q)).Append('|');
            string sig = sb.ToString();
            if (sig != questSignature) { questSignature = sig; Layout(); }
        }

        private List<QuestData> VisibleQuests()
        {
            var list = new List<QuestData>();
            QuestManager qm = QuestManager.Instance;
            if (qm == null || allQuests == null) return list;
            foreach (QuestData q in allQuests)
            {
                if (q == null) continue;
                QuestState st = qm.GetState(q);
                if (st == QuestState.Active) list.Add(q);
                else if (st == QuestState.Completed && questDoneTime.TryGetValue(q, out float t) && Time.unscaledTime - t < QuestKeepSeconds) list.Add(q);
            }
            list.Sort((a, b) => string.CompareOrdinal(a.title, b.title));
            return list;
        }

        private static string QuestText(QuestData q, QuestManager qm)
        {
            int have = Mathf.Min(qm.Have(q), q.amount);
            string goal = q.goal == QuestGoal.EarnBells
                ? $"{have:N0} / {q.amount:N0}벨"
                : $"{(q.item != null ? q.item.itemName : "?")} {have}/{q.amount}";
            return $"{q.title}  <size=80%>{goal}</size>";
        }

        private void Complete(string id)
        {
            Mission m = missions.Find(x => x.id == id);
            if (m == null || m.done) return;
            m.done = true;
            m.doneTime = Time.unscaledTime;
            Debug.Log($"[임무] 완료: {m.text}");
            SaveProgress();
            if (AllDone())
            {
                allDoneTime = Time.unscaledTime;
                IslandNotice.Show("모든 임무를 완료했어요!", this);
            }
            else IslandNotice.Show($"임무 완료! {m.text}", this);
            Refresh();
        }

        /// <summary>임무를 전부 안 한 상태로 되돌림 (전시회 단축키 등에서 호출)</summary>
        public static void ResetAll()
        {
            MissionPanel p = FindAnyObjectByType<MissionPanel>();
            if (p == null) return;
            foreach (Mission m in p.missions) { m.done = false; m.doneTime = -1f; }
            p.allDoneTime = -1f;
            p.SaveProgress();
            p.SetExpanded(true);
            p.Refresh();
            Debug.Log("[임무] 초기화");
        }

        private bool AllDone() => missions.TrueForAll(m => m.done);
        private int DoneCount() => missions.FindAll(m => m.done).Count;

        // ───────────── 화면 ─────────────

        private void BuildUI()
        {
            if (canvas != null) return;
            var go = new GameObject("MissionCanvas", typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500; // 폰 메뉴(700)·상점 등 창보다 뒤
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            Sprite rounded = RoundRect(128, 40f, false), ring = RoundRect(128, 40f, true);
            TMP_FontAsset font = UIFont.FindKorean();
            roundedSprite = rounded; ringSprite = ring; uiFont = font;

            panel = NewRect("Missions", go.transform);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 0.5f);
            panel.anchoredPosition = new Vector2(-24f, 60f);
            group = panel.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            // 바탕 (폰 메뉴처럼: 은은한 빛 → 반투명 바탕 → 줄무늬 → 테두리)
            Img(Fill(NewRect("Glow", panel), -24f), rounded, new Color(Holo.r, Holo.g, Holo.b, 0.07f));
            Img(Fill(NewRect("Fill", panel), 0f), rounded, new Color(Holo.r * 0.4f, Holo.g * 0.6f, Holo.b * 0.8f, 0.55f));
            scan = Fill(NewRect("Scanlines", panel), 8f).gameObject.AddComponent<RawImage>();
            scan.texture = ScanTexture();
            scan.color = new Color(Holo.r, Holo.g, Holo.b, 0.1f);
            scan.raycastTarget = false;
            Img(Fill(NewRect("Border", panel), 0f), ring, new Color(Holo.r, Holo.g, Holo.b, 0.9f));

            header = Text(panel, font, 30f, TextAlignmentOptions.Left);
            header.fontStyle = FontStyles.Bold;
            var hr = header.rectTransform;
            hr.anchorMin = new Vector2(0f, 1f); hr.anchorMax = new Vector2(1f, 1f); hr.pivot = new Vector2(0.5f, 1f);
            hr.offsetMin = new Vector2(26f, -HeaderH); hr.offsetMax = new Vector2(-26f, -10f);

            hint = Text(panel, font, 20f, TextAlignmentOptions.Right);
            hint.alpha = 0.6f;
            var hir = hint.rectTransform;
            hir.anchorMin = new Vector2(0f, 1f); hir.anchorMax = new Vector2(1f, 1f); hir.pivot = new Vector2(0.5f, 1f);
            hir.offsetMin = new Vector2(26f, -HeaderH); hir.offsetMax = new Vector2(-26f, -10f);

            // 펼쳤을 때 보이는 내용 (튜토리얼 소제목·임무 줄·부탁 소제목·부탁 줄). 위치는 Layout()이 정함
            list = NewRect("List", panel);
            list.anchorMin = Vector2.zero; list.anchorMax = Vector2.one;
            list.offsetMin = new Vector2(22f, 0f); list.offsetMax = new Vector2(-22f, -HeaderH);

            tutorialTitle = Text(list, font, 22f, TextAlignmentOptions.Left);
            tutorialTitle.color = new Color(Holo.r, Holo.g, Holo.b, 1f);
            questTitle = Text(list, font, 22f, TextAlignmentOptions.Left);
            questTitle.color = new Color(Holo.r, Holo.g, Holo.b, 1f);
            questTitle.text = "부탁";

            for (int i = 0; i < missions.Count; i++)
            {
                Mission m = missions[i];
                RectTransform row = NewRect("Row_" + m.id, list);
                m.row = row;

                // 체크 칸 (테두리) + 완료하면 안이 채워짐
                RectTransform box = NewRect("Box", row);
                box.anchorMin = box.anchorMax = box.pivot = new Vector2(0f, 0.5f);
                box.anchoredPosition = new Vector2(6f, 0f);
                box.sizeDelta = new Vector2(24f, 24f);
                m.box = Img(box, ring, new Color(Holo.r, Holo.g, Holo.b, 0.9f));
                RectTransform chk = NewRect("Check", box);
                chk.anchorMin = Vector2.zero; chk.anchorMax = Vector2.one;
                chk.offsetMin = new Vector2(5f, 5f); chk.offsetMax = new Vector2(-5f, -5f);
                m.check = Img(chk, rounded, Holo);

                m.label = Text(row, font, 25f, TextAlignmentOptions.Left);
                var lr = m.label.rectTransform;
                lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
                lr.offsetMin = new Vector2(44f, 0f); lr.offsetMax = Vector2.zero;
                m.label.textWrappingMode = TextWrappingModes.NoWrap;
            }
            shownAlpha = 0f;
            group.alpha = 0f;
        }

        private void Refresh()
        {
            if (canvas == null) return;
            int quests = VisibleQuests().Count;
            header.text = AllDone() ? (quests > 0 ? $"임무  부탁 {quests}" : "임무 완료!") : $"임무  {DoneCount()}/{missions.Count}";
            hint.text = expanded ? "J 접기" : "J 펼치기";
            tutorialTitle.text = AllDone() ? "튜토리얼 완료!" : $"튜토리얼  {DoneCount()}/{missions.Count}";
            foreach (Mission m in missions)
            {
                m.label.text = m.done ? $"<s>{m.text}</s>" : m.text;
                m.check.enabled = m.done;
                if (m.doneTime < 0f) ApplyRowColor(m, 1f);
            }
            Layout();
        }

        // 위에서부터 차례로 놓고 패널 높이를 맞춤
        private void Layout()
        {
            if (panel == null) return;
            QuestManager qm = QuestManager.Instance;
            List<QuestData> quests = VisibleQuests();
            float y = 0f;

            Place(tutorialTitle.rectTransform, ref y, SubH);
            bool showMissions = !AllDone();
            foreach (Mission m in missions)
            {
                m.row.gameObject.SetActive(showMissions);
                if (showMissions) Place(m.row, ref y, RowH);
            }

            questTitle.gameObject.SetActive(quests.Count > 0);
            if (quests.Count > 0) { y -= 6f; Place(questTitle.rectTransform, ref y, SubH); }
            while (questRows.Count < quests.Count) questRows.Add(NewQuestRow());
            for (int i = 0; i < questRows.Count; i++)
            {
                QuestRow r = questRows[i];
                bool on = i < quests.Count && qm != null;
                r.rt.gameObject.SetActive(on);
                if (!on) continue;
                QuestData q = quests[i];
                QuestState st = qm.GetState(q);
                bool done = st == QuestState.Completed, ready = qm.IsReady(q);
                string text = QuestText(q, qm);
                r.label.text = done ? $"<s>{text}</s>" : ready ? $"{text}  <size=80%>가져다주기!</size>" : text;
                r.label.color = done ? DoneColor : ready ? ReadyColor : Color.white;
                r.box.color = done ? new Color(Holo.r, Holo.g, Holo.b, 0.35f) : ready ? ReadyColor : new Color(Holo.r, Holo.g, Holo.b, 0.9f);
                r.check.enabled = done;
                r.check.color = new Color(Holo.r, Holo.g, Holo.b, 0.35f);
                Place(r.rt, ref y, RowH);
            }

            panel.sizeDelta = new Vector2(Width, expanded ? HeaderH - y + 16f : HeaderH + 6f);
            if (scan != null) scan.uvRect = new Rect(0f, 0f, 1f, panel.sizeDelta.y / 6f); // 6px마다 가로줄
            header.text = AllDone() ? (quests.Count > 0 ? $"임무  부탁 {quests.Count}" : "임무 완료!") : $"임무  {DoneCount()}/{missions.Count}";
        }

        private static void Place(RectTransform rt, ref float y, float h)
        {
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(0f, y - h); rt.offsetMax = new Vector2(0f, y);
            y -= h;
        }

        private QuestRow NewQuestRow()
        {
            var r = new QuestRow { rt = NewRect("Quest", list) };
            RectTransform box = NewRect("Box", r.rt);
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0f, 0.5f);
            box.anchoredPosition = new Vector2(6f, 0f);
            box.sizeDelta = new Vector2(24f, 24f);
            r.box = Img(box, ringSprite, Holo);
            RectTransform chk = NewRect("Check", box);
            chk.anchorMin = Vector2.zero; chk.anchorMax = Vector2.one;
            chk.offsetMin = new Vector2(5f, 5f); chk.offsetMax = new Vector2(-5f, -5f);
            r.check = Img(chk, roundedSprite, Holo);
            r.label = Text(r.rt, uiFont, 24f, TextAlignmentOptions.Left);
            var lr = r.label.rectTransform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(44f, 0f); lr.offsetMax = Vector2.zero;
            r.label.textWrappingMode = TextWrappingModes.NoWrap;
            r.label.overflowMode = TextOverflowModes.Ellipsis;
            return r;
        }

        // 막 완료한 줄은 0.8초 동안 밝게 빛났다가 회색 반투명으로
        private void AnimateRows()
        {
            foreach (Mission m in missions)
            {
                if (m.doneTime < 0f) continue;
                float k = Mathf.Clamp01((Time.unscaledTime - m.doneTime) / 0.8f);
                ApplyRowColor(m, k);
                if (k >= 1f) m.doneTime = -1f;
            }
        }

        private static void ApplyRowColor(Mission m, float k)
        {
            if (m.label == null) return;
            if (!m.done) { m.label.color = Color.white; m.box.color = new Color(Holo.r, Holo.g, Holo.b, 0.9f); return; }
            Color flash = new Color(0.75f, 1f, 1f, 1f);
            m.label.color = Color.Lerp(flash, DoneColor, k);
            m.box.color = Color.Lerp(Holo, new Color(Holo.r, Holo.g, Holo.b, 0.35f), k);
            m.check.color = Color.Lerp(Holo, new Color(Holo.r, Holo.g, Holo.b, 0.35f), k);
        }

        private void SetExpanded(bool on)
        {
            expanded = on;
            if (panel == null) return;
            list.gameObject.SetActive(on);
            Refresh(); // Layout()이 패널 높이를 맞춤
        }

        private void SetVisible(bool on)
        {
            if (group == null) return;
            shownAlpha = Mathf.MoveTowards(shownAlpha, on ? 1f : 0f, Time.unscaledDeltaTime * 6f);
            group.alpha = shownAlpha;
        }

        // ───────────── 도우미 ─────────────

        private static RectTransform NewRect(string n, Transform parent)
        {
            var go = new GameObject(n, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static RectTransform Fill(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        private static Image Img(RectTransform rt, Sprite s, Color c)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = s;
            img.type = Image.Type.Sliced;
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        private static TextMeshProUGUI Text(Transform parent, TMP_FontAsset font, float size, TextAlignmentOptions align)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.fontSize = size;
            t.alignment = align;
            t.color = Color.white;
            t.raycastTarget = false;
            return t;
        }

        // 폰 메뉴와 같은 둥근 사각형 (ringOnly = 테두리만)
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
    }
}