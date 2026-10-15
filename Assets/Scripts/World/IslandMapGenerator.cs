using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using FarmingSystem.Farming;

namespace FarmingSystem.World
{
    // 지역 종류
    public enum ZoneType
    {
        Wild,      // 지역이 아닌 일반 땅 (나무·바위가 자라는 곳)
        Home,      // 집
        Farm,      // 농장 (FarmTile이 붙은 밭)
        Plaza,     // 우주선 / 상인 광장
        Beach,     // 해변 / 항구 (추후 낚시)
        Highland   // 높은 지대 (추후 광산)
    }

    // 지역 하나의 설정값 (인스펙터에서 위치·크기·높이·잠금 여부를 조절)
    [Serializable]
    public class ZoneDefinition
    {
        public string displayName = "지역";
        public ZoneType type = ZoneType.Wild;
        [Tooltip("맵 안에서의 위치 (0~1). (0,0) = 왼쪽 아래(남서), (1,1) = 오른쪽 위(북동)")]
        public Vector2 center = new Vector2(0.5f, 0.5f);
        [Tooltip("지역 반지름 (칸 수)")]
        public float radius = 5f;
        [Tooltip("지면 높이 단계. 0 = 해변 높이, 1 = 기본 평지, 2 = 높은 지대")]
        public int level = 1;
        [Tooltip("추후 콘텐츠라 지금은 들어갈 수 없게 막을지")]
        public bool locked = false;
        [TextArea] public string lockedMessage = "아직 갈 수 없는 곳이에요.";
    }

    // 맵에 쓰는 프리팹 묶음 (AssetHuntsl 3D Tile 에셋을 끌어다 넣는다)
    [Serializable]
    public class IslandPrefabSet
    {
        [Header("땅 블록 (위쪽 면)")]
        [Tooltip("기본 잔디. 예: 3D_Tile_Ground_01 (초록 윗면)")] public GameObject grassTop;
        [Tooltip("해변 모래 (비우면 잔디)")] public GameObject sandTop;
        [Tooltip("흙길 (비우면 잔디)")] public GameObject pathTop;
        [Tooltip("광장 바닥 (비우면 흙길)")] public GameObject plazaTop;
        [Tooltip("높은 지대 윗면 (비우면 잔디)")] public GameObject highlandTop;
        [Tooltip("산 꼭대기 쪽 바위 윗면 (비우면 Stone Fill)")] public GameObject mountainRockTop;

        [Header("땅 블록 (옆면 / 속)")]
        [Tooltip("절벽 옆면 흙. 예: 갈색 블록 (비우면 잔디)")] public GameObject dirtFill;
        [Tooltip("높은 지대 절벽 (비우면 흙)")] public GameObject stoneFill;
        [Tooltip("물 밑바닥 (비우면 모래 → 흙)")] public GameObject seabed;

        [Header("밭 (FarmTile)")]
        [Tooltip("갈기 전 밭 모습 (비우면 잔디). 기존 WorldMapPainter에서 쓰던 것과 같게")] public GameObject farmNaturalVisual;
        [Tooltip("간 밭 모습. 예: 3D_Tile_Farm_F...")] public GameObject farmTilledVisual;

        [Header("경사로 (높이가 다른 길을 잇는 블록)")]
        [Tooltip("경사 블록. 예: 비스듬한 3D_Tile_Ground")] public GameObject slope;
        [Tooltip("경사 블록이 올라가는 방향이 길과 안 맞으면 90 / 180 / 270으로 조절")] public float slopeYawOffset = 0f;
        [Tooltip("경사 블록의 Box Collider를 Mesh Collider로 바꿔 실제로 올라갈 수 있게")] public bool fixSlopeCollider = true;

        [Header("물 / 다리")]
        [Tooltip("물 1칸 (AssetHuntsl 물 에셋)")] public GameObject waterTile;
        [Tooltip("섬 바깥 큰 바다 (선택, 하나만 크게 깔림)")] public GameObject oceanPlane;
        [Tooltip("다리 1칸 (비우면 흙길 블록으로 대신 놓음)")] public GameObject bridge;
        [Tooltip("부서진 다리 1칸 (잠긴 지역으로 가는 다리, 비우면 아무것도 안 놓음)")] public GameObject brokenBridge;
        [Tooltip("다리 방향이 길과 안 맞으면 90으로 조절")] public float bridgeYawOffset = 0f;

        [Header("장식")]
        public GameObject[] trees;
        public GameObject[] rocks;
        [Tooltip("잠긴 지역 입구를 막는 잔해 (바위, 표지판 등)")] public GameObject gateRubble;

        [Header("건물 (선택 - 비우면 위치 표시용 빈 오브젝트만 생성)")]
        public GameObject house;
        public GameObject shop;
    }

    // 생성 중에 쓰는 맵 데이터 (칸 단위 2차원 배열)
    public class IslandLayout
    {
        public readonly int width, depth;
        public readonly bool[,] land;          // 땅인지 (false = 물)
        public readonly bool[,] river;         // 강인지 (바다와 구분, 다리를 놓을 수 있는 물)
        public readonly int[,] level;          // 지면 높이 단계 (물 = -1)
        public readonly int[,] zoneIndex;      // 어느 지역인지 (-1 = 일반 땅)
        public readonly bool[,] road;          // 길인지 (강 위면 다리)
        public readonly bool[,] shore;         // 섬 둘레 모래사장인지 (해변 높이, 모래 블록)
        public readonly int[,] rampDir;        // 경사로 방향 (-1 = 없음, 0~3 = 올라가는 방향)
        public readonly int[,] bridgeAxis;     // 다리 방향 (-1 = 없음, 0 = 남북, 1 = 동서)
        public readonly int[,] bridgeLockZone; // 잠긴 지역으로 가는 부서진 다리면 그 지역 번호 (-1 = 정상 다리)
        public int[,] coastDistance;           // 가장 가까운 물까지 거리
        public int[,] zoneDistance;            // 가장 가까운 지역(집·농장 등)까지 거리
        public readonly List<Vector2> treePositions = new List<Vector2>();
        public readonly List<Vector2> rockPositions = new List<Vector2>();
        public readonly Dictionary<int, Vector2Int> gateCells = new Dictionary<int, Vector2Int>();
        public readonly Dictionary<int, int> gateDirs = new Dictionary<int, int>(); // 입구에서 바깥을 향하는 방향
        public readonly List<string> warnings = new List<string>();

        public IslandLayout(int width, int depth)
        {
            this.width = width;
            this.depth = depth;
            land = new bool[width, depth];
            river = new bool[width, depth];
            level = new int[width, depth];
            zoneIndex = new int[width, depth];
            road = new bool[width, depth];
            shore = new bool[width, depth];
            rampDir = new int[width, depth];
            bridgeAxis = new int[width, depth];
            bridgeLockZone = new int[width, depth];
            for (int x = 0; x < width; x++)
                for (int z = 0; z < depth; z++)
                {
                    zoneIndex[x, z] = -1;
                    rampDir[x, z] = -1;
                    bridgeAxis[x, z] = -1;
                    bridgeLockZone[x, z] = -1;
                }
        }

        public bool InBounds(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < width && c.y < depth;
        public bool IsSea(Vector2Int c) => !land[c.x, c.y] && !river[c.x, c.y];
        // 땅이거나 다리가 놓인 강 = 걸어 다닐 수 있는 칸
        public bool IsWalkable(Vector2Int c) => land[c.x, c.y] || (river[c.x, c.y] && road[c.x, c.y]);
        // 이 칸의 윗면 높이 단계 (물 = -1)
        public int TopLevel(int x, int z) => land[x, z] ? level[x, z] : -1;
    }

    // 섬 맵 자동 생성기
    // 1) 섬 모양: 원형 감쇠 + 프랙탈 노이즈 → 셀룰러 오토마타로 다듬기 → 플러드 필로 본섬만 남기기
    // 2) 지역: 설정한 지역 중심/반지름으로 칸마다 가장 가까운 지역을 정함 (가중 보로노이)
    // 3) 높이: 지역별 높이 + 노이즈 언덕 → 계단식 지형
    // 4) 강: 높은 지대 가장자리에서 바다까지 A*로 굽이치는 물길을 냄
    // 5) 길: 집에서 각 지역까지 A* (기존 길 재사용 시 비용↓ → 자연스럽게 길이 합쳐짐), 높이차는 경사로, 강은 다리
    // 6) 잠긴 지역: 경계를 투명 벽으로 막고, 가는 길의 다리는 부서진 다리, 입구엔 잔해를 놓음
    // 7) 장식: 포아송 디스크 샘플링으로 나무·바위를 고르게 배치
    // 에디터에서 [미리보기]로 색상 지도를 확인하고 [맵 생성]으로 실제 블록을 배치한다
    public class IslandMapGenerator : MonoBehaviour
    {
        private const string RootName = "GeneratedIsland";
        private const int IgnoreRaycastLayer = 2;

        [Header("맵 크기")]
        [Tooltip("가로 칸 수 (바깥 물 테두리 포함)")]
        [SerializeField] private int width = 112;
        [Tooltip("세로 칸 수 (바깥 물 테두리 포함)")]
        [SerializeField] private int depth = 96;
        [Tooltip("같은 시드 = 항상 같은 맵. 마음에 드는 맵이 나오면 이 숫자를 기록")]
        [SerializeField] private int seed = 12345;

        [Header("전체 크기")]
        [Tooltip("맵 전체 배율. 블록·높이·물·벽·나무 크기가 모두 같이 커짐. 캐릭터에 비해 맵이 작으면 1.5 ~ 2로")]
        [SerializeField, Range(0.5f, 4f)] private float worldScale = 2f;
        [Tooltip("나무·바위 크기 배율 (전체 배율에 추가로 곱해짐)")]
        [SerializeField] private float propScale = 1f;

        [Header("블록 크기 (전체 배율 1일 때 기준 - 기존 WorldMapPainter와 같은 값)")]
        [Tooltip("칸 간격. 이음매 제거용으로 1보다 살짝 작게. FarmGrid의 Tile Size와 반드시 같아야 함")]
        [SerializeField] private float cellSize = 0.97f;
        [Tooltip("높이 1단계 = 블록 1개 높이")]
        [SerializeField] private float levelHeight = 1f;
        [Tooltip("원본 2유닛 블록을 1유닛으로 줄이는 배율")]
        [SerializeField] private float blockScale = 0.5f;
        [Tooltip("블록 피벗이 바닥 중앙이 아니면 보정")]
        [SerializeField] private Vector3 blockOffset = Vector3.zero;

        [Header("섬 모양")]
        [Tooltip("맵 가장자리에서 몇 칸을 무조건 물로 둘지")]
        [SerializeField] private int borderWater = 3;
        [Tooltip("높을수록 섬이 작아짐")]
        [SerializeField, Range(-0.2f, 0.6f)] private float landThreshold = 0.08f;
        [Tooltip("섬 모양이 얼마나 네모난지 (2 = 원형, 4 = 둥근 네모, 클수록 맵을 꽉 채움)")]
        [SerializeField, Range(2f, 8f)] private float islandSquareness = 4f;
        [Tooltip("해안선 노이즈 크기 (작을수록 큰 굴곡)")]
        [SerializeField] private float coastNoiseScale = 0.05f;
        [Tooltip("해안선이 얼마나 울퉁불퉁한지")]
        [SerializeField, Range(0f, 1.5f)] private float coastRoughness = 0.55f;
        [Tooltip("셀룰러 오토마타 다듬기 횟수")]
        [SerializeField, Range(0, 6)] private int smoothingPasses = 2;
        [Tooltip("지역이 아닌 일반 땅의 높이 단계")]
        [SerializeField] private int baseLevel = 1;

        [Header("언덕 (일반 땅에 생기는 한 단 높은 둔덕)")]
        [SerializeField] private bool generateHills = true;
        [SerializeField] private float hillNoiseScale = 0.15f;
        [Tooltip("높을수록 언덕이 적어짐")]
        [SerializeField, Range(0.4f, 0.9f)] private float hillThreshold = 0.64f;

        [Header("강")]
        [SerializeField] private bool generateRiver = true;
        [Tooltip("강이 바다로 빠져나가는 쪽 (0~1 좌표)")]
        [SerializeField] private Vector2 riverMouthHint = new Vector2(0.70f, 0.05f);
        [Tooltip("하구 위치에서 이 거리(칸) 안의 바다에 닿으면 강이 끝남")]
        [SerializeField] private float riverMouthRadius = 16f;
        [Tooltip("클수록 강이 구불구불")]
        [SerializeField, Range(0f, 6f)] private float riverMeander = 3f;
        [SerializeField, Range(1, 2)] private int riverWidth = 2;

        [Header("길 (A* 비용)")]
        [Tooltip("다리 1칸 비용 (클수록 다리를 덜 놓으려 함)")]
        [SerializeField] private float bridgeCost = 6f;
        [Tooltip("경사로 비용 (클수록 높이가 같은 길로 돌아감)")]
        [SerializeField] private float rampCost = 3f;
        [Tooltip("이미 있는 길을 지날 때 비용 (작을수록 길이 하나로 합쳐짐)")]
        [SerializeField, Range(0.1f, 1f)] private float existingRoadCost = 0.4f;
        [Tooltip("밭을 가로지르는 비용 (밭 한가운데로 길이 나지 않게)")]
        [SerializeField] private float farmCrossCost = 40f;

        [Header("나무 / 바위 (포아송 디스크 + 숲 노이즈)")]
        [Tooltip("섬에서 숲이 차지하는 비율 (0 = 나무 없음, 1 = 빈 땅마다 나무). 나머지는 농사짓기 좋은 탁 트인 풀밭")]
        [SerializeField, Range(0f, 1f)] private float forestCoverage = 0.4f;
        [Tooltip("숲 덩어리 크기 (작을수록 큰 숲)")]
        [SerializeField] private float forestNoiseScale = 0.06f;
        [Tooltip("나무 사이 최소 거리 (칸)")]
        [SerializeField] private float treeSpacing = 2.2f;
        [Tooltip("바위 사이 최소 거리 (칸)")]
        [SerializeField] private float rockSpacing = 4.5f;
        [SerializeField, Range(0f, 1f)] private float rockChance = 0.5f;
        [Tooltip("크기 무작위 범위 (0.15 = ±15%)")]
        [SerializeField, Range(0f, 0.5f)] private float propScaleJitter = 0.15f;

        [Header("물")]
        [Tooltip("물 표면 높이 (해변 윗면 = 1보다 살짝 낮게)")]
        [SerializeField] private float waterSurfaceY = 0.85f;
        [Tooltip("물 1칸 프리팹 크기 배율")]
        [SerializeField] private float waterTileScale = 0.5f;
        [Tooltip("큰 바다 프리팹 크기 배율")]
        [SerializeField] private float oceanPlaneScale = 20f;
        [Tooltip("물 밑바닥 블록을 칸마다 깔지 (격자 무늬가 보임). Smooth Seabed가 켜져 있으면 무시")]
        [SerializeField] private bool placeSeabed = false;
        [Tooltip("물 밑바닥을 블록 대신 매끈한 경사면 하나로 (해안에서 멀어질수록 깊어져 물 색이 자연스럽게 진해짐)")]
        [SerializeField] private bool smoothSeabed = true;
        [Tooltip("물 밑바닥 색")]
        [SerializeField] private Color seabedColor = new Color(0.93f, 0.82f, 0.6f);
        [Tooltip("해안에서 몇 칸에 걸쳐 깊어질지")]
        [SerializeField] private float seabedShelfWidth = 8f;
        [Tooltip("가장 깊은 곳 깊이 (물 표면 아래, 블록 높이 단위)")]
        [SerializeField] private float seabedMaxDepth = 4f;
        [Tooltip("Ocean Plane이 있을 때, 해안에서 몇 칸까지만 물 칸·밑바닥을 깔지 (먼 바다는 큰 바다 한 장으로 대신해 오브젝트 수를 줄임)")]
        [SerializeField] private int seaTileMargin = 4;

        [Header("잠긴 지역")]
        [SerializeField] private float lockedWallHeight = 3f;

        [Header("지역 배치 (기획 이미지 기준, 숲·특별 지역은 제외)")]
        [SerializeField] private List<ZoneDefinition> zones = RecommendedZones(2f);

        [Header("광산 산 (높은 지대를 계단식 산으로)")]
        [Tooltip("가장자리 높이(지역 Level) 위로 몇 단 더 쌓을지. 클수록 높은 산")]
        [SerializeField, Range(0, 8)] private int mountainHeight = 6;
        [Tooltip("산 가장자리를 평평하게 남길 비율 (나중에 광산 입구·길이 들어설 자리)")]
        [SerializeField, Range(0f, 0.5f)] private float mountainFlatRim = 0.2f;
        [Tooltip("계단 경계가 얼마나 불규칙한지 (0 = 동그란 계단, 클수록 자연스러운 바위산)")]
        [SerializeField, Range(0f, 2f)] private float mountainRoughness = 0.9f;
        [Tooltip("산 모양 노이즈 크기")]
        [SerializeField] private float mountainNoiseScale = 0.18f;
        [Tooltip("위에서부터 몇 단까지 윗면을 바위로 덮을지 (0 = 전부 잔디)")]
        [SerializeField, Range(0, 8)] private int mountainRockTiers = 2;

        [Header("프리팹")]
        [SerializeField] private IslandPrefabSet prefabs = new IslandPrefabSet();

        [Header("밭 설정 (기존 WorldMapPainter와 같은 값)")]
        [Tooltip("물 준 밭에 곱해질 색상")]
        [SerializeField] private Color wateredTintMultiplier = new Color(0.55f, 0.4f, 0.3f, 1f);
        [Tooltip("작물 크기 배율 (FarmTile.cropScaleMultiplier로 전달)")]
        [SerializeField] private float cropScaleMultiplier = 2f;

        [Header("레이어 이름")]
        [SerializeField] private string farmableLayerName = "FarmableGround";
        [SerializeField] private string groundLayerName = "NonFarmableGround";
        [SerializeField] private string blockerLayerName = "FarmingBlocker";

        [Header("모래사장 (섬 둘레)")]
        [Tooltip("해안선을 따라 몇 칸 폭으로 모래사장을 깔지 (0 = 없음). 해변 높이(0단)라 물과 맞닿아 섬이 떠 보이지 않음")]
        [SerializeField, Range(0, 6)] private int shoreBeachWidth = 2;
        [Tooltip("모래사장과 위 땅을 잇는 경사로 간격 (칸). 모래사장으로 내려갔다가 못 올라오는 일이 없게")]
        [SerializeField] private float shoreRampSpacing = 14f;

        [Header("매끈한 지면 (격자 없애기)")]
        [Tooltip("켜면 블록 모델 대신 이어진 매끈한 지면을 그림 (블록마다 둥근 모서리가 만드는 격자 무늬가 사라짐)\n" +
                 "블록은 그대로 남아 충돌·밭(FarmTile) 기능만 하고 보이지 않음. 갈아놓은 밭은 밭 블록으로 보임")]
        [SerializeField] private bool smoothSurface = false;
        [SerializeField] private Color surfaceGrassColor = new Color(0.63f, 0.76f, 0.2f);
        [SerializeField] private Color surfacePathColor = new Color(0.94f, 0.6f, 0.24f);
        [SerializeField] private Color surfaceSandColor = new Color(0.97f, 0.85f, 0.6f);
        [SerializeField] private Color surfacePlazaColor = new Color(0.86f, 0.86f, 0.88f);
        [SerializeField] private Color surfaceRockColor = new Color(0.62f, 0.62f, 0.65f);
        [Tooltip("절벽 옆면 (흙)")]
        [SerializeField] private Color surfaceDirtColor = new Color(0.74f, 0.46f, 0.25f);
        [Tooltip("높은 지대 절벽 옆면 (바위)")]
        [SerializeField] private Color surfaceStoneColor = new Color(0.5f, 0.5f, 0.53f);
        [Tooltip("땅 색의 자연스러운 얼룩 정도 (0 = 단색). 넓은 풀밭이 밋밋하지 않게 밝고 어두운 부분이 생김")]
        [SerializeField, Range(0f, 0.4f)] private float surfaceColorVariation = 0.12f;
        [Tooltip("얼룩 크기 (작을수록 큰 얼룩)")]
        [SerializeField] private float surfaceNoiseScale = 0.07f;
        [Tooltip("잔디 얼룩에 섞을 색 (밝은 곳은 이 색 쪽으로, 어두운 곳은 진한 초록 쪽으로)")]
        [SerializeField] private Color surfaceGrassHighlight = new Color(0.78f, 0.82f, 0.32f);
        [Tooltip("1칸당 지면 텍스처 픽셀 수 (클수록 길·모래 경계가 또렷)")]
        [SerializeField, Range(2, 8)] private int surfaceTexelsPerCell = 4;
        [Tooltip("땅 종류 경계를 부드럽게 번지게 하는 정도 (0 = 또렷)")]
        [SerializeField, Range(0, 3)] private int surfaceEdgeBlur = 1;

        [Header("블록 틈 메우기 (매끈한 지면을 끈 경우에만)")]
        [Tooltip("블록 모서리가 둥글어서 생기는 틈 사이로 빈 공간이 보이지 않게, 블록 바로 밑에 색판을 깔아 막음")]
        [SerializeField] private bool fillBevelGaps = true;
        [Tooltip("틈으로 보이는 윗면 색 (잔디 그림자 색)")]
        [SerializeField] private Color gapTopColor = new Color(0.32f, 0.42f, 0.12f);
        [Tooltip("틈으로 보이는 옆면 색 (흙 색)")]
        [SerializeField] private Color gapSideColor = new Color(0.45f, 0.27f, 0.15f);

        [Header("지형 속 채우기")]
        [Tooltip("보이지 않는 땅 속을 콜라이더로 꽉 채움. 끄면 윗면 블록 아래가 비어 있어서, 캐릭터가 한 번 땅 밑으로 들어가면 빈 공간에 갇힘")]
        [SerializeField] private bool fillHollowGround = true;

        [Header("미리보기")]
        [SerializeField] private bool showPreview = true;

        // 전체 배율이 적용된 실제 값 (코드에서는 항상 이 값들을 사용)
        private float Cell => cellSize * worldScale;
        private float LevelH => levelHeight * worldScale;
        private float BlockS => blockScale * worldScale;
        private float WaterY => waterSurfaceY * worldScale;
        private float WaterS => waterTileScale * worldScale;
        private float WallH => lockedWallHeight * worldScale;
        private float OceanS => oceanPlaneScale * worldScale;

        // 마지막으로 계산한 맵 (미리보기 표시용, 저장되지 않음)
        [NonSerialized] private IslandLayout previewLayout;

        public IReadOnlyList<string> LastWarnings => previewLayout != null ? previewLayout.warnings : (IReadOnlyList<string>)Array.Empty<string>();
        public bool HasPreview => previewLayout != null;
        // FarmGrid Tile Size에 넣을 실제 칸 간격 (전체 배율 포함)
        public float CellSize => Cell;
        // 생성된 맵 루트 (없으면 null) - 에디터가 FarmGrid의 World Root를 자동 연결할 때 사용
        public Transform GeneratedRoot => transform.Find(RootName);

        // 기획 이미지 기준 추천 지역 배치 (숲·특별 지역 제외)
        // 위쪽 가운데 큰 바위산(광산) / 가운데 집 / 오른쪽 상인 광장 / 아래 해변
        // 농지는 따로 정하지 않음: 기능 없는 맨땅(잔디)이면 어디서든 농사 가능 (Farm On All Grass)
        // size = 지역 크기 배율 (56×50 맵 기준 1, 112×96 맵 기준 2)
        private static List<ZoneDefinition> RecommendedZones(float size)
        {
            return new List<ZoneDefinition>
            {
                new ZoneDefinition { displayName = "집",        type = ZoneType.Home,     center = new Vector2(0.50f, 0.46f), radius = 6f * size,  level = 1 },
                new ZoneDefinition { displayName = "상인 광장", type = ZoneType.Plaza,    center = new Vector2(0.76f, 0.46f), radius = 6f * size,  level = 1 },
                new ZoneDefinition { displayName = "광산",      type = ZoneType.Highland, center = new Vector2(0.50f, 0.80f), radius = 10f * size, level = 2,
                                     locked = true, lockedMessage = "무너진 길 너머로 광산이 보인다. 아직은 갈 수 없다." },
                new ZoneDefinition { displayName = "해변",      type = ZoneType.Beach,    center = new Vector2(0.55f, 0.12f), radius = 7f * size,  level = 0,
                                     locked = true, lockedMessage = "해변으로 가는 길이 막혀 있다. 언젠가 낚시하러 가자." },
            };
        }

        // 맵 크기·지역 배치·산·강 설정을 추천값으로 되돌림 (프리팹·블록 크기 설정은 그대로 유지)
        // 지역 반지름과 산 높이, 강 하구 범위는 맵 크기에 맞춰 같이 커짐
        public void ApplyRecommendedLayout(int mapWidth, int mapDepth)
        {
            float size = mapWidth / 56f;
            width = mapWidth;
            depth = mapDepth;
            zones = RecommendedZones(size);
            mountainHeight = Mathf.Clamp(Mathf.RoundToInt(4f * Mathf.Sqrt(size)), 3, 8);
            mountainFlatRim = 0.2f;
            mountainRoughness = 0.9f;
            mountainRockTiers = 2;
            forestCoverage = 0.4f;
            forestNoiseScale = 0.06f / size;
            treeSpacing = 2.2f;
            rockSpacing = 6f;
            rockChance = 0.4f;
            riverMouthHint = new Vector2(0.70f, 0.05f);
            riverMouthRadius = 8f * size;
            riverWidth = size >= 1.5f ? 2 : 1;
            borderWater = 3;
            landThreshold = 0.08f;
            islandSquareness = 4f;
            coastNoiseScale = 0.1f / size;
            shoreBeachWidth = 2;
            shoreRampSpacing = 14f;
            smoothSeabed = true;
            placeSeabed = false;
            fillBevelGaps = true;
        }

        // ==================== 에디터 버튼에서 호출 ====================

        // 시드를 무작위로 바꾼다
        public void RandomizeSeed()
        {
            seed = UnityEngine.Random.Range(1, 999999);
        }

        // 블록은 만들지 않고 맵 데이터만 계산해서 Scene 뷰에 색으로 보여준다
        public void Preview()
        {
            previewLayout = BuildLayout();
        }

        // 맵 데이터를 계산하고 실제 블록·물·나무를 배치한다
        public void Generate()
        {
            Clear();
            previewLayout = BuildLayout();
            BuildScene(previewLayout);
        }

        // 생성된 맵 오브젝트를 지운다
        public void Clear()
        {
            Transform old = transform.Find(RootName);
            if (old == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying) { Undo.DestroyObjectImmediate(old.gameObject); return; }
#endif
            Destroy(old.gameObject);
        }

        // ==================== 1단계: 맵 데이터 계산 ====================

        private IslandLayout BuildLayout()
        {
            var rng = new System.Random(seed);
            var layout = new IslandLayout(Mathf.Max(10, width), Mathf.Max(10, depth));

            // 노이즈마다 다른 위치를 쓰도록 시드로 오프셋을 정함
            Vector2 coastOffset = new Vector2(rng.Next(-9999, 9999), rng.Next(-9999, 9999));
            Vector2 hillOffset = new Vector2(rng.Next(-9999, 9999), rng.Next(-9999, 9999));
            Vector2 riverOffset = new Vector2(rng.Next(-9999, 9999), rng.Next(-9999, 9999));

            BuildLandMask(layout, coastOffset);
            AssignZones(layout);
            AssignLevels(layout, hillOffset);
            AssignShoreBeach(layout);
            if (generateRiver) CarveRiver(layout, riverOffset);

            // 강이 생긴 뒤 해안 거리 다시 계산
            layout.coastDistance = MapAlgorithms.DistanceField(layout.width, layout.depth, (x, z) => !layout.land[x, z]);

            BuildRoads(layout);
            AddShoreRamps(layout, rng);
            ScatterProps(layout, rng);
            return layout;
        }

        // 섬 모양 만들기
        private void BuildLandMask(IslandLayout L, Vector2 noiseOffset)
        {
            int w = L.width, h = L.depth;
            bool[,] forced = new bool[w, h];

            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    if (IsBorder(L, x, z)) { L.land[x, z] = false; continue; }

                    // 맵 중앙 0, 가장자리 1인 거리 (원형 감쇠)
                    float nx = (x + 0.5f) / w * 2f - 1f;
                    float nz = (z + 0.5f) / h * 2f - 1f;
                    // 초타원 거리: 지수가 클수록 네모에 가까워져 섬이 맵을 꽉 채움
                    float p = Mathf.Max(2f, islandSquareness);
                    float falloff = Mathf.Pow(Mathf.Pow(Mathf.Abs(nx), p) + Mathf.Pow(Mathf.Abs(nz), p), 1f / p);

                    // 프랙탈 노이즈로 해안선을 울퉁불퉁하게
                    float noise = MapAlgorithms.Fbm(x * coastNoiseScale + noiseOffset.x, z * coastNoiseScale + noiseOffset.y, 4);
                    float value = (1f - falloff) + (noise - 0.5f) * coastRoughness;
                    L.land[x, z] = value > landThreshold;
                }
            }

            // 지역 원 안은 무조건 땅 (지역이 바다에 잘리지 않게)
            for (int i = 0; i < zones.Count; i++)
            {
                Vector2Int c = ZoneCenterCell(L, i);
                float r = zones[i].radius + 1f;
                for (int x = 0; x < w; x++)
                    for (int z = 0; z < h; z++)
                        if (!IsBorder(L, x, z) && (new Vector2(x, z) - (Vector2)c).magnitude <= r)
                        {
                            L.land[x, z] = true;
                            forced[x, z] = true;
                        }
            }

            MapAlgorithms.SmoothCellular(L.land, smoothingPasses, forced);

            // 다듬은 뒤에도 가장자리는 물로
            for (int x = 0; x < w; x++)
                for (int z = 0; z < h; z++)
                    if (IsBorder(L, x, z)) L.land[x, z] = false;

            MapAlgorithms.KeepLargestRegion(L.land);
        }

        // 칸마다 어느 지역에 속하는지 정함 (지역 원 안에서 (거리 / 반지름)이 가장 작은 지역)
        private void AssignZones(IslandLayout L)
        {
            for (int x = 0; x < L.width; x++)
            {
                for (int z = 0; z < L.depth; z++)
                {
                    L.zoneIndex[x, z] = -1;
                    if (!L.land[x, z]) continue;

                    float best = 1f;
                    for (int i = 0; i < zones.Count; i++)
                    {
                        if (zones[i].type == ZoneType.Wild || zones[i].radius <= 0f) continue;
                        float d = (new Vector2(x, z) - (Vector2)ZoneCenterCell(L, i)).magnitude / zones[i].radius;
                        if (d <= best) { best = d; L.zoneIndex[x, z] = i; }
                    }
                }
            }

            // 섬 밖으로 밀려나 칸이 하나도 없는 지역 경고
            for (int i = 0; i < zones.Count; i++)
            {
                bool any = false;
                for (int x = 0; x < L.width && !any; x++)
                    for (int z = 0; z < L.depth && !any; z++)
                        if (L.zoneIndex[x, z] == i) any = true;
                if (!any) L.warnings.Add($"'{zones[i].displayName}' 지역이 섬 안에 없음 (위치나 반지름 확인)");
            }
        }

        // 높이 단계 정하기
        private void AssignLevels(IslandLayout L, Vector2 hillOffset)
        {
            Vector2 mountainOffset = hillOffset + new Vector2(311f, 173f);
            L.coastDistance = MapAlgorithms.DistanceField(L.width, L.depth, (x, z) => !L.land[x, z]);
            L.zoneDistance = MapAlgorithms.DistanceField(L.width, L.depth, (x, z) => L.zoneIndex[x, z] >= 0);

            for (int x = 0; x < L.width; x++)
            {
                for (int z = 0; z < L.depth; z++)
                {
                    if (!L.land[x, z]) { L.level[x, z] = -1; continue; }

                    int zi = L.zoneIndex[x, z];
                    if (zi >= 0)
                    {
                        // 지역은 정해진 높이로 평평하게, 높은 지대는 계단식 산으로
                        L.level[x, z] = zones[zi].type == ZoneType.Highland
                            ? MountainLevel(L, x, z, zi, mountainOffset)
                            : zones[zi].level;
                        continue;
                    }

                    L.level[x, z] = baseLevel;

                    // 지역에서 2칸 이상, 해안에서 3칸 이상 떨어진 일반 땅에만 언덕 (지역 옆에 절벽이 생기지 않게)
                    if (generateHills && L.zoneDistance[x, z] >= 2 && L.coastDistance[x, z] >= 3)
                    {
                        float n = MapAlgorithms.Fbm(x * hillNoiseScale + hillOffset.x, z * hillNoiseScale + hillOffset.y, 3);
                        if (n > hillThreshold) L.level[x, z] = baseLevel + 1;
                    }
                }
            }
        }

        // 섬 둘레 모래사장: 해안에서 shoreBeachWidth칸 안쪽의 일반 땅을 해변 높이(0단) 모래로 바꿈
        // (집·농장 등 지역과 그 바로 옆은 제외)
        private void AssignShoreBeach(IslandLayout L)
        {
            if (shoreBeachWidth <= 0) return;
            int shoreLevel = Mathf.Max(0, baseLevel - 1);

            for (int x = 0; x < L.width; x++)
                for (int z = 0; z < L.depth; z++)
                {
                    if (!L.land[x, z] || L.zoneIndex[x, z] >= 0) continue;
                    if (L.coastDistance[x, z] > shoreBeachWidth) continue;
                    if (L.zoneDistance[x, z] < 2) continue;
                    L.shore[x, z] = true;
                    L.level[x, z] = shoreLevel;
                }
        }

        // 모래사장과 위 땅 사이 경사로: 경계를 따라 일정 간격으로 놓아, 어디서 내려가도 근처에서 다시 올라올 수 있게 함
        private void AddShoreRamps(IslandLayout L, System.Random rng)
        {
            if (shoreBeachWidth <= 0) return;

            // 후보: 모래사장 칸 중 한 단 높은 일반 땅과 맞닿은 칸
            var candidates = new List<(Vector2Int cell, int dir)>();
            for (int x = 0; x < L.width; x++)
                for (int z = 0; z < L.depth; z++)
                {
                    if (!L.shore[x, z] || L.rampDir[x, z] >= 0 || L.road[x, z]) continue;
                    for (int d = 0; d < 4; d++)
                    {
                        Vector2Int n = new Vector2Int(x, z) + MapAlgorithms.Dirs4[d];
                        if (!L.InBounds(n) || !L.land[n.x, n.y] || L.shore[n.x, n.y]) continue;
                        if (L.level[n.x, n.y] != L.level[x, z] + 1) continue;
                        if (L.zoneIndex[n.x, n.y] >= 0 && zones[L.zoneIndex[n.x, n.y]].locked) continue;

                        // 경사로 반대편(내려오는 쪽)도 모래사장이어야 자연스럽게 이어짐
                        Vector2Int back = new Vector2Int(x, z) - MapAlgorithms.Dirs4[d];
                        if (!L.InBounds(back) || !L.land[back.x, back.y] || L.level[back.x, back.y] != L.level[x, z]) continue;

                        candidates.Add((new Vector2Int(x, z), d));
                        break;
                    }
                }

            // 무작위 순서로 훑으며, 이미 놓은 경사로와 간격이 충분할 때만 추가
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                var t = candidates[i]; candidates[i] = candidates[j]; candidates[j] = t;
            }
            var placed = new List<Vector2Int>();
            foreach (var c in candidates)
            {
                bool farEnough = true;
                foreach (Vector2Int p in placed)
                    if ((p - c.cell).magnitude < shoreRampSpacing) { farEnough = false; break; }
                if (!farEnough) continue;

                L.rampDir[c.cell.x, c.cell.y] = c.dir;
                placed.Add(c.cell);
            }
        }

        // 계단식 산 높이 계산
        // 중심에 가까울수록 단이 높아지고(원뿔), 노이즈로 계단 경계를 흔들어 자연스러운 바위산 모양을 만든다
        // 가장자리(mountainFlatRim)는 항상 기본 높이로 남겨, 바깥 땅과 한 단 차이만 나게 함 (길·경사로가 닿을 수 있게)
        private int MountainLevel(IslandLayout L, int x, int z, int zi, Vector2 noiseOffset)
        {
            ZoneDefinition zone = zones[zi];
            float d = (new Vector2(x, z) - (Vector2)ZoneCenterCell(L, zi)).magnitude / Mathf.Max(0.01f, zone.radius);
            if (d >= 1f - mountainFlatRim) return zone.level;

            // 0(가장자리 평지 끝) ~ 1(중심)
            float t = 1f - d / Mathf.Max(0.01f, 1f - mountainFlatRim);
            float n = MapAlgorithms.Fbm(x * mountainNoiseScale + noiseOffset.x, z * mountainNoiseScale + noiseOffset.y, 3) - 0.5f;
            float raw = t * (mountainHeight + 0.99f) + n * mountainRoughness * 2f;
            int tier = Mathf.Clamp(Mathf.FloorToInt(raw) + 1, 1, mountainHeight);
            if (mountainHeight <= 0) tier = 0;
            return zone.level + tier;
        }

        // 높은 지대 가장자리에서 바다까지 강 파기
        private void CarveRiver(IslandLayout L, Vector2 noiseOffset)
        {
            int source = FindZone(ZoneType.Highland);
            if (source < 0) { L.warnings.Add("강: 높은 지대(Highland) 지역이 없어 강을 만들지 않음"); return; }

            // 시작점: 높은 지대 바로 바깥 칸 중 강 하구 방향에 가장 가까운 칸
            Vector2 mouth = new Vector2(riverMouthHint.x * (L.width - 1), riverMouthHint.y * (L.depth - 1));
            Vector2Int start = new Vector2Int(-1, -1);
            float bestDist = float.MaxValue;
            for (int x = 0; x < L.width; x++)
            {
                for (int z = 0; z < L.depth; z++)
                {
                    if (!L.land[x, z] || L.zoneIndex[x, z] >= 0) continue;
                    bool touchesHighland = false;
                    foreach (Vector2Int d in MapAlgorithms.Dirs4)
                    {
                        var n = new Vector2Int(x + d.x, z + d.y);
                        if (L.InBounds(n) && L.zoneIndex[n.x, n.y] == source) { touchesHighland = true; break; }
                    }
                    if (!touchesHighland) continue;

                    float dist = (new Vector2(x, z) - mouth).sqrMagnitude;
                    if (dist < bestDist) { bestDist = dist; start = new Vector2Int(x, z); }
                }
            }
            if (start.x < 0) { L.warnings.Add("강: 높은 지대 옆에 시작할 땅이 없음"); return; }

            // 하구 근처 바다까지 가는 경로. 노이즈 비용으로 구불구불하게, 지역 안쪽은 피해서
            // 하구가 아닌 바다로는 들어가지 못하게 해서, 가까운 해안으로 바로 빠지지 않고 섬을 가로질러 흐르게 함
            bool NearMouth(Vector2Int c) => (new Vector2(c.x, c.y) - mouth).magnitude <= riverMouthRadius;

            float RiverCost(Vector2Int a, Vector2Int b, bool mouthOnly)
            {
                if (L.IsSea(b)) return (!mouthOnly || NearMouth(b)) ? 1f : -1f;
                int zi = L.zoneIndex[b.x, b.y];
                if (zi >= 0) return zones[zi].type == ZoneType.Beach ? 25f : 200f;
                float n = MapAlgorithms.Fbm(b.x * 0.25f + noiseOffset.x, b.y * 0.25f + noiseOffset.y, 2);
                float towardMouth = (new Vector2(b.x, b.y) - mouth).magnitude / Mathf.Max(L.width, L.depth);
                return 1f + n * riverMeander + towardMouth;
            }

            List<Vector2Int> path = MapAlgorithms.FindPath(L.width, L.depth, start,
                c => L.IsSea(c) && NearMouth(c), (a, b) => RiverCost(a, b, true), c => 0f);

            // 하구까지 못 가면 가장 가까운 바다로라도
            if (path == null)
            {
                L.warnings.Add("강: 하구 쪽 바다까지 길이 없어 가까운 바다로 빠지게 함 (River Mouth Hint 조절)");
                path = MapAlgorithms.FindPath(L.width, L.depth, start, c => L.IsSea(c), (a, b) => RiverCost(a, b, false), c => 0f);
            }

            if (path == null) { L.warnings.Add("강: 바다까지 경로를 찾지 못함"); return; }

            for (int i = 0; i < path.Count; i++)
            {
                Vector2Int c = path[i];
                if (L.IsSea(c)) break;
                MakeRiver(L, c);

                // 폭 2면 진행 방향 오른쪽 칸도 강으로
                if (riverWidth >= 2 && i + 1 < path.Count)
                {
                    int dir = MapAlgorithms.DirBetween(c, path[i + 1]);
                    if (dir >= 0)
                    {
                        Vector2Int side = c + MapAlgorithms.Dirs4[(dir + 1) % 4];
                        if (L.InBounds(side) && L.land[side.x, side.y] && L.zoneIndex[side.x, side.y] < 0)
                            MakeRiver(L, side);
                    }
                }
            }
        }

        private static void MakeRiver(IslandLayout L, Vector2Int c)
        {
            L.land[c.x, c.y] = false;
            L.river[c.x, c.y] = true;
            L.level[c.x, c.y] = -1;
            L.zoneIndex[c.x, c.y] = -1;
        }

        // 집에서 각 지역까지 길 내기
        private void BuildRoads(IslandLayout L)
        {
            int home = FindZone(ZoneType.Home);
            if (home < 0) { L.warnings.Add("길: 집(Home) 지역이 없어 길을 만들지 않음"); return; }

            Vector2Int start = ZoneCenterCellOnLand(L, home);
            if (start.x < 0) { L.warnings.Add("길: 집 지역에 땅이 없음"); return; }

            // 열린 지역 먼저, 잠긴 지역은 나중에 (잠긴 길이 정상 다리를 부수지 않도록)
            var order = new List<int>();
            for (int i = 0; i < zones.Count; i++) if (i != home && !zones[i].locked) order.Add(i);
            for (int i = 0; i < zones.Count; i++) if (i != home && zones[i].locked) order.Add(i);

            foreach (int target in order)
            {
                if (zones[target].type == ZoneType.Wild) continue;
                Vector2Int targetCenter = ZoneCenterCell(L, target);
                float targetRadius = zones[target].radius;
                int t = target;

                List<Vector2Int> path = MapAlgorithms.FindPath(L.width, L.depth, start,
                    c => L.land[c.x, c.y] && L.zoneIndex[c.x, c.y] == t,
                    (a, b) => RoadStepCost(L, a, b, t),
                    c => Mathf.Max(0f, (new Vector2(c.x, c.y) - (Vector2)targetCenter).magnitude - targetRadius) * existingRoadCost);

                if (path == null)
                {
                    L.warnings.Add($"길: '{zones[target].displayName}'까지 길을 찾지 못함 (지역 위치나 높이를 확인)");
                    continue;
                }
                ApplyRoad(L, path, target);
            }
        }

        // 길 1칸 이동 비용 (-1 = 갈 수 없음)
        private float RoadStepCost(IslandLayout L, Vector2Int a, Vector2Int b, int target)
        {
            if (L.IsSea(b)) return -1f;

            bool aRiver = L.river[a.x, a.y], bRiver = L.river[b.x, b.y];
            int la = aRiver ? baseLevel : L.level[a.x, a.y];
            int lb = bRiver ? baseLevel : L.level[b.x, b.y];

            // 다리는 같은 높이끼리만, 땅은 한 단계 차이까지만 (경사로)
            if ((aRiver || bRiver) && la != lb) return -1f;
            int diff = Mathf.Abs(la - lb);
            if (diff > 1) return -1f;

            float cost = L.road[b.x, b.y] ? existingRoadCost : 1f;
            if (bRiver && !L.road[b.x, b.y]) cost = bridgeCost;
            if (diff == 1)
            {
                // 이미 다른 방향 경사로가 있는 칸은 피함
                if (L.rampDir[a.x, a.y] >= 0 || L.rampDir[b.x, b.y] >= 0) cost += rampCost * 3f;
                cost += rampCost;
            }

            int zi = L.land[b.x, b.y] ? L.zoneIndex[b.x, b.y] : -1;
            if (zi >= 0 && zi != target)
            {
                if (zones[zi].locked) return -1f;                 // 다른 잠긴 지역은 통과 불가
                if (zones[zi].type == ZoneType.Farm) cost += farmCrossCost; // 밭은 가로지르지 않게
            }
            return cost;
        }

        // 찾은 경로를 길·경사로·다리로 표시
        private void ApplyRoad(IslandLayout L, List<Vector2Int> path, int target)
        {
            // 이번 경로 이전부터 있던 다리 (잠긴 길이라도 이 다리는 부수지 않음)
            var preexisting = new HashSet<Vector2Int>();
            foreach (Vector2Int c in path) if (L.road[c.x, c.y]) preexisting.Add(c);

            for (int k = 0; k < path.Count; k++)
            {
                Vector2Int c = path[k];

                // 땅은 지역 구분 없이 모두 같은 땅이라, 길은 상점·집까지 그대로 이어짐

                L.road[c.x, c.y] = true;

                // 강 위 = 다리. 앞뒤 칸으로 다리 방향 결정
                if (L.river[c.x, c.y] && L.bridgeAxis[c.x, c.y] < 0)
                {
                    Vector2Int prev = k > 0 ? path[k - 1] : c;
                    Vector2Int next = k + 1 < path.Count ? path[k + 1] : c;
                    Vector2Int span = next - prev;
                    L.bridgeAxis[c.x, c.y] = Mathf.Abs(span.x) > Mathf.Abs(span.y) ? 1 : 0;
                }
            }

            // 높이가 1단계 다른 두 칸 사이 = 낮은 칸에 경사로 (높은 쪽을 향함)
            for (int k = 0; k + 1 < path.Count; k++)
            {
                Vector2Int a = path[k], b = path[k + 1];
                if (!L.land[a.x, a.y] || !L.land[b.x, b.y]) continue;
                int diff = L.level[b.x, b.y] - L.level[a.x, a.y];
                if (diff == 0) continue;

                Vector2Int low = diff > 0 ? a : b;
                Vector2Int high = diff > 0 ? b : a;
                if (L.rampDir[low.x, low.y] < 0) L.rampDir[low.x, low.y] = MapAlgorithms.DirBetween(low, high);
            }

            if (!zones[target].locked) return;

            // 잠긴 지역: 지역에 가장 가까운 강 구간(새로 놓은 다리)을 부서진 다리로
            int lastRiver = -1;
            for (int k = path.Count - 1; k >= 0; k--)
                if (L.river[path[k].x, path[k].y] && !preexisting.Contains(path[k])) { lastRiver = k; break; }

            if (lastRiver >= 0)
            {
                for (int k = lastRiver; k >= 0 && L.river[path[k].x, path[k].y] && !preexisting.Contains(path[k]); k--)
                    L.bridgeLockZone[path[k].x, path[k].y] = target;
            }

            // 입구 = 지역에 처음 들어선 칸, 방향 = 바깥(이전 칸) 쪽
            Vector2Int gate = path[path.Count - 1];
            L.gateCells[target] = gate;
            L.gateDirs[target] = path.Count >= 2 ? MapAlgorithms.DirBetween(gate, path[path.Count - 2]) : 2;
        }

        // 나무·바위 흩뿌리기
        private void ScatterProps(IslandLayout L, System.Random rng)
        {
            L.zoneDistance = MapAlgorithms.DistanceField(L.width, L.depth, (x, z) => L.zoneIndex[x, z] >= 0 && zones[L.zoneIndex[x, z]].type != ZoneType.Highland);
            int[,] roadDistance = MapAlgorithms.DistanceField(L.width, L.depth, (x, z) => L.road[x, z] || L.rampDir[x, z] >= 0);
            var used = new HashSet<Vector2Int>();
            Vector2 forestOffset = new Vector2(rng.Next(-9999, 9999), rng.Next(-9999, 9999));

            // 숲 노이즈: 값이 높은 곳만 숲. 나무가 섬 전체에 고르게 흩어지지 않고 숲 덩어리와 빈 풀밭으로 나뉨
            bool InForest(Vector2Int c)
            {
                float n = MapAlgorithms.Fbm(c.x * forestNoiseScale + forestOffset.x, c.y * forestNoiseScale + forestOffset.y, 3);
                return n > 1f - forestCoverage * 0.9f - 0.05f;
            }

            // 장식을 놓아도 되는 칸인지
            bool CanPlace(Vector2 p)
            {
                var c = new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));
                if (!L.InBounds(c) || !L.land[c.x, c.y]) return false;
                if (used.Contains(c)) return false;
                if (roadDistance[c.x, c.y] < 2) return false;     // 길 바로 옆은 비워 둠
                if (L.coastDistance[c.x, c.y] < 1) return false;
                foreach (var gate in L.gateCells.Values)
                    if ((gate - c).sqrMagnitude <= 4) return false; // 입구 주변 비워 둠

                if (L.shore[c.x, c.y]) return false;                 // 모래사장엔 나무 없음
                int zi = L.zoneIndex[c.x, c.y];
                if (zi < 0) return L.zoneDistance[c.x, c.y] >= 2 && InForest(c); // 일반 땅: 지역에서 2칸 이상 + 숲 지대만
                return zones[zi].type == ZoneType.Highland;         // 지역 중엔 높은 지대만
            }

            foreach (Vector2 p in MapAlgorithms.PoissonDisk(L.width, L.depth, treeSpacing, rng, CanPlace))
            {
                var c = new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));
                used.Add(c);
                L.treePositions.Add(p);
            }

            // 바위는 숲 여부와 상관없이 드문드문 (지역·길·모래사장은 제외)
            bool CanPlaceRock(Vector2 p)
            {
                var c = new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));
                if (!L.InBounds(c) || !L.land[c.x, c.y] || L.shore[c.x, c.y] || used.Contains(c)) return false;
                if (roadDistance[c.x, c.y] < 2 || L.coastDistance[c.x, c.y] < 1) return false;
                int zi = L.zoneIndex[c.x, c.y];
                return zi < 0 ? L.zoneDistance[c.x, c.y] >= 2 : zones[zi].type == ZoneType.Highland;
            }

            foreach (Vector2 p in MapAlgorithms.PoissonDisk(L.width, L.depth, rockSpacing, rng, CanPlaceRock))
            {
                if (rng.NextDouble() > rockChance) continue;
                used.Add(new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y)));
                L.rockPositions.Add(p);
            }
        }

        // ==================== 2단계: 씬에 배치 ====================

        private void BuildScene(IslandLayout L)
        {
            if (prefabs.grassTop == null)
            {
                L.warnings.Add("프리팹: Grass Top이 비어 있어 블록을 만들 수 없음");
                Debug.LogWarning("[IslandMapGenerator] Grass Top 프리팹을 먼저 넣어주세요.");
                return;
            }

            var root = new GameObject(RootName);
            root.transform.SetParent(transform, false);
            root.transform.SetPositionAndRotation(transform.position, Quaternion.identity);
#if UNITY_EDITOR
            if (!Application.isPlaying) Undo.RegisterCreatedObjectUndo(root, "섬 맵 생성");
#endif
            Transform ground = Group(root.transform, "Ground");
            Transform water = Group(root.transform, "Water");
            Transform bridges = Group(root.transform, "Bridges");
            Transform props = Group(root.transform, "Props");
            Transform anchors = Group(root.transform, "Anchors");
            Transform lockedRoot = Group(root.transform, "LockedAreas");
            Transform blockers = Group(root.transform, "Blockers");

            int farmLayer = LayerMask.NameToLayer(farmableLayerName);
            int groundLayer = LayerMask.NameToLayer(groundLayerName);
            int blockerLayer = LayerMask.NameToLayer(blockerLayerName);
            if (farmLayer < 0) L.warnings.Add($"레이어 '{farmableLayerName}'가 없음 (밭 판정이 안 될 수 있음)");

            // 지형 (큰 맵은 오래 걸려서 진행 표시줄을 띄움)
            int[,] landDistance = MapAlgorithms.DistanceField(L.width, L.depth, (x, z) => L.land[x, z]);
            bool useOcean = prefabs.oceanPlane != null;
            try
            {
                for (int x = 0; x < L.width; x++)
                {
#if UNITY_EDITOR
                    if (!Application.isPlaying && x % 4 == 0)
                        EditorUtility.DisplayProgressBar("섬 맵 생성", $"지형 배치 중... ({x}/{L.width})", (float)x / L.width);
#endif
                    for (int z = 0; z < L.depth; z++)
                    {
                        if (L.land[x, z]) BuildColumn(L, x, z, ground, farmLayer, groundLayer);
                        else if (!useOcean || L.river[x, z] || landDistance[x, z] <= seaTileMargin) BuildWaterCell(L, x, z, water, groundLayer);
                    }
                }
            }
            finally
            {
#if UNITY_EDITOR
                if (!Application.isPlaying) EditorUtility.ClearProgressBar();
#endif
            }

            if (fillHollowGround) BuildGroundFill(L, ground, groundLayer);
            if (smoothSeabed) BuildSeabedMesh(L, water);
            if (smoothSurface) BuildSurfaceMesh(L, ground);
            else if (fillBevelGaps) BuildGapMesh(L, ground);

            if (prefabs.oceanPlane != null)
            {
                GameObject ocean = Spawn(prefabs.oceanPlane, CellToWorld(L, L.width / 2, L.depth / 2, WaterY - 0.02f), Quaternion.identity, water, OceanS);
                // 바다 판이 시선·마우스 판정을 가로채지 않게 콜라이더 제거
                if (ocean != null) { ocean.name = "Ocean"; SetLayer(ocean, IgnoreRaycastLayer); RemoveColliders(ocean); }
            }

            // 잠긴 지역 그룹 (부서진 다리·벽은 locked 쪽, 수리된 다리는 unlocked 쪽)
            var lockedGroups = new Dictionary<int, Transform>();
            var unlockedGroups = new Dictionary<int, Transform>();
            var gates = new Dictionary<int, LockedAreaGate>();
            for (int i = 0; i < zones.Count; i++)
            {
                if (!zones[i].locked || !L.gateCells.ContainsKey(i)) continue;

                var area = new GameObject($"LockedArea_{zones[i].displayName}");
                area.transform.SetParent(lockedRoot, false);
                Transform lockedObjs = Group(area.transform, "WhileLocked");
                Transform unlockedObjs = Group(area.transform, "AfterUnlock");
                lockedGroups[i] = lockedObjs;
                unlockedGroups[i] = unlockedObjs;

                Vector2Int gc = L.gateCells[i];
                var gatePoint = new GameObject("GatePoint");
                gatePoint.transform.SetParent(area.transform, false);
                gatePoint.transform.position = CellTop(L, gc.x, gc.y);

                var gate = area.AddComponent<LockedAreaGate>();
                gate.Configure(zones[i].displayName, zones[i].lockedMessage, lockedObjs.gameObject, unlockedObjs.gameObject, gatePoint.transform);
                gates[i] = gate;
            }

            BuildBridges(L, bridges, lockedGroups, unlockedGroups, groundLayer);
            BuildRamps(L, ground, groundLayer);
            BuildProps(L, props, blockerLayer);
            BuildAnchors(L, anchors, blockerLayer);
            BuildLockedWalls(L, lockedGroups, blockerLayer);
            BuildWaterBlockers(L, blockers);

            foreach (Transform g in unlockedGroups.Values) g.gameObject.SetActive(false);

            int blockCount = ground.childCount;
            Debug.Log($"[IslandMapGenerator] 시드 {seed} 맵 생성 완료 - 지형 블록 {blockCount}개, 나무 {L.treePositions.Count}개, 바위 {L.rockPositions.Count}개" +
                      (L.warnings.Count > 0 ? $", 경고 {L.warnings.Count}개" : ""));
            foreach (string w in L.warnings) Debug.LogWarning($"[IslandMapGenerator] {w}");
        }

        // 땅 1칸 = 위쪽 블록 + 옆에서 보이는 아래 블록들 (가려지는 블록은 만들지 않아 최적화)
        private void BuildColumn(IslandLayout L, int x, int z, Transform parent, int farmLayer, int groundLayer)
        {
            int top = L.level[x, z];
            int zi = L.zoneIndex[x, z];
            ZoneType type = zi >= 0 ? zones[zi].type : ZoneType.Wild;

            for (int y = 0; y <= top; y++)
            {
                bool isTop = y == top;
                if (!isTop && !IsSideExposed(L, x, z, y)) continue;

                GameObject prefab = isTop ? TopPrefab(L, x, z, type) : FillPrefab(type);
                GameObject block = Spawn(prefab, CellToWorld(L, x, z, y * LevelH) + blockOffset, Quaternion.identity, parent, BlockS);
                if (block == null) continue;
                block.name = $"Block_{x}_{z}_{y}";
                EnsureCollider(block);
                if (smoothSurface) HideRenderers(block);

                if (isTop && IsFarmable(L, x, z, zi, type, prefab))
                {
                    // 농장 구역은 밭 전용 비주얼, 그 외 맨땅은 원래 블록 모습 그대로 두고 갈면 밭으로 바뀜
                    // 매끈한 지면이면 갈기 전엔 아무 모델도 안 띄움 (매끈한 지면이 대신 보임), 갈면 밭 블록이 나타남
                    GameObject natural = smoothSurface ? null
                        : prefab;
                    SetupFarmTile(block, natural);
                    SetLayer(block, farmLayer);
                }
                else
                {
                    SetLayer(block, groundLayer);
                    MarkStatic(block);
                }
            }
        }

        // ---------- 매끈한 물 밑바닥 ----------
        // 칸 꼭짓점마다 "가장 가까운 땅까지 거리"로 깊이를 정한 경사면 메시 하나를 만든다
        // 해안 근처는 얕아서 물이 밝고 투명하게, 멀어질수록 깊어져 물 색이 진해짐 (물 셰이더의 깊이 표현과 맞물림)
        private void BuildSeabedMesh(IslandLayout L, Transform parent)
        {
            int[,] landDist = MapAlgorithms.DistanceField(L.width, L.depth, (x, z) => L.land[x, z]);
            float shallowY = WaterY - LevelH * 0.35f;
            float deepY = WaterY - LevelH * Mathf.Max(0.5f, seabedMaxDepth);

            int vw = L.width + 1, vd = L.depth + 1;
            var verts = new Vector3[vw * vd];
            var uvs = new Vector2[vw * vd];
            for (int vx = 0; vx < vw; vx++)
            {
                for (int vz = 0; vz < vd; vz++)
                {
                    // 꼭짓점을 둘러싼 4칸 중 땅과 가장 가까운 거리
                    int best = int.MaxValue;
                    for (int dx = -1; dx <= 0; dx++)
                        for (int dz = -1; dz <= 0; dz++)
                        {
                            int cx = Mathf.Clamp(vx + dx, 0, L.width - 1), cz = Mathf.Clamp(vz + dz, 0, L.depth - 1);
                            best = Mathf.Min(best, landDist[cx, cz]);
                        }
                    float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(best / Mathf.Max(1f, seabedShelfWidth)));
                    float y = Mathf.Lerp(shallowY, deepY, t);

                    Vector3 corner = LocalCell(L, vx, vz, y) - new Vector3(Cell * 0.5f, 0f, Cell * 0.5f);
                    verts[vx * vd + vz] = corner;
                    uvs[vx * vd + vz] = new Vector2(vx, vz);
                }
            }

            var tris = new int[L.width * L.depth * 6];
            int k = 0;
            for (int x = 0; x < L.width; x++)
                for (int z = 0; z < L.depth; z++)
                {
                    int a = x * vd + z, b = a + 1, c = a + vd, d = c + 1;
                    tris[k++] = a; tris[k++] = b; tris[k++] = d;
                    tris[k++] = a; tris[k++] = d; tris[k++] = c;
                }

            var mesh = new Mesh { name = "IslandSeabed", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            GameObject go = CreateMeshObject("Seabed", parent, mesh, new[] { MakeColorMaterial("IslandSeabed", seabedColor) });
            SetLayer(go, IgnoreRaycastLayer);
        }

        // ---------- 블록 틈 메우기 ----------
        // 블록 모서리가 둥글어서 블록 사이에 얇은 틈이 생기는데, 생성기는 안 보이는 아래 블록을 만들지 않으므로
        // 그 틈으로 빈 공간(하늘·바다)이 비쳐 보인다. 블록보다 살짝 안쪽·아래에 색 상자를 메시 하나로 깔아 틈을 막는다.
        // 윗면은 잔디 그림자 색, 옆면은 흙 색 (서브메시 2개)
        private void BuildGapMesh(IslandLayout L, Transform parent)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var topTris = new List<int>();
            var sideTris = new List<int>();
            float half = Cell * 0.5f;
            float sink = LevelH * 0.12f;   // 윗면보다 이만큼 아래 (블록 윗면을 가리지 않도록)
            float bottom = -LevelH;

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, List<int> tris)
            {
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                normals.Add(n); normals.Add(n); normals.Add(n); normals.Add(n);
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
            }

            for (int x = 0; x < L.width; x++)
            {
                for (int z = 0; z < L.depth; z++)
                {
                    if (!L.land[x, z]) continue;
                    float top = (L.level[x, z] + 1) * LevelH - sink;
                    Vector3 c = LocalCell(L, x, z, 0f);

                    // 윗면 (시계 방향으로 감아 위를 향하게)
                    Quad(c + new Vector3(-half, top, -half), c + new Vector3(-half, top, half),
                         c + new Vector3(half, top, half), c + new Vector3(half, top, -half), Vector3.up, topTris);

                    // 옆면: 옆 칸이 더 낮을 때만 (그 높이부터 내 윗면까지)
                    for (int d = 0; d < 4; d++)
                    {
                        Vector2Int dir = MapAlgorithms.Dirs4[d];
                        int nx = x + dir.x, nz = z + dir.y;
                        int nTop = (nx < 0 || nz < 0 || nx >= L.width || nz >= L.depth) ? -1 : L.TopLevel(nx, nz);
                        if (nTop >= L.level[x, z]) continue;

                        float from = nTop < 0 ? bottom : (nTop + 1) * LevelH - sink;
                        Vector3 n = new Vector3(dir.x, 0f, dir.y);
                        Vector3 right = new Vector3(-dir.y, 0f, dir.x); // 바깥에서 이 면을 바라볼 때 오른쪽
                        Vector3 face = c + n * half;
                        Quad(face - right * half + Vector3.up * from, face - right * half + Vector3.up * top,
                             face + right * half + Vector3.up * top, face + right * half + Vector3.up * from, n, sideTris);
                    }
                }
            }

            var mesh = new Mesh { name = "IslandGapFill", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(topTris, 0);
            mesh.SetTriangles(sideTris, 1);
            mesh.RecalculateBounds();

            GameObject go = CreateMeshObject("GapFill", parent, mesh, new[]
            {
                MakeColorMaterial("IslandGapTop", gapTopColor),
                MakeColorMaterial("IslandGapSide", gapSideColor)
            });
            SetLayer(go, IgnoreRaycastLayer);
            MarkStatic(go);
        }

        // ---------- 매끈한 지면 ----------
        // 칸마다 윗면 사각형 + 옆 칸이 낮을 때 옆면 사각형을 이어 붙여 섬 전체를 메시 하나로 그린다
        // 같은 종류(잔디·길·모래·광장·바위)끼리 같은 단색 머티리얼을 써서 칸 경계가 전혀 보이지 않음
        // 윗면은 실제 블록 윗면보다 아주 살짝 낮게 그려서, 갈아놓은 밭 블록이 지면 위로 깔끔하게 드러나게 함
        private enum SurfaceKind { Grass, Path, Sand, Plaza, Rock, Dirt, Stone, Count }

        // 지면 윗면은 칸마다 색을 칠한 "지도 텍스처" 한 장으로 그림:
        // - 칸 경계가 아닌 땅 종류 경계에서만 색이 바뀌고, 그 경계도 살짝 번져서 자연스럽게 섞임
        // - 노이즈로 밝고 어두운 얼룩을 넣어 넓은 풀밭도 밋밋하지 않음
        private Texture2D BuildSurfaceTexture(IslandLayout L)
        {
            int tpc = Mathf.Clamp(surfaceTexelsPerCell, 2, 8);
            int tw = L.width * tpc, th = L.depth * tpc;
            var pixels = new Color[tw * th];
            var rng = new System.Random(seed + 101);
            Vector2 off = new Vector2(rng.Next(-9999, 9999), rng.Next(-9999, 9999));

            for (int py = 0; py < th; py++)
            {
                for (int px = 0; px < tw; px++)
                {
                    int x = px / tpc, z = py / tpc;
                    Color c;
                    SurfaceKind kind = SurfaceKind.Grass;
                    if (L.land[x, z])
                    {
                        int zi = L.zoneIndex[x, z];
                        kind = TopSurfaceKind(L, x, z, zi >= 0 ? zones[zi].type : ZoneType.Wild);
                        c = KindColor(kind);
                    }
                    else c = surfaceSandColor; // 물 칸 (윗면이 없어 실제로 보이진 않음, 번짐용)

                    // 칸 단위가 아닌 실제 위치 기준 노이즈 (큰 얼룩 + 잔 무늬)
                    float wx = (px + 0.5f) / tpc, wz = (py + 0.5f) / tpc;
                    float big = MapAlgorithms.Fbm(wx * surfaceNoiseScale + off.x, wz * surfaceNoiseScale + off.y, 3) - 0.5f;
                    float fine = Mathf.PerlinNoise(wx * 0.9f + off.y, wz * 0.9f + off.x) - 0.5f;
                    float v = big * 2f + fine * 0.5f;

                    if (kind == SurfaceKind.Grass)
                        c = v > 0f ? Color.Lerp(c, surfaceGrassHighlight, v * surfaceColorVariation * 3f)
                                   : Color.Lerp(c, c * 0.75f, -v * surfaceColorVariation * 3f);
                    else
                        c *= 1f + v * surfaceColorVariation;

                    c.a = 1f;
                    pixels[py * tw + px] = c;
                }
            }

            // 박스 블러로 땅 종류 경계를 부드럽게
            for (int pass = 0; pass < surfaceEdgeBlur; pass++)
            {
                var blurred = new Color[pixels.Length];
                for (int py = 0; py < th; py++)
                    for (int px = 0; px < tw; px++)
                    {
                        Color sum = Color.clear;
                        int n = 0;
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int qx = px + dx, qy = py + dy;
                                if (qx < 0 || qy < 0 || qx >= tw || qy >= th) continue;
                                sum += pixels[qy * tw + qx];
                                n++;
                            }
                        blurred[py * tw + px] = sum / n;
                    }
                pixels = blurred;
            }

            var tex = new Texture2D(tw, th, TextureFormat.RGBA32, true)
            {
                name = "IslandSurfaceMap",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 4
            };
            tex.SetPixels(pixels);
            tex.Apply(true);
            return SaveGeneratedAsset(tex, "IslandSurfaceMap.asset");
        }

        private Color KindColor(SurfaceKind kind)
        {
            switch (kind)
            {
                case SurfaceKind.Path: return surfacePathColor;
                case SurfaceKind.Sand: return surfaceSandColor;
                case SurfaceKind.Plaza: return surfacePlazaColor;
                case SurfaceKind.Rock: return surfaceRockColor;
                case SurfaceKind.Dirt: return surfaceDirtColor;
                case SurfaceKind.Stone: return surfaceStoneColor;
                default: return surfaceGrassColor;
            }
        }

        private void BuildSurfaceMesh(IslandLayout L, Transform parent)
        {
            int kinds = (int)SurfaceKind.Count;
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>[kinds];
            for (int i = 0; i < kinds; i++) tris[i] = new List<int>();

            float half = Cell * 0.5f;
            float sink = 0.02f;
            float bottom = -LevelH;
            int cx0 = 0, cz0 = 0; // 지금 그리는 칸 (지도 텍스처 UV 계산용)

            // 윗면은 모두 Grass 서브메시(지도 텍스처)에 넣고, 칸 위치로 UV를 계산
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, SurfaceKind kind)
            {
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                normals.Add(n); normals.Add(n); normals.Add(n); normals.Add(n);
                if (n == Vector3.up)
                {
                    float u0 = (float)cx0 / L.width, u1 = (float)(cx0 + 1) / L.width;
                    float v0 = (float)cz0 / L.depth, v1 = (float)(cz0 + 1) / L.depth;
                    uvs.Add(new Vector2(u0, v0)); uvs.Add(new Vector2(u0, v1)); uvs.Add(new Vector2(u1, v1)); uvs.Add(new Vector2(u1, v0));
                    kind = SurfaceKind.Grass;
                }
                else
                {
                    uvs.Add(Vector2.zero); uvs.Add(Vector2.zero); uvs.Add(Vector2.zero); uvs.Add(Vector2.zero);
                }
                List<int> t = tris[(int)kind];
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
                t.Add(i); t.Add(i + 2); t.Add(i + 3);
            }

            for (int x = 0; x < L.width; x++)
            {
                for (int z = 0; z < L.depth; z++)
                {
                    if (!L.land[x, z]) continue;
                    cx0 = x; cz0 = z;

                    int zi = L.zoneIndex[x, z];
                    ZoneType type = zi >= 0 ? zones[zi].type : ZoneType.Wild;
                    SurfaceKind topKind = TopSurfaceKind(L, x, z, type);
                    SurfaceKind sideKind = type == ZoneType.Highland ? SurfaceKind.Stone
                                         : L.shore[x, z] || type == ZoneType.Beach ? SurfaceKind.Sand
                                         : SurfaceKind.Dirt;

                    float top = (L.level[x, z] + 1) * LevelH - sink;
                    Vector3 c = LocalCell(L, x, z, 0f);

                    // 윗면
                    Quad(c + new Vector3(-half, top, -half), c + new Vector3(-half, top, half),
                         c + new Vector3(half, top, half), c + new Vector3(half, top, -half), Vector3.up, topKind);

                    // 옆면 (옆 칸이 더 낮을 때, 그 높이부터 내 윗면까지)
                    for (int d = 0; d < 4; d++)
                    {
                        Vector2Int dir = MapAlgorithms.Dirs4[d];
                        int nx = x + dir.x, nz = z + dir.y;
                        int nTop = (nx < 0 || nz < 0 || nx >= L.width || nz >= L.depth) ? -1 : L.TopLevel(nx, nz);
                        if (nTop >= L.level[x, z]) continue;

                        float from = nTop < 0 ? bottom : (nTop + 1) * LevelH - sink;
                        Vector3 n = new Vector3(dir.x, 0f, dir.y);
                        Vector3 right = new Vector3(-dir.y, 0f, dir.x);
                        Vector3 face = c + n * half;
                        Quad(face - right * half + Vector3.up * from, face - right * half + Vector3.up * top,
                             face + right * half + Vector3.up * top, face + right * half + Vector3.up * from, n, sideKind);
                    }
                }
            }

            var mesh = new Mesh { name = "IslandSurface", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = kinds;
            for (int i = 0; i < kinds; i++) mesh.SetTriangles(tris[i], i);
            mesh.RecalculateBounds();

            var materials = new Material[kinds];
            // 윗면 전체 = 지도 텍스처 (흰색 머티리얼에 텍스처를 입힘)
            Material topMat = MakeColorMaterial("IslandSurface_Top", Color.white);
            Texture2D surfaceMap = BuildSurfaceTexture(L);
            if (topMat.HasProperty("_BaseMap")) topMat.SetTexture("_BaseMap", surfaceMap);
            topMat.mainTexture = surfaceMap;
#if UNITY_EDITOR
            if (!Application.isPlaying) { EditorUtility.SetDirty(topMat); AssetDatabase.SaveAssets(); }
#endif
            materials[(int)SurfaceKind.Grass] = topMat;
            materials[(int)SurfaceKind.Path] = MakeColorMaterial("IslandSurface_Path", surfacePathColor);
            materials[(int)SurfaceKind.Sand] = MakeColorMaterial("IslandSurface_Sand", surfaceSandColor);
            materials[(int)SurfaceKind.Plaza] = MakeColorMaterial("IslandSurface_Plaza", surfacePlazaColor);
            materials[(int)SurfaceKind.Rock] = MakeColorMaterial("IslandSurface_Rock", surfaceRockColor);
            materials[(int)SurfaceKind.Dirt] = MakeColorMaterial("IslandSurface_Dirt", surfaceDirtColor);
            materials[(int)SurfaceKind.Stone] = MakeColorMaterial("IslandSurface_Stone", surfaceStoneColor);

            GameObject go = CreateMeshObject("Surface", parent, mesh, materials);
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            go.GetComponent<MeshRenderer>().receiveShadows = true;
            SetLayer(go, IgnoreRaycastLayer);
            MarkStatic(go);
        }

        // 칸 윗면 종류 (TopPrefab과 같은 규칙)
        private SurfaceKind TopSurfaceKind(IslandLayout L, int x, int z, ZoneType type)
        {
            if (L.road[x, z]) return SurfaceKind.Path;
            if (L.shore[x, z]) return SurfaceKind.Sand;
            switch (type)
            {
                case ZoneType.Beach: return SurfaceKind.Sand;
                case ZoneType.Highland:
                    {
                        int zi = L.zoneIndex[x, z];
                        int peak = zi >= 0 ? zones[zi].level + mountainHeight : L.level[x, z];
                        return mountainRockTiers > 0 && L.level[x, z] > peak - mountainRockTiers ? SurfaceKind.Rock : SurfaceKind.Grass;
                    }
                default: return SurfaceKind.Grass;
            }
        }

        // 블록 모델은 숨기고 콜라이더만 남김 (매끈한 지면 모드)
        private static void HideRenderers(GameObject go)
        {
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        }

        // 메시를 보여주는 오브젝트 생성 (에디터에서는 메시를 에셋 파일로 저장해야 씬을 다시 열어도 남음)
        private GameObject CreateMeshObject(string name, Transform parent, Mesh mesh, Material[] materials)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = SaveGeneratedAsset(mesh, mesh.name + ".asset");
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = materials;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        // 단색 머티리얼 생성 (URP Lit, 없으면 기본 셰이더)
        private Material MakeColorMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader) { name = name };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            mat.color = color;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.1f);
            return SaveGeneratedAsset(mat, name + ".mat");
        }

        // 생성한 메시·머티리얼을 Assets/IslandGenerated 폴더에 저장 (같은 이름이 있으면 교체)
        // 저장하지 않으면 씬을 닫았다 열 때 메시·머티리얼이 사라져 분홍색·빈 오브젝트가 됨
        private static T SaveGeneratedAsset<T>(T obj, string fileName) where T : UnityEngine.Object
        {
#if UNITY_EDITOR
            if (Application.isPlaying) return obj;
            const string folder = "Assets/IslandGenerated";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "IslandGenerated");
            string path = folder + "/" + fileName;
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(obj, path);
            AssetDatabase.SaveAssets();
#endif
            return obj;
        }

        // 땅 속 채우기: 최적화 때문에 옆에서 안 보이는 아래쪽 블록은 만들지 않는데,
        // 그러면 윗면 블록 밑이 비어서 캐릭터가 그 빈 공간에 빠지면 위로 못 올라온다.
        // 그래서 눈에 안 보이는 콜라이더로 칸마다 "물 밑바닥 높이 ~ 윗면 블록 바닥"을 꽉 채운다.
        // 같은 줄에서 높이가 같은 칸이 이어지면 상자 하나로 합쳐 콜라이더 수를 줄인다 (런 렝스 병합)
        private void BuildGroundFill(IslandLayout L, Transform parent, int groundLayer)
        {
            var go = new GameObject("GroundFill");
            go.transform.SetParent(parent, false);
            if (groundLayer >= 0) go.layer = groundLayer;
            float bottom = -LevelH; // 물 밑바닥 블록의 아래쪽

            for (int z = 0; z < L.depth; z++)
            {
                int runStart = -1, runLevel = -1;
                for (int x = 0; x <= L.width; x++)
                {
                    // 높이 1 이상인 땅 칸만 채움 (높이 0은 윗면 블록이 이미 바닥까지 닿아 있음)
                    int level = x < L.width && L.land[x, z] ? L.level[x, z] : -1;
                    bool needFill = level >= 1;

                    if (runStart >= 0 && (!needFill || level != runLevel))
                    {
                        int runEnd = x - 1;
                        float top = runLevel * LevelH; // 윗면 블록의 바닥 높이
                        Vector3 a = LocalCell(L, runStart, z, 0f), b = LocalCell(L, runEnd, z, 0f);
                        Vector3 center = (a + b) * 0.5f + Vector3.up * ((bottom + top) * 0.5f);
                        AddBox(go, center, new Vector3((runEnd - runStart + 1) * Cell, top - bottom, Cell));
                        runStart = -1;
                    }
                    if (needFill && runStart < 0) { runStart = x; runLevel = level; }
                }
            }
        }

        // 이 높이의 블록이 옆 칸에서 보이는지 (옆 칸이 더 낮거나 물이면 보임)
        private static bool IsSideExposed(IslandLayout L, int x, int z, int y)
        {
            foreach (Vector2Int d in MapAlgorithms.Dirs4)
            {
                int nx = x + d.x, nz = z + d.y;
                if (nx < 0 || nz < 0 || nx >= L.width || nz >= L.depth) return true;
                if (L.TopLevel(nx, nz) < y) return true;
            }
            return false;
        }

        // 이 칸 윗면에 농사를 지을 수 있는지 (스타듀밸리 방식: 맵의 모든 땅에서 가능)
        // - 가능: 잔디, 모래, 언덕, 산, 집 마당, 잠긴 지역(열리면 바로 농사 가능) 등 모든 땅
        // - 제외: 돌길(길), 경사로(비탈이라 밭 블록이 안 맞음)
        // - 광장도 가능: 광장 전용 블록을 안 넣으면 잔디 블록으로 깔려서 겉보기엔 그냥 잔디밭인데
        //   농사가 안 돼서 고장처럼 보였음
        // - 물은 땅 칸이 아니라서 애초에 대상이 아님
        // - 건물·나무·바위·오브젝트가 서 있는 칸은 FarmPlacementValidator가 갈 때 막음
        private bool IsFarmable(IslandLayout L, int x, int z, int zoneIndex, ZoneType type, GameObject topPrefab)
        {
            if (!L.land[x, z]) return false;
            if (L.road[x, z] || L.rampDir[x, z] >= 0) return false;
            return true;
        }

        // 밭 블록에 FarmTile 붙이기 (FarmGrid가 게임 시작 시 자동 등록)
        private void SetupFarmTile(GameObject block, GameObject naturalVisual)
        {
            FarmTile tile = block.GetComponent<FarmTile>();
            if (tile == null) tile = block.AddComponent<FarmTile>();
            tile.SetVisualPrefabs(naturalVisual, prefabs.farmTilledVisual);
            tile.SetWateredTint(wateredTintMultiplier);
            tile.SetCropScaleMultiplier(cropScaleMultiplier);
#if UNITY_EDITOR
            if (!Application.isPlaying) EditorUtility.SetDirty(tile);
#endif
        }

        // 물 1칸 = 밑바닥 블록 + 물 표면
        private void BuildWaterCell(IslandLayout L, int x, int z, Transform parent, int groundLayer)
        {
            if (placeSeabed && !smoothSeabed)
            {
                GameObject bedPrefab = Or(prefabs.seabed, Or(prefabs.sandTop, Or(prefabs.dirtFill, prefabs.grassTop)));
                GameObject bed = Spawn(bedPrefab, CellToWorld(L, x, z, -LevelH) + blockOffset, Quaternion.identity, parent, BlockS);
                if (bed != null) { bed.name = $"Seabed_{x}_{z}"; EnsureCollider(bed); SetLayer(bed, groundLayer); MarkStatic(bed); }
            }

            if (prefabs.waterTile != null)
            {
                GameObject w = Spawn(prefabs.waterTile, CellToWorld(L, x, z, WaterY), Quaternion.identity, parent, WaterS);
                if (w != null) { w.name = L.river[x, z] ? $"River_{x}_{z}" : $"Water_{x}_{z}"; SetLayer(w, IgnoreRaycastLayer); RemoveColliders(w); }
            }
        }

        // 강 위 다리 (잠긴 지역 쪽은 부서진 다리, 수리된 다리는 미리 만들어 꺼둠)
        private void BuildBridges(IslandLayout L, Transform parent, Dictionary<int, Transform> lockedGroups,
                                  Dictionary<int, Transform> unlockedGroups, int groundLayer)
        {
            for (int x = 0; x < L.width; x++)
            {
                for (int z = 0; z < L.depth; z++)
                {
                    if (!L.river[x, z] || !L.road[x, z]) continue;

                    Quaternion rot = Quaternion.Euler(0f, (L.bridgeAxis[x, z] == 1 ? 90f : 0f) + prefabs.bridgeYawOffset, 0f);
                    int lockZone = L.bridgeLockZone[x, z];

                    if (lockZone >= 0 && lockedGroups.ContainsKey(lockZone))
                    {
                        // 잠긴 상태: 부서진 다리 + 지나가지 못하게 막는 벽
                        if (prefabs.brokenBridge != null)
                            Spawn(prefabs.brokenBridge, CellToWorld(L, x, z, (baseLevel + 1) * LevelH), rot, lockedGroups[lockZone], BlockS);
                        AddBox(lockedGroups[lockZone].gameObject, LocalCell(L, x, z, (baseLevel + 1) * LevelH + WallH * 0.5f),
                               new Vector3(Cell, WallH + LevelH * 2f, Cell));

                        // 해금 후: 정상 다리
                        SpawnBridge(L, x, z, rot, unlockedGroups[lockZone], groundLayer);
                    }
                    else
                    {
                        SpawnBridge(L, x, z, rot, parent, groundLayer);
                    }
                }
            }
        }

        private void SpawnBridge(IslandLayout L, int x, int z, Quaternion rot, Transform parent, int groundLayer)
        {
            GameObject b;
            if (prefabs.bridge != null)
                b = Spawn(prefabs.bridge, CellToWorld(L, x, z, (baseLevel + 1) * LevelH), rot, parent, BlockS);
            else
                b = Spawn(Or(prefabs.pathTop, prefabs.grassTop), CellToWorld(L, x, z, baseLevel * LevelH) + blockOffset, Quaternion.identity, parent, BlockS);
            if (b != null) { b.name = $"Bridge_{x}_{z}"; EnsureCollider(b); SetLayer(b, groundLayer); }
        }

        // 경사로 블록 (낮은 칸 위에 놓여 높은 칸으로 올라감)
        private void BuildRamps(IslandLayout L, Transform parent, int groundLayer)
        {
            if (prefabs.slope == null)
            {
                bool any = false;
                for (int x = 0; x < L.width && !any; x++)
                    for (int z = 0; z < L.depth && !any; z++)
                        if (L.rampDir[x, z] >= 0) any = true;
                if (any) L.warnings.Add("프리팹: Slope가 비어 있어 높이가 다른 길을 올라갈 수 없음");
                return;
            }

            for (int x = 0; x < L.width; x++)
            {
                for (int z = 0; z < L.depth; z++)
                {
                    int dir = L.rampDir[x, z];
                    if (dir < 0 || !L.land[x, z]) continue;

                    Quaternion rot = Quaternion.Euler(0f, MapAlgorithms.DirToYaw(dir) + prefabs.slopeYawOffset, 0f);
                    GameObject ramp = Spawn(prefabs.slope, CellToWorld(L, x, z, (L.level[x, z] + 1) * LevelH) + blockOffset, rot, parent, BlockS);
                    if (ramp == null) continue;
                    ramp.name = $"Ramp_{x}_{z}";
                    SetLayer(ramp, groundLayer);
                    if (prefabs.fixSlopeCollider) UseMeshCollider(ramp);
                }
            }
        }

        // 나무·바위
        private void BuildProps(IslandLayout L, Transform parent, int blockerLayer)
        {
            var rng = new System.Random(seed + 7);
            PlaceProps(L, L.treePositions, prefabs.trees, parent, blockerLayer, rng, "Tree");
            PlaceProps(L, L.rockPositions, prefabs.rocks, parent, blockerLayer, rng, "Rock");
        }

        private void PlaceProps(IslandLayout L, List<Vector2> positions, GameObject[] options, Transform parent,
                                int blockerLayer, System.Random rng, string label)
        {
            if (options == null || options.Length == 0) return;
            foreach (Vector2 p in positions)
            {
                GameObject prefab = options[rng.Next(options.Length)];
                if (prefab == null) continue;

                int cx = Mathf.FloorToInt(p.x), cz = Mathf.FloorToInt(p.y);
                // 칸 안에서 살짝 흔들어 격자 느낌 줄이기
                Vector3 pos = CellToWorld(L, cx, cz, (L.level[cx, cz] + 1) * LevelH)
                              + new Vector3((p.x - cx - 0.5f) * Cell * 0.6f, 0f, (p.y - cz - 0.5f) * Cell * 0.6f);
                Quaternion rot = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                float scale = (1f + ((float)rng.NextDouble() * 2f - 1f) * propScaleJitter) * propScale * worldScale;

                GameObject prop = Spawn(prefab, pos, rot, parent, scale);
                if (prop == null) continue;
                prop.name = $"{label}_{cx}_{cz}";
                SetLayer(prop, blockerLayer);
                MarkStatic(prop);
            }
        }

        // start에서 가장 가까운 농사 가능한 맨땅 칸 (길·모래·경사로가 아닌 칸). 없으면 start 그대로
        private Vector2Int NearestFarmableCell(IslandLayout L, Vector2Int start, int zoneIndex)
        {
            for (int r = 0; r <= 8; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dz = -r; dz <= r; dz++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                        int x = start.x + dx, z = start.y + dz;
                        if (x < 0 || z < 0 || x >= L.width || z >= L.depth) continue;
                        if (!L.land[x, z] || L.road[x, z] || L.shore[x, z] || L.rampDir[x, z] >= 0) continue;
                        return new Vector2Int(x, z);
                    }
                }
            }
            return start;
        }

        // 집·상점·스폰 위치 표시 (기존 플레이어, NPC_Merchant 등을 이 위치로 옮기면 됨)
        private void BuildAnchors(IslandLayout L, Transform parent, int blockerLayer)
        {
            int home = FindZone(ZoneType.Home);
            if (home >= 0)
            {
                Vector2Int c = ZoneCenterCellOnLand(L, home);
                if (c.x >= 0)
                {
                    // 길은 집 지역 가운데로 모이기 때문에 가운데가 거의 항상 길 위다.
                    // 시작하자마자 발밑이 길이면 땅을 갈 수 없어 고장난 것처럼 보이므로, 가장 가까운 잔디 칸에서 시작
                    Vector2Int spawn = NearestFarmableCell(L, c, home);
                    Anchor(parent, "Anchor_PlayerSpawn", CellTop(L, spawn.x, spawn.y), 0f);
                    Vector2Int houseCell = new Vector2Int(c.x, Mathf.Min(L.depth - 1, c.y + 2));
                    Transform a = Anchor(parent, "Anchor_House", CellTop(L, houseCell.x, houseCell.y), 180f);
                    if (prefabs.house != null) { GameObject hObj = Spawn(prefabs.house, a.position, a.rotation, a, worldScale); if (hObj != null) SetLayer(hObj, blockerLayer); }
                }
            }

            int plaza = FindZone(ZoneType.Plaza);
            if (plaza >= 0)
            {
                Vector2Int c = ZoneCenterCellOnLand(L, plaza);
                if (c.x >= 0)
                {
                    Transform a = Anchor(parent, "Anchor_Shop", CellTop(L, c.x, c.y), 270f);
                    if (prefabs.shop != null) { GameObject sObj = Spawn(prefabs.shop, a.position, a.rotation, a, worldScale); if (sObj != null) SetLayer(sObj, blockerLayer); }
                    Vector2Int m = new Vector2Int(Mathf.Max(0, c.x - 2), c.y);
                    Anchor(parent, "Anchor_Merchant", CellTop(L, m.x, m.y), 270f);
                }
            }

            int farm = FindZone(ZoneType.Farm);
            if (farm >= 0)
            {
                Vector2Int c = ZoneCenterCellOnLand(L, farm);
                if (c.x >= 0) Anchor(parent, "Anchor_FarmCenter", CellTop(L, c.x, c.y), 0f);
            }
        }

        // 잠긴 지역 경계를 투명 벽으로 막고 입구에 잔해를 놓음
        private void BuildLockedWalls(IslandLayout L, Dictionary<int, Transform> lockedGroups, int blockerLayer)
        {
            foreach (var pair in lockedGroups)
            {
                int zi = pair.Key;
                Transform group = pair.Value;
                var walls = new GameObject("Walls");
                walls.transform.SetParent(group, false);
                walls.layer = IgnoreRaycastLayer;

                for (int x = 0; x < L.width; x++)
                {
                    for (int z = 0; z < L.depth; z++)
                    {
                        if (!L.land[x, z] || L.zoneIndex[x, z] != zi) continue;

                        for (int d = 0; d < 4; d++)
                        {
                            Vector2Int n = new Vector2Int(x, z) + MapAlgorithms.Dirs4[d];
                            if (!L.InBounds(n) || !L.IsWalkable(n)) continue;
                            if (L.land[n.x, n.y] && L.zoneIndex[n.x, n.y] == zi) continue;

                            // 두 칸 사이 경계선에 얇은 벽
                            int low = Mathf.Min(L.level[x, z], L.TopLevel(n.x, n.y) < 0 ? baseLevel : L.TopLevel(n.x, n.y));
                            int high = Mathf.Max(L.level[x, z], L.TopLevel(n.x, n.y) < 0 ? baseLevel : L.TopLevel(n.x, n.y));
                            float bottom = (low + 1) * LevelH;
                            float topY = (high + 1) * LevelH + WallH;
                            Vector3 center = LocalCell(L, x, z, (bottom + topY) * 0.5f) +
                                             new Vector3(MapAlgorithms.Dirs4[d].x, 0f, MapAlgorithms.Dirs4[d].y) * Cell * 0.5f;
                            Vector3 size = MapAlgorithms.Dirs4[d].x != 0
                                ? new Vector3(0.1f, topY - bottom, Cell)
                                : new Vector3(Cell, topY - bottom, 0.1f);
                            AddBox(walls, center, size);
                        }
                    }
                }

                // 입구 잔해 (시각적으로 막혀 보이게)
                if (prefabs.gateRubble != null && L.gateCells.TryGetValue(zi, out Vector2Int gate))
                {
                    float yaw = L.gateDirs.TryGetValue(zi, out int gd) ? MapAlgorithms.DirToYaw(gd) : 0f;
                    GameObject rubble = Spawn(prefabs.gateRubble, CellTop(L, gate.x, gate.y), Quaternion.Euler(0f, yaw, 0f), group, worldScale);
                    if (rubble != null) { rubble.name = "GateRubble"; SetLayer(rubble, blockerLayer); }
                }
            }
        }

        // 물 칸을 투명 벽으로 막기 (같은 줄의 연속된 물 칸은 박스 하나로 합쳐 콜라이더 수를 줄임)
        private void BuildWaterBlockers(IslandLayout L, Transform parent)
        {
            var go = new GameObject("WaterBlockers");
            go.transform.SetParent(parent, false);
            go.layer = IgnoreRaycastLayer;
            float height = (baseLevel + 2) * LevelH + WallH;

            for (int z = 0; z < L.depth; z++)
            {
                int runStart = -1;
                for (int x = 0; x <= L.width; x++)
                {
                    bool blocked = x < L.width && !L.land[x, z] && !L.road[x, z];
                    if (blocked && runStart < 0) runStart = x;
                    if ((!blocked || x == L.width) && runStart >= 0)
                    {
                        int runEnd = x - 1;
                        int count = runEnd - runStart + 1;
                        Vector3 a = LocalCell(L, runStart, z, 0f), b = LocalCell(L, runEnd, z, 0f);
                        Vector3 center = (a + b) * 0.5f + Vector3.up * (height * 0.5f - LevelH);
                        AddBox(go, center, new Vector3(count * Cell, height, Cell));
                        runStart = -1;
                    }
                }
            }
        }

        // ==================== 도우미 ====================

        private bool IsBorder(IslandLayout L, int x, int z) =>
            x < borderWater || z < borderWater || x >= L.width - borderWater || z >= L.depth - borderWater;

        private int FindZone(ZoneType type)
        {
            for (int i = 0; i < zones.Count; i++) if (zones[i].type == type) return i;
            return -1;
        }

        // 지역 중심 칸 (0~1 좌표 → 칸 좌표)
        private Vector2Int ZoneCenterCell(IslandLayout L, int i)
        {
            return new Vector2Int(Mathf.RoundToInt(zones[i].center.x * (L.width - 1)), Mathf.RoundToInt(zones[i].center.y * (L.depth - 1)));
        }

        // 지역 중심에서 가장 가까운, 실제로 그 지역에 속한 땅 칸 (없으면 (-1,-1))
        private Vector2Int ZoneCenterCellOnLand(IslandLayout L, int i)
        {
            Vector2Int c = ZoneCenterCell(L, i);
            Vector2Int best = new Vector2Int(-1, -1);
            float bestD = float.MaxValue;
            for (int x = 0; x < L.width; x++)
                for (int z = 0; z < L.depth; z++)
                {
                    if (!L.land[x, z] || L.zoneIndex[x, z] != i) continue;
                    float d = (new Vector2(x, z) - (Vector2)c).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = new Vector2Int(x, z); }
                }
            return best;
        }

        // 칸 좌표 → 월드 좌표 (FarmGrid가 같은 칸으로 계산하도록 생성기 위치 기준, 정수 칸 간격)
        private Vector3 CellToWorld(IslandLayout L, int x, int z, float y)
        {
            return transform.position + LocalCell(L, x, z, y);
        }

        private Vector3 LocalCell(IslandLayout L, int x, int z, float y)
        {
            return new Vector3((x - L.width / 2) * Cell, y, (z - L.depth / 2) * Cell);
        }

        // 칸 윗면 위치 (사람·건물이 서는 높이)
        private Vector3 CellTop(IslandLayout L, int x, int z)
        {
            int top = L.land[x, z] ? L.level[x, z] : baseLevel;
            return CellToWorld(L, x, z, (top + 1) * LevelH);
        }

        private GameObject TopPrefab(IslandLayout L, int x, int z, ZoneType type)
        {
            if (L.road[x, z]) return Or(prefabs.pathTop, prefabs.grassTop);
            if (L.shore[x, z]) return Or(prefabs.sandTop, prefabs.grassTop);
            switch (type)
            {
                case ZoneType.Beach: return Or(prefabs.sandTop, prefabs.grassTop);
                case ZoneType.Highland:
                    {
                        // 꼭대기 쪽 몇 단은 바위, 아래쪽은 잔디
                        int zi = L.zoneIndex[x, z];
                        int peak = zi >= 0 ? zones[zi].level + mountainHeight : L.level[x, z];
                        if (mountainRockTiers > 0 && L.level[x, z] > peak - mountainRockTiers)
                            return Or(prefabs.mountainRockTop, Or(prefabs.stoneFill, Or(prefabs.highlandTop, prefabs.grassTop)));
                        return Or(prefabs.highlandTop, prefabs.grassTop);
                    }
                default: return prefabs.grassTop;
            }
        }

        private GameObject FillPrefab(ZoneType type)
        {
            if (type == ZoneType.Highland) return Or(prefabs.stoneFill, Or(prefabs.dirtFill, prefabs.grassTop));
            if (type == ZoneType.Beach) return Or(prefabs.sandTop, Or(prefabs.dirtFill, prefabs.grassTop));
            return Or(prefabs.dirtFill, prefabs.grassTop);
        }

        private static GameObject Or(GameObject a, GameObject b) => a != null ? a : b;

        private static Transform Group(Transform parent, string name)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            return g.transform;
        }

        private static Transform Anchor(Transform parent, string name, Vector3 position, float yaw)
        {
            var a = new GameObject(name);
            a.transform.SetParent(parent, false);
            a.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            return a.transform;
        }

        // 프리팹 생성 (에디터에서는 프리팹 연결을 유지해서, 원본 프리팹을 고치면 맵에도 반영됨)
        private static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent, float scaleMultiplier)
        {
            if (prefab == null) return null;
            GameObject go = null;
#if UNITY_EDITOR
            if (!Application.isPlaying) go = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
#endif
            if (go == null) go = Instantiate(prefab, parent);
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = prefab.transform.localScale * scaleMultiplier;
            return go;
        }

        private static void AddBox(GameObject target, Vector3 localCenter, Vector3 size)
        {
            BoxCollider box = target.AddComponent<BoxCollider>();
            box.center = localCenter;
            box.size = size;
        }

        private static void SetLayer(GameObject go, int layer)
        {
            if (go == null || layer < 0) return;
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        // 프리팹에 콜라이더가 없으면 모델 크기에 맞는 상자 콜라이더를 붙임
        // (에셋 중 일부 블록은 콜라이더 없이 들어 있어서, 그 블록 위에선 캐릭터가 바닥을 뚫고 파묻힘)
        // BoxCollider는 붙이는 순간 MeshRenderer 크기에 자동으로 맞춰짐
        private static void EnsureCollider(GameObject go)
        {
            if (go == null || go.GetComponentInChildren<Collider>(true) != null) return;

            MeshFilter mf = go.GetComponentInChildren<MeshFilter>(true);
            GameObject owner = mf != null ? mf.gameObject : go;
            owner.AddComponent<BoxCollider>();
        }

        // 물 표면이 마우스·시선 판정을 가로채지 않도록 콜라이더 제거
        private static void RemoveColliders(GameObject go)
        {
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
        }

        // 경사 블록을 실제로 걸어 올라갈 수 있게 상자 콜라이더 → 메시 콜라이더
        private static void UseMeshCollider(GameObject go)
        {
            foreach (BoxCollider box in go.GetComponentsInChildren<BoxCollider>(true))
            {
                MeshFilter mf = box.GetComponent<MeshFilter>();
                GameObject owner = box.gameObject;
                DestroyImmediate(box);
                if (mf != null && mf.sharedMesh != null && owner.GetComponent<MeshCollider>() == null)
                    owner.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            }
        }

        // 움직이지 않는 블록은 정적 배칭 대상으로 (드로우콜 감소)
        private static void MarkStatic(GameObject go)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
#endif
        }

        // ==================== Scene 뷰 미리보기 ====================

        // 미리보기 색: 물 파랑 / 강 하늘색 / 일반 땅 초록(언덕은 진하게) / 길 베이지 / 다리 갈색 / 부서진 다리 빨강
        // 집 주황 / 농장 갈색 / 광장 보라 / 해변 모래색 / 높은 지대 회색 / 잠긴 입구 자홍 / 나무 진초록 점
        private void OnDrawGizmos()
        {
            if (!showPreview || previewLayout == null) return;
            IslandLayout L = previewLayout;

            for (int x = 0; x < L.width; x++)
            {
                for (int z = 0; z < L.depth; z++)
                {
                    Color color;
                    float top;
                    if (!L.land[x, z])
                    {
                        top = WaterY;
                        if (L.road[x, z]) color = L.bridgeLockZone[x, z] >= 0 ? Color.red : new Color(0.55f, 0.35f, 0.2f);
                        else color = L.river[x, z] ? new Color(0.4f, 0.8f, 1f) : new Color(0.15f, 0.4f, 0.85f);
                    }
                    else
                    {
                        top = (L.level[x, z] + 1) * LevelH;
                        int zi = L.zoneIndex[x, z];
                        color = zi < 0
                            ? (L.level[x, z] > baseLevel ? new Color(0.2f, 0.55f, 0.2f) : new Color(0.45f, 0.75f, 0.35f))
                            : ZoneColor(zones[zi].type);
                        // 산은 높을수록 밝게 (계단이 보이도록)
                        if (zi >= 0 && zones[zi].type == ZoneType.Highland)
                            color = Color.Lerp(new Color(0.35f, 0.35f, 0.35f), Color.white, (L.level[x, z] - zones[zi].level) / (float)Mathf.Max(1, mountainHeight));
                        if (L.shore[x, z]) color = new Color(0.95f, 0.88f, 0.6f);
                        if (L.road[x, z]) color = new Color(0.9f, 0.8f, 0.6f);
                        if (L.rampDir[x, z] >= 0) color = Color.yellow;
                    }

                    Gizmos.color = color;
                    Gizmos.DrawCube(CellToWorld(L, x, z, top), new Vector3(Cell * 0.95f, 0.05f, Cell * 0.95f));
                }
            }

            Gizmos.color = new Color(0.05f, 0.3f, 0.05f);
            foreach (Vector2 p in L.treePositions)
            {
                int cx = Mathf.FloorToInt(p.x), cz = Mathf.FloorToInt(p.y);
                Gizmos.DrawSphere(CellToWorld(L, cx, cz, (L.level[cx, cz] + 1) * LevelH + 0.3f), Cell * 0.3f);
            }

            Gizmos.color = Color.magenta;
            foreach (Vector2Int g in L.gateCells.Values)
                Gizmos.DrawWireCube(CellTop(L, g.x, g.y) + Vector3.up * 0.5f, new Vector3(Cell, 1f, Cell));
        }

        private static Color ZoneColor(ZoneType type)
        {
            switch (type)
            {
                case ZoneType.Home: return new Color(1f, 0.6f, 0.2f);
                case ZoneType.Farm: return new Color(0.55f, 0.35f, 0.15f);
                case ZoneType.Plaza: return new Color(0.65f, 0.5f, 0.9f);
                case ZoneType.Beach: return new Color(0.95f, 0.88f, 0.6f);
                case ZoneType.Highland: return new Color(0.6f, 0.6f, 0.6f);
                default: return Color.green;
            }
        }
    }
}