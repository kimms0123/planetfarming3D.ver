namespace FarmingSystem.Inventory
{
    // 작물 등급 (수확 리듬게임 결과로 결정)
    // 순서 주의: 이미 저장된 아이템·에셋 값이 밀리지 않도록 새 값은 항상 맨 끝에 추가
    //   Normal  = 기본
    //   Good    = 상급
    //   Perfect = 최상급
    //   Trash   = 쓰레기 (판매가 1벨 고정)
    public enum ItemQuality
    {
        Normal,
        Good,
        Perfect,
        Trash
    }

    // 등급별 가격·이름·순위를 한 곳에서 관리
    public static class ItemQualityUtility
    {
        // 쓰레기 등급의 개당 판매가 (작물 종류와 상관없이 고정)
        public const float TrashUnitPrice = 1f;

        // 판매가 배율 (쓰레기는 배율 대신 TrashUnitPrice 고정가를 씀)
        public static float GetPriceMultiplier(ItemQuality quality)
        {
            switch (quality)
            {
                case ItemQuality.Good: return 1.3f;
                case ItemQuality.Perfect: return 1.6f;
                case ItemQuality.Trash: return 0f;
                default: return 1f;
            }
        }

        public static bool IsFixedPrice(ItemQuality quality) => quality == ItemQuality.Trash;

        // 화면에 보여줄 이름
        public static string GetDisplayName(ItemQuality quality)
        {
            switch (quality)
            {
                case ItemQuality.Perfect: return "최상급";
                case ItemQuality.Good: return "상급";
                case ItemQuality.Trash: return "쓰레기";
                default: return "기본";
            }
        }

        // 등급 순위 (0 = 쓰레기, 1 = 기본, 2 = 상급, 3 = 최상급). 비교·별 개수에 사용 (enum 숫자 순서와 다름)
        public static int GetRank(ItemQuality quality)
        {
            switch (quality)
            {
                case ItemQuality.Trash: return 0;
                case ItemQuality.Good: return 2;
                case ItemQuality.Perfect: return 3;
                default: return 1;
            }
        }

        public static ItemQuality FromRank(int rank)
        {
            switch (rank)
            {
                case 0: return ItemQuality.Trash;
                case 2: return ItemQuality.Good;
                case 3: return ItemQuality.Perfect;
                default: return ItemQuality.Normal;
            }
        }
    }
}