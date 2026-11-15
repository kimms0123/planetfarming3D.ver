using UnityEngine;
using UnityEngine.Events;

namespace FarmingSystem.Intro
{
    // 새 게임 오프닝 카메라 경로의 점 하나 (IntroCameraPath의 자식) - PV의 PVCamPoint와 같음
    // 이 오브젝트의 위치·방향 = 그 순간 카메라 위치·방향
    public class IntroCamPoint : MonoBehaviour
    {
        public enum EaseType { Smooth, Linear, EaseIn, EaseOut }

        [Tooltip("앞 점에서 이 점까지 걸리는 시간 (초). 첫 점은 무시 (바로 이동)")]
        public float moveTime = 3f;
        [Tooltip("이 점에 도착해서 멈춰 있는 시간 (초)")]
        public float holdTime = 0f;
        [Tooltip("이 점에서의 화각 (0 = 앞 점 그대로). 작을수록 확대")]
        public float fov = 0f;
        [Tooltip("이 점 근처에서 화면 흔들림 (0 = 없음, 0.05 = 약하게, 0.2 = 세게)")]
        public float shake = 0f;
        [Tooltip("움직임 느낌: Smooth = 천천히 출발·천천히 멈춤, Linear = 일정, EaseIn = 점점 빠르게, EaseOut = 점점 느리게")]
        public EaseType ease = EaseType.Smooth;
        [Tooltip("켜면: 멈춤 시간이 지난 뒤에도 우주선 착륙 + 캐릭터 등장이 끝날 때까지 이 점에서 기다렸다가 다음 점으로")]
        public bool waitForLanding = false;
        [Tooltip("착륙이 끝난 뒤 조금 더 머무는 시간 (초) - 캐릭터가 나온 걸 보여 주고 출발")]
        public float afterLandingHold = 0.8f;
        [Tooltip("이 점에 도착했을 때 실행 (예: 우주선 착륙 시작)")]
        public UnityEvent onArrive = new UnityEvent();
    }
}