using System;
using System.Collections.Generic;
using UnityEngine;
using FarmingSystem.Core;

namespace FarmingSystem.World
{
    // 계절별 블록 묶음
    // 한 줄(Entry) = 모양 하나의 봄·여름·가을·겨울 프리팹 (예: 블록 → block / summer_block / fall_block / winter_block)
    // 계절 블록 교체기(SeasonalBlockSwapper)가 이 목록을 보고, 맵에 깔린 블록의 재질을 그 계절 프리팹의 재질로 바꿔 끼운다
    // 만드는 법: Project 창 우클릭 → Create → FarmingSystem → Seasonal Block Set
    [CreateAssetMenu(fileName = "SeasonalBlockSet", menuName = "FarmingSystem/Seasonal Block Set")]
    public class SeasonalBlockSet : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("알아보기 쉬운 이름 (예: 블록, 반블록, 경사 블록)")]
            public string name;
            [Tooltip("봄 프리팹 = 맵 생성기에 넣는 프리팹")]
            public GameObject spring;
            public GameObject summer;
            public GameObject autumn;
            public GameObject winter;

            // 그 계절 프리팹 (비어 있으면 봄 프리팹으로 대신)
            public GameObject Get(Season season)
            {
                GameObject p = season == Season.Summer ? summer
                             : season == Season.Autumn ? autumn
                             : season == Season.Winter ? winter
                             : spring;
                return p != null ? p : spring;
            }

            public IEnumerable<GameObject> All()
            {
                if (spring != null) yield return spring;
                if (summer != null) yield return summer;
                if (autumn != null) yield return autumn;
                if (winter != null) yield return winter;
            }
        }

        public List<Entry> entries = new List<Entry>();
    }
}