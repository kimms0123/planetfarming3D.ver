#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;
using FarmingSystem.UI;

namespace FarmingSystem.EditorTools
{
    // 시계 HUD 만들기
    // 메뉴: Tools → 행성농사일지 → UI → 시계 HUD 만들기
    // - 핫바가 있는 Canvas 왼쪽 위에 ClockHUD(시간 / 큰 날짜 / 요일 / 계절)를 만들고 ClockHUD 스크립트에 연결
    // - 글자는 흰색 + 진한 테두리 + 부드러운 그림자 (밝은 하늘·맵 위에서도 잘 보이게)
    //   → 폰트 재질(머티리얼) 하나를 Assets/UI/HUD에 만들어서 4개 글자가 같이 씀. 그림자·테두리는 그 재질에서 조절
    // - 만든 뒤 씬에서 위치·크기·폰트 크기를 마음대로 바꾸면 됨 (다시 만들면 직접 고친 건 사라짐)
    public static class ClockHUDBuilder
    {
        private const string Folder = "Assets/UI/HUD";

        [MenuItem("Tools/행성농사일지/UI/시계 HUD 만들기")]
        private static void Build()
        {
            Canvas canvas = FindCanvas();
            if (canvas == null)
            {
                EditorUtility.DisplayDialog("시계 HUD 만들기", "화면용 Canvas를 찾지 못했어요. 핫바가 있는 씬을 열고 다시 실행해 주세요.", "확인");
                return;
            }

            Transform existing = canvas.transform.Find("ClockHUD");
            if (existing != null)
            {
                if (!EditorUtility.DisplayDialog("시계 HUD 만들기",
                        $"'{canvas.name}' 안에 이미 ClockHUD가 있어요.\n지우고 새로 만들까요? (직접 고친 위치·크기는 사라져요)", "새로 만들기", "취소"))
                    return;
                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            TMP_FontAsset font = FindFont();
            Material shadowMat = font != null ? MakeShadowMaterial(font) : null;

            // 뿌리: 왼쪽 위 고정
            GameObject root = new GameObject("ClockHUD", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(root, "시계 HUD 만들기");
            root.layer = LayerMask.NameToLayer("UI");
            root.transform.SetParent(canvas.transform, false);
            RectTransform rr = (RectTransform)root.transform;
            rr.anchorMin = rr.anchorMax = new Vector2(0f, 1f);
            rr.pivot = new Vector2(0f, 1f);
            rr.anchoredPosition = new Vector2(36f, -24f);
            rr.sizeDelta = new Vector2(340f, 190f);

            //   오전 10:07
            //   17  화
            //       가을
            TMP_Text time = MakeText("Time", root.transform, font, shadowMat, "오전 10:07", 46f,
                new Vector2(0f, 0f), new Vector2(340f, 56f), TextAlignmentOptions.MidlineLeft);
            TMP_Text day = MakeText("Day", root.transform, font, shadowMat, "17", 110f,
                new Vector2(0f, -56f), new Vector2(130f, 124f), TextAlignmentOptions.Center);
            // 날짜는 톡 튀는 연출이 가운데 기준으로 커지게 Pivot을 가운데로
            RectTransform dr = day.rectTransform;
            dr.pivot = new Vector2(0.5f, 0.5f);
            dr.anchoredPosition = new Vector2(65f, -56f - 62f);
            TMP_Text weekday = MakeText("Weekday", root.transform, font, shadowMat, "화", 42f,
                new Vector2(138f, -66f), new Vector2(190f, 52f), TextAlignmentOptions.MidlineLeft);
            TMP_Text season = MakeText("Season", root.transform, font, shadowMat, "가을", 42f,
                new Vector2(138f, -120f), new Vector2(190f, 52f), TextAlignmentOptions.MidlineLeft);

            ClockHUD hud = root.AddComponent<ClockHUD>();
            var so = new SerializedObject(hud);
            so.FindProperty("timeText").objectReferenceValue = time;
            so.FindProperty("dayText").objectReferenceValue = day;
            so.FindProperty("weekdayText").objectReferenceValue = weekday;
            so.FindProperty("seasonText").objectReferenceValue = season;
            so.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
            Debug.Log($"[시계 HUD] '{canvas.name}' 왼쪽 위에 ClockHUD를 만들었어요. 위치는 ClockHUD, 글자 크기는 Time/Day/Weekday/Season에서 바꾸세요." +
                      (shadowMat != null ? $"\n글자 그림자·테두리 재질: {AssetDatabase.GetAssetPath(shadowMat)}" : "") +
                      (font == null ? "\n⚠ 한글 폰트를 못 찾았어요. 네 글자의 Font Asset을 한글 폰트로 바꿔 주세요" : ""));
        }

        private static TMP_Text MakeText(string name, Transform parent, TMP_FontAsset font, Material mat, string sample,
                                         float size, Vector2 topLeft, Vector2 box, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            if (mat != null) t.fontSharedMaterial = mat;
            t.text = sample;
            t.fontSize = size;
            t.fontStyle = FontStyles.Bold;
            t.color = Color.white;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            RectTransform r = t.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = topLeft;
            r.sizeDelta = box;
            return t;
        }

        // 폰트 기본 재질을 복사해서 테두리 + 그림자를 켠 재질 (같은 폰트 그림을 그대로 씀)
        private static Material MakeShadowMaterial(TMP_FontAsset font)
        {
            EnsureFolder(Folder);
            string path = $"{Folder}/ClockHUD_Text.mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null && existing.mainTexture == font.material.mainTexture) return existing;

            var m = new Material(font.material) { name = "ClockHUD_Text" };
            // 테두리 (진한 보라빛 갈색)
            m.EnableKeyword("OUTLINE_ON");
            if (m.HasProperty("_OutlineWidth")) m.SetFloat("_OutlineWidth", 0.18f);
            if (m.HasProperty("_OutlineColor")) m.SetColor("_OutlineColor", new Color(0.24f, 0.18f, 0.26f, 1f));
            if (m.HasProperty("_FaceDilate")) m.SetFloat("_FaceDilate", 0.1f);
            // 그림자 (오른쪽 아래로 부드럽게)
            m.EnableKeyword("UNDERLAY_ON");
            if (m.HasProperty("_UnderlayColor")) m.SetColor("_UnderlayColor", new Color(0.12f, 0.08f, 0.2f, 0.55f));
            if (m.HasProperty("_UnderlayOffsetX")) m.SetFloat("_UnderlayOffsetX", 0.6f);
            if (m.HasProperty("_UnderlayOffsetY")) m.SetFloat("_UnderlayOffsetY", -0.6f);
            if (m.HasProperty("_UnderlayDilate")) m.SetFloat("_UnderlayDilate", 0.3f);
            if (m.HasProperty("_UnderlaySoftness")) m.SetFloat("_UnderlaySoftness", 0.4f);

            if (existing != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
            AssetDatabase.SaveAssets();
            return m;
        }

        // 핫바가 있는 Canvas → 아무 화면 Canvas
        private static Canvas FindCanvas()
        {
            HotbarUI hotbar = Object.FindAnyObjectByType<HotbarUI>(FindObjectsInactive.Include);
            Canvas c = hotbar != null ? hotbar.GetComponentInParent<Canvas>(true) : null;
            if (c != null) return c.rootCanvas;
            foreach (Canvas any in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
                if (any.isRootCanvas && any.renderMode != RenderMode.WorldSpace) return any;
            return null;
        }

        // 씬에서 이미 쓰는 한글 폰트
        private static TMP_FontAsset FindFont()
        {
            foreach (TextMeshProUGUI t in Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include))
                if (t.font != null && !t.font.name.StartsWith("LiberationSans")) return t.font;
            return null;
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