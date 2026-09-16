using System.Collections.Generic;
using UnityEngine;

namespace FarmingSystem.Farming
{
    /// <summary>
    /// 월드에 이미 배치된 FarmTile들을 좌표 기준으로 등록/조회하는 레지스트리.
    /// 더 이상 타일을 직접 생성하지 않는다 (WorldMapPainter가 에디터에서 미리 배치).
    /// </summary>
    public class FarmGrid : MonoBehaviour
    {
        public static FarmGrid Instance { get; private set; }

        [Header("그리드 설정")]
        [SerializeField] private float tileSize = 1f;

        [Header("참조 (선택)")]
        [Tooltip("그리드 좌표 계산 기준점. 비워두면 월드 원점(0,0,0) 기준")]
        [SerializeField] private Transform origin;

        [Header("타일 자동 등록")]
        [Tooltip("이 Transform 하위에 있는 모든 FarmTile을 Awake 시점에 자동 등록")]
        [SerializeField] private Transform worldRoot;

        private readonly Dictionary<Vector2Int, FarmTile> tiles = new Dictionary<Vector2Int, FarmTile>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            RegisterExistingTiles();
        }

        private void RegisterExistingTiles()
        {
            Transform root = worldRoot != null ? worldRoot : transform;
            FarmTile[] existingTiles = root.GetComponentsInChildren<FarmTile>(true);

            foreach (FarmTile tile in existingTiles)
            {
                Vector2Int gridPos = WorldToGrid(tile.transform.position);
                tile.Setup(gridPos);
                tiles[gridPos] = tile;
            }
        }

        public Vector3 GridToWorld(Vector2Int gridPos)
        {
            Vector3 originPos = origin != null ? origin.position : Vector3.zero;
            return originPos + new Vector3(gridPos.x * tileSize, 0f, gridPos.y * tileSize);
        }

        public Vector2Int WorldToGrid(Vector3 worldPos)
        {
            Vector3 originPos = origin != null ? origin.position : Vector3.zero;
            Vector3 local = worldPos - originPos;
            int x = Mathf.RoundToInt(local.x / tileSize);
            int z = Mathf.RoundToInt(local.z / tileSize);
            return new Vector2Int(x, z);
        }

        public bool TryGetTile(Vector2Int gridPos, out FarmTile tile)
        {
            return tiles.TryGetValue(gridPos, out tile);
        }

        public bool TryGetTileAtWorldPosition(Vector3 worldPos, out FarmTile tile)
        {
            return TryGetTile(WorldToGrid(worldPos), out tile);
        }
    }
}