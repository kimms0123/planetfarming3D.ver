#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using FarmingSystem.Core;

namespace FarmingSystem.EditorTools
{
    /// <summary>
    /// GameClock 인스펙터에 테스트용 시간·계절 조작 버튼을 추가한다. (플레이 중에만 동작)
    /// </summary>
    [CustomEditor(typeof(GameClock))]
    public class GameClockEditor : Editor
    {
        private int targetDay = 1;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("디버그: 시간 / 계절 조작", EditorStyles.boldLabel);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("플레이 중에만 사용할 수 있어요.", MessageType.Info);
                return;
            }

            GameClock clock = (GameClock)target;

            EditorGUILayout.HelpBox(
                $"{clock.CurrentYear}년차 {GameClock.GetSeasonDisplayName(clock.CurrentSeason)} {clock.DayOfSeason}일  ·  {clock.CurrentHour:00}:{clock.CurrentMinute:00}  (누적 Day {clock.CurrentDay})",
                MessageType.None);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+1시간")) clock.AdvanceMinutes(60);
            if (GUILayout.Button("다음 날 (취침)")) clock.Sleep();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("다가올 계절의 첫날로 이동", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("봄")) clock.DebugJumpToSeason(Season.Spring);
            if (GUILayout.Button("여름")) clock.DebugJumpToSeason(Season.Summer);
            if (GUILayout.Button("가을")) clock.DebugJumpToSeason(Season.Autumn);
            if (GUILayout.Button("겨울")) clock.DebugJumpToSeason(Season.Winter);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            targetDay = EditorGUILayout.IntField("누적 Day로 이동", targetDay);
            if (GUILayout.Button("이동", GUILayout.Width(60))) clock.DebugJumpToDay(targetDay);
            EditorGUILayout.EndHorizontal();

            // 플레이 중 표시가 계속 갱신되도록
            Repaint();
        }
    }
}
#endif