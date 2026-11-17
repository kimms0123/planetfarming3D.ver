#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FarmingSystem.Economy;
using FarmingSystem.Farming;
using FarmingSystem.Save;

namespace FarmingSystem.EditorTools
{
    // 기본 비료 3종을 만들고 상점 '비료' 탭에 올려 주는 도구 (에디터 전용)
    // 메뉴: Tools → 행성농사일지 → 비료 → 기본 비료 3종 만들기
    //  - Assets/Data/Fertilizers 에 성장·품질·수확량 비료 에셋을 만듦 (이미 있으면 그대로 둠)
    //  - 씬의 ShopManager 구매 목록(Buy Catalog)에 없으면 추가 (가격은 Inspector에서 바꾸면 됨)
    //  - SaveManager 아이템 목록을 다시 채움 (저장·불러오기가 비료를 찾을 수 있게)
    // 아이콘은 비어 있음 → 각 비료 에셋의 Icon 칸에 그림을 넣어 주세요
    public static class FertilizerSetup
    {
        private const string Folder = "Assets/Data/Fertilizers";

        [MenuItem("Tools/행성농사일지/비료/기본 비료 3종 만들기")]
        private static void Create()
        {
            EnsureFolder(Folder);
            FertilizerData growth = Make("Fertilizer_Growth", "쑥쑥 비료", "뿌린 밭의 작물이 조금 더 빨리 자란다.", FertilizerType.Growth, new Color(0.5f, 0.85f, 0.4f));
            FertilizerData quality = Make("Fertilizer_Quality", "반짝 비료", "뿌린 밭의 작물은 수확 등급이 한 단계 오른다.", FertilizerType.Quality, new Color(1f, 0.82f, 0.3f));
            FertilizerData yield = Make("Fertilizer_Yield", "듬뿍 비료", "뿌린 밭의 작물은 수확량이 조금 늘어난다.", FertilizerType.Yield, new Color(0.9f, 0.55f, 0.35f));
            AssetDatabase.SaveAssets();

            string shopMsg = "씬에 ShopManager가 없어서 상점에는 못 올렸어요 (게임 씬을 열고 다시)";
            ShopManager shop = Object.FindAnyObjectByType<ShopManager>(FindObjectsInactive.Include);
            if (shop != null)
            {
                var so = new SerializedObject(shop);
                SerializedProperty list = so.FindProperty("buyCatalog");
                int added = 0;
                added += AddToShop(list, growth, 30);
                added += AddToShop(list, yield, 40);
                added += AddToShop(list, quality, 60);
                so.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(shop.gameObject.scene);
                shopMsg = $"상점 구매 목록에 {added}개 추가 (이미 있던 건 그대로)";
            }

            SaveManager save = Object.FindAnyObjectByType<SaveManager>(FindObjectsInactive.Include);
            if (save != null) { save.FillDatabases(); EditorSceneManager.MarkSceneDirty(save.gameObject.scene); }

            Selection.activeObject = growth;
            EditorUtility.DisplayDialog("비료", $"비료 3종: {Folder}\n{shopMsg}\n\n아이콘은 각 에셋의 Icon 칸에 넣어 주세요.\nCtrl+S로 씬 저장", "확인");
        }

        private static FertilizerData Make(string file, string name, string desc, FertilizerType type, Color color)
        {
            string path = $"{Folder}/{file}.asset";
            FertilizerData f = AssetDatabase.LoadAssetAtPath<FertilizerData>(path);
            if (f != null) return f;
            f = ScriptableObject.CreateInstance<FertilizerData>();
            f.itemName = name;
            f.description = desc;
            f.category = FarmingSystem.Inventory.ItemCategory.Fertilizer;
            f.type = type;
            f.pelletColor = color;
            AssetDatabase.CreateAsset(f, path);
            return f;
        }

        private static int AddToShop(SerializedProperty list, FertilizerData item, int price)
        {
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).FindPropertyRelative("item").objectReferenceValue == item) return 0;
            int idx = list.arraySize;
            list.InsertArrayElementAtIndex(idx);
            SerializedProperty e = list.GetArrayElementAtIndex(idx);
            e.FindPropertyRelative("item").objectReferenceValue = item;
            e.FindPropertyRelative("price").intValue = price;
            e.FindPropertyRelative("stock").intValue = -1;
            return 1;
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
