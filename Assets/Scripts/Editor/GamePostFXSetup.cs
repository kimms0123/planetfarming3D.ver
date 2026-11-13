#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using FarmingSystem.Effects;

namespace FarmingSystem.EditorTools
{
    // 게임 화면 보정 만들기 (PV 화면 보정과 같은 느낌)
    // 메뉴: Tools → 행성농사일지 → 화면 보정 만들기 (게임용)
    // - 보정 설정 파일 Assets/Settings/Game_PostProcess.asset (초점·색감·빛 번짐·가장자리 어둡게)
    // - 씬에 Game_PostProcess 오브젝트 (Global Volume + GamePostFX)
    // - Main Camera의 Post Processing 켜기 + 계단 현상 줄이기(SMAA High)
    public static class GamePostFXSetup
    {
        private const string Folder = "Assets/Settings";
        private const string ProfilePath = Folder + "/Game_PostProcess.asset";

        [MenuItem("Tools/행성농사일지/화면 보정 만들기 (게임용)")]
        private static void Setup()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "Settings");
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            AddIfMissing<DepthOfField>(profile, d =>
            {
                d.mode.Override(DepthOfFieldMode.Bokeh);
                d.focusDistance.Override(10f);
                d.aperture.Override(10f);
                d.focalLength.Override(50f);
            });
            AddIfMissing<ColorAdjustments>(profile, c => { c.contrast.Override(8f); c.saturation.Override(12f); });
            AddIfMissing<Bloom>(profile, b => { b.intensity.Override(0.6f); b.threshold.Override(0.9f); b.scatter.Override(0.7f); });
            AddIfMissing<Vignette>(profile, v => { v.intensity.Override(0.22f); v.smoothness.Override(0.45f); });
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            GamePostFX fx = Object.FindAnyObjectByType<GamePostFX>(FindObjectsInactive.Include);
            GameObject go;
            if (fx != null) go = fx.gameObject;
            else
            {
                go = new GameObject("Game_PostProcess");
                Undo.RegisterCreatedObjectUndo(go, "화면 보정 만들기");
            }
            Volume vol = go.GetComponent<Volume>();
            if (vol == null) vol = Undo.AddComponent<Volume>(go);
            vol.isGlobal = true;
            vol.priority = 10f;
            vol.sharedProfile = profile;
            if (go.GetComponent<GamePostFX>() == null) Undo.AddComponent<GamePostFX>(go);

            string camMsg;
            Camera cam = Camera.main;
            if (cam != null)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                Undo.RecordObject(data, "카메라 설정");
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.antialiasingQuality = AntialiasingQuality.High;
                EditorUtility.SetDirty(data);
                camMsg = $"'{cam.name}' 카메라: Post Processing 켬, Anti-aliasing SMAA High";
            }
            else camMsg = "⚠ Main Camera를 못 찾았어요. 카메라 Inspector에서 Rendering → Post Processing을 직접 체크해 주세요";

            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeObject = go;
            EditorUtility.DisplayDialog("화면 보정 (게임용)",
                "완료!\n\n· 씬에 Game_PostProcess 생성\n· " + camMsg +
                "\n\n색감 값은 Game_PostProcess의 GamePostFX에서 조절 (플레이 중에도 바로 바뀜)\nCtrl+S로 씬 저장", "확인");
        }

        private static void AddIfMissing<T>(VolumeProfile profile, System.Action<T> init) where T : VolumeComponent
        {
            if (profile.Has<T>()) return;
            T comp = profile.Add<T>(false);
            comp.name = typeof(T).Name;
            comp.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            init(comp);
            AssetDatabase.AddObjectToAsset(comp, profile);
        }
    }
}
#endif