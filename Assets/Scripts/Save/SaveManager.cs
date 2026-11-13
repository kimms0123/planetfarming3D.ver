using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using FarmingSystem.Core;
using FarmingSystem.Farming;
using FarmingSystem.Inventory;
using FarmingSystem.Economy;
using FarmingSystem.Building;
using FarmingSystem.Weather;
using FarmingSystem.Player;
using FarmingSystem.Rhythm;
using FarmingSystem.NPC;
using FarmingSystem.Dialogue;

namespace FarmingSystem.Save
{
    // 저장 관리 (3칸, 몇 분마다 자동 저장)
    // - 시작 화면(TitleScreen)에서 칸을 고르면 StartNewGame / LoadGame이 불리고 그때부터 자동 저장
    // - 자동 저장: Autosave Minutes마다 + 하루가 바뀔 때 + 게임을 끌 때
    //   (수확 리듬게임 중이면 끝날 때까지 미뤘다가 저장)
    // - 저장 위치: Application.persistentDataPath/save_1.json ~ save_3.json
    //   (Windows: C:\Users\이름\AppData\LocalLow\회사이름\게임이름) - 메뉴 "저장 폴더 열기"로 바로 열 수 있음
    // - 저장하는 것: 날짜·시각, 돈, 플레이어 위치, 가방·도구칸, 밭·작물, 건물, 오늘 날씨, 상점 재고, 흥정 누적
    //   + 주민(누가 어느 집에 사는지 - 한 번 정해지면 안 바뀜)·한 번 온 손님 (NPC가 돌아다니는 위치는 저장 안 함)
    // - 아이템·작물·건물은 에셋 이름으로 저장 → Items / Buildings 목록에 있어야 불러올 수 있음
    //   (Tools → 행성농사일지 → 저장 → 저장·시작 화면 만들기 를 다시 누르면 목록이 새로 채워짐)
    public class SaveManager : MonoBehaviour
    {
        public static SaveManager Instance { get; private set; }
        public const int SlotCount = 3;

        [Header("자동 저장")]
        [Tooltip("몇 분(실제 시간)마다 자동 저장할지")]
        [SerializeField] private float autosaveMinutes = 3f;
        [Tooltip("게임 속 하루가 바뀔 때도 저장")]
        [SerializeField] private bool saveOnDayChange = true;
        [Tooltip("게임을 끌 때 저장")]
        [SerializeField] private bool saveOnQuit = true;
        [Tooltip("저장할 때 화면 오른쪽 아래에 '저장했어요' 표시")]
        [SerializeField] private bool showIndicator = true;

        [Header("데이터 목록 (메뉴가 자동으로 채움 - 새 아이템·건물을 만들면 메뉴를 다시 누르기)")]
        [SerializeField] private List<ItemData> items = new List<ItemData>();
        [SerializeField] private List<BuildingData> buildingDatas = new List<BuildingData>();

        /// <summary>지금 플레이 중인 칸 (0~2). 시작 화면에 있을 땐 -1</summary>
        public int CurrentSlot { get; private set; } = -1;
        public bool IsPlaying => CurrentSlot >= 0;
        public event Action<int> OnSaved;

        private float autosaveTimer;
        private float playSeconds;
        private int lastDay = -1;
        private bool pendingSave;
        private Dictionary<string, ItemData> itemByName;
        private Dictionary<string, BuildingData> buildingByName;
        private CanvasGroup indicator;
        private Coroutine indicatorRoutine;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (!IsPlaying) return;
            playSeconds += Time.unscaledDeltaTime;

            autosaveTimer += Time.unscaledDeltaTime;
            if (autosaveMinutes > 0f && autosaveTimer >= autosaveMinutes * 60f) pendingSave = true;

            GameClock clock = GameClock.Instance;
            if (clock != null)
            {
                if (saveOnDayChange && lastDay >= 0 && clock.CurrentDay != lastDay) pendingSave = true;
                lastDay = clock.CurrentDay;
            }

            if (pendingSave && !BlockedNow) Save();
        }

        private void OnApplicationQuit()
        {
            if (saveOnQuit && IsPlaying) Save(force: true);
        }

        // 수확 리듬게임 중엔 작물이 반쯤 뽑힌 상태라 저장을 미룸
        private static bool BlockedNow => HarvestRhythmGame.Instance != null && HarvestRhythmGame.Instance.IsPlaying;

        // ───────────────────────── 칸 ─────────────────────────

        public static string SlotPath(int slot) => Path.Combine(Application.persistentDataPath, $"save_{slot + 1}.json");

        public static bool HasSave(int slot) => File.Exists(SlotPath(slot));

        /// <summary>칸 고르는 화면에 보여 줄 요약</summary>
        public static SlotSummary GetSummary(int slot)
        {
            var s = new SlotSummary { slot = slot };
            SaveData d = ReadFile(slot);
            if (d == null) return s;
            s.exists = true;
            s.farmerName = d.farmerName;
            s.day = d.day;
            s.bells = d.bells;
            s.playSeconds = d.playSeconds;
            s.savedAt = d.savedAt;
            return s;
        }

        /// <summary>새 게임: 그 칸을 지금 씬 상태(처음 상태)로 시작하고 바로 한 번 저장</summary>
        public void StartNewGame(int slot)
        {
            CurrentSlot = Mathf.Clamp(slot, 0, SlotCount - 1);
            playSeconds = 0f;
            BargainHistory.Attempts = 0;
            BargainHistory.Successes = 0;
            if (ResidentManager.Instance != null) ResidentManager.Instance.ResetForNewGame();
            if (QuestManager.Instance != null) QuestManager.Instance.ResetForNewGame();
            ResetTimers();
            Save(force: true);
            Debug.Log($"[저장] {CurrentSlot + 1}번 칸 새 게임 시작");
        }

        /// <summary>이어하기: 그 칸의 저장을 불러옴. 실패하면 false</summary>
        public bool LoadGame(int slot)
        {
            SaveData d = ReadFile(slot);
            if (d == null) { Debug.LogWarning($"[저장] {slot + 1}번 칸을 불러오지 못했어요"); return false; }
            BuildLookups();
            Apply(d);
            CurrentSlot = slot;
            playSeconds = d.playSeconds;
            ResetTimers();
            Debug.Log($"[저장] {slot + 1}번 칸 불러옴 (Day {d.day} {d.hour:00}:{d.minute:00})");
            return true;
        }

        /// <summary>가장 최근에 저장한 칸의 내용 (없으면 null) - 시작 화면에서 주민 미리 보여 줄 때</summary>
        public static SaveData ReadLatest()
        {
            SaveData best = null;
            for (int i = 0; i < SlotCount; i++)
            {
                SaveData d = ReadFile(i);
                if (d != null && (best == null || string.CompareOrdinal(d.savedAt, best.savedAt) > 0)) best = d;
            }
            return best;
        }

        public static void DeleteSlot(int slot)
        {
            try
            {
                if (File.Exists(SlotPath(slot))) File.Delete(SlotPath(slot));
                if (File.Exists(SlotPath(slot) + ".bak")) File.Delete(SlotPath(slot) + ".bak");
            }
            catch (Exception e) { Debug.LogWarning("[저장] 지우기 실패: " + e.Message); }
        }

        /// <summary>저장하고 시작 화면으로 (씬을 다시 불러옴)</summary>
        public void ReturnToTitle()
        {
            if (IsPlaying) Save(force: true);
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private void ResetTimers()
        {
            autosaveTimer = 0f;
            pendingSave = false;
            lastDay = GameClock.Instance != null ? GameClock.Instance.CurrentDay : -1;
        }

        // ───────────────────────── 저장 ─────────────────────────

        /// <summary>지금 상태를 지금 칸에 저장. force = 리듬게임 중이어도 저장</summary>
        public bool Save(bool force = false)
        {
            if (!IsPlaying) return false;
            if (!force && BlockedNow) { pendingSave = true; return false; }
            pendingSave = false;
            autosaveTimer = 0f;
            try
            {
                SaveData d = Capture();
                string path = SlotPath(CurrentSlot);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(d, true));
                if (File.Exists(path)) File.Copy(path, path + ".bak", true); // 바로 전 저장을 하나 남겨 둠
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                OnSaved?.Invoke(CurrentSlot);
                if (showIndicator && Application.isPlaying) ShowIndicator();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[저장] 저장 실패: " + e.Message);
                return false;
            }
        }

        private static SaveData ReadFile(int slot)
        {
            foreach (string p in new[] { SlotPath(slot), SlotPath(slot) + ".bak" }) // 본 파일이 깨졌으면 바로 전 저장으로
            {
                try
                {
                    if (!File.Exists(p)) continue;
                    SaveData d = JsonUtility.FromJson<SaveData>(File.ReadAllText(p));
                    if (d != null) return d;
                }
                catch (Exception e) { Debug.LogWarning($"[저장] {Path.GetFileName(p)} 읽기 실패: {e.Message}"); }
            }
            return null;
        }

        private SaveData Capture()
        {
            var d = new SaveData
            {
                savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                playSeconds = playSeconds,
                bargainAttempts = BargainHistory.Attempts,
                bargainSuccesses = BargainHistory.Successes,
            };

            GameClock clock = GameClock.Instance;
            if (clock != null) { d.day = clock.CurrentDay; d.hour = clock.CurrentHour; d.minute = clock.CurrentMinute; }
            if (CurrencyManager.Instance != null) d.bells = CurrencyManager.Instance.CurrentBells;

            PlayerController player = FindAnyObjectByType<PlayerController>();
            if (player != null)
            {
                Vector3 p = player.transform.position;
                d.playerPos = new[] { p.x, p.y, p.z };
                d.playerYaw = player.transform.eulerAngles.y;
            }

            InventoryManager inv = InventoryManager.Instance;
            if (inv != null)
            {
                for (int i = 0; i < inv.GetSlotCount(SlotArea.Main); i++) AddSlot(d.bag, i, inv.GetSlot(SlotArea.Main, i));
                for (int i = 0; i < inv.GetSlotCount(SlotArea.Tool); i++) AddSlot(d.tools, i, inv.GetSlot(SlotArea.Tool, i));
                d.selectedHotbar = inv.SelectedHotbarIndex;
                d.selectedTool = inv.CurrentToolIndex;
            }

            foreach (FarmTile t in FindObjectsByType<FarmTile>(FindObjectsInactive.Include))
            {
                if (t.State == TileState.Natural && t.CurrentCrop == null) continue;
                CropInstance c = t.CurrentCrop;
                d.tiles.Add(new TileSave
                {
                    x = t.GridPosition.x,
                    y = t.GridPosition.y,
                    state = (int)t.State,
                    watered = t.IsWatered,
                    crop = c != null && c.Data != null ? c.Data.name : "",
                    growthDay = c != null ? c.CurrentGrowthDay : 0,
                    withered = c != null && c.IsWithered,
                    storm = c != null && c.IsStormDamaged,
                    daysSinceReady = c != null ? c.DaysSinceReady : 0,
                });
            }

            if (BuildingSystem.Instance != null)
            {
                foreach (PlacedBuilding b in BuildingSystem.Instance.Buildings)
                {
                    if (b == null || b.Data == null) continue;
                    var bs = new BuildingSave
                    {
                        data = b.Data.name,
                        rotation = b.Rotation,
                        complete = b.State == PlacedBuilding.BuildState.Complete,
                        completeDay = b.CompleteDay,
                        center = new[] { b.transform.position.x, b.transform.position.y, b.transform.position.z },
                    };
                    foreach (Vector2Int c in b.Cells) { bs.cells.Add(c.x); bs.cells.Add(c.y); }
                    d.buildings.Add(bs);
                }
            }

            if (WeatherManager.Instance != null) d.todayWeather = (int)WeatherManager.Instance.CurrentWeather;
            if (ResidentManager.Instance != null) ResidentManager.Instance.Capture(d);
            if (QuestManager.Instance != null) QuestManager.Instance.Capture(d);

            if (ShopManager.Instance != null)
                foreach (ShopItemEntry e in ShopManager.Instance.BuyCatalog)
                    if (e != null && e.item != null && e.stock >= 0) d.shopStock.Add(new StockSave { item = e.item.name, stock = e.stock });

            return d;
        }

        private static void AddSlot(List<SlotSave> list, int index, InventorySlotData s)
        {
            if (s == null || s.IsEmpty) return;
            list.Add(new SlotSave { index = index, item = s.item.name, quantity = s.quantity, quality = (int)s.quality });
        }

        // ───────────────────────── 불러오기 ─────────────────────────

        private void Apply(SaveData d)
        {
            // 1. 시간 (하늘·시계·계절 블록이 따라 바뀜)
            if (GameClock.Instance != null) GameClock.Instance.LoadTime(d.day, d.hour, d.minute);

            // 2. 돈
            CurrencyManager money = CurrencyManager.Instance;
            if (money != null)
            {
                int diff = d.bells - money.CurrentBells;
                if (diff > 0) money.AddBells(diff);
                else if (diff < 0) money.TrySpendBells(-diff);
            }

            // 3. 가방·도구칸 (처음 시작 아이템은 지우고 저장된 것만)
            InventoryManager inv = InventoryManager.Instance;
            if (inv != null)
            {
                FillArea(inv, SlotArea.Tool, d.tools);
                FillArea(inv, SlotArea.Main, d.bag);
                InventorySlotData first = inv.GetSlot(0);
                if (first != null) inv.SetSlot(0, first.item, first.quantity, first.quality); // 화면 갱신 신호
                if (d.selectedHotbar >= 0 && d.selectedHotbar < inv.HotbarSize) inv.SelectHotbarSlot(d.selectedHotbar);
                if (d.selectedTool >= 0 && d.selectedTool < inv.ToolSlotCount) inv.SelectToolSlot(d.selectedTool);
            }

            // 4. 밭·작물
            var tileByPos = new Dictionary<Vector2Int, FarmTile>();
            foreach (FarmTile t in FindObjectsByType<FarmTile>(FindObjectsInactive.Include)) tileByPos[t.GridPosition] = t;
            int missingCrops = 0;
            foreach (TileSave ts in d.tiles)
            {
                if (!tileByPos.TryGetValue(new Vector2Int(ts.x, ts.y), out FarmTile tile)) continue;
                CropData crop = null;
                if (!string.IsNullOrEmpty(ts.crop))
                {
                    crop = FindItem(ts.crop) as CropData;
                    if (crop == null) missingCrops++;
                }
                tile.RestoreSave((TileState)ts.state, ts.watered, crop, ts.growthDay, ts.withered, ts.storm, ts.daysSinceReady);
            }
            if (missingCrops > 0) Debug.LogWarning($"[저장] 작물 {missingCrops}개를 못 찾았어요 → SaveManager 목록을 새로 채워 주세요");

            // 5. 건물
            BuildingSystem bsys = BuildingSystem.Instance;
            if (bsys != null)
            {
                bsys.RefreshGrid();
                foreach (BuildingSave b in d.buildings)
                {
                    BuildingData data = FindBuilding(b.data);
                    if (data == null) { Debug.LogWarning($"[저장] 건물 '{b.data}'을(를) 못 찾았어요"); continue; }
                    var cells = new List<Vector2Int>();
                    for (int i = 0; i + 1 < b.cells.Count; i += 2) cells.Add(new Vector2Int(b.cells[i], b.cells[i + 1]));
                    Vector3 center = b.center != null && b.center.Length == 3 ? new Vector3(b.center[0], b.center[1], b.center[2]) : Vector3.zero;
                    PlacedBuilding placed = bsys.Place(data, cells, center, b.rotation);
                    if (placed == null) continue;
                    if (b.complete) { if (placed.State != PlacedBuilding.BuildState.Complete) placed.Complete(); }
                    else placed.RestoreCompleteDay(b.completeDay);
                }
            }


            // 6. 플레이어 위치
            PlayerController player = FindAnyObjectByType<PlayerController>();
            if (player != null && d.playerPos != null && d.playerPos.Length == 3)
            {
                CharacterController cc = player.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                player.transform.SetPositionAndRotation(new Vector3(d.playerPos[0], d.playerPos[1], d.playerPos[2]), Quaternion.Euler(0f, d.playerYaw, 0f));
                if (cc != null) cc.enabled = true;
            }

            // 7. 오늘 날씨
            if (WeatherManager.Instance != null && d.todayWeather >= 0) WeatherManager.Instance.SetWeatherToday((WeatherType)d.todayWeather);

            // 8. 상점 재고
            if (ShopManager.Instance != null)
                foreach (StockSave s in d.shopStock)
                    foreach (ShopItemEntry e in ShopManager.Instance.BuyCatalog)
                        if (e != null && e.item != null && e.item.name == s.item) e.stock = s.stock;

            // 9. 흥정 누적
            BargainHistory.Attempts = d.bargainAttempts;
            BargainHistory.Successes = d.bargainSuccesses;

            // 10. 주민(누가 어느 집에)·손님 (집이 다 놓인 뒤에)
            if (ResidentManager.Instance != null) ResidentManager.Instance.OnGameLoaded(d);

            // 11. 퀘스트·대화 기억 (돈을 맞춘 뒤에 - 불러올 때 돈이 바뀐 걸 '번 돈'으로 세지 않게)
            if (QuestManager.Instance != null) QuestManager.Instance.Apply(d);
        }

        private void FillArea(InventoryManager inv, SlotArea area, List<SlotSave> saved)
        {
            int count = inv.GetSlotCount(area);
            for (int i = 0; i < count; i++) inv.GetSlot(area, i)?.Clear();
            foreach (SlotSave s in saved)
            {
                InventorySlotData slot = inv.GetSlot(area, s.index);
                ItemData item = FindItem(s.item);
                if (slot == null) continue;
                if (item == null) { Debug.LogWarning($"[저장] 아이템 '{s.item}'을(를) 못 찾았어요 → SaveManager 목록을 새로 채워 주세요"); continue; }
                slot.item = item;
                slot.quantity = s.quantity;
                slot.quality = (ItemQuality)s.quality;
            }
        }

        private void BuildLookups()
        {
            itemByName = new Dictionary<string, ItemData>();
            foreach (ItemData i in items) if (i != null && !itemByName.ContainsKey(i.name)) itemByName[i.name] = i;
            buildingByName = new Dictionary<string, BuildingData>();
            foreach (BuildingData b in buildingDatas) if (b != null && !buildingByName.ContainsKey(b.name)) buildingByName[b.name] = b;
        }

        private ItemData FindItem(string n) => !string.IsNullOrEmpty(n) && itemByName != null && itemByName.TryGetValue(n, out ItemData i) ? i : null;
        private BuildingData FindBuilding(string n) => !string.IsNullOrEmpty(n) && buildingByName != null && buildingByName.TryGetValue(n, out BuildingData b) ? b : null;

        // ───────────────────────── '저장했어요' 표시 ─────────────────────────

        private void ShowIndicator()
        {
            if (indicator == null) BuildIndicator();
            if (indicatorRoutine != null) StopCoroutine(indicatorRoutine);
            indicatorRoutine = StartCoroutine(IndicatorRoutine());
        }

        private IEnumerator IndicatorRoutine()
        {
            for (float t = 0f; t < 0.25f; t += Time.unscaledDeltaTime) { indicator.alpha = t / 0.25f; yield return null; }
            indicator.alpha = 1f;
            for (float t = 0f; t < 1.2f; t += Time.unscaledDeltaTime) yield return null;
            for (float t = 0f; t < 0.6f; t += Time.unscaledDeltaTime) { indicator.alpha = 1f - t / 0.6f; yield return null; }
            indicator.alpha = 0f;
            indicatorRoutine = null;
        }

        private void BuildIndicator()
        {
            var canvasGo = new GameObject("SaveIndicatorCanvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 900;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var box = new GameObject("SaveIndicator", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            box.transform.SetParent(canvasGo.transform, false);
            var rt = (RectTransform)box.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-32f, 32f);
            rt.sizeDelta = new Vector2(220f, 56f);
            box.GetComponent<Image>().color = new Color(0.08f, 0.1f, 0.18f, 0.75f);
            indicator = box.GetComponent<CanvasGroup>();
            indicator.alpha = 0f;
            indicator.blocksRaycasts = false;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(box.transform, false);
            var trt = (RectTransform)textGo.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            var tmp = textGo.GetComponent<TextMeshProUGUI>();
            TMP_FontAsset font = UIFont.FindKorean();
            if (font != null) tmp.font = font;
            tmp.text = "저장했어요";
            tmp.fontSize = 26f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
        }

#if UNITY_EDITOR
        /// <summary>프로젝트의 모든 아이템·건물 에셋으로 목록 채우기 (에디터 전용, 메뉴·우클릭에서 호출)</summary>
        [ContextMenu("아이템·건물 목록 새로 채우기")]
        public void FillDatabases()
        {
            items.Clear();
            foreach (string g in UnityEditor.AssetDatabase.FindAssets("t:ItemData", new[] { "Assets" }))
            {
                var i = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemData>(UnityEditor.AssetDatabase.GUIDToAssetPath(g));
                if (i != null && !items.Contains(i)) items.Add(i);
            }
            buildingDatas.Clear();
            foreach (string g in UnityEditor.AssetDatabase.FindAssets("t:BuildingData", new[] { "Assets" }))
            {
                var b = UnityEditor.AssetDatabase.LoadAssetAtPath<BuildingData>(UnityEditor.AssetDatabase.GUIDToAssetPath(g));
                if (b != null && !buildingDatas.Contains(b)) buildingDatas.Add(b);
            }
            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"[저장] 목록 채움: 아이템 {items.Count}개, 건물 {buildingDatas.Count}개");
        }

        public int ItemCount => items.Count;
        public int BuildingCount => buildingDatas.Count;
#endif
    }

    /// <summary>씬의 다른 글자에서 한글 TMP 폰트 찾기 (저장 표시·시작 화면이 같이 씀)</summary>
    public static class UIFont
    {
        private static TMP_FontAsset cached;

        public static TMP_FontAsset FindKorean()
        {
            if (cached != null) return cached;
            foreach (TMP_Text t in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include))
                if (t.font != null && !t.font.name.StartsWith("LiberationSans")) { cached = t.font; break; }
            return cached;
        }
    }
}