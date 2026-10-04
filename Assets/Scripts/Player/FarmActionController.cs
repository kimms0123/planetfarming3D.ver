using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using FarmingSystem.Core;
using FarmingSystem.Farming;
using FarmingSystem.Inventory;

namespace FarmingSystem.Player
{
    /// <summary>
    /// 마우스 좌클릭 시 클릭한 타일 상황에 맞는 농사 행동을 실행한다.
    /// 우선순위: 수확 가능한 작물 수확 > (갈아진 빈 밭 + 핫바에 씨앗 선택됨) 씨앗 심기 > 현재 도구(호미/물뿌리개) 행동
    /// 어떤 도구/씨앗을 들고 있는지는 InventoryManager.Instance에서 매번 조회한다.
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

        [Header("행동별 소요 시간(분) - 기획서 기준")]
        [SerializeField] private int tillMinutes = 30;
        [SerializeField] private int plantMinutes = 30;
        [SerializeField] private int waterMinutes = 20;

        private bool pointerOverUI;

        private void Awake()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;
        }

        private void Update()
        {
            // OnInteract(입력 이벤트 처리 중)에서 IsPointerOverGameObject를 직접 부르면
            // Input System이 경고를 내고 값도 부정확해서, 매 프레임 미리 저장해 둔다
            pointerOverUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
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

            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray ray = targetCamera.ScreenPointToRay(mousePos);

            if (!Physics.Raycast(ray, out RaycastHit hit, 100f, groundRaycastMask))
                return;

            float distance = Vector3.Distance(transform.position, hit.point);
            if (distance > maxInteractDistance)
                return;

            if (!FarmGrid.Instance.TryGetTileAtWorldPosition(hit.point, out FarmTile tile))
                return;

            InventoryManager inventory = InventoryManager.Instance;

            // 1순위: 수확
            if (tile.CurrentCrop != null && tile.CurrentCrop.IsReadyToHarvest)
            {
                // 등급은 수확 순간에 정해지므로, 어떤 등급이 나와도 들어갈 공간이 있을 때만 수확
                // (공간 없이 수확하면 못 넣은 수확물이 사라지기 때문)
                CropData readyCrop = tile.CurrentCrop.Data;
                int room = Mathf.Min(inventory.GetAddableAmount(readyCrop, ItemQuality.Normal),
                           Mathf.Min(inventory.GetAddableAmount(readyCrop, ItemQuality.Good),
                                     inventory.GetAddableAmount(readyCrop, ItemQuality.Perfect)));
                if (room < readyCrop.expectedYield)
                {
                    Debug.Log($"[수확 보류] 가방 공간 부족 ({readyCrop.expectedYield}개 필요, {room}개 가능)");
                    return;
                }

                CropData harvested = tile.Harvest(out ItemQuality quality);
                if (harvested != null)
                {
                    int notAdded = inventory.AddItem(harvested, harvested.expectedYield, quality);
                    Debug.Log($"[수확] {harvested.cropName} x{harvested.expectedYield} ({quality}) 수확 완료, 인벤토리에 못 넣은 수량: {notAdded}");
                }
                return;
            }

            // 2순위: 갈아진 빈 밭 + 핫바에 씨앗(SeedData)이 선택되어 있으면 심기
            // CurrentSeed는 선택된 아이템이 SeedData일 때만 값을 반환하므로,
            // 수확물(CropData)을 핫바에 들고 있으면 여기서 걸러진다.
            SeedData selectedSeed = inventory.CurrentSeed;
            if (tile.State == TileState.Tilled && tile.CurrentCrop == null && selectedSeed != null)
            {
                if (tile.Plant(selectedSeed, cropParentContainer))
                {
                    // 심은 씨앗 1개 소모
                    inventory.RemoveFromSlot(inventory.SelectedHotbarIndex, 1);
                    Debug.Log($"[씨앗심기] {selectedSeed.itemName} 심기 성공");
                    GameClock.Instance?.AdvanceMinutes(plantMinutes);
                    return;
                }
            }

            // 3순위: 현재 도구 행동
            ToolType currentTool = inventory.CurrentTool;
            Debug.Log($"[도구행동 시도] 타일: {tile.name}, State: {tile.State}, 현재 도구: {currentTool}");

            switch (currentTool)
            {
                case ToolType.Hoe:
                    bool canTill = FarmPlacementValidator.Instance == null || FarmPlacementValidator.Instance.CanTillAt(hit.point);
                    if (canTill && tile.TryTill())
                        GameClock.Instance?.AdvanceMinutes(tillMinutes);
                    break;

                case ToolType.WateringCan:
                    if (tile.Water())
                        GameClock.Instance?.AdvanceMinutes(waterMinutes);
                    break;
            }
        }
    }
}