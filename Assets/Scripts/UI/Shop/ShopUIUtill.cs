using UnityEngine.InputSystem;
using FarmingSystem.Inventory;

namespace FarmingSystem.UI.Shop
{
    public static class ShopUIUtil
    {
        public const string Currency = "벨";

        public static string Money(long value) => $"{value:N0}{Currency}";

        /// <summary>소수 단가 표시: 1.3 -> "1.3벨", 2.0 -> "2벨"</summary>
        public static string UnitPrice(float value) => $"{value:0.#}{Currency}";

        public static string QualityMark(ItemQuality quality)
        {
            switch (quality)
            {
                case ItemQuality.Good: return " ★";
                case ItemQuality.Perfect: return " ★★";
                default: return "";
            }
        }

        /// <summary>Shift를 누르고 있으면 10개 단위</summary>
        public static int Step()
        {
            return Keyboard.current != null && Keyboard.current.shiftKey.isPressed ? 10 : 1;
        }
    }
}