using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using FarmingSystem.Core;
using FarmingSystem.NPC;
using FarmingSystem.Player;
using FarmingSystem.Save;
using FarmingSystem.UI.Shop;

namespace FarmingSystem.Building
{
    // 내 우주선에서 잠자기 → 다음날 아침 6시 (설치 필요 없음 - 게임이 시작되면 저절로 생김)
    //  - 다 지어진 '내 우주선'(Building_player_spaceship) 가까이에서 우주선을 바라보면 "E 잠자기" 안내
    //  - E → "오늘은 이만 쉴까요?" (잘래요 / 아직이요)
    //  - 잘래요 → 화면이 어두워지고 다음날 아침 6:00 → 다시 밝아짐
    //    · 하루가 바뀌므로 작물 성장·잡초·날씨·상인 일정이 평소처럼 처리되고 자동 저장도 됨
    //    · 자정이 지난 새벽(0~6시)에 자면 날짜는 그대로 두고 그날 아침 6시로 (하루를 건너뛰지 않게)
    public class ShipSleep : MonoBehaviour, TalkMarkers.IPromptSource
    {
        private const string ShipName = "player_spaceship"; // 건물 데이터 이름에 이게 들어가면 내 우주선
        private const int MorningHour = 6;
        private const float InteractRange = 2.2f;   // 우주선 가장자리까지 거리 (m)
        private const float MaxLookAngle = 75f;     // 우주선 쪽을 이 각도 안으로 바라봐야 함

        private static ShipSleep instance;
        private PlacedBuilding ship;
        private Bounds shipBounds;
        private float findTimer;
        private bool asking, sleeping;
        private Canvas canvas;
        private Image fade;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (instance != null) return;
            var go = new GameObject("ShipSleep");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<ShipSleep>();
        }

        private void OnEnable() => TalkMarkers.Register(this);
        private void OnDisable() => TalkMarkers.Unregister(this);

        private void Update()
        {
            findTimer -= Time.unscaledDeltaTime;
            if (findTimer <= 0f) { findTimer = 1f; FindShip(); }

            if (asking || sleeping || TitleScreen.IsShowing || UIInputBlocker.IsBlocked) return;
            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame && CanSleep(out _)) Ask();
        }

        // 다 지어진 내 우주선 찾기
        private void FindShip()
        {
            ship = null;
            BuildingSystem sys = BuildingSystem.Instance;
            if (sys == null) return;
            foreach (PlacedBuilding b in sys.Buildings)
            {
                if (b == null || b.Data == null || !b.Data.name.Contains(ShipName)) continue;
                if (b.State != PlacedBuilding.BuildState.Complete) continue;
                if (!BuildingSystem.TryGetBounds(b.transform, out shipBounds)) continue;
                ship = b;
                return;
            }
        }

        // 우주선 가까이에서 우주선 쪽을 보고 있는지 (+ 안내를 띄울 자리)
        private bool CanSleep(out Vector3 promptPos)
        {
            promptPos = Vector3.zero;
            if (ship == null || GameClock.Instance == null) return false;
            PlayerController pc = FindAnyObjectByType<PlayerController>();
            if (pc == null || !pc.enabled) return false;
            Vector3 p = pc.transform.position;
            Vector3 closest = shipBounds.ClosestPoint(new Vector3(p.x, shipBounds.center.y, p.z));
            Vector3 flat = closest - p;
            flat.y = 0f;
            if (flat.magnitude > InteractRange) return false;
            Vector3 toShip = shipBounds.center - p;
            toShip.y = 0f;
            if (toShip.sqrMagnitude > 0.01f && Vector3.Angle(pc.transform.forward, toShip) > MaxLookAngle) return false;
            promptPos = new Vector3(closest.x, p.y + 1.6f, closest.z); // 우주선 쪽, 캐릭터 얼굴 높이
            return true;
        }

        public bool TryGetPrompt(out string label, out Vector3 worldPos)
        {
            label = "잠자기";
            if (asking || sleeping) { worldPos = Vector3.zero; return false; }
            return CanSleep(out worldPos);
        }

        // ───────────────────────── 물어보기 ─────────────────────────

        private void Ask()
        {
            ShopConversationUI view = FindAnyObjectByType<ShopConversationUI>(FindObjectsInactive.Include);
            if (view == null) view = new GameObject("NPCConversationUI").AddComponent<ShopConversationUI>();
            asking = true;
            UIInputBlocker.Block(this);
            int h = GameClock.Instance.CurrentHour;
            string line = h < MorningHour ? "벌써 새벽이에요. 얼른 자고 아침에 일어날까요?"
                        : h < 18 ? "아직 밝은데… 그래도 오늘은 이만 쉴까요?"
                        : "오늘은 이만 쉴까요?";
            view.Show("내 우주선", line, new[] { "잘래요", "아직이요" }, OnChoose, 1);
        }

        private void OnChoose(int index)
        {
            asking = false;
            UIInputBlocker.Unblock(this);
            if (index == 0) StartCoroutine(SleepRoutine());
        }

        // ───────────────────────── 잠자기 ─────────────────────────

        private IEnumerator SleepRoutine()
        {
            sleeping = true;
            UIInputBlocker.Block(this);
            PlayerController pc = FindAnyObjectByType<PlayerController>();
            bool pcWasOn = pc != null && pc.enabled;
            if (pcWasOn) pc.enabled = false;
            if (canvas == null) BuildUI();
            canvas.enabled = true;
            yield return Fade(0f, 1f, 1f);

            // 다음날 아침 (새벽이면 그날 아침)
            GameClock clock = GameClock.Instance;
            if (clock != null)
            {
                if (clock.CurrentHour < MorningHour) clock.AdvanceMinutes(MorningHour * 60 - (clock.CurrentHour * 60 + clock.CurrentMinute));
                else clock.Sleep(); // 하루 넘김 → 작물·잡초·날씨·상인·자동 저장
                Debug.Log($"[잠자기] {clock.CurrentDay}일차 아침 {clock.CurrentHour:00}:{clock.CurrentMinute:00}");
            }

            for (float t = 0f; t < 0.8f; t += Time.unscaledDeltaTime) yield return null; // 잠깐 검은 화면

            yield return Fade(1f, 0f, 1f);
            canvas.enabled = false;
            if (pcWasOn && pc != null) pc.enabled = true;
            UIInputBlocker.Unblock(this);
            sleeping = false;
        }

        private IEnumerator Fade(float from, float to, float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / seconds);
                fade.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, k));
                yield return null;
            }
            fade.color = new Color(0f, 0f, 0f, to);
        }

        private void BuildUI()
        {
            var go = new GameObject("ShipSleepCanvas", typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 950; // 다른 UI보다 위
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var f = new GameObject("Fade", typeof(RectTransform), typeof(Image));
            f.transform.SetParent(go.transform, false);
            var frt = (RectTransform)f.transform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = frt.offsetMax = Vector2.zero;
            fade = f.GetComponent<Image>();
            fade.color = Color.clear;
        }
    }
}