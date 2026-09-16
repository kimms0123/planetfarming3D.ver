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
    /// 자식 비주얼(모델)만 교체한다.
    /// </summary>
    public class FarmTile : MonoBehaviour
    {
        [Header("상태 (읽기 전용, 디버그용)")]
        [SerializeField] private TileState state = TileState.Natural;
        [SerializeField] private bool isWatered = false;

        [Header("비주얼 프리팹")]
        [SerializeField] private GameObject naturalVisualPrefab;
        [SerializeField] private GameObject tilledVisualPrefab;
        [SerializeField] private GameObject wateredVisualPrefab;

        [Header("작물")]
        [SerializeField] private Transform cropAnchor;
        [Tooltip("작물 크기 배율. 1 = 밭 블록과 동일 배율, 값을 올리면 더 크게, 내리면 더 작게 표시")]
        [SerializeField] private float cropScaleMultiplier = 1f;

        private GameObject currentVisualObject;
        private bool isInitialized = false;

        public Vector2Int GridPosition { get; private set; }
        public TileState State => state;
        public bool IsWatered => isWatered;
        public CropInstance CurrentCrop { get; private set; }

        private void OnEnable()
        {
            if (GameClock.Instance != null)
                GameClock.Instance.OnDayChanged += HandleDayChanged;
        }

        private void OnDisable()
        {
            if (GameClock.Instance != null)
                GameClock.Instance.OnDayChanged -= HandleDayChanged;
        }

        private void Start()
        {
            if (!isInitialized)
                RefreshVisual();
        }

        /// <summary>WorldMapPainter가 생성 시점에 좌표를 부여할 때 호출</summary>
        public void Setup(Vector2Int gridPosition)
        {
            GridPosition = gridPosition;
            isInitialized = true;
            DisableOwnRenderer();
            RefreshVisual();
        }

        /// <summary>비주얼 프리팹을 코드에서 주입할 때 사용 (WorldMapPainter가 생성 시 호출)</summary>
        public void SetVisualPrefabs(GameObject natural, GameObject tilled, GameObject watered)
        {
            naturalVisualPrefab = natural;
            tilledVisualPrefab = tilled;
            wateredVisualPrefab = watered;
        }

        /// <summary>작물 크기 배율을 코드에서 주입 (WorldMapPainter가 생성 시 호출)</summary>
        public void SetCropScaleMultiplier(float multiplier)
        {
            cropScaleMultiplier = multiplier;
        }

        /// <summary>
        /// 이 오브젝트 자신은 논리 컨테이너 역할만 하고, 실제 비주얼은 자식(currentVisualObject)만 담당한다.
        /// </summary>
        private void DisableOwnRenderer()
        {
            Renderer ownRenderer = GetComponent<Renderer>();
            if (ownRenderer != null)
                ownRenderer.enabled = false;
        }

        /// <summary>밭 갈기. Natural 상태에서만 가능. 성공하면 true.</summary>
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

        /// <summary>씨앗 심기. Tilled 상태이고 아직 작물이 없을 때만 가능.</summary>
        public bool Plant(CropData cropData, Transform cropParent)
        {
            if (state != TileState.Tilled)
            {
                Debug.Log($"[씨앗심기 실패] {name} - 갈아진 밭이 아님 (현재: {state})");
                return false;
            }
            if (cropData == null)
            {
                Debug.Log("[씨앗심기 실패] 장착된 씨앗(CropData)이 없음");
                return false;
            }
            if (CurrentCrop != null)
            {
                Debug.Log($"[씨앗심기 실패] {name} - 이미 작물이 심어져 있음");
                return false;
            }

            GameObject cropObj = new GameObject($"Crop_{cropData.cropName}");
            Transform anchor = cropAnchor != null ? cropAnchor : transform;
            cropObj.transform.SetParent(cropParent != null ? cropParent : anchor);
            cropObj.transform.position = GetSurfacePosition();
            cropObj.transform.localScale = transform.lossyScale * cropScaleMultiplier;

            CurrentCrop = cropObj.AddComponent<CropInstance>();
            CurrentCrop.Initialize(cropData);

            Debug.Log($"[씨앗심기 성공] {name} (좌표 {GridPosition}) - 씨앗이 심겼습니다: {cropData.cropName}");
            return true;
        }

        /// <summary>물 주기. 매일 다시 줘야 하며, 그날 물을 줘야만 다음날 성장이 발생한다.</summary>
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

        /// <summary>
        /// 수확. 성장 완료된 작물이 있을 때만 가능. 성공 시 CropData를 반환하고 등급(quality)도 함께 넘긴다.
        /// TODO: 지금은 등급을 임시로 랜덤 판정한다 - 수확 리듬게임 시스템이 완성되면
        /// 그 결과(Perfect 비율 등)를 quality 인자로 그대로 대체하면 된다.
        /// </summary>
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

        /// <summary>임시 등급 판정 (Normal 60%, Good 30%, Perfect 10%). 리듬게임 완성 전까지 사용.</summary>
        private ItemQuality RollHarvestQuality()
        {
            float roll = UnityEngine.Random.value;
            if (roll < 0.10f) return ItemQuality.Perfect;
            if (roll < 0.40f) return ItemQuality.Good;
            return ItemQuality.Normal;
        }

        /// <summary>
        /// 하루가 바뀔 때 호출됨.
        /// 1) 오늘 물을 줬다면 작물을 하루치 성장시킨다.
        /// 2) 물 상태는 다음날을 위해 초기화한다.
        /// </summary>
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
            GameObject targetPrefab = ChoosePrefab();
            if (targetPrefab == null) return;

            if (currentVisualObject != null)
                Destroy(currentVisualObject);

            currentVisualObject = Instantiate(targetPrefab, transform.position, transform.rotation, transform);
        }

        private GameObject ChoosePrefab()
        {
            if (state == TileState.Natural)
                return naturalVisualPrefab;

            if (isWatered && wateredVisualPrefab != null)
                return wateredVisualPrefab;

            return tilledVisualPrefab != null ? tilledVisualPrefab : naturalVisualPrefab;
        }

        /// <summary>
        /// 현재 타일 비주얼(currentVisualObject)의 실제 윗면 좌표를 Renderer.bounds 기반으로 계산한다.
        /// 모델의 Pivot 위치에 의존하지 않으므로, 어떤 에셋을 갈아끼워도 작물이 항상 표면 위에 정확히 얹힌다.
        /// </summary>
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