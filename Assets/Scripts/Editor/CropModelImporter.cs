#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using FarmingSystem.Farming;
using FarmingSystem.Inventory;

namespace FarmingSystem.EditorTools
{
    // 그래픽 팀 작물 모델 → 게임용 프리팹·데이터 자동 생성 도구
    // 메뉴: Tools → 행성농사일지 → 작물 모델 가져오기
    //
    // 그래픽 팀 작물 프리팹(예: Assets/plant1/a.prefab)에는 모델이 4개(a_1 ~ a_4) 같이 들어 있다
    //   _1 씨앗 → _2 새싹 → _3 다 자란 모습  = 성장 단계
    //   _4 수확물 (뽑았을 때 나오는 열매·결정)
    // 이 도구가 하는 일:
    //   1) 단계마다 프리팹을 따로 만듦 (발밑 가운데가 기준점, 다 자란 모습 높이를 기준으로 크기 통일)
    //   2) 수확물·씨앗 아이콘 PNG를 모델로 찍어서 만듦 (인벤토리·상점에 쓰는 아이콘)
    //   3) 작물마다 CropData(수확물)와 SeedData(씨앗) 에셋을 만들고 프리팹·아이콘을 연결
    // 이미 만든 CropData/SeedData가 있으면 모델·아이콘 칸만 갱신하고, 이름·계절·가격 등 직접 바꾼 값은 그대로 둠
    public class CropModelImporter : EditorWindow
    {
        [SerializeField] private string sourceFolders = "Assets/Model/plant1;Assets/Model/plant2";
        // 만들어질 위치 (폴더가 없으면 자동으로 만듦)
        [SerializeField] private string cropDataFolder = "Assets/Crops/Harvest";   // 작물(수확물) CropData
        [SerializeField] private string seedDataFolder = "Assets/Crops/Seed";      // 씨앗 SeedData
        [SerializeField] private string prefabFolderRoot = "Assets/Crops/Prefabs"; // 단계별 모델 프리팹
        [SerializeField] private string iconFolder = "Assets/Crops/Icons";         // 아이콘 PNG
        // 크기 기준 (밭에 심었을 때 실제 크기, 단위 m)
        [SerializeField] private float matureHeightMeters = 0.9f;   // 다 자란 모습 높이
        [SerializeField] private float maxWidthRatio = 0.8f;        // 폭 최대치 (밭 한 칸 대비)
        [SerializeField] private float plantedMultiplier = 2f;      // 밭에 심을 때 곱해지는 배율 (씬에서 읽음)
        [SerializeField] private float tileSizeMeters = 1.94f;      // 밭 한 칸 크기 (씬에서 읽음)
        [SerializeField] private float playerHeightMeters = 2f;     // 비교용 캐릭터 키 (씬에서 읽음)

        // 흙에 묻히는 깊이
        // 고정 깊이 방식(기본): 모든 작물·단계를 밭에서 같은 깊이(m)만큼 묻음. 제외 목록에 있는 작물은 안 묻음
        [SerializeField] private bool useFixedSink = true;
        [SerializeField] private float fixedSinkMeters = 0.13f;
        [SerializeField] private string sinkExcludeIds = "e,i,j";
        // 비율 방식: 각 단계 모델 높이 대비 비율
        [SerializeField] private float seedSink = 0.45f;    // _1 씨앗: 절반쯤 묻혀서 빼꼼
        [SerializeField] private float sproutSink = 0.12f;  // _2 새싹: 뿌리 쪽만
        [SerializeField] private float matureSink = 0.05f;  // _3 다 자람: 밑동만
        [SerializeField] private bool makeIcons = true;
        [SerializeField] private int iconSize = 256;

        private Vector2 scroll;
        private readonly List<string> log = new List<string>();

        [MenuItem("Tools/행성농사일지/작물 모델 가져오기")]
        private static void Open() => GetWindow<CropModelImporter>("작물 모델 가져오기");

        private void OnEnable() => ReadSceneScale(false);

        // 지금 열린 씬에서 밭 칸 크기·작물 배율·캐릭터 키를 읽음
        // 작물이 밭에 심어질 때 크기 = 프리팹 크기 x (밭 블록 크기 x 작물 배율)  ← FarmTile.Plant 계산과 같음
        private void ReadSceneScale(bool verbose)
        {
            FarmTile tile = FindAnyObjectByType<FarmTile>(FindObjectsInactive.Include);
            if (tile != null)
            {
                var so = new SerializedObject(tile);
                SerializedProperty mult = so.FindProperty("cropScaleMultiplier");
                plantedMultiplier = tile.transform.lossyScale.y * (mult != null ? mult.floatValue : 1f);
                Collider col = tile.GetComponentInChildren<Collider>(true);
                if (col != null && col.bounds.size.x > 0.01f) tileSizeMeters = Mathf.Max(col.bounds.size.x, col.bounds.size.z);
            }
            FarmGrid grid = FindAnyObjectByType<FarmGrid>(FindObjectsInactive.Include);
            if (grid != null)
            {
                SerializedProperty ts = new SerializedObject(grid).FindProperty("tileSize");
                if (ts != null && ts.floatValue > 0.01f) tileSizeMeters = ts.floatValue;
            }
            CharacterController cc = FindAnyObjectByType<CharacterController>(FindObjectsInactive.Include);
            if (cc != null) playerHeightMeters = cc.height * cc.transform.lossyScale.y;

            if (verbose)
                log.Insert(0, tile != null
                    ? $"씬에서 읽음: 밭 한 칸 {tileSizeMeters:0.##}m, 심을 때 배율 x{plantedMultiplier:0.##}, 캐릭터 {playerHeightMeters:0.##}m"
                    : "⚠ 씬에 밭(FarmTile)이 없어서 기본값 사용. 맵을 생성한 씬을 열고 다시 읽어주세요");
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "그래픽 팀 작물 프리팹(a_1~a_4처럼 단계 모델 4개가 들어 있는 것)을 찾아서\n" +
                "· 단계별 프리팹 (_1 씨앗, _2 새싹, _3 다 자람, _4 수확물)\n" +
                "· 수확물·씨앗 아이콘\n" +
                "· CropData / SeedData 에셋\n" +
                "을 자동으로 만들어요. 이미 있는 데이터는 모델·아이콘만 갱신하고 직접 바꾼 값은 유지해요.",
                MessageType.Info);

            sourceFolders = EditorGUILayout.TextField(new GUIContent("작물 모델 폴더", "여러 개는 ; 로 구분"), sourceFolders);
            EditorGUILayout.LabelField("만들 위치", EditorStyles.boldLabel);
            cropDataFolder = EditorGUILayout.TextField(new GUIContent("작물 데이터 (CropData)"), cropDataFolder);
            seedDataFolder = EditorGUILayout.TextField(new GUIContent("씨앗 데이터 (SeedData)"), seedDataFolder);
            prefabFolderRoot = EditorGUILayout.TextField(new GUIContent("단계별 프리팹", "작물마다 하위 폴더(a, b, ...)가 생김"), prefabFolderRoot);
            iconFolder = EditorGUILayout.TextField(new GUIContent("아이콘"), iconFolder);
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("크기 기준 (밭에 심었을 때 실제 크기)", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"밭 한 칸 {tileSizeMeters:0.##}m · 심을 때 배율 x{plantedMultiplier:0.##} · 캐릭터 키 {playerHeightMeters:0.##}m", EditorStyles.miniLabel);
                if (GUILayout.Button("씬에서 다시 읽기", GUILayout.Width(110))) ReadSceneScale(true);
            }
            matureHeightMeters = EditorGUILayout.Slider(new GUIContent("다 자란 높이 (m)", "다 자란 모습(_3)을 밭에 심었을 때의 실제 높이. 다른 단계도 같은 비율로 맞춤"), matureHeightMeters, 0.2f, 3f);
            EditorGUILayout.LabelField($"   → 캐릭터 키의 {matureHeightMeters / Mathf.Max(0.01f, playerHeightMeters) * 100f:0}%", EditorStyles.miniLabel);
            maxWidthRatio = EditorGUILayout.Slider(new GUIContent("최대 폭 (밭 한 칸 대비)", "작물이 옆 칸까지 넘어가지 않게, 폭이 이보다 넓으면 그만큼 더 작게 만듦"), maxWidthRatio, 0.3f, 1.2f);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("흙에 묻히는 깊이", EditorStyles.boldLabel);
            useFixedSink = EditorGUILayout.Toggle(new GUIContent("고정 깊이 (m)", "켜면 모든 작물을 밭에서 같은 깊이만큼 묻음. 끄면 단계별 비율로 묻음"), useFixedSink);
            if (useFixedSink)
            {
                fixedSinkMeters = EditorGUILayout.Slider(new GUIContent("   묻는 깊이 (m)", "밭에 심었을 때 흙 표면 아래로 내려가는 깊이. 0.13 = 땅 높이 4.0에서 3.87"), fixedSinkMeters, 0f, 0.5f);
                sinkExcludeIds = EditorGUILayout.TextField(new GUIContent("   안 묻을 작물", "쉼표로 구분 (예: e,i,j). 이 작물은 흙 위에 그대로 섬"), sinkExcludeIds);
            }
            else
            {
                seedSink = EditorGUILayout.Slider(new GUIContent("   씨앗 (_1)", "모델 높이 대비. 0 = 흙 위에 얹힘, 0.5 = 절반이 흙 속"), seedSink, 0f, 0.8f);
                sproutSink = EditorGUILayout.Slider(new GUIContent("   새싹 (_2)"), sproutSink, 0f, 0.5f);
                matureSink = EditorGUILayout.Slider(new GUIContent("   다 자람 (_3)"), matureSink, 0f, 0.3f);
            }
            makeIcons = EditorGUILayout.Toggle(new GUIContent("아이콘 만들기", "수확물(_4)과 씨앗(_1) 모델을 찍어서 아이콘 PNG 생성"), makeIcons);
            if (makeIcons) iconSize = EditorGUILayout.IntPopup("아이콘 크기", iconSize, new[] { "128", "256", "512" }, new[] { 128, 256, 512 });

            EditorGUILayout.Space(6);
            if (GUILayout.Button("실행", GUILayout.Height(34))) Run();
            if (GUILayout.Button(new GUIContent("씬에 크기 비교 배치", "만든 작물의 다 자란 모습을 플레이어 앞에 밭에 심은 크기 그대로 한 줄로 세움 (확인 후 CropSizePreview 오브젝트를 지우면 됨)"))) PlacePreview();

            EditorGUILayout.Space(6);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (string line in log) EditorGUILayout.LabelField(line, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndScrollView();
        }

        // =====================================================================

        private void Run()
        {
            log.Clear();
            var folders = new List<string>();
            foreach (string f in sourceFolders.Split(';'))
            {
                string t = f.Trim().TrimEnd('/');
                if (t.Length == 0) continue;
                if (AssetDatabase.IsValidFolder(t)) { folders.Add(t); continue; }

                // 경로가 다르면(폴더를 옮긴 경우) 같은 이름의 폴더를 프로젝트에서 찾아봄
                string found = FindFolderByName(Path.GetFileName(t));
                if (found != null) { folders.Add(found); log.Add($"폴더 위치가 달라서 '{found}'를 사용"); }
                else log.Add($"⚠ 폴더 없음: {t}");
            }
            if (folders.Count == 0) { log.Add("작물 모델 폴더를 찾지 못했어요. 패키지를 가져왔는지, 폴더 이름이 맞는지 확인해 주세요."); return; }

            EnsureFolder(prefabFolderRoot);
            EnsureFolder(cropDataFolder);
            EnsureFolder(seedDataFolder);
            if (makeIcons) EnsureFolder(iconFolder);

            string[] guids = AssetDatabase.FindAssets("t:Prefab", folders.ToArray());
            int done = 0;
            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null) continue;
                    EditorUtility.DisplayProgressBar("작물 모델 가져오기", prefab.name, (float)i / guids.Length);
                    try
                    {
                        if (ProcessCrop(prefab)) done++;
                    }
                    catch (Exception e)
                    {
                        log.Add($"✖ {prefab.name}: {e.Message}");
                        Debug.LogException(e);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            log.Insert(0, $"완료: 작물 {done}종 → 작물 {cropDataFolder} / 씨앗 {seedDataFolder}");
            Debug.Log($"[작물 모델 가져오기] 작물 {done}종 처리 완료 → 작물 {cropDataFolder}, 씨앗 {seedDataFolder}, 프리팹 {prefabFolderRoot}");
        }

        // 작물 하나 처리. 단계 모델(이름_1 ~ 이름_4)이 없으면 건너뜀
        private bool ProcessCrop(GameObject source)
        {
            string id = source.name;
            var stagePattern = new Regex("^" + Regex.Escape(id) + @"[_\s-]?(\d)$", RegexOptions.IgnoreCase);

            GameObject temp = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                var stages = new SortedDictionary<int, Transform>();
                foreach (Transform t in temp.GetComponentsInChildren<Transform>(true))
                {
                    Match m = stagePattern.Match(t.name);
                    if (m.Success && t.GetComponentInChildren<Renderer>(true) != null)
                        stages[int.Parse(m.Groups[1].Value)] = t;
                }
                if (stages.Count == 0) return false; // 작물 프리팹이 아님 (조용히 건너뜀)

                // 크기 기준: 다 자란 모습(3단계, 없으면 가장 높은 성장 단계)
                Transform reference = stages.ContainsKey(3) ? stages[3] : null;
                if (reference == null) foreach (var kv in stages) if (kv.Key <= 3) reference = kv.Value;
                if (reference == null) foreach (var kv in stages) { reference = kv.Value; break; }
                // 1) 키: 다 자란 모습이 밭에서 matureHeightMeters가 되게
                float refHeight = Mathf.Max(0.0001f, WorldBounds(reference).size.y);
                float mult = Mathf.Max(0.0001f, plantedMultiplier);
                float scale = matureHeightMeters / (refHeight * mult);

                // 2) 폭: 성장 단계 중 가장 넓은 것이 밭 한 칸의 maxWidthRatio를 넘지 않게
                float widest = 0f;
                foreach (var kv in stages)
                {
                    if (kv.Key == 4) continue; // 수확물은 밭에 안 서 있으니 제외
                    Bounds wb = WorldBounds(kv.Value);
                    widest = Mathf.Max(widest, Mathf.Max(wb.size.x, wb.size.z));
                }
                bool widthLimited = false;
                if (widest > 0f)
                {
                    float widthScale = maxWidthRatio * tileSizeMeters / (widest * mult);
                    if (widthScale < scale) { scale = widthScale; widthLimited = true; }
                }
                float finalHeight = refHeight * scale * mult;
                float finalWidth = widest * scale * mult;

                string prefabFolder = $"{prefabFolderRoot}/{id}";
                EnsureFolder(prefabFolder);

                var growth = new List<GameObject>();
                GameObject harvested = null;
                foreach (var kv in stages)
                {
                    string label = kv.Key == 4 ? "harvest" : $"stage{kv.Key}";
                    // 묻는 깊이 (프리팹 안 단위). 수확물(_4)은 땅에 안 심어지니 0
                    float sinkLocal = 0f;
                    if (kv.Key != 4)
                    {
                        if (useFixedSink)
                        {
                            // 밭에서 fixedSinkMeters가 되도록 심을 때 배율만큼 나눔
                            if (!IsExcluded(id)) sinkLocal = fixedSinkMeters / mult;
                        }
                        else
                        {
                            float ratio = kv.Key == 1 ? seedSink : kv.Key == 2 ? sproutSink : matureSink;
                            sinkLocal = WorldBounds(kv.Value).size.y * scale * Mathf.Clamp01(ratio);
                        }
                    }
                    GameObject saved = SaveStagePrefab(kv.Value, scale, sinkLocal, $"{prefabFolder}/{id}_{label}.prefab", $"{id}_{label}");
                    if (kv.Key == 4) harvested = saved;
                    else growth.Add(saved);
                }
                if (growth.Count == 0 && harvested != null) growth.Add(harvested);

                Sprite cropIcon = null, seedIcon = null;
                if (makeIcons)
                {
                    GameObject cropIconModel = harvested != null ? harvested : growth[growth.Count - 1];
                    cropIcon = RenderIcon(cropIconModel, $"{iconFolder}/{id}_crop.png");
                    seedIcon = RenderIcon(growth[0], $"{iconFolder}/{id}_seed.png");
                }

                // CropData: 없으면 새로 만들고, 있으면 모델·아이콘만 갱신
                string cropPath = $"{cropDataFolder}/Crop_{id}.asset";
                CropData crop = AssetDatabase.LoadAssetAtPath<CropData>(cropPath);
                bool newCrop = crop == null;
                if (newCrop)
                {
                    crop = CreateInstance<CropData>();
                    crop.itemName = $"작물 {id.ToUpperInvariant()}";
                    crop.category = ItemCategory.Crop;
                    AssetDatabase.CreateAsset(crop, cropPath);
                }
                crop.growthStagePrefabs = growth.ToArray();
                crop.harvestedPrefab = harvested;
                if (cropIcon != null) crop.icon = cropIcon;
                EditorUtility.SetDirty(crop);

                // SeedData
                string seedPath = $"{seedDataFolder}/Seed_{id}.asset";
                SeedData seed = AssetDatabase.LoadAssetAtPath<SeedData>(seedPath);
                bool newSeed = seed == null;
                if (newSeed)
                {
                    seed = CreateInstance<SeedData>();
                    seed.itemName = $"{crop.itemName} 씨앗";
                    seed.category = ItemCategory.Seed;
                    AssetDatabase.CreateAsset(seed, seedPath);
                }
                if (seed.resultCrop == null) seed.resultCrop = crop;
                if (seedIcon != null) seed.icon = seedIcon;
                EditorUtility.SetDirty(seed);

                log.Add($"✔ {id}: 성장 {growth.Count}단계{(harvested != null ? " + 수확물" : "")}, 밭에서 높이 {finalHeight:0.00}m · 폭 {finalWidth:0.00}m (칸의 {finalWidth / tileSizeMeters * 100f:0}%)" +
                        (widthLimited ? " ← 폭이 넓어 키를 줄임" : "") +
                        $"{(newCrop ? ", CropData 새로 만듦" : ", CropData 갱신")}{(newSeed ? ", SeedData 새로 만듦" : "")}");
                return true;
            }
            finally
            {
                DestroyImmediate(temp);
            }
        }

        // 단계 모델 하나를 프리팹으로 저장
        // 구조: 루트(기준점 = 발밑 가운데, 크기 1) → Model(크기 배율) → 원본 모델
        // 루트 크기를 1로 두는 이유: 작물 스크립트가 단계 모델을 띄울 때 크기를 1로 되돌리기 때문
        // 안 묻을 작물 목록에 있는지 (대소문자 무시)
        private bool IsExcluded(string id)
        {
            foreach (string e in sinkExcludeIds.Split(','))
                if (string.Equals(e.Trim(), id, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // sinkLocal = 흙에 묻히는 깊이 (프리팹 단위). 기준점(흙 표면)보다 그만큼 아래로 내림
        private static GameObject SaveStagePrefab(Transform stage, float scale, float sinkLocal, string path, string name)
        {
            var root = new GameObject(name);
            try
            {
                var model = new GameObject("Model");
                model.transform.SetParent(root.transform, false);

                GameObject copy = Instantiate(stage.gameObject); // 원본 연결을 끊은 복사본 (재질은 그대로)
                copy.name = stage.name;
                copy.transform.SetParent(model.transform, false);
                copy.transform.localPosition = Vector3.zero;
                copy.transform.localRotation = stage.rotation; // 원본이 세워진 방향 유지
                copy.transform.localScale = stage.lossyScale;

                model.transform.localScale = Vector3.one * scale;

                // 발밑 가운데를 기준점으로
                Bounds b = WorldBounds(copy.transform);
                model.transform.position -= new Vector3(b.center.x, b.min.y, b.center.z);
                // 흙에 묻기 (기준점 = 흙 표면)
                model.transform.position -= Vector3.up * Mathf.Max(0f, sinkLocal);

                foreach (Collider c in root.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
                foreach (Animator a in root.GetComponentsInChildren<Animator>(true)) DestroyImmediate(a);

                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                DestroyImmediate(root);
            }
        }

        private static Bounds WorldBounds(Transform t)
        {
            bool found = false;
            Bounds b = new Bounds(t.position, Vector3.zero);
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
            {
                if (!found) { b = r.bounds; found = true; }
                else b.Encapsulate(r.bounds);
            }
            return b;
        }

        // ---------- 아이콘 ----------
        // 별도의 미리보기 씬에 모델·카메라·조명을 두고 찍음 (지금 열린 씬은 건드리지 않음)
        // 배경은 마젠타로 찍은 뒤 투명하게 지움 (렌더러가 알파를 안 남겨도 투명 배경이 되게)
        private Sprite RenderIcon(GameObject prefab, string path)
        {
            if (prefab == null) return null;
            Scene scene = EditorSceneManager.NewPreviewScene();
            RenderTexture rt = null;
            try
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                go.transform.rotation = Quaternion.Euler(0f, 25f, 0f);

                var camGO = new GameObject("IconCamera");
                SceneManager.MoveGameObjectToScene(camGO, scene);
                var cam = camGO.AddComponent<Camera>();
                cam.scene = scene;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(1f, 0f, 1f, 0f);
                cam.fieldOfView = 22f;
                cam.allowMSAA = false;

                AddLight(scene, Quaternion.Euler(40f, -30f, 0f), 1.2f);
                AddLight(scene, Quaternion.Euler(20f, 150f, 0f), 0.5f);

                Bounds b = WorldBounds(go.transform);
                float radius = Mathf.Max(0.01f, b.extents.magnitude);
                // 앞쪽 조금 위에서 내려다보는 각도로, 모델 전체가 화면에 꽉 차게
                Vector3 offsetDir = Quaternion.Euler(20f, 0f, 0f) * Vector3.back;
                float dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
                cam.transform.position = b.center + offsetDir * dist;
                cam.transform.LookAt(b.center);
                cam.nearClipPlane = Mathf.Max(0.01f, dist - radius * 2f);
                cam.farClipPlane = dist + radius * 2f;

                rt = RenderTexture.GetTemporary(iconSize, iconSize, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(iconSize, iconSize, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, iconSize, iconSize), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                cam.targetTexture = null;

                // 배경(마젠타) → 투명
                Color32[] px = tex.GetPixels32();
                for (int i = 0; i < px.Length; i++)
                {
                    Color32 c = px[i];
                    bool background = c.r > 220 && c.g < 40 && c.b > 220;
                    px[i] = background ? new Color32(0, 0, 0, 0) : new Color32(c.r, c.g, c.b, 255);
                }
                tex.SetPixels32(px);
                tex.Apply();

                File.WriteAllBytes(path, tex.EncodeToPNG());
                DestroyImmediate(tex);

                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
                return AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }
            catch (Exception e)
            {
                log.Add($"⚠ 아이콘 실패 ({Path.GetFileName(path)}): {e.Message}");
                return null;
            }
            finally
            {
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        // 만든 작물의 다 자란 모습(+씨앗·새싹)을 플레이어 앞에 밭에 심은 크기 그대로 세워서 비교
        private void PlacePreview()
        {
            ReadSceneScale(false);
            string[] guids = AssetDatabase.FindAssets("t:CropData", new[] { cropDataFolder });
            if (guids.Length == 0) { log.Insert(0, "⚠ 먼저 [실행]으로 작물을 만들어주세요"); return; }

            GameObject old = GameObject.Find("CropSizePreview (지워도 됨)");
            if (old != null) Undo.DestroyObjectImmediate(old);

            CharacterController player = FindAnyObjectByType<CharacterController>();
            Vector3 origin = player != null ? player.transform.position : Vector3.zero;
            Vector3 right = player != null ? player.transform.right : Vector3.right;
            Vector3 fwd = player != null ? player.transform.forward : Vector3.forward;

            var root = new GameObject("CropSizePreview (지워도 됨)");
            Undo.RegisterCreatedObjectUndo(root, "작물 크기 비교");
            int i = 0;
            foreach (string guid in guids)
            {
                var crop = AssetDatabase.LoadAssetAtPath<CropData>(AssetDatabase.GUIDToAssetPath(guid));
                if (crop == null || crop.growthStagePrefabs == null || crop.growthStagePrefabs.Length == 0) continue;

                // 한 줄에 작물 하나씩: 씨앗 · 새싹 · 다 자람 을 앞뒤로, 작물끼리는 칸 하나씩 띄워 옆으로
                Vector3 rowBase = origin + fwd * (tileSizeMeters * 2f) + right * ((i - guids.Length * 0.5f) * tileSizeMeters);
                for (int s = 0; s < crop.growthStagePrefabs.Length; s++)
                {
                    GameObject p = crop.growthStagePrefabs[s];
                    if (p == null) continue;
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(p, root.transform);
                    Vector3 pos = rowBase + fwd * (s * tileSizeMeters);
                    if (Physics.Raycast(pos + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 60f, ~0, QueryTriggerInteraction.Ignore))
                        pos.y = hit.point.y;
                    inst.transform.position = pos;
                    inst.transform.localScale = Vector3.one * plantedMultiplier; // 밭에 심을 때와 같은 배율
                    inst.name = $"{crop.itemName} ({s + 1}단계)";
                }
                i++;
            }
            Selection.activeGameObject = root;
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
            log.Insert(0, $"씬에 {i}종 배치 (플레이어 앞, 한 칸 간격). 확인 후 'CropSizePreview (지워도 됨)' 오브젝트를 지우세요");
        }

        private static void AddLight(Scene scene, Quaternion rotation, float intensity)
        {
            var lightGO = new GameObject("IconLight");
            SceneManager.MoveGameObjectToScene(lightGO, scene);
            lightGO.transform.rotation = rotation;
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
        }

        private static string FindFolderByName(string folderName)
        {
            if (string.IsNullOrEmpty(folderName)) return null;
            foreach (string guid in AssetDatabase.FindAssets(folderName))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(p) && Path.GetFileName(p) == folderName) return p;
            }
            return null;
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
#endif