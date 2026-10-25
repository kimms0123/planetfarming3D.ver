using UnityEngine;

namespace FarmingSystem.Rhythm
{
    // 가장 단순한 난이도 모델: 난이도 = 기본값 + Σ(특징값 × 가중치), 0~1로 자름
    // Project 창 우클릭 → Create → FarmingSystem → Rhythm → Rule Based Difficulty 로 에셋을 만들어 가중치를 조절할 수 있다
    // 에셋을 안 넣으면 HarvestRhythmGame이 아래 기본 가중치(Default)로 계산한다
    [CreateAssetMenu(fileName = "RuleBasedDifficulty", menuName = "FarmingSystem/Rhythm/Rule Based Difficulty")]
    public class RuleBasedDifficultyModel : HarvestDifficultyModel
    {
        // 기본 가중치 (HarvestContext.FeatureNames 순서: 작물 복잡도, 방치 일수, 시간대, 계절 적합도, 계절 진행도, 날씨)
        public static readonly float[] DefaultWeights = { 0.35f, 0.25f, 0.15f, 0.1f, 0.1f, 0.15f };
        public const float DefaultBias = 0.1f;

        [Tooltip("모든 수확의 기본 난이도")]
        [Range(0f, 1f)] public float bias = DefaultBias;
        [Tooltip("특징값별 가중치 (순서: 작물 복잡도, 방치 일수, 시간대, 계절 적합도, 계절 진행도, 날씨)")]
        public float[] weights = (float[])DefaultWeights.Clone();

        public override float Evaluate(float[] features) => Compute(features, weights, bias);

        public static float EvaluateDefault(float[] features) => Compute(features, DefaultWeights, DefaultBias);

        private static float Compute(float[] features, float[] w, float b)
        {
            float d = b;
            if (features != null && w != null)
                for (int i = 0; i < features.Length && i < w.Length; i++)
                    d += features[i] * w[i];
            return Mathf.Clamp01(d);
        }
    }
}