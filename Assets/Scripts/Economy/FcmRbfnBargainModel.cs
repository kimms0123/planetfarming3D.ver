using System;
using UnityEngine;

namespace FarmingSystem.Economy
{
    // FCM + RBFN 흥정 모델 (ShopSellPanelUI의 Bargain Model 칸에 넣는 에셋)
    // 메뉴: Tools → 행성농사일지 → 상인 AI → 설정하기 (에셋 만들고 칸에 넣어 줌)
    //
    // 수락 확률 = 기본 규칙(Safe Ratio까지 수락 → Max Ratio로 갈수록 줄어듦)인데, Max Ratio가 상황에 따라 움직임:
    //   + Σ 성향 소속도 x 성향 보너스      (FCM: 흥정 집착형이면 깐깐, 즉시 판매형이면 너그럽게)
    //   - 버티기 예측 깐깐함 x (P - 0.5)    (RBFN: 거절해도 팔 사람이면 깐깐, 떠날 사람이면 너그럽게)
    //   + 품질 / - 이번 방문 시도 횟수 / + 궂은 날씨 (BargainContext 특징값)
    // 대사: 판매가 3번 이상 쌓이고 한 성향이 뚜렷하면 그 성향 대사로 바뀜
    [CreateAssetMenu(fileName = "FcmRbfnBargain", menuName = "FarmingSystem/Economy/FCM RBFN Bargain")]
    public class FcmRbfnBargainModel : BargainModel
    {
        [Serializable]
        public class TypeLines
        {
            public string typeName;
            [TextArea] public string[] greet;
            [Tooltip("{0} 자리에 금액이 들어감")]
            [TextArea] public string[] accept;
            [TextArea] public string[] reject;
        }

        [Header("기본 규칙")]
        [Range(1f, 2f)] public float safeRatio = RuleBasedBargainModel.DefaultSafeRatio;
        [Range(1.01f, 3f)] public float maxRatio = RuleBasedBargainModel.DefaultMaxRatio;

        [Header("FCM 성향 보너스 (Max Ratio에 더함) - 순서: 즉시 판매 / 장기 보유 / 흥정 집착 / 기회주의")]
        public float[] typeBonus = { 0.05f, 0.08f, -0.15f, -0.05f };

        [Header("RBFN 버티기 예측")]
        [Tooltip("거절해도 팔 것 같은 사람일수록 이만큼 깐깐해짐 (P=1이면 Max Ratio - 값/2)")]
        public float stayToughness = 0.2f;

        [Header("상황 보정")]
        public float qualityWeight = 0.15f;  // 좋은 품질이면 더 쳐 줌
        public float attemptPenalty = 0.08f; // 한 번에 여러 번 조르면 깐깐
        public float weatherWeight = 0.05f;  // 궂은 날엔 빨리 떠나고 싶어서 너그럽게

        [Header("성향별 대사 (판매 창)")]
        public TypeLines[] lines = DefaultLines();

        public override float AcceptChance(BargainContext ctx)
        {
            if (ctx == null || ctx.Offer <= ctx.BaseTotal) return 1f;

            float max = maxRatio;
            if (MerchantMind.Sales > 0)
            {
                float[] u = MerchantMind.Memberships;
                for (int j = 0; j < u.Length && j < typeBonus.Length; j++) max += u[j] * typeBonus[j];
            }
            float stay = MerchantMind.PredictStay(ctx);
            max -= stayToughness * (stay - 0.5f);
            float[] f = ctx.Features;
            max += qualityWeight * (f[2] - 0.33f) - attemptPenalty * f[5] + weatherWeight * f[7];
            max = Mathf.Max(safeRatio + 0.02f, max);

            float chance = RuleBasedBargainModel.Compute(ctx, safeRatio, max);
            MerchantMind.LastStay = stay;
            MerchantMind.LastMaxRatio = max;
            MerchantMind.LastChance = chance;
            return chance;
        }

        public override string GetLine(MerchantLineKind kind, string fallback)
        {
            if (!MerchantMind.PersonaReady || lines == null) return fallback;
            int t = (int)MerchantMind.Dominant;
            if (t >= lines.Length || lines[t] == null) return fallback;
            string[] pool = kind == MerchantLineKind.Greet ? lines[t].greet
                          : kind == MerchantLineKind.Accept ? lines[t].accept
                          : lines[t].reject;
            if (pool == null || pool.Length == 0) return fallback;
            string line = pool[UnityEngine.Random.Range(0, pool.Length)];
            return string.IsNullOrWhiteSpace(line) ? fallback : line;
        }

        private static TypeLines[] DefaultLines() => new[]
        {
            new TypeLines
            {
                typeName = "즉시 판매형",
                greet = new[] { "왔구나! 오늘도 시원하게 팔고 갈 거지?", "역시 너는 오자마자 꺼내 놓는구나. 좋아 좋아!" },
                accept = new[] { "좋아, {0}! 단골이니까 기분 좋게 해 줄게." },
                reject = new[] { "에이, 평소처럼 깔끔하게 원래 가격으로 하자~" },
            },
            new TypeLines
            {
                typeName = "장기 보유형",
                greet = new[] { "오랜만에 꺼내 오는구나. 아껴 둔 좋은 물건이지?", "네가 가져오는 건 늘 잘 익었더라. 뭐 가져왔어?" },
                accept = new[] { "{0}! 기다린 보람이 있는 물건이니까." },
                reject = new[] { "아무리 좋아도 그건 좀 세다~ 원래 가격으로 하자." },
            },
            new TypeLines
            {
                typeName = "흥정 집착형",
                greet = new[] { "또 왔구나... 이번엔 얼마를 부르려고?", "흥, 오늘은 나도 단단히 준비했다고!" },
                accept = new[] { "알았어 알았어, {0}! 너한텐 못 이기겠다." },
                reject = new[] { "또 그 수법이야? 이번엔 안 넘어가!", "매번 깎이면 내가 굶어~ 원래 가격!" },
            },
            new TypeLines
            {
                typeName = "기회주의형",
                greet = new[] { "특별 상품 보고 달려왔지? 다 알아~", "오늘 뭐가 비싼지 벌써 알고 온 눈치인데?" },
                accept = new[] { "{0}... 눈치 하나는 빠르다니까." },
                reject = new[] { "특별 상품 덕 봤으면 됐지, 거기서 더는 안 돼~" },
            },
        };
    }
}
