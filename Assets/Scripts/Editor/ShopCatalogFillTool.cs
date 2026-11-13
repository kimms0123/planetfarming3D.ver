#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FarmingSystem.Inventory;
using FarmingSystem.Economy;

namespace FarmingSystem.EditorTools
{
    // 프로젝트에 있는 씨앗(SeedData)을 전부 상점 구매 목록(ShopManager → Buy Catalog)에 넣기
    // 메뉴: Tools → 행성농사일지 → 상점 → 씨앗 전부 구매 목록에 넣기
    //  - 이미 목록에 있는 씨앗은 그대로 둠 (가격·재고 안 바뀜)
    //  - 새로 넣는 씨앗 가격 = 그 씨앗이 키우는 작물의 기본 판매가(basePrice) x 비율
    //    비율은 이미 넣어 둔 씨앗(예: 감자 씨앗 40벨)의 "가격 ÷ 작물 기본가"를 따라감 → 지금 정한 가격 감각 그대로
    //    (기준이 될 씨앗이 없으면 작물 기본가의 0.5배)
    //  - 재고 = -1 (무제한), 새로 넣은 건 가격 낮은 순으로 뒤에 붙음
    //  - Ctrl+Z로 되돌릴 수 있음
    public static class ShopCatalogFillTool
    {
        private const float DefaultRatio = 0.5f;

        [MenuItem("Tools/행성농사일지/상점/씨앗 전부 구매 목록에 넣기")]
        private static void Fill()
        {
            ShopManager shop = Object.FindAnyObjectByType<ShopManager>(FindObjectsInactive.Include);
            if (shop == null) { EditorUtility.DisplayDialog("구매 목록", "씬에서 ShopManager를 못 찾았어요.", "확인"); return; }

            // 프로젝트의 씨앗 전부 (SeedData와 그 자식 타입)
            List<ItemData> seeds = AssetDatabase.FindAssets("t:ItemData", new[] { "Assets" })
                .Select(g => AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(i => i != null && IsSeed(i))
                .Distinct()
                .ToList();
            if (seeds.Count == 0) { EditorUtility.DisplayDialog("구매 목록", "프로젝트에서 씨앗(SeedData)을 하나도 못 찾았어요.", "확인"); return; }

            var so = new SerializedObject(shop);
            SerializedProperty list = so.FindProperty("buyCatalog");

            // 이미 있는 것 + 가격 비율 기준 찾기
            var existing = new HashSet<ItemData>();
            var ratios = new List<float>();
            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty e = list.GetArrayElementAtIndex(i);
                ItemData item = e.FindPropertyRelative("item").objectReferenceValue as ItemData;
                if (item == null) continue;
                existing.Add(item);
                float bp = CropBasePrice(item);
                int price = e.FindPropertyRelative("price").intValue;
                if (bp > 0f && price > 0) ratios.Add(price / bp);
            }
            float ratio = ratios.Count > 0 ? ratios.Average() : DefaultRatio;

            List<(ItemData item, int price)> toAdd = seeds.Where(s => !existing.Contains(s))
                .Select(s => (s, PriceFor(s, ratio)))
                .OrderBy(x => x.Item2).ThenBy(x => x.s.itemName)
                .ToList();
            if (toAdd.Count == 0)
            {
                EditorUtility.DisplayDialog("구매 목록", $"씨앗 {seeds.Count}개가 이미 전부 구매 목록에 있어요.", "확인");
                return;
            }

            var preview = new StringBuilder();
            foreach (var x in toAdd.Take(15)) preview.AppendLine($"  {x.item.itemName}  {x.price}벨");
            if (toAdd.Count > 15) preview.AppendLine($"  … 외 {toAdd.Count - 15}개");
            string basis = ratios.Count > 0 ? $"이미 넣은 씨앗 가격 기준 (작물 기본가 x {ratio:0.##})" : $"작물 기본가 x {DefaultRatio}";
            if (!EditorUtility.DisplayDialog("구매 목록",
                    $"새 씨앗 {toAdd.Count}개를 구매 목록에 넣을까요?\n가격: {basis}, 재고 무제한\n(이미 있는 {existing.Count}개는 그대로)\n\n{preview}",
                    "넣기", "취소"))
                return;

            Undo.RecordObject(shop, "씨앗 구매 목록 채우기");
            foreach (var x in toAdd)
            {
                int idx = list.arraySize;
                list.InsertArrayElementAtIndex(idx);
                SerializedProperty e = list.GetArrayElementAtIndex(idx);
                e.FindPropertyRelative("item").objectReferenceValue = x.item;
                e.FindPropertyRelative("price").intValue = x.price;
                e.FindPropertyRelative("stock").intValue = -1;
            }
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(shop.gameObject.scene);
            Selection.activeGameObject = shop.gameObject;
            Debug.Log($"[구매 목록] 씨앗 {toAdd.Count}개 추가\n{preview}");
            EditorUtility.DisplayDialog("구매 목록", $"씨앗 {toAdd.Count}개를 넣었어요. 가격은 Inspector에서 하나씩 고칠 수 있어요.\n\nCtrl+S로 씬 저장", "확인");
        }

        private static bool IsSeed(ItemData item)
        {
            for (System.Type t = item.GetType(); t != null && t != typeof(ItemData); t = t.BaseType)
                if (t.Name == "SeedData") return true;
            return false;
        }

        // 씨앗이 키우는 작물(resultCrop)의 기본 판매가 (없으면 0)
        private static float CropBasePrice(ItemData seed)
        {
            SerializedProperty crop = new SerializedObject(seed).FindProperty("resultCrop");
            if (crop == null || crop.objectReferenceValue == null) return 0f;
            SerializedProperty bp = new SerializedObject(crop.objectReferenceValue).FindProperty("basePrice");
            if (bp == null) return 0f;
            return bp.propertyType == SerializedPropertyType.Integer ? bp.intValue : bp.floatValue;
        }

        private static int PriceFor(ItemData seed, float ratio)
        {
            float bp = CropBasePrice(seed);
            return bp > 0f ? Mathf.Max(1, Mathf.RoundToInt(bp * ratio)) : 10;
        }
    }
}
#endif