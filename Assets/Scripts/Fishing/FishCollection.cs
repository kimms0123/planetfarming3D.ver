using System;
using System.Collections.Generic;
using UnityEngine;

namespace FarmingSystem.Fishing
{
    // 물고기 도감 기록 (어떤 물고기를 잡아 봤는지) - 작물 도감(CropCollection)과 같은 방식
    //  - 처음 잡으면 등록 / 저장 파일에 같이 들어감 / 새 게임이면 비움
    public static class FishCollection
    {
        private static readonly HashSet<string> registered = new HashSet<string>();

        public static event Action<FishData> OnRegistered;
        public static int Count => registered.Count;
        public static bool IsRegistered(FishData fish) => fish != null && registered.Contains(fish.name);

        /// <summary>등록 (처음이면 true)</summary>
        public static bool Register(FishData fish)
        {
            if (fish == null || !registered.Add(fish.name)) return false;
            Debug.Log($"[도감] 물고기 새로 등록: {fish.itemName} ({registered.Count}종)");
            OnRegistered?.Invoke(fish);
            return true;
        }

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
