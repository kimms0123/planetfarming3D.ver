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
        [SerializeField] private int width = 44;
        [Tooltip("세로 칸 수 (바깥 물 테두리 포함)")]
        [SerializeField] private int depth = 40;
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
        [SerializeField] private int borderWater = 2;
        [Tooltip("높을수록 섬이 작아짐")]
        [SerializeField, Range(0f, 0.6f)] private float landThreshold = 0.2f;
        [Tooltip("해안선 노이즈 크기 (작을수록 큰 굴곡)")]
        [SerializeField] private float coastNoiseScale = 0.12f;
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
        [SerializeField] private float riverMouthRadius = 8f;
        [Tooltip("클수록 강이 구불구불")]
        [SerializeField, Range(0f, 6f)] private float riverMeander = 3f;
        [SerializeField, Range(1, 2)] private int riverWidth = 1;

        [Header("길 (A* 비용)")]
        [Tooltip("다리 1칸 비용 (클수록 다리를 덜 놓으려 함)")]
        [SerializeField] private float bridgeCost = 6f;
        [Tooltip("경사로 비용 (클수록 높이가 같은 길로 돌아감)")]
        [SerializeField] private float rampCost = 3f;
        [Tooltip("이미 있는 길을 지날 때 비용 (작을수록 길이 하나로 합쳐짐)")]
        [SerializeField, Range(0.1f, 1f)] private float existingRoadCost = 0.4f;
        [Tooltip("밭을 가로지르는 비용 (밭 한가운데로 길이 나지 않게)")]
        [SerializeField] private float farmCrossCost = 40f;

        [Header("나무 / 바위 (포아송 디스크)")]
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
        [Tooltip("물 밑바닥 블록을 깔지 (물이 투명하면 켜기)")]
        [SerializeField] private bool placeSeabed = true;

        [Header("잠긴 지역")]
        [SerializeField] private float lockedWallHeight = 3f;

        [Header("지역 배치 (기획 이미지 기준, 숲·특별 지역은 제외)")]
        [SerializeField]
        private List<ZoneDefinition> zones = new List<ZoneDefinition>
        {
            new ZoneDefinition { displayName = "집",        type = ZoneType.Home,     center = new Vector2(0.50f, 0.55f), radius = 4f, level = 1 },
            new ZoneDefinition { displayName = "농장",      type = ZoneType.Farm,     center = new Vector2(0.26f, 0.48f), radius = 6f, level = 1 },
            new ZoneDefinition { displayName = "상인 광장", type = ZoneType.Plaza,    center = new Vector2(0.74f, 0.50f), radius = 5f, level = 1 },
            new ZoneDefinition { displayName = "광산",      type = ZoneType.Highland, center = new Vector2(0.48f, 0.84f), radius = 6f, level = 2,
                                 locked = true, lockedMessage = "무너진 길 너머로 광산이 보인다. 아직은 갈 수 없다." },
            new ZoneDefinition { displayName = "해변",      type = ZoneType.Beach,    center = new Vector2(0.55f, 0.15f), radius = 6f, level = 0,
                                 locked = true, lockedMessage = "다리가 부서져 있다. 언젠가 고쳐서 낚시하러 가자." },
        };

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
        [Tooltip("켜면 밭뿐 아니라 일반 잔디에도 FarmTile을 붙여 어디서든 농사 가능 (블록이 많아 무거워짐)")]
        [SerializeField] private bool farmOnAllGrass = false;

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
            if (generateRiver) CarveRiver(layout, riverOffset);

            // 강이 생긴 뒤 해안 거리 다시 계산
            layout.coastDistance = MapAlgorithms.DistanceField(layout.width, layout.depth, (x, z) => !layout.land[x, z]);

            BuildRoads(layout);
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
                    float falloff = Mathf.Sqrt(nx * nx + nz * nz);

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
                        // 지역은 정해진 높이로 평평하게
                        L.level[x, z] = zones[zi].level;
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
                bool isGoal = k == path.Count - 1;
                int zi = L.land[c.x, c.y] ? L.zoneIndex[c.x, c.y] : -1;

                // 밭과 광장 안쪽은 길 블록으로 덮지 않음 (밭은 밭, 광장은 광장 바닥)
                if (zi >= 0 && (zones[zi].type == ZoneType.Farm || zones[zi].type == ZoneType.Plaza)) continue;
                if (isGoal && zones[target].type == ZoneType.Farm) continue;

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

                int zi = L.zoneIndex[c.x, c.y];
                if (zi < 0) return L.zoneDistance[c.x, c.y] >= 2; // 일반 땅: 지역에서 2칸 이상
                return zones[zi].type == ZoneType.Highland;         // 지역 중엔 높은 지대만
            }

            foreach (Vector2 p in MapAlgorithms.PoissonDisk(L.width, L.depth, treeSpacing, rng, CanPlace))
            {
                var c = new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));
                used.Add(c);
                L.treePositions.Add(p);
            }

            foreach (Vector2 p in MapAlgorithms.PoissonDisk(L.width, L.depth, rockSpacing, rng, CanPlace))
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

            // 지형
            for (int x = 0; x < L.width; x++)
                for (int z = 0; z < L.depth; z++)
                {
                    if (L.land[x, z]) BuildColumn(L, x, z, ground, farmLayer, groundLayer);
                    else BuildWaterCell(L, x, z, water, groundLayer);
                }

            if (prefabs.oceanPlane != null)
            {
                GameObject ocean = Spawn(prefabs.oceanPlane, CellToWorld(L, L.width / 2, L.depth / 2, WaterY - 0.02f), Quaternion.identity, water, OceanS);
                if (ocean != null) ocean.name = "Ocean";
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

                bool farmable = isTop && !L.road[x, z] && L.rampDir[x, z] < 0 &&
                                (type == ZoneType.Farm || (farmOnAllGrass && type == ZoneType.Wild));
                if (farmable)
                {
                    SetupFarmTile(block);
                    SetLayer(block, farmLayer);
                }
                else
                {
                    SetLayer(block, groundLayer);
                    MarkStatic(block);
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

        // 밭 블록에 FarmTile 붙이기 (FarmGrid가 게임 시작 시 자동 등록)
        private void SetupFarmTile(GameObject block)
        {
            FarmTile tile = block.GetComponent<FarmTile>();
            if (tile == null) tile = block.AddComponent<FarmTile>();
            GameObject natural = prefabs.farmNaturalVisual != null ? prefabs.farmNaturalVisual : prefabs.grassTop;
            tile.SetVisualPrefabs(natural, prefabs.farmTilledVisual);
            tile.SetWateredTint(wateredTintMultiplier);
            tile.SetCropScaleMultiplier(cropScaleMultiplier);
#if UNITY_EDITOR
            if (!Application.isPlaying) EditorUtility.SetDirty(tile);
#endif
        }

        // 물 1칸 = 밑바닥 블록 + 물 표면
        private void BuildWaterCell(IslandLayout L, int x, int z, Transform parent, int groundLayer)
        {
            if (placeSeabed)
            {
                GameObject bedPrefab = Or(prefabs.seabed, Or(prefabs.sandTop, Or(prefabs.dirtFill, prefabs.grassTop)));
                GameObject bed = Spawn(bedPrefab, CellToWorld(L, x, z, -LevelH) + blockOffset, Quaternion.identity, parent, BlockS);
                if (bed != null) { bed.name = $"Seabed_{x}_{z}"; SetLayer(bed, groundLayer); MarkStatic(bed); }
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
            if (b != null) { b.name = $"Bridge_{x}_{z}"; SetLayer(b, groundLayer); }
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

        // 집·상점·스폰 위치 표시 (기존 플레이어, NPC_Merchant 등을 이 위치로 옮기면 됨)
        private void BuildAnchors(IslandLayout L, Transform parent, int blockerLayer)
        {
            int home = FindZone(ZoneType.Home);
            if (home >= 0)
            {
                Vector2Int c = ZoneCenterCellOnLand(L, home);
                if (c.x >= 0)
                {
                    Anchor(parent, "Anchor_PlayerSpawn", CellTop(L, c.x, c.y), 0f);
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
            switch (type)
            {
                case ZoneType.Beach: return Or(prefabs.sandTop, prefabs.grassTop);
                case ZoneType.Plaza: return Or(prefabs.plazaTop, Or(prefabs.pathTop, prefabs.grassTop));
                case ZoneType.Highland: return Or(prefabs.highlandTop, prefabs.grassTop);
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