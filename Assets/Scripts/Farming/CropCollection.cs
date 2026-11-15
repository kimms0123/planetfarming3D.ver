using System;
using System.Collections.Generic;
using UnityEngine;

namespace FarmingSystem.Farming
{
    // 작물 도감 기록 (어떤 작물을 수확해 봤는지)
    //  - 처음 수확하면 등록 (FarmTile.Harvest에서 부름 → 리듬 수확·그냥 수확 둘 다)
    //  - 저장: SaveManager가 저장 파일에 같이 넣고 불러옴. 새 게임이면 비움
    //  - 작물 데이터 에셋 이름(Crop_a, Crop_old_A …)으로 기억 → 표시 이름을 바꿔도 기록 유지
    public static class CropCollection
    {
        private static readonly HashSet<string> registered = new HashSet<string>();

        /// <summary>새 작물이 도감에 등록될 때 (나중에 알림 띄울 때 사용)</summary>
        public static event Action<CropData> OnRegistered;

        public static int Count => registered.Count;

        public static bool IsRegistered(CropData crop) => crop != null && registered.Contains(crop.name);

        public static void Register(CropData crop)
        {
            if (crop == null || !registered.Add(crop.name)) return;
            Debug.Log($"[도감] 새로 등록: {crop.cropName} ({registered.Count}종)");
            OnRegistered?.Invoke(crop);
        }

        // ───── 저장 ─────
        public static List<string> ToList() => new List<string>(registered);

        public static void Load(List<string> names)
        {
            registered.Clear();
            if (names == null) return;
            foreach (string n in names) if (!string.IsNullOrEmpty(n)) registered.Add(n);
        }

        public static void Clear() => registered.Clear();
    }
}
