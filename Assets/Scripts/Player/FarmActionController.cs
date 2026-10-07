using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using FarmingSystem.Core;
using FarmingSystem.Farming;
using FarmingSystem.Inventory;

namespace FarmingSystem.Player
{
    /// <summary>지금 마우스가 가리키는 타일을 클릭하면 일어날 행동</summary>
    public enum FarmActionType
    {
        None,     // 할 수 있는 행동 없음
        Harvest,
        ClearWithered, // 시든 작물 제거
        Plant,
        Till,
        Water
    }

    /// <summary>
    /// 마우스 좌클릭 시 클릭한 타일 상황에 맞는 농사 행동을 실행한다.
    /// 우선순위: 수확 가능한 작물 수확 > (갈아진 빈 밭 + 핫바에 씨앗 선택됨) 씨앗 심기 > 현재 도구(호미/물뿌리개) 행동
    /// 대상 타일 선택:
    /// - 커서가 잠겨 있을 때(마우스로 화면을 돌리는 중) = "시선(화면 가운데 십자선)" 기준.
    ///   카메라에서 화면 정중앙으로 선을 쏴서 처음 닿는 땅. 카메라(=시선)를 아래로 숙여야
    ///   발 근처 땅에 닿으므로, 고개를 숙여야 밭을 갈고 물을 줄 수 있다 (두근두근타운 방식).
    ///   십자선이 가리키는 곳 = 실제 대상이라 화면에 보이는 것과 항상 일치한다.
    /// - 커서가 보일 때(Alt를 누르고 있거나 RightDrag 카메라 모드) = 마우스 아래 타일
    /// 매 프레임 대상 타일과 "클릭하면 일어날 행동"을 미리 계산해서 공개한다 (TileHighlighter가 사용).
    /// 클릭 판정과 미리보기가 같은 함수(GetActionFor)를 쓰므로 표시와 실제 동작이 항상 일치한다.
    /// 상점 등 UI가 열려 있거나 마우스가 UI 위에 있으면 아무것도 하지 않는다.
    /// </summary>
    public class FarmActionController : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] private Transform cropParentContainer;
        [SerializeField] private Camera targetCamera;

        [Header("상호작용 설정")]
        [Tooltip("플레이어로부터 이 거리 이내의 타일만 상호작용 가능")]
        [SerializeField] private float maxInteractDistance = 3f;
        [SerializeField] private LayerMask groundRaycastMask;

        [Header("시선 기반 대상 선택 (커서가 잠겨 있을 때)")]
        [Tooltip("디버그: Scene 뷰에 시선(카메라 → 화면 가운데)을 선으로 그림. 초록 = 땅에 닿음, 빨강 = 못 닿음")]
        [SerializeField] private bool drawDebugRay = true;
        [Tooltip("시선이 닿은 곳이 상호작용 거리보다 이 값 이내로만 멀면, 같은 방향의 손 닿는 칸으로 당겨서 선택 (0이면 끔)")]
        [SerializeField] private float reachAssist = 2f;
        [Tooltip("클릭했는데 아무 일도 없을 때 콘솔에 이유 출력 (테스트용)")]
        [SerializeField] private bool logWhyNoAction = true;
        [Tooltip("행동할 때 캐릭터가 대상 타일 쪽으로 몸을 돌릴지")]
        [SerializeField] private bool turnTowardsTarget = true;

        [Header("행동별 소요 시간(분) - 기획서 기준")]
        [SerializeField] private int tillMinutes = 30;
        [SerializeField] private int plantMinutes = 30;
        [SerializeField] private int waterMinutes = 20;

        private bool pointerOverUI;
        private PlayerController playerController;

        /// <summary>마우스가 가리키는 타일 (없으면 null)</summary>
        public FarmTile HoveredTile { get; private set; }
        /// <summary>가리키는 타일이 상호작용 거리 안인지</summary>
        public bool HoveredInRange { get; private set; }
        /// <summary>지금 클릭하면 일어날 행동 (거리 밖이면 None)</summary>
        public FarmActionType HoveredAction { get; private set; }

        private void Awake()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;
            playerController = GetComponent<PlayerController>();
            if (playerController == null) playerController = FindFirstObjectByType<PlayerController>();
            if (playerController == null)
                Debug.LogWarning("[FarmActionController] 씬에서 PlayerController를 찾지 못함 - 거리 계산이 이 오브젝트 기준으로 됨");
            else if (playerController.gameObject != gameObject)
                Debug.LogWarning($"[FarmActionController] 이 스크립트가 Player가 아닌 '{name}'에 붙어 있음. 거리는 Player 기준으로 계산하지만, Player로 옮기는 것을 권장");
        }

        private void Update()
        {
            // OnInteract(입력 이벤트 처리 중)에서 IsPointerOverGameObject를 직접 부르면
            // Input System이 경고를 내고 값도 부정확해서, 매 프레임 미리 저장해 둔다
            // 커서가 잠겨 있으면 화면 가운데에 숨어 있는 상태라 UI 판정은 무시
            pointerOverUI = !IsCursorLocked
                            && EventSystem.current != null
                            && EventSystem.current.IsPointerOverGameObject();

            UpdateHover();

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                TryInteract();
        }

        private void UpdateHover()
        {
            HoveredTile = null;
            HoveredInRange = false;
            HoveredAction = FarmActionType.None;

            if (UIInputBlocker.IsBlocked || pointerOverUI) return;
            if (!TryGetTargetTile(out FarmTile tile, out Vector3 hitPoint, out bool inRange)) return;

            HoveredTile = tile;
            HoveredInRange = inRange;
            HoveredAction = inRange ? GetActionFor(tile, hitPoint) : FarmActionType.None;
        }

        /// <summary>
        /// 좌클릭 시 농사 행동. PlayerInput의 Interact 액션은 기본 템플릿에서 E키에 묶여 있어서
        /// 상점 E키와 동시에 실행되는 문제가 있었다 → 다른 단순 입력처럼 마우스 좌클릭을 직접 폴링한다.
        /// </summary>
        private void TryInteract()
        {
            // 상점 등 UI가 열려 있거나, 마우스가 UI(핫바 등) 위에 있으면 농사 행동 안 함
            if (UIInputBlocker.IsBlocked || pointerOverUI) return;

            if (FarmGrid.Instance == null || targetCamera == null || InventoryManager.Instance == null)
            {
                Debug.Log("[농사행동 실패] 필요한 매니저(FarmGrid/InventoryManager)가 씬에 없음");
                return;
            }

            if (!TryGetTargetTile(out FarmTile tile, out Vector3 hitPoint, out bool inRange))
            {
                if (logWhyNoAction) Debug.Log($"[농사행동 없음] 시선이 밭 타일에 닿지 않음 - 맞은 곳: {lastRayHitInfo} / 사용 중인 카메라: {(targetCamera != null ? targetCamera.name : "없음")}");
                return;
            }
            if (!inRange)
            {
                if (logWhyNoAction) Debug.Log($"[농사행동 없음] 대상 칸이 너무 멂 (거리 {HorizontalDistance(tile.transform.position):0.0}m, 허용 {maxInteractDistance}m) - 시선을 더 아래로 내려보세요");
                return;
            }

            InventoryManager inventory = InventoryManager.Instance;
            FarmActionType action = GetActionFor(tile, hitPoint);

            // 실제로 뭔가 하는 경우에만 캐릭터가 그 칸을 바라보게 돌림
            if (action != FarmActionType.None && turnTowardsTarget && playerController != null)
                playerController.FaceTowards(tile.transform.position);

            switch (action)
            {
                case FarmActionType.Harvest:
                    {
                        CropData harvested = tile.Harvest(out ItemQuality quality);
                        if (harvested != null)
                        {
                            int notAdded = inventory.AddItem(harvested, harvested.expectedYield, quality);
                            Debug.Log($"[수확] {harvested.cropName} x{harvested.expectedYield} ({quality}) 수확 완료, 인벤토리에 못 넣은 수량: {notAdded}");
                        }
                        break;
                    }

                case FarmActionType.ClearWithered:
                    tile.ClearWitheredCrop();
                    break;

                case FarmActionType.Plant:
                    {
                        SeedData seed = inventory.CurrentSeed;
                        if (tile.Plant(seed, cropParentContainer))
                        {
                            // 심은 씨앗 1개 소모
                            inventory.RemoveFromSlot(inventory.SelectedHotbarIndex, 1);
                            Debug.Log($"[씨앗심기] {seed.itemName} 심기 성공");
                            GameClock.Instance?.AdvanceMinutes(plantMinutes);
                        }
                        break;
                    }

                case FarmActionType.Till:
                    if (tile.TryTill())
                        GameClock.Instance?.AdvanceMinutes(tillMinutes);
                    break;

                case FarmActionType.Water:
                    if (tile.Water())
                        GameClock.Instance?.AdvanceMinutes(waterMinutes);
                    break;

                case FarmActionType.None:
                    if (IsSeedOutOfSeason(tile, out string seasonInfo))
                        Debug.Log($"[씨앗심기 불가] {seasonInfo}");
                    else if (tile.CurrentCrop != null && tile.CurrentCrop.IsReadyToHarvest)
                        Debug.Log($"[수확 보류] 가방 공간 부족 ({tile.CurrentCrop.Data.cropName} {tile.CurrentCrop.Data.expectedYield}개를 넣을 자리가 없음)");
                    break;
            }
        }

        /// <summary>거리·방향 계산의 기준이 되는 플레이어 위치 (스크립트가 다른 오브젝트에 붙어 있어도 안전)</summary>
        private Transform PlayerTransform => playerController != null ? playerController.transform : transform;

        private static bool IsCursorLocked => Cursor.lockState == CursorLockMode.Locked;

        /// <summary>커서 상태에 따라 플레이어 앞 타일 또는 마우스 아래 타일을 대상으로 고른다.</summary>
        private bool TryGetTargetTile(out FarmTile tile, out Vector3 hitPoint, out bool inRange)
        {
            return IsCursorLocked
                ? TryGetTileInView(out tile, out hitPoint, out inRange)
                : TryGetTileUnderMouse(out tile, out hitPoint, out inRange);
        }

        /// <summary>
        /// 시선 기준 대상 타일: 카메라에서 화면 정중앙(십자선)으로 선을 쏴서 처음 닿는 땅.
        /// 수평에 가깝게 보면 멀리 닿아서 거리 밖(회색)이 되고, 아래로 숙일수록 발 근처 타일이 잡힌다.
        /// (Ground Raycast Mask에 플레이어 레이어가 없어야 선이 캐릭터에 막히지 않음)
        /// </summary>
        private bool TryGetTileInView(out FarmTile tile, out Vector3 hitPoint, out bool inRange)
        {
            tile = null;
            hitPoint = Vector3.zero;
            inRange = false;

            if (FarmGrid.Instance == null || targetCamera == null) return false;

            Ray ray = targetCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            Vector3 hitPosition;

            // 선 길이는 자동 계산: 카메라~플레이어 거리 + 손 닿는 거리 + 여유
            // (예전 버전의 짧은 값이 인스펙터에 남아 선이 땅까지 못 닿던 문제를 막기 위해 고정값을 쓰지 않음)
            float rayLength = Vector3.Distance(ray.origin, PlayerTransform.position) + maxInteractDistance + reachAssist + 5f;
            bool physicsHit = RaycastIgnoringSelf(ray, rayLength, out RaycastHit hit);

            if (drawDebugRay)
                Debug.DrawRay(ray.origin, ray.direction * (physicsHit ? hit.distance : rayLength), physicsHit ? Color.green : Color.red);

            if (physicsHit)
            {
                hitPosition = hit.point;
            }
            else
            {
                // 대비책: 플레이어 발 높이의 수평면과 시선이 만나는 점 (FarmGrid는 좌표로 타일을 찾으므로 콜라이더 없이도 됨)
                Plane ground = new Plane(Vector3.up, PlayerTransform.position);
                if (!ground.Raycast(ray, out float enter) || enter > rayLength) return false;
                hitPosition = ray.GetPoint(enter);
            }

            Vector3 point = hitPosition;
            float distance = HorizontalDistance(point);

            // 손 닿는 거리보다 조금만 멀면, 같은 방향으로 손 닿는 칸까지 당겨옴
            // (완전히 앞을 보면 여전히 거리 밖 → 고개를 숙여야 하는 규칙은 유지)
            if (distance > maxInteractDistance && distance <= maxInteractDistance + reachAssist)
            {
                Vector3 dir = point - PlayerTransform.position;
                dir.y = 0f;
                point = PlayerTransform.position + dir.normalized * (maxInteractDistance * 0.9f);
                point.y = hitPosition.y;
            }

            if (!FarmGrid.Instance.TryGetTileAtWorldPosition(point, out tile)) return false;

            hitPoint = point;
            inRange = HorizontalDistance(tile.transform.position) <= maxInteractDistance;
            return true;
        }

        private string lastRayHitInfo = "아무것도 없음";

        /// <summary>플레이어 자신의 콜라이더는 건너뛰고 처음 맞은 것을 반환 (시선이 캐릭터 몸에 막히지 않게)</summary>
        private bool RaycastIgnoringSelf(Ray ray, float length, out RaycastHit result)
        {
            result = default;
            RaycastHit[] hits = Physics.RaycastAll(ray, length, groundRaycastMask, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (RaycastHit h in hits)
            {
                if (IsPlayerCollider(h.collider)) continue;
                result = h;
                lastRayHitInfo = $"{h.collider.name} (레이어 {LayerMask.LayerToName(h.collider.gameObject.layer)})";
                return true;
            }

            // 진단용: 마스크를 무시하고 쐈을 때 실제로 무엇이 있는지 기록
            RaycastHit[] all = Physics.RaycastAll(ray, length, ~0, QueryTriggerInteraction.Collide);
            System.Array.Sort(all, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit h in all)
            {
                if (IsPlayerCollider(h.collider)) continue;
                lastRayHitInfo = $"마스크에 걸린 것 없음. 실제로는 {h.collider.name} (레이어 {LayerMask.LayerToName(h.collider.gameObject.layer)})가 있음 → 이 레이어를 Ground Raycast Mask에 추가하면 됨";
                return false;
            }

            lastRayHitInfo = "콜라이더가 전혀 없음 (땅 오브젝트에 Collider가 있는지 확인)";
            return false;
        }

        /// <summary>플레이어 캐릭터 자신의 콜라이더인지 (밭 타일·땅은 절대 플레이어 취급하지 않음)</summary>
        private bool IsPlayerCollider(Collider col)
        {
            if (col.GetComponentInParent<FarmTile>() != null) return false;
            if (col is CharacterController) return true;
            return playerController != null && col.transform.IsChildOf(playerController.transform);
        }

        /// <summary>높이 차이는 무시한 플레이어와의 수평 거리</summary>
        private float HorizontalDistance(Vector3 point)
        {
            Vector3 d = point - PlayerTransform.position;
            d.y = 0f;
            return d.magnitude;
        }

        /// <summary>마우스 아래 타일을 찾는다. 타일이 있으면 true (거리 밖이어도), 거리 안인지는 inRange로.</summary>
        private bool TryGetTileUnderMouse(out FarmTile tile, out Vector3 hitPoint, out bool inRange)
        {
            tile = null;
            hitPoint = Vector3.zero;
            inRange = false;

            if (FarmGrid.Instance == null || targetCamera == null || Mouse.current == null) return false;

            Ray ray = targetCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit, 100f, groundRaycastMask)) return false;
            if (!FarmGrid.Instance.TryGetTileAtWorldPosition(hit.point, out tile)) return false;

            hitPoint = hit.point;
            inRange = HorizontalDistance(hit.point) <= maxInteractDistance;
            return true;
        }

        /// <summary>
        /// 이 타일을 지금 클릭하면 어떤 행동이 일어나는지 판정한다. 실제 클릭과 하이라이트 표시가 같이 쓴다.
        /// 우선순위: 수확 > 씨앗 심기 > 현재 도구
        /// </summary>
        private FarmActionType GetActionFor(FarmTile tile, Vector3 hitPoint)
        {
            InventoryManager inventory = InventoryManager.Instance;
            if (inventory == null) return FarmActionType.None;

            // 1순위: 수확 (어떤 등급이 나와도 들어갈 공간이 있을 때만 - 공간 없이 수확하면 수확물이 사라짐)
            if (tile.CurrentCrop != null && tile.CurrentCrop.IsReadyToHarvest)
                return HasRoomForHarvest(tile.CurrentCrop.Data) ? FarmActionType.Harvest : FarmActionType.None;

            // 시든 작물: 지금은 어떤 도구로든 클릭하면 제거 (나중에 낫 전용으로 바꿀 자리)
            if (tile.CurrentCrop != null && tile.CurrentCrop.IsWithered)
                return FarmActionType.ClearWithered;

            // 2순위: 갈아진 빈 밭 + 핫바에 씨앗(SeedData) 선택 + 지금 계절에 심을 수 있는 씨앗
            // CurrentSeed는 선택된 아이템이 SeedData일 때만 값을 반환하므로 수확물(CropData)은 여기서 걸러진다
            if (tile.State == TileState.Tilled && tile.CurrentCrop == null && inventory.CurrentSeed != null)
                return IsSeedOutOfSeason(tile, out _) ? FarmActionType.None : FarmActionType.Plant;

            // 3순위: 현재 도구
            switch (inventory.CurrentTool)
            {
                case ToolType.Hoe:
                    bool canTill = FarmPlacementValidator.Instance == null || FarmPlacementValidator.Instance.CanTillAt(hitPoint);
                    return tile.State == TileState.Natural && canTill ? FarmActionType.Till : FarmActionType.None;

                case ToolType.WateringCan:
                    return tile.State == TileState.Tilled && !tile.IsWatered ? FarmActionType.Water : FarmActionType.None;
            }

            return FarmActionType.None;
        }

        /// <summary>갈린 빈 밭에 핫바 씨앗을 심으려는데, 그 씨앗이 지금 계절 작물이 아닌지</summary>
        private static bool IsSeedOutOfSeason(FarmTile tile, out string info)
        {
            info = null;
            SeedData seed = InventoryManager.Instance != null ? InventoryManager.Instance.CurrentSeed : null;
            if (seed == null || seed.resultCrop == null) return false;
            if (tile.State != TileState.Tilled || tile.CurrentCrop != null) return false;
            if (!SeasonUtil.TryGetCurrentSeason(out Season current)) return false;
            if (seed.resultCrop.CanGrowIn(current)) return false;

            info = $"{seed.itemName}은(는) 지금 계절({current})에 심을 수 없음 (가능 계절: {seed.resultCrop.season})";
            return true;
        }

        private static bool HasRoomForHarvest(CropData crop)
        {
            InventoryManager inventory = InventoryManager.Instance;
            int room = Mathf.Min(inventory.GetAddableAmount(crop, ItemQuality.Normal),
                       Mathf.Min(inventory.GetAddableAmount(crop, ItemQuality.Good),
                                 inventory.GetAddableAmount(crop, ItemQuality.Perfect)));
            return room >= crop.expectedYield;
        }
    }
}