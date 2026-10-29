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
    /// 하루가 바뀔 때 심어진 작물이 새 계절에 맞지 않으면 시들게 하고, 시든 작물은 ClearWitheredCrop()으로 제거한다.
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

        [Tooltip("간 밭 모델 위치 보정 (맵 생성기가 모델 바닥 높이를 재서 자동으로 채움)")]
        [SerializeField] private Vector3 tilledVisualOffset = Vector3.zero;

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

        public void SetTilledVisualOffset(Vector3 offset)
        {
            tilledVisualOffset = offset;
        }

        public void SetWateredTint(Color tint)
        {
            wateredTintMultiplier = tint;
        }

        public void SetCropScaleMultiplier(float multiplier)
        {
            cropScaleMultiplier = multiplier;
        }

        // 원래 블록 모델 숨기기 (자연 상태 비주얼이 대신 나타나므로)
        // 예전엔 자기 자신의 Renderer만 껐는데, 맵 생성기가 만든 블록은 모델이 "자식"에 있어서 안 꺼졌다.
        // 그래서 원래 블록 + 자연 비주얼 + (갈면) 밭 블록이 같은 자리에 겹쳐 깜빡이거나, 갈아도 잔디가 그대로 보였음
        // 자연 비주얼이 없으면(매끈한 지면 모드) 원래 모델을 그대로 둔다 (생성기가 이미 숨겨둠)
        private void DisableOwnRenderer()
        {
            if (naturalVisualPrefab == null)
            {
                Renderer ownRenderer = GetComponent<Renderer>();
                if (ownRenderer != null) ownRenderer.enabled = false;
                return;
            }

            foreach (Renderer r in GetComponentsInChildren<Renderer>())
            {
                if (CurrentCrop != null && r.transform.IsChildOf(CurrentCrop.transform)) continue;
                r.enabled = false;
            }
        }

        // 이 칸 땅의 실제 범위 (블록 콜라이더 기준, 작물·밭 모델 제외)
        // 블록 모델의 기준점(pivot)이 칸 가운데가 아닐 수 있어서 transform.position 대신 이걸 써야
        // 하이라이트 위치·거리 계산이 실제로 보이는 칸과 맞는다
        public Bounds GetGroundBounds()
        {
            bool found = false;
            Bounds b = new Bounds(transform.position, Vector3.one);

            foreach (Collider c in GetComponentsInChildren<Collider>())
            {
                if (!c.enabled || c.isTrigger) continue;
                if (CurrentCrop != null && c.transform.IsChildOf(CurrentCrop.transform)) continue;
                if (currentVisualObject != null && c.transform.IsChildOf(currentVisualObject.transform)) continue;
                if (!found) { b = c.bounds; found = true; }
                else b.Encapsulate(c.bounds);
            }

            // 콜라이더가 없으면 보이는 모델 크기로 대신
            if (!found)
            {
                foreach (Renderer r in GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled) continue;
                    if (CurrentCrop != null && r.transform.IsChildOf(CurrentCrop.transform)) continue;
                    if (!found) { b = r.bounds; found = true; }
                    else b.Encapsulate(r.bounds);
                }
            }
            return b;
        }

        // 칸 윗면 가운데 (거리 계산, 캐릭터가 바라볼 방향, 작물 위치 기준)
        public Vector3 Center
        {
            get
            {
                Bounds b = GetGroundBounds();
                return new Vector3(b.center.x, b.max.y, b.center.z);
            }
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

            if (SeasonUtil.TryGetCurrentSeason(out Season currentSeason) && !cropData.CanGrowIn(currentSeason))
            {
                Debug.Log($"[씨앗심기 실패] {cropData.cropName}은(는) {currentSeason}에 심을 수 없음 (가능 계절: {cropData.season})");
                return false;
            }

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

        // 예전 방식: 등급을 임시 랜덤으로 정해서 수확 (리듬게임이 씬에 없을 때 대비용)
        public CropData Harvest(out ItemQuality quality)
        {
            quality = RollHarvestQuality();
            return Harvest(quality);
        }

        // 리듬게임 결과처럼 이미 정해진 등급으로 수확
        public CropData Harvest(ItemQuality quality)
        {
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
            Destroy(CurrentCrop.gameObject);
            CurrentCrop = null;

            isWatered = false;
            RefreshVisual();
            Debug.Log($"[수확 성공] {name} (좌표 {GridPosition}) - {harvested.cropName} 수확 완료 (등급: {quality}).");
            return harvested;
        }

        /// <summary>시든 작물 제거. 지금은 맨손(아무 도구)으로 가능, 나중에 낫 전용으로 바꿀 예정.</summary>
        public bool ClearWitheredCrop()
        {
            if (CurrentCrop == null || !CurrentCrop.IsWithered)
            {
                Debug.Log($"[시든 작물 제거 실패] {name} - 시든 작물이 없음");
                return false;
            }

            string cropName = CurrentCrop.Data.cropName;
            Destroy(CurrentCrop.gameObject);
            CurrentCrop = null;
            RefreshVisual();
            Debug.Log($"[시든 작물 제거] {name} (좌표 {GridPosition}) - 시든 {cropName}을(를) 치웠습니다. 밭은 갈린 상태로 남아 바로 다시 심을 수 있어요.");
            return true;
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
            // 다 자란 채로 하루가 지났으면 방치 일수 +1 (오늘 자라서 다 자란 경우는 아래 Grow 이후라 포함 안 됨)
            if (CurrentCrop != null) CurrentCrop.MarkReadyDayPassed();

            if (CurrentCrop != null && !CurrentCrop.IsWithered)
            {
                // 계절이 지나 이 작물의 계절이 아니면 시듦 (그날은 성장하지 않음)
                if (!CurrentCrop.Data.CanGrowIn(season))
                {
                    CurrentCrop.Wither();
                }
                else if (isWatered)
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

            Vector3 visualPos = transform.position + (state == TileState.Tilled ? tilledVisualOffset : Vector3.zero);
            currentVisualObject = Instantiate(targetPrefab, visualPos, transform.rotation, transform);
            // 계절 블록이면 지금 계절 색으로 맞춤 (씬에 계절 블록 교체기가 있을 때만)
            FarmingSystem.World.SeasonalBlockSwapper.ApplyTo(currentVisualObject);

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

                    Vector3 pos = Center;
                    pos.y = maxY;
                    return pos;
                }
            }

            Debug.LogWarning($"[표면 계산 실패] {name} - currentVisualObject에 Renderer가 없어 블록 윗면 위치 사용");
            return Center;
        }
    }
}