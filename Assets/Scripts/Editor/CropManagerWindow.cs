#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FarmingSystem.Core;
using FarmingSystem.Economy;
using FarmingSystem.Farming;
using FarmingSystem.Inventory;
using FarmingSystem.Save;

namespace FarmingSystem.EditorTools
{
    // 작물 관리 창 (에디터 전용 - 빌드에 안 들어가서 게임 용량·속도에 영향 없음)
    // 메뉴: Tools → 행성농사일지 → 작물 관리
    //  1. 한 표에서 보고 고치기: 작물(CropData) + 짝 씨앗(SeedData) + 상점 씨앗 가격을 한 줄씩
    //     이름·계절·성장일·수확량·판매가·리듬 템포·흔들림·난이도를 바로 수정 (Ctrl+Z 가능)
    //     계절별 걸러보기·이름 검색·정렬
    //  2. 빠진 것 검사: 성장 모델·아이콘·씨앗 연결·리듬 패턴·가격·상점 등록·저장 목록 등록이 빠진 작물은 빨간 ⚠ (마우스를 올리면 이유)
    //     → 게임 중에 작물이 안 보이거나, 저장을 불러올 때 작물이 사라지는 문제를 미리 막음
    //  3. 목록 정리 버튼: 모든 씨앗을 상점 판매 목록에, 모든 작물·씨앗을 저장 목록에, 작물을 '오늘의 특별 상품' 후보에 한 번에 등록
    // 새 작물 모델을 가져올 때는 지금처럼 Tools → 행성농사일지 → 작물 모델 가져오기 를 쓰고, 이 창에서 값만 정리하면 됨
    public class CropManagerWindow : EditorWindow
    {
        private class Row
        {
            public CropData crop;
            public SeedData seed;
            public List<string> problems = new List<string>();
        }

        private enum Filter { 전체, 봄, 여름, 가을, 겨울, 문제있음 }
        private enum Sort { 이름, 계절, 판매가, 성장일 }

        private readonly List<Row> rows = new List<Row>();
        private Filter filter = Filter.전체;
        private Sort sort = Sort.계절;
        private string search = "";
        private Vector2 scroll;
        private ShopManager shop;
        private SaveManager save;
        private GUIStyle warnStyle, headStyle;

        [MenuItem("Tools/행성농사일지/작물 관리")]
        private static void Open()
        {
            CropManagerWindow w = GetWindow<CropManagerWindow>("작물 관리");
            w.minSize = new Vector2(1100f, 400f);
            w.Reload();
        }

        private void OnEnable() => Reload();
        private void OnFocus() => Reload();
        private void OnHierarchyChange() => Reload();

        // ───────────────────────── 데이터 읽기 ─────────────────────────

        private void Reload()
        {
            rows.Clear();
            shop = FindAnyObjectByType<ShopManager>(FindObjectsInactive.Include);
            save = FindAnyObjectByType<SaveManager>(FindObjectsInactive.Include);

            var seeds = Load<SeedData>();
            foreach (CropData c in Load<CropData>())
            {
                var r = new Row { crop = c, seed = seeds.FirstOrDefault(s => s.resultCrop == c) };
                Check(r);
                rows.Add(r);
            }
            Repaint();
        }

        private static List<T> Load<T>() where T : Object =>
            AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { "Assets" })
                .Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(a => a != null).ToList();

        // 빠진 것 검사
        private void Check(Row r)
        {
            r.problems.Clear();
            CropData c = r.crop;
            if (c.growthStagePrefabs == null || c.growthStagePrefabs.Length == 0) r.problems.Add("성장 단계 모델이 없음 (밭에 심어도 안 보임)");
            else if (c.growthStagePrefabs.Any(p => p == null)) r.problems.Add("성장 단계 모델 중 빈 칸이 있음");
            if (c.icon == null) r.problems.Add("작물 아이콘 없음 (가방·상점에 빈칸)");
            if (c.totalGrowthDays <= 0) r.problems.Add("성장일이 0 이하");
            if (c.basePrice <= 0f) r.problems.Add("판매가가 0 이하");
            if (c.expectedYield <= 0) r.problems.Add("수확량이 0 이하");
            if (c.harvestPattern == null || c.harvestPattern.Length == 0) r.problems.Add("리듬 패턴이 비어 있음 (자동 생성만 사용)");
            if (r.seed == null) r.problems.Add("이 작물로 자라는 씨앗(SeedData)이 없음");
            else
            {
                if (r.seed.icon == null) r.problems.Add("씨앗 아이콘 없음");
                if (shop != null && SeedEntryIndex(r.seed) < 0) r.problems.Add("씨앗이 상점 판매 목록에 없음 → '목록 정리'");
            }
            if (save != null && (!InSaveList(c) || (r.seed != null && !InSaveList(r.seed)))) r.problems.Add("저장 목록에 없음 (불러오면 사라짐) → '목록 정리'");
        }

        private int SeedEntryIndex(SeedData seed)
        {
            if (shop == null) return -1;
            var list = shop.BuyCatalog;
            for (int i = 0; i < list.Count; i++) if (list[i] != null && list[i].item == seed) return i;
            return -1;
        }

        private bool InSaveList(ItemData item)
        {
            if (save == null) return true;
            SerializedProperty items = new SerializedObject(save).FindProperty("items");
            if (items == null) return true;
            for (int i = 0; i < items.arraySize; i++) if (items.GetArrayElementAtIndex(i).objectReferenceValue == item) return true;
            return false;
        }

        // ───────────────────────── 화면 ─────────────────────────

        private void OnGUI()
        {
            if (warnStyle == null)
            {
                warnStyle = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(1f, 0.35f, 0.3f) }, alignment = TextAnchor.MiddleCenter };
                headStyle = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            }

            // 위쪽 도구 줄
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                filter = (Filter)EditorGUILayout.EnumPopup(filter, EditorStyles.toolbarPopup, GUILayout.Width(90));
                sort = (Sort)EditorGUILayout.EnumPopup("정렬", sort, EditorStyles.toolbarPopup, GUILayout.Width(170));
                GUILayout.Label("검색", GUILayout.Width(30));
                search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField, GUILayout.Width(180));
                GUILayout.FlexibleSpace();
                int bad = rows.Count(r => r.problems.Count > 0);
                GUILayout.Label($"작물 {rows.Count}종  ·  문제 {bad}개", GUILayout.Width(150));
                if (GUILayout.Button("새로고침", EditorStyles.toolbarButton, GUILayout.Width(70))) Reload();
                if (GUILayout.Button("목록 정리 (상점·저장·특별 상품)", EditorStyles.toolbarButton, GUILayout.Width(200))) SyncLists();
            }
            if (shop == null) EditorGUILayout.HelpBox("씬에 ShopManager가 없어서 씨앗 가격·상점 등록은 비활성 (게임 씬을 열면 보임)", MessageType.None);

            Header();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (Row r in Visible()) DrawRow(r);
            EditorGUILayout.EndScrollView();
        }

        private IEnumerable<Row> Visible()
        {
            IEnumerable<Row> q = rows.Where(r => r.crop != null);
            if (!string.IsNullOrWhiteSpace(search))
                q = q.Where(r => r.crop.itemName.Contains(search) || r.crop.name.Contains(search) || (r.seed != null && r.seed.itemName.Contains(search)));
            switch (filter)
            {
                case Filter.봄: q = q.Where(r => r.crop.CanGrowIn(Season.Spring)); break;
                case Filter.여름: q = q.Where(r => r.crop.CanGrowIn(Season.Summer)); break;
                case Filter.가을: q = q.Where(r => r.crop.CanGrowIn(Season.Autumn)); break;
                case Filter.겨울: q = q.Where(r => r.crop.CanGrowIn(Season.Winter)); break;
                case Filter.문제있음: q = q.Where(r => r.problems.Count > 0); break;
            }
            switch (sort)
            {
                case Sort.이름: q = q.OrderBy(r => r.crop.itemName); break;
                case Sort.계절: q = q.OrderBy(r => (int)r.crop.season).ThenBy(r => r.crop.itemName); break;
                case Sort.판매가: q = q.OrderByDescending(r => r.crop.basePrice); break;
                case Sort.성장일: q = q.OrderBy(r => r.crop.totalGrowthDays); break;
            }
            return q.ToList();
        }

        private static readonly (string label, float width)[] Cols =
        {
            ("", 22), ("아이콘", 36), ("작물 이름", 120), ("계절", 80), ("+계절", 40), ("성장일", 50), ("모델", 40),
            ("수확량", 50), ("판매가", 60), ("씨앗", 150), ("씨앗 이름", 110), ("씨앗 가격", 64), ("리듬 템포", 60), ("흔들림", 54), ("난이도", 54), ("패턴", 38), ("", 40),
        };

        private void Header()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                foreach (var c in Cols) GUILayout.Label(c.label, headStyle, GUILayout.Width(c.width));
        }

        private void DrawRow(Row r)
        {
            CropData c = r.crop;
            using (new EditorGUILayout.HorizontalScope(GUILayout.Height(36)))
            {
                int i = 0;
                // 문제 표시
                GUIContent warn = r.problems.Count > 0 ? new GUIContent("⚠", string.Join("\n", r.problems)) : new GUIContent("✓", "문제 없음");
                GUILayout.Label(warn, r.problems.Count > 0 ? warnStyle : headStyle, GUILayout.Width(Cols[i++].width), GUILayout.Height(34));
                // 아이콘
                Rect ir = GUILayoutUtility.GetRect(Cols[i].width, 34, GUILayout.Width(Cols[i++].width));
                if (c.icon != null) GUI.DrawTexture(ir, AssetPreview.GetAssetPreview(c.icon) ?? c.icon.texture, ScaleMode.ScaleToFit);

                EditorGUI.BeginChangeCheck();
                string cname = EditorGUILayout.TextField(c.itemName, GUILayout.Width(Cols[i++].width));
                Season season = (Season)EditorGUILayout.EnumPopup(c.season, GUILayout.Width(Cols[i++].width));
                GUILayout.Label(new GUIContent(c.additionalSeasons != null && c.additionalSeasons.Length > 0 ? $"+{c.additionalSeasons.Length}" : "-",
                                               "추가로 자랄 수 있는 계절 (작물 에셋에서 수정)"), headStyle, GUILayout.Width(Cols[i++].width));
                int days = EditorGUILayout.IntField(c.totalGrowthDays, GUILayout.Width(Cols[i++].width));
                int stages = c.growthStagePrefabs != null ? c.growthStagePrefabs.Count(p => p != null) : 0;
                GUILayout.Label(new GUIContent($"{stages}개", "성장 단계 모델 수"), stages == 0 ? warnStyle : headStyle, GUILayout.Width(Cols[i++].width));
                int yield = EditorGUILayout.IntField(c.expectedYield, GUILayout.Width(Cols[i++].width));
                float price = EditorGUILayout.FloatField(c.basePrice, GUILayout.Width(Cols[i++].width));
                bool cropChanged = EditorGUI.EndChangeCheck();

                // 씨앗 연결
                EditorGUI.BeginChangeCheck();
                SeedData seed = (SeedData)EditorGUILayout.ObjectField(r.seed, typeof(SeedData), false, GUILayout.Width(Cols[i++].width));
                if (EditorGUI.EndChangeCheck() && seed != null)
                {
                    Undo.RecordObject(seed, "씨앗 연결");
                    seed.resultCrop = c;
                    EditorUtility.SetDirty(seed);
                    r.seed = seed;
                    Check(r);
                }
                if (r.seed != null)
                {
                    EditorGUI.BeginChangeCheck();
                    string sname = EditorGUILayout.TextField(r.seed.itemName, GUILayout.Width(Cols[i++].width));
                    if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(r.seed, "씨앗 이름"); r.seed.itemName = sname; EditorUtility.SetDirty(r.seed); }
                }
                else GUILayout.Label("-", headStyle, GUILayout.Width(Cols[i++].width));

                // 상점 씨앗 가격
                int idx = r.seed != null ? SeedEntryIndex(r.seed) : -1;
                if (idx >= 0)
                {
                    var so = new SerializedObject(shop);
                    SerializedProperty p = so.FindProperty("buyCatalog").GetArrayElementAtIndex(idx).FindPropertyRelative("price");
                    EditorGUI.BeginChangeCheck();
                    int v = EditorGUILayout.IntField(p.intValue, GUILayout.Width(Cols[i++].width));
                    if (EditorGUI.EndChangeCheck()) { p.intValue = v; so.ApplyModifiedProperties(); EditorSceneManager.MarkSceneDirty(shop.gameObject.scene); }
                }
                else GUILayout.Label(new GUIContent("미등록", "상점 판매 목록에 없음 → 목록 정리"), headStyle, GUILayout.Width(Cols[i++].width));

                // 리듬게임
                EditorGUI.BeginChangeCheck();
                float tempo = EditorGUILayout.FloatField(c.harvestTempo, GUILayout.Width(Cols[i++].width));
                float shake = EditorGUILayout.FloatField(c.harvestShakeScale, GUILayout.Width(Cols[i++].width));
                float diff = EditorGUILayout.FloatField(c.harvestDifficultyOffset, GUILayout.Width(Cols[i++].width));
                bool rhythmChanged = EditorGUI.EndChangeCheck();
                int pat = c.harvestPattern != null ? c.harvestPattern.Length : 0;
                GUILayout.Label(new GUIContent(pat > 0 ? pat.ToString() : "-", "리듬 패턴 노트 수 (작물 에셋에서 수정)"), headStyle, GUILayout.Width(Cols[i++].width));

                if (GUILayout.Button(new GUIContent("열기", "작물 에셋을 선택해서 Inspector로 자세히"), GUILayout.Width(Cols[i++].width)))
                {
                    Selection.activeObject = c;
                    EditorGUIUtility.PingObject(c);
                }

                if (cropChanged || rhythmChanged)
                {
                    Undo.RecordObject(c, "작물 수정");
                    c.itemName = cname;
                    c.season = season;
                    c.totalGrowthDays = Mathf.Max(1, days);
                    c.expectedYield = Mathf.Max(0, yield);
                    c.basePrice = Mathf.Max(0f, price);
                    c.harvestTempo = Mathf.Clamp(tempo, 0.6f, 1.5f);         // CropData의 Range와 같게
                    c.harvestShakeScale = Mathf.Clamp(shake, 0.3f, 2f);
                    c.harvestDifficultyOffset = Mathf.Clamp(diff, -0.5f, 0.5f);
                    EditorUtility.SetDirty(c);
                    Check(r);
                }
            }
        }

        // ───────────────────────── 목록 정리 ─────────────────────────

        private void SyncLists()
        {
            int addedShop = 0, special = 0, saveCount = 0;
            if (shop != null)
            {
                var so = new SerializedObject(shop);
                SerializedProperty cat = so.FindProperty("buyCatalog");
                foreach (Row r in rows)
                {
                    if (r.seed == null || SeedEntryIndex(r.seed) >= 0) continue;
                    cat.arraySize++;
                    SerializedProperty e = cat.GetArrayElementAtIndex(cat.arraySize - 1);
                    e.FindPropertyRelative("item").objectReferenceValue = r.seed;
                    e.FindPropertyRelative("price").intValue = Mathf.Max(10, Mathf.RoundToInt(r.crop.basePrice * r.crop.expectedYield * 0.4f)); // 처음 가격: 수확 금액의 약 40%
                    e.FindPropertyRelative("stock").intValue = -1;
                    so.ApplyModifiedProperties();
                    addedShop++;
                }
                // 오늘의 특별 상품 후보 = 작물 전부
                SerializedProperty sp = so.FindProperty("possibleSpecialCrops");
                if (sp != null)
                {
                    var crops = rows.Select(r => r.crop).Where(c => c != null).ToList();
                    sp.arraySize = crops.Count;
                    for (int i = 0; i < crops.Count; i++) sp.GetArrayElementAtIndex(i).objectReferenceValue = crops[i];
                    special = crops.Count;
                }
                so.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(shop.gameObject.scene);
            }
            if (save != null)
            {
                save.FillDatabases(); // 저장 목록 = 프로젝트의 모든 아이템·건물
                saveCount = save.ItemCount;
                EditorUtility.SetDirty(save);
                EditorSceneManager.MarkSceneDirty(save.gameObject.scene);
            }
            AssetDatabase.SaveAssets();
            Reload();
            string msg = $"상점에 새로 등록한 씨앗: {addedShop}개 (처음 가격은 자동 - 표에서 바로 고치기)\n특별 상품 후보: {special}종\n저장 목록: 아이템 {saveCount}개" +
                         (shop == null ? "\n\n※ 씬에 ShopManager가 없어서 상점은 건너뜀" : "") + (save == null ? "\n※ 씬에 SaveManager가 없어서 저장 목록은 건너뜀" : "") +
                         "\n\nCtrl+S로 씬 저장";
            Debug.Log("[작물 관리] " + msg);
            EditorUtility.DisplayDialog("목록 정리", msg, "확인");
        }
    }
}
#endif
