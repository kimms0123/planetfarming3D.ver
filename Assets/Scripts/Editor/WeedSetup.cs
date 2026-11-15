#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FarmingSystem.Farming;

namespace FarmingSystem.EditorTools
{
    // 잡초 매니저 만들기
    // 메뉴: Tools → 행성농사일지 → 잡초 → 잡초 매니저 만들기
    //  - 씬에 WeedManager가 없으면 만들고, 잡초 모델(spring_weed / summer_weed / fall_weed / winter_weed 프리팹)을 찾아 연결
    //  - 이미 있으면 비어 있는 모델 칸만 채움
    public static class WeedSetup
    {
        [MenuItem("Tools/행성농사일지/잡초/잡초 매니저 만들기")]
        private static void Create()
        {
            WeedManager wm = Object.FindAnyObjectByType<WeedManager>(FindObjectsInactive.Include);
            bool created = wm == null;
            if (created)
            {
                var go = new GameObject("WeedManager");
                Undo.RegisterCreatedObjectUndo(go, "잡초 매니저 만들기");
                wm = go.AddComponent<WeedManager>();
            }

            var so = new SerializedObject(wm);
            int found = 0;
            found += Fill(so, "springWeed", "spring_weed");
            found += Fill(so, "summerWeed", "summer_weed");
            found += Fill(so, "autumnWeed", "fall_weed");
            found += Fill(so, "winterWeed", "winter_weed");
            so.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(wm.gameObject.scene);
            Selection.activeObject = wm.gameObject;
            string msg = $"{(created ? "WeedManager를 만들었어요." : "이미 있는 WeedManager에 연결했어요.")}\n잡초 모델 {found}/4 연결" +
                         (found < 4 ? "\n\n못 찾은 칸은 Assets/weed 의 프리팹을 직접 넣어 주세요." : "") +
                         "\n\nCtrl+S로 씬 저장";
            Debug.Log("[잡초] " + msg);
            EditorUtility.DisplayDialog("잡초 매니저", msg, "확인");
        }

        private static int Fill(SerializedObject so, string field, string prefabName)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p == null) return 0;
            if (p.objectReferenceValue != null) return 1;
            foreach (string guid in AssetDatabase.FindAssets(prefabName + " t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) != prefabName) continue;
                p.objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                return 1;
            }
            return 0;
        }
    }
}
#endif
