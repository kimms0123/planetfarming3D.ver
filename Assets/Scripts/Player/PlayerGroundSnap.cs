using UnityEngine;

namespace FarmingSystem.Player
{
    // 플레이어가 땅 속에 파묻히거나 맵 밖으로 떨어지는 것을 막는 스크립트 (Player 오브젝트에 붙임)
    // - 게임 시작 시: 스폰 위치(Anchor_PlayerSpawn)가 있으면 그곳으로, 없으면 지금 자리의 땅 위로 올려놓음
    //   (맵을 다시 생성하거나 크기를 바꾸면 예전 위치가 새 블록 안쪽이 되어 캐릭터가 파묻히기 때문)
    // - 플레이 중: 블록 안에 끼었거나 일정 높이 아래로 떨어지면 가장 가까운 땅 위로 되돌림
    [RequireComponent(typeof(CharacterController))]
    public class PlayerGroundSnap : MonoBehaviour
    {
        [Tooltip("게임 시작 위치. 비워두면 이름이 Anchor_PlayerSpawn인 오브젝트를 자동으로 찾음")]
        [SerializeField] private Transform spawnPoint;
        [Tooltip("땅을 찾을 레이어 (땅 블록 레이어들). 기본 = Ignore Raycast(투명 벽)를 뺀 전부")]
        [SerializeField] private LayerMask groundMask = ~(1 << 2);
        [Tooltip("이 높이보다 아래로 떨어지면 스폰 위치로 되돌림")]
        [SerializeField] private float fallResetY = -10f;
        [Tooltip("블록 안에 끼었는지 검사할지 (캐릭터 머리 위에 땅이 있으면 끼인 것으로 판단)")]
        [SerializeField] private bool fixWhenStuck = true;

        private CharacterController controller;
        private Vector3 lastSafePosition;
        private float stuckTimer;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
        }

        private void Start()
        {
            if (spawnPoint == null)
            {
                GameObject anchor = GameObject.Find("Anchor_PlayerSpawn");
                if (anchor != null) spawnPoint = anchor.transform;
            }

            Vector3 start = spawnPoint != null ? spawnPoint.position : transform.position;
            Debug.Log($"[PlayerGroundSnap] 시작 위치: {(spawnPoint != null ? spawnPoint.name : "현재 자리")} → 땅 위로 올림");
            if (spawnPoint != null) transform.rotation = spawnPoint.rotation;
            TeleportToGround(start);
        }

        private void LateUpdate()
        {
            // 맵 밖으로 떨어짐 → 스폰 위치로
            if (transform.position.y < fallResetY)
            {
                Debug.Log("[PlayerGroundSnap] 맵 아래로 떨어져서 스폰 위치로 되돌림");
                TeleportToGround(spawnPoint != null ? spawnPoint.position : lastSafePosition);
                return;
            }

            if (controller.isGrounded && !IsInsideGround())
            {
                lastSafePosition = transform.position;
                stuckTimer = 0f;
            }
            else if (fixWhenStuck && IsInsideGround())
            {
                // 잠깐 겹치는 건 무시하고, 0.2초 넘게 끼어 있으면 위로 꺼냄
                stuckTimer += Time.deltaTime;
                if (stuckTimer > 0.2f)
                {
                    Debug.Log("[PlayerGroundSnap] 땅 블록에 끼어서 위로 꺼냄");
                    TeleportToGround(transform.position);
                    stuckTimer = 0f;
                }
            }
        }

        // 캐릭터 몸통 중심이 땅 콜라이더 안에 들어가 있는지
        private bool IsInsideGround()
        {
            Vector3 center = transform.position + controller.center;
            float radius = controller.radius * 0.5f;
            Collider[] hits = Physics.OverlapSphere(center, radius, groundMask, QueryTriggerInteraction.Ignore);
            foreach (Collider c in hits)
                if (c != controller && !c.transform.IsChildOf(transform)) return true;
            return false;
        }

        // 지정한 위치의 바로 위 하늘에서 아래로 선을 쏴서 가장 높은 땅 윗면에 캐릭터를 세움
        public void TeleportToGround(Vector3 around)
        {
            Vector3 from = new Vector3(around.x, around.y + 100f, around.z);
            Vector3 target = around;

            RaycastHit[] hits = Physics.RaycastAll(from, Vector3.down, 300f, groundMask, QueryTriggerInteraction.Ignore);
            float bestY = float.MinValue;
            foreach (RaycastHit h in hits)
            {
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (h.point.y > bestY) { bestY = h.point.y; target = h.point; }
            }

            // CharacterController가 켜져 있으면 위치를 바로 바꿔도 무시되므로 잠깐 끄고 옮김
            controller.enabled = false;
            transform.position = target + Vector3.up * (controller.skinWidth + 0.05f);
            controller.enabled = true;
            lastSafePosition = transform.position;
        }
    }
}