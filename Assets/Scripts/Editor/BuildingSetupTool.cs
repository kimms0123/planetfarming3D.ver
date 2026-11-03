#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;
using FarmingSystem.Building;
using FarmingSystem.Inventory;
using FarmingSystem.UI;

namespace FarmingSystem.EditorTools
{
    // 건축 시스템 만들기
    // 메뉴: Tools → 행성농사일지 → 건축 → 건축 시스템 만들기
    // 한 번 누르면 아래를 전부 해 줌 (다시 눌러도 안전 - 이미 있는 건 그대로 두고 빠진 것만 채움)
    // 1. 씬에 "BuildingSystem" 오브젝트 (BuildingSystem + BuildingPlacer)
    // 2. 재질 3개: 설치 미리보기(반투명), 공사장 울타리(갈색), 표지판(노랑) → Assets/Buildings/Materials
    // 3. 화면 아래 가운데 안내 글자 (핫바가 있는 Canvas 안 "BuildingHint")
    // 4. 우주선 3종의 Building Data + 건물 키트 아이템 → Assets/Buildings/Data, Assets/Buildings/Kits
    //    (아이콘은 모델 미리보기 그림으로 임시 생성 → Assets/Buildings/Icons. 그래픽 팀 아이콘이 오면 교체)
    // 5. 우주선 재질을 곡면 셰이더로 바꿈 (맵처럼 멀리서 휘어 보이게 - '곡면 월드/되돌리기'로 원래대로 가능)
    // 6. (선택) 테스트용으로 키트 3개를 시작 아이템(가방 21~23번째 칸)에 넣기
    public static class BuildingSetupTool
    {
        private const string Root = "Assets/Buildings";
        private const string MatFolder = Root + "/Materials";
        private const string DataFolder = Root + "/Data";
        private const string KitFolder = Root + "/Kits";
        private const string IconFolder = Root + "/Icons";

        // 기본 건물 목록 (프리팹 이름, 표시 이름, 설명, 칸 수)
        private static readonly (string prefab, string name, string desc, Vector2Int fp)[] Defaults =
        {
            ("player_spaceship", "내 우주선", "내가 타고 온 우주선. 행성 생활의 보금자리가 되어 줄 거예요.", new Vector2Int(3, 3)),
            ("fox_spaceship", "여우 우주선", "여우 모양의 우주선. 행성에 손님을 맞이할 수 있어요.", new Vector2Int(4, 4)),
            ("fish_spaceship", "물고기 우주선", "물고기 모양의 우주선. 행성에 손님을 맞이할 수 있어요.", new Vector2Int(3, 3)),
        };

        [MenuItem("Tools/행성농사일지/건축/건축 시스템 만들기")]
        private static void Setup()
        {
            var log = new List<string>();

            // 1. 재질
            Material ghost = MakeGhostMaterial(log);
            Material fence = MakeLitMaterial("Building_Fence", new Color(0.55f, 0.38f, 0.24f), log);
            Material sign = MakeLitMaterial("Building_Sign", new Color(0.98f, 0.82f, 0.32f), log);

            // 2. 씬 오브젝트
            BuildingSystem sys = Object.FindAnyObjectByType<BuildingSystem>(FindObjectsInactive.Include);
            if (sys == null)
            {
                var go = new GameObject("BuildingSystem");
                Undo.RegisterCreatedObjectUndo(go, "건축 시스템 만들기");
                sys = go.AddComponent<BuildingSystem>();
                log.Add("씬에 BuildingSystem 오브젝트를 만들었어요");
            }
            BuildingPlacer placer = sys.GetComponent<BuildingPlacer>();
            if (placer == null) placer = Undo.AddComponent<BuildingPlacer>(sys.gameObject);

            Undo.RecordObject(sys, "건축 시스템 만들기");
            sys.ghostMaterial = ghost;
            sys.fenceMaterial = fence;
            sys.signMaterial = sign;
            EditorUtility.SetDirty(sys);

            if (LayerMask.NameToLayer(sys.blockerLayerName) < 0)
                log.Add($"⚠ '{sys.blockerLayerName}' 레이어가 없어요. 건물 충돌체가 Default 레이어로 들어가요 (밭 갈기 막기는 칸 점유로 따로 막혀서 큰 문제는 없음)");

            // 3. 안내 글자
            TMP_Text hint = MakeHint(log);
            if (hint != null)
            {
                var so = new SerializedObject(placer);
                so.FindProperty("hintText").objectReferenceValue = hint;
                so.ApplyModifiedProperties();
            }

            // 4. 건물 데이터 + 키트
            var kits = new List<BuildingKitData>();
            foreach (var d in Defaults)
            {
                BuildingKitData kit = MakeBuilding(d.prefab, d.name, d.desc, d.fp, log);
                if (kit != null) kits.Add(kit);
            }

            EditorSceneManager.MarkSceneDirty(sys.gameObject.scene);
            AssetDatabase.SaveAssets();

            // 5. 테스트용 시작 아이템
            if (kits.Count > 0 && EditorUtility.DisplayDialog("건축 시스템 만들기",
                    $"테스트용으로 건물 키트 {kits.Count}개를 시작 아이템(가방 21번째 칸부터)에 넣을까요?\n(InventoryManager의 Starting Items에 추가돼요. 나중에 지워도 됨)",
                    "넣기", "안 넣기"))
                AddStartingKits(kits, log);

            Selection.activeObject = sys.gameObject;
            Debug.Log("[건축 시스템 만들기] 완료\n  " + string.Join("\n  ", log));
            EditorUtility.DisplayDialog("건축 시스템 만들기",
                "완료!\n\n" + string.Join("\n", log) +
                "\n\n플레이 → 핫바에서 건물 키트 고르기 → 좌클릭으로 설치 모드", "확인");
        }

        // ---------- 건물 1종 ----------

        private static BuildingKitData MakeBuilding(string prefabName, string displayName, string desc, Vector2Int fp, List<string> log)
        {
            GameObject prefab = FindPrefab(prefabName);
            if (prefab == null)
            {
                log.Add($"⚠ '{prefabName}' 프리팹을 못 찾았어요 (unitypackage를 먼저 Import 해 주세요) → 건너뜀");
                return null;
            }

            EnsureFolder(DataFolder);
            EnsureFolder(KitFolder);

            // Building Data (이미 있으면 사용자가 고친 값 유지, 모델만 비어 있으면 채움)
            string dataPath = $"{DataFolder}/Building_{prefabName}.asset";
            BuildingData data = AssetDatabase.LoadAssetAtPath<BuildingData>(dataPath);
            bool newData = data == null;
            if (newData)
            {
                data = ScriptableObject.CreateInstance<BuildingData>();
                data.displayName = displayName;
                data.description = desc;
                data.footprint = fp;
                data.buildDays = 1;
                AssetDatabase.CreateAsset(data, dataPath);
            }
            if (data.prefab == null) data.prefab = prefab;
            if (data.icon == null) data.icon = MakeIcon(prefab, prefabName);
            EditorUtility.SetDirty(data);

            // 재질 곡면 셰이더로 (처음 만들 때만)
            if (newData) ConvertToCurved(prefab, log);

            // 키트 아이템
            string kitPath = $"{KitFolder}/Kit_{prefabName}.asset";
            BuildingKitData kit = AssetDatabase.LoadAssetAtPath<BuildingKitData>(kitPath);
            if (kit == null)
            {
                kit = ScriptableObject.CreateInstance<BuildingKitData>();
                kit.itemName = $"{displayName} 키트";
                kit.description = desc;
                kit.category = ItemCategory.Material;
                kit.maxStack = 1;
                AssetDatabase.CreateAsset(kit, kitPath);
            }
            if (kit.building == null) kit.building = data;
            if (kit.icon == null) kit.icon = data.icon;
            EditorUtility.SetDirty(kit);

            log.Add(newData
                ? $"{displayName}: Building Data + 키트 만듦 ({fp.x}x{fp.y}칸)"
                : $"{displayName}: 이미 있어서 그대로 둠 (빠진 연결만 채움)");
            return kit;
        }

        private static GameObject FindPrefab(string name)
        {
            foreach (string guid in AssetDatabase.FindAssets($"{name} t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == name)
                    return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            return null;
        }

        // 프리팹이 쓰는 URP Lit 재질 → 곡면 셰이더 (곡면 월드 도구와 같은 방식, '되돌리기'로 원래대로 가능)
        private static void ConvertToCurved(GameObject prefab, List<string> log)
        {
            Shader curved = Shader.Find("FarmingSystem/Curved Lit");
            if (curved == null) return;
            foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null || m.shader == null) continue;
                    if (m.shader.name != "Universal Render Pipeline/Lit" && m.shader.name != "Universal Render Pipeline/Simple Lit") continue;
                    if (m.HasProperty("_Surface") && m.GetFloat("_Surface") > 0.5f) continue; // 반투명은 건너뜀
                    if (!AssetDatabase.Contains(m) || AssetDatabase.GetAssetPath(m).EndsWith(".fbx")) continue; // FBX 안 재질은 못 바꿈

                    Texture baseMap = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.mainTexture;
                    Color color = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
                    string original = m.shader.name;
                    Undo.RecordObject(m, "곡면 셰이더로 바꾸기");
                    m.shader = curved;
                    m.SetOverrideTag("CurvedWorldOriginalShader", original);
                    m.SetTexture("_BaseMap", baseMap);
                    m.SetColor("_BaseColor", color);
                    m.SetFloat("_Smoothness", 0f);
                    EditorUtility.SetDirty(m);
                    log.Add($"  재질 '{m.name}' → 곡면 셰이더");
                }
        }

        // 모델 미리보기 그림으로 임시 아이콘 만들기
        private static Sprite MakeIcon(GameObject prefab, string name)
        {
            try
            {
                Texture2D preview = null;
                for (int i = 0; i < 150 && preview == null; i++) // 최대 약 3초 기다림
                {
                    preview = AssetPreview.GetAssetPreview(prefab);
                    if (preview == null) System.Threading.Thread.Sleep(20);
                }
                if (preview == null) return null;

                var copy = new Texture2D(preview.width, preview.height, TextureFormat.RGBA32, false);
                copy.SetPixels(preview.GetPixels());
                copy.Apply();
                EnsureFolder(IconFolder);
                string path = $"{IconFolder}/Icon_{name}.png";
                File.WriteAllBytes(path, copy.EncodeToPNG());
                Object.DestroyImmediate(copy);
                AssetDatabase.ImportAsset(path);

                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled = false;
                imp.SaveAndReimport();
                return AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[건축 시스템 만들기] {name} 아이콘 만들기 실패 (아이콘 없이 진행): {e.Message}");
                return null;
            }
        }

        // ---------- 재질 ----------

        // 반투명 단색 (URP Unlit Transparent)
        private static Material MakeGhostMaterial(List<string> log)
        {
            EnsureFolder(MatFolder);
            string path = $"{MatFolder}/Building_Ghost.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;

            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) { log.Add("⚠ URP Unlit 셰이더를 못 찾아서 미리보기 재질을 못 만들었어요"); return null; }
            m = new Material(sh) { name = "Building_Ghost" };
            m.SetFloat("_Surface", 1f);   // Transparent
            m.SetFloat("_Blend", 0f);     // Alpha
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);      // 양면
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = 3000;
            m.SetColor("_BaseColor", new Color(0.4f, 1f, 0.55f, 0.45f));
            AssetDatabase.CreateAsset(m, path);
            log.Add("설치 미리보기 재질 만듦 (Building_Ghost)");
            return m;
        }

        // 공사장용 단색 (곡면 셰이더가 있으면 그걸로 → 맵처럼 휨)
        private static Material MakeLitMaterial(string name, Color color, List<string> log)
        {
            EnsureFolder(MatFolder);
            string path = $"{MatFolder}/{name}.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;

            Shader sh = Shader.Find("FarmingSystem/Curved Lit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            m = new Material(sh) { name = name };
            m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0f);
            AssetDatabase.CreateAsset(m, path);
            log.Add($"공사장 재질 만듦 ({name})");
            return m;
        }

        // ---------- 안내 글자 ----------

        private static TMP_Text MakeHint(List<string> log)
        {
            Canvas canvas = FindCanvas();
            if (canvas == null) { log.Add("⚠ 화면 Canvas를 못 찾아서 안내 글자는 안 만들었어요 (콘솔 로그로만 안내)"); return null; }

            Transform old = canvas.transform.Find("BuildingHint");
            if (old != null) return old.GetComponent<TMP_Text>();

            var go = new GameObject("BuildingHint", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "건축 안내 글자");
            go.transform.SetParent(canvas.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 190f); // 핫바 바로 위
            rt.sizeDelta = new Vector2(1200f, 110f);

            var t = go.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset font = FindFont();
            if (font != null) t.font = font;
            // 시계 HUD 글자 재질(테두리+그림자)이 같은 폰트면 같이 씀 → 밝은 배경에서도 잘 보임
            Material clockMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/UI/HUD/ClockHUD_Text.mat");
            if (font != null && clockMat != null && clockMat.mainTexture == font.material.mainTexture) t.fontSharedMaterial = clockMat;
            t.fontSize = 30f;
            t.alignment = TextAlignmentOptions.Bottom;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.richText = true;
            t.raycastTarget = false;
            t.text = "<b>내 우주선</b> 설치  —  <b>좌클릭</b> 설치 · <b>R</b> 회전 · <b>Esc</b>/<b>우클릭</b> 취소";
            go.SetActive(false); // 설치 모드일 때만 켜짐
            log.Add($"안내 글자 만듦 ('{canvas.name}' 안 BuildingHint)" + (font == null ? " ⚠ 한글 폰트를 못 찾음 → Font Asset을 한글 폰트로 바꿔 주세요" : ""));
            return t;
        }

        private static Canvas FindCanvas()
        {
            HotbarUI hotbar = Object.FindAnyObjectByType<HotbarUI>(FindObjectsInactive.Include);
            Canvas c = hotbar != null ? hotbar.GetComponentInParent<Canvas>(true) : null;
            if (c != null) return c.rootCanvas;
            foreach (Canvas any in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
                if (any.isRootCanvas && any.renderMode != RenderMode.WorldSpace) return any;
            return null;
        }

        private static TMP_FontAsset FindFont()
        {
            foreach (TextMeshProUGUI t in Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include))
                if (t.font != null && !t.font.name.StartsWith("LiberationSans")) return t.font;
            return null;
        }

        // ---------- 시작 아이템 ----------

        private static void AddStartingKits(List<BuildingKitData> kits, List<string> log)
        {
            InventoryManager inv = Object.FindAnyObjectByType<InventoryManager>(FindObjectsInactive.Include);
            if (inv == null) { log.Add("⚠ 씬에 InventoryManager가 없어서 시작 아이템은 못 넣었어요"); return; }

            var so = new SerializedObject(inv);
            SerializedProperty arr = so.FindProperty("startingItems");
            var used = new HashSet<int>();
            var already = new HashSet<Object>();
            for (int i = 0; i < arr.arraySize; i++)
            {
                SerializedProperty e = arr.GetArrayElementAtIndex(i);
                used.Add(e.FindPropertyRelative("slotIndex").intValue);
                already.Add(e.FindPropertyRelative("item").objectReferenceValue);
            }

            int slot = 20;
            int added = 0;
            foreach (BuildingKitData kit in kits)
            {
                if (already.Contains(kit)) continue;
                while (slot < 30 && used.Contains(slot)) slot++;
                if (slot >= 30) { log.Add("⚠ 가방 21~30번째 칸이 다 차서 일부 키트는 못 넣었어요"); break; }
                arr.arraySize++;
                SerializedProperty e = arr.GetArrayElementAtIndex(arr.arraySize - 1);
                e.FindPropertyRelative("slotIndex").intValue = slot;
                e.FindPropertyRelative("item").objectReferenceValue = kit;
                e.FindPropertyRelative("quantity").intValue = 1;
                used.Add(slot);
                added++;
            }
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(inv.gameObject.scene);
            log.Add(added > 0 ? $"시작 아이템에 키트 {added}개 추가 (가방 셋째 줄)" : "키트가 이미 시작 아이템에 들어 있어요");
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