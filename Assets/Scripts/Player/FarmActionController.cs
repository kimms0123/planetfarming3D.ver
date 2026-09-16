using UnityEngine;
using UnityEngine.InputSystem;
using FarmingSystem.Core;
using FarmingSystem.Farming;
using FarmingSystem.Inventory;

namespace FarmingSystem.Player
{
    /// <summary>
    /// 마우스 우클릭 한 번으로 상황에 맞는 농사 행동을 실행한다.
    /// 우선순위: 수확 가능한 작물 수확 > (갈아진 빈 밭 + 핫바에 씨앗 선택됨) 씨앗 심기 > 장착 도구(호미/물뿌리개) 행동
    /// 어떤 도구/씨앗을 쓸지는 InventoryManager.Instance에서 매번 조회한다.
    /// </summary>
    public class FarmActionController : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] private Transform cropParentContainer;
        [SerializeField] private Camera targetCamera;

        [Header("상호작용 설정")]
        [Tooltip("플레이어로부터 이 거리 이내의 땅만 상호작용 가능")]
        [SerializeField] private float maxInteractDistance = 3f;
        [SerializeField] private LayerMask groundRaycastMask;

        [Header("행동별 소요 시간(분) - 기획서 기준")]
        [SerializeField] private int tillMinutes = 30;
        [SerializeField] private int plantMinutes = 30;
        [SerializeField] private int waterMinutes = 20;

        private void Awake()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;
        }

        /// <summary>PlayerInput(Send Messages)이 Interact(우클릭)가 눌릴 때 자동 호출</summary>
        public void OnInteract(InputValue value)
        {
            if (!value.isPressed) return;

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

            // 1순위: 수확
            if (tile.CurrentCrop != null && tile.CurrentCrop.IsReadyToHarvest)
            {
                CropData harvested = tile.Harvest(out ItemQuality quality);
                if (harvested != null)
                {
                    int notAdded = InventoryManager.Instance.AddItem(harvested, harvested.expectedYield, quality);
                    Debug.Log($"[수확] {harvested.cropName} x{harvested.expectedYield} ({quality}) 수확 완료, 인벤토리에 못 넣은 수량: {notAdded}");
                }
                return;
            }

            // 2순위: 갈아진 빈 밭 + 핫바에 씨앗이 선택되어 있으면 심기
            CropData selectedSeed = InventoryManager.Instance.CurrentSeed;
            if (tile.State == TileState.Tilled && tile.CurrentCrop == null && selectedSeed != null)
            {
                if (tile.Plant(selectedSeed, cropParentContainer))
                {
                    Debug.Log($"[씨앗심기] {selectedSeed.cropName} 심기 성공");
                    GameClock.Instance?.AdvanceMinutes(plantMinutes);
                    return;
                }
            }

            // 3순위: 장착 도구 행동
            ToolType currentTool = InventoryManager.Instance.CurrentTool;
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