using System.Collections.Generic;
using UnityEngine;

namespace FarmingSystem.Rhythm
{
    // 노트 1개: 언제(곡 시작 후 몇 초) 어떤 방향키를 눌러야 하는지
    public struct HarvestNote
    {
        public double Time;
        public float Beat;
        public ArrowDirection Direction;
        public int Number;
    }

    // 한 번의 수확 리듬게임에 쓰일 전체 패턴
    public class HarvestPattern
    {
        public float Difficulty;
        public float Bpm;
        public double BeatLength;      // 1박자 길이(초)
        public int LeadInBeats;        // 첫 노트 전 카운트 박자 수
        public float ApproachTime;     // 접근 원이 판정 원까지 줄어드는 시간(초)
        public float PerfectWindow;    // ±초
        public float GoodWindow;       // ±초
        public int TotalBeats;         // 메트로놈이 울릴 전체 박자 수
        public readonly List<HarvestNote> Notes = new List<HarvestNote>();

        public double FirstNoteTime => Notes.Count > 0 ? Notes[0].Time : 0;
        public double LastNoteTime => Notes.Count > 0 ? Notes[Notes.Count - 1].Time : 0;
    }

    // 난이도 → 실제 노트 패턴을 만드는 생성기 (기획서 18~19장 기준)
    // 난이도가 오를수록: 노트 수 6 → 16개, 접근 원이 빨리 줄어듦, 판정 범위 좁아짐, 템포 빨라짐, 반박자 노트 등장
    // 방향: 작물에 패턴이 있으면 그 순서를 반복, 없으면 랜덤 (같은 방향 3연속은 피함)
    // 같은 seed면 항상 같은 패턴 (같은 날 같은 작물은 같은 곡)
    public static class HarvestPatternGenerator
    {
        public struct Settings
        {
            public float MinBpm, MaxBpm;
            public int MinNotes, MaxNotes;
            public float EasyApproach, HardApproach;
            public float EasyPerfect, HardPerfect;
            public float EasyGood, HardGood;
            public float Tempo;                 // 작물 템포 배율
            public ArrowDirection[] CropPattern; // 작물 고유 방향 패턴 (없으면 null)
            public int LeadInBeats;

            public static Settings Default => new Settings
            {
                MinBpm = 80f,
                MaxBpm = 120f,
                MinNotes = 6,
                MaxNotes = 16,
                EasyApproach = 1.4f,
                HardApproach = 0.8f,
                EasyPerfect = 0.08f,
                HardPerfect = 0.06f,
                EasyGood = 0.18f,
                HardGood = 0.12f,
                Tempo = 1f,
                LeadInBeats = 4
            };
        }

        public static HarvestPattern Generate(float difficulty, int seed, Settings s)
        {
            float d = Mathf.Clamp01(difficulty);
            var rng = new System.Random(seed);

            var p = new HarvestPattern
            {
                Difficulty = d,
                Bpm = Mathf.Round(Mathf.Lerp(s.MinBpm, s.MaxBpm, d) * Mathf.Clamp(s.Tempo, 0.5f, 2f)),
                LeadInBeats = Mathf.Max(1, s.LeadInBeats),
                ApproachTime = Mathf.Lerp(s.EasyApproach, s.HardApproach, d),
                PerfectWindow = Mathf.Lerp(s.EasyPerfect, s.HardPerfect, d),
                GoodWindow = Mathf.Lerp(s.EasyGood, s.HardGood, d)
            };
            p.BeatLength = 60.0 / p.Bpm;

            int noteCount = Mathf.RoundToInt(Mathf.Lerp(s.MinNotes, s.MaxNotes, d));
            // 반박자는 어려운 작물에서만 가끔 (방향까지 맞춰야 해서 너무 빠르면 피곤함)
            float halfBeatChance = Mathf.Lerp(0f, 0.25f, Mathf.InverseLerp(0.6f, 1f, d));
            float restChance = Mathf.Lerp(0.3f, 0.1f, d); // 2박 쉬는 확률 (쉬울수록 여유)

            bool hasPattern = s.CropPattern != null && s.CropPattern.Length > 0;
            float beat = p.LeadInBeats;
            bool lastWasHalf = false;

            for (int i = 0; i < noteCount; i++)
            {
                if (i > 0)
                {
                    double r = rng.NextDouble();
                    float gap;
                    if (!lastWasHalf && r < halfBeatChance) gap = 0.5f;
                    else if (r < halfBeatChance + restChance) gap = 2f;
                    else gap = 1f;
                    lastWasHalf = gap < 1f;
                    beat += gap;
                }

                ArrowDirection dir = hasPattern ? s.CropPattern[i % s.CropPattern.Length] : RandomDirection(rng, p.Notes);

                p.Notes.Add(new HarvestNote
                {
                    Beat = beat,
                    Time = beat * p.BeatLength,
                    Direction = dir,
                    Number = i + 1
                });
            }

            p.TotalBeats = Mathf.CeilToInt(beat) + 2;
            return p;
        }

        // 같은 방향이 3번 연속 나오지 않게 고름
        private static ArrowDirection RandomDirection(System.Random rng, List<HarvestNote> prev)
        {
            int n = prev.Count;
            for (int tries = 0; tries < 8; tries++)
            {
                var dir = (ArrowDirection)rng.Next(4);
                if (n >= 2 && prev[n - 1].Direction == dir && prev[n - 2].Direction == dir) continue;
                return dir;
            }
            return (ArrowDirection)rng.Next(4);
        }
    }
}