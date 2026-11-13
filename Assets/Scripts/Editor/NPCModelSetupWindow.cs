#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using FarmingSystem.NPC;

namespace FarmingSystem.EditorTools
{
    // NPC 모델 적용 도구 (Meshy 등에서 받은 리그+애니메이션 FBX → 바로 쓰는 NPC 프리팹)
    // 메뉴: Tools → 행성농사일지 → NPC → NPC 모델 적용
    //
    // 하는 일 (FBX 하나당 한 번, 모델이 새로 오면 다시 누르면 덮어씀)
    //  1. FBX 임포트 설정: Generic 리그, 애니메이션 반복(Loop) 켜기
    //  2. 재질: 몸 = 우리 휘는 월드 셰이더(Curved Lit) + 같은 폴더의 PNG 텍스처, 유리(헬멧 등) = 반투명 재질 (그림자 없음)
    //  3. 애니메이션 자동 찾기: 이름에 idle / walk / run / talk 가 들어간 클립
    //     - 걷기·달리기 발이 땅에서 미끄러지지 않는 실제 속도를 발 뼈로 재서 맞춤
    //     - 서 있기(Idle) 모션이 없으면 걷기 중 두 발이 땅에 닿은 자세 + 숨쉬기로 임시 Idle을 만듦
    //  4. Animator Controller: Speed(걷기 속도)로 서기↔걷기↔달리기, IsTalking(말하는 중)으로 Talk
    //  5. 프리팹 저장: FBX와 같은 폴더에 "FBX이름_NPC.prefab"
    //  6. (선택) 씬의 NPC 오브젝트에 넣기: 자식 "Model"로 넣고, NPCDialogue 걷기 속도를 모델에 맞추고, 몸 충돌체 추가
    public class NPCModelSetupWindow : EditorWindow
    {
        [SerializeField] private GameObject model;
        [SerializeField] private Texture2D texture;
        [SerializeField] private GameObject targetNpc;
        [SerializeField] private int moveMode = 1; // 0 = 제자리, 1 = 구역 안 (Area), 2 = 자유 (Free)
        [SerializeField] private float scale = 1f;
        private string log = "";
        private Vector2 scroll;

        private static readonly string[] IdleKeys = { "idle", "stand", "breath", "wait" };
        private static readonly string[] WalkKeys = { "walk" };
        private static readonly string[] RunKeys = { "run", "jog" };
        private static readonly string[] TalkKeys = { "talk", "speak", "chat" };

        [MenuItem("Tools/행성농사일지/NPC/NPC 모델 적용")]
        private static void Open()
        {
            var w = GetWindow<NPCModelSetupWindow>("NPC 모델 적용");
            w.minSize = new Vector2(400, 460);
            // Project에서 FBX, Hierarchy에서 NPC를 선택해 두었으면 자동으로 채움
            foreach (Object o in Selection.objects)
            {
                string p = AssetDatabase.GetAssetPath(o);
                if (!string.IsNullOrEmpty(p) && AssetImporter.GetAtPath(p) is ModelImporter)
                {
                    w.model = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                    w.texture = FindTexture(p, null);
                }
                else if (o is GameObject g && g.scene.IsValid()) w.targetNpc = g;
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "1) Project 창의 NPC FBX를 '모델 FBX'에 끌어다 놓기\n" +
                "2) 텍스처가 자동으로 안 채워지면 PNG를 넣기\n" +
                "3) 씬의 NPC 오브젝트를 'NPC'에 넣기 (비우면 프리팹만 만듦)\n" +
                "4) [만들기] → 같은 폴더에 프리팹이 생기고 NPC에 들어감", MessageType.Info);

            EditorGUI.BeginChangeCheck();
            model = (GameObject)EditorGUILayout.ObjectField("모델 FBX", model, typeof(GameObject), false);
            if (EditorGUI.EndChangeCheck() && model != null)
            {
                string p = AssetDatabase.GetAssetPath(model);
                if (AssetImporter.GetAtPath(p) is ModelImporter) texture = FindTexture(p, null);
                else { model = null; ShowNotification(new GUIContent("FBX 파일을 넣어 주세요")); }
            }
            texture = (Texture2D)EditorGUILayout.ObjectField("텍스처 (몸 색)", texture, typeof(Texture2D), false);
            scale = EditorGUILayout.Slider(new GUIContent("크기 배율", "1 = 모델 원래 크기"), scale, 0.3f, 3f);

            EditorGUILayout.Space();
            targetNpc = (GameObject)EditorGUILayout.ObjectField(new GUIContent("NPC (씬, 선택)", "이 오브젝트 자식으로 모델을 넣음"), targetNpc, typeof(GameObject), true);
            using (new EditorGUI.DisabledScope(targetNpc == null))
                moveMode = EditorGUILayout.Popup(new GUIContent("움직임", "제자리 = 상인처럼 서 있기 / 구역 안 = 처음 자리 둘레만 (낚시꾼) / 자유 = 섬 전체"),
                    moveMode, new[] { "제자리", "구역 안에서 돌아다니기", "자유롭게 돌아다니기" });

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(model == null))
                if (GUILayout.Button(targetNpc != null ? "만들기 + NPC에 넣기" : "프리팹 만들기", GUILayout.Height(32)))
                {
                    try { log = Build(); }
                    catch (System.Exception e) { log = "실패: " + e.Message; Debug.LogException(e); }
                }

            if (!string.IsNullOrEmpty(log))
            {
                scroll = EditorGUILayout.BeginScrollView(scroll);
                EditorGUILayout.HelpBox(log, log.StartsWith("실패") ? MessageType.Error : MessageType.None);
                EditorGUILayout.EndScrollView();
            }
        }

        // ---------------------------------------------------------------- 만들기

        private string Build()
        {
            string path = AssetDatabase.GetAssetPath(model);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new System.Exception("FBX가 아니에요.");
            string folder = Path.GetDirectoryName(path).Replace('\\', '/');
            string baseName = Path.GetFileNameWithoutExtension(path);
            var sb = new StringBuilder();

            // 1. 임포트 설정
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.optimizeGameObjects = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.SaveAndReimport();

            // 2. 재질
            Material body, glass;
            int glassSlots = SetupMaterials(importer, path, folder, baseName, out body, out glass);
            sb.AppendLine($"재질: 몸 = {body.shader.name}" + (glassSlots > 0 ? $", 유리 {glassSlots}칸 = 반투명" : ""));
            sb.AppendLine($"텍스처: {(texture != null ? texture.name : "없음 (색만)")}");

            // 3. 애니메이션 반복 설정 + 찾기
            ModelImporterClipAnimation[] defs = importer.defaultClipAnimations;
            if (defs.Length == 0) throw new System.Exception("FBX에 애니메이션이 없어요. (Animation 탭 확인)");
            foreach (ModelImporterClipAnimation c in defs)
            {
                c.loopTime = true;
                c.loopPose = true;
                c.lockRootRotation = true;
                c.lockRootPositionXZ = true;
            }
            importer.clipAnimations = defs;
            importer.SaveAndReimport();

            AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToArray();
            AnimationClip idle = Pick(clips, IdleKeys), walk = Pick(clips, WalkKeys), run = Pick(clips, RunKeys), talk = Pick(clips, TalkKeys);
            if (walk == null && clips.Length == 1) walk = clips[0];
            if (walk == null) throw new System.Exception("걷기 애니메이션을 못 찾았어요. 클립 이름: " + string.Join(", ", clips.Select(c => c.name)));
            sb.AppendLine($"애니메이션: 걷기 = {walk.name}" + (run != null ? $", 달리기 = {run.name}" : "") +
                          (idle != null ? $", 서기 = {idle.name}" : "") + (talk != null ? $", 말하기 = {talk.name}" : ""));

            // 4. 발 뼈로 실제 걷는 속도 재기 (임시 복제본에서)
            float walkSpeed, runSpeed = 0f, poseTime, height;
            GameObject probe = Instantiate(model);
            probe.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                Transform lf = FindBone(probe.transform, "foot", "toe", true), rf = FindBone(probe.transform, "foot", "toe", false);
                height = MeasureHeight(probe);
                if (lf == null || rf == null)
                {
                    walkSpeed = 1f; runSpeed = 2f; poseTime = 0f;
                    sb.AppendLine("⚠ 발 뼈(LeftFoot/RightFoot)를 못 찾아서 걷기 속도를 1 m/s로 정했어요");
                }
                else
                {
                    walkSpeed = MeasureSpeed(probe, walk, lf, rf, out poseTime, out bool backward);
                    if (backward) sb.AppendLine("⚠ 모델이 뒤(-Z)를 보고 걷는 것 같아요. 모델러에게 앞(+Z) 방향을 확인해 주세요");
                    if (run != null) runSpeed = MeasureSpeed(probe, run, lf, rf, out _, out _);
                }
                if (walkSpeed < 0.05f) { walkSpeed = 1f; sb.AppendLine("⚠ 걷기 속도를 잴 수 없어 1 m/s로 정했어요"); }
                if (run != null && runSpeed <= walkSpeed + 0.05f) runSpeed = walkSpeed * 2f;
                walkSpeed *= scale;
                runSpeed *= scale;
                height *= scale;
                sb.AppendLine($"발이 안 미끄러지는 속도: 걷기 {walkSpeed:0.00} m/s" + (run != null ? $", 달리기 {runSpeed:0.00} m/s" : "") + $" · 키 {height:0.00} m");

                // 5. 서 있기 모션이 없으면 임시 Idle 만들기
                if (idle == null)
                {
                    idle = MakeIdle(probe, walk, poseTime, $"{folder}/{baseName}_Idle.anim");
                    sb.AppendLine("서기 모션이 없어서 임시 Idle을 만들었어요 (걷기 중 두 발이 땅에 닿은 자세 + 숨쉬기)");
                }
            }
            finally { DestroyImmediate(probe); }

            // 6. Animator Controller
            AnimatorController ctrl = BuildController($"{folder}/{baseName}_NPC.controller", idle, walk, run, talk, walkSpeed, runSpeed);

            // 7. 프리팹
            string prefabPath = $"{folder}/{baseName}_NPC.prefab";
            GameObject inst = Instantiate(model);
            inst.name = baseName + "_NPC";
            try
            {
                foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) if (mats[i] == null || (mats[i] != body && mats[i] != glass)) mats[i] = body;
                    r.sharedMaterials = mats;
                }
                Animator anim = inst.GetComponent<Animator>();
                if (anim == null) anim = inst.AddComponent<Animator>();
                anim.runtimeAnimatorController = ctrl;
                anim.applyRootMotion = false;
                inst.transform.localScale = Vector3.one * scale;
                PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
            }
            finally { DestroyImmediate(inst); }
            AssetDatabase.SaveAssets();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            sb.AppendLine($"프리팹: {prefabPath}");

            // 8. 씬의 NPC에 넣기
            if (targetNpc != null) sb.Append(ApplyToNpc(prefab, walkSpeed, height));
            else EditorGUIUtility.PingObject(prefab);
            return "완료!\n" + sb.ToString().TrimEnd();
        }

        // ---------------------------------------------------------------- 재질

        private int SetupMaterials(ModelImporter importer, string path, string folder, string baseName, out Material body, out Material glass)
        {
            // FBX 안의 재질 이름 (처음엔 내장 재질, 다시 누를 땐 이미 연결된 목록에서)
            var slots = new List<(string name, bool isGlass)>();
            Material[] embedded = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().ToArray();
            foreach (Material m in embedded) slots.Add((m.name, LooksLikeGlass(m)));
            foreach (var kv in importer.GetExternalObjectMap())
                if (kv.Key.type == typeof(Material) && slots.All(s => s.name != kv.Key.name))
                    slots.Add((kv.Key.name, kv.Value != null && kv.Value.name.EndsWith("_Glass")));
            if (texture == null) texture = FindTexture(path, embedded);

            Shader curved = Shader.Find("FarmingSystem/Curved Lit");
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) throw new System.Exception("URP 프로젝트가 아니에요.");

            body = LoadOrCreate($"{folder}/{baseName}_Body.mat", curved != null ? curved : lit);
            if (body.shader != (curved != null ? curved : lit)) body.shader = curved != null ? curved : lit;
            body.SetTexture("_BaseMap", texture);
            body.SetColor("_BaseColor", Color.white);
            if (body.HasProperty("_Smoothness")) body.SetFloat("_Smoothness", 0.2f);
            EditorUtility.SetDirty(body);

            glass = null;
            int glassCount = slots.Count(s => s.isGlass);
            if (glassCount > 0)
            {
                glass = LoadOrCreate($"{folder}/{baseName}_Glass.mat", lit);
                glass.shader = lit;
                glass.SetColor("_BaseColor", new Color(0.55f, 0.85f, 0.9f, 0.08f));
                glass.SetFloat("_Surface", 1f);
                glass.SetFloat("_Blend", 0f);
                glass.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                glass.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                glass.SetFloat("_ZWrite", 0f);
                glass.SetFloat("_Smoothness", 0.8f);
                glass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                glass.SetOverrideTag("RenderType", "Transparent");
                glass.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                glass.SetShaderPassEnabled("ShadowCaster", false); // 투명 헬멧이 땅에 동그란 그림자를 만들지 않게
                EditorUtility.SetDirty(glass);
            }

            foreach (var s in slots)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), s.name), s.isGlass ? glass : body);
            importer.SaveAndReimport();
            return glassCount;
        }

        private static bool LooksLikeGlass(Material m)
        {
            string n = m.name.ToLowerInvariant();
            if (n.Contains("glass") || n.Contains("helmet") || n.Contains("visor") || n.Contains("transparent") || n.Contains("유리")) return true;
            // 이름으로 모르면: 텍스처 없이 거의 투명한 색만 있는 재질만 유리로 봄
            // (몸 재질도 투명도 맵 때문에 '투명'으로 들어올 수 있어서 그건 기준으로 안 씀)
            bool hasTex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") != null : m.mainTexture != null;
            return !hasTex && m.HasProperty("_BaseColor") && m.GetColor("_BaseColor").a < 0.5f;
        }

        private static Material LoadOrCreate(string path, Shader shader)
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            return m;
        }

        // 같은 폴더의 PNG 중: FBX 재질이 쓰던 이름과 같은 것 → 없으면 가장 큰 것 (Preview 그림은 제외)
        private static Texture2D FindTexture(string fbxPath, Material[] fbxMats)
        {
            string folder = Path.GetDirectoryName(fbxPath).Replace('\\', '/');
            var cands = AssetDatabase.FindAssets("t:Texture2D", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Path.GetDirectoryName(p).Replace('\\', '/') == folder && !Path.GetFileName(p).ToLowerInvariant().Contains("preview"))
                .ToList();
            if (cands.Count == 0) return null;
            if (fbxMats != null)
                foreach (Material m in fbxMats)
                {
                    Texture t = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.mainTexture;
                    if (t == null) continue;
                    string hit = cands.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == t.name);
                    if (hit != null) return AssetDatabase.LoadAssetAtPath<Texture2D>(hit);
                }
            string biggest = cands.OrderByDescending(p => new FileInfo(p).Length).First();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(biggest);
        }

        // ---------------------------------------------------------------- 애니메이션

        // 이름으로 클립 찾기 ('|'가 들어간 옛날 이름 테이크는 뒤로)
        private static AnimationClip Pick(AnimationClip[] clips, string[] keys)
        {
            return clips.Where(c => keys.Any(k => c.name.ToLowerInvariant().Contains(k)))
                        .OrderBy(c => c.name.Contains("|") ? 1 : 0).FirstOrDefault();
        }

        private static bool IsSide(string name, bool left)
        {
            string n = name.ToLowerInvariant();
            string word = left ? "left" : "right";
            string c = left ? "l" : "r";
            return n.Contains(word) || n.StartsWith(c + "_") || n.StartsWith(c + ".") || n.EndsWith("_" + c) || n.EndsWith("." + c) || n.Contains("_" + c + "_");
        }

        private static Transform FindBone(Transform root, string key, string exclude, bool left)
        {
            return root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t =>
            {
                string n = t.name.ToLowerInvariant();
                return n.Contains(key) && (exclude == null || !n.Contains(exclude)) && IsSide(t.name, left);
            });
        }

        private static float MeasureHeight(GameObject go)
        {
            Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return 1.7f;
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            return Mathf.Max(0.5f, b.max.y - go.transform.position.y);
        }

        // 제자리 걷기 클립에서 땅을 딛고 있는 발이 뒤로 가는 속도 = 실제로 앞으로 가야 하는 속도
        // poseTime = 두 발이 모두 땅에 가깝고 너무 벌어지지 않은 순간 (임시 Idle 자세)
        private static float MeasureSpeed(GameObject go, AnimationClip clip, Transform lf, Transform rf, out float poseTime, out bool backward)
        {
            const int N = 60;
            float len = Mathf.Max(clip.length, 0.01f), dt = len / N;
            var L = new Vector3[N];
            var R = new Vector3[N];
            for (int i = 0; i < N; i++)
            {
                clip.SampleAnimation(go, i * dt);
                L[i] = go.transform.InverseTransformPoint(lf.position);
                R[i] = go.transform.InverseTransformPoint(rf.position);
            }

            float best = float.MaxValue;
            poseTime = 0f;
            for (int i = 0; i < N; i++)
            {
                float score = Mathf.Max(L[i].y, R[i].y) + 0.15f * Mathf.Abs(L[i].z - R[i].z);
                if (score < best) { best = score; poseTime = i * dt; }
            }

            float sl = StanceSpeed(L, dt), sr = StanceSpeed(R, dt);
            float v = (sl + sr) * 0.5f;
            backward = v < 0f;
            return Mathf.Abs(v);
        }

        private static float StanceSpeed(Vector3[] p, float dt)
        {
            int n = p.Length;
            float[] ys = p.Select(v => v.y).OrderBy(y => y).ToArray();
            float thr = ys[Mathf.Clamp(Mathf.RoundToInt(n * 0.4f), 0, n - 1)];
            var vs = new List<float>();
            for (int i = 0; i < n; i++)
            {
                if (p[i].y > thr) continue;
                float dz = p[(i + 1) % n].z - p[(i - 1 + n) % n].z;
                if (Mathf.Abs(dz) > 0.5f) continue; // 반복 경계에서 튀는 값 제외
                vs.Add(-dz / (2f * dt));
            }
            if (vs.Count == 0) return 0f;
            vs.Sort();
            return vs[vs.Count / 2];
        }

        // 임시 서기 모션: 걷기 중 두 발이 땅에 닿은 자세 + 팔은 걷기 전체 평균(흔들림 없이) + 상체 숨쉬기
        private static AnimationClip MakeIdle(GameObject go, AnimationClip walk, float poseTime, string assetPath)
        {
            Transform root = go.transform;
            Transform[] bones = root.GetComponentsInChildren<Transform>(true).Where(t => t != root).ToArray();

            var armAvg = new Dictionary<Transform, Vector4>();
            Transform[] arms = bones.Where(t =>
            {
                string n = t.name.ToLowerInvariant();
                return n.Contains("arm") || n.Contains("hand") || n.Contains("shoulder") || n.Contains("clavicle");
            }).ToArray();
            const int samples = 30;
            for (int i = 0; i < samples; i++)
            {
                walk.SampleAnimation(go, walk.length * i / samples);
                foreach (Transform a in arms)
                {
                    Quaternion q = a.localRotation;
                    var v = new Vector4(q.x, q.y, q.z, q.w);
                    if (armAvg.TryGetValue(a, out Vector4 s)) armAvg[a] = s + (Vector4.Dot(s, v) < 0f ? -v : v);
                    else armAvg[a] = v;
                }
            }
            walk.SampleAnimation(go, poseTime);

            Transform[] spines = bones.Where(t => t.name.ToLowerInvariant().Contains("spine")).ToArray();
            Transform breathe = spines.Length > 0 ? spines[spines.Length / 2] : null;

            var clip = new AnimationClip { name = Path.GetFileNameWithoutExtension(assetPath), frameRate = 30f };
            const float dur = 3f;
            foreach (Transform t in bones)
            {
                string p = AnimationUtility.CalculateTransformPath(t, root);
                Vector3 lp = t.localPosition;
                Quaternion lr = t.localRotation;
                if (armAvg.TryGetValue(t, out Vector4 s)) { s.Normalize(); lr = new Quaternion(s.x, s.y, s.z, s.w); }

                SetConst(clip, p, "m_LocalPosition.x", lp.x, dur);
                SetConst(clip, p, "m_LocalPosition.y", lp.y, dur);
                SetConst(clip, p, "m_LocalPosition.z", lp.z, dur);

                if (t == breathe)
                {
                    // 몸 앞뒤로 아주 살짝 (±1.2도, 3초에 한 번)
                    Vector3 axis = t.InverseTransformDirection(root.right);
                    var cx = new AnimationCurve(); var cy = new AnimationCurve(); var cz = new AnimationCurve(); var cw = new AnimationCurve();
                    for (int k = 0; k <= 8; k++)
                    {
                        float time = dur * k / 8f;
                        Quaternion q = lr * Quaternion.AngleAxis(1.2f * Mathf.Sin(time / dur * Mathf.PI * 2f), axis);
                        cx.AddKey(time, q.x); cy.AddKey(time, q.y); cz.AddKey(time, q.z); cw.AddKey(time, q.w);
                    }
                    SetCurve(clip, p, "m_LocalRotation.x", cx);
                    SetCurve(clip, p, "m_LocalRotation.y", cy);
                    SetCurve(clip, p, "m_LocalRotation.z", cz);
                    SetCurve(clip, p, "m_LocalRotation.w", cw);
                }
                else
                {
                    SetConst(clip, p, "m_LocalRotation.x", lr.x, dur);
                    SetConst(clip, p, "m_LocalRotation.y", lr.y, dur);
                    SetConst(clip, p, "m_LocalRotation.z", lr.z, dur);
                    SetConst(clip, p, "m_LocalRotation.w", lr.w, dur);
                }
            }
            clip.EnsureQuaternionContinuity();
            AnimationClipSettings set = AnimationUtility.GetAnimationClipSettings(clip);
            set.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, set);

            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath) != null) AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.CreateAsset(clip, assetPath);
            return clip;
        }

        private static void SetConst(AnimationClip clip, string path, string prop, float v, float dur) =>
            SetCurve(clip, path, prop, AnimationCurve.Constant(0f, dur, v));

        private static void SetCurve(AnimationClip clip, string path, string prop, AnimationCurve c) =>
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), prop), c);

        // Speed: 0 = 서기, 걷기 속도 = 걷기, 달리기 속도 = 달리기 (사이 값은 섞임) / IsTalking = 말하기
        private static AnimatorController BuildController(string path, AnimationClip idle, AnimationClip walk, AnimationClip run, AnimationClip talk, float walkSpeed, float runSpeed)
        {
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null) AssetDatabase.DeleteAsset(path);
            AnimatorController ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("IsTalking", AnimatorControllerParameterType.Bool);

            AnimatorStateMachine sm = ctrl.layers[0].stateMachine;
            AnimatorState move = ctrl.CreateBlendTreeInController("Move", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, walkSpeed);
            if (run != null) tree.AddChild(run, runSpeed);
            sm.defaultState = move;

            if (talk != null)
            {
                AnimatorState ts = sm.AddState("Talk", new Vector3(300, 160, 0));
                ts.motion = talk;
                AnimatorStateTransition a = move.AddTransition(ts);
                a.hasExitTime = false; a.duration = 0.2f; a.AddCondition(AnimatorConditionMode.If, 0f, "IsTalking");
                AnimatorStateTransition b = ts.AddTransition(move);
                b.hasExitTime = false; b.duration = 0.2f; b.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsTalking");
            }
            EditorUtility.SetDirty(ctrl);
            return ctrl;
        }

        // ---------------------------------------------------------------- 씬의 NPC에 넣기

        private string ApplyToNpc(GameObject prefab, float walkSpeed, float height)
        {
            var sb = new StringBuilder();
            Undo.RegisterFullObjectHierarchyUndo(targetNpc, "NPC 모델 적용");
            Transform t = targetNpc.transform;

            // 전에 이 도구로 넣은 모델은 바꿔 끼움
            Transform old = t.Find("Model");
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            // 원래 있던 임시 모습(스크립트 없는 그림 자식)은 지우지 않고 꺼 둠
            var hidden = new List<string>();
            foreach (Transform c in t)
                if (c.gameObject.activeSelf && c.GetComponentInChildren<Renderer>(true) != null && c.GetComponentsInChildren<MonoBehaviour>(true).Length == 0)
                {
                    c.gameObject.SetActive(false);
                    hidden.Add(c.name);
                }
            if (hidden.Count > 0) sb.AppendLine($"원래 모습 꺼 둠 (지우진 않음): {string.Join(", ", hidden)}");
            if (t.GetComponent<Renderer>() != null) sb.AppendLine("⚠ NPC 오브젝트 자체에 그림(Renderer)이 붙어 있어요. 필요 없으면 꺼 주세요");

            var mi = (GameObject)PrefabUtility.InstantiatePrefab(prefab, t);
            Undo.RegisterCreatedObjectUndo(mi, "NPC 모델 적용");
            mi.name = "Model";
            mi.transform.localPosition = Vector3.zero;
            mi.transform.localRotation = Quaternion.identity;

            // 돌아다니기 / 대화 스크립트
            bool wander = moveMode > 0;
            NPCDialogue d = targetNpc.GetComponent<NPCDialogue>();
            bool added = false;
            if (d == null && wander) { d = Undo.AddComponent<NPCDialogue>(targetNpc); added = true; }
            if (d != null)
            {
                var so = new SerializedObject(d);
                so.FindProperty("makePlaceholder").boolValue = false;
                so.FindProperty("wander").boolValue = wander;
                if (wander) so.FindProperty("roamMode").enumValueIndex = moveMode == 2 ? (int)NPCDialogue.RoamMode.Free : (int)NPCDialogue.RoamMode.Area;
                so.FindProperty("walkSpeed").floatValue = Mathf.Round(walkSpeed * 100f) / 100f;
                if (added)
                {
                    // 새로 붙인 경우: 낚시꾼 대사가 들어가지 않게 비움 (대사를 넣으면 말 걸기 가능)
                    so.FindProperty("npcName").stringValue = targetNpc.name;
                    so.FindProperty("lines").arraySize = 0;
                    so.FindProperty("finalChoices").arraySize = 0;
                    so.FindProperty("choiceReplies").arraySize = 0;
                }
                so.ApplyModifiedProperties();
                sb.AppendLine(moveMode == 2 ? $"돌아다니기: 자유 (섬 전체), 걷기 속도 {walkSpeed:0.00} m/s"
                            : moveMode == 1 ? $"돌아다니기: 구역 안, 걷기 속도 {walkSpeed:0.00} m/s (Scene 창 노란 원 = 범위)" : "돌아다니기: 끔 (제자리)");
                if (added) sb.AppendLine("NPCDialogue를 새로 붙였어요 (대사 비어 있음 → 말 걸기 안 됨)");
            }

            // 몸 충돌체 (플레이어가 뚫고 지나가지 않게)
            if (targetNpc.GetComponent<Collider>() == null)
            {
                CapsuleCollider cc = Undo.AddComponent<CapsuleCollider>(targetNpc);
                cc.height = height;
                cc.radius = Mathf.Clamp(height * 0.18f, 0.2f, 0.6f);
                cc.center = new Vector3(0f, height * 0.5f, 0f);
                sb.AppendLine($"몸 충돌체 추가 (높이 {height:0.0} m)");
            }

            EditorSceneManager.MarkSceneDirty(targetNpc.scene);
            Selection.activeGameObject = targetNpc;
            sb.AppendLine($"'{targetNpc.name}'에 넣었어요 → Ctrl+S로 씬 저장");
            return sb.ToString();
        }
    }
}
#endif