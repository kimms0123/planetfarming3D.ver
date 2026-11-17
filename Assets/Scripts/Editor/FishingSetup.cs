#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FarmingSystem.Economy;
using FarmingSystem.Fishing;
using FarmingSystem.Inventory;
using FarmingSystem.Save;

namespace FarmingSystem.EditorTools
{
    // 낚시를 게임에 넣어 주는 도구 (에디터 전용)
    // 메뉴: Tools → 행성농사일지 → 물고기 → 낚시 준비하기
    //  1. 물고기 모델(black_fish): 헤엄 애니메이션이 저절로 돌게 Legacy 애니메이션(반복)으로, 텍스처(black_fish.png)로 머티리얼을 만들어 연결
    //  2. 씬에 FishingManager를 만들고 모델 연결
    //  3. 낚싯대 도구(Tool_FishingRod)를 만들고 상점 '도구' 탭에 올림
    //  4. 저장 목록 다시 채우기
    // 물고기 데이터는 따로: 물고기 → 시트에서 가져오기 (물고기데이터.xlsx)
    public static class FishingSetup
    {
        private const string ToolFolder = "Assets/Data/Tools";
        private const int RodPrice = 400, RodDurability = 40;

        [MenuItem("Tools/행성농사일지/물고기/낚시 준비하기")]
        private static void Setup()
        {
            string log = "";

            // 1. 모델
            GameObject model = null;
            string modelPath = FindAssetPath("black_fish", "t:Model");
            if (modelPath != null)
            {
                var mi = AssetImporter.GetAtPath(modelPath) as ModelImporter;
                if (mi != null)
                {
                    bool changed = false;
                    if (mi.animationType != ModelImporterAnimationType.Legacy) { mi.animationType = ModelImporterAnimationType.Legacy; changed = true; }
                    ModelImporterClipAnimation[] clips = mi.clipAnimations.Length > 0 ? mi.clipAnimations : mi.defaultClipAnimations;
                    foreach (ModelImporterClipAnimation c in clips) { c.loopTime = true; c.wrapMode = WrapMode.Loop; }
                    if (clips.Length > 0) { mi.clipAnimations = clips; changed = true; }

                    // 텍스처로 머티리얼 만들어 연결 (모델 안의 머티리얼 이름 → 새 머티리얼)
                    string texPath = FindAssetPath("black_fish", "t:Texture2D");
                    if (texPath != null)
                    {
                        string matPath = Path.Combine(Path.GetDirectoryName(modelPath), "M_black_fish.mat").Replace('\\', '/');
                        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                        if (mat == null)
                        {
                            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                            mat = new Material(sh);
                            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                            mat.mainTexture = tex;
                            AssetDatabase.CreateAsset(mat, matPath);
                        }
                        foreach (Material inner in AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Material>())
                            mi.AddRemap(new AssetImporter.SourceAssetIdentifier(inner), mat);
                        changed = true;
                        log += "· 물고기 머티리얼 M_black_fish 연결\n";
                    }
                    else log += "※ black_fish.png를 못 찾았어요 (모델 색이 안 나올 수 있음)\n";
                    if (changed) mi.SaveAndReimport();
                    log += "· 물고기 모델: 헤엄 애니메이션 반복 설정\n";
                }
                model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            }
            else log += "※ black_fish 모델을 못 찾았어요 → 프로젝트에 넣은 뒤 다시 (없으면 검은 타원으로 나옴)\n";

            // 2. FishingManager
            FishingManager fm = Object.FindAnyObjectByType<FishingManager>(FindObjectsInactive.Include);
            if (fm == null)
            {
                fm = new GameObject("FishingManager").AddComponent<FishingManager>();
                Undo.RegisterCreatedObjectUndo(fm.gameObject, "낚시 준비");
                log += "· 씬에 FishingManager를 만들었어요\n";
            }
            if (model != null)
            {
                var so = new SerializedObject(fm);
                so.FindProperty("fishModel").objectReferenceValue = model;
                so.ApplyModifiedProperties();
            }
            EditorSceneManager.MarkSceneDirty(fm.gameObject.scene);

            // 3. 낚싯대
            EnsureFolder(ToolFolder);
            string rodPath = $"{ToolFolder}/Tool_FishingRod.asset";
            ToolData rod = AssetDatabase.LoadAssetAtPath<ToolData>(rodPath);
            if (rod == null)
            {
                rod = ScriptableObject.CreateInstance<ToolData>();
                rod.itemName = "낚싯대";
                rod.description = "물가를 바라보고 던지면 찌가 날아간다. 찌가 쑥 들어갈 때 당기자.";
                rod.category = ItemCategory.Tool;
                rod.maxStack = 1;
                rod.toolType = ToolType.FishingRod;
                rod.range = ToolRange.Single;
                rod.maxDurability = RodDurability;
                AssetDatabase.CreateAsset(rod, rodPath);
                log += $"· 낚싯대 도구 (내구도 {RodDurability})\n";
            }
            AssetDatabase.SaveAssets();

            ShopManager shop = Object.FindAnyObjectByType<ShopManager>(FindObjectsInactive.Include);
            if (shop != null)
            {
                var so = new SerializedObject(shop);
                SerializedProperty list = so.FindProperty("buyCatalog");
                bool has = false;
                for (int i = 0; i < list.arraySize && !has; i++) has = list.GetArrayElementAtIndex(i).FindPropertyRelative("item").objectReferenceValue == rod;
                if (!has)
                {
                    int idx = list.arraySize;
                    list.InsertArrayElementAtIndex(idx);
                    SerializedProperty e = list.GetArrayElementAtIndex(idx);
                    e.FindPropertyRelative("item").objectReferenceValue = rod;
                    e.FindPropertyRelative("price").intValue = RodPrice;
                    e.FindPropertyRelative("stock").intValue = -1;
                    log += $"· 상점 '도구' 탭에 낚싯대 추가 ({RodPrice}벨)\n";
                }
                so.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(shop.gameObject.scene);
            }
            else log += "※ 씬에 ShopManager가 없어서 상점에는 못 올렸어요\n";

            SaveManager save = Object.FindAnyObjectByType<SaveManager>(FindObjectsInactive.Include);
            if (save != null) { save.FillDatabases(); EditorSceneManager.MarkSceneDirty(save.gameObject.scene); }

            Selection.activeObject = fm.gameObject;
            EditorUtility.DisplayDialog("낚시 준비", log + "\n물고기 데이터: 물고기 → 시트에서 가져오기\nCtrl+S로 씬 저장", "확인");
        }

        private static string FindAssetPath(string name, string filter)
        {
            foreach (string g in AssetDatabase.FindAssets(name + " " + filter, new[] { "Assets" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileNameWithoutExtension(p) == name) return p;
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
