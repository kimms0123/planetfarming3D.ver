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
        [Tooltip("플레이어에서 타일 가장자리까지의 거리. 이 안에 있는 칸이면 십자선으로 가리켜 행동 가능")]
        [SerializeField] private float maxInteractDistance = 3f;
        [SerializeField] private LayerMask groundRaycastMask;

        [Header("시선 기반 대상 선택 (커서가 잠겨 있을 때)")]
        [Tooltip("디버그: Scene 뷰에 시선(카메라 → 화면 가운데)을 선으로 그림. 초록 = 땅에 닿음, 빨강 = 못 닿음")]
        [SerializeField] private bool drawDebugRay = true;
        [Tooltip("땅을 갈 때 이 레이어의 물체(나무·바위·건물)가 칸 위에 있으면 막음")]
        [SerializeField] private LayerMask obstacleMask;
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
            if (obstacleMask.value == 0) obstacleMask = LayerMask.GetMask("FarmingBlocker");
            playerController = GetComponent<PlayerController>();
            if (playerController == null) playerController = FindAnyObjectByType<PlayerController>();
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
                if (logWhyNoAction)
                {
                    // 땅에는 맞았는데 밭이 아닌 경우 = 돌길·경사로처럼 원래 농사 못 짓는 땅
                    if (lastRayHitInfo.Contains("NonFarmableGround"))
                        Debug.Log($"[농사행동 없음] 농사를 지을 수 없는 땅 (돌길·경사로) - 맞은 곳: {lastRayHitInfo}. 길 밖의 땅을 바라보세요");
                    else
                        Debug.Log($"[농사행동 없음] 시선이 밭 타일에 닿지 않음 - 맞은 곳: {lastRayHitInfo} / 사용 중인 카메라: {(targetCamera != null ? targetCamera.name : "없음")}");
                }
                return;
            }
            if (!inRange)
            {
                if (logWhyNoAction) Debug.Log($"[농사행동 없음] 대상 칸이 너무 멂 (가장자리까지 {DistanceToTileEdge(tile):0.0}m, 허용 {maxInteractDistance}m) - 더 가까이 가거나 시선을 더 아래로 내려보세요");
                return;
            }

            InventoryManager inventory = InventoryManager.Instance;
            FarmActionType action = GetActionFor(tile, hitPoint);

            // 실제로 뭔가 하는 경우에만 캐릭터가 그 칸을 바라보게 돌림
            if (action != FarmActionType.None && turnTowardsTarget && playerController != null)
                playerController.FaceTowards(tile.Center);

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
                    if (inventory.CurrentTool == ToolType.Hoe && tile.State == TileState.Natural && !IsTileClear(tile))
                        Debug.Log($"[밭갈기 불가] {tile.name} 위에 나무·바위·건물 같은 장애물이 있음");
                    else if (IsSeedOutOfSeason(tile, out string seasonInfo))
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

            if (targetCamera == null) return false;

            // 십자선이 가리키는 블록 = 대상. 다른 칸으로 당기거나 좌표를 보정하지 않음
            // (예전엔 조금 멀면 플레이어 쪽 칸으로 당겨와서, 십자선과 다른 칸이 잡히거나 시선을 살짝만 틀어도 결과가 바뀌었음)
            Ray ray = targetCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            float rayLength = Vector3.Distance(ray.origin, PlayerTransform.position) + maxInteractDistance + 10f;
            bool physicsHit = RaycastIgnoringSelf(ray, rayLength, out RaycastHit hit);

            if (drawDebugRay)
                Debug.DrawRay(ray.origin, ray.direction * (physicsHit ? hit.distance : rayLength), physicsHit ? Color.green : Color.red);

            if (!physicsHit) return false;

            tile = hit.collider.GetComponentInParent<FarmTile>();
            if (tile == null && !ResolveTile(hit.point, out tile)) return false;

            hitPoint = hit.point;
            inRange = DistanceToTileEdge(tile) <= maxInteractDistance;
            return true;
        }

        // 플레이어에서 타일의 가장 가까운 가장자리까지의 수평 거리 (타일 위에 서 있으면 0)
        // 칸 가운데 기준으로 재면 칸이 큰 맵에서는 바로 옆 칸도 멀다고 나와서 가장자리 기준으로 잼
        private float DistanceToTileEdge(FarmTile tile)
        {
            Bounds b = tile.GetGroundBounds();
            Vector3 p = PlayerTransform.position;
            float dx = Mathf.Max(0f, Mathf.Abs(p.x - b.center.x) - b.extents.x);
            float dz = Mathf.Max(0f, Mathf.Abs(p.z - b.center.z) - b.extents.z);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // 이 칸 위에 나무·바위·건물이 없는지 (칸 윗면 바로 위의 상자 공간에 장애물 레이어가 겹치는지 검사)
        // 예전 FarmPlacementValidator는 시선이 맞은 "점"에서 바닥을 다시 검사해서
        // 블록 모서리(둥글게 깎인 부분)를 보면 경사로 판정되거나, 내 캐릭터 몸에 막혀 실패하는 경우가 있었음
        // 이제 FarmTile이 붙은 칸 = 이미 농사 가능한 땅이므로 장애물만 확인
        private bool IsTileClear(FarmTile tile)
        {
            Bounds b = tile.GetGroundBounds();
            Vector3 center = new Vector3(b.center.x, b.max.y + 0.6f, b.center.z);
            Vector3 half = new Vector3(b.extents.x * 0.8f, 0.5f, b.extents.z * 0.8f);
            return !Physics.CheckBox(center, half, Quaternion.identity, obstacleMask, QueryTriggerInteraction.Ignore);
        }

        // 월드 위치 → 그 자리의 밭 타일
        // 1) 위에서 아래로 선을 쏴서 맞은 블록의 FarmTile (보이는 칸과 정확히 일치)
        // 2) 못 찾으면 FarmGrid 좌표 계산으로 대신
        private bool ResolveTile(Vector3 point, out FarmTile tile)
        {
            tile = null;
            Ray down = new Ray(point + Vector3.up * 20f, Vector3.down);
            RaycastHit[] hits = Physics.RaycastAll(down, 40f, groundRaycastMask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit h in hits)
            {
                if (IsPlayerCollider(h.collider)) continue;
                tile = h.collider.GetComponentInParent<FarmTile>();
                if (tile != null) return true;
                break; // 가장 위에 있는 땅이 밭이 아니면 (길, 나무 등) 좌표 계산으로 넘어감
            }
            return FarmGrid.Instance != null && FarmGrid.Instance.TryGetTileAtWorldPosition(point, out tile);
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
            tile = hit.collider.GetComponentInParent<FarmTile>();
            if (tile == null && !ResolveTile(hit.point, out tile)) return false;

            hitPoint = hit.point;
            inRange = DistanceToTileEdge(tile) <= maxInteractDistance;
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
                    return tile.State == TileState.Natural && IsTileClear(tile) ? FarmActionType.Till : FarmActionType.None;

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