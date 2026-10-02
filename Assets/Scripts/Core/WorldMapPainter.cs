using System.Collections.Generic;
using UnityEngine;

namespace FarmingSystem.Core
{
    public enum GroundType
    {
        Farmable,
        NonFarmable
    }

    [System.Serializable]
    public class ZoneRule
    {
        public string zoneName = "New Zone";
        public Vector2Int startCoord;
        public int width = 5;
        public int depth = 5;
        public GroundType groundType = GroundType.NonFarmable;
        public GameObject overridePrefab;
    }

    /// <summary>
    /// 에디터 전용 배경 맵 배치 도구.
    /// Play 없이 인스펙터 버튼으로 실제 Scene에 영구적인 블록을 깔아준다.
    /// 실제 배치 로직은 Editor/WorldMapPainterEditor.cs 가 담당한다.
    /// </summary>
    public class WorldMapPainter : MonoBehaviour
    {
        [Header("전체 맵 범위")]
        public int mapWidth = 20;
        public int mapDepth = 20;
        public float tileSize = 0.97f;
        public float visualScale = 0.5f;

        [Header("기본 바닥 (규칙에 안 걸리는 나머지 전부)")]
        public GameObject defaultFarmablePrefab;

        [Header("FarmTile 비주얼 (Farmable 칸에 부착됨)")]
        public GameObject naturalVisualPrefab;   // 예: 3D_Tile_Ground_01
        public GameObject tilledVisualPrefab;    // 예: 3D_Tile_Farm_Field_01
        [Tooltip("물 준 상태일 때 곱해질 색상 (같은 tilledVisualPrefab에 틴트만 적용됨)")]
        public Color wateredTintMultiplier = new Color(0.55f, 0.4f, 0.3f, 1f);

        [Header("작물 비주얼 설정")]
        [Tooltip("FarmTile.cropScaleMultiplier로 전달됨. 1 = 밭 블록과 동일 배율")]
        public float cropScaleMultiplier = 2f;

        [Header("구역별 덮어쓰기 규칙")]
        public List<ZoneRule> zoneRules = new List<ZoneRule>();

        [Header("레이어")]
        public string farmableLayerName = "FarmableGround";
        public string nonFarmableLayerName = "NonFarmableGround";
    }
}