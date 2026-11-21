using System.Collections.Generic;
using UnityEngine;
using FarmingSystem.Inventory;
using FarmingSystem.Player;
using FarmingSystem.Audio;

namespace FarmingSystem.Farming
{
    // 도구 범위·내구도 (FarmActionController가 밭 갈기·물 주기·수확 때 부름)
    //  - 든 도구의 Range에 따라 가리킨 칸 + 주변 칸까지 같이 처리
    //      1칸      : 가리킨 칸만
    //      앞으로 3칸: 가리킨 칸 + 캐릭터가 바라보는 쪽으로 2칸 더
    //      3x3      : 가리킨 칸을 가운데로 9칸
    //  - 한 번 쓸 때마다 내구도 1 (칸 수와 상관없이). 0이 되면 부서져서 사라짐
    //  - 낫: 낫을 들고 수확하면 리듬게임 한 번의 결과(등급·개수)로 범위 안의 '같은 작물'까지 같이 수확
    public static class ToolUse
    {
        /// <summary>밭 갈기 (가리킨 칸이 갈리면 true). 주변 칸은 갈 수 있는 맨땅만</summary>
        public static bool Till(FarmTile center)
        {
            if (center == null || !center.TryTill()) return false;
            GameAudio.PlayAt(SoundId.Till, center.transform.position);
            foreach (FarmTile t in Around(center, ToolType.Hoe))
                if (t.State == TileState.Natural && t.CurrentCrop == null && IsClear(t)) t.TryTill();
            Spend();
            return true;
        }

        /// <summary>물 주기 (가리킨 칸에 물이 들어가면 true). 주변 칸은 갈아 놓고 아직 물을 안 준 칸만</summary>
        public static bool Water(FarmTile center)
        {
            if (center == null || !center.Water()) return false;
            GameAudio.PlayAt(SoundId.Water, center.transform.position);
            foreach (FarmTile t in Around(center, ToolType.WateringCan))
                if (t.State == TileState.Tilled && !t.IsWatered) t.Water();
            Spend();
            return true;
        }

        /// <summary>
        /// 낫: 방금 수확한 칸 주변의 같은 작물(다 자란 것)을 같은 등급·같은 개수로 같이 수확. 수확한 칸 수를 돌려줌.
        /// 가방에 자리가 모자라면 거기서 멈춤. 낫을 들고 있지 않으면 아무것도 안 함
        /// </summary>
        public static int HarvestAround(FarmTile center, CropData crop, ItemQuality quality, int amountPerTile)
        {
            InventoryManager inv = InventoryManager.Instance;
            if (inv == null || center == null || crop == null || inv.CurrentTool != ToolType.Sickle) return 0;
            int count = 0;
            foreach (FarmTile t in Around(center, ToolType.Sickle))
            {
                if (t.CurrentCrop == null || !t.CurrentCrop.IsReadyToHarvest || t.CurrentCrop.Data != crop) continue;
                if (inv.GetAddableAmount(crop, quality) < amountPerTile) break; // 가방이 가득 참
                if (t.Harvest(quality) == null) continue;
                inv.AddItem(crop, amountPerTile, quality);
                count++;
            }
            Spend(); // 낫을 들고 수확하면 주변에 작물이 없어도 한 번 쓴 것
            if (count > 0) Debug.Log($"[낫] 주변 {count}칸의 {crop.cropName}도 같이 수확 (칸마다 {amountPerTile}개)");
            return count;
        }

        private static void Spend()
        {
            if (InventoryManager.Instance != null) InventoryManager.Instance.UseCurrentTool();
        }

        // 칸 좌표 → 칸 (씬의 모든 밭 칸. 처음 쓸 때 만들고, 칸이 사라졌으면 다시 만듦)
        private static readonly Dictionary<Vector2Int, FarmTile> tileAt = new Dictionary<Vector2Int, FarmTile>();

        private static bool TryGetTile(Vector2Int pos, out FarmTile tile)
        {
            if (tileAt.Count == 0 || (tileAt.TryGetValue(pos, out tile) && tile == null))
            {
                tileAt.Clear();
                foreach (FarmTile t in Object.FindObjectsByType<FarmTile>(FindObjectsInactive.Exclude)) tileAt[t.GridPosition] = t;
            }
            return tileAt.TryGetValue(pos, out tile) && tile != null;
        }

        // 가리킨 칸을 뺀 범위 안의 칸들 (든 도구가 그 종류일 때만)
        private static List<FarmTile> Around(FarmTile center, ToolType type)
        {
            var list = new List<FarmTile>();
            InventoryManager inv = InventoryManager.Instance;
            ToolData tool = inv != null ? inv.CurrentToolData : null;
            if (tool == null || tool.toolType != type || tool.range == ToolRange.Single) return list;

            Vector2Int c = center.GridPosition;
            if (tool.range == ToolRange.Square3)
            {
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        if ((dx != 0 || dy != 0) && TryGetTile(c + new Vector2Int(dx, dy), out FarmTile t)) list.Add(t);
                return list;
            }

            // 앞으로 3칸: 캐릭터 → 가리킨 칸 방향 (가로·세로 중 더 큰 쪽). 칸 좌표 x = 월드 X, y = 월드 Z
            Vector2Int dir = Vector2Int.up;
            PlayerController pc = Object.FindAnyObjectByType<PlayerController>();
            if (pc != null)
            {
                Vector3 d = center.Center - pc.transform.position;
                d.y = 0f;
                if (d.magnitude < 0.3f) d = pc.transform.forward; // 발밑 칸이면 바라보는 방향
                dir = Mathf.Abs(d.x) >= Mathf.Abs(d.z) ? new Vector2Int(d.x > 0f ? 1 : -1, 0) : new Vector2Int(0, d.z > 0f ? 1 : -1);
            }
            for (int i = 1; i <= 2; i++)
                if (TryGetTile(c + dir * i, out FarmTile t)) list.Add(t);
            return list;
        }

        // 그 칸 위에 나무·바위·건물이 없는지 (FarmActionController와 같은 검사)
        private static bool IsClear(FarmTile tile)
        {
            int mask = LayerMask.GetMask("FarmingBlocker");
            if (mask == 0) return true;
            Bounds b = tile.GetGroundBounds();
            Vector3 center = new Vector3(b.center.x, b.max.y + 0.6f, b.center.z);
            Vector3 half = new Vector3(b.extents.x * 0.8f, 0.5f, b.extents.z * 0.8f);
            return !Physics.CheckBox(center, half, Quaternion.identity, mask, QueryTriggerInteraction.Ignore);
        }
    }
}