#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FarmingSystem.Economy;
using FarmingSystem.UI.Shop;

namespace FarmingSystem.EditorTools
{
    // 상인 AI(FCM + RBFN)를 판매 창에 연결하는 도구 (에디터 전용)
    // 메뉴: Tools → 행성농사일지 → 상인 AI → 설정하기
    //  1. Assets/Data/Economy/FcmRbfnBargain.asset 만들기 (있으면 그대로)
    //  2. 씬의 ShopSellPanelUI → Bargain Model 칸에 넣기
    // 되돌리려면 Bargain Model 칸을 비우거나 RuleBasedBargain 에셋을 넣으면 됨
    public static class MerchantAISetup
    {
        private const string Folder = "Assets/Data/Economy";
        private const string AssetPath = Folder + "/FcmRbfnBargain.asset";

        [MenuItem("Tools/행성농사일지/상인 AI/설정하기")]
        private static void Setup()
        {
            EnsureFolder(Folder);
            var model = AssetDatabase.LoadAssetAtPath<FcmRbfnBargainModel>(AssetPath);
            string log = "";
            if (model == null)
            {
                model = ScriptableObject.CreateInstance<FcmRbfnBargainModel>();
                AssetDatabase.CreateAsset(model, AssetPath);
                AssetDatabase.SaveAssets();
                log += "· FcmRbfnBargain 에셋을 만들었어요 (" + AssetPath + ")\n";
            }

            ShopSellPanelUI panel = Object.FindAnyObjectByType<ShopSellPanelUI>(FindObjectsInactive.Include);
            if (panel == null)
            {
                EditorUtility.DisplayDialog("상인 AI", log + "※ 씬에 ShopSellPanelUI가 없어요. 게임 씬을 열고 다시 해 주세요.", "확인");
                return;
            }
            var so = new SerializedObject(panel);
            so.FindProperty("bargainModel").objectReferenceValue = model;
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
            log += "· 판매 창 Bargain Model 칸에 연결했어요\n";

            Selection.activeObject = model;
            EditorUtility.DisplayDialog("상인 AI", log +
                "\n게임 중 F9 = 상인 AI 들여다보기 창\n성향 대사는 판매 3번 이후부터 나와요\nCtrl+S로 씬 저장", "확인");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
#endif
