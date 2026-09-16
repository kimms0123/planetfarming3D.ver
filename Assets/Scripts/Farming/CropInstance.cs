using UnityEngine;

namespace FarmingSystem.Farming
{
    /// <summary>
    /// FarmTile 위에 심어진 작물 1개체의 런타임 상태.
    /// 성장 자체는 FarmTile.HandleDayChanged()가 하루가 바뀔 때 Grow()를 호출해 진행시킨다.
    /// (물 안 준 날은 FarmTile이 아예 Grow()를 호출하지 않음 -> "그날 성장 없음" 규칙)
    /// </summary>
    public class CropInstance : MonoBehaviour
    {
        public CropData Data { get; private set; }
        public int CurrentGrowthDay { get; private set; }
        public bool IsReadyToHarvest => CurrentGrowthDay >= Data.totalGrowthDays;

        private GameObject currentStageObject;
        private int currentStageIndex = -1;

        public void Initialize(CropData data)
        {
            Data = data;
            CurrentGrowthDay = 0;
            Debug.Log($"[작물 초기화] {Data.cropName} - 성장 스테이지 프리팹 {(Data.growthStagePrefabs != null ? Data.growthStagePrefabs.Length : 0)}개 등록됨");
            RefreshVisual();
        }

        /// <summary>하루치 성장 진행. 물을 준 날에만 FarmTile이 호출한다.</summary>
        public void Grow()
        {
            if (IsReadyToHarvest) return;

            CurrentGrowthDay++;
            Debug.Log($"{Data.cropName} 성장: {CurrentGrowthDay}/{Data.totalGrowthDays}일차");
            RefreshVisual();

            if (IsReadyToHarvest)
                Debug.Log($"{Data.cropName} 수확 가능 상태가 되었습니다!");
        }

        /// <summary>성장 이벤트(리듬게임) 성공 시 성장일을 앞당기는 등 외부 보정용 훅</summary>
        public void ApplyGrowthBonus(int extraDays)
        {
            CurrentGrowthDay = Mathf.Min(Data.totalGrowthDays, CurrentGrowthDay + extraDays);
            RefreshVisual();
        }

        private void RefreshVisual()
        {
            if (Data.growthStagePrefabs == null || Data.growthStagePrefabs.Length == 0)
            {
                Debug.LogWarning($"[비주얼 갱신 실패] {Data.cropName} - growthStagePrefabs가 비어있음 (CropData 에셋에서 배열을 채워야 함)");
                return;
            }

            int stage = Data.GetStageIndex(CurrentGrowthDay);
            Debug.Log($"[비주얼 갱신 시도] {Data.cropName} - CurrentGrowthDay: {CurrentGrowthDay}, 계산된 stage: {stage}, 이전 stage: {currentStageIndex}");

            if (stage == currentStageIndex)
            {
                Debug.Log("[비주얼 갱신 스킵] stage가 이전과 동일해서 교체 안 함");
                return;
            }

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
                Debug.Log($"[비주얼 갱신 성공] {prefab.name} 인스턴스 생성됨 (월드 위치: {currentStageObject.transform.position}, 부모 스케일: {transform.lossyScale})");
            }
            currentStageIndex = stage;
        }
    }
}