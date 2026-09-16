namespace FarmingSystem.Core
{
    public enum Season
    {
        Spring,
        Summer,
        Autumn
    }

    public enum TimeBlock
    {
        LateNight,   // 00:00 ~ 05:59
        Morning,     // 06:00 ~ 09:59
        Midday,      // 10:00 ~ 12:59
        Afternoon,   // 13:00 ~ 17:59
        Evening,     // 18:00 ~ 20:59
        Night        // 21:00 ~ 23:59
    }
}