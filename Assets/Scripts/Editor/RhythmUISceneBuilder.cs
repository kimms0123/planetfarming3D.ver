#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FarmingSystem.Rhythm;

namespace FarmingSystem.EditorTools
{
    // 수확 리듬게임 UI를 씬에 놓기
    // 메뉴: Tools → 행성농사일지 → 리듬게임 → 리듬게임 UI 씬에 만들기
    // - 지금까지 플레이할 때 코드가 만들던 UI와 똑같은 UI를 HarvestRhythmGame 오브젝트 아래(HarvestRhythmCanvas)에 만들어 둠
    // - 코드가 그리던 기본 그림(원·고리·화살표·별·둥근 판·음표)은 PNG로 저장 → Assets/UI/Rhythm
    // - 이후로는 플레이해도 UI를 새로 만들지 않고 씬의 UI를 그대로 씀 → 씬에서 위치·크기·색·이미지·글자를 마음대로 수정
    // - 다시 실행하면 기존 씬 UI를 지우고 새로 만듦 (직접 고친 내용은 사라지니 주의)
    public static class RhythmUISceneBuilder
    {
        private const string Folder = "Assets/UI/Rhythm";
        private static readonly string[] SpriteNames =
            { "Rhythm_Disc", "Rhythm_Ring", "Rhythm_ThickRing", "Rhythm_Arrow", "Rhythm_Star", "Rhythm_Rounded", "Rhythm_Note" };

        [MenuItem("Tools/행성농사일지/리듬게임/리듬게임 UI 씬에 만들기")]
        private static void Build()
        {
            HarvestRhythmGame game = Object.FindAnyObjectByType<HarvestRhythmGame>(FindObjectsInactive.Include);
            if (game == null)
            {
                EditorUtility.DisplayDialog("리듬게임 UI", "씬에 HarvestRhythmGame이 없어요. 게임 씬을 열고 다시 실행해 주세요.", "확인");
                return;
            }

            HarvestRhythmUI old = game.GetComponentInChildren<HarvestRhythmUI>(true);
            if (old != null)
            {
                if (!EditorUtility.DisplayDialog("리듬게임 UI",
                        "이미 씬에 리듬게임 UI가 있어요.\n지우고 새로 만들까요? (씬에서 직접 고친 위치·크기·색은 사라져요)", "새로 만들기", "취소"))
                    return;
                Undo.DestroyObjectImmediate(old.gameObject);
            }
            // 예전에 남은 같은 이름 캔버스도 정리
            Transform leftover = game.transform.Find("HarvestRhythmCanvas");
            if (leftover != null) Undo.DestroyObjectImmediate(leftover.gameObject);

            // 1. 기본 그림을 PNG로 저장
            Sprite[] saved = SaveDefaultSprites(game);
            if (saved == null) return;

            // 2. UI 만들기 + 연결표 채우기
            HarvestRhythmUI ui = game.EditorBuildSceneUI(saved);
            Undo.RegisterCreatedObjectUndo(ui.gameObject, "리듬게임 UI 씬에 만들기");

            // 3. HarvestRhythmGame의 Scene UI 칸에 연결 (씬에 저장되게)
            var so = new SerializedObject(game);
            SerializedProperty p = so.FindProperty("sceneUI");
            if (p != null) { p.objectReferenceValue = ui; so.ApplyModifiedProperties(); }

            ui.gameObject.SetActive(true); // 편집하기 좋게 켜 둠 (플레이하면 자동으로 숨겨졌다가 리듬게임 때만 켜짐)
            EditorUtility.SetDirty(game);
            EditorUtility.SetDirty(ui);
            EditorSceneManager.MarkSceneDirty(game.gameObject.scene);
            Selection.activeObject = ui.gameObject;
            EditorGUIUtility.PingObject(ui.gameObject);

            Debug.Log("[리듬게임 UI] 씬에 만들었어요 → Hierarchy: " + game.name + " / HarvestRhythmCanvas\n" +
                      "  HUD: 판정 원(JudgeRoot), 수확 목표(GoalPanel), 점수 게이지(ProgressBar), 콤보(Combo), 남은 노트(RemainPanel), 방향키(KeyPad), 안내(Guide)\n" +
                      "  결과창(ResultPanel)은 꺼져 있어요. 고칠 때 체크해서 켜고, 다 고치면 다시 끄기\n" +
                      "  안 쓰는 UI는 지우지 말고 체크를 꺼서 숨기기 (지우면 연결이 끊겨요)");
            EditorUtility.DisplayDialog("리듬게임 UI",
                "씬에 리듬게임 UI를 만들었어요!\n\nHierarchy → " + game.name + " → HarvestRhythmCanvas\n\n" +
                "· 위치·크기·색·이미지·글자를 씬에서 바로 고치면 돼요\n" +
                "· 결과창(ResultPanel)은 꺼져 있어요 (고칠 때만 켜기)\n" +
                "· 안 쓰는 UI는 지우지 말고 체크를 꺼서 숨기기\n" +
                "· 다 고치면 HarvestRhythmCanvas 체크를 꺼 둬도 돼요 (게임 화면을 가리지 않게)\n" +
                "· 씬 저장(Ctrl+S) 잊지 마세요", "확인");
        }

        // 코드가 그리던 그림 7개 → PNG 에셋 (이미 있으면 그대로 사용 → 그래픽 팀 그림으로 덮어써도 유지됨)
        private static Sprite[] SaveDefaultSprites(HarvestRhythmGame game)
        {
            EnsureFolder(Folder);
            Sprite[] generated = null;
            var result = new Sprite[SpriteNames.Length];

            for (int i = 0; i < SpriteNames.Length; i++)
            {
                string path = $"{Folder}/{SpriteNames[i]}.png";
                Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (existing != null) { result[i] = existing; continue; }

                if (generated == null) generated = game.EditorMakeDefaultSprites();
                Texture2D tex = generated[i].texture;
                File.WriteAllBytes(path, tex.EncodeToPNG());
                AssetDatabase.ImportAsset(path);

                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled = false;
                imp.filterMode = FilterMode.Bilinear;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.spritePixelsPerUnit = 100f;
                if (SpriteNames[i] == "Rhythm_Rounded") imp.spriteBorder = new Vector4(24, 24, 24, 24); // 9-슬라이스 (판 크기를 바꿔도 모서리 유지)
                imp.SaveAndReimport();
                result[i] = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }

            // 만들 때 쓴 임시 텍스처 정리
            if (generated != null)
                foreach (Sprite s in generated)
                    if (s != null) { Object.DestroyImmediate(s.texture); Object.DestroyImmediate(s); }

            for (int i = 0; i < result.Length; i++)
                if (result[i] == null)
                {
                    EditorUtility.DisplayDialog("리듬게임 UI", $"{SpriteNames[i]} 그림을 저장하지 못했어요. Console을 확인해 주세요.", "확인");
                    return null;
                }
            return result;
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