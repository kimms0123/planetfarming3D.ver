#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FarmingSystem.World;
using FarmingSystem.Farming;

namespace FarmingSystem.EditorTools
{
    // IslandMapGenerator 인스펙터에 버튼 추가
    // [랜덤 시드] [미리보기] [맵 생성] [지우기] + 경고 표시
    [CustomEditor(typeof(IslandMapGenerator))]
    public class IslandMapGeneratorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var gen = (IslandMapGenerator)target;

            EditorGUILayout.HelpBox(
                "1) 프리팹 칸에 AssetHuntsl 블록을 넣고\n" +
                "2) [미리보기]로 Scene 뷰에서 색상 지도를 확인하며 시드·지역 위치를 조절한 뒤\n" +
                "3) 마음에 들면 [맵 생성]",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("랜덤 시드 + 미리보기", GUILayout.Height(28)))
            {
                Undo.RecordObject(gen, "랜덤 시드");
                gen.RandomizeSeed();
                gen.Preview();
                EditorUtility.SetDirty(gen);
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("미리보기", GUILayout.Height(28)))
            {
                gen.Preview();
                SceneView.RepaintAll();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
            if (GUILayout.Button("맵 생성", GUILayout.Height(32)))
            {
                gen.Generate();
                ConnectFarmGrid(gen);
                EditorSceneManager.MarkSceneDirty(gen.gameObject.scene);
            }
            GUI.backgroundColor = new Color(1f, 0.7f, 0.7f);
            if (GUILayout.Button("생성된 맵 지우기", GUILayout.Height(32)))
            {
                gen.Clear();
                EditorSceneManager.MarkSceneDirty(gen.gameObject.scene);
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            if (gen.HasPreview && gen.LastWarnings.Count > 0)
            {
                EditorGUILayout.Space(4);
                foreach (string w in gen.LastWarnings)
                    EditorGUILayout.HelpBox(w, MessageType.Warning);
            }

            EditorGUILayout.Space(8);
            DrawDefaultInspector();

            EditorGUILayout.Space(4);
            if (GUILayout.Button("FarmGrid 설정 다시 연결")) ConnectFarmGrid(gen);

            // 값을 바꾸면 미리보기를 바로 다시 계산
            if (GUI.changed && gen.HasPreview)
            {
                gen.Preview();
                SceneView.RepaintAll();
            }
        }

        // 씬의 FarmGrid를 생성된 맵에 맞게 자동 설정
        // Tile Size가 칸 간격과 다르면 맵 가장자리로 갈수록 좌표가 어긋나 밭이 엉뚱한 칸으로 등록되기 때문에 반드시 맞춰야 함
        private static void ConnectFarmGrid(IslandMapGenerator gen)
        {
            FarmGrid grid = Object.FindAnyObjectByType<FarmGrid>();
            if (grid == null)
            {
                Debug.LogWarning("[IslandMapGenerator] 씬에 FarmGrid가 없어 자동 연결을 건너뜀");
                return;
            }

            var so = new SerializedObject(grid);
            so.FindProperty("tileSize").floatValue = gen.CellSize;
            so.FindProperty("origin").objectReferenceValue = gen.transform;
            so.FindProperty("worldRoot").objectReferenceValue = gen.GeneratedRoot;
            so.ApplyModifiedProperties();
            Debug.Log($"[IslandMapGenerator] FarmGrid 자동 연결: Tile Size {gen.CellSize}, Origin = {gen.name}, World Root = {(gen.GeneratedRoot != null ? gen.GeneratedRoot.name : "없음")}");
        }
    }
}
#endif