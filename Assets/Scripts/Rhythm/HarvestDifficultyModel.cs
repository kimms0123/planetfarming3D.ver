using UnityEngine;

namespace FarmingSystem.Rhythm
{
    // 환경 특징값(HarvestContext.Features) → 난이도(0 = 쉬움 ~ 1 = 어려움) 계산기의 공통 틀
    // 지금은 RuleBasedDifficultyModel(가중치 합)을 쓰고,
    // 나중에 FCM 군집 + RBFN 예측 모델을 이 클래스를 상속해서 만들면 코드 수정 없이 에셋만 바꿔 끼울 수 있다
    // (HarvestRhythmGame 인스펙터의 Difficulty Model 칸에 넣는 방식)
    public abstract class HarvestDifficultyModel : ScriptableObject
    {
        public abstract float Evaluate(float[] features);
    }
}