using UnityEngine;
using UnityEngine.InputSystem;
using FarmingSystem.Core;

namespace FarmingSystem.Player
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("이동 설정")]
        [SerializeField] private float moveSpeed = 4f;
        [SerializeField] private float rotationSpeed = 12f;
        [SerializeField] private float gravity = -9.81f;

        [Header("참조")]
        [SerializeField] private Transform cameraTransform;

        private CharacterController controller;
        private Vector3 verticalVelocity;
        private Vector2 moveInput;
        private Quaternion? pendingFacing; // 이동하지 않을 때 돌아볼 방향 (농사 행동 등)

        public Vector3 FacingDirection { get; private set; } = Vector3.forward;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (cameraTransform == null && Camera.main != null)
                cameraTransform = Camera.main.transform;
        }

        public void OnMove(InputValue value)
        {
            moveInput = value.Get<Vector2>();
        }

        private void Update()
        {
            // 상점 등 UI가 입력을 막고 있으면 이동하지 않음 (중력은 계속 적용)
            Vector2 input = UIInputBlocker.IsBlocked ? Vector2.zero : moveInput;
            Vector3 moveDir = CalculateCameraRelativeDirection(input);

            if (moveDir.sqrMagnitude > 0.001f)
            {
                controller.Move(moveDir * moveSpeed * Time.deltaTime);

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

            ApplyGravity();
        }

        /// <summary>제자리에서 특정 지점을 바라보도록 부드럽게 몸을 돌린다 (밭 갈기·물주기 등).</summary>
        public void FaceTowards(Vector3 worldPoint)
        {
            Vector3 dir = worldPoint - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;

            FacingDirection = dir.normalized;
            pendingFacing = Quaternion.LookRotation(FacingDirection, Vector3.up);
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

        private void ApplyGravity()
        {
            if (controller.isGrounded && verticalVelocity.y < 0)
                verticalVelocity.y = -2f;

            verticalVelocity.y += gravity * Time.deltaTime;
            controller.Move(verticalVelocity * Time.deltaTime);
        }
    }
}