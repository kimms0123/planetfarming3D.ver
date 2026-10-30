using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using FarmingSystem.Core;
using FarmingSystem.Inventory;

namespace FarmingSystem.Player
{
    public enum CameraControlMode
    {
        FreeLook,  // 마우스를 움직이면 바로 회전 (커서 숨김)
        RightDrag  // 우클릭을 누른 채 드래그할 때만 회전 (커서 항상 보임)
    }

    //
    // 3인칭 카메라 1개로 플레이어 주변을 도는 카메라 리그.
    //
    // 구조:  CameraRig (이 스크립트)
    //         └─ CameraPivot   ← 플레이어 위치(+높이)를 따라가고, 좌우(yaw)·상하(pitch)로 회전
    //             └─ Main Camera ← Pivot 뒤쪽 일정 거리에 고정, 항상 Pivot을 바라봄
    //
    // Pivot은 플레이어의 "위치만" 따라가고 회전은 따라가지 않는다.
    // (플레이어 자식으로 두면 캐릭터가 방향을 틀 때마다 카메라도 같이 휙 돌아가기 때문)
    //
    // - 마우스 좌우 → Pivot 좌우 회전 (360도)
    // - 마우스 상하 → Pivot 상하 각도 (최소~최대 각도로 제한)
    // - 각도와 위치 모두 부드러운 보간을 적용하고, 거리는 항상 일정하게 유지
    // - 벽·땅·물체에 카메라가 파묻히면 그 앞까지 당겨오고, 올려다볼 때는 캐릭터 쪽으로 다가옴
    // - 상점·인벤토리가 열리거나 Alt를 누르면 커서가 나타나고 회전이 멈춤
    // - 강조(Focus): 수확 리듬게임처럼 특정 지점을 잠깐 강조할 때, 바라보는 점을 그쪽으로 옮기고 살짝 다가감
    //   게임 시간이 멈춰 있어도(Time.timeScale = 0) 카메라는 실제 시간으로 움직임
    //
    public class ThirdPersonCameraRig : MonoBehaviour
    {
        public static ThirdPersonCameraRig Instance { get; private set; }

        [Header("구성")]
        [SerializeField] private Transform target;        // Player
        [SerializeField] private Transform pivot;         // CameraPivot
        [SerializeField] private Transform cameraTransform; // Main Camera (Pivot의 자식)
        [SerializeField] private CameraControlMode controlMode = CameraControlMode.FreeLook;

        [Header("거리 / 높이")]
        [Tooltip("Pivot(바라보는 점)과 카메라 사이 거리")]
        [SerializeField] private float distance = 8f;
        [Tooltip("플레이어 발밑에서 Pivot까지의 높이 (카메라가 바라보는 지점)")]
        [SerializeField] private float pivotHeight = 1.2f;

        [Header("각도")]
        [Tooltip("시작 좌우 각도 (0 = 월드 +Z 방향을 바라봄)")]
        [SerializeField] private float yaw = 0f;
        [Tooltip("시작 상하 각도. 0 = 수평, 90 = 바로 위에서 내려다봄, 음수 = 아래에서 올려다봄(하늘 보기)")]
        [SerializeField] private float pitch = 30f;
        [Tooltip("올려다볼 수 있는 한계 (음수일수록 하늘을 더 많이 봄)")]
        [SerializeField] private float minPitch = -25f;
        [Tooltip("내려다볼 수 있는 한계 (클수록 더 높이서 내려다봄)")]
        [SerializeField] private float maxPitch = 80f;
        [Tooltip("이 각도보다 낮게(올려다보게) 내리면 카메라가 캐릭터 쪽으로 다가옴 (땅에 파묻히지 않게)")]
        [SerializeField] private float zoomInBelowPitch = 10f;
        [Tooltip("가장 많이 올려다볼 때 거리 배율 (0.4 = 기본 거리의 40%까지 다가옴)")]
        [SerializeField, Range(0.2f, 1f)] private float lowPitchDistanceScale = 0.4f;
        [SerializeField] private bool invertPitch = false;

        [Header("감도 (마우스 1픽셀당 각도)")]
        [SerializeField] private float freeLookYawSensitivity = 0.12f;
        [SerializeField] private float freeLookPitchSensitivity = 0.08f;
        [SerializeField] private float dragYawSensitivity = 0.25f;
        [SerializeField] private float dragPitchSensitivity = 0.15f;

        [Header("부드러움 (클수록 빠르게 따라감, 0이면 즉시)")]
        [SerializeField] private float rotationSharpness = 18f;
        [SerializeField] private float followSharpness = 12f;

        [Header("벽 충돌 (선택)")]
        [Tooltip("카메라가 파고들면 안 되는 레이어 (땅, 건물, 나무 등). 기본 = Ignore Raycast(투명 벽)를 뺀 전부. 플레이어 자신은 자동으로 무시")]
        [SerializeField] private LayerMask collisionMask = ~(1 << 2);
        [SerializeField] private float collisionRadius = 0.3f;
        [SerializeField] private float minDistance = 1.5f;

        // 강조 상태
        private bool focusActive;
        private Vector3 focusPoint;
        private float focusBlend;
        private float focusDistanceScale = 1f;
        private float focusWeight;             // 0 = 평소, 1 = 완전히 강조 (부드럽게 오감)
        private float pitchBeforeFocus;
        private const float FocusSharpness = 6f;

        // 연출 시점 상태 (상점 상인 클로즈업처럼 카메라를 정해진 위치·방향으로 옮김)
        private bool shotActive;
        private Vector3 shotPosition;
        private Quaternion shotRotation = Quaternion.identity;
        private float shotFov = -1f;
        private float shotWeight;              // 0 = 평소 카메라, 1 = 완전히 연출 시점
        private float baseFov = -1f;
        private Camera cam;
        private const float ShotSharpness = 5f;
        // 연출 시점끼리 옮겨갈 때(상인 얼굴 → 구매 창 구도) 지금 연출 위치가 새 위치로 부드럽게 따라가게
        private Vector3 shotCurrentPosition;
        private Quaternion shotCurrentRotation = Quaternion.identity;
        private float shotCurrentFov = -1f;
        private const float ShotMoveSharpness = 4f;

        // 지금 연출 시점으로 옮겨져 있는지 (조금이라도)
        public bool IsInShot => shotWeight > 0.01f;

        private float targetYaw;
        private float targetPitch;
        private float currentDistance;
        private bool isDragging = false;
        private bool skipNextDelta = false;

        private void Start()
        {
            targetYaw = yaw;
            targetPitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            pitch = targetPitch;
            currentDistance = distance;

            // 시작할 때는 보간 없이 바로 제자리로
            if (target != null) pivot.position = target.position + Vector3.up * pivotHeight;
            ApplyTransforms();
        }

        private void OnEnable()
        {
            Instance = this;
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
            SetCursorLocked(false);
        }

        // 특정 지점 강조 시작
        // blend = 바라보는 점을 플레이어에서 그 지점 쪽으로 옮기는 정도 (0~1)
        // distanceScale = 거리 배율 (0.6 = 60%까지 다가감), pitch = 강조 중 각도 (음수면 지금 각도 유지)
        public void BeginFocus(Vector3 point, float blend, float distanceScale, float pitch)
        {
            if (!focusActive) pitchBeforeFocus = targetPitch;
            focusActive = true;
            focusPoint = point;
            focusBlend = Mathf.Clamp01(blend);
            focusDistanceScale = Mathf.Clamp(distanceScale, 0.2f, 1f);
            if (pitch >= 0f) targetPitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        }

        // 연출 시점 시작: 카메라를 position에서 rotation 방향으로 보게 부드럽게 옮김 (fov > 0이면 화각도 바꿈)
        // 평소 카메라 계산은 그대로 돌아가고, 마지막에 이 시점으로 섞기만 해서 끝나면 자연스럽게 원래 자리로 돌아옴
        public void BeginShot(Vector3 position, Quaternion rotation, float fov = -1f)
        {
            if (cam == null && cameraTransform != null) cam = cameraTransform.GetComponent<Camera>();
            if (!shotActive && cam != null && shotWeight < 0.01f) baseFov = cam.fieldOfView;
            // 연출 시점이 처음 시작될 때는 목표 위치에서 바로 시작 (평소 카메라 → 연출 시점 섞기로 부드럽게 들어감)
            if (shotWeight < 0.01f)
            {
                shotCurrentPosition = position;
                shotCurrentRotation = rotation;
                shotCurrentFov = fov;
            }
            shotActive = true;
            shotPosition = position;
            shotRotation = rotation;
            shotFov = fov;
        }

        // 연출 시점 끝 → 평소 카메라로 부드럽게 복귀
        public void EndShot()
        {
            shotActive = false;
        }

        // 강조 끝 → 원래 각도·거리로 부드럽게 복귀
        public void EndFocus()
        {
            if (!focusActive) return;
            focusActive = false;
            targetPitch = pitchBeforeFocus;
        }

        private void Update()
        {
            if (Mouse.current == null) return;

            if (controlMode == CameraControlMode.FreeLook) HandleFreeLookInput();
            else HandleRightDragInput();
        }

        // 플레이어 이동(Update)이 끝난 뒤에 카메라를 옮겨야 떨림이 없다
        private void LateUpdate()
        {
            if (target == null || pivot == null || cameraTransform == null) return;

            // 게임 시간이 멈춘 동안(리듬게임 등)에도 카메라는 움직여야 해서 실제 시간 사용
            float dt = Time.unscaledDeltaTime;

            // 강조 정도를 부드럽게 0 ↔ 1로
            focusWeight = Mathf.Lerp(focusWeight, focusActive ? 1f : 0f, SmoothFactor(FocusSharpness, dt));

            // 1) Pivot 위치: 플레이어 위치(+높이)를 부드럽게 따라감. 강조 중이면 강조 지점 쪽으로 옮김
            Vector3 desiredPivot = target.position + Vector3.up * pivotHeight;
            if (focusWeight > 0.001f)
                desiredPivot = Vector3.Lerp(desiredPivot, Vector3.Lerp(desiredPivot, focusPoint, focusBlend), focusWeight);
            pivot.position = Smooth(pivot.position, desiredPivot, followSharpness, dt);

            // 2) 각도: 목표 각도로 부드럽게 회전 (좌우는 360도를 넘나들어도 최단 방향으로)
            float t = SmoothFactor(rotationSharpness, dt);
            yaw = Mathf.LerpAngle(yaw, targetYaw, t);
            pitch = Mathf.Lerp(pitch, targetPitch, t);

            // 3) 거리: 기본은 일정, 벽에 막히면 그 앞까지 당김
            currentDistance = CalculateDistance(dt);

            ApplyTransforms();
            ApplyShot(dt);
        }

        // 연출 시점 섞기 (ApplyTransforms가 매 프레임 평소 위치로 되돌리므로 그 위에 덮어씀)
        private void ApplyShot(float dt)
        {
            shotWeight = Mathf.Lerp(shotWeight, shotActive ? 1f : 0f, SmoothFactor(ShotSharpness, dt));
            if (!shotActive && shotWeight < 0.001f) shotWeight = 0f;

            if (cam == null) cam = cameraTransform.GetComponent<Camera>();
            if (shotWeight <= 0f)
            {
                if (cam != null && baseFov > 0f) { cam.fieldOfView = baseFov; baseFov = -1f; }
                return;
            }

            // 연출 위치끼리 이동 (BeginShot을 다시 부르면 새 위치로 미끄러지듯 옮겨감)
            float m = SmoothFactor(ShotMoveSharpness, dt);
            shotCurrentPosition = Vector3.Lerp(shotCurrentPosition, shotPosition, m);
            shotCurrentRotation = Quaternion.Slerp(shotCurrentRotation, shotRotation, m);
            if (shotFov > 0f) shotCurrentFov = shotCurrentFov > 0f ? Mathf.Lerp(shotCurrentFov, shotFov, m) : shotFov;

            float w = Mathf.SmoothStep(0f, 1f, shotWeight);
            cameraTransform.position = Vector3.Lerp(cameraTransform.position, shotCurrentPosition, w);
            cameraTransform.rotation = Quaternion.Slerp(cameraTransform.rotation, shotCurrentRotation, w);
            if (cam != null && baseFov > 0f && shotCurrentFov > 0f) cam.fieldOfView = Mathf.Lerp(baseFov, shotCurrentFov, w);
        }

        private void ApplyTransforms()
        {
            pivot.rotation = Quaternion.Euler(pitch, yaw, 0f);
            cameraTransform.localPosition = new Vector3(0f, 0f, -currentDistance);
            cameraTransform.localRotation = Quaternion.identity; // Pivot 쪽(앞)을 바라봄
        }

        private float CalculateDistance(float dt)
        {
            // 올려다볼수록 카메라를 캐릭터 쪽으로 당김 (낮은 각도에서 카메라가 땅 밑으로 들어가는 것 방지)
            float wanted = distance * Mathf.Lerp(1f, focusDistanceScale, focusWeight);
            if (pitch < zoomInBelowPitch)
            {
                float t = Mathf.InverseLerp(zoomInBelowPitch, minPitch, pitch);
                wanted *= Mathf.Lerp(1f, lowPitchDistanceScale, t);
            }

            // 벽·땅·나무에 막히면 그 앞까지 당김 (플레이어 자신의 콜라이더는 무시)
            if (collisionMask.value != 0)
            {
                Vector3 back = pivot.rotation * Vector3.back;
                RaycastHit[] hits = Physics.SphereCastAll(pivot.position, collisionRadius, back, wanted,
                                                          collisionMask, QueryTriggerInteraction.Ignore);
                float nearest = wanted;
                foreach (RaycastHit hit in hits)
                {
                    if (target != null && hit.collider.transform.IsChildOf(target)) continue;
                    if (hit.distance <= 0f) continue; // 시작점부터 겹친 콜라이더 (캐릭터 발밑 블록 등)는 무시
                    nearest = Mathf.Min(nearest, hit.distance);
                }
                wanted = Mathf.Max(minDistance, nearest);
            }

            // 막힐 때는 즉시 당기고(파묻힘 방지), 풀릴 때는 부드럽게 원래 거리로
            if (wanted < currentDistance) return wanted;
            return Mathf.Lerp(currentDistance, wanted, SmoothFactor(followSharpness, dt));
        }

        // ---------- 입력 ----------

        private void HandleFreeLookInput()
        {
            if (ShouldShowCursor())
            {
                SetCursorLocked(false);
                skipNextDelta = true;
                return;
            }

            if (Cursor.lockState != CursorLockMode.Locked)
            {
                SetCursorLocked(true);
                skipNextDelta = true;
                return;
            }

            if (skipNextDelta)
            {
                skipNextDelta = false; // 커서를 막 잠근 프레임의 튀는 값 무시
                return;
            }

            AddRotation(Mouse.current.delta.ReadValue(), freeLookYawSensitivity, freeLookPitchSensitivity);
        }

        private void HandleRightDragInput()
        {
            SetCursorLocked(false);

            bool blocked = UIInputBlocker.IsBlocked;
            if (Mouse.current.rightButton.wasPressedThisFrame && !blocked && !IsPointerOverUI())
                isDragging = true;
            if (!Mouse.current.rightButton.isPressed || blocked)
                isDragging = false;

            if (isDragging)
                AddRotation(Mouse.current.delta.ReadValue(), dragYawSensitivity, dragPitchSensitivity);
        }

        private void AddRotation(Vector2 delta, float yawSensitivity, float pitchSensitivity)
        {
            targetYaw += delta.x * yawSensitivity;
            targetPitch += (invertPitch ? delta.y : -delta.y) * pitchSensitivity;
            targetPitch = Mathf.Clamp(targetPitch, minPitch, maxPitch);
        }

        private bool ShouldShowCursor()
        {
            if (UIInputBlocker.IsBlocked) return true;
            if (!Application.isFocused) return true;
            if (Keyboard.current != null && (Keyboard.current.leftAltKey.isPressed || Keyboard.current.rightAltKey.isPressed)) return true;
            if (InventoryManager.Instance != null && InventoryManager.Instance.IsInventoryOpen) return true;
            return false;
        }

        // ---------- 유틸 ----------

        // 프레임 속도와 상관없이 같은 부드러움을 내는 보간 비율
        private static float SmoothFactor(float sharpness, float dt)
        {
            return sharpness <= 0f ? 1f : 1f - Mathf.Exp(-sharpness * dt);
        }

        private static Vector3 Smooth(Vector3 from, Vector3 to, float sharpness, float dt)
        {
            return Vector3.Lerp(from, to, SmoothFactor(sharpness, dt));
        }

        private static void SetCursorLocked(bool locked)
        {
            CursorLockMode mode = locked ? CursorLockMode.Locked : CursorLockMode.None;
            if (Cursor.lockState != mode) Cursor.lockState = mode;
            Cursor.visible = !locked;
        }

        private static bool IsPointerOverUI()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }
    }
}