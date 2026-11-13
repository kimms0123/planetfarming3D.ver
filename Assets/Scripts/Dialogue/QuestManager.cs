using System.Collections.Generic;
using UnityEngine;
using FarmingSystem.Core;
using FarmingSystem.Economy;
using FarmingSystem.Inventory;
using FarmingSystem.NPC;
using FarmingSystem.Save;

namespace FarmingSystem.Dialogue
{
    public enum QuestState { NotStarted, Active, Completed }

    // 퀘스트 진행 상태 + NPC와 나눈 대화 기억 (누구를 만났는지, 오늘 말 걸었는지, 한 번만 하는 말을 했는지)
    // 씬에 하나 (Tools → 행성농사일지 → 대화 → 물고기 NPC 예시 대사·퀘스트 만들기 가 같이 만들어 줌)
    // 저장 칸에 같이 저장됨
    public class QuestManager : MonoBehaviour
    {
        public static QuestManager Instance { get; private set; }

        [Tooltip("게임에 있는 퀘스트 전부 (저장 불러오기에 필요). 메뉴가 자동으로 채움 - 새 퀘스트를 만들면 다시 누르기")]
        [SerializeField] private List<QuestData> quests = new List<QuestData>();

        private readonly Dictionary<string, QuestState> states = new Dictionary<string, QuestState>();
        private readonly Dictionary<string, int> progress = new Dictionary<string, int>(); // 돈 벌기: 받은 뒤 번 벨
        private readonly Dictionary<string, TalkMemory> talks = new Dictionary<string, TalkMemory>();
        private CurrencyManager money;
        private int lastBells;

        private class TalkMemory
        {
            public int lastDay = -1;
            public readonly HashSet<string> seen = new HashSet<string>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (money != null) money.OnBellsChanged -= OnBellsChanged;
        }

        private void Update()
        {
            if (money == null && CurrencyManager.Instance != null)
            {
                money = CurrencyManager.Instance;
                lastBells = money.CurrentBells;
                money.OnBellsChanged += OnBellsChanged;
            }
        }

        // 돈 벌기 퀘스트: 늘어난 만큼만 셈
        private void OnBellsChanged(int bells)
        {
            int delta = bells - lastBells;
            lastBells = bells;
            if (delta <= 0) return;
            foreach (QuestData q in quests)
                if (q != null && q.goal == QuestGoal.EarnBells && GetState(q) == QuestState.Active)
                    progress[q.name] = Progress(q) + delta;
        }

        // ───────────────────────── 퀘스트 ─────────────────────────

        public QuestState GetState(QuestData q) => q != null && states.TryGetValue(q.name, out QuestState s) ? s : QuestState.NotStarted;

        public bool RequirementMet(QuestData q) => q == null || q.requires == null || GetState(q.requires) == QuestState.Completed;

        private int Progress(QuestData q) => progress.TryGetValue(q.name, out int p) ? p : 0;

        /// <summary>지금 가진 개수 / 번 벨</summary>
        public int Have(QuestData q)
        {
            if (q == null) return 0;
            return q.goal == QuestGoal.EarnBells ? Progress(q) : CountItem(q.item);
        }

        /// <summary>진행 중이고 목표를 다 채웠는지</summary>
        public bool IsReady(QuestData q) => GetState(q) == QuestState.Active && Have(q) >= q.amount;

        public void StartQuest(QuestData q)
        {
            if (q == null || GetState(q) != QuestState.NotStarted) return;
            states[q.name] = QuestState.Active;
            progress[q.name] = 0;
            Debug.Log($"[퀘스트] 시작: {q.title}");
            IslandNotice.Show($"새 부탁: {q.title}", this);
        }

        /// <summary>퀘스트 끝내기 (물건을 가져가고 보상을 줌). 아직 못 끝내면 false</summary>
        public bool TryTurnIn(QuestData q)
        {
            if (!IsReady(q)) return false;
            if (q.goal == QuestGoal.DeliverItem) RemoveItem(q.item, q.amount);
            if (q.rewardBells > 0 && CurrencyManager.Instance != null) CurrencyManager.Instance.AddBells(q.rewardBells);
            if (q.rewardItem != null && InventoryManager.Instance != null) InventoryManager.Instance.AddItem(q.rewardItem, q.rewardItemAmount);
            states[q.name] = QuestState.Completed;
            Debug.Log($"[퀘스트] 완료: {q.title}");
            IslandNotice.Show(q.rewardBells > 0 ? $"부탁 완료! {q.title}  +{q.rewardBells:N0}벨" : $"부탁 완료! {q.title}", this);
            if (SaveManager.Instance != null) SaveManager.Instance.Save();
            return true;
        }

        private static int CountItem(ItemData item)
        {
            InventoryManager inv = InventoryManager.Instance;
            if (inv == null || item == null) return 0;
            int n = 0;
            for (int i = 0; i < inv.TotalSlotCount; i++)
            {
                InventorySlotData s = inv.GetSlot(i);
                if (s != null && !s.IsEmpty && s.item == item) n += s.quantity;
            }
            return n;
        }

        private static void RemoveItem(ItemData item, int amount)
        {
            InventoryManager inv = InventoryManager.Instance;
            if (inv == null || item == null) return;
            for (int i = 0; i < inv.TotalSlotCount && amount > 0; i++)
            {
                InventorySlotData s = inv.GetSlot(i);
                if (s == null || s.IsEmpty || s.item != item) continue;
                amount -= inv.RemoveFromSlot(i, Mathf.Min(amount, s.quantity));
            }
        }

        // ───────────────────────── 대화 기억 ─────────────────────────

        private TalkMemory Memory(string npc)
        {
            if (!talks.TryGetValue(npc, out TalkMemory m)) { m = new TalkMemory(); talks[npc] = m; }
            return m;
        }

        public bool HasMet(string npc) => talks.TryGetValue(npc, out TalkMemory m) && m.lastDay >= 0;

        public bool TalkedToday(string npc) =>
            talks.TryGetValue(npc, out TalkMemory m) && GameClock.Instance != null && m.lastDay == GameClock.Instance.CurrentDay;

        public bool HasSeen(string npc, string key) => talks.TryGetValue(npc, out TalkMemory m) && m.seen.Contains(key);

        /// <summary>말을 건 뒤 기록 (오늘 말 걸었음 + 한 번만 하는 말이면 기억)</summary>
        public void MarkTalk(string npc, string entryKey, bool onceOnly)
        {
            TalkMemory m = Memory(npc);
            m.lastDay = GameClock.Instance != null ? GameClock.Instance.CurrentDay : 0;
            if (onceOnly && !string.IsNullOrEmpty(entryKey)) m.seen.Add(entryKey);
        }

        /// <summary>대사 속 {item} {amount} {have} {reward}를 퀘스트 값으로 바꿈</summary>
        public string Format(string text, QuestData q)
        {
            if (string.IsNullOrEmpty(text) || q == null) return text;
            string itemName = q.item != null ? (string.IsNullOrEmpty(q.item.itemName) ? q.item.name : q.item.itemName) : "";
            return text.Replace("{item}", itemName)
                       .Replace("{amount}", q.amount.ToString("N0"))
                       .Replace("{have}", Have(q).ToString("N0"))
                       .Replace("{reward}", q.rewardBells.ToString("N0"));
        }

        // ───────────────────────── 저장 ─────────────────────────

        public void Capture(SaveData d)
        {
            d.quests.Clear();
            foreach (var kv in states)
                d.quests.Add(new QuestSave { id = kv.Key, state = (int)kv.Value, progress = progress.TryGetValue(kv.Key, out int p) ? p : 0 });
            d.talks.Clear();
            foreach (var kv in talks)
                d.talks.Add(new TalkSave { npc = kv.Key, lastDay = kv.Value.lastDay, seen = new List<string>(kv.Value.seen) });
        }

        public void Apply(SaveData d)
        {
            ResetForNewGame();
            foreach (QuestSave q in d.quests)
            {
                states[q.id] = (QuestState)q.state;
                progress[q.id] = q.progress;
            }
            foreach (TalkSave t in d.talks)
            {
                TalkMemory m = Memory(t.npc);
                m.lastDay = t.lastDay;
                if (t.seen != null) foreach (string s in t.seen) m.seen.Add(s);
            }
        }

        public void ResetForNewGame()
        {
            states.Clear();
            progress.Clear();
            talks.Clear();
            if (CurrencyManager.Instance != null) lastBells = CurrencyManager.Instance.CurrentBells;
        }

#if UNITY_EDITOR
        [ContextMenu("퀘스트 목록 새로 채우기")]
        public void FillQuests()
        {
            quests.Clear();
            foreach (string g in UnityEditor.AssetDatabase.FindAssets("t:QuestData", new[] { "Assets" }))
            {
                var q = UnityEditor.AssetDatabase.LoadAssetAtPath<QuestData>(UnityEditor.AssetDatabase.GUIDToAssetPath(g));
                if (q != null && !quests.Contains(q)) quests.Add(q);
            }
            UnityEditor.EditorUtility.SetDirty(this);
        }

        public int QuestCount => quests.Count;
#endif
    }
}
