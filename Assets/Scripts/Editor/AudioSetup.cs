#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FarmingSystem.Audio;
using FarmingSystem.Weather;

namespace FarmingSystem.EditorTools
{
    // 소리 설정 도구 (에디터 전용)
    // 메뉴: Tools → 행성농사일지 → 소리 → 소리 설정하기
    //  1. 프로젝트에서 이름이 SFX_ / BGM_ / AMB_ 로 시작하는 오디오 파일을 모두 찾음 (폴더 상관없음)
    //  2. 가져오기 설정 맞춤 (배경음·환경음 = 스트리밍, 효과음 = 미리 풀어 둠)
    //  3. Assets/Resources/SoundLibrary.asset 를 만들거나 고쳐서 이름대로 연결
    //       SFX_Till, SFX_Till_1, SFX_Till_2 … → 밭 갈기 (여러 개면 무작위)
    //       BGM_Title / BGM_Intro / BGM_Day / BGM_Night / BGM_Rain / BGM_Typhoon
    //       AMB_Birds / AMB_Insects / AMB_Leaves
    //  4. 씬의 WeatherVisuals에 비(AMB_Rain)·바람(AMB_Wind)·천둥(SFX_Thunder…) 연결 (소리 내는 부품이 없으면 만듦)
    // 진짜 소리로 바꿀 때: 같은 이름으로 파일을 덮어쓰거나, 새 파일 이름을 규칙대로 짓고 메뉴를 다시 누르면 끝
    // 이미 조절한 크기·음높이 흔들림은 다시 눌러도 유지됨 (파일 연결만 새로 고침)
    public static class AudioSetup
    {
        private const string LibPath = "Assets/Resources/SoundLibrary.asset";

        [MenuItem("Tools/행성농사일지/소리/소리 설정하기")]
        private static void Setup()
        {
            string log = "";
            var clips = new Dictionary<string, List<AudioClip>>(); // "SFX_Till" → [Till_1, Till_2 …]
            var single = new Dictionary<string, AudioClip>();      // 정확한 이름 → 파일
            int imported = 0;

            foreach (string g in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                string name = Path.GetFileNameWithoutExtension(path);
                bool isBgm = name.StartsWith("BGM_"), isAmb = name.StartsWith("AMB_"), isSfx = name.StartsWith("SFX_");
                if (!isBgm && !isAmb && !isSfx) continue;
                if (FixImport(path, isBgm || isAmb)) imported++;
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) continue;
                single[name] = clip;
                string baseName = Regex.Replace(name, @"_\d+$", ""); // 끝의 _1, _2 떼기
                if (!clips.TryGetValue(baseName, out var list)) clips[baseName] = list = new List<AudioClip>();
                list.Add(clip);
            }
            if (imported > 0) log += $"· 가져오기 설정 {imported}개 맞춤\n";

            // ---------- SoundLibrary ----------
            EnsureFolder("Assets/Resources");
            var lib = AssetDatabase.LoadAssetAtPath<SoundLibrary>(LibPath);
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<SoundLibrary>();
                AssetDatabase.CreateAsset(lib, LibPath);
                log += "· SoundLibrary를 만들었어요 (" + LibPath + ")\n";
            }
            Undo.RecordObject(lib, "소리 설정");

            var missing = new List<string>();
            foreach (SoundId id in Enum.GetValues(typeof(SoundId)))
            {
                SoundLibrary.Sfx e = lib.sfx.Find(x => x != null && x.id == id);
                if (e == null)
                {
                    e = new SoundLibrary.Sfx { id = id };
                    Defaults(e);
                    lib.sfx.Add(e);
                }
                e.label = id.ToString();
                if (clips.TryGetValue("SFX_" + id, out var found))
                    e.clips = found.OrderBy(c => c.name, StringComparer.Ordinal).ToArray();
                else if (e.clips == null || e.clips.Length == 0 || e.clips.All(c => c == null))
                    missing.Add("SFX_" + id);
            }
            lib.sfx.Sort((a, b) => ((int)a.id).CompareTo((int)b.id));

            lib.title = Pick(single, "BGM_Title", lib.title, missing);
            lib.intro = Pick(single, "BGM_Intro", lib.intro, missing);
            lib.day = Pick(single, "BGM_Day", lib.day, missing);
            lib.night = Pick(single, "BGM_Night", lib.night, null);   // 없어도 됨 (밤에도 기본 배경음)
            lib.rain = Pick(single, "BGM_Rain", lib.rain, missing);
            lib.typhoon = Pick(single, "BGM_Typhoon", lib.typhoon, missing);
            lib.birds = Pick(single, "AMB_Birds", lib.birds, missing);
            lib.insects = Pick(single, "AMB_Insects", lib.insects, missing);
            lib.leaves = Pick(single, "AMB_Leaves", lib.leaves, missing);
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            log += $"· 효과음 {lib.sfx.Count(e => e.clips != null && e.clips.Length > 0)}종류 연결\n";

            // ---------- 날씨 소리 (WeatherVisuals) ----------
            WeatherVisuals wv = UnityEngine.Object.FindAnyObjectByType<WeatherVisuals>(FindObjectsInactive.Include);
            if (wv != null)
            {
                var so = new SerializedObject(wv);
                single.TryGetValue("AMB_Rain", out AudioClip rain);
                single.TryGetValue("AMB_Wind", out AudioClip wind);
                SetSource(so, wv, "rainAudio", "RainAudio", rain);
                SetSource(so, wv, "windAudio", "WindAudio", wind);
                SetSource(so, wv, "thunderAudio", "ThunderAudio", null);
                if (clips.TryGetValue("SFX_Thunder", out var thunder))
                {
                    SerializedProperty arr = so.FindProperty("thunderClips");
                    arr.arraySize = thunder.Count;
                    for (int i = 0; i < thunder.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = thunder[i];
                }
                so.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(wv.gameObject.scene);
                log += "· 날씨(WeatherVisuals)에 비·바람·천둥 소리 연결\n";
            }
            else log += "※ 씬에 WeatherVisuals가 없어서 비·바람·천둥은 연결 못 했어요 (게임 씬에서 다시)\n";

            if (missing.Count > 0) log += "\n없는 소리 (조용히 넘어감):\n" + string.Join(", ", missing) + "\n";
            Selection.activeObject = lib;
            EditorUtility.DisplayDialog("소리 설정", log + "\n씬 저장: Ctrl+S", "확인");
        }

        private static AudioClip Pick(Dictionary<string, AudioClip> single, string name, AudioClip current, List<string> missing)
        {
            if (single.TryGetValue(name, out AudioClip c)) return c;
            if (current == null) missing?.Add(name);
            return current;
        }

        // 날씨 부품의 소리 칸이 비어 있으면 자식으로 AudioSource를 만들어 넣고, 파일 연결
        private static void SetSource(SerializedObject so, WeatherVisuals wv, string field, string childName, AudioClip clip)
        {
            SerializedProperty p = so.FindProperty(field);
            var src = p.objectReferenceValue as AudioSource;
            if (src == null)
            {
                Transform child = wv.transform.Find(childName);
                if (child == null)
                {
                    child = new GameObject(childName).transform;
                    child.SetParent(wv.transform, false);
                    Undo.RegisterCreatedObjectUndo(child.gameObject, "소리 설정");
                }
                src = child.GetComponent<AudioSource>();
                if (src == null) src = child.gameObject.AddComponent<AudioSource>();
                p.objectReferenceValue = src;
            }
            src.playOnAwake = false;
            src.spatialBlend = 0f; // 화면 전체에 들리는 소리
            if (clip != null) { src.clip = clip; src.loop = true; }
            EditorUtility.SetDirty(src);
        }

        // 처음 만들 때 소리별 기본 크기·흔들림
        private static void Defaults(SoundLibrary.Sfx e)
        {
            switch (e.id)
            {
                case SoundId.FootstepGrass:
                case SoundId.FootstepSnow: e.volume = 0.35f; e.pitchJitter = 0.12f; e.cooldown = 0.12f; break;
                case SoundId.UIClick: e.volume = 0.45f; e.pitchJitter = 0.03f; e.cooldown = 0.03f; break;
                case SoundId.ToolSwitch: e.volume = 0.4f; e.cooldown = 0.03f; break;
                case SoundId.Till:
                case SoundId.Water:
                case SoundId.Plant:
                case SoundId.ClearCrop:
                case SoundId.Fertilize: e.volume = 0.7f; e.pitchJitter = 0.1f; e.cooldown = 0.08f; break;
                case SoundId.FishNibble: e.volume = 0.5f; e.pitchJitter = 0.15f; break;
                case SoundId.ShipEngine: e.volume = 0.8f; e.pitchJitter = 0f; e.cooldown = 0.5f; break;
                case SoundId.ItemPickup: e.volume = 0.55f; e.pitchJitter = 0.08f; e.cooldown = 0.08f; break;
                case SoundId.RhythmPerfect:
                case SoundId.RhythmGood:
                case SoundId.RhythmNormal:
                case SoundId.RhythmBad:
                case SoundId.FishResult:
                case SoundId.FishResultRare:
                case SoundId.QuestAccept:
                case SoundId.QuestComplete:
                case SoundId.SleepJingle:
                case SoundId.MorningChime: e.volume = 0.8f; e.pitchJitter = 0f; e.cooldown = 0.3f; break;
                default: e.volume = 0.7f; e.pitchJitter = 0.04f; e.cooldown = 0.05f; break;
            }
        }

        // 배경음·환경음: 스트리밍 (메모리 절약) / 효과음: 불러올 때 풀어 둠 (바로 재생)
        private static bool FixImport(string path, bool streaming)
        {
            var ai = AssetImporter.GetAtPath(path) as AudioImporter;
            if (ai == null) return false;
            AudioImporterSampleSettings s = ai.defaultSampleSettings;
            AudioClipLoadType want = streaming ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            if (s.loadType == want && s.compressionFormat == AudioCompressionFormat.Vorbis) return false;
            s.loadType = want;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = streaming ? 0.6f : 0.8f;
            ai.defaultSampleSettings = s;
            ai.loadInBackground = streaming;
            ai.SaveAndReimport();
            return true;
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
