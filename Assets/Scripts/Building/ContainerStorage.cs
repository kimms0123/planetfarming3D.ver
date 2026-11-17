using System.Collections.Generic;
using UnityEngine;
using FarmingSystem.Inventory;

namespace FarmingSystem.Building
{
    // 보관 상자·판매 포탈 안의 물건 (설치된 자리마다 따로)
    //  - 열쇠(key) = "S:" 또는 "P:" + 건물 데이터 이름 + "@" + 차지한 칸 중 가장 작은 좌표  (S = 상자, P = 포탈)
    //    → 저장했다 불러와도 같은 자리면 같은 내용
    //  - 저장: SaveManager가 저장 파일에 같이 넣고 불러옴. 새 게임이면 비움
    public static class ContainerStorage
    {
        public const int Size = 30; // 칸 수 (10칸 x 3줄)

        private static readonly Dictionary<string, InventorySlotData[]> store = new Dictionary<string, InventorySlotData[]>();

        public static IEnumerable<KeyValuePair<string, InventorySlotData[]>> All => store;

        public static bool IsPortalKey(string key) => key != null && key.StartsWith("P:");

        public static string KeyOf(PlacedBuilding b)
        {
            if (b == null || b.Data == null || b.Cells.Count == 0) return null;
            Vector2Int min = b.Cells[0];
            foreach (Vector2Int c in b.Cells)
                if (c.y < min.y || (c.y == min.y && c.x < min.x)) min = c;
            string kind = b.Data.function == BuildingFunction.SellPortal ? "P:" : "S:";
            return $"{kind}{b.Data.name}@{min.x},{min.y}";
        }

        /// <summary>그 자리의 칸들 (처음이면 빈 칸으로 만듦)</summary>
        public static InventorySlotData[] Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (!store.TryGetValue(key, out InventorySlotData[] slots))
            {
                slots = new InventorySlotData[Size];
                for (int i = 0; i < Size; i++) slots[i] = new InventorySlotData();
                store[key] = slots;
            }
            return slots;
        }

        public static void Clear() => store.Clear();

        /// <summary>
        /// 칸들에 넣기: 같은 아이템·같은 등급 칸부터 채우고 빈 칸으로. 넣은 개수를 돌려줌.
        /// 도구는 내구도를 그대로 가져감
        /// </summary>
        public static int PutInto(InventorySlotData[] slots, ItemData item, int amount, ItemQuality quality, int durability)
        {
            if (slots == null || item == null || amount <= 0) return 0;
            int left = amount;
            int max = Mathf.Max(1, item.maxStack);
            if (max > 1)
                foreach (InventorySlotData s in slots)
                {
                    if (left <= 0) break;
                    if (s.IsEmpty || s.item != item || s.quality != quality || s.quantity >= max) continue;
                    int add = Mathf.Min(left, max - s.quantity);
                    s.quantity += add;
                    left -= add;
                }
            foreach (InventorySlotData s in slots)
            {
                if (left <= 0) break;
                if (!s.IsEmpty) continue;
                int add = Mathf.Min(left, max);
                s.item = item;
                s.quantity = add;
                s.quality = quality;
                s.durability = durability;
                left -= add;
            }
            return amount - left;
        }
    }
}
