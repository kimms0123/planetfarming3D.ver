using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using FarmingSystem.Player;

namespace FarmingSystem.Save
{
    // 게임 설정 (시작 화면 OPTION) - 바꾸면 바로 적용되고 컴퓨터에 저장됨 (저장 칸과 상관없이 공통)
    //  - 전체 소리: 게임의 모든 소리 크기
    //  - 마우스 감도: 카메라 돌리는 속도 (100% = 카메라 스크립트에 원래 설정된 값)
    //  - 카메라 상하 반전
    //  - 전체 화면 (에디터에서는 변화 없음, 빌드에서 적용)
    // 게임을 켜면 저장된 값을 자동으로 적용 (설치 필요 없음)
    // 배경음·효과음을 따로 조절하는 건 배경음악이 들어갈 때 같이 추가 예정
    public static class GameSettings
    {
        private const string KeyMaster = "opt_master", KeyMouse = "opt_mouse", KeyInvert = "opt_invert", KeyFull = "opt_fullscreen";

        public static float MasterVolume
        {
            get => PlayerPrefs.GetFloat(KeyMaster, 1f);
            set { PlayerPrefs.SetFloat(KeyMaster, Mathf.Clamp01(value)); PlayerPrefs.Save(); ApplyAudio(); }
        }

        public static float MouseSensitivity
        {
            get => PlayerPrefs.GetFloat(KeyMouse, 1f);
            set { PlayerPrefs.SetFloat(KeyMouse, Mathf.Clamp(value, 0.1f, 5f)); PlayerPrefs.Save(); ApplyCamera(); }
        }

        public static bool InvertY
        {
            get => PlayerPrefs.GetInt(KeyInvert, 0) == 1;
            set { PlayerPrefs.SetInt(KeyInvert, value ? 1 : 0); PlayerPrefs.Save(); ApplyCamera(); }
        }

        public static bool Fullscreen
        {
            get => PlayerPrefs.GetInt(KeyFull, Screen.fullScreen ? 1 : 0) == 1;
            set { PlayerPrefs.SetInt(KeyFull, value ? 1 : 0); PlayerPrefs.Save(); ApplyScreen(); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            ApplyAll();
            SceneManager.sceneLoaded += (s, m) => ApplyCamera();
        }

        public static void ApplyAll()
        {
            ApplyAudio();
            ApplyCamera();
            ApplyScreen();
        }

        private static void ApplyAudio() => AudioListener.volume = MasterVolume;

        private static void ApplyScreen()
        {
            if (Application.isEditor || !PlayerPrefs.HasKey(KeyFull)) return; // 한 번도 안 바꿨으면 그대로
            Screen.fullScreenMode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        }

        // 카메라 감도: 팀 카메라 스크립트(ThirdPersonCameraRig)는 고치지 않고, 원래 값 × 감도로 맞춤
        private static readonly string[] SensFields = { "freeLookYawSensitivity", "freeLookPitchSensitivity", "dragYawSensitivity", "dragPitchSensitivity" };
        private static readonly Dictionary<ThirdPersonCameraRig, float[]> baseSens = new Dictionary<ThirdPersonCameraRig, float[]>(); // 카메라마다 원래 값

        private static void ApplyCamera()
        {
            ThirdPersonCameraRig rig = Object.FindAnyObjectByType<ThirdPersonCameraRig>(FindObjectsInactive.Include);
            if (rig == null) return;
            const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
            System.Type type = typeof(ThirdPersonCameraRig);
            if (!baseSens.TryGetValue(rig, out float[] bases))
            {
                bases = new float[SensFields.Length];
                for (int i = 0; i < SensFields.Length; i++)
                {
                    FieldInfo fi = type.GetField(SensFields[i], F);
                    bases[i] = fi != null && fi.FieldType == typeof(float) ? (float)fi.GetValue(rig) : 0f;
                }
                baseSens[rig] = bases;
            }
            float mult = MouseSensitivity;
            for (int i = 0; i < SensFields.Length; i++)
            {
                FieldInfo fi = type.GetField(SensFields[i], F);
                if (fi != null && fi.FieldType == typeof(float)) fi.SetValue(rig, bases[i] * mult);
            }
            FieldInfo inv = type.GetField("invertPitch", F);
            if (inv != null && inv.FieldType == typeof(bool)) inv.SetValue(rig, InvertY);
        }
    }
}