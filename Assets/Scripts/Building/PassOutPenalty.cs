using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FarmingSystem.Audio;
using FarmingSystem.Core;
using FarmingSystem.Economy;
using FarmingSystem.Farming;
using FarmingSystem.Inventory;
using FarmingSystem.Player;
using FarmingSystem.Save;

namespace FarmingSystem.Building
{
    // 강제 취침 (스타듀밸리의 "새벽 2시 기절"처럼)
    //  - 새벽 2시(Pass Out Hour)까지 우주선에서 안 자면 그 자리에서 쓰러짐
    //    → 우주 구조대가 우주선 앞까지 데려다주고, 가진 벨의 10% (최대 1,000벨)를 구조비로 가져감
    //    → 벨이 0이면 대신 가방 속 작물 1개를 수고비로 가져감 (작물도 없으면 그냥 데려다줌)
    //    → 다음 날 아침 6시에 우주선 앞에서 깨어남 (작물·날씨·상인 등 하루 넘김은 자정에 이미 처리돼 있음)
    //  - 30분 전에 경고 ("곧 새벽 2시예요! 우주선으로 돌아가 주무세요")
    //  - 대화·상점·리듬게임·메뉴가 열려 있으면 그게 끝나는 대로 쓰러짐
    // 설치 필요 없음 (게임이 시작되면 저절로 생김). 기존 스크립트는 안 고침 (GameClock 시간 신호만 받음)
    // 값을 바꾸려면: 씬의 아무 오브젝트에 PassOutPenalty를 붙이고 Inspector에서 (붙인 게 있으면 자동 생성은 안 함)
    public class PassOutPenalty : MonoBehaviour
    {
        [Header("언제")]
        [Tooltip("이 시각(새벽, 시)이 되면 쓰러짐")]
        [SerializeField, Range(1, 5)] private int passOutHour = 2;
        [Tooltip("깨어나는 시각 (시)")]
        [SerializeField, Range(5, 10)] private int wakeHour = 6;
        [Tooltip("몇 분 전에 경고할지 (0 = 경고 안 함)")]
        [SerializeField] private int warnMinutesBefore = 30;

        [Header("구조비")]
        [Tooltip("가진 벨 중 가져가는 비율")]
        [SerializeField, Range(0f, 1f)] private float feeRatio = 0.1f;
        [Tooltip("구조비 최대 (벨)")]
        [SerializeField] private int feeMax = 1000;
        [Tooltip("벨이 0일 때 가방에서 작물 1개를 대신 가져감")]
        [SerializeField] private bool takeCropWhenBroke = true;

        private static PassOutPenalty instance;
        private GameClock hooked;
        private bool pending, running, warnedToday;
        private Canvas canvas;
        private Image fade;
        private TextMeshProUGUI centerText, noticeText;
        private CanvasGroup noticeGroup;
        private float noticeTimer;
        private const float NoticeSeconds = 4.5f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindAnyObjectByType<PassOutPenalty>() != null) return;
            var go = new GameObject("PassOutPenalty");
            DontDestroyOnLoad(go);
            go.AddComponent<PassOutPenalty>();
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(this); return; }
            instance = this;
        }

        private void OnDestroy()
        {
            if (hooked != null) hooked.OnMinuteChanged -= HandleMinute;
            if (instance == this) instance = null;
            if (running) UIInputBlocker.Unblock(this);
        }

        private void Update()
        {
            // GameClock이 늦게 생기거나 바뀌어도 다시 연결
            GameClock clock = GameClock.Instance;
            if (clock != hooked)
            {
                if (hooked != null) hooked.OnMinuteChanged -= HandleMinute;
                hooked = clock;
                if (hooked != null) hooked.OnMinuteChanged += HandleMinute;
            }

            // 다른 창이 열려 있었으면 닫히는 대로 쓰러짐
            if (pending && !running && CanPassOutNow()) StartCoroutine(PassOutRoutine());

            if (noticeGroup != null && noticeTimer > 0f)
            {
                noticeTimer -= Time.unscaledDeltaTime;
                float shown = NoticeSeconds - noticeTimer;
                noticeGroup.alpha = Mathf.Min(Mathf.Clamp01(noticeTimer / 0.4f), Mathf.Clamp01(shown / 0.25f)); // 0.25초 나타나고 0.4초 사라짐
            }
        }

        private void HandleMinute(int hour, int minute)
        {
            if (running || TitleScreen.IsShowing) return;
            int now = hour * 60 + minute;
            int limit = passOutHour * 60;

            // 아침이 되면 오늘 경고 기록 초기화 (잠을 잤거나 불러오기로 시간이 바뀐 경우)
            if (hour >= wakeHour) { warnedToday = false; pending = false; return; }

            if (now >= limit) { pending = true; return; } // 새벽 2시 ~ 아침 6시 전 → 쓰러짐
            if (warnMinutesBefore > 0 && !warnedToday && now >= limit - warnMinutesBefore)
            {
                warnedToday = true;
                ShowNotice($"곧 새벽 {passOutHour}시예요! 우주선으로 돌아가 주무세요\n<size=75%>안 그러면 쓰러져서 구조비를 내야 해요</size>");
                GameAudio.Play(SoundId.Notice);
            }
        }

        private bool CanPassOutNow() => !TitleScreen.IsShowing && !UIInputBlocker.IsBlocked && GameClock.Instance != null && !GameClock.Instance.Paused;

        private IEnumerator PassOutRoutine()
        {
            running = true;
            pending = false;
            UIInputBlocker.Block(this);
            PlayerController pc = FindAnyObjectByType<PlayerController>();
            bool pcWasOn = pc != null && pc.enabled;
            if (pcWasOn) pc.enabled = false;
            GameClock clock = GameClock.Instance;
            bool clockWasPaused = clock != null && clock.Paused;
            if (clock != null) clock.Paused = true;
            if (canvas == null) BuildUI();
            canvas.enabled = true;

            // 1. 털썩 (화면이 천천히 어두워짐)
            centerText.text = "";
            GameAudio.Play(SoundId.SleepJingle);
            yield return Fade(0f, 1f, 1.6f);
            centerText.text = "너무 늦게까지 깨어 있어서 쓰러졌어요…";
            yield return Wait(1.6f);

            // 2. 구조비 + 우주선 앞으로 옮김 + 아침으로
            int fee = 0;
            CurrencyManager money = CurrencyManager.Instance;
            if (money != null && money.CurrentBells > 0)
            {
                fee = Mathf.Min(feeMax, Mathf.FloorToInt(money.CurrentBells * feeRatio));
                if (feeRatio > 0f) fee = Mathf.Max(1, fee); // 벨이 조금이라도 있으면 최소 1벨 (10%가 1벨도 안 되는 경우)
                if (fee > 0 && !money.TrySpendBells(fee)) fee = 0;
            }
            string takenCrop = null;
            if (fee == 0 && takeCropWhenBroke) takenCrop = TakeOneCrop();
            if (pc != null) MovePlayerToShip(pc);
            if (clock != null)
            {
                clock.Paused = clockWasPaused;
                int now = clock.CurrentHour * 60 + clock.CurrentMinute;
                int wake = wakeHour * 60;
                if (now < wake) clock.AdvanceMinutes(wake - now); // 자정이 이미 지나서 하루 넘김은 처리돼 있음 → 아침까지만
                Debug.Log($"[강제 취침] {clock.CurrentDay}일차 {clock.CurrentHour:00}:{clock.CurrentMinute:00}에 우주선 앞에서 깨어남 (구조비 {fee}벨)");
            }

            centerText.text = "";
            yield return Wait(0.6f);
            GameAudio.Play(SoundId.MorningChime);
            yield return Fade(1f, 0f, 1.2f);

            // 3. 아침 알림
            ShowNotice(fee > 0
                ? $"우주 구조대가 우주선까지 데려다줬어요\n<size=85%>구조비 <color=#C0392B><b>{fee:N0}벨</b></color>을 냈어요</size>"
                : takenCrop != null
                ? $"우주 구조대가 우주선까지 데려다줬어요\n<size=85%>벨이 없어서 수고비로 <color=#C0392B><b>{takenCrop} 1개</b></color>를 가져갔어요</size>"
                : "우주 구조대가 우주선까지 데려다줬어요\n<size=85%>벨도 작물도 없어서 이번엔 그냥 데려다줬어요</size>");

            if (pcWasOn && pc != null) pc.enabled = true;
            UIInputBlocker.Unblock(this);
            warnedToday = false;
            running = false;
        }

        // 가방(핫바 포함)에 있는 작물 중 무작위로 1개를 가져감. 가져간 작물 이름 (없으면 null)
        private static string TakeOneCrop()
        {
            InventoryManager inv = InventoryManager.Instance;
            if (inv == null) return null;
            var crops = new System.Collections.Generic.List<int>();
            int count = inv.GetSlotCount(SlotArea.Main);
            for (int i = 0; i < count; i++)
            {
                InventorySlotData s = inv.GetSlot(SlotArea.Main, i);
                if (s != null && !s.IsEmpty && s.item is CropData) crops.Add(i);
            }
            if (crops.Count == 0) return null;
            int index = crops[Random.Range(0, crops.Count)];
            InventorySlotData slot = inv.GetSlot(SlotArea.Main, index);
            string itemName = slot.item.itemName;
            string quality = ItemQualityUtility.GetDisplayName(slot.quality);
            inv.RemoveFromSlot(index, 1);
            Debug.Log($"[강제 취침] 벨이 없어서 {itemName}({quality}) 1개를 수고비로 가져감");
            return itemName;
        }

        // 내 우주선 바로 앞(지금 캐릭터가 있는 쪽 가장자리)으로 옮김. 우주선이 없으면 그 자리에서 깨어남
        private static void MovePlayerToShip(PlayerController pc)
        {
            BuildingSystem sys = BuildingSystem.Instance;
            if (sys == null) return;
            foreach (PlacedBuilding b in sys.Buildings)
            {
                if (b == null || b.Data == null || !b.Data.name.Contains("player_spaceship")) continue;
                if (b.State != PlacedBuilding.BuildState.Complete) continue;
                if (!BuildingSystem.TryGetBounds(b.transform, out Bounds bounds)) continue;

                Vector3 p = pc.transform.position;
                Vector3 dir = new Vector3(p.x - bounds.center.x, 0f, p.z - bounds.center.z);
                if (dir.sqrMagnitude < 0.01f) dir = Vector3.back;
                dir.Normalize();
                Vector3 edge = bounds.ClosestPoint(bounds.center + dir * 1000f);
                Vector3 target = new Vector3(edge.x, bounds.max.y + 5f, edge.z) + dir * 1.5f;
                if (Physics.Raycast(target, Vector3.down, out RaycastHit hit, 30f, ~0, QueryTriggerInteraction.Ignore)) target = hit.point + Vector3.up * 0.05f;
                else target.y = p.y;

                var cc = pc.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false; // CharacterController는 꺼야 순간이동이 됨
                pc.transform.position = target;
                if (cc != null) cc.enabled = true;
                pc.FaceTowards(target + dir * 3f); // 우주선 반대쪽(밖)을 보며 깨어남
                return;
            }
        }

        // ───────────────────────── 화면 ─────────────────────────

        private IEnumerator Fade(float from, float to, float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                fade.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / seconds)));
                yield return null;
            }
            fade.color = new Color(0f, 0f, 0f, to);
        }

        private static IEnumerator Wait(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime) yield return null;
        }

        private void ShowNotice(string text)
        {
            if (canvas == null) BuildUI();
            canvas.enabled = true;
            noticeText.text = text;
            noticeTimer = NoticeSeconds;
            noticeGroup.alpha = 0f;
        }

        private void BuildUI()
        {
            var go = new GameObject("PassOutCanvas", typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 960; // 잠자기 화면(950)보다 위
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            TMP_FontAsset font = UIFont.FindKorean();

            var f = new GameObject("Fade", typeof(RectTransform), typeof(Image));
            f.transform.SetParent(go.transform, false);
            Stretch((RectTransform)f.transform);
            fade = f.GetComponent<Image>();
            fade.color = Color.clear;
            fade.raycastTarget = false;

            centerText = MakeText(go.transform, font, 40f);
            Stretch(centerText.rectTransform);

            // 위쪽 가운데 알림 판
            var n = new GameObject("Notice", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            n.transform.SetParent(go.transform, false);
            var nrt = (RectTransform)n.transform;
            nrt.anchorMin = nrt.anchorMax = new Vector2(0.5f, 1f);
            nrt.pivot = new Vector2(0.5f, 1f);
            nrt.anchoredPosition = new Vector2(0f, -150f);
            nrt.sizeDelta = new Vector2(820f, 120f);
            Image bg = n.GetComponent<Image>();
            bg.color = new Color(0.12f, 0.1f, 0.18f, 0.82f);
            bg.raycastTarget = false;
            noticeGroup = n.GetComponent<CanvasGroup>();
            noticeGroup.alpha = 0f;
            noticeGroup.blocksRaycasts = false;
            noticeText = MakeText(n.transform, font, 30f);
            Stretch(noticeText.rectTransform);
        }

        private static TextMeshProUGUI MakeText(Transform parent, TMP_FontAsset font, float size)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.fontSize = size;
            t.color = Color.white;
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false;
            t.richText = true;
            return t;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}