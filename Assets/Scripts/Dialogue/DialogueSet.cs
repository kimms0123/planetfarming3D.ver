using System;
using System.Collections.Generic;
using UnityEngine;
using FarmingSystem.Core;
using FarmingSystem.Weather;

namespace FarmingSystem.Dialogue
{
    // NPC 한 명의 대사 모음 (모동숲처럼 상황에 따라 다른 말)
    // Project 창 우클릭 → Create → FarmingSystem → Dialogue → Dialogue Set 으로 만들고, NPC의 NPCDialogue → Dialogue 칸에 넣기
    //
    // 말을 걸 때마다:
    //  1. 조건에 맞는 대사 묶음만 고름 (처음 만남·오늘 처음·아침/밤·날씨·계절·며칠차·퀘스트 상태·한 번만)
    //  2. 그중 Priority(우선순위)가 가장 높은 것들만 남김 → 퀘스트·첫 만남처럼 중요한 말이 먼저
    //  3. 그 안에서 Weight(비중)대로 무작위 (방금 한 말은 다른 후보가 있으면 피함)
    // 대사 안에 {item} {amount} {have} {reward}를 쓰면 묶음에 연결한 퀘스트 값으로 바뀜 (예: "{item} {amount}개만 줄래?")
    [CreateAssetMenu(fileName = "Dialogue", menuName = "FarmingSystem/Dialogue/Dialogue Set")]
    public class DialogueSet : ScriptableObject
    {
        [Tooltip("대화창에 뜨는 이름 (비우면 NPCDialogue의 Npc Name)")]
        public string npcName;
        public List<DialogueEntry> entries = new List<DialogueEntry>();

        /// <summary>지금 상황에 할 말 고르기 (없으면 null)</summary>
        public DialogueEntry Pick(string npcId, string lastEntryId)
        {
            int best = int.MinValue;
            var candidates = new List<DialogueEntry>();
            foreach (DialogueEntry e in entries)
            {
                if (e == null || !e.Matches(npcId)) continue;
                if (e.priority > best) { best = e.priority; candidates.Clear(); }
                if (e.priority == best) candidates.Add(e);
            }
            if (candidates.Count == 0) return null;
            if (candidates.Count > 1 && !string.IsNullOrEmpty(lastEntryId))
                candidates.RemoveAll(c => c.Key == lastEntryId); // 방금 한 말 피하기
            if (candidates.Count == 0) return null;

            float total = 0f;
            foreach (DialogueEntry c in candidates) total += Mathf.Max(0.0001f, c.weight);
            float r = UnityEngine.Random.value * total;
            foreach (DialogueEntry c in candidates)
            {
                r -= Mathf.Max(0.0001f, c.weight);
                if (r <= 0f) return c;
            }
            return candidates[candidates.Count - 1];
        }
    }

    public enum TalkCondition
    {
        [InspectorName("상관없음")] Any,
        [InspectorName("처음 만났을 때")] FirstMeeting,
        [InspectorName("오늘 처음 말 걸 때")] FirstToday,
        [InspectorName("오늘 이미 말 걸었을 때")] AlreadyToday,
    }

    public enum TimeCondition
    {
        [InspectorName("상관없음")] Any,
        [InspectorName("아침 (6~12시)")] Morning,
        [InspectorName("낮 (12~18시)")] Afternoon,
        [InspectorName("저녁 (18~22시)")] Evening,
        [InspectorName("밤 (22~6시)")] Night,
    }

    public enum WeatherCondition
    {
        [InspectorName("상관없음")] Any,
        [InspectorName("맑음")] Clear,
        [InspectorName("비")] Rain,
        [InspectorName("태풍")] Typhoon,
    }

    public enum SeasonCondition
    {
        [InspectorName("상관없음")] Any,
        [InspectorName("봄")] Spring,
        [InspectorName("여름")] Summer,
        [InspectorName("가을")] Autumn,
        [InspectorName("겨울")] Winter,
    }

    public enum QuestCondition
    {
        [InspectorName("상관없음")] Any,
        [InspectorName("아직 안 받음")] NotStarted,
        [InspectorName("진행 중 (아직 못 끝냄)")] InProgress,
        [InspectorName("끝낼 수 있음 (물건·돈 다 모음)")] ReadyToTurnIn,
        [InspectorName("완료함")] Completed,
    }

    public enum ChoiceAction
    {
        [InspectorName("없음")] None,
        [InspectorName("퀘스트 받기")] StartQuest,
        [InspectorName("퀘스트 끝내기 (물건 건네기)")] TurnInQuest,
    }

    [Serializable]
    public class DialogueChoice
    {
        public string text = "좋아요!";
        [Tooltip("이 선택지를 고르면 NPC가 하는 대답 (비우면 바로 끝)")]
        [TextArea(1, 2)] public string reply;
        [Tooltip("고르면 일어나는 일 (퀘스트는 이 묶음의 Quest 칸에 연결한 것)")]
        public ChoiceAction action;
    }

    [Serializable]
    public class DialogueEntry
    {
        [Tooltip("구분용 이름 (예: 첫만남, 퀘스트제안). '한 번만' 대사를 기억할 때도 이 이름을 씀 → 겹치지 않게")]
        public string id;
        [Tooltip("높을수록 먼저 (조건에 맞는 것 중 가장 높은 것들만 후보)")]
        public int priority;
        [Tooltip("같은 우선순위끼리 나올 비중")]
        [Min(0f)] public float weight = 1f;
        [Tooltip("한 번 나오면 다시는 안 나옴")]
        public bool onceOnly;

        [Header("조건")]
        public TalkCondition talk;
        public TimeCondition time;
        public WeatherCondition weather;
        public SeasonCondition season;
        [Tooltip("이 날 이후에만 (0 = 상관없음)")]
        public int minDay;
        [Tooltip("이 날까지만 (0 = 상관없음)")]
        public int maxDay;
        [Tooltip("퀘스트 상태로 고를 때 (대사 속 {item} {amount} {have} {reward}도 이 퀘스트 값)")]
        public QuestData quest;
        public QuestCondition questState;

        [Header("대사")]
        [TextArea(2, 3)] public string[] lines = new string[1];
        [Tooltip("마지막 대사에 붙는 선택지 (비우면 선택지 없이 끝)")]
        public List<DialogueChoice> choices = new List<DialogueChoice>();

        public string Key => !string.IsNullOrEmpty(id) ? id : (lines != null && lines.Length > 0 ? lines[0] : "");

        public bool Matches(string npcId)
        {
            if (lines == null || lines.Length == 0 || string.IsNullOrEmpty(lines[0])) return false;
            QuestManager qm = QuestManager.Instance;
            GameClock clock = GameClock.Instance;

            if (onceOnly && qm != null && qm.HasSeen(npcId, Key)) return false;

            switch (talk)
            {
                case TalkCondition.FirstMeeting: if (qm != null && qm.HasMet(npcId)) return false; break;
                case TalkCondition.FirstToday: if (qm == null || !qm.HasMet(npcId) || qm.TalkedToday(npcId)) return false; break;
                case TalkCondition.AlreadyToday: if (qm == null || !qm.TalkedToday(npcId)) return false; break;
            }

            if (clock != null)
            {
                int h = clock.CurrentHour;
                switch (time)
                {
                    case TimeCondition.Morning: if (h < 6 || h >= 12) return false; break;
                    case TimeCondition.Afternoon: if (h < 12 || h >= 18) return false; break;
                    case TimeCondition.Evening: if (h < 18 || h >= 22) return false; break;
                    case TimeCondition.Night: if (h >= 6 && h < 22) return false; break;
                }
                if (season != SeasonCondition.Any && (int)clock.CurrentSeason != (int)season - 1) return false;
                if (minDay > 0 && clock.CurrentDay < minDay) return false;
                if (maxDay > 0 && clock.CurrentDay > maxDay) return false;
            }

            if (weather != WeatherCondition.Any)
            {
                WeatherManager wm = WeatherManager.Instance;
                WeatherType w = wm != null ? wm.CurrentWeather : WeatherType.Clear;
                if ((int)w != (int)weather - 1) return false;
            }

            if (quest != null && questState != QuestCondition.Any)
            {
                if (qm == null) return false;
                QuestState s = qm.GetState(quest);
                bool ready = qm.IsReady(quest);
                switch (questState)
                {
                    case QuestCondition.NotStarted: if (s != QuestState.NotStarted || !qm.RequirementMet(quest)) return false; break;
                    case QuestCondition.InProgress: if (s != QuestState.Active || ready) return false; break;
                    case QuestCondition.ReadyToTurnIn: if (s != QuestState.Active || !ready) return false; break;
                    case QuestCondition.Completed: if (s != QuestState.Completed) return false; break;
                }
            }
            return true;
        }
    }
}
