using UnityEngine;

namespace FarmingSystem.Building
{
    // 건축물 1종의 정보 (우주선 집, 상인 우주선 등) - 건축물을 추가할 때마다 하나씩 만들면 됨
    // 만들기: Project 창 우클릭 → Create → FarmingSystem → Building Data
    // - 모델(프리팹)이 어떤 크기·방향으로 만들어졌든, 차지할 칸 수(Footprint)에 맞춰 자동으로 크기를 맞추고 바닥에 붙여 줌
    [CreateAssetMenu(fileName = "NewBuildingData", menuName = "FarmingSystem/Building Data")]
    public class BuildingData : ScriptableObject
    {
        [Header("기본 정보")]
        public string displayName = "새 건물";
        [TextArea(2, 4)] public string description;
        public Sprite icon;

        [Header("모델")]
        [Tooltip("완성되면 놓일 건물 모델 (그래픽 팀 프리팹)")]
        public GameObject prefab;
        [Tooltip("모델이 바라보는 방향 보정 (도). 설치했을 때 건물 정면이 뒤나 옆을 보면 90/180/270으로")]
        public float modelYaw = 0f;

        [Header("크기")]
        [Tooltip("차지하는 칸 수 (가로 x 세로). 동숲 집은 대략 3x3 정도 느낌")]
        public Vector2Int footprint = new Vector2Int(3, 3);
        [Tooltip("모델이 칸을 얼마나 채울지 (1 = 칸 끝까지, 0.9 = 가장자리에 조금 여유)")]
        [Range(0.5f, 1.2f)] public float fill = 0.9f;
        [Tooltip("0보다 크면 자동 크기 대신 이 배율을 그대로 씀")]
        public float manualScale = 0f;

        [Header("짓기")]
        [Tooltip("설치할 때 드는 돈 (0 = 무료, 키트를 상점에서 사는 방식이면 0)")]
        public int cost = 0;
        [Tooltip("공사 기간 (일). 동숲처럼 1이면 다음 날 아침 완성, 0이면 바로 완성")]
        public int buildDays = 1;
        [Tooltip("설치할 때 90도씩 돌릴 수 있게 (끄면 항상 같은 방향)")]
        public bool allowRotate = true;
    }
}