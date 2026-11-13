#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FarmingSystem.Save;

namespace FarmingSystem.EditorTools
{
    // 저장·시작 화면 한 번에 만들기
    // 메뉴: Tools → 행성농사일지 → 저장 → 저장·시작 화면 만들기
    //  - 씬에 SaveManager (저장 3칸·자동 저장) + TitleScreen (시작 화면) 오브젝트를 만듦 (이미 있으면 그대로 두고 설정만 갱신)
    //  - SaveManager에 프로젝트의 모든 아이템·건물 에셋 목록을 채움 → 새 아이템을 만들면 이 메뉴를 다시 누르기
    //  - 시작 화면 칸 고르는 창을 UI_Blue 조각(Panel_Dialogue, Button_Rect_Grey)으로 꾸밈
    // 메뉴: 저장 → 저장 폴더 열기 / 저장 전부 지우기 (테스트용)
    public static class SaveTitleSetup
    {
        [MenuItem("Tools/행성농사일지/저장/저장·시작 화면 만들기")]
        private static void Setup()
        {
            SaveManager sm = Object.FindAnyObjectByType<SaveManager>(FindObjectsInactive.Include);
            if (sm == null)
            {
                var go = new GameObject("SaveManager");
                Undo.RegisterCreatedObjectUndo(go, "저장·시작 화면 만들기");
                sm = go.AddComponent<SaveManager>();
            }
            sm.FillDatabases();

            TitleScreen title = Object.FindAnyObjectByType<TitleScreen>(FindObjectsInactive.Include);
            if (title == null)
            {
                var go = new GameObject("TitleScreen");
                Undo.RegisterCreatedObjectUndo(go, "저장·시작 화면 만들기");
                title = go.AddComponent<TitleScreen>();
            }
            var so = new SerializedObject(title);
            int found = 0;
            found += SetSprite(so, "panelSprite", "Panel_Dialogue");
            found += SetSprite(so, "buttonSprite", "Button_Rect_Grey");
            found += SetSprite(so, "dangerButtonSprite", "Button_Rect_Grey");
            so.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(sm.gameObject.scene);
            Selection.activeGameObject = title.gameObject;

            string scene = sm.gameObject.scene.path;
            bool inBuild = false;
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes) if (s.enabled && s.path == scene) inBuild = true;

            string msg = $"완료!\n\n· SaveManager: 아이템 {sm.ItemCount}개, 건물 {sm.BuildingCount}개 등록\n· TitleScreen: 창 꾸미기 조각 {found}/3개 찾음" +
                         (found < 3 ? " (못 찾은 건 단색으로 보임)" : "") +
                         "\n\n로고 이미지는 TitleScreen의 Logo 칸에 넣어 주세요 (비우면 글자 로고)" +
                         (inBuild ? "" : "\n\n⚠ 이 씬이 Build Settings에 없어요. File → Build Profiles(Build Settings)에서 Add Open Scenes를 눌러 주세요 (시작 화면으로 돌아가기에 필요)") +
                         "\n\nCtrl+S로 씬 저장";
            Debug.Log("[저장·시작 화면] " + msg);
            EditorUtility.DisplayDialog("저장·시작 화면", msg, "확인");
        }

        [MenuItem("Tools/행성농사일지/저장/저장 폴더 열기")]
        private static void OpenFolder() => EditorUtility.RevealInFinder(Application.persistentDataPath + "/");

        [MenuItem("Tools/행성농사일지/저장/저장 전부 지우기 (테스트용)")]
        private static void DeleteAll()
        {
            if (!EditorUtility.DisplayDialog("저장 전부 지우기", "저장 3칸을 전부 지울까요? 되돌릴 수 없어요.", "지우기", "취소")) return;
            for (int i = 0; i < SaveManager.SlotCount; i++) SaveManager.DeleteSlot(i);
            Debug.Log("[저장] 3칸 전부 지움: " + Application.persistentDataPath);
        }

        // UI_Blue 등 Sprite Editor로 자른 조각을 이름으로 찾아 넣기
        private static int SetSprite(SerializedObject so, string field, string spriteName)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p == null) return 0;
            if (p.objectReferenceValue != null) return 1;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" }))
            {
                foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)))
                {
                    if (o is Sprite s && s.name == spriteName)
                    {
                        p.objectReferenceValue = s;
                        return 1;
                    }
                }
            }
            return 0;
        }
    }
}
#endif