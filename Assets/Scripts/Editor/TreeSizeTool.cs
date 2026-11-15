#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FarmingSystem.World;

namespace FarmingSystem.EditorTools
{
    // 나무 크기 무작위 (맵을 다시 만들지 않고 지금 있는 나무만)
    // 메뉴: Tools → 행성농사일지 → 맵 → 나무 크기 무작위
    //  - 대상: 맵 생성기가 심은 계절 나무 (이름이 Tree_ 로 시작 + SeasonalObject) → 봄·여름·가을·겨울 모델과 밑동 충돌체가 같이 커짐
    //  - 크기 = 생성기가 정한 지금 크기 × (최소 ~ 최대 사이 무작위). 여러 번 눌러도 계속 커지거나 작아지지 않음 (항상 원래 크기 기준)
    //  - Hierarchy에서 나무(또는 나무들이 들어 있는 부모)를 선택하면 그 안의 나무만, 아무것도 선택 안 하면 씬의 나무 전부
    //  - Ctrl+Z로 되돌리기 가능, "원래 크기로"로 생성기 크기로 복원
    // 에디터 전용 (빌드에 안 들어감)
    public class TreeSizeTool : EditorWindow
    {
        private float minScale = 0.8f;
        private float maxScale = 1.3f;
        [Tooltip("1보다 크면 작은 나무가 더 많이, 1보다 작으면 큰 나무가 더 많이 나옴")]
        private float smallBias = 1.3f;

        [MenuItem("Tools/행성농사일지/맵/나무 크기 무작위")]
        private static void Open()
        {
            TreeSizeTool w = GetWindow<TreeSizeTool>("나무 크기");
            w.minSize = new Vector2(320f, 230f); // 창 최소 크기
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("지금 맵의 나무 크기를 무작위로 섞어요. 맵 생성기가 정한 크기를 1로 보고, 그 크기 × 아래 범위 안에서 정해요.\n" +
                                    "Hierarchy에서 나무나 그 부모를 선택하면 그 안의 나무만, 선택 안 하면 씬 전체.", MessageType.Info);
            minScale = EditorGUILayout.Slider("최소 크기", minScale, 0.3f, 2f);
            maxScale = EditorGUILayout.Slider("최대 크기", maxScale, 0.3f, 2.5f);
            if (maxScale < minScale) maxScale = minScale;
            smallBias = EditorGUILayout.Slider(new GUIContent("작은 나무 비율", "1 = 고르게, 1보다 크면 작은 나무가 더 많고 큰 나무는 가끔"), smallBias, 0.5f, 3f);

            List<Transform> trees = FindTrees();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"대상 나무: {trees.Count}그루 {(Selection.transforms.Length > 0 ? "(선택한 것 안에서)" : "(씬 전체)")}");

            using (new EditorGUI.DisabledScope(trees.Count == 0))
            {
                if (GUILayout.Button("무작위 크기로 바꾸기", GUILayout.Height(34))) Apply(trees, false);
                if (GUILayout.Button("원래 크기로 (생성기 크기)", GUILayout.Height(26))) Apply(trees, true);
            }
        }

        private void OnSelectionChange() => Repaint();

        private void Apply(List<Transform> trees, bool reset)
        {
            Undo.SetCurrentGroupName(reset ? "나무 원래 크기로" : "나무 크기 무작위");
            int group = Undo.GetCurrentGroup();
            float lo = 1f, hi = 1f;
            foreach (Transform t in trees)
            {
                Undo.RecordObject(t, "나무 크기");
                float k = reset ? 1f : Mathf.Lerp(minScale, maxScale, Mathf.Pow(Random.value, smallBias));
                t.localScale = Vector3.one * k; // 계절 나무 묶음(부모)의 원래 크기는 1 → 항상 원래 크기 기준
                lo = Mathf.Min(lo, k);
                hi = Mathf.Max(hi, k);
                EditorUtility.SetDirty(t);
            }
            Undo.CollapseUndoOperations(group);
            if (trees.Count > 0) EditorSceneManager.MarkSceneDirty(trees[0].gameObject.scene);
            Debug.Log(reset ? $"[나무 크기] {trees.Count}그루 원래 크기로" : $"[나무 크기] {trees.Count}그루 무작위 ({lo:0.##}배 ~ {hi:0.##}배). Ctrl+S로 씬 저장");
        }

        // 맵 생성기가 심은 계절 나무: 이름 Tree_ + SeasonalObject (계절 모델 4개를 가진 부모)
        private static List<Transform> FindTrees()
        {
            var list = new List<Transform>();
            var seen = new HashSet<Transform>();
            IEnumerable<SeasonalObject> source;
            if (Selection.transforms.Length > 0)
            {
                var found = new List<SeasonalObject>();
                foreach (Transform s in Selection.transforms) found.AddRange(s.GetComponentsInChildren<SeasonalObject>(true));
                source = found;
            }
            else source = Object.FindObjectsByType<SeasonalObject>(FindObjectsInactive.Include);

            foreach (SeasonalObject so in source)
            {
                Transform t = so.transform;
                if (!t.name.StartsWith("Tree_") || !seen.Add(t)) continue;
                list.Add(t);
            }
            return list;
        }
    }
}
#endif