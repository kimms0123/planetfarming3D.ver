using UnityEngine;
using UnityEditor;
using FarmingSystem.Core;

[CustomEditor(typeof(GroundBlockPainter))]
public class GroundBlockPainterEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GroundBlockPainter painter = (GroundBlockPainter)target;

        if (GUILayout.Button("씬에 블록 배치 (Generate In Scene)"))
        {
            GenerateBlocks(painter);
        }

        if (GUILayout.Button("배치된 블록 전부 삭제"))
        {
            ClearBlocks(painter);
        }
    }

    private void GenerateBlocks(GroundBlockPainter painter)
    {
        if (painter.groundTilePrefab == null)
        {
            Debug.LogWarning("Ground Tile Prefab이 비어있습니다.");
            return;
        }

        int layer = LayerMask.NameToLayer(painter.targetLayerName);
        Vector3 startOffset = new Vector3(-(painter.width * painter.tileSize) / 2f, 0f, -(painter.depth * painter.tileSize) / 2f);

        for (int x = 0; x < painter.width; x++)
        {
            for (int z = 0; z < painter.depth; z++)
            {
                Vector3 pos = painter.transform.position + startOffset + new Vector3(x * painter.tileSize, 0f, z * painter.tileSize);

                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(painter.groundTilePrefab, painter.transform);
                instance.transform.position = pos;
                instance.transform.localScale = Vector3.one * painter.visualScale; // 이 줄 추가
                if (layer >= 0) instance.layer = layer;

                Undo.RegisterCreatedObjectUndo(instance, "Generate Ground Block");
            }
        }

        EditorUtility.SetDirty(painter.gameObject);
    }

    private void ClearBlocks(GroundBlockPainter painter)
    {
        for (int i = painter.transform.childCount - 1; i >= 0; i--)
        {
            Undo.DestroyObjectImmediate(painter.transform.GetChild(i).gameObject);
        }
    }
}