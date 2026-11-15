#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FarmingSystem.Core;
using FarmingSystem.Economy;
using FarmingSystem.Farming;

namespace FarmingSystem.EditorTools
{
    // 작물 데이터를 엑셀 시트로 관리 (에디터 전용 - 빌드에 안 들어감)
    // 메뉴: Tools → 행성농사일지 → 작물 → 시트에서 가져오기   : 엑셀(.xlsx) 또는 CSV를 읽어 작물·씨앗 데이터에 적용
    //       Tools → 행성농사일지 → 작물 → 시트로 내보내기     : 지금 유니티의 작물 값을 CSV로 저장 (엑셀에서 열림)
    // 시트 규칙
    //  - 첫 줄 = 제목 (묶음, 코드, 에셋 이름, 이름, 계절, 성장일, 수확량, 판매가, 씨앗 이름, 씨앗 가격, 리듬 템포, 흔들림, 난이도, 특징, 장단점, 이야기)
    //    제목 순서는 바뀌어도 됨, 없는 제목의 칸은 건드리지 않음
    //  - '에셋 이름'(Crop_a, Crop_old_A …)으로 작물 데이터를 찾음 (없으면 묶음+코드로)
    //  - 빈 칸 = 유니티 값을 그대로 둠
    //  - 특징·장단점·이야기는 한 줄씩 합쳐서 작물 설명(description)에 들어감 → 나중에 도감에서 줄별로 나눠 쓸 수 있음
    //  - 씨앗 가격은 씬의 ShopManager 판매 목록에 있는 씨앗만 바뀜 (게임 씬을 열어 두고 실행)
    // 실행 취소(Ctrl+Z) 가능
    public static class CropSheetImporter
    {
        private static readonly string[] SeasonNames = { "봄", "여름", "가을", "겨울" }; // Season 순서와 같음
        private static readonly string[] Headers =
            { "묶음", "코드", "에셋 이름", "이름", "계절", "성장일", "수확량", "판매가", "씨앗 이름", "씨앗 가격", "리듬 템포", "흔들림", "난이도", "특징", "장단점", "이야기" };

        // ───────────────────────── 가져오기 ─────────────────────────

        [MenuItem("Tools/행성농사일지/작물/시트에서 가져오기")]
        private static void Import()
        {
            string path = EditorUtility.OpenFilePanelWithFilters("작물 데이터 시트 고르기", Application.dataPath, new[] { "엑셀·CSV", "xlsx,csv" });
            if (string.IsNullOrEmpty(path)) return;

            List<List<string>> table;
            try { table = path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? ReadCsv(path) : ReadXlsx(path); }
            catch (IOException) { EditorUtility.DisplayDialog("시트에서 가져오기", "파일을 열 수 없어요. 엑셀에서 열려 있다면 저장한 뒤 다시 해 주세요.", "확인"); return; }
            catch (Exception e) { EditorUtility.DisplayDialog("시트에서 가져오기", "시트를 읽지 못했어요: " + e.Message + "\n\n엑셀에서 'CSV UTF-8'로 저장해서 CSV로 가져와도 돼요.", "확인"); return; }
            if (table.Count < 2) { EditorUtility.DisplayDialog("시트에서 가져오기", "제목 줄 아래에 작물 줄이 없어요.", "확인"); return; }

            var col = new Dictionary<string, int>();
            for (int i = 0; i < table[0].Count; i++)
            {
                string h = table[0][i].Trim();
                if (h.Length > 0 && !col.ContainsKey(h)) col[h] = i;
            }
            if (!col.ContainsKey("에셋 이름") && !col.ContainsKey("코드"))
            {
                EditorUtility.DisplayDialog("시트에서 가져오기", "첫 줄에 '에셋 이름' 또는 '코드' 제목이 있어야 해요.", "확인");
                return;
            }

            var crops = Load<CropData>().ToDictionary(c => c.name, StringComparer.Ordinal);
            var seeds = Load<SeedData>();
            ShopManager shop = UnityEngine.Object.FindAnyObjectByType<ShopManager>(FindObjectsInactive.Include);
            SerializedObject shopSo = shop != null ? new SerializedObject(shop) : null;
            SerializedProperty catalog = shopSo?.FindProperty("buyCatalog");

            Undo.SetCurrentGroupName("작물 시트 가져오기");
            int group = Undo.GetCurrentGroup();
            int updated = 0, priceSet = 0;
            var missing = new List<string>();
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
                    asset = Cell("묶음").StartsWith("1") ? "Crop_old_" + code : "Crop_" + code;
                }
                if (!crops.TryGetValue(asset, out CropData crop)) { missing.Add(asset); continue; }

                Undo.RecordObject(crop, "작물 시트 가져오기");
                string name = Cell("이름");
                if (name.Length > 0) crop.itemName = name;
                string season = Cell("계절");
                if (season.Length > 0 && !ApplySeasons(crop, season)) warnings.Add($"{asset}: 계절 '{season}'을 읽지 못함 (봄·여름·가을·겨울)");
                if (TryInt(Cell("성장일"), out int days)) crop.totalGrowthDays = Mathf.Max(1, days);
                if (TryInt(Cell("수확량"), out int yield)) crop.expectedYield = Mathf.Max(0, yield);
                if (TryFloat(Cell("판매가"), out float price)) crop.basePrice = Mathf.Max(0f, price);
                if (TryFloat(Cell("리듬 템포"), out float tempo)) crop.harvestTempo = Mathf.Clamp(tempo, 0.6f, 1.5f);
                if (TryFloat(Cell("흔들림"), out float shake)) crop.harvestShakeScale = Mathf.Clamp(shake, 0.3f, 2f);
                if (TryFloat(Cell("난이도"), out float diff)) crop.harvestDifficultyOffset = Mathf.Clamp(diff, -0.5f, 0.5f);

                // 도감 설명: 특징 / 장단점 / 이야기 → 한 줄씩
                string[] parts = { Cell("특징"), Cell("장단점"), Cell("이야기") };
                if (parts.Any(p => p.Length > 0))
                {
                    string[] old = (crop.description ?? "").Split('\n');
                    for (int i = 0; i < 3; i++) if (parts[i].Length == 0 && i < old.Length) parts[i] = old[i]; // 빈 칸은 원래 줄 유지
                    crop.description = string.Join("\n", parts).TrimEnd('\n');
                }
                EditorUtility.SetDirty(crop);

                // 씨앗
                SeedData seed = seeds.FirstOrDefault(s => s.resultCrop == crop);
                string seedName = Cell("씨앗 이름");
                if (seed != null && seedName.Length > 0)
                {
                    Undo.RecordObject(seed, "작물 시트 가져오기");
                    seed.itemName = seedName;
                    EditorUtility.SetDirty(seed);
                }
                if (TryInt(Cell("씨앗 가격"), out int seedPrice))
                {
                    int idx = seed != null ? CatalogIndex(catalog, seed) : -1;
                    if (idx >= 0) { catalog.GetArrayElementAtIndex(idx).FindPropertyRelative("price").intValue = Mathf.Max(0, seedPrice); priceSet++; }
                    else warnings.Add($"{asset}: 씨앗이 상점 목록에 없어서 가격을 못 넣음 (작물 관리 → 목록 정리 후 다시)");
                }
                updated++;
            }

            if (shopSo != null && priceSet > 0)
            {
                shopSo.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(shop.gameObject.scene);
            }
            Undo.CollapseUndoOperations(group);
            AssetDatabase.SaveAssets();

            var msg = new StringBuilder($"작물 {updated}종 적용, 씨앗 가격 {priceSet}개");
            if (missing.Count > 0) msg.Append($"\n\n유니티에 아직 없는 작물 {missing.Count}개 (건너뜀): {string.Join(", ", missing.Take(8))}{(missing.Count > 8 ? " …" : "")}");
            if (shop == null) msg.Append("\n\n※ 씬에 ShopManager가 없어서 씨앗 가격은 건너뜀 (게임 씬을 열고 다시)");
            if (warnings.Count > 0) msg.Append("\n\n확인할 것:\n· " + string.Join("\n· ", warnings.Take(10)));
            if (priceSet > 0) msg.Append("\n\nCtrl+S로 씬 저장 (씨앗 가격)");
            Debug.Log("[작물 시트] " + msg);
            EditorUtility.DisplayDialog("시트에서 가져오기", msg.ToString(), "확인");
        }

        // "봄", "봄·여름", "봄, 여름", "모든 계절" → 첫 계절 = 기본, 나머지 = 추가 계절
        private static bool ApplySeasons(CropData crop, string text)
        {
            var list = new List<Season>();
            if (text.Contains("모든") || text.Contains("사계절")) list.AddRange(new[] { Season.Spring, Season.Summer, Season.Autumn, Season.Winter });
            else
                foreach (string p in text.Split(new[] { '·', ',', '/', ' ', '、', '+' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int i = Array.IndexOf(SeasonNames, p.Trim());
                    if (i < 0) return false;
                    if (!list.Contains((Season)i)) list.Add((Season)i);
                }
            if (list.Count == 0) return false;
            crop.season = list[0];
            crop.additionalSeasons = list.Skip(1).ToArray();
            return true;
        }

        private static int CatalogIndex(SerializedProperty catalog, SeedData seed)
        {
            if (catalog == null) return -1;
            for (int i = 0; i < catalog.arraySize; i++)
                if (catalog.GetArrayElementAtIndex(i).FindPropertyRelative("item").objectReferenceValue == seed) return i;
            return -1;
        }

        // ───────────────────────── 내보내기 ─────────────────────────

        [MenuItem("Tools/행성농사일지/작물/시트로 내보내기")]
        private static void Export()
        {
            string path = EditorUtility.SaveFilePanel("작물 데이터 내보내기 (엑셀에서 열림)", Application.dataPath, "작물데이터_내보내기", "csv");
            if (string.IsNullOrEmpty(path)) return;
            var seeds = Load<SeedData>();
            ShopManager shop = UnityEngine.Object.FindAnyObjectByType<ShopManager>(FindObjectsInactive.Include);
            SerializedProperty catalog = shop != null ? new SerializedObject(shop).FindProperty("buyCatalog") : null;

            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", Headers.Select(Csv)));
            foreach (CropData c in Load<CropData>().OrderBy(c => c.name.StartsWith("Crop_old_") ? 1 : 0).ThenBy(c => c.name, StringComparer.Ordinal))
            {
                SeedData seed = seeds.FirstOrDefault(s => s.resultCrop == c);
                int idx = seed != null ? CatalogIndex(catalog, seed) : -1;
                bool old = c.name.StartsWith("Crop_old_");
                string code = old ? c.name.Substring(9) : c.name.StartsWith("Crop_") ? c.name.Substring(5) : c.name;
                var seasons = new List<string> { SeasonNames[(int)c.season] };
                if (c.additionalSeasons != null) seasons.AddRange(c.additionalSeasons.Select(s => SeasonNames[(int)s]));
                string[] desc = (c.description ?? "").Split('\n');
                string[] row =
                {
                    old ? "1차" : "2차", code, c.name, c.itemName, string.Join("·", seasons.Distinct()),
                    c.totalGrowthDays.ToString(), c.expectedYield.ToString(), c.basePrice.ToString(CultureInfo.InvariantCulture),
                    seed != null ? seed.itemName : "", idx >= 0 ? catalog.GetArrayElementAtIndex(idx).FindPropertyRelative("price").intValue.ToString() : "",
                    c.harvestTempo.ToString(CultureInfo.InvariantCulture), c.harvestShakeScale.ToString(CultureInfo.InvariantCulture), c.harvestDifficultyOffset.ToString(CultureInfo.InvariantCulture),
                    desc.Length > 0 ? desc[0] : "", desc.Length > 1 ? desc[1] : "", desc.Length > 2 ? string.Join(" ", desc.Skip(2)) : "",
                };
                sb.AppendLine(string.Join(",", row.Select(Csv)));
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true)); // BOM → 엑셀에서 한글이 안 깨짐
            EditorUtility.RevealInFinder(path);
            Debug.Log($"[작물 시트] 내보내기 완료: {path}");
        }

        private static string Csv(string v)
        {
            if (v == null) v = "";
            return v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        }

        // ───────────────────────── 파일 읽기 ─────────────────────────

        private static List<T> Load<T>() where T : UnityEngine.Object =>
            AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { "Assets" })
                .Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(a => a != null).ToList();

        private static bool TryInt(string s, out int v)
        {
            v = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return true;
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) { v = Mathf.RoundToInt(f); return true; } // 엑셀이 "3.0"으로 줄 때
            return false;
        }

        private static bool TryFloat(string s, out float v)
        {
            v = 0f;
            return !string.IsNullOrWhiteSpace(s) && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        // CSV (UTF-8, 따옴표·줄바꿈 처리)
        private static List<List<string>> ReadCsv(string path)
        {
            string text;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs, new UTF8Encoding(false), true)) text = sr.ReadToEnd();
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (quoted)
                {
                    if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else if (ch == '"') quoted = false;
                    else cell.Append(ch);
                }
                else if (ch == '"') quoted = true;
                else if (ch == ',') { row.Add(cell.ToString()); cell.Clear(); }
                else if (ch == '\n' || ch == '\r')
                {
                    if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(cell.ToString()); cell.Clear();
                    if (row.Any(c => c.Length > 0)) rows.Add(row);
                    row = new List<string>();
                }
                else cell.Append(ch);
            }
            row.Add(cell.ToString());
            if (row.Any(c => c.Length > 0)) rows.Add(row);
            return rows;
        }

        // 엑셀 .xlsx (첫 번째 시트만). xlsx = zip 안의 XML이라 따로 라이브러리 없이 읽음
        private static List<List<string>> ReadXlsx(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

                // 공유 문자열
                var shared = new List<string>();
                XmlDocument ss = LoadXml(zip, "xl/sharedStrings.xml");
                if (ss != null)
                {
                    var ns = new XmlNamespaceManager(ss.NameTable); ns.AddNamespace("m", Main);
                    foreach (XmlNode si in ss.SelectNodes("//m:si", ns))
                        shared.Add(string.Concat(si.SelectNodes(".//m:t", ns).Cast<XmlNode>().Select(t => t.InnerText)));
                }

                // 첫 번째 시트 파일 찾기
                string sheetPath = "xl/worksheets/sheet1.xml";
                XmlDocument wbDoc = LoadXml(zip, "xl/workbook.xml"), rels = LoadXml(zip, "xl/_rels/workbook.xml.rels");
                if (wbDoc != null && rels != null)
                {
                    var ns = new XmlNamespaceManager(wbDoc.NameTable); ns.AddNamespace("m", Main);
                    XmlElement first = wbDoc.SelectSingleNode("//m:sheets/m:sheet", ns) as XmlElement;
                    string rid = first?.GetAttribute("id", Rel);
                    foreach (XmlElement rel in rels.GetElementsByTagName("Relationship"))
                        if (rel.GetAttribute("Id") == rid)
                        {
                            string target = rel.GetAttribute("Target").TrimStart('/');
                            sheetPath = target.StartsWith("xl/") ? target : "xl/" + target;
                        }
                }

                XmlDocument sheet = LoadXml(zip, sheetPath) ?? throw new Exception("시트를 찾지 못함");
                var sns = new XmlNamespaceManager(sheet.NameTable); sns.AddNamespace("m", Main);
                var rows = new List<List<string>>();
                foreach (XmlElement r in sheet.SelectNodes("//m:sheetData/m:row", sns))
                {
                    var row = new List<string>();
                    foreach (XmlElement c in r.SelectNodes("m:c", sns))
                    {
                        int ci = ColumnIndex(c.GetAttribute("r"));
                        while (row.Count < ci) row.Add("");
                        string type = c.GetAttribute("t");
                        string v = c.SelectSingleNode("m:v", sns)?.InnerText ?? "";
                        string value = type == "s" && int.TryParse(v, out int si) && si < shared.Count ? shared[si]
                                     : type == "inlineStr" ? string.Concat(c.SelectNodes(".//m:t", sns).Cast<XmlNode>().Select(t => t.InnerText))
                                     : v;
                        if (ci < row.Count) row[ci] = value; else row.Add(value);
                    }
                    if (row.Any(x => x.Length > 0)) rows.Add(row);
                }
                return rows;
            }
        }

        private static XmlDocument LoadXml(ZipArchive zip, string entry)
        {
            ZipArchiveEntry e = zip.GetEntry(entry);
            if (e == null) return null;
            var doc = new XmlDocument();
            using (Stream s = e.Open()) doc.Load(s);
            return doc;
        }

        // "C12" → 2
        private static int ColumnIndex(string cellRef)
        {
            int n = 0;
            foreach (char ch in cellRef)
            {
                if (!char.IsLetter(ch)) break;
                n = n * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
            }
            return Mathf.Max(0, n - 1);
        }
    }
}
#endif
