#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FarmingSystem.EditorTools
{
    // 업그레이드 도구 아이콘을 기본 도구 아이콘의 "색만 바꾼" 그림으로 만들어 넣기
    // 메뉴: Tools → 행성농사일지 → 도구 → 업그레이드 도구 색 아이콘 만들기
    //
    //  - 같은 종류(호미·물뿌리개·수확장갑…)끼리 묶어서, 범위(Range)가 가장 작은 도구 = 기본 도구 → 그 아이콘을 그대로 씀
    //  - 그 위 단계 도구는 기본 아이콘의 "쇠 부분(회색·흰색 픽셀)"만 색을 칠함 → 손잡이·테두리·초록 부분은 그대로
    //  - 색은 도구 이름에 맞춰 고름 (창의 "이름별 색" 목록, 이름에 그 낱말이 들어 있으면 그 색)
    //      튼튼한 = 구리 / 강화된 = 붉은 강철 / 넓은 = 청록 물빛 / 소나기 = 비 파랑 / 별빛 = 보랏빛 별
    //    목록에 맞는 낱말이 없으면 단계 색 (1단계 위 = 구리, 2단계 위 = 금)
    //    쇠 부분이 거의 없는 아이콘(장갑 등)은 아이콘 전체를 그 색 쪽으로 물들임
    //  - 만든 그림: Assets/Art/Icons/ToolTiers/(도구 에셋 이름).png → 그 도구의 Icon 칸에 자동으로 넣음
    //  - 색을 바꿔서 다시 누르면 다시 만들어서 덮어씀 (원본 아이콘 시트는 절대 안 건드림)
    //  - 도구 스크립트(ToolData)·UI 스크립트는 하나도 안 고침 → 핫바·가방·알림 어디서든 그대로 바뀐 아이콘이 보임
    public class ToolTierTintWindow : EditorWindow
    {
        private const string OutFolder = "Assets/Art/Icons/ToolTiers";

        [System.Serializable]
        private class NameColor { public string word; public Color color; public NameColor(string w, Color c) { word = w; color = c; } }

        // 이름에 이 낱말이 들어 있으면 이 색 (위에서부터 먼저 맞는 것)
        [SerializeField]
        private List<NameColor> nameColors = new List<NameColor>
        {
            new NameColor("튼튼한", new Color(0.85f, 0.50f, 0.28f)), // 단단한 구리
            new NameColor("강화된", new Color(0.90f, 0.35f, 0.30f)), // 붉은 강철
            new NameColor("넓은",   new Color(0.30f, 0.80f, 0.72f)), // 넓은 물빛 (청록)
            new NameColor("소나기", new Color(0.28f, 0.48f, 0.98f)), // 쏟아지는 비 (진한 파랑)
            new NameColor("별빛",   new Color(0.72f, 0.55f, 0.98f)), // 보랏빛 별
        };
        private Vector2 scroll;

        [SerializeField] private Color tier1Color = new Color(0.85f, 0.48f, 0.25f); // 구리
        [SerializeField] private Color tier2Color = new Color(0.95f, 0.78f, 0.25f); // 금
        [SerializeField, Range(0.05f, 0.6f)] private float metalSaturation = 0.28f;

        [MenuItem("Tools/행성농사일지/도구/업그레이드 도구 색 아이콘 만들기")]
        private static void Open() => GetWindow<ToolTierTintWindow>("도구 색 아이콘").minSize = new Vector2(380, 420);

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("기본 도구 아이콘의 쇠 부분만 단계 색으로 칠해서 업그레이드 도구 아이콘을 만들어요.\n" +
                                    "원본 아이콘 시트는 안 건드리고, 새 PNG를 만들어 Icon 칸에 넣어요.", MessageType.Info);
            EditorGUILayout.LabelField("이름별 색 (도구 이름에 이 낱말이 있으면 이 색)", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MaxHeight(150));
            for (int i = 0; i < nameColors.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                nameColors[i].word = EditorGUILayout.TextField(nameColors[i].word, GUILayout.Width(110));
                nameColors[i].color = EditorGUILayout.ColorField(nameColors[i].color);
                if (GUILayout.Button("−", GUILayout.Width(24))) { nameColors.RemoveAt(i); break; }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("+ 낱말 추가")) nameColors.Add(new NameColor("새 낱말", Color.white));
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("이름에 맞는 낱말이 없을 때", EditorStyles.boldLabel);
            tier1Color = EditorGUILayout.ColorField(new GUIContent("1단계 위 색"), tier1Color);
            tier2Color = EditorGUILayout.ColorField(new GUIContent("2단계 위 색"), tier2Color);
            metalSaturation = EditorGUILayout.Slider(new GUIContent("쇠로 볼 기준", "이 값보다 색이 옅은(회색·흰색) 픽셀만 칠함. 손잡이까지 칠해지면 낮추고, 쇠가 덜 칠해지면 올리기"), metalSaturation, 0.05f, 0.6f);
            EditorGUILayout.Space();
            if (GUILayout.Button("만들기 (아이콘 자동으로 넣기)", GUILayout.Height(34))) Build();
        }

        private class Tool
        {
            public ScriptableObject asset;
            public SerializedObject so;
            public int type, range, durability;
            public string itemName;
            public Sprite icon;
            public bool Generated => icon != null && AssetDatabase.GetAssetPath(icon).StartsWith(OutFolder);
        }

        private void Build()
        {
            var tools = new List<Tool>();
            foreach (string g in AssetDatabase.FindAssets("t:ToolData"))
            {
                var a = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(g));
                if (a == null) continue;
                var so = new SerializedObject(a);
                SerializedProperty t = so.FindProperty("toolType"), r = so.FindProperty("range"), d = so.FindProperty("maxDurability"), ic = so.FindProperty("icon");
                if (t == null || ic == null) continue;
                tools.Add(new Tool
                {
                    asset = a,
                    so = so,
                    type = t.enumValueIndex >= 0 ? t.enumValueIndex : t.intValue,
                    itemName = so.FindProperty("itemName") != null ? so.FindProperty("itemName").stringValue : a.name,
                    range = r != null ? r.intValue : 0,
                    durability = d != null ? d.intValue : 0,
                    icon = ic.objectReferenceValue as Sprite
                });
            }

            // 이미 다른(직접 넣은) 아이콘이 있는 업그레이드 도구도 바꿀지 한 번만 물어봄
            var jobs = new List<(Tool tool, Tool baseTool, int step)>();
            var report = new List<string>();
            foreach (var group in tools.GroupBy(x => x.type))
            {
                var list = group.OrderBy(x => x.range).ThenBy(x => x.durability).ToList();
                if (list.Count < 2) continue; // 단계가 하나뿐 (낚싯대 등)
                Tool baseTool = list[0];
                if (baseTool.icon == null || baseTool.Generated)
                {
                    report.Add($"✗ {baseTool.asset.name}: 기본 도구에 아이콘이 없어서 건너뜀 → 먼저 Icon 칸에 기본 아이콘을 넣어 주세요");
                    continue;
                }
                for (int i = 1; i < list.Count; i++) jobs.Add((list[i], baseTool, i));
            }
            int custom = jobs.Count(j => j.tool.icon != null && !j.tool.Generated);
            bool replaceCustom = custom == 0 || EditorUtility.DisplayDialog("도구 색 아이콘",
                $"업그레이드 도구 중 {custom}개는 이미 다른 아이콘이 들어 있어요.\n그것도 기본 아이콘 색 바꾼 걸로 바꿀까요?", "모두 바꾸기", "빈 칸만 채우기");

            EnsureFolder(OutFolder);
            var srcCache = new Dictionary<string, Texture2D>();
            foreach (var (tool, baseTool, step) in jobs)
            {
                if (tool.icon != null && !tool.Generated && !replaceCustom) { report.Add($"- {tool.asset.name}: 이미 아이콘이 있어서 그대로"); continue; }
                NameColor named = nameColors.FirstOrDefault(n => !string.IsNullOrEmpty(n.word) && tool.itemName != null && tool.itemName.Contains(n.word));
                Color tint = named != null ? named.color : step == 1 ? tier1Color : tier2Color;
                Texture2D pixels = ReadSprite(baseTool.icon, srcCache);
                if (pixels == null) { report.Add($"✗ {tool.asset.name}: 기본 아이콘 그림을 못 읽음"); continue; }
                Recolor(pixels, tint);

                string path = $"{OutFolder}/{tool.asset.name}.png";
                File.WriteAllBytes(path, pixels.EncodeToPNG());
                DestroyImmediate(pixels);
                AssetDatabase.ImportAsset(path);
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.spritePixelsPerUnit = baseTool.icon.pixelsPerUnit;
                imp.filterMode = FilterMode.Point;       // 픽셀 그림이 흐려지지 않게
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.mipmapEnabled = false;
                imp.alphaIsTransparency = true;
                imp.SaveAndReimport();

                Sprite made = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                tool.so.Update();
                tool.so.FindProperty("icon").objectReferenceValue = made;
                tool.so.ApplyModifiedProperties();
                EditorUtility.SetDirty(tool.asset);
                report.Add($"✓ {tool.itemName} ({tool.asset.name}): {baseTool.asset.name} 아이콘 + {(named != null ? $"'{named.word}' 색" : step == 1 ? "1단계 색" : "2단계 색")}");
            }
            foreach (Texture2D t in srcCache.Values) DestroyImmediate(t);
            AssetDatabase.SaveAssets();

            string msg = report.Count > 0 ? string.Join("\n", report) : "바꿀 도구가 없어요 (같은 종류 도구가 2개 이상이어야 해요)";
            Debug.Log("[도구 색 아이콘]\n" + msg);
            EditorUtility.DisplayDialog("도구 색 아이콘", msg, "확인");
        }

        // 스프라이트 한 칸의 픽셀 (원본 PNG 파일을 직접 읽음 → 텍스처 Read/Write 설정과 상관없음)
        private static Texture2D ReadSprite(Sprite sprite, Dictionary<string, Texture2D> cache)
        {
            string path = AssetDatabase.GetAssetPath(sprite.texture);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            if (!cache.TryGetValue(path, out Texture2D src))
            {
                src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!src.LoadImage(File.ReadAllBytes(path))) return null;
                cache[path] = src;
            }
            // 임포트 설정(Max Size)으로 줄어든 텍스처 기준 좌표 → 원본 크기로 맞춤
            float k = src.width / (float)sprite.texture.width;
            Rect r = sprite.rect;
            int x = Mathf.RoundToInt(r.x * k), y = Mathf.RoundToInt(r.y * k);
            int w = Mathf.Clamp(Mathf.RoundToInt(r.width * k), 1, src.width - x), h = Mathf.Clamp(Mathf.RoundToInt(r.height * k), 1, src.height - y);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels(src.GetPixels(x, y, w, h));
            return tex;
        }

        // 쇠 부분(옅은 색·너무 어둡지 않은 픽셀)만 단계 색으로. 밝기(명암)는 그대로라 픽셀 그림 입체감 유지
        private void Recolor(Texture2D tex, Color tint)
        {
            Color.RGBToHSV(tint, out float th, out float ts, out _);
            Color[] px = tex.GetPixels();
            int opaque = 0, metal = 0;
            foreach (Color c in px)
            {
                if (c.a < 0.1f) continue;
                opaque++;
                Color.RGBToHSV(c, out _, out float s, out float v);
                if (s < metalSaturation && v > 0.22f) metal++;
            }
            bool wholeIcon = opaque > 0 && metal < opaque * 0.1f; // 쇠가 거의 없는 그림(장갑 등) → 전체를 물들임

            for (int i = 0; i < px.Length; i++)
            {
                Color c = px[i];
                if (c.a < 0.1f) continue;
                Color.RGBToHSV(c, out float h, out float s, out float v);
                if (!wholeIcon)
                {
                    if (s >= metalSaturation || v <= 0.22f) continue;
                    float ns = ts * (0.35f + 0.65f * (1f - v * 0.6f)); // 밝은 곳은 살짝 옅게 → 반짝이는 느낌
                    Color n = Color.HSVToRGB(th, ns, v);
                    n.a = c.a;
                    px[i] = n;
                }
                else if (v > 0.18f) // 테두리(아주 어두운 픽셀)는 그대로
                {
                    Color n = Color.HSVToRGB(th, Mathf.Lerp(s, ts, 0.7f), v);
                    n = Color.Lerp(c, n, 0.75f);
                    n.a = c.a;
                    px[i] = n;
                }
            }
            tex.SetPixels(px);
            tex.Apply();
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