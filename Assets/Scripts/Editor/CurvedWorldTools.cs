using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FarmingSystem.World;

namespace FarmingSystem.EditorTools
{
    // 곡면 월드 설정 도구
    // 메뉴: Tools → 행성농사일지 → 곡면 월드 → ...
    //  1. 재질을 곡면 셰이더로 바꾸기  : Assets 안의 URP Lit / Simple Lit 재질을 "FarmingSystem/Curved Lit"로 (텍스처·색 유지)
    //  2. 물 셰이더 곡면 버전 만들기    : Uber Stylized Water 셰이더를 복사해서 휘는 계산을 넣은 버전을 만들고 물 재질을 그걸로
    //  3. 씬에 곡면 월드 조절기 놓기    : 휘는 세기를 정하는 CurvedWorldController
    //  되돌리기                        : 1·2번으로 바꾼 재질을 원래 셰이더로
    // 원래 셰이더 이름은 재질에 기록해 둬서 언제든 되돌릴 수 있음
    public static class CurvedWorldTools
    {
        private const string CurvedShaderName = "FarmingSystem/Curved Lit";
        private const string OriginalTag = "CurvedWorldOriginalShader";
        private const string WaterShaderName = "UberStylizedWater";
        private const string CurvedWaterShaderName = "UberStylizedWater (Curved)";

        private static readonly string[] ConvertibleShaders =
        {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Simple Lit",
        };

        // ---------------- 1. 재질 바꾸기 ----------------
        [MenuItem("Tools/행성농사일지/곡면 월드/1. 재질을 곡면 셰이더로 바꾸기")]
        private static void ConvertMaterials()
        {
            Shader curved = Shader.Find(CurvedShaderName);
            if (curved == null)
            {
                EditorUtility.DisplayDialog("곡면 월드", "CurvedLit.shader를 찾지 못했어요. 프로젝트에 넣었는지 확인하세요.", "확인");
                return;
            }

            var targets = new List<Material>();
            var skippedTransparent = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == null) continue;
                if (System.Array.IndexOf(ConvertibleShaders, mat.shader.name) < 0) continue;
                // 반투명 재질은 곡면 셰이더(불투명 전용)로 바꾸면 깨지므로 건너뜀
                if (mat.HasProperty("_Surface") && mat.GetFloat("_Surface") > 0.5f) { skippedTransparent.Add(path); continue; }
                targets.Add(mat);
            }

            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog("곡면 월드", "바꿀 URP Lit 재질이 없어요 (이미 바꿨거나 다른 셰이더를 쓰는 중).", "확인");
                return;
            }
            if (!EditorUtility.DisplayDialog("곡면 월드",
                    $"URP Lit 재질 {targets.Count}개를 곡면 셰이더로 바꿀까요?\n(텍스처·색은 그대로, 나중에 '되돌리기'로 원래대로 돌릴 수 있어요)",
                    "바꾸기", "취소"))
                return;

            foreach (Material mat in targets)
            {
                Undo.RecordObject(mat, "곡면 셰이더로 바꾸기");
                // 바꾸기 전 값 기억 (속성 이름이 같아서 대부분 그대로 이어지지만, 확실히 하려고 다시 넣음)
                Texture baseMap = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap") : mat.mainTexture;
                Vector2 scale = mat.HasProperty("_BaseMap") ? mat.GetTextureScale("_BaseMap") : Vector2.one;
                Vector2 offset = mat.HasProperty("_BaseMap") ? mat.GetTextureOffset("_BaseMap") : Vector2.zero;
                Color color = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : Color.white;
                float clip = mat.HasProperty("_AlphaClip") ? mat.GetFloat("_AlphaClip") : 0f;
                float cutoff = mat.HasProperty("_Cutoff") ? mat.GetFloat("_Cutoff") : 0.5f;
                float cull = mat.HasProperty("_Cull") ? mat.GetFloat("_Cull") : 2f;
                string original = mat.shader.name;

                mat.shader = curved;
                mat.SetOverrideTag(OriginalTag, original);
                mat.SetTexture("_BaseMap", baseMap);
                mat.SetTextureScale("_BaseMap", scale);
                mat.SetTextureOffset("_BaseMap", offset);
                mat.SetColor("_BaseColor", color);
                mat.SetFloat("_AlphaClip", clip);
                mat.SetFloat("_Cutoff", cutoff);
                mat.SetFloat("_Cull", cull);
                mat.SetFloat("_Smoothness", 0f);
                if (clip > 0.5f) mat.EnableKeyword("_ALPHATEST_ON"); else mat.DisableKeyword("_ALPHATEST_ON");
                EditorUtility.SetDirty(mat);
            }
            AssetDatabase.SaveAssets();

            string msg = $"[곡면 월드] 재질 {targets.Count}개를 곡면 셰이더로 바꿨어요.";
            if (skippedTransparent.Count > 0)
                msg += $"\n반투명 재질 {skippedTransparent.Count}개는 건너뜀 (곡면이 안 걸려요):\n  " + string.Join("\n  ", skippedTransparent);
            Debug.Log(msg);
            EnsureController(false);
        }

        // ---------------- 2. 물 셰이더 ----------------
        // Uber Stylized Water는 Shader Graph로 만들어진 셰이더라, 꼭짓점 위치를 정하는 줄(description.Position = ...)이 패스마다 있음
        // 원본은 그대로 두고 복사본을 만들어서, 그 줄마다 곡면 계산을 감싸 넣음
        [MenuItem("Tools/행성농사일지/곡면 월드/2. 물 셰이더 곡면 버전 만들기")]
        private static void MakeCurvedWater()
        {
            Shader water = Shader.Find(WaterShaderName);
            string path = water != null ? AssetDatabase.GetAssetPath(water) : null;
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".shader"))
            {
                EditorUtility.DisplayDialog("곡면 월드", "Uber Stylized Water 셰이더(.shader 파일)를 찾지 못했어요.", "확인");
                return;
            }

            string src = File.ReadAllText(path);
            string helper = @"
        // ---- 곡면 월드 (CurvedWorldTools가 넣음) ----
        float4 _CurveOrigin;
        float4 _CurveForward;
        float _CurveStrength;
        float _CurveSide;
        float _CurveFlat;
        float3 CurvedWorld_BendObject(float3 positionOS)
        {
            float3 ws = TransformObjectToWorld(positionOS);
            float2 d = ws.xz - _CurveOrigin.xz;
            float2 f = _CurveForward.xz;
            float z = dot(d, f);
            float x = d.x * f.y - d.y * f.x;
            float r = max(sqrt(z * z + _CurveSide * x * x) - _CurveFlat, 0);
            ws.y -= r * r * _CurveStrength;
            return TransformWorldToObject(ws);
        }
";
            int functions = 0, positions = 0;
            string patched = Regex.Replace(src, @"(\n\s*)VertexDescription VertexDescriptionFunction\(", m =>
            {
                functions++;
                return helper + m.Value;
            });
            patched = Regex.Replace(patched, @"description\.Position = ([^;]+);", m =>
            {
                positions++;
                return $"description.Position = CurvedWorld_BendObject({m.Groups[1].Value});";
            });
            patched = Regex.Replace(patched, "^Shader \"" + Regex.Escape(WaterShaderName) + "\"", "Shader \"" + CurvedWaterShaderName + "\"", RegexOptions.Multiline);

            if (functions == 0 || positions == 0 || functions != positions)
            {
                EditorUtility.DisplayDialog("곡면 월드",
                    $"물 셰이더 구조가 예상과 달라서 곡면 버전을 만들지 못했어요.\n(꼭짓점 함수 {functions}개, 위치 줄 {positions}개)", "확인");
                return;
            }

            string outPath = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + "_Curved.shader").Replace('\\', '/');
            File.WriteAllText(outPath, patched);
            AssetDatabase.ImportAsset(outPath);
            Shader curvedWater = AssetDatabase.LoadAssetAtPath<Shader>(outPath);
            if (curvedWater == null) { Debug.LogError("[곡면 월드] 물 셰이더 곡면 버전을 불러오지 못했어요: " + outPath); return; }

            // 원래 물 셰이더를 쓰던 재질을 곡면 버전으로
            int changed = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (mat == null || mat.shader != water) continue;
                Undo.RecordObject(mat, "물 곡면 셰이더");
                int queue = mat.renderQueue;
                mat.shader = curvedWater;
                mat.renderQueue = queue;
                mat.SetOverrideTag(OriginalTag, WaterShaderName);
                EditorUtility.SetDirty(mat);
                changed++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[곡면 월드] 물 셰이더 곡면 버전을 만들었어요: {outPath} (패스 {functions}개에 적용), 물 재질 {changed}개를 곡면 버전으로 바꿈");
            EnsureController(false);
        }

        // ---------------- 3. 조절기 ----------------
        [MenuItem("Tools/행성농사일지/곡면 월드/3. 씬에 곡면 월드 조절기 놓기")]
        private static void PlaceController() => EnsureController(true);

        private static void EnsureController(bool select)
        {
            CurvedWorldController c = Object.FindAnyObjectByType<CurvedWorldController>(FindObjectsInactive.Include);
            if (c == null)
            {
                var go = new GameObject("CurvedWorld");
                Undo.RegisterCreatedObjectUndo(go, "곡면 월드 조절기");
                c = go.AddComponent<CurvedWorldController>();
                EditorSceneManager.MarkSceneDirty(go.scene);
                Debug.Log("[곡면 월드] 씬에 CurvedWorld 조절기를 놓았어요. Strength로 휘는 세기를 조절하세요");
            }
            if (select) Selection.activeGameObject = c.gameObject;
        }

        // ---------------- 분홍색 고치기 ----------------
        // 셰이더 파일을 지웠다가 다시 넣으면 재질이 셰이더를 잃어버려 분홍색이 됨 → 다시 연결
        // 셰이더 자체에 오류가 있어서 분홍색인 경우도 찾아서 알려줌
        [MenuItem("Tools/행성농사일지/곡면 월드/분홍색 재질 고치기")]
        private static void RepairPink()
        {
            Shader curved = Shader.Find(CurvedShaderName);
            Shader curvedWater = Shader.Find(CurvedWaterShaderName);
            Shader water = Shader.Find(WaterShaderName);
            var report = new List<string>();

            // 1) 셰이더 자체 오류 검사
            foreach (Shader sh in new[] { curved, curvedWater })
                if (sh != null && ShaderUtil.ShaderHasError(sh))
                    report.Add($"⚠ 셰이더 '{sh.name}'에 오류가 있어요 → Project에서 그 셰이더 파일을 선택하면 Inspector에 오류 내용이 나와요 (그 내용을 보내주세요)");

            // 2) 셰이더를 잃어버린 재질 다시 연결
            int fixedCount = 0;
            var stillBroken = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;
                bool broken = mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader" || !mat.shader.isSupported;
                if (!broken) continue;

                string original = mat.GetTag(OriginalTag, false, "");
                Shader target = null;
                if (original == WaterShaderName) target = curvedWater != null ? curvedWater : water;
                else if (!string.IsNullOrEmpty(original) || path.StartsWith("Assets/IslandGenerated")) target = curved;

                if (target == null) { stillBroken.Add(path); continue; }
                Undo.RecordObject(mat, "분홍색 재질 고치기");
                int queue = mat.renderQueue;
                mat.shader = target;
                if (target == water || target == curvedWater) mat.renderQueue = queue;
                if (mat.HasProperty("_AlphaClip") && mat.GetFloat("_AlphaClip") > 0.5f) mat.EnableKeyword("_ALPHATEST_ON");
                EditorUtility.SetDirty(mat);
                fixedCount++;
            }
            AssetDatabase.SaveAssets();

            report.Insert(0, $"[곡면 월드] 분홍색 재질 {fixedCount}개를 다시 연결했어요");
            if (stillBroken.Count > 0)
                report.Add($"원래 셰이더를 몰라서 못 고친 재질 {stillBroken.Count}개 (직접 Shader를 골라 주세요):\n  " + string.Join("\n  ", stillBroken));
            Debug.Log(string.Join("\n", report));
        }

        // ---------------- 되돌리기 ----------------
        [MenuItem("Tools/행성농사일지/곡면 월드/되돌리기 (원래 셰이더로)")]
        private static void Revert()
        {
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (mat == null) continue;
                string original = mat.GetTag(OriginalTag, false, "");
                if (string.IsNullOrEmpty(original)) continue;
                Shader s = Shader.Find(original);
                if (s == null) continue;
                Undo.RecordObject(mat, "곡면 셰이더 되돌리기");
                int queue = mat.renderQueue;
                mat.shader = s;
                mat.renderQueue = queue;
                mat.SetOverrideTag(OriginalTag, "");
                if (mat.HasProperty("_AlphaClip") && mat.GetFloat("_AlphaClip") > 0.5f) mat.EnableKeyword("_ALPHATEST_ON");
                EditorUtility.SetDirty(mat);
                count++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[곡면 월드] 재질 {count}개를 원래 셰이더로 되돌렸어요");
        }
    }
}