using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FarmingSystem.UI;
using FarmingSystem.UI.Shop;

namespace FarmingSystem.EditorTools
{
    // 상점 대화창(아래 대화 상자 + 오른쪽 선택지)을 씬의 Canvas 안에 실제 오브젝트로 만들어 주는 도구
    // 만든 뒤에는 핫바처럼 씬에서 위치·크기·이미지·색·글꼴을 마음대로 고치면 됨 (코드가 덮어쓰지 않음)
    // - Canvas: 핫바가 들어 있는 Canvas (없으면 ShopDialogueUI가 있는 Canvas, 그것도 없으면 새로 만듦)
    // - 이미지: 둥근 상자·화살표 PNG를 Assets/UI/ShopConversation에 저장해서 씀 (디자이너 이미지로 바꿔 끼워도 됨)
    // - 연결: ShopDialogueUI 오브젝트에 ShopConversationUI를 붙이고 칸들을 자동으로 채움
    //
    // 메뉴: Tools → 행성농사일지 → 상점 대화창 만들기
    public static class ShopConversationBuilder
    {
        private const string SpriteFolder = "Assets/UI/ShopConversation";

        [MenuItem("Tools/행성농사일지/상점 대화창 만들기")]
        private static void Build()
        {
            ShopDialogueUI dialogue = Object.FindAnyObjectByType<ShopDialogueUI>(FindObjectsInactive.Include);
            if (dialogue == null)
            {
                EditorUtility.DisplayDialog("상점 대화창 만들기", "씬에 ShopDialogueUI가 없어요. 상점이 있는 씬을 열고 다시 실행하세요.", "확인");
                return;
            }

            Canvas canvas = FindTargetCanvas(dialogue);

            // 이미 있으면 다시 만들지 물어봄
            Transform existing = canvas.transform.Find("ShopConversation");
            if (existing != null)
            {
                if (!EditorUtility.DisplayDialog("상점 대화창 만들기",
                        $"'{canvas.name}' 안에 이미 ShopConversation이 있어요.\n지우고 새로 만들까요? (직접 고친 모양은 사라져요)", "새로 만들기", "취소"))
                    return;
                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            Sprite round = SaveSprite("ShopRounded.png", ShopConversationLayout.RoundedPixels(64, 24), 64, 24);
            Sprite arrow = SaveSprite("ShopArrow.png", ShopConversationLayout.ArrowPixels(48), 48, 0);
            TMP_FontAsset font = FindKoreanFont();

            var parts = ShopConversationLayout.Build(canvas.transform, round, arrow, font,
                new Color(1f, 1f, 1f, 0.97f),        // 상자 바탕
                new Color(0.25f, 0.27f, 0.32f, 1f),  // 테두리
                new Color(0.2f, 0.2f, 0.24f, 1f),    // 글자
                new Color(0.98f, 0.72f, 0.3f, 1f),   // 이름표
                40f);
            parts.root.SetAsLastSibling(); // 핫바 등보다 위에 그려지게
            Undo.RegisterCreatedObjectUndo(parts.root.gameObject, "상점 대화창 만들기");
            foreach (ShopChoiceRowUI row in parts.root.GetComponentsInChildren<ShopChoiceRowUI>(true))
                EditorUtility.SetDirty(row);

            // ShopDialogueUI 오브젝트에 ShopConversationUI 연결
            ShopConversationUI view = dialogue.GetComponent<ShopConversationUI>();
            if (view == null) view = Undo.AddComponent<ShopConversationUI>(dialogue.gameObject);
            var so = new SerializedObject(view);
            so.FindProperty("viewRoot").objectReferenceValue = parts.root.gameObject;
            so.FindProperty("nameTagObject").objectReferenceValue = parts.nameTag;
            so.FindProperty("nameLabel").objectReferenceValue = parts.nameLabel;
            so.FindProperty("lineLabel").objectReferenceValue = parts.lineLabel;
            so.FindProperty("nextIndicator").objectReferenceValue = parts.next;
            so.FindProperty("choiceBox").objectReferenceValue = parts.choiceBox;
            if (font != null) so.FindProperty("font").objectReferenceValue = font;
            so.ApplyModifiedProperties();

            var dso = new SerializedObject(dialogue);
            dso.FindProperty("useConversationView").boolValue = true;
            dso.FindProperty("conversation").objectReferenceValue = view;
            dso.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(dialogue.gameObject.scene);
            Selection.activeGameObject = parts.root.gameObject;
            EditorGUIUtility.PingObject(parts.root.gameObject);

            Debug.Log($"[상점 대화창] '{canvas.name}' 안에 ShopConversation을 만들고 ShopDialogueUI에 연결했어요.\n" +
                      "씬에서 DialogueBox(대화 상자)·ChoiceBox(선택지 상자)·NameTag(이름표)를 골라 위치·크기·이미지·색을 바꾸면 돼요. " +
                      "게임을 시작하면 자동으로 숨겨졌다가 상인에게 말을 걸면 나타나요." +
                      (font == null ? "\n⚠ 한글 폰트를 못 찾아서 기본 폰트로 만들었어요. Line·Name·Label 글자의 Font Asset을 한글 폰트로 바꿔주세요" : ""));
        }

        // 핫바가 있는 Canvas → ShopDialogueUI가 있는 Canvas → 아무 화면 Canvas → 새로 만듦
        private static Canvas FindTargetCanvas(ShopDialogueUI dialogue)
        {
            HotbarUI hotbar = Object.FindAnyObjectByType<HotbarUI>(FindObjectsInactive.Include);
            Canvas c = hotbar != null ? hotbar.GetComponentInParent<Canvas>(true) : null;
            if (c == null) c = dialogue.GetComponentInParent<Canvas>(true);
            if (c == null)
            {
                foreach (Canvas any in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
                    if (any.isRootCanvas && any.renderMode != RenderMode.WorldSpace) { c = any; break; }
            }
            if (c != null) return c.rootCanvas;

            var go = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(go, "Canvas 만들기");
            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        // 그림을 PNG로 저장하고 스프라이트(9-슬라이스 테두리 포함)로 불러옴
        private static Sprite SaveSprite(string fileName, Color32[] pixels, int size, int border)
        {
            if (!AssetDatabase.IsValidFolder("Assets/UI")) AssetDatabase.CreateFolder("Assets", "UI");
            if (!AssetDatabase.IsValidFolder(SpriteFolder)) AssetDatabase.CreateFolder("Assets/UI", "ShopConversation");
            string path = $"{SpriteFolder}/{fileName}";

            // 이미 있으면(디자이너가 바꿔 그렸을 수도 있으니) 그대로 씀
            Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null) return existing;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.spriteBorder = new Vector4(border, border, border, border);
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // 씬에서 이미 쓰는 한글 TMP 폰트
        private static TMP_FontAsset FindKoreanFont()
        {
            foreach (TextMeshProUGUI t in Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include))
                if (t.font != null && !t.font.name.StartsWith("LiberationSans")) return t.font;
            return null;
        }
    }
}