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
        Plant,
        Till,
        Water
    }

    /// <summary>
    /// 마우스 좌클릭 시 클릭한 타일 상황에 맞는 농사 행동을 실행한다.
    /// 우선순위: 수확 가능한 작물 수확 > (갈아진 빈 밭 + 핫바에 씨앗 선택됨) 씨앗 심기 > 현재 도구(호미/물뿌리개) 행동
    /// 대상 타일 선택:
    /// - 커서가 잠겨 있을 때(마우스로 화면을 돌리는 중) = 플레이어가 바라보는 방향 바로 앞 타일
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

        [Header("대상 타일 선택 (커서가 잠겨 있을 때)")]
        [Tooltip("플레이어 앞 몇 미터 지점의 타일을 대상으로 할지 (타일 1칸 크기 정도)")]
        [SerializeField] private float frontTileDistance = 1f;

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

        /// <summary>PlayerInput(Send Messages)이 Interact(좌클릭)를 받을 때 자동 호출</summary>
        public void OnInteract(InputValue value)
        {
            if (!value.isPressed) return;

            // 상점 등 UI가 열려 있거나, 마우스가 UI(핫바 등) 위에 있으면 농사 행동 안 함
            if (UIInputBlocker.IsBlocked || pointerOverUI) return;

            if (FarmGrid.Instance == null || targetCamera == null || InventoryManager.Instance == null)
            {
                Debug.Log("[농사행동 실패] 필요한 매니저(FarmGrid/InventoryManager)가 씬에 없음");
                return;
            }

            if (!TryGetTargetTile(out FarmTile tile, out Vector3 hitPoint, out bool inRange)) return;
            if (!inRange) return;

            InventoryManager inventory = InventoryManager.Instance;

            switch (GetActionFor(tile, hitPoint))
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
                    if (tile.CurrentCrop != null && tile.CurrentCrop.IsReadyToHarvest)
                        Debug.Log($"[수확 보류] 가방 공간 부족 ({tile.CurrentCrop.Data.cropName} {tile.CurrentCrop.Data.expectedYield}개를 넣을 자리가 없음)");
                    break;
            }
        }

        private static bool IsCursorLocked => Cursor.lockState == CursorLockMode.Locked;

        /// <summary>커서 상태에 따라 플레이어 앞 타일 또는 마우스 아래 타일을 대상으로 고른다.</summary>
        private bool TryGetTargetTile(out FarmTile tile, out Vector3 hitPoint, out bool inRange)
        {
            return IsCursorLocked
                ? TryGetTileInFront(out tile, out hitPoint, out inRange)
                : TryGetTileUnderMouse(out tile, out hitPoint, out inRange);
        }

        /// <summary>플레이어가 바라보는 방향 바로 앞의 타일. 항상 거리 안으로 취급한다.</summary>
        private bool TryGetTileInFront(out FarmTile tile, out Vector3 hitPoint, out bool inRange)
        {
            tile = null;
            hitPoint = Vector3.zero;
            inRange = false;

            if (FarmGrid.Instance == null) return false;

            Vector3 facing = playerController != null ? playerController.FacingDirection : transform.forward;
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.0001f) facing = Vector3.forward;

            Vector3 probe = transform.position + facing.normalized * frontTileDistance;
            if (!FarmGrid.Instance.TryGetTileAtWorldPosition(probe, out tile)) return false;

            hitPoint = tile.transform.position;
            inRange = true;
            return true;
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
            inRange = Vector3.Distance(transform.position, hit.point) <= maxInteractDistance;
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

            // 2순위: 갈아진 빈 밭 + 핫바에 씨앗(SeedData) 선택
            // CurrentSeed는 선택된 아이템이 SeedData일 때만 값을 반환하므로 수확물(CropData)은 여기서 걸러진다
            if (tile.State == TileState.Tilled && tile.CurrentCrop == null && inventory.CurrentSeed != null)
                return FarmActionType.Plant;

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