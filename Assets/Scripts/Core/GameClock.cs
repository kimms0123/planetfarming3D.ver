using System;
using UnityEngine;

namespace FarmingSystem.Core
{
    /// 게임 전체 시간의 기준이 되는 싱글톤 시계.
    /// - 24시간제 / 하루 = 게임 내 실시간 흐름
    /// - 밭갈기(30분), 물주기(20분) 등 행동은 AdvanceMinutes()로 시간을 소모시킨다.
    /// - 자정을 넘기면 OnDayChanged 이벤트를 발행 -> FarmTile이 이를 구독해 작물 성장 처리
    public class GameClock : MonoBehaviour
    {
        public static GameClock Instance { get; private set; }

        [Header("시간 흐름 설정")]
        [Tooltip("실시간 1초당 흐르는 게임 내 '분'. 예: 1 이면 24시간이 실시간 24분")]
        [SerializeField] private float gameMinutesPerRealSecond = 1f;

        [Header("현재 상태 (읽기 전용, 디버그용)")]
        [SerializeField] private int currentHour = 6;
        [SerializeField] private int currentMinute = 0;
        [Tooltip("게임 시작부터 센 누적 날짜 (1부터). 계절·연도·날짜는 이 값으로 계산")]
        [SerializeField] private int currentDay = 1;

        [Header("달력")]
        [Tooltip("한 계절(한 달)의 길이. 봄→여름→가을→겨울→다시 봄으로 순환")]
        [SerializeField] private int daysPerSeason = 27;

        private float minuteAccumulator = 0f;
        private bool isSleeping = false;

        public int CurrentHour => currentHour;
        public int CurrentMinute => currentMinute;
        public int CurrentDay => currentDay;
        public Season CurrentSeason => GetSeasonForDay(currentDay);
        public int DaysPerSeason => daysPerSeason;
        /// <summary>몇 번째 해인지 (1년 = 4계절)</summary>
        public int CurrentYear => (currentDay - 1) / (daysPerSeason * SeasonCount) + 1;
        /// <summary>이번 계절의 며칠째인지 (1 ~ daysPerSeason)</summary>
        public int DayOfSeason => (currentDay - 1) % daysPerSeason + 1;

        private const int SeasonCount = 4;

        /// 매 분 갱신 (UI 시계 갱신용)</summary>
        public event Action<int, int> OnMinuteChanged;
        /// 하루가 바뀔 때 (day, season) 발행 -> 작물 성장 트리거</summary>
        public event Action<int, Season> OnDayChanged;
        /// 계절이 바뀔 때 발행</summary>
        public event Action<Season> OnSeasonChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Update()
        {
            if (isSleeping) return;

            minuteAccumulator += gameMinutesPerRealSecond * Time.deltaTime;
            if (minuteAccumulator >= 1f)
            {
                int minutesToAdd = Mathf.FloorToInt(minuteAccumulator);
                minuteAccumulator -= minutesToAdd;
                AdvanceMinutes(minutesToAdd);
            }
        }

        /// 특정 행동(밭갈기 30분, 물주기 20분 등)에 대응해 시간을 즉시 소모시킨다.
        /// 자정을 넘기면 내부적으로 날짜 변경 처리까지 수행한다.
        public void AdvanceMinutes(int minutes)
        {
            if (minutes <= 0) return;

            Season prevSeason = CurrentSeason;
            int totalMinutes = currentHour * 60 + currentMinute + minutes;

            while (totalMinutes >= 24 * 60)
            {
                totalMinutes -= 24 * 60;
                AdvanceDay();
            }

            currentHour = totalMinutes / 60;
            currentMinute = totalMinutes % 60;
            OnMinuteChanged?.Invoke(currentHour, currentMinute);

            Season newSeason = CurrentSeason;
            if (newSeason != prevSeason)
                OnSeasonChanged?.Invoke(newSeason);
        }

        /// 취침: 다음날 06:00으로 즉시 점프. 기획서의 "취침(수면)" 행동 대응.
        public void Sleep()
        {
            AdvanceDay();
            currentHour = 6;
            currentMinute = 0;
            OnMinuteChanged?.Invoke(currentHour, currentMinute);
        }

        private void AdvanceDay()
        {
            Season prevSeason = CurrentSeason;
            currentDay++;
            Debug.Log($"날짜가 지났습니다. {CurrentYear}년차 {GetSeasonDisplayName(CurrentSeason)} {DayOfSeason}일 (누적 Day {currentDay})");
            OnDayChanged?.Invoke(currentDay, CurrentSeason);

            if (CurrentSeason != prevSeason)
                OnSeasonChanged?.Invoke(CurrentSeason);
        }

        /// <summary>누적 날짜 → 계절. 27일마다 봄 → 여름 → 가을 → 겨울 → 다시 봄</summary>
        private Season GetSeasonForDay(int day)
        {
            int seasonIndex = ((day - 1) / daysPerSeason) % SeasonCount;
            switch (seasonIndex)
            {
                case 0: return Season.Spring;
                case 1: return Season.Summer;
                case 2: return Season.Autumn;
                default: return Season.Winter;
            }
        }

        private static int SeasonToIndex(Season season)
        {
            switch (season)
            {
                case Season.Summer: return 1;
                case Season.Autumn: return 2;
                case Season.Winter: return 3;
                default: return 0;
            }
        }

        /// <summary>오늘 이후(오늘 포함 안 함)로 가장 가까운, 해당 계절이 시작되는 날</summary>
        public int GetNextSeasonStartDay(Season season)
        {
            int yearLength = daysPerSeason * SeasonCount;
            int yearStart = (CurrentYear - 1) * yearLength + 1;
            int start = yearStart + SeasonToIndex(season) * daysPerSeason;
            while (start <= currentDay) start += yearLength;
            return start;
        }

        public static string GetSeasonDisplayName(Season season)
        {
            switch (season)
            {
                case Season.Spring: return "봄";
                case Season.Summer: return "여름";
                case Season.Autumn: return "가을";
                case Season.Winter: return "겨울";
                default: return season.ToString();
            }
        }

        // ---------- 디버그 (GameClockEditor의 버튼에서 호출) ----------

        /// <summary>
        /// 디버그: 지정한 날의 06:00으로 이동.
        /// 앞으로 갈 때는 하루씩 넘겨서 매일의 이벤트(작물 성장·시듦, 상인 방문)가 실제처럼 처리되고,
        /// 뒤로 갈 때는 그 날로 바로 옮긴 뒤 이벤트를 한 번만 보낸다 (계절 판정용).
        /// </summary>
        public void DebugJumpToDay(int targetDay)
        {
            targetDay = Mathf.Max(1, targetDay);
            Season prevSeason = CurrentSeason;

            if (targetDay > currentDay)
            {
                while (currentDay < targetDay)
                    AdvanceDay();
            }
            else
            {
                currentDay = targetDay;
                Debug.Log($"[디버그] Day {currentDay} ({CurrentSeason})로 되돌림");
                OnDayChanged?.Invoke(currentDay, CurrentSeason);
                if (CurrentSeason != prevSeason)
                    OnSeasonChanged?.Invoke(CurrentSeason);
            }

            currentHour = 6;
            currentMinute = 0;
            minuteAccumulator = 0f;
            OnMinuteChanged?.Invoke(currentHour, currentMinute);
            Debug.Log($"[디버그] Day {currentDay} ({CurrentSeason}) 06:00으로 이동");
        }

        /// <summary>디버그: 앞으로 다가올 해당 계절의 첫날로 이동 (지나가는 날들은 실제처럼 처리)</summary>
        public void DebugJumpToSeason(Season season) => DebugJumpToDay(GetNextSeasonStartDay(season));

        public TimeBlock GetCurrentTimeBlock()
        {
            if (currentHour < 6) return TimeBlock.LateNight;
            if (currentHour < 10) return TimeBlock.Morning;
            if (currentHour < 13) return TimeBlock.Midday;
            if (currentHour < 18) return TimeBlock.Afternoon;
            if (currentHour < 21) return TimeBlock.Evening;
            return TimeBlock.Night;
        }
    }
}