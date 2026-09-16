using UnityEngine;

namespace FarmingSystem.Core
{
    /// <summary>
    /// 배경 월드 바닥을 지정된 범위만큼 블록 타일로 깔아주는 생성기.
    /// FarmGrid(밭)와 달리 씬 시작 시 한 번만 생성되는 고정 배경이다.
    /// </summary>
    public class WorldGroundGenerator : MonoBehaviour
    {
        [Header("범위 설정")]
        [SerializeField] private int width = 20;
        [SerializeField] private int depth = 20;
        [SerializeField] private float tileSize = 1f;

        [Header("프리팹")]
        [SerializeField] private GameObject groundTilePrefab; // 예: 3D_Tile_Ground_01

        private void Awake()
        {
            GenerateGround();
        }

        private void GenerateGround()
        {
            Vector3 startOffset = new Vector3(-(width * tileSize) / 2f, 0f, -(depth * tileSize) / 2f);

            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < depth; z++)
                {
                    Vector3 pos = transform.position + startOffset + new Vector3(x * tileSize, 0f, z * tileSize);
                    Instantiate(groundTilePrefab, pos, Quaternion.identity, transform);
                }
            }
        }
    }
}