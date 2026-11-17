#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FarmingSystem.Building;
using FarmingSystem.Economy;
using FarmingSystem.Inventory;
using FarmingSystem.NPC;
using FarmingSystem.Save;

namespace FarmingSystem.EditorTools
{
    // 주민 집을 키트 하나로 통일해 주는 도구 (에디터 전용)
    // 메뉴: Tools → 행성농사일지 → 설치물 → 주민 집 키트 만들기
    //  1. 건물 데이터 'Building_resident_house' (3x3, 공사 1일, 기본 모델 = mushroom_house)
    //  2. 키트 아이템 'Kit_resident_house' (아이콘 = 공구함 Icon_BuildKit)
    //  3. 상점 '도구' 탭에 올림
    //  4. ResidentManager: House Buildings에 이 건물을 넣고, Resident Houses에 집 모델 4개(bird·bunny·mushroom·shark)를 넣음
    //     → 주민 NPC 이름에 bird·bunny·mushroom·shark가 들어 있으면 자동으로 짝지음. 안 되면 Inspector에서 NPC 칸만 채우기
    // 이미 있는 에셋은 그대로 둠 (여러 번 눌러도 됨)
    public static class ResidentHouseSetup
    {
        private const string DataFolder = "Assets/Buildings/Data", KitFolder = "Assets/Buildings/Kits";
        private const int KitPrice = 1200;
        private static readonly string[] Houses = { "bird_house", "bunny_house", "mushroom_house", "shark_house" };

        [MenuItem("Tools/행성농사일지/설치물/주민 집 키트 만들기")]
        private static void Create()
        {
            EnsureFolder(DataFolder);
            EnsureFolder(KitFolder);
            var prefabs = new Dictionary<string, GameObject>();
            foreach (string h in Houses) { GameObject p = FindPrefab(h); if (p != null) prefabs[h] = p; }
            Sprite kitIcon = FindSprite("Icon_BuildKit");

            // 1. 건물 데이터
            string dPath = $"{DataFolder}/Building_resident_house.asset";
            BuildingData data = AssetDatabase.LoadAssetAtPath<BuildingData>(dPath);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<BuildingData>();
                data.displayName = "주민 집";
                data.description = "새 주민이 이사 와서 살 집. 이사 온 주민에 맞게 모습이 바뀐다.";
                data.prefab = prefabs.TryGetValue("mushroom_house", out GameObject def) ? def : null;
                data.footprint = new Vector2Int(3, 3);
                data.fill = 0.9f;
                data.buildDays = 1;
                data.allowRotate = true;
                data.function = BuildingFunction.None;
                AssetDatabase.CreateAsset(data, dPath);
            }
            if (data.icon == null && kitIcon != null) { data.icon = kitIcon; EditorUtility.SetDirty(data); }

            // 2. 키트
            string kPath = $"{KitFolder}/Kit_resident_house.asset";
            BuildingKitData kit = AssetDatabase.LoadAssetAtPath<BuildingKitData>(kPath);
            if (kit == null)
            {
                kit = ScriptableObject.CreateInstance<BuildingKitData>();
                kit.itemName = "주민 집 키트";
                kit.description = "지으면 다음 날 아침 완성되고, 새 주민이 이사 온다.";
                kit.maxStack = 5;
                kit.building = data;
                AssetDatabase.CreateAsset(kit, kPath);
            }
            if (kit.icon == null && kitIcon != null) { kit.icon = kitIcon; EditorUtility.SetDirty(kit); }
            AssetDatabase.SaveAssets();

            // 3. 상점
            string shopMsg = "씬에 ShopManager가 없어서 상점에는 못 올렸어요";
            ShopManager shop = Object.FindAnyObjectByType<ShopManager>(FindObjectsInactive.Include);
            if (shop != null)
            {
                var so = new SerializedObject(shop);
                SerializedProperty list = so.FindProperty("buyCatalog");
                bool has = false;
                for (int i = 0; i < list.arraySize && !has; i++)
                    has = list.GetArrayElementAtIndex(i).FindPropertyRelative("item").objectReferenceValue == kit;
                if (!has)
                {
                    int idx = list.arraySize;
                    list.InsertArrayElementAtIndex(idx);
                    SerializedProperty e = list.GetArrayElementAtIndex(idx);
                    e.FindPropertyRelative("item").objectReferenceValue = kit;
                    e.FindPropertyRelative("price").intValue = KitPrice;
                    e.FindPropertyRelative("stock").intValue = -1;
                }
                so.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(shop.gameObject.scene);
                shopMsg = has ? "상점에는 이미 있어요" : $"상점 '도구' 탭에 추가 ({KitPrice}벨)";
            }

            // 4. ResidentManager
            string resMsg = "씬에 ResidentManager가 없어요";
            int matched = 0;
            ResidentManager rm = Object.FindAnyObjectByType<ResidentManager>(FindObjectsInactive.Include);
            if (rm != null)
            {
                var so = new SerializedObject(rm);
                SerializedProperty hb = so.FindProperty("houseBuildings");
                bool inList = false;
                for (int i = 0; i < hb.arraySize && !inList; i++) inList = hb.GetArrayElementAtIndex(i).objectReferenceValue == data;
                if (!inList) { hb.InsertArrayElementAtIndex(hb.arraySize); hb.GetArrayElementAtIndex(hb.arraySize - 1).objectReferenceValue = data; }

                SerializedProperty rh = so.FindProperty("residentHouses");
                NPCDialogue[] npcs = Object.FindObjectsByType<NPCDialogue>(FindObjectsInactive.Include);
                foreach (string h in Houses)
                {
                    if (!prefabs.TryGetValue(h, out GameObject prefab)) continue;
                    int row = -1;
                    for (int i = 0; i < rh.arraySize && row < 0; i++)
                        if (rh.GetArrayElementAtIndex(i).FindPropertyRelative("housePrefab").objectReferenceValue == prefab) row = i;
                    if (row < 0) { row = rh.arraySize; rh.InsertArrayElementAtIndex(row); rh.GetArrayElementAtIndex(row).FindPropertyRelative("npc").objectReferenceValue = null; }
                    SerializedProperty r = rh.GetArrayElementAtIndex(row);
                    r.FindPropertyRelative("housePrefab").objectReferenceValue = prefab;
                    SerializedProperty npcProp = r.FindPropertyRelative("npc");
                    if (npcProp.objectReferenceValue == null)
                    {
                        string key = h.Replace("_house", "");
                        foreach (NPCDialogue n in npcs)
                            if (n.name.ToLowerInvariant().Contains(key)) { npcProp.objectReferenceValue = n; break; }
                    }
                    if (npcProp.objectReferenceValue != null) matched++;
                }
                so.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(rm.gameObject.scene);
                resMsg = $"ResidentManager에 주민 집 등록, 집 모델 {prefabs.Count}개 중 {matched}개는 주민과 자동으로 짝지음";
            }

            SaveManager save = Object.FindAnyObjectByType<SaveManager>(FindObjectsInactive.Include);
            if (save != null) { save.FillDatabases(); EditorSceneManager.MarkSceneDirty(save.gameObject.scene); }

            string warn = prefabs.Count < Houses.Length ? $"\n※ 집 프리팹을 {prefabs.Count}/4개만 찾았어요 (npc_house 패키지를 먼저 넣어 주세요)" : "";
            if (kitIcon == null) warn += "\n※ Icon_BuildKit 아이콘을 못 찾았어요 → 키트의 Icon 칸에 직접 넣어 주세요";
            if (rm != null && matched < prefabs.Count) warn += "\n※ 짝을 못 찾은 집은 ResidentManager → Resident Houses에서 NPC 칸을 채워 주세요";
            Selection.activeObject = rm != null ? (Object)rm.gameObject : kit;
            EditorUtility.DisplayDialog("주민 집", $"{shopMsg}\n{resMsg}{warn}\n\nCtrl+S로 씬 저장", "확인");
        }

        private static GameObject FindPrefab(string name)
        {
            foreach (string g in AssetDatabase.FindAssets(name + " t:Prefab", new[] { "Assets" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileNameWithoutExtension(p) == name) return AssetDatabase.LoadAssetAtPath<GameObject>(p);
            }
            return null;
        }

        private static Sprite FindSprite(string name)
        {
            foreach (string g in AssetDatabase.FindAssets(name + " t:Sprite", new[] { "Assets" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileNameWithoutExtension(p) == name) return AssetDatabase.LoadAssetAtPath<Sprite>(p);
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
