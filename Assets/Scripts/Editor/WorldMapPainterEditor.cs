using UnityEngine;
using UnityEditor;
using FarmingSystem.Core;
using FarmingSystem.Farming;

[CustomEditor(typeof(WorldMapPainter))]
public class WorldMapPainterEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        WorldMapPainter map = (WorldMapPainter)target;

        if (GUILayout.Button("전체 맵 배치 (Generate World)"))
        {
            GenerateWorld(map);
        }

        if (GUILayout.Button("전체 맵 삭제"))
        {
            ClearWorld(map);
        }
    }

    private void GenerateWorld(WorldMapPainter map)
    {
        if (map.defaultFarmablePrefab == null)
        {
            Debug.LogWarning("[WorldMapPainter] Default Farmable Prefab이 비어있습니다.");
            return;
        }

        int farmableLayer = LayerMask.NameToLayer(map.farmableLayerName);
        int nonFarmableLayer = LayerMask.NameToLayer(map.nonFarmableLayerName);

        Vector3 startOffset = new Vector3(-(map.mapWidth * map.tileSize) / 2f, 0f, -(map.mapDepth * map.tileSize) / 2f);

        int farmTileCount = 0;
        int nonFarmableCount = 0;

        for (int x = 0; x < map.mapWidth; x++)
        {
            for (int z = 0; z < map.mapDepth; z++)
            {
                Vector2Int coord = new Vector2Int(x, z);
                ZoneRule matchedRule = FindMatchingRule(map, coord);

                GameObject prefabToUse = map.defaultFarmablePrefab;
                int layerToUse = farmableLayer;
                bool isFarmable = true;

                if (matchedRule != null)
                {
                    if (matchedRule.overridePrefab != null)
                        prefabToUse = matchedRule.overridePrefab;

                    isFarmable = matchedRule.groundType == GroundType.Farmable;
                    layerToUse = isFarmable ? farmableLayer : nonFarmableLayer;
                }

                Vector3 pos = map.transform.position + startOffset + new Vector3(x * map.tileSize, 0f, z * map.tileSize);

                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabToUse, map.transform);
                instance.transform.position = pos;
                instance.transform.localScale = Vector3.one * map.visualScale;
                if (layerToUse >= 0) instance.layer = layerToUse;

                if (isFarmable)
                {
                    FarmTile tile = instance.GetComponent<FarmTile>();
                    if (tile == null)
                        tile = instance.AddComponent<FarmTile>();

                    tile.SetVisualPrefabs(map.naturalVisualPrefab, map.tilledVisualPrefab, map.wateredVisualPrefab);
                    tile.SetCropScaleMultiplier(map.cropScaleMultiplier);
                    farmTileCount++;
                }
                else
                {
                    nonFarmableCount++;
                }

                Undo.RegisterCreatedObjectUndo(instance, "Generate World Tile");
            }
        }

        Debug.Log($"[WorldMapPainter] 맵 배치 완료 - FarmTile(농사 가능) {farmTileCount}칸, NonFarmable {nonFarmableCount}칸, 총 {farmTileCount + nonFarmableCount}칸");

        EditorUtility.SetDirty(map.gameObject);
    }

    private ZoneRule FindMatchingRule(WorldMapPainter map, Vector2Int coord)
    {
        for (int i = map.zoneRules.Count - 1; i >= 0; i--)
        {
            ZoneRule rule = map.zoneRules[i];
            if (coord.x >= rule.startCoord.x && coord.x < rule.startCoord.x + rule.width &&
                coord.y >= rule.startCoord.y && coord.y < rule.startCoord.y + rule.depth)
            {
                return rule;
            }
        }
        return null;
    }

    private void ClearWorld(WorldMapPainter map)
    {
        int count = map.transform.childCount;
        for (int i = map.transform.childCount - 1; i >= 0; i--)
        {
            Undo.DestroyObjectImmediate(map.transform.GetChild(i).gameObject);
        }
        Debug.Log($"[WorldMapPainter] 기존 블록 {count}개 삭제 완료");
    }
}