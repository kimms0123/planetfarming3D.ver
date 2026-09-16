using UnityEngine;

namespace FarmingSystem.Core
{
    public class GroundBlockPainter : MonoBehaviour
    {
        [Header("범위 설정")]
        public int width = 20;
        public int depth = 20;
        public float tileSize = 1f;

        [Header("프리팹")]
        public GameObject groundTilePrefab;
        [Tooltip("프리팹 원본 크기가 1유닛이 아닐 때 보정할 배율. 예: 원본이 2유닛이면 0.5")]
        public float visualScale = 1f;

        [Header("레이어")]
        public string targetLayerName = "FarmableGround";
    }
}