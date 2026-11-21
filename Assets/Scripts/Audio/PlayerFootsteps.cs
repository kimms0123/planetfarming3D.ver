using UnityEngine;
using FarmingSystem.Core;
using FarmingSystem.Player;
using FarmingSystem.Save;

namespace FarmingSystem.Audio
{
    // 발걸음 소리 - GameAudio가 플레이어에 저절로 붙임 (직접 붙여도 됨)
    // 땅에 붙어서 걸은 거리가 보폭만큼 쌓일 때마다 한 번 (빨리 걸으면 자주)
    // 봄·여름·가을 = 풀밭 / 겨울 = 눈 밟는 소리
    // 플레이어 조작이 꺼져 있을 때(낚시·잠·오프닝 등)나 UI가 열려 있을 땐 소리 없음
    public class PlayerFootsteps : MonoBehaviour
    {
        [Tooltip("한 걸음 거리 (m) - 걷는 애니메이션 발 박자에 맞게 조절")]
        public float stride = 1.5f;
        [Range(0f, 1f)] public float volume = 1f;

        private PlayerController controller;
        private CharacterController body;
        private Vector3 lastPos;
        private float walked;

        private void Awake()
        {
            controller = GetComponent<PlayerController>();
            body = GetComponent<CharacterController>();
            lastPos = transform.position;
        }

        private void Update()
        {
            Vector3 pos = transform.position;
            Vector3 delta = pos - lastPos;
            lastPos = pos;
            delta.y = 0f;
            float dist = delta.magnitude;

            bool canStep = (controller == null || controller.enabled)
                           && (body == null || body.isGrounded)
                           && !TitleScreen.IsShowing && !UIInputBlocker.IsBlocked
                           && dist < 2f; // 순간이동(불러오기·자리 옮김)은 무시
            if (!canStep || Time.deltaTime <= 0f) { walked = stride * 0.6f; return; }

            if (dist / Time.deltaTime < 0.3f) { walked = stride * 0.6f; return; } // 멈추면 다음 첫 걸음이 바로 나게
            walked += dist;
            if (walked < stride) return;
            walked -= stride;

            bool winter = GameClock.Instance != null && GameClock.Instance.CurrentSeason == Season.Winter;
            GameAudio.PlayAt(winter ? SoundId.FootstepSnow : SoundId.FootstepGrass, pos, volume);
        }
    }
}
