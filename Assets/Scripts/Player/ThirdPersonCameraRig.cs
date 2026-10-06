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

    /// <summary>
    /// 3인칭 카메라 1개로 플레이어 주변을 도는 카메라 리그.
    ///
    /// 구조:  CameraRig (이 스크립트)
    ///         └─ CameraPivot   ← 플레이어 위치(+높이)를 따라가고, 좌우(yaw)·상하(pitch)로 회전
    ///             └─ Main Camera ← Pivot 뒤쪽 일정 거리에 고정, 항상 Pivot을 바라봄
    ///
    /// Pivot은 플레이어의 "위치만" 따라가고 회전은 따라가지 않는다.
    /// (플레이어 자식으로 두면 캐릭터가 방향을 틀 때마다 카메라도 같이 휙 돌아가기 때문)
    ///
    /// - 마우스 좌우 → Pivot 좌우 회전 (360도)
    /// - 마우스 상하 → Pivot 상하 각도 (최소~최대 각도로 제한)
    /// - 각도와 위치 모두 부드러운 보간을 적용하고, 거리는 항상 일정하게 유지
    /// - (선택) 벽·물체에 카메라가 파묻히면 그 앞까지 당겨옴
    /// - 상점·인벤토리가 열리거나 Alt를 누르면 커서가 나타나고 회전이 멈춤
    /// </summary>
    public class ThirdPersonCameraRig : MonoBehaviour
    {
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
        [Tooltip("시작 상하 각도. 0 = 수평, 90 = 바로 위에서 내려다봄")]
        [SerializeField] private float pitch = 30f;
        [SerializeField] private float minPitch = 10f;
        [SerializeField] private float maxPitch = 65f;
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
        [Tooltip("카메라가 파고들면 안 되는 레이어 (건물, 나무 등). Nothing이면 충돌 처리 안 함")]
        [SerializeField] private LayerMask collisionMask = 0;
        [SerializeField] private float collisionRadius = 0.3f;
        [SerializeField] private float minDistance = 1.5f;

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

        private void OnDisable()
        {
            SetCursorLocked(false);
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

            float dt = Time.deltaTime;

            // 1) Pivot 위치: 플레이어 위치(+높이)를 부드럽게 따라감
            Vector3 desiredPivot = target.position + Vector3.up * pivotHeight;
            pivot.position = Smooth(pivot.position, desiredPivot, followSharpness, dt);

            // 2) 각도: 목표 각도로 부드럽게 회전 (좌우는 360도를 넘나들어도 최단 방향으로)
            float t = SmoothFactor(rotationSharpness, dt);
            yaw = Mathf.LerpAngle(yaw, targetYaw, t);
            pitch = Mathf.Lerp(pitch, targetPitch, t);

            // 3) 거리: 기본은 일정, 벽에 막히면 그 앞까지 당김
            currentDistance = CalculateDistance(dt);

            ApplyTransforms();
        }

        private void ApplyTransforms()
        {
            pivot.rotation = Quaternion.Euler(pitch, yaw, 0f);
            cameraTransform.localPosition = new Vector3(0f, 0f, -currentDistance);
            cameraTransform.localRotation = Quaternion.identity; // Pivot 쪽(앞)을 바라봄
        }

        private float CalculateDistance(float dt)
        {
            float wanted = distance;

            if (collisionMask.value != 0)
            {
                Vector3 back = pivot.rotation * Vector3.back;
                if (Physics.SphereCast(pivot.position, collisionRadius, back, out RaycastHit hit,
                                       distance, collisionMask, QueryTriggerInteraction.Ignore))
                {
                    wanted = Mathf.Max(minDistance, hit.distance);
                }
            }

            // 벽에 막힐 때는 즉시 당기고(파묻힘 방지), 풀릴 때는 부드럽게 원래 거리로
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