using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using FarmingSystem.Core;
using FarmingSystem.Player;

namespace FarmingSystem.World
{
    // 추후 콘텐츠 지역(광산, 해변 등)의 입구
    // - 잠겨 있는 동안: WhileLocked 그룹(투명 벽, 부서진 다리, 잔해)을 켜두고 AfterUnlock 그룹(수리된 다리)은 꺼둠
    // - 입구를 바라보고 E를 누르면 안내 문구를 띄움 (onLockedInteract 이벤트로 대사 UI에 연결 가능)
    // - Unlock()을 부르면 벽과 잔해가 사라지고 수리된 다리가 나타남 (나중에 퀘스트·업그레이드에서 호출)
    // IslandMapGenerator가 맵을 만들 때 자동으로 붙이고 설정한다
    public class LockedAreaGate : MonoBehaviour
    {
        [SerializeField] private string areaName = "잠긴 지역";
        [SerializeField, TextArea] private string lockedMessage = "아직 갈 수 없는 곳이에요.";
        [Tooltip("잠겨 있는 동안 켜둘 오브젝트 묶음 (벽, 부서진 다리, 잔해)")]
        [SerializeField] private GameObject lockedObjects;
        [Tooltip("해금 후 켤 오브젝트 묶음 (수리된 다리)")]
        [SerializeField] private GameObject unlockedObjects;
        [Tooltip("입구 위치 (이 근처에서 바라보고 E를 누르면 안내 문구)")]
        [SerializeField] private Transform gatePoint;
        [SerializeField] private float interactRange = 3f;
        [Tooltip("시선과 입구 방향 사이 허용 각도")]
        [SerializeField] private float maxLookAngle = 45f;
        [Tooltip("켜면 게임 시작부터 열려 있음 (테스트용)")]
        [SerializeField] private bool startUnlocked = false;

        // 안내 문구를 대사 UI 등에 보내고 싶을 때 연결
        public UnityEvent<string> onLockedInteract = new UnityEvent<string>();

        public string AreaName => areaName;
        public bool IsUnlocked { get; private set; }
        // 지금 E를 누르면 안내 문구가 나오는 상태인지 (십자선 강조 등에 사용)
        public bool CanInteract { get; private set; }

        private Transform player;

        // 생성기가 호출하는 초기 설정
        public void Configure(string name, string message, GameObject whileLocked, GameObject afterUnlock, Transform gate)
        {
            areaName = name;
            lockedMessage = message;
            lockedObjects = whileLocked;
            unlockedObjects = afterUnlock;
            gatePoint = gate;
        }

        private void Start()
        {
            PlayerController pc = FindAnyObjectByType<PlayerController>();
            if (pc != null) player = pc.transform;
            ApplyState(startUnlocked);
        }

        private void Update()
        {
            CanInteract = false;
            if (IsUnlocked || player == null || gatePoint == null || Keyboard.current == null) return;
            if (UIInputBlocker.IsBlocked) return;

            // 입구 근처에서 입구 쪽을 바라보고 있는지
            Vector3 toGate = gatePoint.position - player.position;
            toGate.y = 0f;
            if (toGate.magnitude > interactRange) return;

            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 view = cam.transform.forward;
                view.y = 0f;
                if (view.sqrMagnitude > 0.0001f && toGate.sqrMagnitude > 0.0001f && Vector3.Angle(view, toGate) > maxLookAngle) return;
            }

            CanInteract = true;
            if (Keyboard.current.eKey.wasPressedThisFrame)
            {
                Debug.Log($"[{areaName}] {lockedMessage}");
                onLockedInteract?.Invoke(lockedMessage);
            }
        }

        // 지역 열기 (벽·잔해 제거, 수리된 다리 표시)
        [ContextMenu("디버그: 잠금 해제")]
        public void Unlock() => ApplyState(true);

        // 다시 잠그기
        [ContextMenu("디버그: 다시 잠그기")]
        public void Lock() => ApplyState(false);

        private void ApplyState(bool unlocked)
        {
            IsUnlocked = unlocked;
            if (lockedObjects != null) lockedObjects.SetActive(!unlocked);
            if (unlockedObjects != null) unlockedObjects.SetActive(unlocked);
            if (unlocked) Debug.Log($"[{areaName}] 지역이 열렸습니다!");
        }
    }
}