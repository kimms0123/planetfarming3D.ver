using System.Collections;
using UnityEngine;
using TMPro;
using FarmingSystem.Core;

namespace FarmingSystem.UI
{
    // 화면 왼쪽 위 시계 HUD (포코피아 느낌)
    //   10:07 AM          ← 시간
    //   17  화            ← 계절의 몇째 날 (크게) + 요일
    //       가을          ← 계절 (계절마다 글자색이 다름)
    // - GameClock의 시·분·날짜를 매 프레임 확인해서 바뀌었을 때만 글자를 고침 (잠자기·디버그 점프에도 바로 반영)
    // - 날짜가 바뀌면 큰 날짜 숫자가 톡 튀어 오르는 연출
    // - 한국어(오전 10:07 / 화 / 가을) ↔ 영어(10:07 AM / TUE / AUT) 전환 가능
    // - 상점 대화·인벤토리처럼 조작이 막히는 UI가 열리면 서서히 숨었다가 닫히면 다시 나타남
    // - 글자 오브젝트는 씬에 있으니 위치·크기·폰트는 씬에서 마음대로 바꾸면 됨 (Tools → 행성농사일지 → UI → 시계 HUD 만들기)
    public class ClockHUD : MonoBehaviour
    {
        public enum Language { Korean, English }

        [Header("글자 (씬에 있는 것 연결)")]
        [Tooltip("시간 (10:07 AM)")]
        [SerializeField] private TMP_Text timeText;
        [Tooltip("계절의 몇째 날 - 크게 (17)")]
        [SerializeField] private TMP_Text dayText;
        [Tooltip("요일 (화 / TUE)")]
        [SerializeField] private TMP_Text weekdayText;
        [Tooltip("계절 (가을 / AUT)")]
        [SerializeField] private TMP_Text seasonText;

        [Header("표시 방식")]
        [SerializeField] private Language language = Language.Korean;
        [Tooltip("켜면 24시간제 (14:30), 끄면 오전/오후 (오후 2:30)")]
        [SerializeField] private bool use24Hour = false;
        [Tooltip("분을 몇 분 단위로 보여줄지 (1 = 매분, 10 = 스타듀처럼 10분 단위)")]
        [SerializeField, Range(1, 30)] private int minuteStep = 1;

        [Header("계절 글자색 (봄 / 여름 / 가을 / 겨울)")]
        [SerializeField] private Color springColor = new Color32(0xFF, 0x9E, 0xC4, 0xFF);
        [SerializeField] private Color summerColor = new Color32(0x8F, 0xE0, 0x7A, 0xFF);
        [SerializeField] private Color autumnColor = new Color32(0xFF, 0xB0, 0x5E, 0xFF);
        [SerializeField] private Color winterColor = new Color32(0x9F, 0xD8, 0xFF, 0xFF);

        [Header("숨기기")]
        [Tooltip("상점 대화·인벤토리 등 다른 UI가 열려 조작이 막혀 있는 동안 시계를 서서히 숨김")]
        [SerializeField] private bool hideWhileUIOpen = true;
        [SerializeField, Range(1f, 20f)] private float fadeSpeed = 8f;

        [Header("날짜가 바뀔 때 연출")]
        [SerializeField] private bool popOnDayChange = true;
        [SerializeField, Range(1f, 1.6f)] private float popScale = 1.25f;
        [SerializeField, Range(0.1f, 1f)] private float popDuration = 0.35f;

        private static readonly string[] WeekdaysKo = { "월요일", "화요일", "수요일", "목요일", "금요일", "토요일", "일요일" };
        private static readonly string[] WeekdaysEn = { "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN" };
        private static readonly string[] SeasonsKo = { "봄", "여름", "가을", "겨울" };
        private static readonly string[] SeasonsEn = { "SPR", "SUM", "AUT", "WIN" };

        private int lastHour = -1, lastMinute = -1, lastDay = -1;
        private Season lastSeason;
        private Coroutine popRoutine;
        private CanvasGroup group;

        private void Awake()
        {
            group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false; // 시계는 클릭을 막지 않음
            group.interactable = false;
        }

        private void OnEnable()
        {
            lastHour = lastMinute = lastDay = -1; // 켜질 때 한 번 무조건 갱신
        }

        private void OnValidate()
        {
            // 인스펙터에서 언어·색을 바꾸면 바로 반영
            lastHour = lastMinute = lastDay = -1;
        }

        private void Update()
        {
            // 다른 UI가 열려 있으면 서서히 숨김
            if (group != null)
            {
                float target = hideWhileUIOpen && UIInputBlocker.IsBlocked ? 0f : 1f;
                group.alpha = Mathf.MoveTowards(group.alpha, target, fadeSpeed * Time.unscaledDeltaTime);
            }

            GameClock clock = GameClock.Instance;
            if (clock == null) return;

            int hour = clock.CurrentHour;
            int minute = clock.CurrentMinute - clock.CurrentMinute % Mathf.Max(1, minuteStep);
            int day = clock.CurrentDay;
            Season season = clock.CurrentSeason;
            if (hour == lastHour && minute == lastMinute && day == lastDay && season == lastSeason) return;

            bool dayChanged = lastDay != -1 && day != lastDay;
            lastHour = hour; lastMinute = minute; lastDay = day; lastSeason = season;

            if (timeText != null) timeText.text = FormatTime(hour, minute);
            if (dayText != null) dayText.text = clock.DayOfSeason.ToString();
            if (weekdayText != null)
            {
                int w = ((day - 1) % 7 + 7) % 7; // 1일 = 월요일
                weekdayText.text = language == Language.Korean ? WeekdaysKo[w] : WeekdaysEn[w];
            }
            if (seasonText != null)
            {
                int s = (int)season;
                seasonText.text = language == Language.Korean ? SeasonsKo[s] : SeasonsEn[s];
                seasonText.color = SeasonColor(season);
            }

            if (dayChanged && popOnDayChange && dayText != null && isActiveAndEnabled)
            {
                if (popRoutine != null) StopCoroutine(popRoutine);
                popRoutine = StartCoroutine(Pop(dayText.transform));
            }
        }

        private string FormatTime(int hour, int minute)
        {
            if (use24Hour) return $"{hour:00}:{minute:00}";
            int h12 = hour % 12 == 0 ? 12 : hour % 12;
            bool pm = hour >= 12;
            return language == Language.Korean
                ? $"{(pm ? "오후" : "오전")} {h12}:{minute:00}"
                : $"{h12}:{minute:00} {(pm ? "PM" : "AM")}";
        }

        private Color SeasonColor(Season s)
        {
            switch (s)
            {
                case Season.Spring: return springColor;
                case Season.Summer: return summerColor;
                case Season.Autumn: return autumnColor;
                default: return winterColor;
            }
        }

        // 톡 커졌다가 원래 크기로 (게임 시간이 멈춰도 실제 시간으로)
        private IEnumerator Pop(Transform t)
        {
            float elapsed = 0f;
            while (elapsed < popDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(elapsed / popDuration);
                float s = 1f + (popScale - 1f) * Mathf.Sin(k * Mathf.PI);
                t.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            t.localScale = Vector3.one;
            popRoutine = null;
        }
    }
}