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
        [SerializeField] private int currentDay = 1; // 1 ~ 35 (기획서 5주 달력 기준)

        private float minuteAccumulator = 0f;
        private bool isSleeping = false;

        public int CurrentHour => currentHour;
        public int CurrentMinute => currentMinute;
        public int CurrentDay => currentDay;
        public Season CurrentSeason => GetSeasonForDay(currentDay);

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
            Debug.Log($"날짜가 지났습니다. 현재 Day {currentDay} ({CurrentSeason})");
            OnDayChanged?.Invoke(currentDay, CurrentSeason);

            if (CurrentSeason != prevSeason)
                OnSeasonChanged?.Invoke(CurrentSeason);
        }

        /// 기획서 캘린더 기준 계절 계산 (봄 1~14 / 여름 15~21 / 가을 22~35, 이후는 가을 유지)</summary>
        private Season GetSeasonForDay(int day)
        {
            if (day <= 14) return Season.Spring;
            if (day <= 21) return Season.Summer;
            return Season.Autumn;
        }

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