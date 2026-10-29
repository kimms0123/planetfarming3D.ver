using System.Collections.Generic;
using UnityEngine;
using FarmingSystem.Core;

namespace FarmingSystem.World
{
    // 계절이 바뀌면 맵 블록의 재질을 그 계절 프리팹의 재질로 바꿔 끼우는 관리자
    // - 블록을 지우고 새로 만들지 않고 재질(색)만 바꿈 → 맵을 계절마다 다시 만들 필요 없음, 빠름
    // - 씬의 블록 재질이 계절 묶음(SeasonalBlockSet)의 어느 프리팹 재질인지 찾아서, 같은 모양의 지금 계절 프리팹 재질로 교체
    //   (봄이든 겨울이든 묶음에 들어 있는 재질이면 알아봄 → 겨울에서 봄으로도 정상적으로 돌아감)
    // - GameClock의 계절 변경 이벤트를 받아 자동으로 바뀜. 게임 시작 시에도 지금 계절로 맞춤
    // - 게임 중 새로 생긴 블록(밭 칸 모습 등)은 SeasonalBlockSwapper.ApplyTo(오브젝트)로 지금 계절에 맞춤
    // 씬에 하나만 두면 됨
    public class SeasonalBlockSwapper : MonoBehaviour
    {
        public static SeasonalBlockSwapper Instance { get; private set; }

        [Tooltip("계절별 블록 묶음 (여러 개 가능)")]
        [SerializeField] private SeasonalBlockSet[] blockSets;
        [Tooltip("재질을 바꿀 범위. 비우면 씬 전체 (맵 생성기가 만든 GeneratedIsland를 넣으면 더 빠름)")]
        [SerializeField] private Transform[] roots;

        [Header("디버그")]
        [Tooltip("우클릭 메뉴 '디버그: 이 계절로 보기'에서 쓸 계절 (시간을 안 바꾸고 색만 확인)")]
        [SerializeField] private Season debugSeason = Season.Winter;

        // 재질 → (어느 모양 줄, 몇 번째 재질 칸)
        private readonly Dictionary<Material, (SeasonalBlockSet.Entry entry, int slot)> lookup =
            new Dictionary<Material, (SeasonalBlockSet.Entry, int)>();
        // 프리팹 → 그 프리팹의 재질 목록 (매번 찾지 않게 저장)
        private readonly Dictionary<GameObject, Material[]> prefabMaterials = new Dictionary<GameObject, Material[]>();
        private Season currentSeason = Season.Spring;
        private bool subscribed;

        public Season CurrentSeason => currentSeason;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[계절 블록] SeasonalBlockSwapper가 씬에 두 개 이상 있음 - 이 오브젝트는 무시됨");
                enabled = false;
                return;
            }
            Instance = this;
            BuildLookup();
        }

        private void OnEnable() => TrySubscribe();

        private void Start()
        {
            TrySubscribe();
            if (GameClock.Instance != null) currentSeason = GameClock.Instance.CurrentSeason;
            ApplyAll(currentSeason);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (subscribed && GameClock.Instance != null) GameClock.Instance.OnSeasonChanged -= HandleSeasonChanged;
        }

        private void TrySubscribe()
        {
            if (subscribed || GameClock.Instance == null) return;
            GameClock.Instance.OnSeasonChanged += HandleSeasonChanged;
            subscribed = true;
        }

        private void HandleSeasonChanged(Season season)
        {
            currentSeason = season;
            ApplyAll(season);
        }

        // 묶음에 있는 모든 프리팹의 재질을 기억해 둠
        private void BuildLookup()
        {
            lookup.Clear();
            prefabMaterials.Clear();
            if (blockSets == null) return;

            foreach (SeasonalBlockSet set in blockSets)
            {
                if (set == null) continue;
                foreach (SeasonalBlockSet.Entry e in set.entries)
                {
                    if (e.spring == null) Debug.LogWarning($"[계절 블록] '{set.name}'의 '{e.name}' 줄에 봄 프리팹이 비어 있음");
                    foreach (GameObject prefab in e.All())
                    {
                        Material[] mats = MaterialsOf(prefab);
                        for (int i = 0; i < mats.Length; i++)
                            if (mats[i] != null && !lookup.ContainsKey(mats[i]))
                                lookup[mats[i]] = (e, i);
                    }
                }
            }
        }

        // 프리팹에서 첫 번째 Renderer의 재질 목록
        private Material[] MaterialsOf(GameObject prefab)
        {
            if (prefab == null) return new Material[0];
            if (prefabMaterials.TryGetValue(prefab, out Material[] cached)) return cached;
            Renderer r = prefab.GetComponentInChildren<Renderer>(true);
            Material[] mats = r != null ? r.sharedMaterials : new Material[0];
            if (r == null) Debug.LogWarning($"[계절 블록] 프리팹 '{prefab.name}'에 Renderer가 없음");
            prefabMaterials[prefab] = mats;
            return mats;
        }

        // 범위 안 모든 블록을 그 계절 재질로
        public void ApplyAll(Season season)
        {
            if (lookup.Count == 0) BuildLookup();
            int changed = 0;
            if (roots != null && roots.Length > 0)
            {
                foreach (Transform root in roots)
                    if (root != null)
                        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                            if (ApplyRenderer(r, season)) changed++;
            }
            else
            {
                foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsInactive.Include))
                    if (ApplyRenderer(r, season)) changed++;
            }
            // 모델이 통째로 바뀌는 오브젝트(계절 나무)도 같이 맞춤 → 디버그 메뉴로 볼 때 나무까지 한 번에 확인 가능
            int objects = 0;
            foreach (SeasonalObject so in FindObjectsByType<SeasonalObject>(FindObjectsInactive.Include))
            {
                so.Apply(season);
                objects++;
            }
            Debug.Log($"[계절 블록] {GameClock.GetSeasonDisplayName(season)} 블록으로 변경 - 블록 {changed}개, 계절 나무 {objects}개");
        }

        // 오브젝트 하나(와 자식)를 지금 계절 재질로. 게임 중 새로 만든 블록에 사용
        public static void ApplyTo(GameObject go)
        {
            if (Instance == null || go == null) return;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                Instance.ApplyRenderer(r, Instance.currentSeason);
        }

        private bool ApplyRenderer(Renderer r, Season season)
        {
            Material[] mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null || !lookup.TryGetValue(mats[i], out var found)) continue;
                Material[] target = MaterialsOf(found.entry.Get(season));
                if (found.slot >= target.Length || target[found.slot] == null) continue;
                if (target[found.slot] != mats[i])
                {
                    mats[i] = target[found.slot];
                    changed = true;
                }
            }
            if (changed) r.sharedMaterials = mats;
            return changed;
        }

        [ContextMenu("디버그: 이 계절로 보기")]
        private void DebugPreview()
        {
            BuildLookup();
            currentSeason = debugSeason;
            ApplyAll(debugSeason);
        }

        [ContextMenu("디버그: 봄으로 되돌리기")]
        private void DebugResetSpring()
        {
            BuildLookup();
            currentSeason = Season.Spring;
            ApplyAll(Season.Spring);
        }
    }
}