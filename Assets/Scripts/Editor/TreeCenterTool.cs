#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using FarmingSystem.World;

namespace FarmingSystem.EditorTools
{
    // 계절 나무 프리팹의 모델 중심을 프리팹 원점에 맞추는 도구
    // 원인: 맵 생성기는 봄 나무 크기에 맞춘 충돌체(캡슐) 하나를 나무 자리 가운데에 붙임
    //       그런데 winter_tree 프리팹은 안쪽 모델(winter_tree1, winter_tree2)이 원점에서 몇 m 떨어져 있어서
    //       겨울엔 보이는 나무와 충돌체 위치가 어긋남 → 나무는 통과되고 빈 곳에서 막힘
    // 해결: 모델을 원점(=충돌체 위치)으로 옮김. 높이(Y)는 건드리지 않음
    //       씬의 나무 211개는 이 프리팹의 인스턴스라 저장하면 자동으로 같이 바뀜 (맵 다시 만들 필요 없음)
    public static class TreeCenterTool
    {
        private const float Tolerance = 0.05f; // 이보다 적게 어긋나면 그대로 둠 (m)

        [MenuItem("Tools/행성농사일지/나무/계절 나무 중심 맞추기 (통과 방지)")]
        private static void Run()
        {
            SeasonalBlockSet set = FindTreeSet();
            if (set == null)
            {
                EditorUtility.DisplayDialog("계절 나무 중심 맞추기",
                    "TreeSeasonSet을 찾지 못했어요.\nProject 창에서 계절 나무 묶음(Seasonal Block Set)을 선택한 뒤 다시 실행해 주세요.", "확인");
                return;
            }

            var prefabs = new List<GameObject>();
            foreach (SeasonalBlockSet.Entry e in set.entries)
                foreach (GameObject p in e.All())
                    if (p != null && !prefabs.Contains(p)) prefabs.Add(p);

            // 1) 측정만
            var toFix = new List<(string path, Vector3 offset)>();
            var report = new StringBuilder();
            foreach (GameObject p in prefabs)
            {
                string path = AssetDatabase.GetAssetPath(p);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (!Measure(root, out Vector3 off, out bool meshOnRoot))
                    {
                        report.AppendLine($"- {p.name}: 모델 없음 (건너뜀)");
                        continue;
                    }
                    float dist = off.magnitude;
                    if (dist < Tolerance)
                        report.AppendLine($"- {p.name}: 정상 (어긋남 {dist:0.00}m)");
                    else if (meshOnRoot)
                        report.AppendLine($"- {p.name}: {dist:0.00}m 어긋남 - 모델이 루트에 있어 자동 수정 불가");
                    else
                    {
                        report.AppendLine($"- {p.name}: {dist:0.00}m 어긋남 → 수정 예정");
                        toFix.Add((path, off));
                    }
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }

            Debug.Log("[나무 중심]\n" + report);
            if (toFix.Count == 0)
            {
                EditorUtility.DisplayDialog("계절 나무 중심 맞추기", "고칠 나무가 없어요.\n\n" + report, "확인");
                return;
            }
            if (!EditorUtility.DisplayDialog("계절 나무 중심 맞추기",
                    report + "\n위 프리팹의 모델을 가운데로 옮길까요? (높이는 그대로)\n씬에 있는 나무들도 같이 바뀝니다.",
                    "옮기기", "취소"))
                return;

            // 2) 실제 수정
            foreach (var (path, off) in toFix)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (Transform child in root.transform)
                        child.position -= off;
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    Debug.Log($"[나무 중심] {path} 모델을 {off.magnitude:0.00}m 옮김");
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("계절 나무 중심 맞추기",
                $"{toFix.Count}개 프리팹을 고쳤어요.\nPlay 후 디버그로 겨울을 띄워 나무에 막히는지 확인해 주세요.", "확인");
        }

        // 프리팹 루트 기준으로 모델(렌더러 전체)의 가로 중심이 얼마나 떨어져 있는지
        // (맵 생성기는 프리팹 루트의 회전·크기는 살리고 위치만 덮어씀 → 루트 위치 기준으로 재면 됨)
        private static bool Measure(GameObject root, out Vector3 offset, out bool meshOnRoot)
        {
            offset = Vector3.zero;
            meshOnRoot = root.GetComponent<Renderer>() != null;
            Renderer[] rs = root.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return false;
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            offset = b.center - root.transform.position;
            offset.y = 0f;
            return true;
        }

        private static SeasonalBlockSet FindTreeSet()
        {
            if (Selection.activeObject is SeasonalBlockSet selected) return selected;
            foreach (string guid in AssetDatabase.FindAssets("TreeSeasonSet t:SeasonalBlockSet"))
            {
                var s = AssetDatabase.LoadAssetAtPath<SeasonalBlockSet>(AssetDatabase.GUIDToAssetPath(guid));
                if (s != null) return s;
            }
            return null;
        }
    }
}
#endif