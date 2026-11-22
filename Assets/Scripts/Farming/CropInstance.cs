using UnityEngine;
using FarmingSystem.Effects;

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
        // 회오리 피해를 입은 작물 색 (시든 것보다는 덜 칙칙하게, 살짝 누렇고 어둡게)
        private static readonly Color StormDamagedTint = new Color(0.75f, 0.68f, 0.5f, 1f);

        public CropData Data { get; private set; }
        public int CurrentGrowthDay { get; private set; }
        public bool IsWithered { get; private set; }
        // 회오리에 휩쓸렸지만 살아남은 작물 → 수확할 때 등급이 낮아짐 (HarvestRhythmGame / FarmTile.Harvest가 반영)
        public bool IsStormDamaged { get; private set; }
        // 다 자란 뒤 수확하지 않고 지난 날 수 (수확 리듬게임 난이도에 반영: 오래 둘수록 어려움)
        public int DaysSinceReady { get; private set; }
        public bool IsReadyToHarvest => !IsWithered && CurrentGrowthDay >= Data.totalGrowthDays;
        /// <summary>이 작물이 심긴 밭에 뿌려져 있던 비료 (없으면 null). 수확 등급·개수 계산이 읽음</summary>
        public FertilizerData Fertilizer { get; set; }

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

        /// <summary>저장 불러오기: 성장일·시듦·회오리 피해·방치 일수를 그대로 되돌림 (반짝이 없이 바로 그 모습으로)</summary>
        public void RestoreSave(CropData data, int growthDay, bool withered, bool stormDamaged, int daysSinceReady)
        {
            Data = data;
            CurrentGrowthDay = Mathf.Clamp(growthDay, 0, data.totalGrowthDays);
            IsWithered = false;
            IsStormDamaged = stormDamaged;
            DaysSinceReady = Mathf.Max(0, daysSinceReady);
            currentStageIndex = -1;
            RefreshVisual();           // 처음 만드는 모습이라 반짝이 없음 (회오리 피해 색도 여기서 칠해짐)
            if (withered) Wither();    // 시든 모습으로
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
                FitIntoSoil(currentStageObject, int.MaxValue, 1);
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

        // 다 자란 상태로 하루가 지남 (FarmTile이 하루가 바뀔 때 호출)
        public void MarkReadyDayPassed()
        {
            if (IsReadyToHarvest) DaysSinceReady++;
        }

        /// <summary>회오리 피해 표시 (살아남았지만 품질이 나빠짐). 작물 색이 살짝 누렇게 변함</summary>
        public void MarkStormDamaged()
        {
            if (IsWithered || IsStormDamaged) return;
            IsStormDamaged = true;
            TintCurrent(StormDamagedTint);
            Debug.Log($"[회오리 피해] {Data.cropName} - 간신히 살아남았지만 품질이 나빠졌어요");
        }

        private void TintCurrent(Color tint)
        {
            if (currentStageObject == null) return;
            foreach (Renderer r in currentStageObject.GetComponentsInChildren<Renderer>())
            {
                Material mat = r.material;
                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", mat.GetColor("_BaseColor") * tint);
                else if (mat.HasProperty("_Color"))
                    mat.color = mat.color * tint;
            }
        }

        [ContextMenu("디버그: 회오리 피해 입히기")]
        private void DebugStormDamage() => MarkStormDamaged();

        [ContextMenu("디버그: 지금 시들게 하기")]
        private void DebugWither() => Wither();

        // ───────── 흙에 묻히는 깊이 자동 맞춤 ─────────
        // 작물·단계마다 모델 크기가 제각각이라, 프리팹에 정해 둔 묻는 깊이(모든 작물 같은 깊이)로는 작은 씨앗이 흙에 다 파묻혀 안 보임
        // → 심은 모델의 실제 크기를 재서, 모델 키에 비해 너무 많이 묻혔으면 그만큼 올려 줌 (덜 묻힌 건 그대로 둠)
        //   씨앗(첫 단계) = 키의 최대 25%까지만 묻힘 / 중간 단계 = 10% / 다 자람·시듦 = 6%
        //   그리고 어떤 경우든 흙 위로 최소 MinVisibleMeters는 보이게
        // 흙 높이 = 작물을 놓은 자리와, 작물 발밑을 위에서 레이저로 쏴서 맞은 땅(밭 블록·갈린 흙) 중 더 높은 쪽
        //   (갈린 흙 모델·블록 모양에 따라 작물 기준점보다 실제 흙이 더 높이 올라와 있어서 묻혀 보이던 경우까지 잡음)
        private const float SeedMaxSink = 0.25f, MidMaxSink = 0.10f, MatureMaxSink = 0.06f;
        private const float MinVisibleMeters = 0.08f;
        // 씨앗(첫 단계) 최소 키: 작물 배율 1일 때 기준 (m). 작물 크기 배율(Crop Scale)을 키우면 같이 커짐
        //   옥수수·꼬불금귤·플레어꽃·해왕열매·배추·뭉게나무·빙하별꽃 씨앗 모델은 납작해서(모델 키 2~5cm) 갈린 흙 골에 가려 안 보임
        //   → 이보다 납작한 씨앗은 비율 그대로 키워 줌 (최대 MaxSeedBoost배)
        private const float MinSeedHeight = 0.06f;
        private const float MaxSeedBoost = 2.5f;
        // true = 심을 때·자랄 때 높이 계산 결과를 Console에 찍음 (묻히는 작물 찾기용). 다 확인하면 false로
        private static readonly bool LogSoilFit = true;
        private static readonly RaycastHit[] soilHits = new RaycastHit[16];

        private void FitIntoSoil(GameObject model, int stage, int stageCount)
        {
            if (model == null) return;
            if (!MeasureModel(model, out Bounds b)) return;

            // 납작한 씨앗은 최소 키까지 키움 (모양 비율은 그대로)
            float boost = 1f;
            float minSeed = MinSeedHeight * Mathf.Abs(transform.lossyScale.y);
            if (stage == 0 && b.size.y < minSeed)
            {
                boost = Mathf.Min(MaxSeedBoost, minSeed / b.size.y);
                model.transform.localScale *= boost;
                if (!MeasureModel(model, out b)) return;
            }

            float soil = Mathf.Max(transform.position.y, GroundTopUnder(b));
            float height = b.size.y;
            float sink = soil - b.min.y;                     // 지금 흙 아래로 들어간 깊이
            float ratio = stage == 0 ? SeedMaxSink : stage >= stageCount - 1 ? MatureMaxSink : MidMaxSink;
            float allowed = Mathf.Min(height * ratio, Mathf.Max(0f, height - MinVisibleMeters)); // 최소 이만큼은 보이게
            float raise = sink - allowed;
            if (raise > 0f) model.transform.position += Vector3.up * raise;

            if (LogSoilFit)
                Debug.Log($"[작물 높이] {(Data != null ? Data.cropName : name)} {stage + 1}단계 - 모델 키 {height:0.000}m{(boost > 1f ? $" (납작해서 {boost:0.00}배 키움)" : "")}, 흙 높이 {soil:0.000} " +
                          $"(놓은 자리 {transform.position.y:0.000}), 묻힌 깊이 {sink:0.000}m → {(raise > 0f ? $"{raise:0.000}m 올림" : "그대로")}");
        }

        private static bool MeasureModel(GameObject model, out Bounds b)
        {
            b = default;
            bool found = false;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || !r.enabled) continue;
                if (!found) { b = r.bounds; found = true; }
                else b.Encapsulate(r.bounds);
            }
            return found && b.size.y >= 0.001f;
        }

        // 작물 발밑 다섯 군데(가운데 + 둘레)를 위에서 아래로 쏴서, 작물 자신이 아닌 가장 높은 땅 높이
        private float GroundTopUnder(Bounds b)
        {
            float best = float.MinValue;
            float r = Mathf.Clamp(Mathf.Min(b.extents.x, b.extents.z) * 0.6f, 0.02f, 0.2f);
            Vector3 c = new Vector3(b.center.x, b.max.y + 2f, b.center.z);
            Vector3[] offsets = { Vector3.zero, new Vector3(r, 0f, 0f), new Vector3(-r, 0f, 0f), new Vector3(0f, 0f, r), new Vector3(0f, 0f, -r) };
            foreach (Vector3 o in offsets)
            {
                int n = Physics.RaycastNonAlloc(c + o, Vector3.down, soilHits, b.size.y + 4f, ~0, QueryTriggerInteraction.Ignore);
                float top = float.MinValue;
                for (int k = 0; k < n; k++)
                {
                    Transform t = soilHits[k].transform;
                    if (t == transform || t.IsChildOf(transform)) continue;                 // 작물 자신
                    if (t.GetComponentInParent<CropInstance>() != null) continue;          // 다른 작물
                    if (soilHits[k].point.y > b.max.y + 0.5f) continue;                    // 머리 위 지붕·나뭇가지 등
                    top = Mathf.Max(top, soilHits[k].point.y);
                }
                // 너무 높은 곳(옆 칸 벽 등)은 빼고, 놓은 자리 근처(±0.3m)만 흙으로 인정
                if (top > float.MinValue && Mathf.Abs(top - transform.position.y) <= 0.3f) best = Mathf.Max(best, top);
            }
            return best;
        }

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
            bool grewUp = currentStageIndex >= 0; // 처음 심을 때가 아니라 자라서 모습이 바뀐 경우

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
                FitIntoSoil(currentStageObject, stage, Data.growthStagePrefabs.Length);
            }
            currentStageIndex = stage;
            if (IsStormDamaged) TintCurrent(StormDamagedTint); // 자라서 모델이 바뀌어도 피해 색 유지
            if (grewUp && currentStageObject != null) FarmFX.CropGrew(currentStageObject, IsReadyToHarvest); // 톡 튀어나옴 + 반짝이
        }
    }
}