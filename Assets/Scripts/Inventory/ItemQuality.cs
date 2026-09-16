namespace FarmingSystem.Inventory
{
    /// <summary>
    /// 수확물의 품질 등급. 지금은 FarmTile.Harvest()에서 임시로 랜덤 판정하지만,
    /// 나중에 수확 리듬게임 시스템이 완성되면 그 결과(Perfect 비율 등)로 대체될 값이다.
    /// </summary>
    public enum ItemQuality
    {
        Normal,
        Good,
        Perfect
    }

    public static class ItemQualityUtility
    {
        /// <summary>등급별 판매가 배율</summary>
        public static float GetPriceMultiplier(ItemQuality quality)
        {
            switch (quality)
            {
                case ItemQuality.Normal: return 1.0f;
                case ItemQuality.Good: return 1.3f;
                case ItemQuality.Perfect: return 1.6f;
                default: return 1.0f;
            }
        }

        public static string GetDisplayName(ItemQuality quality)
        {
            switch (quality)
            {
                case ItemQuality.Normal: return "일반";
                case ItemQuality.Good: return "좋음";
                case ItemQuality.Perfect: return "완벽";
                default: return quality.ToString();
            }
        }
    }
}