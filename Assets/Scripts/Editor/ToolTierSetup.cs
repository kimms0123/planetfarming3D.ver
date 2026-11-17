#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FarmingSystem.Economy;
using FarmingSystem.Inventory;
using FarmingSystem.Save;

namespace FarmingSystem.EditorTools
{
    // 상점 '도구' 탭에 파는 등급 도구 6종을 만들고 구매 목록에 올려 주는 도구 (에디터 전용)
    // 메뉴: Tools → 행성농사일지 → 도구 → 등급 도구 만들기
    //  - Assets/Data/Tools 에 호미 2종·물뿌리개 2종·낫 2종 (이미 있으면 그대로 둠)
    //  - 씬의 ShopManager 구매 목록(Buy Catalog)에 없으면 추가, SaveManager 아이템 목록 다시 채움
    //  - 처음부터 갖고 있는 기본 호미·물뿌리개는 건드리지 않음 (1칸, 닳지 않음)
    // 아이콘은 비어 있음 → 각 에셋의 Icon 칸에 넣기. 가격·범위·내구도는 에셋/Buy Catalog에서 바꾸면 됨
    public static class ToolTierSetup
    {
        private const string Folder = "Assets/Data/Tools";

        // 밸런스 기준 (판매가 8~120벨 × 수확량 8개, 하루에 밭 20~40칸을 돌본다고 봄)
        //  - 2단계(앞으로 3칸): 초반 며칠 벌면 살 수 있는 값. 내구도 = 한 계절(27일) 중 2~3주쯤 쓰는 양
        //  - 3단계(3x3): 중반 이후 목표. 값은 4배쯤, 한 번에 9칸이라 같은 횟수로 3배 넓게 + 내구도도 2.5배
        //  - 낫은 리듬게임 횟수를 줄여 주는 만큼 비싸고 내구도는 적게
        private static readonly (string file, string name, string desc, ToolType type, ToolRange range, int durability, int price)[] Tools =
        {
            ("Tool_Hoe_2", "튼튼한 호미", "바라보는 쪽으로 3칸을 한 번에 간다.", ToolType.Hoe, ToolRange.Line3, 120, 500),
            ("Tool_Hoe_3", "별빛 호미", "주변 9칸을 한 번에 간다.", ToolType.Hoe, ToolRange.Square3, 300, 2000),
            ("Tool_WateringCan_2", "넓은 물뿌리개", "바라보는 쪽으로 3칸에 한 번에 물을 준다.", ToolType.WateringCan, ToolRange.Line3, 200, 600),
            ("Tool_WateringCan_3", "소나기 물뿌리개", "주변 9칸에 한 번에 물을 준다.", ToolType.WateringCan, ToolRange.Square3, 500, 2400),
            ("Tool_Sickle_1", "낫", "들고 수확하면 앞쪽 3칸의 같은 작물을 한 번에 거둔다.", ToolType.Sickle, ToolRange.Line3, 60, 800),
            ("Tool_Sickle_2", "큰 낫", "들고 수확하면 주변 9칸의 같은 작물을 한 번에 거둔다.", ToolType.Sickle, ToolRange.Square3, 150, 2800),
        };

        [MenuItem("Tools/행성농사일지/도구/등급 도구 만들기")]
        private static void Create()
        {
            EnsureFolder(Folder);
            ShopManager shop = Object.FindAnyObjectByType<ShopManager>(FindObjectsInactive.Include);
            SerializedObject so = shop != null ? new SerializedObject(shop) : null;
            SerializedProperty list = so?.FindProperty("buyCatalog");
            int made = 0, added = 0;

            foreach (var t in Tools)
            {
                string path = $"{Folder}/{t.file}.asset";
                ToolData tool = AssetDatabase.LoadAssetAtPath<ToolData>(path);
                if (tool == null)
                {
                    tool = ScriptableObject.CreateInstance<ToolData>();
                    tool.itemName = t.name;
                    tool.description = t.desc;
                    tool.category = ItemCategory.Tool;
                    tool.maxStack = 1;
                    tool.toolType = t.type;
                    tool.range = t.range;
                    tool.maxDurability = t.durability;
                    AssetDatabase.CreateAsset(tool, path);
                    made++;
                }
                if (list == null) continue;
                bool has = false;
                for (int i = 0; i < list.arraySize && !has; i++)
                    has = list.GetArrayElementAtIndex(i).FindPropertyRelative("item").objectReferenceValue == tool;
                if (has) continue;
                int idx = list.arraySize;
                list.InsertArrayElementAtIndex(idx);
                SerializedProperty e = list.GetArrayElementAtIndex(idx);
                e.FindPropertyRelative("item").objectReferenceValue = tool;
                e.FindPropertyRelative("price").intValue = t.price;
                e.FindPropertyRelative("stock").intValue = -1;
                added++;
            }
            AssetDatabase.SaveAssets();
            if (so != null) { so.ApplyModifiedProperties(); EditorSceneManager.MarkSceneDirty(shop.gameObject.scene); }

            SaveManager save = Object.FindAnyObjectByType<SaveManager>(FindObjectsInactive.Include);
            if (save != null) { save.FillDatabases(); EditorSceneManager.MarkSceneDirty(save.gameObject.scene); }

            EditorUtility.DisplayDialog("등급 도구",
                $"도구 에셋 {made}개 새로 만듦 ({Folder})\n" +
                (shop != null ? $"상점 구매 목록에 {added}개 추가" : "씬에 ShopManager가 없어서 상점에는 못 올렸어요 (게임 씬을 열고 다시)") +
                "\n\n아이콘은 각 에셋의 Icon 칸에 넣어 주세요.\nCtrl+S로 씬 저장", "확인");
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
