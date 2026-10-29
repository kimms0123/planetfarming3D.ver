using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FarmingSystem.EditorTools
{
    // 경사 블록 모델을 "바닥부터 꼭대기까지 이어지는 경사로"로 고치는 도구
    // 그래픽팀 경사 블록(slanted block)은 낮은 쪽이 1m 높이의 수직 벽이고, 그 위로 1m → 2m 경사가 붙은 모양이라
    // 경사로 앞에 1m 턱이 생겨서 점프 없이는 못 올라감 (직선이었다가 경사로로 바뀌는 모양)
    // → 모델의 높이를 낮은 쪽으로 갈수록 눌러서, 0m(바닥) → 2m(꼭대기)로 곧게 올라가는 쐐기 모양으로 바꾼다
    //   (윗면 잔디 띠·모서리 둥글림·텍스처 칠은 그대로 따라가고, 낮은 쪽 수직 벽은 높이가 0이 되어 사라짐)
    // 원본 모델(block.fbx)은 건드리지 않고, 고친 메시를 새 파일(이름_Ramp.asset)로 저장해서 프리팹에 끼움
    //
    // 사용법: Project 창에서 경사 블록 프리팹들(spring_slanted block 등)을 선택 → Tools → 행성농사일지 → 경사로 모델 바닥까지 늘리기
    public static class RampMeshFixer
    {
        [MenuItem("Tools/행성농사일지/경사로 모델 바닥까지 늘리기 (선택한 프리팹)")]
        private static void FixSelected()
        {
            var made = new Dictionary<Mesh, Mesh>(); // 원본 메시 → 고친 메시 (계절 프리팹 4개가 같은 메시를 쓰므로 한 번만 만듦)
            int fixedCount = 0;
            var log = new List<string>();

            foreach (UnityEngine.Object o in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(o);
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    log.Add($"건너뜀: {o.name} (프리팹이 아님)");
                    continue;
                }

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    bool changed = false;
                    foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
                    {
                        Mesh src = mf.sharedMesh;
                        if (src == null) continue;

                        if (!made.TryGetValue(src, out Mesh fixedMesh))
                        {
                            fixedMesh = BuildRampMesh(src, out string why);
                            made[src] = fixedMesh;
                            if (fixedMesh == null)
                            {
                                log.Add($"건너뜀: {o.name} - {why}");
                                continue;
                            }
                            string folder = Path.GetDirectoryName(path).Replace('\\', '/');
                            string meshPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{SafeName(src.name)}_Ramp.asset");
                            AssetDatabase.CreateAsset(fixedMesh, meshPath);
                            log.Add($"새 경사로 메시 저장: {meshPath}");
                        }
                        if (fixedMesh == null) continue;

                        mf.sharedMesh = fixedMesh;
                        // 경사면 모양 콜라이더도 새 모양으로 (넣어 둔 경우)
                        foreach (MeshCollider mc in mf.GetComponents<MeshCollider>())
                            mc.sharedMesh = fixedMesh;
                        changed = true;
                    }

                    if (changed)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        fixedCount++;
                        log.Add($"✔ {o.name} 프리팹을 새 경사로 모양으로 교체");
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            AssetDatabase.SaveAssets();
            string msg = $"[경사로 모델] {fixedCount}개 프리팹 수정\n" + string.Join("\n", log);
            if (fixedCount > 0) Debug.Log(msg);
            else Debug.LogWarning(msg + "\nProject 창에서 경사 블록 프리팹을 선택한 뒤 다시 실행하세요");
        }

        [MenuItem("Tools/행성농사일지/경사로 모델 바닥까지 늘리기 (선택한 프리팹)", true)]
        private static bool FixSelectedValidate() => Selection.objects != null && Selection.objects.Length > 0;

        // 낮은 쪽으로 갈수록 높이를 눌러서 바닥(0)부터 꼭대기까지 곧게 오르는 경사로로
        private static Mesh BuildRampMesh(Mesh src, out string why)
        {
            why = null;
            Vector3[] v;
            try { v = src.vertices; }
            catch (Exception)
            {
                why = "모델 데이터를 읽을 수 없음 → block.fbx 선택 → Inspector Model 탭 → Read/Write 체크 → Apply 후 다시 실행";
                return null;
            }
            if (v == null || v.Length == 0)
            {
                why = "모델 데이터를 읽을 수 없음 → block.fbx 선택 → Inspector Model 탭 → Read/Write 체크 → Apply 후 다시 실행";
                return null;
            }

            Bounds b = src.bounds;
            float minY = b.min.y;
            float height = b.size.y;
            if (height <= 0.0001f) { why = "높이가 0인 모델"; return null; }

            // 어느 방향으로 올라가는 경사인지: X·Z 양 끝에서 가장 높은 점을 비교해 차이가 큰 축
            float xLo = TopNear(v, 0, b.min.x, b.size.x), xHi = TopNear(v, 0, b.max.x, b.size.x);
            float zLo = TopNear(v, 2, b.min.z, b.size.z), zHi = TopNear(v, 2, b.max.z, b.size.z);
            bool useX = Mathf.Abs(xHi - xLo) >= Mathf.Abs(zHi - zLo);
            float lo = useX ? xLo : zLo, hi = useX ? xHi : zHi;
            if (Mathf.Abs(hi - lo) < height * 0.2f) { why = "경사 모양이 아님 (양 끝 높이가 거의 같음)"; return null; }

            bool risesToPositive = hi > lo;
            float lowEndTop = Mathf.Min(lo, hi) - minY; // 낮은 쪽 끝의 높이 (그래픽팀 모델은 약 1m)
            if (lowEndTop < height * 0.08f) { why = "이미 바닥부터 이어진 경사로라 고칠 필요 없음"; return null; }

            float aMin = useX ? b.min.x : b.min.z;
            float aSize = Mathf.Max(0.0001f, useX ? b.size.x : b.size.z);

            for (int i = 0; i < v.Length; i++)
            {
                float coord = useX ? v[i].x : v[i].z;
                float u = Mathf.Clamp01((coord - aMin) / aSize);  // 0 = 낮은 쪽 끝, 1 = 높은 쪽 끝
                if (!risesToPositive) u = 1f - u;

                float oldTop = lowEndTop + (height - lowEndTop) * u; // 원래 윗면 높이 (1m → 2m)
                float newTop = height * u;                           // 바꿀 윗면 높이 (0m → 2m)
                float f = oldTop > 0.0001f ? newTop / oldTop : 0f;
                v[i].y = minY + (v[i].y - minY) * f;                 // 바닥은 그대로, 위쪽만 비율대로 눌림
            }

            Mesh m = UnityEngine.Object.Instantiate(src);
            m.name = SafeName(src.name) + "_Ramp";
            m.vertices = v;
            m.RecalculateNormals();
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        // 한 축의 끝(min 또는 max) 근처(전체 폭의 6% 안)에 있는 점들 중 가장 높은 y
        private static float TopNear(Vector3[] v, int axis, float end, float size)
        {
            float band = Mathf.Max(0.0001f, size * 0.06f);
            float best = float.MinValue;
            foreach (Vector3 p in v)
            {
                float c = axis == 0 ? p.x : p.z;
                if (Mathf.Abs(c - end) <= band && p.y > best) best = p.y;
            }
            return best;
        }

        private static string SafeName(string n)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
            return n.Replace(' ', '_');
        }
    }
}