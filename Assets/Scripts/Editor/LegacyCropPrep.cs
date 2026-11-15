#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace FarmingSystem.EditorTools
{
    // 기존 작물(A~T, 작물A~T.zip)을 '작물 모델 가져오기'가 읽을 수 있는 프리팹으로 바꿔 주는 도구 (에디터 전용)
    // 메뉴: Tools → 행성농사일지 → 작물 → 기존 작물(A~T) 준비하기
    // 왜 필요한가
    //  1. 이름 충돌: 기존 작물 A~T와 지금 작물 a~t는 윈도우에서 대소문자를 구분하지 않아서 Crop_A = Crop_a 같은 파일로 취급됨
    //     → 그대로 가져오면 지금 작물 a의 모델이 기존 작물 A로 덮어써짐. 그래서 이름 앞에 old_를 붙임 (old_A, old_A_1 ...)
    //  2. 기존 작물은 FBX만 있고 프리팹이 없음 → 프리팹으로 만듦 (작물 모델 가져오기는 프리팹을 찾음)
    //  3. K·L은 모델이 5개 (성장 4단계 + 수확물) → '작물 모델 가져오기'는 _4를 수확물로 보므로
    //     마지막 모델을 _4(수확물)로, 원래 _4를 _5(마지막 성장 단계)로 바꿔 줌
    // FBX 원본은 건드리지 않음 (새 프리팹만 만듦)
    public class LegacyCropPrep : EditorWindow
    {
        [SerializeField] private string sourceFolder = "Assets/Model/기존작물";
        [SerializeField] private string outputFolder = "Assets/Model/plant3";
        [SerializeField] private string prefix = "old_";
        private readonly List<string> log = new List<string>();
        private Vector2 scroll;

        [MenuItem("Tools/행성농사일지/작물/기존 작물(A~T) 준비하기")]
        private static void Open() => GetWindow<LegacyCropPrep>("기존 작물 준비").minSize = new Vector2(420f, 300f);

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("작물A~T.zip을 푼 FBX(A.fbx ~ T.fbx)를 '작물 모델 가져오기'용 프리팹으로 만들어요.\n" +
                                    "지금 작물(a~t)과 이름이 겹치지 않게 앞에 '" + prefix + "'를 붙이고, 모델 5개짜리(K·L)는 수확물 순서를 맞춰요.", MessageType.Info);
            sourceFolder = EditorGUILayout.TextField("기존 작물 FBX 폴더", sourceFolder);
            outputFolder = EditorGUILayout.TextField("프리팹 만들 폴더", outputFolder);
            prefix = EditorGUILayout.TextField("이름 앞에 붙일 글자", prefix);
            if (GUILayout.Button("프리팹 만들기", GUILayout.Height(32))) Run();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (string l in log) EditorGUILayout.LabelField(l, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndScrollView();
        }

        private void Run()
        {
            log.Clear();
            if (!AssetDatabase.IsValidFolder(sourceFolder)) { log.Add($"폴더가 없어요: {sourceFolder}"); return; }
            EnsureFolder(outputFolder);
            int done = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { sourceFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null) continue;
                string id = model.name;                       // A
                string newId = prefix + id;                   // old_A
                var stagePattern = new Regex("^" + Regex.Escape(id) + @"[_\s-]?(\d)$", RegexOptions.IgnoreCase);

                var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
                try
                {
                    PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    go.name = newId;
                    var stages = new SortedDictionary<int, Transform>();
                    foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                    {
                        Match m = stagePattern.Match(t.name);
                        if (m.Success) stages[int.Parse(m.Groups[1].Value)] = t;
                    }
                    if (stages.Count == 0) { log.Add($"· {id}: 단계 모델({id}_1 ...)이 없어서 건너뜀"); continue; }

                    // 번호 정리: 모델이 5개 이상이면 마지막 = 수확물(_4), 원래 _4부터는 성장 단계로 하나씩 뒤로
                    int last = 0;
                    foreach (int k in stages.Keys) last = k;
                    string note = "";
                    foreach (var kv in stages)
                    {
                        int n = kv.Key;
                        if (last >= 5) n = kv.Key == last ? 4 : kv.Key >= 4 ? kv.Key + 1 : kv.Key;
                        kv.Value.name = $"{newId}_{n}";
                        if (n != kv.Key) note += $" {id}_{kv.Key}→_{n}";
                    }

                    string outPath = $"{outputFolder}/{newId}.prefab";
                    PrefabUtility.SaveAsPrefabAsset(go, outPath);
                    log.Add($"✔ {id} → {newId} (모델 {stages.Count}개{(note.Length > 0 ? ", 순서 바꿈:" + note : "")})");
                    done++;
                }
                finally
                {
                    DestroyImmediate(go);
                }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            log.Insert(0, $"완료: {done}종 → {outputFolder}\n다음: Tools → 행성농사일지 → 작물 모델 가져오기 의 작물 모델 폴더에 ;{outputFolder} 를 추가하고 실행");
            Debug.Log("[기존 작물 준비] " + string.Join("\n", log));
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
#endif
