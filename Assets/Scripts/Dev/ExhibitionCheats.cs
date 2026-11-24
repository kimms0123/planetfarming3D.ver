// 전시회용 단축키 (개발자 치트)
//
// ▶ 언제 들어가나
//   - 기본: 에디터 Play와 빌드 모두 켜짐 (전시회 빌드에도 들어감)
//   - 최종 빌드에서 없애기: Project Settings → Player → Other Settings → Scripting Define Symbols 에 NO_EXHIBITION_CHEATS 를 추가하고 빌드
//     → 이 코드는 빌드에 아예 안 들어감 (이 파일을 지워도 됨. 다른 코드는 이 파일을 안 씀)
//
// ▶ 키 (게임 화면에서, 시작 화면·창이 열려 있을 땐 안 됨)
//   F1  도움말 켜기/끄기
//   F2  내 주변 밭 갈기 (7x7칸, Shift = 13x13칸)
//   F3  갈린 밭 전체 물 주기
//   F4  내 주변 갈린 빈 밭에 씨앗 심기 (핫바에서 고른 씨앗, 안 골랐으면 이번 계절 씨앗 섞어서 · 씨앗은 안 줄어듦)
//   F5  이번 계절 씨앗 30개씩 가방에 (Shift = 모든 계절 씨앗)
//   F6  심어진 작물 전부 다 자라게 (수확 가능 상태로)
//   F7  날씨 바꾸기 (맑음 → 비 → 태풍 → 맑음)
//   F8  다음 계절 1일로 이동 (실제로 날짜가 지나감 · 계절에 안 맞는 작물은 시듦)
//   F9  (안 씀 - 원래 있던 상인 AI 들여다보기 창 MerchantMindDebug 키)
//   F10 1시간 지나가게 (Shift = 3시간)
//   F11 상인 지금 오게 하기 (우주선 착륙 + 오늘의 특별 상품, 원래 머무는 날짜만큼 있다가 떠남)
//   F12 10,000벨 받기
//   Shift+F12 임무창 초기화 (모든 임무를 안 한 상태로 · 실수로 안 눌리게 Shift 필요)
//   End 새 게임 오프닝 건너뛰기 (우주선은 오프닝에서 착륙하던 자리에 바로 놓임)
#if !NO_EXHIBITION_CHEATS
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using FarmingSystem.Core;
using FarmingSystem.Economy;
using FarmingSystem.Farming;
using FarmingSystem.Inventory;
using FarmingSystem.NPC;
using FarmingSystem.Player;
using FarmingSystem.Save;
using FarmingSystem.Weather;

namespace FarmingSystem.DevTools
{
    public class ExhibitionCheats : MonoBehaviour
    {
        private const int SmallRadius = 3;   // 7x7
        private const int BigRadius = 6;     // 13x13
        private const int SeedAmount = 30;
        private const int BellsAmount = 10000;
        private const float MaxHeightDiff = 1.5f; // 플레이어와 높이가 이만큼 넘게 다른 칸(절벽 위·아래)은 건드리지 않음

        private Transform player;
        private GameObject helpPanel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindAnyObjectByType<ExhibitionCheats>() != null) return;
            var go = new GameObject("[ExhibitionCheats]");
            DontDestroyOnLoad(go);
            go.AddComponent<ExhibitionCheats>();
            Debug.Log("[전시회 단축키] 켜짐 - F1로 키 목록 보기");
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            if (kb.f1Key.wasPressedThisFrame) ToggleHelp();
            if (kb.endKey.wasPressedThisFrame && TitleScreen.SkipOpening()) Debug.Log("[전시회 단축키] 오프닝 건너뛰기");

            if (TitleScreen.IsShowing || UIInputBlocker.IsBlocked) return;
            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;

            if (kb.f2Key.wasPressedThisFrame) TillAround(shift ? BigRadius : SmallRadius);
            if (kb.f3Key.wasPressedThisFrame) WaterAll();
            if (kb.f4Key.wasPressedThisFrame) PlantAround(shift ? BigRadius : SmallRadius);
            if (kb.f5Key.wasPressedThisFrame) GiveSeeds(allSeasons: shift);
            if (kb.f6Key.wasPressedThisFrame) GrowAll();
            if (kb.f7Key.wasPressedThisFrame) NextWeather();
            if (kb.f8Key.wasPressedThisFrame) NextSeason();
            // F9는 상인 AI 창(MerchantMindDebug)이 쓰므로 비워 둠
            if (kb.f10Key.wasPressedThisFrame) PassTime(shift ? 180 : 60);
            if (kb.f11Key.wasPressedThisFrame) MerchantNow();
            if (kb.f12Key.wasPressedThisFrame)
            {
                if (shift) { FarmingSystem.UI.MissionPanel.ResetAll(); Say("임무를 처음부터 다시 시작해요"); }
                else GiveBells();
            }
        }

        // ───────────── 밭 ─────────────

        private void TillAround(int radius)
        {
            int count = 0;
            foreach (FarmTile tile in TilesAround(radius))
                if (tile.State == TileState.Natural && IsClear(tile) && tile.TryTill()) count++;
            Say($"밭 {count}칸을 갈았어요");
        }

        private void WaterAll()
        {
            int count = 0;
            foreach (FarmTile tile in FindObjectsByType<FarmTile>(FindObjectsInactive.Exclude))
                if (tile.State == TileState.Tilled && !tile.IsWatered && tile.Water()) count++;
            Say($"밭 {count}칸에 물을 줬어요");
        }

        private void PlantAround(int radius)
        {
            Season season = GameClock.Instance != null ? GameClock.Instance.CurrentSeason : Season.Spring;
            SeedData picked = InventoryManager.Instance != null ? InventoryManager.Instance.CurrentSeed : null;
            List<SeedData> pool;
            if (picked != null && picked.resultCrop != null && picked.resultCrop.CanGrowIn(season)) pool = new List<SeedData> { picked };
            else pool = AllSeeds().Where(s => s.resultCrop.CanGrowIn(season)).ToList();

            if (pool.Count == 0) { Say("이번 계절에 심을 수 있는 씨앗이 없어요"); return; }

            int count = 0, i = 0;
            foreach (FarmTile tile in TilesAround(radius))
            {
                if (tile.State != TileState.Tilled || tile.CurrentCrop != null) continue;
                if (tile.Plant(pool[i % pool.Count], null)) { count++; i++; }
            }
            Say(pool.Count == 1 ? $"{pool[0].itemName}을(를) {count}칸에 심었어요" : $"이번 계절 씨앗 {pool.Count}종을 {count}칸에 심었어요");
        }

        private void GrowAll()
        {
            int count = 0;
            foreach (FarmTile tile in FindObjectsByType<FarmTile>(FindObjectsInactive.Exclude))
            {
                CropInstance crop = tile.CurrentCrop;
                if (crop == null || crop.IsWithered || crop.IsReadyToHarvest) continue;
                crop.ApplyGrowthBonus(crop.Data.totalGrowthDays);
                count++;
            }
            Say($"작물 {count}개가 다 자랐어요");
        }

        // 플레이어 주변 칸 (가까운 칸부터)
        private List<FarmTile> TilesAround(int radius)
        {
            var list = new List<FarmTile>();
            FarmGrid grid = FarmGrid.Instance;
            Transform p = Player();
            if (grid == null || p == null) { Say("밭(FarmGrid)이나 플레이어를 못 찾았어요"); return list; }

            Vector2Int center = grid.WorldToGrid(p.position);
            for (int dx = -radius; dx <= radius; dx++)
                for (int dz = -radius; dz <= radius; dz++)
                {
                    if (!grid.TryGetTile(center + new Vector2Int(dx, dz), out FarmTile tile) || tile == null) continue;
                    if (Mathf.Abs(tile.GetGroundBounds().max.y - p.position.y) > MaxHeightDiff) continue;
                    list.Add(tile);
                }
            list.Sort((a, b) => (a.transform.position - p.position).sqrMagnitude.CompareTo((b.transform.position - p.position).sqrMagnitude));
            return list;
        }

        // 칸 위에 나무·바위·건물 등이 없는지 (괭이질 조건과 비슷하게)
        private static bool IsClear(FarmTile tile)
        {
            Bounds b = tile.GetGroundBounds();
            Vector3 center = new Vector3(b.center.x, b.max.y + 0.6f, b.center.z);
            Vector3 half = new Vector3(b.extents.x * 0.8f, 0.5f, b.extents.z * 0.8f);
            foreach (Collider c in Physics.OverlapBox(center, half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c.GetComponentInParent<FarmTile>() != null) continue;        // 땅 자기 자신·옆 칸
                if (c.GetComponentInParent<PlayerController>() != null) continue; // 플레이어
                if (c is CharacterController) continue;
                return false;
            }
            return true;
        }

        // ───────────── 아이템·돈 ─────────────

        private void GiveSeeds(bool allSeasons)
        {
            InventoryManager inv = InventoryManager.Instance;
            if (inv == null) return;
            Season season = GameClock.Instance != null ? GameClock.Instance.CurrentSeason : Season.Spring;
            int kinds = 0, full = 0;
            foreach (SeedData s in AllSeeds())
            {
                if (!allSeasons && !s.resultCrop.CanGrowIn(season)) continue;
                int left = inv.AddItem(s, SeedAmount);
                if (left < SeedAmount) kinds++;
                if (left > 0) full++;
            }
            Say(full > 0 ? $"씨앗 {kinds}종을 넣었어요 (가방이 가득 차서 {full}종은 덜 들어감)" : $"씨앗 {kinds}종을 {SeedAmount}개씩 넣었어요");
        }

        private void GiveBells()
        {
            if (CurrencyManager.Instance == null) return;
            CurrencyManager.Instance.AddBells(BellsAmount);
            Say($"+{BellsAmount:N0}벨");
        }

        // 게임에 불러와져 있는 씨앗 데이터 전부 (상점 목록 등이 참조하고 있는 것). 이름 순, 중복 제거
        private static List<SeedData> AllSeeds()
        {
            return Resources.FindObjectsOfTypeAll<SeedData>()
                .Where(s => s != null && s.resultCrop != null && s.hideFlags == HideFlags.None)
                .GroupBy(s => s.itemName).Select(g => g.First())
                .OrderBy(s => s.itemName).ToList();
        }

        // ───────────── 상인 ─────────────

        private void MerchantNow()
        {
            ShopManager shop = ShopManager.Instance;
            if (shop == null) { Say("상점(ShopManager)이 없어요"); return; }
            if (shop.IsShopOpen) { Say("상인이 이미 와 있어요"); return; }
            // ShopManager ⋮ 메뉴의 "디버그: 지금 상점 열기"와 같은 동작 (우주선 착륙·특별 상품 포함)
            shop.SendMessage("DebugOpenShop", SendMessageOptions.DontRequireReceiver);
            Say(shop.IsShopOpen ? "상인이 도착했어요!" : "상인을 부르지 못했어요 (Console 확인)");
        }

        // ───────────── 시간·날씨·계절 ─────────────

        private void NextWeather()
        {
            WeatherManager w = WeatherManager.Instance;
            if (w == null) { Say("WeatherManager가 없어요"); return; }
            WeatherType next = w.CurrentWeather == WeatherType.Clear ? WeatherType.Rain
                             : w.CurrentWeather == WeatherType.Rain ? WeatherType.Typhoon : WeatherType.Clear;
            w.SetWeatherToday(next);
            Say($"날씨: {WeatherManager.DisplayName(next)}");
        }

        private void NextSeason()
        {
            GameClock clock = GameClock.Instance;
            if (clock == null) return;
            Season next = (Season)(((int)clock.CurrentSeason + 1) % 4);
            clock.DebugJumpToSeason(next);
            Say($"{GameClock.GetSeasonDisplayName(next)}이 되었어요");
        }

        private void PassTime(int minutes)
        {
            if (GameClock.Instance == null) return;
            GameClock.Instance.AdvanceMinutes(minutes);
            Say($"{minutes / 60}시간이 지났어요");
        }

        // ───────────── 공통 ─────────────

        private Transform Player()
        {
            if (player == null)
            {
                PlayerController pc = FindAnyObjectByType<PlayerController>();
                if (pc != null) player = pc.transform;
            }
            return player;
        }

        private void Say(string text)
        {
            Debug.Log($"[전시회 단축키] {text}");
            IslandNotice.Show(text, this);
        }

        private void ToggleHelp()
        {
            if (helpPanel == null) BuildHelp();
            helpPanel.SetActive(!helpPanel.activeSelf);
        }

        private void BuildHelp()
        {
            var canvasGo = new GameObject("ExhibitionHelp", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 990;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var bg = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(canvasGo.transform, false);
            var rt = (RectTransform)bg.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-30f, -30f);
            rt.sizeDelta = new Vector2(620f, 680f);
            var img = bg.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.72f);
            img.raycastTarget = false;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(bg.transform, false);
            var trt = (RectTransform)textGo.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(28f, 20f); trt.offsetMax = new Vector2(-28f, -20f);
            var t = textGo.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI any = FindAnyObjectByType<TextMeshProUGUI>(); // 게임에서 쓰는 한글 폰트를 빌려 씀
            if (any != null && any != t) t.font = any.font;
            t.fontSize = 28f;
            t.color = Color.white;
            t.raycastTarget = false;
            var sb = new StringBuilder();
            sb.AppendLine("<b>전시회 단축키</b>  (F1 닫기)");
            sb.AppendLine("F2  주변 밭 갈기  (Shift 넓게)");
            sb.AppendLine("F3  갈린 밭 전체 물 주기");
            sb.AppendLine("F4  주변 밭에 씨앗 심기  (Shift 넓게)");
            sb.AppendLine("F5  이번 계절 씨앗 30개씩  (Shift 전부)");
            sb.AppendLine("F6  작물 전부 다 자라게");
            sb.AppendLine("F7  날씨 바꾸기");
            sb.AppendLine("F8  다음 계절로");
            sb.AppendLine("F9  상인 AI 창 (원래 기능)");
            sb.AppendLine("F10 1시간 지나기  (Shift 3시간)");
            sb.AppendLine("F11 상인 지금 오게 하기");
            sb.AppendLine("F12 10,000벨  (Shift 임무창 초기화)");
            sb.AppendLine("End 오프닝 건너뛰기");
            t.text = sb.ToString();
            helpPanel = canvasGo;
            helpPanel.SetActive(false);
        }
    }
}
#endif