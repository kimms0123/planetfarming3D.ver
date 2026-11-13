using UnityEngine;

namespace FarmingSystem.Economy
{
    // 지금 쓰는 흥정 규칙 (흥정 연출에 붙어 있던 규칙을 그대로 옮긴 것)
    //  - 원래 금액 이하로 부르면 항상 수락
    //  - Safe Ratio(110%)까지는 항상 수락
    //  - Max Ratio(150%)로 갈수록 수락 확률이 부드럽게 줄어듦
    //  - 그 이상은 항상 거절
    // Project 창 우클릭 → Create → FarmingSystem → Economy → Rule Based Bargain 으로 에셋을 만들어 값을 조절할 수 있음
    // 에셋을 안 넣으면 ShopSellPanelUI의 Safe Ratio / Max Ratio 값으로 같은 규칙을 씀
    [CreateAssetMenu(fileName = "RuleBasedBargain", menuName = "FarmingSystem/Economy/Rule Based Bargain")]
    public class RuleBasedBargainModel : BargainModel
    {
        public const float DefaultSafeRatio = 1.1f;
        public const float DefaultMaxRatio = 1.5f;

        [Tooltip("원래 금액의 이 배율까지는 항상 수락 (1.1 = 10% 더까지)")]
        [Range(1f, 2f)] public float safeRatio = DefaultSafeRatio;
        [Tooltip("원래 금액의 이 배율 이상은 항상 거절 (1.5 = 50% 더)")]
        [Range(1.01f, 3f)] public float maxRatio = DefaultMaxRatio;

        public override float AcceptChance(BargainContext ctx) => Compute(ctx, safeRatio, maxRatio);

        public static float Compute(BargainContext ctx, float safe, float max)
        {
            if (ctx == null || ctx.Offer <= ctx.BaseTotal) return 1f;
            float ratio = ctx.OfferRatio;
            if (ratio <= safe) return 1f;
            if (ratio >= max) return 0f;
            float k = (ratio - safe) / Mathf.Max(0.0001f, max - safe);
            return 1f - k * k * (3f - 2f * k); // 부드럽게 줄어듦
        }
    }
}