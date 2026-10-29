using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using FarmingSystem.Core;
using FarmingSystem.Player;

namespace FarmingSystem.World
{
    // 산 절벽의 광산 입구
    // - 처음엔 잠겨 있음: '출입금지' 표지판(WhileLocked 묶음)이 입구 앞을 막고, 입구를 보고 E를 누르면 안내 문구
    // - 스토리 이벤트(상인의 광산 해제 등)에서 MineEntrance.UnlockMine()을 부르면 표지판이 사라지고 열림
    // - 열린 뒤 입구를 보고 E를 누르면 onEnter 이벤트 실행 + 씬 이름이 있으면 그 씬(광산 내부)으로 이동
    // IslandMapGenerator가 맵을 만들 때 자동으로 붙이고 설정한다
    public class MineEntrance : MonoBehaviour
    {
        public static MineEntrance Instance { get; private set; }

        [SerializeField, TextArea] private string lockedMessage = "출입금지\n무너질 위험이 있어 지금은 들어갈 수 없다.";
        [Tooltip("열린 뒤 E를 누르면 불러올 씬 이름 (비우면 onEnter 이벤트만)")]
        [SerializeField] private string mineSceneName = "";
        [Tooltip("잠겨 있는 동안 켜둘 오브젝트 묶음 (출입금지 표지판)")]
        [SerializeField] private GameObject lockedObjects;
        [Tooltip("열린 뒤 켤 오브젝트 묶음 (등불 등, 비어 있어도 됨)")]
        [SerializeField] private GameObject unlockedObjects;
        [Tooltip("입구 위치 (이 근처에서 입구를 바라보고 E)")]
        [SerializeField] private Transform entrancePoint;
        [SerializeField] private float interactRange = 3.5f;
        [Tooltip("시선과 입구 방향 사이 허용 각도")]
        [SerializeField] private float maxLookAngle = 50f;
        [Tooltip("켜면 게임 시작부터 열려 있음 (테스트용)")]
        [SerializeField] private bool startUnlocked = false;

        [Header("이벤트 (대사 UI·효과음·씬 이동 등을 연결)")]
        public UnityEvent<string> onLockedInteract = new UnityEvent<string>(); // 잠긴 입구를 조사했을 때 (안내 문구 전달)
        public UnityEvent onUnlocked = new UnityEvent();                      // 광산이 열린 순간
        public UnityEvent onEnter = new UnityEvent();                         // 열린 입구로 들어갈 때

        public bool IsUnlocked { get; private set; }
        // 지금 E를 누를 수 있는 상태인지 (십자선 강조·"[E] 조사하기" 안내 등에 사용)
        public bool CanInteract { get; private set; }

        private Transform player;

        // 생성기가 호출하는 초기 설정
        public void Configure(string message, string sceneName, GameObject whileLocked, GameObject afterUnlock, Transform point)
        {
            lockedMessage = message;
            mineSceneName = sceneName;
            lockedObjects = whileLocked;
            unlockedObjects = afterUnlock;
            entrancePoint = point;
        }

        // 스토리 이벤트에서 부르는 곳: 상인 대사가 끝날 때 등
        // 예) MineEntrance.UnlockMine();
        public static void UnlockMine()
        {
            if (Instance != null) Instance.Unlock();
            else Debug.LogWarning("[광산] 씬에 광산 입구가 없음 (맵 생성기의 Build Mine Entrance 확인)");
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) Debug.LogWarning("[광산] 광산 입구가 두 개 이상 있음");
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            PlayerController pc = FindAnyObjectByType<PlayerController>();
            if (pc != null) player = pc.transform;
            ApplyState(startUnlocked, false);
        }

        private void Update()
        {
            CanInteract = false;
            if (player == null || entrancePoint == null || Keyboard.current == null) return;
            if (UIInputBlocker.IsBlocked) return;

            // 입구 근처에서 입구 쪽을 바라보고 있는지
            Vector3 toEntrance = entrancePoint.position - player.position;
            toEntrance.y = 0f;
            if (toEntrance.magnitude > interactRange) return;

            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 view = cam.transform.forward;
                view.y = 0f;
                if (view.sqrMagnitude > 0.0001f && toEntrance.sqrMagnitude > 0.0001f && Vector3.Angle(view, toEntrance) > maxLookAngle) return;
            }

            CanInteract = true;
            if (!Keyboard.current.eKey.wasPressedThisFrame) return;

            if (!IsUnlocked)
            {
                Debug.Log($"[광산] {lockedMessage}");
                onLockedInteract?.Invoke(lockedMessage);
                return;
            }

            Debug.Log("[광산] 광산으로 들어갑니다");
            onEnter?.Invoke();
            if (!string.IsNullOrEmpty(mineSceneName))
            {
                if (Application.CanStreamedLevelBeLoaded(mineSceneName)) SceneManager.LoadScene(mineSceneName);
                else Debug.LogWarning($"[광산] 씬 '{mineSceneName}'을 불러올 수 없음 (File → Build Profiles의 씬 목록에 추가했는지 확인)");
            }
        }

        // 광산 열기 (표지판 제거)
        [ContextMenu("디버그: 광산 열기")]
        public void Unlock() => ApplyState(true, true);

        // 다시 잠그기
        [ContextMenu("디버그: 다시 잠그기")]
        public void Lock() => ApplyState(false, false);

        private void ApplyState(bool unlocked, bool announce)
        {
            bool wasUnlocked = IsUnlocked;
            IsUnlocked = unlocked;
            if (lockedObjects != null) lockedObjects.SetActive(!unlocked);
            if (unlockedObjects != null) unlockedObjects.SetActive(unlocked);
            if (unlocked && announce && !wasUnlocked)
            {
                Debug.Log("[광산] 광산이 열렸습니다!");
                onUnlocked?.Invoke();
            }
        }
    }
}