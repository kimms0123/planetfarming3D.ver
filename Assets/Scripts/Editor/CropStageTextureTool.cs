#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FarmingSystem.EditorTools
{
    // 작물 단계마다 다른 텍스처 재질 넣기 (그래픽 팀이 단계별 색 텍스처 A_1_color ~ A_4_color를 줬을 때)
    // 메뉴: Tools → 행성농사일지 → 작물 → 단계별 텍스처 재질 넣기
    //
    //  - 지금 작물 프리팹은 4단계가 모두 FBX 안의 재질 1개를 같이 써서, 단계별 텍스처가 적용이 안 돼 있음
    //  - 이 도구: 모델 폴더(plant3)의 "글자_단계_color.png"로 단계별 재질 4개를 만들고 (우리 곡면 셰이더 FarmingSystem/Curved Lit)
    //            작물 프리팹 폴더(Crops/Prefabs/old_A 등)의 stage1·stage2·stage3·harvest 프리팹에 1·2·3·4번 재질을 넣음
    //            모델 원본 프리팹(plant3/old_A.prefab)의 old_A_1 ~ old_A_4에도 같이 넣음
    //  - 모델(FBX)·텍스처 파일은 안 건드림. 재질은 Assets/Crops/Materials/(프리팹 폴더 이름)/ 에 만들어짐 (다시 누르면 덮어씀)
    //  기본값 = 유성파 (모델 글자 A, 프리팹 폴더 old_A)
    public class CropStageTextureTool : EditorWindow
    {
        private const string ModelFolder = "Assets/Art/Model/crops/plant3";
        private const string PrefabRoot = "Assets/Crops/Prefabs";
        private const string MaterialRoot = "Assets/Crops/Materials";

        [SerializeField] private string modelLetter = "A";
        [SerializeField] private string prefabFolder = "old_A";

        [MenuItem("Tools/행성농사일지/작물/단계별 텍스처 재질 넣기")]
        private static void Open() => GetWindow<CropStageTextureTool>("단계별 텍스처").minSize = new Vector2(360, 190);

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("모델 폴더(plant3)의 '글자_1_color.png ~ 글자_4_color.png'로 단계별 재질을 만들어\n" +
                                    "작물 프리팹의 stage1 · stage2 · stage3 · harvest에 차례로 넣어요.\n기본값 = 유성파", MessageType.Info);
            modelLetter = EditorGUILayout.TextField(new GUIContent("모델 글자", "plant3 폴더 텍스처 이름의 앞 글자 (A_1_color.png → A)"), modelLetter);
            prefabFolder = EditorGUILayout.TextField(new GUIContent("프리팹 폴더", "Assets/Crops/Prefabs 안의 폴더 이름 (유성파 = old_A)"), prefabFolder);
            EditorGUILayout.Space();
            if (GUILayout.Button("재질 만들고 넣기", GUILayout.Height(32))) Apply();
        }

        private void Apply()
        {
            var report = new List<string>();
            string matFolder = $"{MaterialRoot}/{prefabFolder}";
            EnsureFolder(matFolder);

            Shader shader = Shader.Find("FarmingSystem/Curved Lit");
            if (shader == null) { shader = Shader.Find("Universal Render Pipeline/Lit"); report.Add("! 곡면 셰이더(FarmingSystem/Curved Lit)가 없어서 URP Lit으로 만듦"); }

            // 1. 단계별 재질 4개
            var mats = new Material[4];
            for (int i = 0; i < 4; i++)
            {
                string texPath = $"{ModelFolder}/{modelLetter}_{i + 1}_color.png";
                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                if (tex == null) { report.Add($"✗ 텍스처 없음: {texPath}"); continue; }
                string matPath = $"{matFolder}/{prefabFolder}_{i + 1}_color.mat";
                Material m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, matPath); }
                else m.shader = shader;
                m.SetTexture("_BaseMap", tex);
                m.mainTexture = tex;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0f);
                EditorUtility.SetDirty(m);
                mats[i] = m;
            }
            AssetDatabase.SaveAssets();

            // 2. 작물 프리팹 (stage1 · stage2 · stage3 · harvest = 재질 1 · 2 · 3 · 4)
            string[] stages = { "stage1", "stage2", "stage3", "harvest" };
            for (int i = 0; i < stages.Length; i++)
            {
                string path = $"{PrefabRoot}/{prefabFolder}/{prefabFolder}_{stages[i]}.prefab";
                if (mats[i] == null) continue;
                report.Add(SetPrefabMaterial(path, mats[i], null));
            }

            // 3. 모델 원본 프리팹 (plant3/old_A.prefab의 old_A_1 ~ old_A_4)
            string modelPrefab = $"{ModelFolder}/{prefabFolder}.prefab";
            if (File.Exists(modelPrefab))
                for (int i = 0; i < 4; i++)
                    if (mats[i] != null) report.Add(SetPrefabMaterial(modelPrefab, mats[i], $"{prefabFolder}_{i + 1}"));

            AssetDatabase.SaveAssets();
            string msg = string.Join("\n", report);
            Debug.Log("[단계별 텍스처]\n" + msg);
            EditorUtility.DisplayDialog("단계별 텍스처", msg + "\n\n이미 심어 둔 작물은 다음 단계로 자랄 때(또는 다시 심을 때) 바뀌어요", "확인");
        }

        // 프리팹 안의 메쉬 렌더러 재질을 바꿈 (childName이 있으면 그 이름의 오브젝트만)
        private static string SetPrefabMaterial(string path, Material mat, string childName)
        {
            if (!File.Exists(path)) return $"✗ 프리팹 없음: {path}";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            int changed = 0;
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (childName != null && r.gameObject.name != childName) continue;
                var arr = r.sharedMaterials;
                for (int k = 0; k < arr.Length; k++) arr[k] = mat;
                r.sharedMaterials = arr;
                changed++;
            }
            if (changed > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
            string label = Path.GetFileNameWithoutExtension(path) + (childName != null ? $" / {childName}" : "");
            return changed > 0 ? $"✓ {label} ← {mat.name}" : $"✗ {label}: 메쉬를 못 찾음";
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