using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using FarmingSystem.Core;
using FarmingSystem.Inventory;
using FarmingSystem.Player;
using FarmingSystem.Rhythm;
using FarmingSystem.Save;
using FarmingSystem.UI;
using FarmingSystem.Weather;

namespace FarmingSystem.Audio
{
    // 게임 소리 관리자. 씬에 넣을 필요 없음 (게임 시작하면 저절로 생김)
    // 소리 목록: Assets/Resources/SoundLibrary.asset (메뉴: Tools → 행성농사일지 → 소리 → 소리 설정하기)
    //
    // 배경음 (상황에 따라 저절로 바뀜, 겹쳐서 부드럽게)
    //  - 오프닝 중 = intro (한 번) / 시작 화면 = title / 태풍 = typhoon / 비 = rain / 밤(20~5시) = night / 그 외 = day
    //  - 리듬게임 중엔 작게, 결과 징글(따단)이 나올 땐 잠깐 작게
    // 환경음: 맑은 낮 새소리 / 맑은 밤 풀벌레 (겨울 제외) / 태풍 때 나무 흔들림 (비·바람·천둥은 WeatherVisuals가 냄)
    // 저절로 나는 효과음 (다른 스크립트 안 고침)
    //  - 버튼 누르기 / 가방 열고 닫기 / 도구 바꾸기 / 도구 부서짐 / 휴대폰 열고 닫기 / 발걸음 (플레이어에 PlayerFootsteps 자동 부착)
    // 다른 스크립트에서 쓰는 법:  GameAudio.Play(SoundId.Coin);  GameAudio.PlayAt(SoundId.Till, 위치);  GameAudio.PlayJingle(SoundId.FishResult);
    public class GameAudio : MonoBehaviour
    {
        private const int PoolSize = 16;
        private const float NightStart = 20f, NightEnd = 5f;

        private static GameAudio instance;
        private static bool warned;

        private SoundLibrary lib;
        private readonly AudioSource[] pool = new AudioSource[PoolSize];
        private int poolIndex;
        private readonly Dictionary<SoundId, float> lastPlayed = new Dictionary<SoundId, float>();

        // 배경음: 두 개를 번갈아 쓰며 겹쳐서 넘어감
        private AudioSource bgmA, bgmB, bgmActive;
        private AudioClip bgmWanted;
        private float duckUntil, lastJingle = -10f;
        private bool introActive;

        // 환경음
        private AudioSource ambBirds, ambInsects, ambLeaves;

        // 저절로 나는 소리 확인용
        private InventoryManager hookedInventory;
        private bool phoneWasOpen;
        private float nextPlayerSearch;

        /// <summary>효과음·환경음 크기 (설정의 효과음 x 전체는 AudioListener가 처리)</summary>
        public static float SfxVolume => GameSettings.SfxVolume;
        public static float BgmVolume => GameSettings.BgmVolume;

        /// <summary>오프닝 중인지 (오프닝 배경음 + 시작 화면 중에도 게임 소리 허용)</summary>
        public static bool IntroActive => instance != null && instance.introActive;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (instance != null) return;
            var go = new GameObject("[GameAudio]");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<GameAudio>();
        }

        private void Awake()
        {
            lib = Resources.Load<SoundLibrary>("SoundLibrary");
            if (lib == null && !warned)
            {
                warned = true;
                Debug.LogWarning("[소리] Assets/Resources/SoundLibrary가 없어요 → Tools → 행성농사일지 → 소리 → 소리 설정하기");
            }
            for (int i = 0; i < PoolSize; i++) pool[i] = NewSource("Sfx" + i, false);
            bgmA = NewSource("BgmA", true);
            bgmB = NewSource("BgmB", true);
            bgmActive = bgmA;
            ambBirds = NewSource("AmbBirds", true);
            ambInsects = NewSource("AmbInsects", true);
            ambLeaves = NewSource("AmbLeaves", true);
            FarmingSystem.Farming.CropCollection.OnRegistered += OnCropRegistered;
        }

        // 도감에 새 작물: 수확 결과 징글이 끝난 뒤에 반짝
        private void OnCropRegistered(FarmingSystem.Farming.CropData crop) => StartCoroutine(PlayLater(SoundId.CollectionNew, 1.8f));

        private System.Collections.IEnumerator PlayLater(SoundId id, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            Play(id);
        }

        private AudioSource NewSource(string n, bool loop)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            AudioSource s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = loop;
            s.spatialBlend = 0f;
            s.volume = 0f;
            return s;
        }

        // ───────────────────────── 효과음 API ─────────────────────────

        /// <summary>화면 소리 (UI·징글처럼 위치 없는 소리)</summary>
        public static void Play(SoundId id, float volume = 1f)
        {
            if (instance != null) instance.PlayInternal(id, volume, null, null);
        }

        /// <summary>그 자리에서 나는 소리 (멀면 작게 들림)</summary>
        public static void PlayAt(SoundId id, Vector3 position, float volume = 1f)
        {
            if (instance == null || !GameplayAllowed) return;
            instance.PlayInternal(id, volume, position, null);
        }

        /// <summary>움직이는 물체를 따라다니는 소리 (우주선 엔진 등)</summary>
        public static void PlayFollow(SoundId id, Transform target, float volume = 1f)
        {
            if (instance == null || target == null || !GameplayAllowed) return;
            instance.PlayInternal(id, volume, target.position, target);
        }

        /// <summary>결과 징글 (따단) - 나오는 동안 배경음을 잠깐 작게</summary>
        public static void PlayJingle(SoundId id, float volume = 1f)
        {
            if (instance == null) return;
            float len = instance.PlayInternal(id, volume, null, null);
            if (len > 0f) { instance.duckUntil = Time.unscaledTime + len + 0.3f; instance.lastJingle = Time.unscaledTime; }
        }

        /// <summary>새 게임 오프닝 시작/끝 (TitleScreen이 부름)</summary>
        public static void SetIntro(bool on)
        {
            if (instance != null) instance.introActive = on;
        }

        // 시작 화면이 떠 있는 동안(오프닝 제외) 게임 속 소리는 안 냄 (불러오기 중 밭·물고기 소리 등)
        private static bool GameplayAllowed => !TitleScreen.IsShowing || IntroActive;

        private float PlayInternal(SoundId id, float volume, Vector3? position, Transform follow)
        {
            if (lib == null) return 0f;
            SoundLibrary.Sfx e = lib.Get(id);
            if (e == null || e.clips == null || e.clips.Length == 0) return 0f;
            float now = Time.unscaledTime;
            if (id == SoundId.Notice && now - lastJingle < 0.3f) return 0f; // 징글과 같이 뜨는 알림(퀘스트 등)은 징글만
            if (lastPlayed.TryGetValue(id, out float last) && now - last < e.cooldown) return 0f;
            AudioClip clip = e.clips[Random.Range(0, e.clips.Length)];
            if (clip == null) return 0f;
            lastPlayed[id] = now;

            float pitch = 1f + Random.Range(-e.pitchJitter, e.pitchJitter);
            float vol = e.volume * volume * SfxVolume;

            if (follow != null)
            {
                // 따라다니는 소리는 따로 만들어서 대상의 자식으로 (끝나면 지워짐)
                var go = new GameObject("Sfx_" + id);
                go.transform.SetParent(follow, false);
                AudioSource s = go.AddComponent<AudioSource>();
                Setup3D(s);
                s.pitch = pitch;
                s.PlayOneShot(clip, vol);
                Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.1f);
                return clip.length;
            }

            AudioSource src = pool[poolIndex];
            poolIndex = (poolIndex + 1) % PoolSize;
            src.Stop();
            src.transform.SetParent(transform, false);
            if (position.HasValue)
            {
                src.transform.position = position.Value;
                Setup3D(src);
            }
            else
            {
                src.transform.localPosition = Vector3.zero;
                src.spatialBlend = 0f;
            }
            src.clip = clip;
            src.pitch = pitch;
            src.volume = vol;
            src.loop = false;
            src.Play();
            return clip.length;
        }

        private static void Setup3D(AudioSource s)
        {
            s.spatialBlend = 0.75f;          // 완전 3D면 너무 작아져서 조금 섞음
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 4f;
            s.maxDistance = 35f;
            s.dopplerLevel = 0f;
        }

        // ───────────────────────── 매 프레임 ─────────────────────────

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            UpdateBgm(dt);
            UpdateAmbience(dt);
            UpdateAutoSounds();
            UpdateUIClicks();
        }

        private void UpdateBgm(float dt)
        {
            if (lib == null) return;
            bool loop = true;
            AudioClip want;
            if (introActive) { want = lib.intro; loop = false; }
            else if (TitleScreen.IsShowing) want = lib.title;
            else
            {
                WeatherManager w = WeatherManager.Instance;
                GameClock clock = GameClock.Instance;
                bool night = clock != null && (clock.CurrentHour >= NightStart || clock.CurrentHour < NightEnd);
                if (w != null && w.IsTyphoon && lib.typhoon != null) want = lib.typhoon;
                else if (w != null && w.IsRaining && lib.rain != null) want = lib.rain;
                else if (night && lib.night != null) want = lib.night;
                else want = lib.day;
            }

            if (want != bgmWanted)
            {
                bgmWanted = want;
                bgmActive = bgmActive == bgmA ? bgmB : bgmA; // 새 곡은 다른 쪽에서 시작, 원래 곡은 줄어듦
                bgmActive.Stop();
                bgmActive.clip = want;
                bgmActive.loop = loop;
                bgmActive.volume = 0f;
                if (want != null) bgmActive.Play();
            }

            float duck = 1f;
            HarvestRhythmGame rhythm = HarvestRhythmGame.Instance;
            if (rhythm != null && rhythm.IsPlaying) duck = 0.2f;
            if (Time.unscaledTime < duckUntil) duck = Mathf.Min(duck, 0.25f);
            float target = lib.bgmVolume * BgmVolume * duck;
            float step = dt / Mathf.Max(0.1f, lib.crossfade);
            AudioSource other = bgmActive == bgmA ? bgmB : bgmA;
            bgmActive.volume = Mathf.MoveTowards(bgmActive.volume, target, step * Mathf.Max(0.05f, target) * 2f);
            other.volume = Mathf.MoveTowards(other.volume, 0f, step);
            if (other.volume <= 0f && other.isPlaying) other.Stop();
        }

        private void UpdateAmbience(float dt)
        {
            if (lib == null) return;
            bool inGame = !TitleScreen.IsShowing;
            WeatherManager w = WeatherManager.Instance;
            GameClock clock = GameClock.Instance;
            bool clear = w == null || !w.IsRaining;
            bool typhoon = w != null && w.IsTyphoon;
            int hour = clock != null ? clock.CurrentHour : 12;
            bool winter = clock != null && clock.CurrentSeason == Season.Winter;
            bool day = hour >= 6 && hour < 19;
            bool night = hour >= 19 || hour < 5;

            float baseVol = lib.ambienceVolume * SfxVolume;
            Fade(ambBirds, lib.birds, inGame && clear && day && !winter ? baseVol * 0.8f : 0f, dt);
            Fade(ambInsects, lib.insects, inGame && clear && night && !winter ? baseVol * 0.6f : 0f, dt);
            Fade(ambLeaves, lib.leaves, inGame && typhoon ? baseVol : 0f, dt);
        }

        private static void Fade(AudioSource s, AudioClip clip, float target, float dt)
        {
            if (clip == null) { if (s.isPlaying) s.Stop(); return; }
            if (s.clip != clip) { s.clip = clip; s.time = Random.Range(0f, clip.length * 0.9f); }
            s.volume = Mathf.MoveTowards(s.volume, target, dt * 0.25f);
            if (s.volume > 0.001f && !s.isPlaying) s.Play();
            else if (s.volume <= 0.001f && s.isPlaying) s.Stop();
        }

        // 다른 스크립트를 고치지 않고 이벤트·상태만 보고 내는 소리
        private void UpdateAutoSounds()
        {
            InventoryManager inv = InventoryManager.Instance;
            if (inv != hookedInventory)
            {
                if (hookedInventory != null)
                {
                    hookedInventory.OnToolBroken -= OnToolBroken;
                    hookedInventory.OnSelectedHotbarIndexChanged -= OnHotbarChanged;
                    hookedInventory.OnInventoryToggled -= OnInventoryToggled;
                }
                hookedInventory = inv;
                if (inv != null)
                {
                    inv.OnToolBroken += OnToolBroken;
                    inv.OnSelectedHotbarIndexChanged += OnHotbarChanged;
                    inv.OnInventoryToggled += OnInventoryToggled;
                }
            }

            if (PhoneMenu.IsOpen != phoneWasOpen)
            {
                phoneWasOpen = PhoneMenu.IsOpen;
                Play(phoneWasOpen ? SoundId.PhoneOpen : SoundId.PhoneClose);
            }

            // 플레이어에 발걸음 붙이기 (못 찾으면 2초마다 다시)
            if (Time.unscaledTime >= nextPlayerSearch)
            {
                nextPlayerSearch = Time.unscaledTime + 2f;
                PlayerController pc = FindAnyObjectByType<PlayerController>();
                if (pc != null && pc.GetComponent<PlayerFootsteps>() == null) pc.gameObject.AddComponent<PlayerFootsteps>();
                if (pc != null) nextPlayerSearch = Time.unscaledTime + 10f;
            }
        }

        private void OnToolBroken(ToolData tool) => Play(SoundId.ToolBreak);
        private void OnHotbarChanged(int index) { if (!TitleScreen.IsShowing) Play(SoundId.ToolSwitch); }
        private void OnInventoryToggled(bool open) { if (!TitleScreen.IsShowing) Play(open ? SoundId.InventoryOpen : SoundId.InventoryClose); }

        // 버튼·토글·슬라이더 등을 마우스로 누르면 딸깍
        private void UpdateUIClicks()
        {
            Mouse mouse = Mouse.current;
            EventSystem es = EventSystem.current;
            if (mouse == null || es == null || !mouse.leftButton.wasPressedThisFrame) return;
            var data = new PointerEventData(es) { position = mouse.position.ReadValue() };
            var hits = new List<RaycastResult>();
            es.RaycastAll(data, hits);
            if (hits.Count == 0) return;
            Selectable sel = hits[0].gameObject.GetComponentInParent<Selectable>();
            if (sel != null && sel.IsInteractable()) Play(SoundId.UIClick);
        }

        private void OnDestroy()
        {
            if (hookedInventory != null)
            {
                hookedInventory.OnToolBroken -= OnToolBroken;
                hookedInventory.OnSelectedHotbarIndexChanged -= OnHotbarChanged;
                hookedInventory.OnInventoryToggled -= OnInventoryToggled;
            }
            FarmingSystem.Farming.CropCollection.OnRegistered -= OnCropRegistered;
            if (instance == this) instance = null;
        }
    }
}
