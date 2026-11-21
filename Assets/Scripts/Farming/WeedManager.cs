using System.Collections.Generic;
using UnityEngine;
using FarmingSystem.Building;
using FarmingSystem.Core;
using FarmingSystem.NPC;
using FarmingSystem.Player;
using FarmingSystem.Save;
using FarmingSystem.World;
using FarmingSystem.Audio;

namespace FarmingSystem.Farming
{
    // 잡초 (모동숲처럼 날이 지나면 맨땅에 조금씩 돋아남)
    //  - 매일 아침: 맨땅(안 간 땅, 건물·나무·바위가 없는 칸)에 몇 개씩 새로 돋음. 이미 있는 잡초 옆에 잘 번짐. 겨울엔 안 돋음(설정)
    //  - 처음엔 짧은 잡초 → 며칠 지나면 긴 잡초까지 자람
    //  - 없애기: 괭이로 그 땅을 갈면 같이 없어짐 / 다른 도구·맨손으로 클릭하면 뽑힘
    //  - 여름 이벤트 (낮은 확률): 밭에 잡초가 번져서 심은 작물의 일부(기본 10%)가 잡초로 변함 → 클릭해서 뽑으면 그 밭에 다시 심을 수 있음
    //  - 계절마다 모델이 바뀜 (봄·여름·가을·겨울 잡초), 저장 칸에 같이 저장됨
    // 만들기: Tools → 행성농사일지 → 잡초 → 잡초 매니저 만들기 (잡초 모델 자동 연결)
    // ※ 클릭해서 뽑기는 FarmActionController에 두 줄을 넣어야 동작 (안내 참고)
    public class WeedManager : MonoBehaviour
    {
        public static WeedManager Instance { get; private set; }

        [Header("잡초 모델 (계절별 - Assets/weed)")]
        [SerializeField] private GameObject springWeed;
        [SerializeField] private GameObject summerWeed;
        [SerializeField] private GameObject autumnWeed;
        [SerializeField] private GameObject winterWeed;
        [Tooltip("잡초 높이 (m). 모델 크기를 재서 이 높이에 맞춤")]
        [SerializeField] private float weedHeight = 0.55f;
        [Tooltip("크기 무작위 범위 (배)")]
        [SerializeField] private Vector2 sizeRange = new Vector2(0.8f, 1.2f);

        [Header("돋아나기")]
        [Tooltip("새 게임을 시작할 때 처음부터 나 있는 잡초 수")]
        [SerializeField] private int startWeeds = 30;
        [Tooltip("하루에 새로 돋는 잡초 수 (최소~최대)")]
        [SerializeField] private Vector2Int perDay = new Vector2Int(2, 5);
        [Tooltip("섬 전체 잡초 최대 수 (이만큼 있으면 더 안 돋음)")]
        [SerializeField] private int maxWeeds = 80;
        [Tooltip("새 잡초가 이미 있는 잡초 바로 옆에 돋을 확률 (높을수록 뭉쳐서 번짐)")]
        [SerializeField, Range(0f, 1f)] private float spreadChance = 0.6f;
        [Tooltip("겨울에도 잡초가 돋는지")]
        [SerializeField] private bool growInWinter = false;
        [Tooltip("돋은 뒤 며칠 지나면 긴 잡초까지 자라는지 (0 = 처음부터 다 자람)")]
        [SerializeField] private int daysToGrowTall = 3;

        [Header("여름 잡초 번짐 이벤트")]
        [Tooltip("여름에 하루마다 이 확률로 밭에 잡초가 번짐 (0.08 = 8%)")]
        [SerializeField, Range(0f, 1f)] private float outbreakChance = 0.08f;
        [Tooltip("심어 둔 작물 중 잡초로 변하는 비율 (0.1 = 10%, 최소 1개)")]
        [SerializeField, Range(0f, 1f)] private float outbreakRatio = 0.1f;
        [SerializeField] private string outbreakMessage = "밤사이 밭에 잡초가 번졌어요! 작물 {n}개가 잡초로 변했어요";

        private class Weed
        {
            public FarmTile tile;
            public GameObject go;
            public int bornDay;
            public bool onField;          // 여름 이벤트로 밭(갈린 땅)에 생긴 잡초
            public float yaw, size;
            public readonly List<Renderer> tall = new List<Renderer>();
        }

        private readonly Dictionary<FarmTile, Weed> weeds = new Dictionary<FarmTile, Weed>();
        private readonly Dictionary<Vector2Int, FarmTile> tileAt = new Dictionary<Vector2Int, FarmTile>();
        private readonly List<FarmTile> allTiles = new List<FarmTile>();
        private Transform root;
        private bool subscribed, startedWithoutSave;
        private float checkTimer;

        public int Count => weeds.Count;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            root = new GameObject("Weeds").transform;
            root.SetParent(transform, false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (subscribed && GameClock.Instance != null) GameClock.Instance.OnDayChanged -= OnDayChanged;
        }

        private void Update()
        {
            if (!subscribed && GameClock.Instance != null)
            {
                GameClock.Instance.OnDayChanged += OnDayChanged;
                subscribed = true;
            }
            // 저장 시스템 없이 테스트할 때는 처음 잡초를 바로 깔아 줌
            if (!startedWithoutSave && SaveManager.Instance == null && Time.time > 0.5f)
            {
                startedWithoutSave = true;
                if (weeds.Count == 0) Sprout(startWeeds, CurrentDay());
            }

            // 괭이로 간 땅·건물이 들어선 땅의 잡초는 치움 (0.25초마다)
            checkTimer -= Time.deltaTime;
            if (checkTimer > 0f || weeds.Count == 0) return;
            checkTimer = 0.25f;
            List<FarmTile> gone = null;
            foreach (Weed w in weeds.Values)
            {
                bool hoed = w.tile == null || (!w.onField && w.tile.State == TileState.Tilled);
                bool built = w.tile != null && BuildingSystem.Instance != null && BuildingSystem.Instance.IsOccupied(w.tile.GridPosition);
                bool planted = w.tile != null && w.onField && w.tile.CurrentCrop != null;
                if (hoed || built || planted)
                {
                    if (gone == null) gone = new List<FarmTile>();
                    gone.Add(w.tile);
                }
            }
            if (gone != null) foreach (FarmTile t in gone) Remove(t);
        }

        // ───────────────────────── 하루가 지남 ─────────────────────────

        private void OnDayChanged(int day, Season season)
        {
            // 며칠 지난 잡초는 길게 자람
            foreach (Weed w in weeds.Values) RefreshTall(w, day);

            // 여름: 낮은 확률로 밭에 잡초가 번짐
            if (season == Season.Summer && Random.value < outbreakChance) Outbreak(day);

            // 맨땅에 새 잡초
            if (season == Season.Winter && !growInWinter) return;
            Sprout(Random.Range(perDay.x, perDay.y + 1), day);
        }

        private void Outbreak(int day)
        {
            var fields = new List<FarmTile>();
            foreach (FarmTile t in Tiles())
                if (t != null && t.CurrentCrop != null && !weeds.ContainsKey(t)) fields.Add(t);
            if (fields.Count == 0) return;
            int n = Mathf.Clamp(Mathf.RoundToInt(fields.Count * outbreakRatio), 1, fields.Count);
            for (int i = 0; i < n; i++)
            {
                int k = Random.Range(i, fields.Count);
                (fields[i], fields[k]) = (fields[k], fields[i]);
                FarmTile t = fields[i];
                t.DestroyCrop("잡초 번짐");
                Spawn(t, day - Mathf.Max(0, daysToGrowTall), true, Random.Range(0f, 360f), Random.Range(sizeRange.x, sizeRange.y)); // 밭 잡초는 다 자란 채로
            }
            Debug.Log($"[잡초] 여름 잡초 번짐 이벤트 - 작물 {n}개가 잡초로 변함 (심은 작물 {fields.Count}개 중)");
            IslandNotice.Show(outbreakMessage.Replace("{n}", n.ToString()), this);
            if (SaveManager.Instance != null && SaveManager.Instance.IsPlaying) SaveManager.Instance.Save();
        }

        // 맨땅에 count개 돋기 (이미 있는 잡초 옆에 잘 번짐)
        private void Sprout(int count, int day)
        {
            count = Mathf.Min(count, maxWeeds - weeds.Count);
            if (count <= 0) return;
            List<FarmTile> free = FreeTiles();
            if (free.Count == 0) return;
            var freeSet = new HashSet<FarmTile>(free);
            for (int i = 0; i < count && freeSet.Count > 0; i++)
            {
                FarmTile pick = null;
                if (weeds.Count > 0 && Random.value < spreadChance) pick = NeighborOf(RandomWeed(), freeSet);
                if (pick == null)
                {
                    int guard = 0;
                    do pick = free[Random.Range(0, free.Count)]; while (!freeSet.Contains(pick) && ++guard < 50);
                    if (!freeSet.Contains(pick)) break;
                }
                freeSet.Remove(pick);
                Spawn(pick, day, false, Random.Range(0f, 360f), Random.Range(sizeRange.x, sizeRange.y));
            }
        }

        private FarmTile RandomWeed()
        {
            int k = Random.Range(0, weeds.Count);
            foreach (FarmTile t in weeds.Keys) if (k-- == 0) return t;
            return null;
        }

        private FarmTile NeighborOf(FarmTile t, HashSet<FarmTile> freeSet)
        {
            if (t == null) return null;
            var options = new List<FarmTile>();
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                    if ((dx != 0 || dz != 0) && tileAt.TryGetValue(t.GridPosition + new Vector2Int(dx, dz), out FarmTile n) && freeSet.Contains(n))
                        options.Add(n);
            return options.Count > 0 ? options[Random.Range(0, options.Count)] : null;
        }

        // 잡초가 돋을 수 있는 칸: 안 간 맨땅 + 작물·잡초 없음 + 건물 없음 + 나무·바위 등 장애물 없음
        private List<FarmTile> FreeTiles()
        {
            var list = new List<FarmTile>();
            foreach (FarmTile t in Tiles())
            {
                if (t == null || !t.gameObject.activeInHierarchy || t.State != TileState.Natural || t.CurrentCrop != null || weeds.ContainsKey(t)) continue;
                if (BuildingSystem.Instance != null && BuildingSystem.Instance.IsOccupied(t.GridPosition)) continue;
                if (Blocked(t)) continue;
                list.Add(t);
            }
            return list;
        }

        private static bool Blocked(FarmTile t)
        {
            Bounds b = t.GetGroundBounds();
            Vector3 center = new Vector3(b.center.x, b.max.y + 0.6f, b.center.z);
            Vector3 half = new Vector3(b.extents.x * 0.8f, 0.5f, b.extents.z * 0.8f);
            foreach (Collider c in Physics.OverlapBox(center, half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c.GetComponentInParent<FarmTile>() != null) continue;          // 땅 블록
                if (c.GetComponentInParent<PlayerController>() != null) continue;  // 캐릭터
                if (c.GetComponentInParent<NPCDialogue>() != null) continue;       // 돌아다니는 NPC
                return true;                                                       // 나무·바위·건물 등
            }
            return false;
        }

        private List<FarmTile> Tiles()
        {
            if (allTiles.Count == 0 || allTiles[0] == null)
            {
                allTiles.Clear();
                tileAt.Clear();
                foreach (FarmTile t in FindObjectsByType<FarmTile>(FindObjectsInactive.Exclude))
                {
                    allTiles.Add(t);
                    tileAt[t.GridPosition] = t;
                }
            }
            return allTiles;
        }

        // ───────────────────────── 잡초 하나 ─────────────────────────

        private void Spawn(FarmTile tile, int bornDay, bool onField, float yaw, float size)
        {
            if (tile == null || weeds.ContainsKey(tile)) return;
            var holder = new GameObject($"Weed_{tile.GridPosition.x}_{tile.GridPosition.y}");
            holder.transform.SetParent(root, false);
            Bounds g = tile.GetGroundBounds();
            // 칸 안에서 살짝 흔들어 격자 느낌 줄이기
            Vector3 jitter = new Vector3(Random.Range(-0.2f, 0.2f) * g.size.x, 0f, Random.Range(-0.2f, 0.2f) * g.size.z);
            holder.transform.SetPositionAndRotation(tile.Center + jitter, Quaternion.Euler(0f, yaw, 0f));

            var w = new Weed { tile = tile, go = holder, bornDay = bornDay, onField = onField, yaw = yaw, size = size };
            GameObject sp = Variant(springWeed, holder.transform, size, w);
            GameObject su = Variant(summerWeed, holder.transform, size, w);
            GameObject au = Variant(autumnWeed, holder.transform, size, w);
            GameObject wi = Variant(winterWeed, holder.transform, size, w);
            if (sp == null && su == null && au == null && wi == null)
            {
                Destroy(holder);
                Debug.LogWarning("[잡초] 잡초 모델이 비어 있어요 - WeedManager의 Spring/Summer/Autumn/Winter Weed 칸을 채워 주세요");
                return;
            }
            holder.AddComponent<SeasonalObject>().Configure(sp != null ? sp : su, su, au, wi);
            holder.GetComponent<SeasonalObject>().Apply(GameClock.Instance != null ? GameClock.Instance.CurrentSeason : Season.Spring);
            weeds[tile] = w;
            RefreshTall(w, CurrentDay());
        }

        // 계절 모델 하나: 바닥을 땅에 맞추고 높이를 weedHeight × size로, 충돌체는 없앰 (밭 갈기·건물 짓기를 막지 않게)
        private GameObject Variant(GameObject prefab, Transform holder, float size, Weed w)
        {
            if (prefab == null) return null;
            GameObject m = Instantiate(prefab, holder);
            m.transform.localPosition = Vector3.zero;
            m.transform.localRotation = prefab.transform.localRotation;
            foreach (Collider c in m.GetComponentsInChildren<Collider>(true)) Destroy(c);

            Renderer[] rs = m.GetComponentsInChildren<Renderer>(true);
            if (rs.Length > 0)
            {
                Bounds b = rs[0].bounds;
                foreach (Renderer r in rs) b.Encapsulate(r.bounds);
                float k = b.size.y > 0.0001f ? weedHeight * size / b.size.y : 1f;
                m.transform.localScale = prefab.transform.localScale * k;
                b = rs[0].bounds;
                foreach (Renderer r in rs) b.Encapsulate(r.bounds);
                m.transform.position += new Vector3(holder.position.x - b.center.x, holder.position.y - b.min.y - 0.02f, holder.position.z - b.center.z);
                foreach (Renderer r in rs)
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    if (r.name.ToLowerInvariant().Contains("long")) w.tall.Add(r);
                }
            }
            return m;
        }

        // 긴 잡초는 며칠 지나야 보임
        private void RefreshTall(Weed w, int today)
        {
            bool tall = w.onField || today - w.bornDay >= daysToGrowTall;
            foreach (Renderer r in w.tall) if (r != null) r.enabled = tall;
        }

        private void Remove(FarmTile tile)
        {
            if (tile == null)
            {
                // 땅이 사라진 잡초 정리
                var dead = new List<FarmTile>();
                foreach (var kv in weeds) if (kv.Key == null) dead.Add(kv.Key);
                foreach (FarmTile d in dead) { if (weeds[d].go != null) Destroy(weeds[d].go); weeds.Remove(d); }
                return;
            }
            if (!weeds.TryGetValue(tile, out Weed w)) return;
            if (w.go != null) Destroy(w.go);
            weeds.Remove(tile);
        }

        private void Clear()
        {
            foreach (Weed w in weeds.Values) if (w.go != null) Destroy(w.go);
            weeds.Clear();
        }

        private static int CurrentDay() => GameClock.Instance != null ? GameClock.Instance.CurrentDay : 1;

        // ───────────────────────── 다른 스크립트가 쓰는 것 ─────────────────────────

        /// <summary>이 칸에 잡초가 있는지 (FarmActionController가 클릭 행동을 정할 때)</summary>
        public static bool HasWeed(FarmTile tile) => Instance != null && tile != null && Instance.weeds.ContainsKey(tile);

        /// <summary>잡초 뽑기 (맨손·다른 도구로 클릭). 뽑았으면 true</summary>
        public static bool TryPull(FarmTile tile)
        {
            if (!HasWeed(tile)) return false;
            Instance.Remove(tile);
            GameAudio.PlayAt(SoundId.ClearCrop, tile.transform.position);
            Debug.Log($"[잡초] {tile.name} 잡초를 뽑았어요");
            return true;
        }

        // ───────────────────────── 저장 ─────────────────────────

        public void Capture(SaveData d)
        {
            d.weedsSaved = true;
            d.weeds.Clear();
            foreach (Weed w in weeds.Values)
                if (w.tile != null)
                    d.weeds.Add(new WeedSave { x = w.tile.GridPosition.x, y = w.tile.GridPosition.y, bornDay = w.bornDay, onField = w.onField, yaw = w.yaw, size = w.size });
        }

        /// <summary>불러오기 (밭·건물을 다 되돌린 뒤에 부름)</summary>
        public void Apply(SaveData d)
        {
            Clear();
            allTiles.Clear();
            if (!d.weedsSaved) { Sprout(startWeeds, d.day); return; } // 잡초가 생기기 전 저장 → 처음 잡초를 깔아 줌
            Tiles();
            foreach (WeedSave s in d.weeds)
                if (tileAt.TryGetValue(new Vector2Int(s.x, s.y), out FarmTile t))
                    Spawn(t, s.bornDay, s.onField, s.yaw, s.size);
        }

        /// <summary>새 게임: 처음 잡초를 깔아 줌</summary>
        public void ResetForNewGame()
        {
            Clear();
            allTiles.Clear();
            Sprout(startWeeds, CurrentDay());
        }

#if UNITY_EDITOR
        [ContextMenu("디버그: 오늘 잡초 돋기")]
        private void DebugSprout() => Sprout(Random.Range(perDay.x, perDay.y + 1), CurrentDay());

        [ContextMenu("디버그: 여름 잡초 번짐 이벤트")]
        private void DebugOutbreak() => Outbreak(CurrentDay());

        [ContextMenu("디버그: 잡초 모두 없애기")]
        private void DebugClear() => Clear();
#endif
    }
}