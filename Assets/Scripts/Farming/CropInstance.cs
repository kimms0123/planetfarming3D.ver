using UnityEngine;

namespace FarmingSystem.Farming
{
    /// <summary>
    /// FarmTile 위에 심어진 작물 1개체의 런타임 상태.
    /// 성장 자체는 FarmTile.HandleDayChanged()가 하루가 바뀔 때 Grow()를 호출해 진행시킨다.
    /// (물 안 준 날은 FarmTile이 아예 Grow()를 호출하지 않음 -> "그날 성장 없음" 규칙)
    /// 계절이 바뀌어 이 작물의 계절이 아니게 되면 FarmTile이 Wither()를 호출해 시들게 한다.
    /// 시든 작물은 더 자라지 않고 수확할 수 없으며, 제거만 가능하다.
    /// </summary>
    public class CropInstance : MonoBehaviour
    {
        private static readonly Color WitheredTint = new Color(0.45f, 0.33f, 0.2f, 1f);

        public CropData Data { get; private set; }
        public int CurrentGrowthDay { get; private set; }
        public bool IsWithered { get; private set; }
        public bool IsReadyToHarvest => !IsWithered && CurrentGrowthDay >= Data.totalGrowthDays;

        private GameObject currentStageObject;
        private int currentStageIndex = -1;

        public void Initialize(CropData data)
        {
            Data = data;
            CurrentGrowthDay = 0;
            IsWithered = false;
            Debug.Log($"[작물 초기화] {Data.cropName} - 성장 스테이지 프리팹 {(Data.growthStagePrefabs != null ? Data.growthStagePrefabs.Length : 0)}개 등록됨");
            RefreshVisual();
        }

        /// <summary>하루치 성장 진행. 물을 준 날에만 FarmTile이 호출한다.</summary>
        public void Grow()
        {
            if (IsWithered || IsReadyToHarvest) return;

            CurrentGrowthDay++;
            Debug.Log($"{Data.cropName} 성장: {CurrentGrowthDay}/{Data.totalGrowthDays}일차");
            RefreshVisual();

            if (IsReadyToHarvest)
                Debug.Log($"{Data.cropName} 수확 가능 상태가 되었습니다!");
        }

        /// <summary>성장 이벤트(리듬게임) 성공 시 성장일을 앞당기는 등 외부 보정용 훅</summary>
        public void ApplyGrowthBonus(int extraDays)
        {
            if (IsWithered) return;
            CurrentGrowthDay = Mathf.Min(Data.totalGrowthDays, CurrentGrowthDay + extraDays);
            RefreshVisual();
        }

        /// <summary>계절이 지나 시듦. 시든 모델이 있으면 교체, 없으면 현재 모델을 갈색으로 칠한다.</summary>
        public void Wither()
        {
            if (IsWithered) return;
            IsWithered = true;

            if (Data.witheredPrefab != null)
            {
                if (currentStageObject != null) Destroy(currentStageObject);
                currentStageObject = Instantiate(Data.witheredPrefab, transform.position, transform.rotation, transform);
                currentStageObject.transform.localScale = Vector3.one;
            }
            else if (currentStageObject != null)
            {
                foreach (Renderer r in currentStageObject.GetComponentsInChildren<Renderer>())
                {
                    Material mat = r.material;
                    if (mat.HasProperty("_BaseColor"))
                        mat.SetColor("_BaseColor", mat.GetColor("_BaseColor") * WitheredTint);
                    else if (mat.HasProperty("_Color"))
                        mat.color = mat.color * WitheredTint;
                }
            }

            Debug.Log($"[시듦] {Data.cropName} - 계절이 지나 시들었습니다. 클릭해서 제거하세요.");
        }

        [ContextMenu("디버그: 지금 시들게 하기")]
        private void DebugWither() => Wither();

        private void RefreshVisual()
        {
            if (IsWithered) return;

            if (Data.growthStagePrefabs == null || Data.growthStagePrefabs.Length == 0)
            {
                Debug.LogWarning($"[비주얼 갱신 실패] {Data.cropName} - growthStagePrefabs가 비어있음 (CropData 에셋에서 배열을 채워야 함)");
                return;
            }

            int stage = Data.GetStageIndex(CurrentGrowthDay);
            if (stage == currentStageIndex) return;

            if (currentStageObject != null)
                Destroy(currentStageObject);

            GameObject prefab = Data.growthStagePrefabs[stage];
            if (prefab == null)
            {
                Debug.LogWarning($"[비주얼 갱신 실패] growthStagePrefabs[{stage}]가 null (CropData Inspector에서 해당 슬롯이 비어있음)");
            }
            else
            {
                currentStageObject = Instantiate(prefab, transform.position, transform.rotation, transform);
                currentStageObject.transform.localScale = Vector3.one; // 부모(작물 오브젝트)의 스케일을 그대로 상속받도록 로컬 스케일은 1 유지
            }
            currentStageIndex = stage;
        }
    }
}