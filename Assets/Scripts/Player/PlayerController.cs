using UnityEngine;
using UnityEngine.InputSystem;

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
            Vector3 moveDir = CalculateCameraRelativeDirection(moveInput);

            if (moveDir.sqrMagnitude > 0.001f)
            {
                controller.Move(moveDir * moveSpeed * Time.deltaTime);

                Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);

                FacingDirection = moveDir;
            }

            ApplyGravity();
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