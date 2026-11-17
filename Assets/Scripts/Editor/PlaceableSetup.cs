#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FarmingSystem.Building;
using FarmingSystem.Economy;
using FarmingSystem.Inventory;
using FarmingSystem.Save;

namespace FarmingSystem.EditorTools
{
    // 보관 상자·판매 포탈을 게임에 넣어 주는 도구 (에디터 전용)
    // 메뉴: Tools → 행성농사일지 → 설치물 → 상자·포탈 만들기
    //  1. 아이콘(Icon_*.png)을 픽셀 그림용 설정으로 (Sprite, Point, 압축 없음)
    //  2. 건물 데이터 2개 (1칸, 바로 완성, 기능 = 보관 상자 / 판매 포탈) - 모델은 프로젝트의 box·portal 프리팹
    //  3. 키트 아이템 2개 (가방에 넣고 핫바에서 골라 좌클릭 → 설치)
    //  4. 상점 '도구' 탭에 올림 + 저장 목록 다시 채우기
    //  5. 아이콘이 비어 있는 기존 건물 키트에 공구함 아이콘(Icon_BuildKit) 넣기
    // 이미 있는 에셋은 그대로 둠 (여러 번 눌러도 됨). 가격·이름은 만든 뒤 Inspector에서 바꾸면 됨
    public static class PlaceableSetup
    {
        private const string Folder = "Assets/Data/Placeables";
        private const int BoxPrice = 150, PortalPrice = 600;

        [MenuItem("Tools/행성농사일지/설치물/상자·포탈 만들기")]
        private static void Create()
        {
            EnsureFolder(Folder);
            Sprite boxIcon = FindIcon("Icon_Box"), portalIcon = FindIcon("Icon_Portal"), kitIcon = FindIcon("Icon_BuildKit");
            FindIcon("Icon_AxePickaxe");
            FindIcon("Icon_Glove");

            GameObject boxPrefab = FindPrefab("box"), portalPrefab = FindPrefab("portal");
            BuildingData boxData = MakeBuilding("Building_storage_box", "보관 상자", "물건을 넣어 둘 수 있는 상자.", boxPrefab, boxIcon, BuildingFunction.Storage);
            BuildingData portalData = MakeBuilding("Building_sell_portal", "판매 포탈", "작물과 씨앗을 넣어 두면 다음 날 아침에 팔린다.", portalPrefab, portalIcon, BuildingFunction.SellPortal);
            BuildingKitData boxKit = MakeKit("Kit_storage_box", "보관 상자", "설치하면 물건을 30칸까지 넣어 둘 수 있다.", boxIcon, boxData);
            BuildingKitData portalKit = MakeKit("Kit_sell_portal", "판매 포탈", "작물·씨앗을 넣어 두면 상인이 없어도 다음 날 아침에 팔린다.", portalIcon, portalData);

            // 기존 건물 키트 아이콘
            int kitIcons = 0;
            if (kitIcon != null)
                foreach (string g in AssetDatabase.FindAssets("t:BuildingKitData", new[] { "Assets" }))
                {
                    var k = AssetDatabase.LoadAssetAtPath<BuildingKitData>(AssetDatabase.GUIDToAssetPath(g));
                    if (k == null || k == boxKit || k == portalKit || k.icon != null) continue;
                    k.icon = kitIcon;
                    EditorUtility.SetDirty(k);
                    kitIcons++;
                }
            AssetDatabase.SaveAssets();

            string shopMsg = "씬에 ShopManager가 없어서 상점에는 못 올렸어요 (게임 씬을 열고 다시)";
            ShopManager shop = Object.FindAnyObjectByType<ShopManager>(FindObjectsInactive.Include);
            if (shop != null)
            {
                var so = new SerializedObject(shop);
                SerializedProperty list = so.FindProperty("buyCatalog");
                int added = AddToShop(list, boxKit, BoxPrice) + AddToShop(list, portalKit, PortalPrice);
                so.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(shop.gameObject.scene);
                shopMsg = $"상점 '도구' 탭에 {added}개 추가 (보관 상자 {BoxPrice}벨, 판매 포탈 {PortalPrice}벨)";
            }
            SaveManager save = Object.FindAnyObjectByType<SaveManager>(FindObjectsInactive.Include);
            if (save != null) { save.FillDatabases(); EditorSceneManager.MarkSceneDirty(save.gameObject.scene); }

            string warn = "";
            if (boxPrefab == null || portalPrefab == null) warn += "\n※ box / portal 프리팹을 못 찾았어요 → 건물 데이터의 Prefab 칸에 직접 넣어 주세요";
            if (boxIcon == null || portalIcon == null || kitIcon == null) warn += "\n※ 아이콘(Icon_Box·Icon_Portal·Icon_BuildKit)을 못 찾았어요 → 먼저 프로젝트에 넣어 주세요";
            Selection.activeObject = boxData;
            EditorUtility.DisplayDialog("설치물",
                $"건물 데이터·키트: {Folder}\n{shopMsg}\n기존 건물 키트 {kitIcons}개에 공구함 아이콘을 넣었어요{warn}\n\nCtrl+S로 씬 저장", "확인");
        }

        private static BuildingData MakeBuilding(string file, string name, string desc, GameObject prefab, Sprite icon, BuildingFunction function)
        {
            string path = $"{Folder}/{file}.asset";
            BuildingData d = AssetDatabase.LoadAssetAtPath<BuildingData>(path);
            if (d != null)
            {
                // 이미 있으면 비어 있는 칸만 채움
                if (d.prefab == null && prefab != null) d.prefab = prefab;
                if (d.icon == null && icon != null) d.icon = icon;
                d.function = function;
                EditorUtility.SetDirty(d);
                return d;
            }
            d = ScriptableObject.CreateInstance<BuildingData>();
            d.displayName = name;
            d.description = desc;
            d.icon = icon;
            d.prefab = prefab;
            d.footprint = new Vector2Int(1, 1);
            d.fill = 0.85f;
            d.buildDays = 0;   // 바로 완성
            d.cost = 0;
            d.allowRotate = true;
            d.function = function;
            AssetDatabase.CreateAsset(d, path);
            return d;
        }

        private static BuildingKitData MakeKit(string file, string name, string desc, Sprite icon, BuildingData building)
        {
            string path = $"{Folder}/{file}.asset";
            BuildingKitData k = AssetDatabase.LoadAssetAtPath<BuildingKitData>(path);
            if (k != null)
            {
                if (k.icon == null && icon != null) { k.icon = icon; EditorUtility.SetDirty(k); }
                return k;
            }
            k = ScriptableObject.CreateInstance<BuildingKitData>();
            k.itemName = name;
            k.description = desc;
            k.icon = icon;
            k.maxStack = 10;
            k.building = building;
            AssetDatabase.CreateAsset(k, path);
            return k;
        }

        private static int AddToShop(SerializedProperty list, ItemData item, int price)
        {
            if (list == null || item == null) return 0;
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

        // 이름이 정확히 같은 프리팹 (box, portal)
        private static GameObject FindPrefab(string name)
        {
            foreach (string g in AssetDatabase.FindAssets(name + " t:Prefab", new[] { "Assets" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileNameWithoutExtension(p) == name) return AssetDatabase.LoadAssetAtPath<GameObject>(p);
            }
            return null;
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
