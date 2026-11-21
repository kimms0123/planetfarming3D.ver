using System;
using System.Collections.Generic;
using UnityEngine;

namespace FarmingSystem.Audio
{
    // 게임의 모든 소리 목록 (Assets/Resources/SoundLibrary.asset 하나만 있으면 됨)
    // 메뉴: Tools → 행성농사일지 → 소리 → 소리 설정하기
    //  → 프로젝트의 SFX_ / BGM_ / AMB_ 로 시작하는 오디오 파일을 이름으로 찾아 자동으로 채움
    //  → 나중에 진짜 소리로 바꿀 땐 같은 이름의 파일로 덮어쓰고 메뉴를 다시 누르면 됨
    // 크기·음높이 흔들림은 여기서 직접 조절 (메뉴를 다시 눌러도 조절한 값은 유지)
    [CreateAssetMenu(fileName = "SoundLibrary", menuName = "FarmingSystem/Audio/Sound Library")]
    public class SoundLibrary : ScriptableObject
    {
        [Serializable]
        public class Sfx
        {
            [HideInInspector] public string label;
            public SoundId id;
            [Tooltip("여러 개면 무작위로 하나 (발걸음처럼 매번 조금 다르게)")]
            public AudioClip[] clips;
            [Range(0f, 1f)] public float volume = 0.8f;
            [Tooltip("재생할 때마다 음높이를 이만큼 무작위로 흔듦 (0.05 = ±5%)")]
            [Range(0f, 0.3f)] public float pitchJitter = 0.05f;
            [Tooltip("같은 소리를 이 시간(초) 안에 또 내지 않음 (3x3 도구처럼 한 번에 여러 칸일 때 겹침 방지)")]
            public float cooldown = 0.05f;
        }

        [Header("배경음")]
        public AudioClip title;     // 시작 화면 (반복)
        public AudioClip intro;     // 새 게임 오프닝 (한 번)
        public AudioClip day;       // 기본
        [Tooltip("비어 있으면 밤에도 기본 배경음")]
        public AudioClip night;
        public AudioClip rain;
        public AudioClip typhoon;
        [Range(0f, 1f)] public float bgmVolume = 0.55f;
        [Tooltip("배경음이 바뀔 때 겹쳐서 넘어가는 시간 (초)")]
        public float crossfade = 2f;

        [Header("환경음 (비·바람·천둥은 WeatherVisuals에 들어감)")]
        public AudioClip birds;     // 맑은 낮
        public AudioClip insects;   // 맑은 밤 (겨울 제외)
        public AudioClip leaves;    // 태풍 때 나무 흔들리는 소리
        [Range(0f, 1f)] public float ambienceVolume = 0.5f;

        [Header("효과음")]
        public List<Sfx> sfx = new List<Sfx>();

        private Dictionary<SoundId, Sfx> map;

        public Sfx Get(SoundId id)
        {
            if (map == null || map.Count != sfx.Count)
            {
                map = new Dictionary<SoundId, Sfx>();
                foreach (Sfx s in sfx) if (s != null) map[s.id] = s;
            }
            return map.TryGetValue(id, out Sfx e) ? e : null;
        }

        private void OnValidate()
        {
            map = null;
            foreach (Sfx s in sfx) if (s != null) s.label = s.id.ToString(); // 인스펙터 목록에 이름 보이게
        }
    }
}
