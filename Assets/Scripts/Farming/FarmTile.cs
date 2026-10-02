using UnityEngine;
using FarmingSystem.Core;
using FarmingSystem.Inventory;

namespace FarmingSystem.Farming
{
    public enum TileState
    {
        Natural,   // 자연 상태 (아직 갈지 않음)
        Tilled     // 갈아진 상태 (씨앗 심기 가능)
    }

    /// <summary>
    /// 월드에 배치된 땅 블록 1칸의 상태를 관리한다.
    /// 호미질해도 오브젝트를 삭제/재생성하지 않고, 같은 오브젝트가 상태만 바꾸고
    /// 자식 비주얼(모델)만 교체한다. 물 준 상태는 별도 프리팹 대신 색상 틴트로 표현한다.
    /// </summary>
    public class FarmTile : MonoBehaviour
    {
        [Header("상태 (읽기 전용, 디버그용)")]
        [SerializeField] private TileState state = TileState.Natural;
        [SerializeField] private bool isWatered = false;

        [Header("비주얼 프리팹")]
        [SerializeField] private GameObject naturalVisualPrefab;
        [SerializeField] private GameObject tilledVisualPrefab;
        [Tooltip("물 준 상태일 때 곱해질 색상 (어둡게+갈색 톤으로). 흰색(1,1,1,1)이면 변화 없음")]
        [SerializeField] private Color wateredTintMultiplier = new Color(0.55f, 0.4f, 0.3f, 1f);

        [Header("작물")]
        [SerializeField] private Transform cropAnchor;
        [Tooltip("작물 크기 배율. 1 = 밭 블록과 동일 배율, 값을 올리면 더 크게, 내리면 더 작게 표시")]
        [SerializeField] private float cropScaleMultiplier = 1f;

        private GameObject currentVisualObject;
        private bool isInitialized = false;
        private bool isSubscribedToClock = false;

        public Vector2Int GridPosition { get; private set; }
        public TileState State => state;
        public bool IsWatered => isWatered;
        public CropInstance CurrentCrop { get; private set; }

        private void OnEnable()
        {
            TrySubscribeToClock();
        }

        private void OnDisable()
        {
            if (GameClock.Instance != null)
                GameClock.Instance.OnDayChanged -= HandleDayChanged;
        }

        private void Start()
        {
            TrySubscribeToClock();
            if (!isInitialized)
                RefreshVisual();
        }

        private void TrySubscribeToClock()
        {
            if (isSubscribedToClock) return;
            if (GameClock.Instance == null) return;

            GameClock.Instance.OnDayChanged += HandleDayChanged;
            isSubscribedToClock = true;
        }

        public void Setup(Vector2Int gridPosition)
        {
            GridPosition = gridPosition;
            isInitialized = true;
            DisableOwnRenderer();
            RefreshVisual();
        }

        public void SetVisualPrefabs(GameObject natural, GameObject tilled)
        {
            naturalVisualPrefab = natural;
            tilledVisualPrefab = tilled;
        }

        public void SetWateredTint(Color tint)
        {
            wateredTintMultiplier = tint;
        }

        public void SetCropScaleMultiplier(float multiplier)
        {
            cropScaleMultiplier = multiplier;
        }

        private void DisableOwnRenderer()
        {
            Renderer ownRenderer = GetComponent<Renderer>();
            if (ownRenderer != null)
                ownRenderer.enabled = false;
        }

        public bool TryTill()
        {
            if (state != TileState.Natural)
            {
                Debug.Log($"[밭갈기 실패] {name} - 이미 Natural 상태가 아님 (현재: {state})");
                return false;
            }

            state = TileState.Tilled;
            RefreshVisual();
            Debug.Log($"[밭갈기 성공] {name} (좌표 {GridPosition}) - 밭이 갈렸습니다.");
            return true;
        }

        /// <summary>
        /// 씨앗 심기. SeedData를 받아서, 그 씨앗이 자라날 작물(resultCrop)의 성장 데이터를 사용한다.
        /// Tilled 상태이고 아직 작물이 없을 때만 가능.
        /// </summary>
        public bool Plant(SeedData seedData, Transform cropParent)
        {
            if (state != TileState.Tilled)
            {
                Debug.Log($"[씨앗심기 실패] {name} - 갈아진 밭이 아님 (현재: {state})");
                return false;
            }
            if (seedData == null || seedData.resultCrop == null)
            {
                Debug.Log("[씨앗심기 실패] 장착된 씨앗이 없거나, 씨앗에 연결된 작물(resultCrop)이 비어있음");
                return false;
            }
            if (CurrentCrop != null)
            {
                Debug.Log($"[씨앗심기 실패] {name} - 이미 작물이 심어져 있음");
                return false;
            }

            CropData cropData = seedData.resultCrop;

            GameObject cropObj = new GameObject($"Crop_{cropData.cropName}");
            Transform anchor = cropAnchor != null ? cropAnchor : transform;
            cropObj.transform.SetParent(cropParent != null ? cropParent : anchor);
            cropObj.transform.position = GetSurfacePosition();
            cropObj.transform.localScale = transform.lossyScale * cropScaleMultiplier;

            CurrentCrop = cropObj.AddComponent<CropInstance>();
            CurrentCrop.Initialize(cropData);

            Debug.Log($"[씨앗심기 성공] {name} (좌표 {GridPosition}) - {seedData.itemName}을(를) 심었습니다 -> {cropData.cropName}로 자람");
            return true;
        }

        public bool Water()
        {
            if (state != TileState.Tilled)
            {
                Debug.Log($"[물주기 실패] {name} - 갈아진 밭이 아님 (현재: {state})");
                return false;
            }
            if (isWatered)
            {
                Debug.Log($"[물주기 실패] {name} - 오늘 이미 물을 줬음");
                return false;
            }

            isWatered = true;
            RefreshVisual();
            Debug.Log($"[물주기 성공] {name} (좌표 {GridPosition}) - 물을 주었습니다.");
            return true;
        }

        public CropData Harvest(out ItemQuality quality)
        {
            quality = ItemQuality.Normal;

            if (CurrentCrop == null)
            {
                Debug.Log($"[수확 실패] {name} - 심어진 작물이 없음");
                return null;
            }
            if (!CurrentCrop.IsReadyToHarvest)
            {
                Debug.Log($"[수확 실패] {name} - 아직 성장 중 ({CurrentCrop.CurrentGrowthDay}/{CurrentCrop.Data.totalGrowthDays}일차)");
                return null;
            }

            CropData harvested = CurrentCrop.Data;
            quality = RollHarvestQuality();
            Destroy(CurrentCrop.gameObject);
            CurrentCrop = null;

            isWatered = false;
            RefreshVisual();
            Debug.Log($"[수확 성공] {name} (좌표 {GridPosition}) - {harvested.cropName} 수확 완료 (등급: {quality}).");
            return harvested;
        }

        private ItemQuality RollHarvestQuality()
        {
            float roll = UnityEngine.Random.value;
            if (roll < 0.10f) return ItemQuality.Perfect;
            if (roll < 0.40f) return ItemQuality.Good;
            return ItemQuality.Normal;
        }

        private void HandleDayChanged(int day, Season season)
        {
            if (CurrentCrop != null)
            {
                if (isWatered)
                {
                    CurrentCrop.Grow();
                }
                else
                {
                    Debug.Log($"[성장 없음] {name} - 오늘 물을 안 줘서 성장하지 않음");
                }
            }

            isWatered = false;
            RefreshVisual();
        }

        private void RefreshVisual()
        {
            GameObject targetPrefab = state == TileState.Natural ? naturalVisualPrefab : tilledVisualPrefab;
            if (targetPrefab == null) return;

            if (currentVisualObject != null)
                Destroy(currentVisualObject);

            currentVisualObject = Instantiate(targetPrefab, transform.position, transform.rotation, transform);

            if (state == TileState.Tilled && isWatered)
                ApplyWateredTint();
        }

        private void ApplyWateredTint()
        {
            if (currentVisualObject == null) return;

            Renderer[] renderers = currentVisualObject.GetComponentsInChildren<Renderer>();
            foreach (Renderer r in renderers)
            {
                Material mat = r.material;
                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", mat.GetColor("_BaseColor") * wateredTintMultiplier);
                else if (mat.HasProperty("_Color"))
                    mat.color = mat.color * wateredTintMultiplier;
            }
        }

        private Vector3 GetSurfacePosition()
        {
            if (currentVisualObject != null)
            {
                Renderer[] renderers = currentVisualObject.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    float maxY = renderers[0].bounds.max.y;
                    foreach (Renderer r in renderers)
                        maxY = Mathf.Max(maxY, r.bounds.max.y);

                    Vector3 pos = transform.position;
                    pos.y = maxY;
                    return pos;
                }
            }

            Debug.LogWarning($"[표면 계산 실패] {name} - currentVisualObject에 Renderer가 없어 기본 위치 사용");
            return transform.position;
        }
    }
}