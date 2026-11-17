#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FarmingSystem.Core;
using FarmingSystem.Fishing;
using FarmingSystem.Inventory;
using FarmingSystem.Save;

namespace FarmingSystem.EditorTools
{
    // 물고기 데이터를 엑셀 시트로 관리 (에디터 전용)
    // 메뉴: Tools → 행성농사일지 → 물고기 → 시트에서 가져오기 : 물고기데이터.xlsx(또는 CSV)를 읽어 FishData를 만들고 고침
    //  - 첫 줄 = 제목 (코드, 에셋 이름, 이름, 장소, 계절, 시작 시각, 끝 시각, 그림자 크기, 희귀도, 판매가, 입질 시간(초), 설명)
    //  - '에셋 이름'(Fish_F01 …)으로 찾고, 없으면 Assets/Data/Fish 에 새로 만듦 (작물과 다른 점: 줄을 추가하면 물고기가 생김)
    //  - 빈 칸 = 그대로 둠. 아이콘은 시트로 못 넣음 → 에셋의 Icon 칸에 직접
    //  - 끝나면 저장 목록(SaveManager)을 다시 채움 (게임 씬을 열어 두고 실행)
    public static class FishSheetImporter
    {
        private const string Folder = "Assets/Data/Fish";
        private static readonly string[] SeasonNames = { "봄", "여름", "가을", "겨울" };

        [MenuItem("Tools/행성농사일지/물고기/시트에서 가져오기")]
        private static void Import()
        {
            string path = EditorUtility.OpenFilePanelWithFilters("물고기 데이터 시트 고르기", Application.dataPath, new[] { "엑셀·CSV", "xlsx,csv" });
            if (string.IsNullOrEmpty(path)) return;

            List<List<string>> table;
            try { table = path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? CropSheetImporter.ReadCsv(path) : CropSheetImporter.ReadXlsx(path); }
            catch (IOException) { EditorUtility.DisplayDialog("물고기 가져오기", "파일을 열 수 없어요. 엑셀에서 열려 있다면 저장한 뒤 다시 해 주세요.", "확인"); return; }
            catch (Exception e) { EditorUtility.DisplayDialog("물고기 가져오기", "시트를 읽지 못했어요: " + e.Message, "확인"); return; }
            if (table.Count < 2) { EditorUtility.DisplayDialog("물고기 가져오기", "제목 줄 아래에 물고기 줄이 없어요.", "확인"); return; }

            var col = new Dictionary<string, int>();
            for (int i = 0; i < table[0].Count; i++)
            {
                string h = table[0][i].Trim();
                if (h.Length > 0 && !col.ContainsKey(h)) col[h] = i;
            }
            if (!col.ContainsKey("에셋 이름") && !col.ContainsKey("코드"))
            {
                EditorUtility.DisplayDialog("물고기 가져오기", "첫 줄에 '에셋 이름' 또는 '코드' 제목이 있어야 해요.", "확인");
                return;
            }

            EnsureFolder(Folder);
            var existing = new Dictionary<string, FishData>();
            foreach (string g in AssetDatabase.FindAssets("t:FishData", new[] { "Assets" }))
            {
                var f = AssetDatabase.LoadAssetAtPath<FishData>(AssetDatabase.GUIDToAssetPath(g));
                if (f != null) existing[f.name] = f;
            }

            int created = 0, updated = 0;
            var warnings = new List<string>();
            for (int r = 1; r < table.Count; r++)
            {
                List<string> row = table[r];
                string Cell(string h) => col.TryGetValue(h, out int i) && i < row.Count ? row[i].Trim() : "";
                string asset = Cell("에셋 이름");
                if (asset.Length == 0)
                {
                    string code = Cell("코드");
                    if (code.Length == 0) continue;
                    asset = "Fish_" + code;
                }

                if (!existing.TryGetValue(asset, out FishData fish))
                {
                    fish = ScriptableObject.CreateInstance<FishData>();
                    fish.category = ItemCategory.Fish;
                    fish.maxStack = 99;
                    AssetDatabase.CreateAsset(fish, $"{Folder}/{asset}.asset");
                    existing[asset] = fish;
                    created++;
                }
                else
                {
                    Undo.RecordObject(fish, "물고기 시트 가져오기");
                    updated++;
                }

                string name = Cell("이름");
                if (name.Length > 0) fish.itemName = name;
                string place = Cell("장소");
                if (place.Length > 0)
                {
                    if (place.Contains("어디") || (place.Contains("바다") && place.Contains("강"))) fish.habitat = FishHabitat.Any;
                    else if (place.Contains("바다")) fish.habitat = FishHabitat.Sea;
                    else if (place.Contains("강")) fish.habitat = FishHabitat.River;
                    else warnings.Add($"{asset}: 장소 '{place}'를 읽지 못함 (바다·강·어디든)");
                }
                string season = Cell("계절");
                if (season.Length > 0 && !ApplySeasons(fish, season)) warnings.Add($"{asset}: 계절 '{season}'을 읽지 못함");
                if (CropSheetImporter.TryInt(Cell("시작 시각"), out int sh)) fish.startHour = Mathf.Clamp(sh, 0, 23);
                if (CropSheetImporter.TryInt(Cell("끝 시각"), out int eh)) fish.endHour = Mathf.Clamp(eh, 0, 23);
                if (CropSheetImporter.TryInt(Cell("그림자 크기"), out int size)) fish.shadowSize = Mathf.Clamp(size, 1, 6);
                string rar = Cell("희귀도");
                if (rar.Length > 0)
                {
                    if (rar.Contains("전설")) fish.rarity = FishRarity.Legendary;
                    else if (rar.Contains("희귀")) fish.rarity = FishRarity.Rare;
                    else if (rar.Contains("보통")) fish.rarity = FishRarity.Uncommon;
                    else if (rar.Contains("흔")) fish.rarity = FishRarity.Common;
                    else warnings.Add($"{asset}: 희귀도 '{rar}'를 읽지 못함 (흔함·보통·희귀·전설)");
                }
                if (CropSheetImporter.TryInt(Cell("판매가"), out int price)) fish.basePrice = Mathf.Max(0, price);
                if (CropSheetImporter.TryFloat(Cell("입질 시간(초)"), out float win)) fish.biteWindow = Mathf.Clamp(win, 0.25f, 1.5f);
                string desc = Cell("설명");
                if (desc.Length > 0) fish.description = desc;
                EditorUtility.SetDirty(fish);
            }
            AssetDatabase.SaveAssets();

            SaveManager save = UnityEngine.Object.FindAnyObjectByType<SaveManager>(FindObjectsInactive.Include);
            if (save != null) { save.FillDatabases(); EditorSceneManager.MarkSceneDirty(save.gameObject.scene); }

            var msg = new StringBuilder($"물고기 {created}종 새로 만듦, {updated}종 고침 ({Folder})");
            if (save == null) msg.Append("\n\n※ 씬에 SaveManager가 없어서 저장 목록은 못 채웠어요 (게임 씬을 열고 다시)");
            else msg.Append("\n\nCtrl+S로 씬 저장");
            if (warnings.Count > 0) msg.Append("\n\n확인할 것:\n· " + string.Join("\n· ", warnings.Take(10)));
            Debug.Log("[물고기 시트] " + msg);
            EditorUtility.DisplayDialog("물고기 가져오기", msg.ToString(), "확인");
        }

        private static bool ApplySeasons(FishData fish, string text)
        {
            if (text.Contains("모든") || text.Contains("사계절")) { fish.seasons = new Season[0]; return true; }
            var list = new List<Season>();
            foreach (string p in text.Split(new[] { '·', ',', '/', ' ', '、', '+' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int i = Array.IndexOf(SeasonNames, p.Trim());
                if (i < 0) return false;
                if (!list.Contains((Season)i)) list.Add((Season)i);
            }
            if (list.Count == 0) return false;
            fish.seasons = list.Count >= 4 ? new Season[0] : list.ToArray();
            return true;
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
