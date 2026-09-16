using UnityEngine;

namespace FarmingSystem.Farming
{
    /// <summary>
    /// 특정 월드 좌표가 밭을 갈 수 있는 위치인지 판정하는 전담 클래스.
    /// FarmActionController는 이 클래스에게 "여기 갈아도 돼?"만 물어보고,
    /// 실제 지형/장애물 판정 규칙은 전부 여기서 관리한다.
    /// </summary>
    public class FarmPlacementValidator : MonoBehaviour
    {
        public static FarmPlacementValidator Instance { get; private set; }

        [Header("Layer 설정")]
        [SerializeField] private LayerMask farmableGroundLayer;
        [SerializeField] private LayerMask blockerLayer;

        [Header("판정 설정")]
        [SerializeField] private float raycastStartHeight = 2f;
        [SerializeField] private float raycastMaxDistance = 5f;
        [SerializeField] private float maxSlopeAngle = 30f;
        [SerializeField] private Vector3 blockerCheckSize = new Vector3(0.4f, 0.4f, 0.4f);

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public bool CanTillAt(Vector3 worldPos)
        {
            Vector3 rayOrigin = worldPos + Vector3.up * raycastStartHeight;
            if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, raycastMaxDistance))
                return false;

            if (((1 << hit.collider.gameObject.layer) & farmableGroundLayer) == 0)
                return false;

            float slopeAngle = Vector3.Angle(hit.normal, Vector3.up);
            if (slopeAngle > maxSlopeAngle)
                return false;

            Vector3 checkCenter = hit.point + Vector3.up * blockerCheckSize.y;
            if (Physics.CheckBox(checkCenter, blockerCheckSize / 2f, Quaternion.identity, blockerLayer))
                return false;

            return true;
        }
    }
}