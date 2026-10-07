using FarmingSystem.Core;

namespace FarmingSystem.Farming
{
    /// <summary>
    /// 현재 계절을 읽는 곳을 한 군데로 모은 도우미.
    /// GameClock의 계절 프로퍼티 이름이 다르면 이 파일의 한 줄만 고치면 된다.
    /// </summary>
    public static class SeasonUtil
    {
        public static bool TryGetCurrentSeason(out Season season)
        {
            season = default;
            if (GameClock.Instance == null) return false;

            season = GameClock.Instance.CurrentSeason; // ← 컴파일 에러가 나면 GameClock의 실제 계절 프로퍼티 이름으로 변경
            return true;
        }
    }
}