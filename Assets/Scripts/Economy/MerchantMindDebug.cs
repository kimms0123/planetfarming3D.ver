using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using FarmingSystem.Core;
using FarmingSystem.Farming;
using FarmingSystem.Inventory;

namespace FarmingSystem.Economy
{
    // 상인 AI 들여다보기 창 (발표·테스트용). 게임 중 F9로 켜고 끄기
    // 씬에 따로 넣을 필요 없음 (게임 시작하면 저절로 생김)
    //  왼쪽: FCM 성향 소속도 + 프로필 / RBFN 버티기 예측 / RBFN 수요 예측
    //  오른쪽: 테스트 버튼 (상인 부르기, 작물 받기, 가상 판매, 초기화)
    public class MerchantMindDebug : MonoBehaviour
    {
        private static MerchantMindDebug instance;
        private bool show;
        private GUIStyle box, label, button;
        private string notice = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (instance != null) return;
            var go = new GameObject("[MerchantMindDebug]");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<MerchantMindDebug>();
        }

        private void Update()
        {
            if (Keyboard.current == null || !Keyboard.current.f9Key.wasPressedThisFrame) return;
            show = !show;
            // 창이 열려 있는 동안 마우스 커서 풀기 (버튼 누르려고)
            if (show) UIInputBlocker.Block(this);
            else UIInputBlocker.Unblock(this);
        }

        private void OnDestroy() => UIInputBlocker.Unblock(this);

        private void OnGUI()
        {
            if (!show) return;
            if (box == null)
            {
                box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, padding = new RectOffset(10, 10, 8, 8) };
                label = new GUIStyle(GUI.skin.label) { fontSize = 14 };
                button = new GUIStyle(GUI.skin.button) { fontSize = 14 };
            }
            DrawInfo(16f, 16f, 360f);
            DrawButtons(386f, 16f, 220f);
        }

        // ---------- 왼쪽: AI 상태 ----------

        private void DrawInfo(float x, float y, float w)
        {
            GUI.Box(new Rect(x, y, w, 480f), "상인 AI (F9 닫기)", box);
            y += 28f;

            float[] u = MerchantMind.Memberships;
            GUI.Label(new Rect(x + 10, y, w, 22), $"FCM 성향 (판매 {MerchantMind.Sales}회" +
                      (MerchantMind.PersonaReady ? $", 판단: {MerchantMind.TypeNames[(int)MerchantMind.Dominant]})" : ", 아직 판단 전)"), label);
            y += 24f;
            for (int j = 0; j < u.Length; j++)
            {
                GUI.Label(new Rect(x + 10, y, 110, 20), MerchantMind.TypeNames[j], label);
                GUI.Box(new Rect(x + 120, y + 3, 160, 14), GUIContent.none);
                Color c = GUI.color;
                GUI.color = new Color(1f, 0.72f, 0.15f);
                GUI.Box(new Rect(x + 120, y + 3, 160 * u[j], 14), GUIContent.none);
                GUI.color = c;
                GUI.Label(new Rect(x + 290, y, 60, 20), $"{u[j]:P0}", label);
                y += 22f;
            }

            y += 6f;
            float[] p = MerchantMind.Profile;
            GUI.Label(new Rect(x + 10, y, w, 22), "플레이어 프로필", label);
            y += 22f;
            for (int i = 0; i < p.Length; i++)
            {
                GUI.Label(new Rect(x + 20, y, w, 20), $"{MerchantMind.ProfileNames[i]}: {p[i]:0.00}", label);
                y += 20f;
            }

            y += 6f;
            GUI.Label(new Rect(x + 10, y, w, 22), $"RBFN 버티기 예측 (학습 {MerchantMind.StayLabels}회)", label);
            y += 22f;
            GUI.Label(new Rect(x + 20, y, w - 20, 40), MerchantMind.LastStay < 0f ? "아직 흥정 없음"
                : $"거절해도 팔 확률 {MerchantMind.LastStay:P0}\n→ Max {MerchantMind.LastMaxRatio:0.00}배, 수락 확률 {MerchantMind.LastChance:P0}", label);
            y += 44f;

            GUI.Label(new Rect(x + 10, y, w, 22), "RBFN 수요 예측 (이번 계절)", label);
            y += 22f;
            var top = MerchantMind.TopDemand(5);
            if (top.Count == 0) GUI.Label(new Rect(x + 20, y, w, 20), "아직 판매 기록 없음", label);
            foreach (var kv in top)
            {
                GUI.Label(new Rect(x + 20, y, w, 20), $"{kv.Key}: {kv.Value:P0}", label);
                y += 20f;
            }
        }

        // ---------- 오른쪽: 테스트 버튼 ----------

        private void DrawButtons(float x, float y, float w)
        {
            GUI.Box(new Rect(x, y, w, 330f), "테스트", box);
            y += 30f;
            float bw = w - 20f, bh = 30f;

            if (GUI.Button(new Rect(x + 10, y, bw, bh), "상인 지금 오게 하기", button))
            {
                ShopManager shop = ShopManager.Instance;
                if (shop == null) notice = "ShopManager가 없어요";
                else if (shop.IsShopOpen) notice = "이미 와 있어요";
                else { shop.SendMessage("DebugOpenShop"); notice = "상인 도착 (특별 상품도 AI가 고름)"; }
            }
            y += bh + 6f;

            if (GUI.Button(new Rect(x + 10, y, bw, bh), "테스트 작물 받기", button)) GiveCrops();
            y += bh + 6f;

            if (GUI.Button(new Rect(x + 10, y, bw, bh), "가상 판매: 흥정형 x3", button))
            {
                for (int i = 0; i < 3; i++) MerchantMind.RecordSale(Fake(0.6f, true, 1.35f, 0.4f, 0.1f, 1500));
                notice = "흥정 집착형처럼 3번 팔았다고 가정";
            }
            y += bh + 6f;

            if (GUI.Button(new Rect(x + 10, y, bw, bh), "가상 판매: 즉시형 x3", button))
            {
                for (int i = 0; i < 3; i++) MerchantMind.RecordSale(Fake(1f, false, 1f, 0.35f, 0.1f, 400));
                notice = "즉시 판매형처럼 3번 팔았다고 가정";
            }
            y += bh + 6f;

            if (GUI.Button(new Rect(x + 10, y, bw, bh), "가상 판매: 기회주의형 x3", button))
            {
                for (int i = 0; i < 3; i++) MerchantMind.RecordSale(Fake(0.5f, false, 1f, 0.4f, 0.9f, 900));
                notice = "기회주의형처럼 3번 팔았다고 가정";
            }
            y += bh + 6f;

            if (GUI.Button(new Rect(x + 10, y, bw, bh), "AI 처음부터", button))
            {
                MerchantMind.Reset();
                notice = "상인 AI 초기화";
            }
            y += bh + 10f;

            GUI.Label(new Rect(x + 10, y, bw, 60), notice, label);
        }

        // 가상 판매 기록 (작물 비중 없음 → 수요 예측은 안 바뀜, 성향만 바뀜)
        private static SaleRecord Fake(float sellFraction, bool bargained, float ratio, float quality, float special, long value)
            => new SaleRecord
            {
                SellFraction = sellFraction,
                Bargained = bargained,
                OfferRatio = ratio,
                AvgQuality = quality,
                SpecialShare = special,
                Value = value,
                CropShares = new Dictionary<CropData, float>(),
            };

        // 불러와져 있는 작물 데이터 중 3종류를 10개씩 (1종류는 '좋음' 품질)
        private void GiveCrops()
        {
            InventoryManager inv = InventoryManager.Instance;
            if (inv == null) { notice = "InventoryManager가 없어요"; return; }
            var crops = new List<CropData>(Resources.FindObjectsOfTypeAll<CropData>());
            crops.RemoveAll(c => c == null || c.basePrice <= 0f);
            if (crops.Count == 0) { notice = "작물 데이터를 못 찾았어요"; return; }

            string names = "";
            for (int i = 0; i < 3 && crops.Count > 0; i++)
            {
                int pick = Random.Range(0, crops.Count);
                CropData c = crops[pick];
                crops.RemoveAt(pick);
                inv.AddItem(c, 10, i == 0 ? ItemQuality.Good : ItemQuality.Normal);
                names += (names.Length > 0 ? ", " : "") + c.cropName;
            }
            notice = "받음: " + names + " (10개씩)";
        }
    }
}