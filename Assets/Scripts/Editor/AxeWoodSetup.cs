#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FarmingSystem.Economy;
using FarmingSystem.Gathering;
using FarmingSystem.Inventory;
using FarmingSystem.Save;

namespace FarmingSystem.EditorTools
{
    // 나무 베기 준비를 한 번에 해 주는 도구 (에디터 전용)
    // 메뉴: Tools → 행성농사일지 → 나무 베기 → 도끼·목재 만들기
    //  1. Assets/Data/Tools/Tool_Axe.asset (도끼, 아이콘 Icon_AxePickaxe) - 이미 있으면 그대로 둠
    //  2. Assets/Data/Gathering/Material_Wood.asset (목재, 아이콘 Icon_Wood) - 이미 있으면 그대로 둠
    //  3. 씬의 ShopManager 구매 목록에 도끼 추가 (없을 때만)
    //  4. SaveManager 아이템 목록 다시 채움 (저장·불러오기에 필요)
    //  5. 씬에 TreeChopper가 없으면 만들고 목재 연결
    // 게임 씬을 열고 실행 → Ctrl+S
    public static class AxeWoodSetup
    {
        private const string ToolFolder = "Assets/Data/Tools";
        private const string WoodFolder = "Assets/Data/Gathering";
        private const int AxePrice = 400;

        [MenuItem("Tools/행성농사일지/나무 베기/도끼·목재 만들기")]
        private static void Create()
        {
            EnsureFolder(ToolFolder);
            EnsureFolder(WoodFolder);
            string msg = "";

            // 1. 도끼
            string axePath = $"{ToolFolder}/Tool_Axe.asset";
            AxeData axe = AssetDatabase.LoadAssetAtPath<AxeData>(axePath);
            if (axe == null)
            {
                axe = ScriptableObject.CreateInstance<AxeData>();
                axe.itemName = "도끼";
                axe.description = "평평한 땅의 나무를 벨 수 있다. 몇 번 내리치면 쓰러지고 목재를 얻는다. 벤 자리는 밭으로 쓸 수 있다.";
                axe.category = ItemCategory.Tool;
                axe.maxStack = 1;
                axe.toolType = ToolType.None; // 농사 동작과 안 겹치게
                axe.range = ToolRange.Single;
                axe.maxDurability = 0;        // 안 닳음 (닳게 하려면 에셋에서 숫자 넣기)
                axe.hitsPerTree = 3;
                axe.icon = FindIcon("Icon_AxePickaxe");
                AssetDatabase.CreateAsset(axe, axePath);
                msg += $"도끼 만듦: {axePath}\n";
            }
            else msg += "도끼는 이미 있어서 그대로 둠\n";
            if (axe.icon == null) { axe.icon = FindIcon("Icon_AxePickaxe"); EditorUtility.SetDirty(axe); }
            if (axe.toolType != ToolType.None) { axe.toolType = ToolType.None; EditorUtility.SetDirty(axe); msg += "도끼 Tool Type을 None으로 바꿈\n"; }

            // 2. 목재
            string woodPath = $"{WoodFolder}/Material_Wood.asset";
            MaterialData wood = AssetDatabase.LoadAssetAtPath<MaterialData>(woodPath);
            if (wood == null)
            {
                wood = ScriptableObject.CreateInstance<MaterialData>();
                wood.itemName = "목재";
                wood.description = "나무를 베면 얻는 단단한 목재. 상인에게 팔 수 있다.";
                wood.category = ItemCategory.Material;
                wood.maxStack = 99;
                wood.sellPrice = 6;
                wood.icon = FindIcon("Icon_Wood");
                AssetDatabase.CreateAsset(wood, woodPath);
                msg += $"목재 만듦: {woodPath}\n";
            }
            else msg += "목재는 이미 있어서 그대로 둠\n";
            if (wood.icon == null) { wood.icon = FindIcon("Icon_Wood"); EditorUtility.SetDirty(wood); }
            AssetDatabase.SaveAssets();

            // 3. 상점에 도끼
            ShopManager shop = Object.FindAnyObjectByType<ShopManager>(FindObjectsInactive.Include);
            if (shop != null)
            {
                var so = new SerializedObject(shop);
                SerializedProperty list = so.FindProperty("buyCatalog");
                bool has = false;
                for (int i = 0; i < list.arraySize && !has; i++)
                    has = list.GetArrayElementAtIndex(i).FindPropertyRelative("item").objectReferenceValue == axe;
                if (!has)
                {
                    int idx = list.arraySize;
                    list.InsertArrayElementAtIndex(idx);
                    SerializedProperty e = list.GetArrayElementAtIndex(idx);
                    e.FindPropertyRelative("item").objectReferenceValue = axe;
                    e.FindPropertyRelative("price").intValue = AxePrice;
                    e.FindPropertyRelative("stock").intValue = -1;
                    so.ApplyModifiedProperties();
                    EditorSceneManager.MarkSceneDirty(shop.gameObject.scene);
                    msg += $"상점 구매 목록에 도끼 추가 ({AxePrice}벨)\n";
                }
                else msg += "상점에 도끼가 이미 있음\n";
            }
            else msg += "※ 씬에 ShopManager가 없어서 상점에는 못 올림 (게임 씬을 열고 다시)\n";

            // 4. 저장용 아이템 목록
            SaveManager save = Object.FindAnyObjectByType<SaveManager>(FindObjectsInactive.Include);
            if (save != null) { save.FillDatabases(); EditorSceneManager.MarkSceneDirty(save.gameObject.scene); msg += "SaveManager 아이템 목록 다시 채움\n"; }

            // 5. TreeChopper
            TreeChopper chopper = Object.FindAnyObjectByType<TreeChopper>(FindObjectsInactive.Include);
            if (chopper == null)
            {
                var go = new GameObject("TreeChopper");
                chopper = go.AddComponent<TreeChopper>();
                Undo.RegisterCreatedObjectUndo(go, "TreeChopper 만들기");
                msg += "씬에 TreeChopper 오브젝트 만듦\n";
            }
            var cso = new SerializedObject(chopper);
            SerializedProperty wp = cso.FindProperty("wood");
            if (wp.objectReferenceValue == null) { wp.objectReferenceValue = wood; cso.ApplyModifiedProperties(); msg += "TreeChopper에 목재 연결\n"; }
            EditorSceneManager.MarkSceneDirty(chopper.gameObject.scene);

            string warn = "";
            if (axe.icon == null) warn += "\n※ Icon_AxePickaxe 그림을 못 찾음 → 도끼 에셋 Icon 칸에 직접 넣기";
            if (wood.icon == null) warn += "\n※ Icon_Wood 그림을 못 찾음 → Icon_Wood.png를 프로젝트에 넣고 다시 실행 (또는 목재 에셋 Icon 칸에 직접)";

            Selection.activeObject = chopper.gameObject;
            EditorUtility.DisplayDialog("나무 베기", msg + warn + "\n\nCtrl+S로 씬 저장", "확인");
        }

        // 아이콘 그림을 찾아서 픽셀 그림 설정으로 바꾸고 Sprite로 돌려줌
        private static Sprite FindIcon(string name)
        {
            foreach (string g in AssetDatabase.FindAssets(name + " t:Texture2D", new[] { "Assets" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileNameWithoutExtension(p) != name) continue;
                var ti = AssetImporter.GetAtPath(p) as TextureImporter;
                if (ti != null && (ti.textureType != TextureImporterType.Sprite || ti.filterMode != FilterMode.Point || ti.textureCompression != TextureImporterCompression.Uncompressed))
                {
                    ti.textureType = TextureImporterType.Sprite;
                    ti.spriteImportMode = SpriteImportMode.Single;
                    ti.filterMode = FilterMode.Point;
                    ti.textureCompression = TextureImporterCompression.Uncompressed;
                    ti.mipmapEnabled = false;
                    ti.alphaIsTransparency = true;
                    ti.SaveAndReimport();
                }
                return AssetDatabase.LoadAssetAtPath<Sprite>(p);
            }
            return null;
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
