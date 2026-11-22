#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FarmingSystem.Farming;

namespace FarmingSystem.EditorTools
{
    // 밭에 심는 작물(씨앗 단계 ~ 다 자란 모습) 크기를 한 번에 바꾸기
    // 메뉴: Tools → 행성농사일지 → 작물 → 작물 크기 바꾸기
    //  - 씬의 모든 밭 칸(FarmTile)의 Crop Scale Multiplier + 섬 생성기(IslandMapGenerator)의 같은 칸을 같이 바꿈
    //    (섬을 다시 생성해도 같은 크기가 유지되게)
    //  - 섬을 다시 만들 필요 없음. 이미 심어 둔 작물은 다시 심거나 저장을 불러오면 새 크기
    //  - 지금 값 2 × 1.5배 = 3 이 기본으로 들어가 있음
    public class CropScaleTool : EditorWindow
    {
        [SerializeField] private float scale = 3f;

        [MenuItem("Tools/행성농사일지/작물/작물 크기 바꾸기")]
        private static void Open()
        {
            var w = GetWindow<CropScaleTool>("작물 크기");
            w.minSize = new Vector2(340, 150);
            FarmTile any = FindAnyObjectByType<FarmTile>(FindObjectsInactive.Include);
            if (any != null)
            {
                SerializedProperty p = new SerializedObject(any).FindProperty("cropScaleMultiplier");
                if (p != null) w.current = p.floatValue;
            }
        }

        private float current = -1f;

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("밭에 심는 작물 전체(씨앗~다 자람)의 크기 배율이에요.\n" +
                                    (current > 0f ? $"지금 값: {current:0.##}  →  1.5배로 키우려면 {current * 1.5f:0.##}" : "씬에서 밭 칸을 찾지 못했어요"), MessageType.Info);
            scale = EditorGUILayout.Slider("작물 크기 배율", scale, 0.5f, 6f);
            if (GUILayout.Button("씬의 모든 밭 칸에 적용", GUILayout.Height(30))) Apply();
        }

        private void Apply()
        {
            int tiles = 0;
            foreach (FarmTile t in FindObjectsByType<FarmTile>(FindObjectsInactive.Include))
            {
                var so = new SerializedObject(t);
                SerializedProperty p = so.FindProperty("cropScaleMultiplier");
                if (p == null) continue;
                p.floatValue = scale;
                so.ApplyModifiedProperties();
                tiles++;
            }

            bool gen = false;
            foreach (MonoBehaviour mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
            {
                if (mb == null || mb.GetType().Name != "IslandMapGenerator") continue;
                var so = new SerializedObject(mb);
                SerializedProperty p = so.FindProperty("cropScaleMultiplier");
                if (p == null) continue;
                p.floatValue = scale;
                so.ApplyModifiedProperties();
                gen = true;
            }

            EditorSceneManager.MarkAllScenesDirty();
            current = scale;
            string msg = $"밭 칸 {tiles}개의 작물 크기를 {scale:0.##}로 바꿨어요" + (gen ? "\n섬 생성기 값도 같이 바꿨어요" : "") +
                         "\n\nCtrl+S로 씬을 저장해 주세요.\n이미 심어 둔 작물은 다시 심거나 저장을 불러오면 바뀌어요.";
            Debug.Log("[작물 크기] " + msg);
            EditorUtility.DisplayDialog("작물 크기", msg, "확인");
        }
    }
}
#endif