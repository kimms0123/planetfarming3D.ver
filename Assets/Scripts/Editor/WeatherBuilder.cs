#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FarmingSystem.Weather;

namespace FarmingSystem.EditorTools
{
    // 날씨 시스템 만들기
    // 메뉴: Tools → 행성농사일지 → 날씨 → 날씨 시스템 만들기
    // 만드는 것
    // - 씬에 Weather 오브젝트: WeatherManager(날씨 정하기·비 물주기·태풍 규칙) + WeatherVisuals(연출)
    //     ├ Rain           빗방울 파티클 (카메라를 따라다님)
    //     ├ LightningLight 번개 빛 (평소엔 꺼짐)
    //     └ RainAudio / WindAudio / ThunderAudio  소리 자리 (소리 파일은 직접 넣기)
    //     ├ Debris         태풍 때 날아다니는 잎·파편
    //     └ Tornado        태풍 때 맵을 떠도는 회오리 바람 기둥 (Funnel 기둥 + Dust 흙먼지)
    // - Assets/Weather 폴더에 빗줄기·잎·구름 그림과 파티클 재질
    // 메뉴: Tools → 행성농사일지 → 날씨 → 허리케인 연출 추가 (이미 만든 날씨 시스템에 잎·회오리만 추가 - 설정 유지)
    public static class WeatherBuilder
    {
        private const string Folder = "Assets/Weather";

        [MenuItem("Tools/행성농사일지/날씨/날씨 시스템 만들기")]
        private static void Build()
        {
            WeatherManager existing = Object.FindAnyObjectByType<WeatherManager>(FindObjectsInactive.Include);
            if (existing != null)
            {
                if (!EditorUtility.DisplayDialog("날씨 시스템 만들기",
                        $"씬에 이미 날씨 시스템('{existing.name}')이 있어요.\n지우고 새로 만들까요? (직접 바꾼 확률·소리 연결은 사라져요)", "새로 만들기", "취소"))
                    return;
                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            EnsureFolder(Folder);
            Material rainMat = MakeRainMaterial();

            GameObject root = new GameObject("Weather");
            Undo.RegisterCreatedObjectUndo(root, "날씨 시스템 만들기");
            root.AddComponent<WeatherManager>();
            WeatherVisuals visuals = root.AddComponent<WeatherVisuals>();

            ParticleSystem rain = MakeRain(root.transform, rainMat);
            Light lightning = MakeLightning(root.transform);
            AudioSource rainAudio = MakeAudio(root.transform, "RainAudio", true);
            AudioSource windAudio = MakeAudio(root.transform, "WindAudio", true);
            AudioSource thunderAudio = MakeAudio(root.transform, "ThunderAudio", false);

            var so = new SerializedObject(visuals);
            so.FindProperty("rain").objectReferenceValue = rain;
            so.FindProperty("lightningLight").objectReferenceValue = lightning;
            so.FindProperty("rainAudio").objectReferenceValue = rainAudio;
            so.FindProperty("windAudio").objectReferenceValue = windAudio;
            so.FindProperty("thunderAudio").objectReferenceValue = thunderAudio;
            so.ApplyModifiedProperties();

            AddHurricaneParts(visuals);

            bool hasSky = Object.FindAnyObjectByType<FarmingSystem.World.SkyController>(FindObjectsInactive.Include) != null;

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
            Debug.Log("[날씨] 날씨 시스템을 만들었어요. 실행 중 Weather의 WeatherManager → Force Weather를 Rain / Typhoon으로 바꾸면 바로 확인할 수 있어요.\n" +
                      "나무를 흔들리게 하려면 나무 잎 재질(Curved Lit)의 Wind Sway를 0.3~1로 올리세요. 소리는 RainAudio·WindAudio의 AudioClip, WeatherVisuals의 Thunder Clips에 넣으세요." +
                      (hasSky ? "" : "\n⚠ SkyController가 씬에 없어서 흐린 하늘·번개 하늘 연출은 안 보여요"));
        }

        [MenuItem("Tools/행성농사일지/날씨/허리케인 연출 추가 (잎·회오리)")]
        private static void AddHurricaneMenu()
        {
            WeatherVisuals visuals = Object.FindAnyObjectByType<WeatherVisuals>(FindObjectsInactive.Include);
            if (visuals == null)
            {
                EditorUtility.DisplayDialog("허리케인 연출 추가", "씬에 날씨 시스템이 없어요. 먼저 '날씨 시스템 만들기'를 실행해 주세요.", "확인");
                return;
            }
            EnsureFolder(Folder);
            string note = AddHurricaneParts(visuals);
            EditorSceneManager.MarkSceneDirty(visuals.gameObject.scene);
            Selection.activeGameObject = visuals.gameObject;
            Debug.Log("[날씨] 허리케인 연출(날아다니는 잎·회오리 바람 기둥)을 추가했어요. 실행 중 Force Weather를 Typhoon으로 바꿔 확인하세요." + note);
        }

        // 잎·파편 + 회오리 (이미 있으면 다시 만듦)
        private static string AddHurricaneParts(WeatherVisuals visuals)
        {
            Transform root = visuals.transform;
            foreach (string n in new[] { "Debris", "Tornado" })
            {
                Transform old = root.Find(n);
                if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            }

            Material leafMat = MakeParticleMaterial("Leaf", MakeTexture("Leaf.png", 32, LeafPixel));
            Material puffMat = MakeParticleMaterial("StormPuff", MakeTexture("StormPuff.png", 64, PuffPixel));

            ParticleSystem debris = MakeDebris(root, leafMat);
            var so = new SerializedObject(visuals);
            so.FindProperty("debris").objectReferenceValue = debris;
            so.ApplyModifiedProperties();

            // 회오리: 섬 생성기(IslandGenerator)가 있으면 그 위치를 중심으로 떠돎
            GameObject tornado = new GameObject("Tornado");
            Undo.RegisterCreatedObjectUndo(tornado, "회오리 만들기");
            tornado.transform.SetParent(root, false);
            GameObject island = GameObject.Find("IslandGenerator");
            if (island != null) tornado.transform.position = island.transform.position;
            ParticleSystem funnel = MakeFunnel(tornado.transform, puffMat);
            ParticleSystem dust = MakeDust(tornado.transform, puffMat);
            WeatherTornado wt = tornado.AddComponent<WeatherTornado>();
            var tso = new SerializedObject(wt);
            tso.FindProperty("funnel").objectReferenceValue = funnel;
            tso.FindProperty("dust").objectReferenceValue = dust;
            if (island != null) tso.FindProperty("roamCenter").objectReferenceValue = island.transform;
            tso.ApplyModifiedProperties();

            return island != null
                ? "\n회오리는 IslandGenerator 위치를 중심으로 반경 60m 안에서 떠돌아요 (Tornado → Roam Radius로 섬 크기에 맞추기)"
                : "\n⚠ IslandGenerator를 못 찾아서 회오리 중심을 (0,0,0)으로 했어요. Tornado의 Roam Center에 섬 중심 오브젝트를 넣어 주세요";
        }

        // ── 날아다니는 잎·파편 ──
        private static ParticleSystem MakeDebris(Transform parent, Material mat)
        {
            var go = new GameObject("Debris");
            Undo.RegisterCreatedObjectUndo(go, "잎 파티클");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 3.5f);
            main.startSpeed = 0f; // 속도는 WeatherVisuals가 바람 방향으로 넣음
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var colors = new Gradient();
            colors.SetKeys(
                new[] { new GradientColorKey(new Color(0.45f, 0.7f, 0.3f), 0f), new GradientColorKey(new Color(0.85f, 0.75f, 0.3f), 0.5f), new GradientColorKey(new Color(0.55f, 0.38f, 0.22f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            main.startColor = new ParticleSystem.MinMaxGradient(colors) { mode = ParticleSystemGradientMode.RandomColor }; // 초록·노랑·갈색 잎 섞어서
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(40f, 8f, 40f);

            var rot = ps.rotationOverLifetime; // 빙글빙글 뒤집히며 날아감
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);

            var noise = ps.noise; // 펄럭임
            noise.enabled = true;
            noise.strength = 2f;
            noise.frequency = 0.5f;
            noise.scrollSpeed = 1f;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        // ── 회오리 기둥 (아래는 좁고 위로 넓어지며 빙글빙글) ──
        private static ParticleSystem MakeFunnel(Transform parent, Material mat)
        {
            var go = new GameObject("Funnel");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = 3.5f;
            main.startSpeed = 7f;
            main.startSize = new ParticleSystem.MinMaxCurve(2.5f, 4f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.57f, 0.63f, 0.5f), new Color(0.75f, 0.77f, 0.83f, 0.45f));
            main.simulationSpace = ParticleSystemSimulationSpace.Local; // 회오리가 움직이면 기둥도 같이
            main.maxParticles = 1500;

            var emission = ps.emission;
            emission.rateOverTime = 160f; // 최대값 (WeatherTornado가 나타나는 정도에 맞춰 줄임)

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 22f;
            shape.radius = 1.2f;
            shape.rotation = new Vector3(-90f, 0f, 0f); // 위로 퍼지는 원뿔

            var vel = ps.velocityOverLifetime; // 기둥을 축으로 빙글빙글
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(0f);
            vel.y = new ParticleSystem.MinMaxCurve(0f);
            vel.z = new ParticleSystem.MinMaxCurve(0f);
            vel.orbitalY = new ParticleSystem.MinMaxCurve(2.5f);

            var size = ps.sizeOverLifetime; // 위로 갈수록 커짐
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));

            var col = ps.colorOverLifetime; // 서서히 나타났다 사라짐
            col.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(fade);

            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(1f, 2f);

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        // ── 회오리 바닥 흙먼지 ──
        private static ParticleSystem MakeDust(Transform parent, Material mat)
        {
            var go = new GameObject("Dust");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(2f, 4f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.58f, 0.52f, 0.46f, 0.5f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 400;

            var emission = ps.emission;
            emission.rateOverTime = 50f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle; // 땅에서 둥글게 퍼져 나감
            shape.radius = 3f;
            shape.rotation = new Vector3(90f, 0f, 0f);

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(0f);
            vel.y = new ParticleSystem.MinMaxCurve(1f);
            vel.z = new ParticleSystem.MinMaxCurve(0f);
            vel.orbitalY = new ParticleSystem.MinMaxCurve(3f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(fade);

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        // ── 그림 만들기 (잎 / 뭉게구름) ──
        private delegate Color PixelFunc(float u, float v);

        // 가운데 잎맥이 있는 뾰족한 타원 잎 (흰색 - 색은 파티클 색으로 입힘)
        private static Color LeafPixel(float u, float v)
        {
            float x = (u - 0.5f) * 2f, y = (v - 0.5f) * 2f;
            float w = 0.55f * (1f - y * y);                 // 위아래 끝이 뾰족
            float inside = Mathf.Clamp01((w - Mathf.Abs(x)) * 12f);
            float vein = Mathf.Abs(x) < 0.05f ? 0.75f : 1f;
            return new Color(vein, vein, vein, inside);
        }

        // 가장자리가 부드럽고 살짝 울퉁불퉁한 구름 덩어리
        private static Color PuffPixel(float u, float v)
        {
            float x = u - 0.5f, y = v - 0.5f;
            float d = Mathf.Sqrt(x * x + y * y) * 2f;
            float n = Mathf.PerlinNoise(u * 6f, v * 6f) * 0.35f;
            float a = Mathf.Clamp01(1f - d - n + 0.2f);
            return new Color(1f, 1f, 1f, a * a);
        }

        private static Texture2D MakeTexture(string file, int size, PixelFunc f)
        {
            string path = $"{Folder}/{file}";
            if (!File.Exists(path))
            {
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                        tex.SetPixel(x, y, f((x + 0.5f) / size, (y + 0.5f) / size));
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ── 빗방울 파티클 ──
        private static ParticleSystem MakeRain(Transform parent, Material mat)
        {
            var go = new GameObject("Rain");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 1f;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = 1.3f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(20f, 26f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.07f);
            main.startColor = new Color(0.82f, 0.88f, 1f, 0.45f);
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World; // 카메라가 움직여도 이미 떨어지는 빗방울은 제자리
            main.maxParticles = 8000;

            var emission = ps.emission;
            emission.rateOverTime = 0f; // 날씨에 맞춰 WeatherVisuals가 조절

            // 카메라 위 40 x 40m 넓이에서 아래로
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(40f, 40f, 0.1f);
            shape.rotation = new Vector3(90f, 0f, 0f); // 상자 앞(+Z)이 아래를 보게 → 아래로 발사

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch; // 속도 방향으로 길쭉한 빗줄기
            r.velocityScale = 0.05f;
            r.lengthScale = 2f;
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        // ── 번개 빛 ──
        private static Light MakeLightning(Transform parent)
        {
            var go = new GameObject("LightningLight");
            go.transform.SetParent(parent, false);
            go.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
            Light l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = new Color(0.78f, 0.84f, 1f);
            l.intensity = 0f;
            l.shadows = LightShadows.None;
            l.enabled = false;
            return l;
        }

        private static AudioSource MakeAudio(Transform parent, string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            AudioSource a = go.AddComponent<AudioSource>();
            a.playOnAwake = false;
            a.loop = loop;
            a.spatialBlend = 0f; // 화면 어디서나 같은 크기
            a.volume = 0f;
            return a;
        }

        // ── 빗줄기 그림 + 재질 ──
        private static Material MakeRainMaterial()
        {
            string texPath = $"{Folder}/RainStreak.png";
            if (!File.Exists(texPath))
            {
                // 가로 8 x 세로 64: 가운데가 진하고 양옆·위아래로 흐려지는 가는 줄
                var tex = new Texture2D(8, 64, TextureFormat.RGBA32, false);
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 8; x++)
                    {
                        float dx = (x + 0.5f) / 8f - 0.5f;
                        float side = Mathf.Exp(-dx * dx * 40f);
                        float v = y / 63f;
                        float along = Mathf.Sin(v * Mathf.PI); // 위아래 끝은 흐리게
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, side * along));
                    }
                tex.Apply();
                File.WriteAllBytes(texPath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(texPath);
                var importer = (TextureImporter)AssetImporter.GetAtPath(texPath);
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
            Texture2D streak = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            return MakeParticleMaterial("Rain", streak);
        }

        // 반투명 파티클 재질 (URP Particles/Unlit, 알파 블렌드)
        private static Material MakeParticleMaterial(string name, Texture2D streak)
        {
            string matPath = $"{Folder}/{name}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat != null) return mat;

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            mat = new Material(shader) { name = name };
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", streak);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", streak);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            // 반투명 (알파 블렌드)
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            AssetDatabase.CreateAsset(mat, matPath);
            AssetDatabase.SaveAssets();
            return mat;
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