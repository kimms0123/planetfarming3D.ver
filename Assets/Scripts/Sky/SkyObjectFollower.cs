using UnityEngine;

namespace FarmingSystem.World
{
    // 하늘에 떠 있는 큰 행성 (배경용)
    // 카메라와 같이 움직여서, 플레이어가 아무리 걸어가도 행성이 가까워지지 않음 → 아주 멀리 있는 것처럼 보임
    // (카메라를 돌리면 하늘의 다른 쪽을 보게 되니 행성은 하늘의 정해진 자리에 그대로 있음)
    // - 이 컴포넌트를 행성들을 모아 둔 빈 오브젝트(SkyPlanets)에 붙이고, 행성들은 그 자식으로 둠
    // - 자식 행성의 위치 = 카메라로부터의 방향·거리 (예: (300, 180, 600) = 앞쪽 위 멀리)
    // - 행성을 천천히 자전시키는 것도 가능
    // - 밤에만 더 잘 보이게 하는 건 SkyController의 하늘 색이 어두워지면서 자연히 됨
    [ExecuteAlways]
    public class SkyObjectFollower : MonoBehaviour
    {
        [Tooltip("따라갈 카메라. 비우면 메인 카메라")]
        [SerializeField] private Camera targetCamera;
        [Tooltip("카메라 위치를 따라가는 정도 (1 = 완전히 따라감 = 무한히 멀리 있는 느낌 / 0.95 = 걸을 때 아주 살짝 움직임)")]
        [SerializeField, Range(0f, 1f)] private float followAmount = 1f;
        [Tooltip("높이(Y)도 따라갈지. 끄면 산에 올라가면 행성이 살짝 내려가 보임")]
        [SerializeField] private bool followHeight = true;
        [Tooltip("실행 안 해도 Scene/Game 화면에서 카메라를 따라감 (배치 확인용)")]
        [SerializeField] private bool previewInEditMode = true;

        [Header("자전")]
        [Tooltip("자식 행성들을 각자 제자리에서 천천히 돌림 (도/초, 실행 중에만). 0 = 안 돌림")]
        [SerializeField, Range(0f, 10f)] private float selfSpinSpeed = 1f;
        [Tooltip("돌리는 축 (자식 행성 기준)")]
        [SerializeField] private Vector3 spinAxis = Vector3.up;

        [Header("확인")]
        [Tooltip("행성이 카메라의 Far Clip(보이는 최대 거리)보다 멀면 안 보임 → 시작할 때 경고")]
        [SerializeField] private bool warnIfBeyondFarClip = true;

        private Vector3 startPosition;
        private Vector3 startCameraPosition;
        private bool hasStart;

        private void OnEnable()
        {
            hasStart = false;
        }

        private void Start()
        {
            if (!Application.isPlaying || !warnIfBeyondFarClip) return;
            Camera cam = GetCamera();
            if (cam == null) return;
            foreach (Transform child in transform)
            {
                float distance = child.localPosition.magnitude * Mathf.Max(transform.lossyScale.x, 0.0001f);
                if (distance > cam.farClipPlane * 0.95f)
                {
                    Debug.LogWarning($"[하늘 행성] '{child.name}'이(가) 카메라에서 {distance:0}m 떨어져 있는데 카메라 Far Clip이 {cam.farClipPlane:0}m라 안 보일 수 있음. " +
                                     "행성을 더 가까이(위치값을 줄이고 크기도 같이 줄임) 하거나 카메라 Far를 늘리세요.", child);
                }
            }
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying && !previewInEditMode) return;
            Camera cam = GetCamera();
            if (cam == null) return;

            Vector3 camPos = cam.transform.position;
            if (!hasStart)
            {
                startPosition = transform.position - camPos * followAmount;
                startCameraPosition = camPos;
                hasStart = true;
            }

            // followAmount 1이면 항상 카메라 위치 = 이 오브젝트 위치 (행성은 자식 위치만큼 떨어져 있음)
            Vector3 p = followAmount >= 1f ? camPos : startPosition + camPos * followAmount;
            if (!followHeight) p.y = followAmount >= 1f ? startCameraPosition.y : startPosition.y + startCameraPosition.y * followAmount;
            transform.position = p;

            if (Application.isPlaying && selfSpinSpeed > 0f)
            {
                foreach (Transform child in transform)
                    child.Rotate(spinAxis, selfSpinSpeed * Time.deltaTime, Space.Self);
            }
        }

        private Camera GetCamera()
        {
            if (targetCamera != null) return targetCamera;
            return Camera.main;
        }
    }
}