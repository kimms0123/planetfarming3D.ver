#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FarmingSystem.World.EditorTools
{
    // 몽환 행성 만들기 도구
    // 메뉴: Tools → 행성농사일지 → 하늘 → 몽환 행성 만들기 (큰 행성 + 고리 / 작은 행성)
    // - 씬의 SkyPlanets(SkyObjectFollower가 붙은 오브젝트) 아래에 행성을 만들어 줌. 없으면 SkyPlanets도 새로 만듦
    // - 행성 하나 = 몸통(구) + 빛무리(카메라를 보는 판) + 고리(선택)
    // - 재질 3개를 Assets/Sky/Planets 폴더에 새로 만들어 붙여 줌 → 만든 뒤 재질에서 색을 마음대로 바꾸면 됨
    // - 충돌체 없음, 그림자 안 만듦 (배경 장식이라)
    public static class DreamyPlanetBuilder
    {
        private const string MaterialFolder = "Assets/Sky/Planets";

        // 행성 한 개의 기본 설정
        private struct Preset
        {
            public string name;
            public Vector3 position;     // 카메라로부터의 방향·거리
            public float diameter;       // 행성 지름 (m)
            public Vector3 tilt;         // 기울기 (고리·띠 방향)
            public Color a, b, c;        // 띠 색 3개
            public Color shadow, rim, glow;
            public float bandCount, seed;
            public bool ring;
            public Color ringInner, ringOuter;
        }

        private static Color C(float r, float g, float b) => new Color(r, g, b, 1f);

        [MenuItem("Tools/행성농사일지/하늘/몽환 행성 만들기 - 큰 행성 (고리)")]
        private static void CreateBig()
        {
            Build(new Preset
            {
                name = "몽환 행성 (큰)",
                position = new Vector3(250f, 160f, 600f),
                diameter = 150f,
                tilt = new Vector3(0f, 0f, 18f),
                a = C(1.00f, 0.78f, 0.86f),   // 분홍
                b = C(0.78f, 0.74f, 1.00f),   // 연보라
                c = C(0.72f, 0.92f, 1.00f),   // 하늘색
                shadow = C(0.55f, 0.50f, 0.85f),
                rim = C(1.00f, 0.85f, 0.95f),
                glow = C(1.00f, 0.80f, 0.95f),
                bandCount = 3.5f,
                seed = 7f,
                ring = true,
                ringInner = C(0.85f, 0.80f, 1.00f),
                ringOuter = C(1.00f, 0.82f, 0.90f),
            });
        }

        [MenuItem("Tools/행성농사일지/하늘/몽환 행성 만들기 - 작은 행성")]
        private static void CreateSmall()
        {
            Build(new Preset
            {
                name = "몽환 행성 (작은)",
                position = new Vector3(-380f, 110f, 520f),
                diameter = 55f,
                tilt = new Vector3(10f, 0f, -12f),
                a = C(0.72f, 0.96f, 0.88f),   // 민트
                b = C(0.70f, 0.86f, 1.00f),   // 하늘색
                c = C(1.00f, 0.95f, 0.78f),   // 연노랑
                shadow = C(0.50f, 0.60f, 0.85f),
                rim = C(0.85f, 1.00f, 0.97f),
                glow = C(0.75f, 0.95f, 1.00f),
                bandCount = 2.5f,
                seed = 42f,
                ring = false,
            });
        }

        private static void Build(Preset p)
        {
            Shader planetShader = Shader.Find("FarmingSystem/Dreamy Planet");
            Shader haloShader = Shader.Find("FarmingSystem/Dreamy Planet Halo");
            Shader ringShader = Shader.Find("FarmingSystem/Dreamy Planet Ring");
            if (planetShader == null || haloShader == null || (p.ring && ringShader == null))
            {
                EditorUtility.DisplayDialog("몽환 행성 만들기",
                    "셰이더를 찾을 수 없어요.\nDreamyPlanet.shader / DreamyPlanetHalo.shader / DreamyPlanetRing.shader 3개가 프로젝트에 있는지, Console에 셰이더 에러가 없는지 확인해 주세요.", "확인");
                return;
            }

            EnsureFolder(MaterialFolder);

            // 하늘 행성 묶음 찾기 (없으면 만들기)
            SkyObjectFollower follower = Object.FindAnyObjectByType<SkyObjectFollower>();
            GameObject group;
            if (follower != null) group = follower.gameObject;
            else
            {
                group = new GameObject("SkyPlanets");
                group.AddComponent<SkyObjectFollower>();
                Undo.RegisterCreatedObjectUndo(group, "SkyPlanets 만들기");
            }

            // 행성 묶음 (위치·크기·기울기는 여기에)
            GameObject root = new GameObject(p.name);
            Undo.RegisterCreatedObjectUndo(root, "몽환 행성 만들기");
            root.transform.SetParent(group.transform, false);
            root.transform.localPosition = p.position;
            root.transform.localRotation = Quaternion.Euler(p.tilt);
            root.transform.localScale = Vector3.one * p.diameter;

            // 몸통 (지름 1짜리 구 → 묶음 크기만큼 커짐)
            Material body = new Material(planetShader) { name = p.name + " 몸통" };
            body.SetColor("_ColorA", p.a);
            body.SetColor("_ColorB", p.b);
            body.SetColor("_ColorC", p.c);
            body.SetColor("_ShadowColor", p.shadow);
            body.SetColor("_RimColor", p.rim);
            body.SetFloat("_BandCount", p.bandCount);
            body.SetFloat("_Seed", p.seed);
            body = SaveMaterial(body);

            GameObject bodyGo = MakeChild(PrimitiveType.Sphere, "Body", root.transform, body);
            Mesh smooth = Resources.Load<Mesh>("Meshes/icosphere_high"); // 행성 팩의 매끈한 구가 있으면 사용
            if (smooth != null)
            {
                bodyGo.GetComponent<MeshFilter>().sharedMesh = smooth;
                float radius = Mathf.Max(smooth.bounds.extents.x, 0.0001f);
                bodyGo.transform.localScale = Vector3.one * (0.5f / radius); // 지름 1로 맞춤
            }

            // 빛무리 (행성보다 1.6배 큰 판, 항상 카메라를 향함)
            Material halo = new Material(haloShader) { name = p.name + " 빛무리" };
            halo.SetColor("_GlowColor", p.glow);
            halo.SetFloat("_PlanetRadius", 1f / 1.6f);
            halo = SaveMaterial(halo);
            GameObject haloGo = MakeChild(PrimitiveType.Quad, "Halo", root.transform, halo);
            haloGo.transform.localScale = Vector3.one * 1.6f;

            // 고리 (행성 적도면에 눕힌 판)
            if (p.ring)
            {
                Material ring = new Material(ringShader) { name = p.name + " 고리" };
                ring.SetColor("_InnerColor", p.ringInner);
                ring.SetColor("_OuterColor", p.ringOuter);
                ring.SetFloat("_Seed", p.seed);
                ring = SaveMaterial(ring);
                GameObject ringGo = MakeChild(PrimitiveType.Quad, "Ring", root.transform, ring);
                ringGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                ringGo.transform.localScale = Vector3.one * 2.8f;
            }

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
            Debug.Log($"[몽환 행성] '{p.name}' 만들었어요. 재질은 {MaterialFolder} 폴더에 있어요. 위치·크기는 '{p.name}' 오브젝트에서 바꾸세요.", root);
        }

        // 기본 도형을 만들고 충돌체·그림자를 끔
        private static GameObject MakeChild(PrimitiveType type, string name, Transform parent, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Collider col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            go.transform.SetParent(parent, false);
            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go;
        }

        private static Material SaveMaterial(Material mat)
        {
            string path = AssetDatabase.GenerateUniqueAssetPath($"{MaterialFolder}/{mat.name}.mat");
            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<Material>(path);
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