using System.Collections.Generic;
using UnityEngine;
using FarmingSystem.Core;
using FarmingSystem.Farming;

namespace FarmingSystem.Building
{
    // 건축 관리자 (씬에 하나)
    // - 맵의 칸(FarmTile) 격자를 읽어서 "몇 번째 칸"과 실제 위치를 오감
    // - 어느 칸에 건물이 서 있는지 기억 (겹쳐 짓지 못하게)
    // - 공사 중인 건물은 날짜가 바뀌면 완성시킴 (동숲처럼 다음 날 아침 완성)
    // - 설치 미리보기·공사장 울타리에 쓰는 재질을 가지고 있음
    public class BuildingSystem : MonoBehaviour
    {
        public static BuildingSystem Instance { get; private set; }

        [Header("재질 (건축 시스템 만들기 도구가 넣어 줌)")]
        [Tooltip("설치 미리보기용 반투명 재질 (초록 = 가능, 빨강 = 불가)")]
        public Material ghostMaterial;
        [Tooltip("공사장 울타리 재질")]
        public Material fenceMaterial;
        [Tooltip("공사장 표지판 재질")]
        public Material signMaterial;

        [Header("공사장")]
        [Tooltip("그래픽 팀 공사장 모델이 생기면 넣기 (칸 크기에 맞춰 늘어남). 비우면 기본 울타리를 만들어 씀")]
        public GameObject constructionSitePrefab;

        [Header("충돌")]
        [Tooltip("완성된 건물·공사장이 들어갈 레이어 (밭 갈기·나무 심기를 막는 레이어)")]
        public string blockerLayerName = "FarmingBlocker";

        public float CellSize => cell;
        public IReadOnlyList<PlacedBuilding> Buildings => buildings;

        private readonly Dictionary<Vector2Int, FarmTile> tiles = new Dictionary<Vector2Int, FarmTile>();
        private readonly HashSet<Vector2Int> occupied = new HashSet<Vector2Int>();
        private readonly List<PlacedBuilding> buildings = new List<PlacedBuilding>();
        private float cell = 1.94f;
        private Vector3 gridOrigin;
        private bool gridReady;
        private int lastDay = -1;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // 날짜가 바뀌면 공사 진행 (매 프레임 확인 → 다른 시스템과 순서가 꼬이지 않음)
        private void Update()
        {
            if (GameClock.Instance == null) return;
            int day = GameClock.Instance.CurrentDay;
            if (day == lastDay) return;
            lastDay = day;
            foreach (PlacedBuilding b in buildings.ToArray())
                if (b != null) b.OnNewDay(day);
        }

        // ---------- 격자 ----------

        /// <summary>맵 칸 목록을 다시 읽음 (맵을 새로 만들었을 때)</summary>
        public void RefreshGrid()
        {
            tiles.Clear();
            gridReady = false;
            FarmTile[] all = FindObjectsByType<FarmTile>(FindObjectsInactive.Exclude);
            foreach (FarmTile t in all) tiles[t.GridPosition] = t;
            if (all.Length == 0) return;

            // 칸 크기·기준점 계산: 같은 줄에서 x칸 번호가 다른 두 칸의 거리
            FarmTile a = all[0];
            cell = 0f;
            foreach (FarmTile b in all)
            {
                int dgx = b.GridPosition.x - a.GridPosition.x;
                if (dgx == 0 || b.GridPosition.y != a.GridPosition.y) continue;
                cell = Mathf.Abs(b.Center.x - a.Center.x) / Mathf.Abs(dgx);
                break;
            }
            if (cell <= 0.01f) cell = 1.94f;
            gridOrigin = a.Center - new Vector3(a.GridPosition.x * cell, 0f, a.GridPosition.y * cell);
            gridReady = true;
        }

        private void EnsureGrid()
        {
            if (!gridReady || tiles.Count == 0) RefreshGrid();
        }

        public bool TryGetTile(Vector2Int g, out FarmTile tile)
        {
            EnsureGrid();
            return tiles.TryGetValue(g, out tile) && tile != null;
        }

        /// <summary>월드 위치 → 칸 번호 (가장 가까운 칸)</summary>
        public bool TryWorldToGrid(Vector3 world, out Vector2Int g)
        {
            EnsureGrid();
            g = default;
            if (!gridReady) return false;
            Vector3 d = world - gridOrigin;
            g = new Vector2Int(Mathf.RoundToInt(d.x / cell), Mathf.RoundToInt(d.z / cell));
            return true;
        }

        public bool IsOccupied(Vector2Int g) => occupied.Contains(g);

        // ---------- 짓기 ----------

        /// <summary>건물 짓기 시작 (공사 기간이 0이면 바로 완성)</summary>
        public PlacedBuilding Place(BuildingData data, List<Vector2Int> cells, Vector3 center, int rotation)
        {
            var go = new GameObject($"Building_{data.displayName}");
            go.transform.SetParent(transform, true);
            go.transform.position = center;
            PlacedBuilding b = go.AddComponent<PlacedBuilding>();
            foreach (Vector2Int c in cells) occupied.Add(c);
            buildings.Add(b);
            int today = GameClock.Instance != null ? GameClock.Instance.CurrentDay : 0;
            b.Setup(this, data, cells, rotation, today);
            Debug.Log($"[건축] {data.displayName} 공사 시작 ({cells.Count}칸, {(data.buildDays <= 0 ? "바로 완성" : $"{data.buildDays}일 뒤 완성")})");
            return b;
        }

        /// <summary>건물 철거 (칸을 다시 비움)</summary>
        public void Remove(PlacedBuilding b)
        {
            if (b == null) return;
            foreach (Vector2Int c in b.Cells) occupied.Remove(c);
            buildings.Remove(b);
            Destroy(b.gameObject);
        }

        public int BlockerLayer
        {
            get
            {
                int l = LayerMask.NameToLayer(blockerLayerName);
                return l >= 0 ? l : 0;
            }
        }

        [ContextMenu("디버그: 공사 중인 건물 전부 바로 완성")]
        private void DebugCompleteAll()
        {
            foreach (PlacedBuilding b in buildings) if (b != null) b.Complete();
        }

        // ---------- 모델 맞추기 (설치 미리보기·완성 건물 공용) ----------

        /// <summary>
        /// 건물 모델을 parent 아래에 만들고, 칸(footprint)에 맞게 크기·위치를 맞춤
        /// 모델이 원점에서 벗어나 있거나 눕혀져 있어도(프리팹마다 제각각) 화면에 보이는 크기를 재서 맞추므로 상관없음
        /// </summary>
        public static GameObject SpawnFitted(BuildingData d, Transform parent, float cellSize, int rotation)
        {
            if (d == null || d.prefab == null) return null;
            var holder = new GameObject("Model").transform;
            holder.SetParent(parent, false);
            holder.localRotation = Quaternion.Euler(0f, rotation * 90f + d.modelYaw, 0f);
            Instantiate(d.prefab, holder, false);

            Vector2Int fp = RotatedFootprint(d, rotation);
            FitToArea(holder, parent.position, fp.x * cellSize * d.fill, fp.y * cellSize * d.fill, d.manualScale);
            return holder.gameObject;
        }

        /// <summary>holder 아래 모델을 가로 sizeX x 세로 sizeZ 안에 들어가게 크기를 맞추고, 가운데를 center에·바닥을 center 높이에 붙임</summary>
        public static void FitToArea(Transform holder, Vector3 center, float sizeX, float sizeZ, float manualScale = 0f)
        {
            if (!TryGetBounds(holder, out Bounds b)) return;
            float s = manualScale > 0f ? manualScale
                : Mathf.Min(sizeX / Mathf.Max(b.size.x, 0.001f), sizeZ / Mathf.Max(b.size.z, 0.001f));
            holder.localScale = holder.localScale * s;

            // 크기를 바꾼 뒤 다시 재서: 가운데를 칸 중심에, 바닥을 땅에
            TryGetBounds(holder, out b);
            holder.position += new Vector3(center.x - b.center.x, center.y - b.min.y, center.z - b.center.z);
        }

        public static Vector2Int RotatedFootprint(BuildingData d, int rotation)
        {
            Vector2Int f = new Vector2Int(Mathf.Max(1, d.footprint.x), Mathf.Max(1, d.footprint.y));
            return (rotation & 1) == 1 ? new Vector2Int(f.y, f.x) : f;
        }

        public static bool TryGetBounds(Transform root, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                if (!found) { bounds = r.bounds; found = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return found;
        }
    }
}