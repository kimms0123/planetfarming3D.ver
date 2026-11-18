using UnityEngine;
using UnityEngine.InputSystem;

namespace FarmingSystem.Economy
{
    // 상인 AI 들여다보기 창 (발표·테스트용). 게임 중 F9로 켜고 끄기
    // 씬에 따로 넣을 필요 없음 (게임 시작하면 저절로 생김)
    //  - FCM: 성향 4가지 소속도 막대 + 프로필 6가지 값
    //  - RBFN: 마지막 흥정의 버티기 예측, 그때 Max Ratio, 수락 확률
    //  - RBFN: 이번 계절 수요 예측 상위 5개 작물
    public class MerchantMindDebug : MonoBehaviour
    {
        private static MerchantMindDebug instance;
        private bool show;
        private GUIStyle box, label;

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
            if (Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame) show = !show;
        }

        private void OnGUI()
        {
            if (!show) return;
            if (box == null)
            {
                box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, padding = new RectOffset(10, 10, 8, 8) };
                label = new GUIStyle(GUI.skin.label) { fontSize = 14 };
            }

            float x = 16f, y = 16f, w = 340f;
            GUI.Box(new Rect(x, y, w, 470f), "상인 AI (F9 닫기)", box);
            y += 28f;

            float[] u = MerchantMind.Memberships;
            GUI.Label(new Rect(x + 10, y, w, 22), $"FCM 성향 (판매 {MerchantMind.Sales}회" +
                      (MerchantMind.PersonaReady ? $", 판단: {MerchantMind.TypeNames[(int)MerchantMind.Dominant]})" : ", 아직 판단 전)"), label);
            y += 24f;
            for (int j = 0; j < u.Length; j++)
            {
                GUI.Label(new Rect(x + 10, y, 110, 20), MerchantMind.TypeNames[j], label);
                GUI.Box(new Rect(x + 120, y + 3, 150, 14), GUIContent.none);
                Color c = GUI.color;
                GUI.color = new Color(1f, 0.72f, 0.15f);
                GUI.Box(new Rect(x + 120, y + 3, 150 * u[j], 14), GUIContent.none);
                GUI.color = c;
                GUI.Label(new Rect(x + 278, y, 60, 20), $"{u[j]:P0}", label);
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
            GUI.Label(new Rect(x + 20, y, w, 22), MerchantMind.LastStay < 0f ? "아직 흥정 없음"
                : $"거절해도 팔 확률 {MerchantMind.LastStay:P0} → Max {MerchantMind.LastMaxRatio:0.00}배, 수락 {MerchantMind.LastChance:P0}", label);
            y += 28f;

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
    }
}
