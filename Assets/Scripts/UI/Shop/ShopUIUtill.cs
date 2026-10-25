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

        // 아이템 이름 뒤에 붙는 등급 표시 (기본 등급은 표시 안 함)
        public static string QualityMark(ItemQuality quality)
        {
            switch (quality)
            {
                case ItemQuality.Good: return " <color=#4FA3E0>[상급]</color>";
                case ItemQuality.Perfect: return " <color=#E8A317>[최상급]</color>";
                case ItemQuality.Trash: return " <color=#8A8A8A>[쓰레기]</color>";
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