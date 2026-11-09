using UnityEngine;
using UnityEngine.InputSystem;
using FarmingSystem.Core;

namespace FarmingSystem.Player
{
    // 플레이어 이동·달리기·점프·회전
    // - 이동: WASD (PlayerInput의 Move 액션, Send Messages → OnMove)
    // - 달리기: Shift를 누르고 있는 동안 빨라짐
    // - 점프: Space. 땅에 있을 때만 가능
    //   · 코요테 타임: 절벽 끝에서 막 떨어진 직후에도 잠깐은 점프가 됨 (조작감 개선)
    //   · 점프 입력 버퍼: 착지 직전에 미리 누른 점프도 착지하자마자 실행됨
    // - 달리기·점프는 키보드를 직접 읽음 (PlayerInput 액션 설정을 건드리지 않아도 바로 동작)
    // - 상점 등 UI가 열려 있으면 이동·달리기·점프 모두 막음 (중력은 계속 적용)
    // - 애니메이션: 자식 모델의 Animator에 Speed(Float)·Jump(Trigger)를 넘겨줌
    //   · 심기/물주기/괭이질은 PlayPlant()/PlayWater()/PlayHoe()로 재생하고, 동작이 끝날 때까지 이동을 막음
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("이동 설정")]
        [Tooltip("걷기 속도")]
        [SerializeField] private float moveSpeed = 4f;
        [Tooltip("Shift를 누르고 있을 때 속도")]
        [SerializeField] private float runSpeed = 7f;
        [Tooltip("속도가 바뀔 때 따라가는 빠르기 (클수록 즉시 반응, 작을수록 미끄러지듯 가속)")]
        [SerializeField] private float acceleration = 14f;
        [SerializeField] private float rotationSpeed = 12f;

        [Header("점프 설정")]
        [Tooltip("점프 사용 여부. 끄면 Space를 눌러도 안 뜀 (동숲처럼 높이가 다른 곳은 경사로로만 오르내림)")]
        [SerializeField] private bool enableJump = true;
        [Tooltip("점프 높이 (미터). 지형 한 층이 2m라서 2.5면 한 블록 위로 올라감 (잠긴 지역 벽은 6m라 못 넘음)")]
        [SerializeField] private float jumpHeight = 2.5f;
        [SerializeField] private float gravity = -20f;
        [Tooltip("땅에서 떨어진 뒤에도 점프를 허용하는 시간 (초)")]
        [SerializeField] private float coyoteTime = 0.12f;
        [Tooltip("착지 전에 미리 누른 점프를 기억하는 시간 (초)")]
        [SerializeField] private float jumpBufferTime = 0.12f;
        [Tooltip("공중에서 방향을 바꿀 수 있는 정도 (1 = 땅과 같음, 0 = 공중에서 조작 불가)")]
        [SerializeField, Range(0f, 1f)] private float airControl = 0.6f;

        [Header("참조")]
        [SerializeField] private Transform cameraTransform;
        [Tooltip("자식 모델(SpaceFarmer_AllClips)의 Animator. 비워두면 자식에서 자동으로 찾음")]
        [SerializeField] private Animator animator;

        [Header("농사 동작 길이 (초) - 이 시간 동안 이동을 막음")]
        [SerializeField] private float plantDuration = 3.2f;   // Plant_Seed 96프레임
        [SerializeField] private float waterDuration = 2.4f;   // Watering 72프레임
        [SerializeField] private float hoeDuration = 1.7f;     // Hoe_Till_Once 51프레임

        // Animator 파라미터 (이름을 바꾸면 Animator Controller 쪽도 같이 바꿔야 함)
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int JumpHash = Animator.StringToHash("Jump");
        private static readonly int PlantHash = Animator.StringToHash("Plant");
        private static readonly int WaterHash = Animator.StringToHash("Water");
        private static readonly int HoeHash = Animator.StringToHash("Hoe");
        private float actionEndTime = -999f;

        private CharacterController controller;
        private Vector3 verticalVelocity;
        private Vector3 horizontalVelocity;
        private Vector2 moveInput;
        private Quaternion? pendingFacing; // 이동하지 않을 때 돌아볼 방향 (농사 행동 등)

        private float lastGroundedTime = -999f;
        private float lastJumpPressedTime = -999f;

        public Vector3 FacingDirection { get; private set; } = Vector3.forward;
        // 애니메이션 연결용 상태값
        public bool IsRunning { get; private set; }
        public bool IsGrounded => controller != null && controller.isGrounded;
        public float CurrentSpeed => horizontalVelocity.magnitude;
        // 심기·물주기·괭이질 동작 중이면 true (이 동안 이동·점프 불가)
        public bool IsBusy => Time.time < actionEndTime;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (cameraTransform == null && Camera.main != null)
                cameraTransform = Camera.main.transform;
            if (animator == null)
                animator = GetComponentInChildren<Animator>();
        }

        public void OnMove(InputValue value)
        {
            moveInput = value.Get<Vector2>();
        }

        private void Update()
        {
            bool blocked = UIInputBlocker.IsBlocked || IsBusy;
            Keyboard kb = Keyboard.current;

            Vector2 input = blocked ? Vector2.zero : moveInput;
            bool runHeld = !blocked && kb != null && kb.shiftKey.isPressed;
            if (enableJump && !blocked && kb != null && kb.spaceKey.wasPressedThisFrame)
                lastJumpPressedTime = Time.time;

            if (controller.isGrounded) lastGroundedTime = Time.time;

            // 수평 이동 (걷기/달리기 속도로 부드럽게 가감속)
            Vector3 moveDir = CalculateCameraRelativeDirection(input);
            IsRunning = runHeld && moveDir.sqrMagnitude > 0.001f;
            Vector3 targetVelocity = moveDir * (IsRunning ? runSpeed : moveSpeed);
            float accel = controller.isGrounded ? acceleration : acceleration * airControl;
            horizontalVelocity = Vector3.Lerp(horizontalVelocity, targetVelocity, 1f - Mathf.Exp(-accel * Time.deltaTime));

            // 회전
            if (moveDir.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);

                FacingDirection = moveDir;
                pendingFacing = null; // 움직이기 시작하면 이동 방향이 우선
            }
            else if (pendingFacing.HasValue)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, pendingFacing.Value, rotationSpeed * Time.deltaTime);
                if (Quaternion.Angle(transform.rotation, pendingFacing.Value) < 1f)
                {
                    transform.rotation = pendingFacing.Value;
                    pendingFacing = null;
                }
            }

            UpdateVertical();

            // 수평 + 수직 이동을 한 번에 (두 번 나눠 Move하면 isGrounded가 흔들림)
            controller.Move((horizontalVelocity + verticalVelocity) * Time.deltaTime);

            // 애니메이션: 실제 수평 속도 (걷기 4, 달리기 7 → Animator에서 Speed > 5면 Run)
            if (animator != null)
                animator.SetFloat(SpeedHash, horizontalVelocity.magnitude, 0.1f, Time.deltaTime);
        }

        // 점프와 중력
        private void UpdateVertical()
        {
            bool canJump = Time.time - lastGroundedTime <= coyoteTime;
            bool wantsJump = Time.time - lastJumpPressedTime <= jumpBufferTime;

            if (canJump && wantsJump && verticalVelocity.y <= 0f)
            {
                // 원하는 높이까지 올라가는 데 필요한 초기 속도: v = √(2·h·|g|)
                verticalVelocity.y = Mathf.Sqrt(2f * jumpHeight * -gravity);
                if (animator != null) animator.SetTrigger(JumpHash);
                lastJumpPressedTime = -999f;
                lastGroundedTime = -999f; // 같은 점프로 두 번 뛰지 않게
                return;
            }

            if (controller.isGrounded && verticalVelocity.y < 0f)
                verticalVelocity.y = -2f; // 땅에 붙어 있게 살짝 눌러줌 (경사로 내려갈 때 튀지 않게)
            else
                verticalVelocity.y += gravity * Time.deltaTime;

            // 점프 중 머리가 천장에 닿으면 바로 떨어지기 시작
            if ((controller.collisionFlags & CollisionFlags.Above) != 0 && verticalVelocity.y > 0f)
                verticalVelocity.y = 0f;
        }

        // 제자리에서 특정 지점을 바라보도록 부드럽게 몸을 돌린다 (밭 갈기·물주기 등)
        public void FaceTowards(Vector3 worldPoint)
        {
            Vector3 dir = worldPoint - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;

            FacingDirection = dir.normalized;
            pendingFacing = Quaternion.LookRotation(FacingDirection, Vector3.up);
        }

        // 농사 동작 재생 (농사 시스템에서 호출). 바라볼 지점을 주면 그쪽으로 돌아선 뒤 동작함
        // 이미 다른 동작 중이면 무시하고 false를 돌려줌
        public bool PlayPlant(Vector3? lookAt = null) => PlayAction(PlantHash, plantDuration, lookAt);
        public bool PlayWater(Vector3? lookAt = null) => PlayAction(WaterHash, waterDuration, lookAt);
        public bool PlayHoe(Vector3? lookAt = null) => PlayAction(HoeHash, hoeDuration, lookAt);

        private bool PlayAction(int trigger, float duration, Vector3? lookAt)
        {
            if (IsBusy || !controller.isGrounded) return false;
            if (lookAt.HasValue) FaceTowards(lookAt.Value);
            horizontalVelocity = Vector3.zero;     // 미끄러지며 동작하지 않게 즉시 멈춤
            actionEndTime = Time.time + duration;
            if (animator != null) animator.SetTrigger(trigger);
            return true;
        }

        private Vector3 CalculateCameraRelativeDirection(Vector2 input)
        {
            if (input.sqrMagnitude < 0.0001f) return Vector3.zero;

            Vector3 camForward = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
            Vector3 camRight = cameraTransform != null ? cameraTransform.right : Vector3.right;

            camForward.y = 0f;
            camRight.y = 0f;
            camForward.Normalize();
            camRight.Normalize();

            return (camForward * input.y + camRight * input.x).normalized;
        }
    }
}