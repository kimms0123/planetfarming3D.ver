using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using FarmingSystem.Core;
using FarmingSystem.Economy;
using FarmingSystem.Inventory;
using FarmingSystem.NPC;
using FarmingSystem.Player;
using FarmingSystem.Save;
using FarmingSystem.UI;
using FarmingSystem.Audio;

namespace FarmingSystem.Building
{
    // 설치물(보관 상자·판매 포탈) 쓰기 (설치 필요 없음 - 게임이 시작되면 저절로 생김)
    //  - 기능이 있는 건물(Building Data의 Function)이 다 지어져 있으면, 가까이에서 바라볼 때 "E 열기" / "E 판매하기" 안내
    //  - E → 상자·포탈 창 (ContainerWindow). 상자는 뚜껑이 열림
    //  - 판매 포탈: 넣어 둔 작물·씨앗은 다음 날 아침(날짜가 바뀔 때) 팔리고, 아침에 "어젯밤 판매 수입" 알림
    //    · 가격은 넣어 둔 날의 가격 (오늘의 특별 상품 보너스 포함). 상인이 안 오는 날에도 팔 수 있음
    public class PlacedObjects : MonoBehaviour, TalkMarkers.IPromptSource
    {
        private const float InteractRange = 1.6f;  // 설치물 가장자리까지 거리 (m)
        private const float MaxLookAngle = 75f;
        private const float LidAngle = 75f;        // 상자 뚜껑이 열리는 각도

        private static PlacedObjects instance;
        public static PlacedObjects Instance => instance;

        private PlacedBuilding near;
        private Bounds nearBounds;
        private float findTimer, priceTimer;
        private GameClock subscribedClock;
        private readonly Dictionary<string, int> pendingValue = new Dictionary<string, int>(); // 포탈별 예상 수입 (어제 가격)

        // 상자 뚜껑
        private Transform lid;
        private Vector3 lidPos, hingePoint, hingeAxis;
        private Quaternion lidRot;
        private float lidOpen; // 0 닫힘 ~ 1 열림
        private bool lidTarget;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (instance != null) return;
            var go = new GameObject("PlacedObjects");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<PlacedObjects>();
        }

        private void OnEnable() => TalkMarkers.Register(this);

        private void OnDisable()
        {
            TalkMarkers.Unregister(this);
            if (subscribedClock != null) subscribedClock.OnDayChanged -= HandleDayChanged;
            subscribedClock = null;
        }

        private void Update()
        {
            if (subscribedClock == null && GameClock.Instance != null)
            {
                subscribedClock = GameClock.Instance;
                subscribedClock.OnDayChanged += HandleDayChanged;
            }

            findTimer -= Time.unscaledDeltaTime;
            if (findTimer <= 0f) { findTimer = 0.2f; FindNear(); }

            priceTimer -= Time.unscaledDeltaTime;
            if (priceTimer <= 0f) { priceTimer = 0.5f; RefreshPortalValues(); }

            AnimateLid();

            if (ContainerWindow.IsOpen || !CanUseNow()) return;
            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame && CanReach(near, out _)) Open(near);
        }

        private static bool CanUseNow()
        {
            if (TitleScreen.IsShowing || UIInputBlocker.IsBlocked || PhoneMenu.IsOpen || BuildingPlacer.BlocksFarming) return false;
            InventoryManager inv = InventoryManager.Instance;
            return inv == null || !inv.IsInventoryOpen;
        }

        // ───────────────────────── 가까운 설치물 ─────────────────────────

        private void FindNear()
        {
            near = null;
            BuildingSystem sys = BuildingSystem.Instance;
            PlayerController pc = FindAnyObjectByType<PlayerController>();
            if (sys == null || pc == null) return;
            float best = float.MaxValue;
            foreach (PlacedBuilding b in sys.Buildings)
            {
                if (b == null || b.Data == null || b.Data.function == BuildingFunction.None) continue;
                if (b.State != PlacedBuilding.BuildState.Complete) continue;
                if (!BuildingSystem.TryGetBounds(b.transform, out Bounds bounds)) continue;
                float d = FlatDistance(bounds, pc.transform.position);
                if (d < best) { best = d; near = b; nearBounds = bounds; }
            }
        }

        private static float FlatDistance(Bounds b, Vector3 p)
        {
            Vector3 c = b.ClosestPoint(new Vector3(p.x, b.center.y, p.z)) - p;
            c.y = 0f;
            return c.magnitude;
        }

        // 가까이에서 그쪽을 보고 있는지 (+ 안내를 띄울 자리)
        private bool CanReach(PlacedBuilding b, out Vector3 promptPos)
        {
            promptPos = Vector3.zero;
            if (b == null) return false;
            PlayerController pc = FindAnyObjectByType<PlayerController>();
            if (pc == null || !pc.enabled) return false;
            Vector3 p = pc.transform.position;
            if (FlatDistance(nearBounds, p) > InteractRange) return false;
            Vector3 to = nearBounds.center - p;
            to.y = 0f;
            if (to.sqrMagnitude > 0.01f && Vector3.Angle(pc.transform.forward, to) > MaxLookAngle) return false;
            promptPos = new Vector3(nearBounds.center.x, Mathf.Max(nearBounds.max.y + 0.3f, p.y + 1.4f), nearBounds.center.z);
            return true;
        }

        public bool TryGetPrompt(out string label, out Vector3 worldPos)
        {
            label = near != null && near.Data.function == BuildingFunction.SellPortal ? "판매하기" : "열기";
            if (ContainerWindow.IsOpen || !CanUseNow()) { worldPos = Vector3.zero; return false; }
            return CanReach(near, out worldPos);
        }

        // ───────────────────────── 열기·닫기 ─────────────────────────

        private void Open(PlacedBuilding b)
        {
            string key = ContainerStorage.KeyOf(b);
            if (key == null) return;
            bool portal = b.Data.function == BuildingFunction.SellPortal;
            if (!portal) StartLid(b);
            GameAudio.PlayAt(portal ? SoundId.PortalOpen : SoundId.ChestOpen, b.transform.position);
            Vector3 at = b.transform.position;
            ContainerWindow.Open(b.Data.displayName, key, portal, () => { lidTarget = false; GameAudio.PlayAt(portal ? SoundId.UIClose : SoundId.ChestClose, at); });
        }

        // 뚜껑(이름에 "top"이 들어간 부분)을 플레이어 반대쪽 모서리를 축으로 열기
        private void StartLid(PlacedBuilding b)
        {
            Transform found = null;
            foreach (Transform t in b.GetComponentsInChildren<Transform>())
                if (t.name.ToLowerInvariant().Contains("top")) { found = t; break; }
            if (lid != null && lid != found) ResetLid();
            lid = found;
            lidTarget = lid != null;
            if (lid == null) return;
            if (lidOpen <= 0f) { lidPos = lid.localPosition; lidRot = lid.localRotation; }

            Renderer r = lid.GetComponentInChildren<Renderer>();
            Bounds lb = r != null ? r.bounds : nearBounds;
            PlayerController pc = FindAnyObjectByType<PlayerController>();
            Vector3 toPlayer = pc != null ? pc.transform.position - lb.center : Vector3.back;
            toPlayer.y = 0f;
            // 가로·세로 중 가까운 축으로 (설치물은 90도 단위로 놓이니까)
            Vector3 dir = Mathf.Abs(toPlayer.x) > Mathf.Abs(toPlayer.z) ? new Vector3(Mathf.Sign(toPlayer.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(toPlayer.z));
            float extent = Mathf.Abs(dir.x) > 0f ? lb.extents.x : lb.extents.z;
            hingePoint = new Vector3(lb.center.x, lb.min.y, lb.center.z) - dir * extent; // 먼 쪽 아래 모서리
            hingeAxis = Vector3.Cross(Vector3.up, dir);
        }

        private void AnimateLid()
        {
            if (lid == null) return;
            lidOpen = Mathf.MoveTowards(lidOpen, lidTarget ? 1f : 0f, Time.unscaledDeltaTime / 0.25f);
            lid.localPosition = lidPos;
            lid.localRotation = lidRot;
            if (lidOpen > 0f) lid.RotateAround(hingePoint, hingeAxis, -LidAngle * Mathf.SmoothStep(0f, 1f, lidOpen));
            else if (!lidTarget) lid = null;
        }

        private void ResetLid()
        {
            if (lid == null) return;
            lid.localPosition = lidPos;
            lid.localRotation = lidRot;
            lid = null;
            lidOpen = 0f;
        }

        // ───────────────────────── 판매 포탈 ─────────────────────────

        /// <summary>포탈 안 물건의 예상 수입 (지금 가격)</summary>
        public static int ValueOf(InventorySlotData[] slots)
        {
            ShopManager shop = ShopManager.Instance;
            if (shop == null || slots == null) return 0;
            int total = 0;
            foreach (InventorySlotData s in slots)
                if (ShopManager.IsSellable(s)) total += Mathf.RoundToInt(shop.GetSellUnitPrice(s) * s.quantity);
            return total;
        }

        /// <summary>상자·포탈 창에서 물건을 옮긴 뒤 바로 다시 계산</summary>
        public void RefreshPortalValues()
        {
            pendingValue.Clear();
            foreach (var kv in ContainerStorage.All)
                if (ContainerStorage.IsPortalKey(kv.Key)) pendingValue[kv.Key] = ValueOf(kv.Value);
        }

        // 날짜가 바뀌면 포탈 안 물건을 어제 가격으로 팔기
        private void HandleDayChanged(int day, Season season)
        {
            int total = 0, count = 0;
            foreach (var kv in ContainerStorage.All)
            {
                if (!ContainerStorage.IsPortalKey(kv.Key)) continue;
                int value = pendingValue.TryGetValue(kv.Key, out int v) ? v : ValueOf(kv.Value);
                foreach (InventorySlotData s in kv.Value)
                {
                    if (s.IsEmpty) continue;
                    count += s.quantity;
                    s.Clear();
                }
                total += value;
            }
            pendingValue.Clear();
            if (count == 0) return;
            if (total > 0 && CurrencyManager.Instance != null) CurrencyManager.Instance.AddBells(total);
            Debug.Log($"[판매 포탈] 물건 {count}개 → {total}벨");
            StartCoroutine(NoticeLater($"어젯밤 판매 포탈에서 {total:N0}벨을 벌었어요!"));
        }

        // 잠자기 화면이 밝아진 뒤에 보이게 조금 늦게
        private IEnumerator NoticeLater(string text)
        {
            for (float t = 0f; t < 3f; t += Time.unscaledDeltaTime) yield return null;
            IslandNotice.Show(text, this);
        }
    }
}