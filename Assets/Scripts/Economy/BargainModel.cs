using UnityEngine;

namespace FarmingSystem.Economy
{
    // 흥정 상황(BargainContext) → 상인이 받아들일 확률(0~1) 계산기의 공통 틀
    // 수확 리듬게임의 HarvestDifficultyModel과 같은 방식:
    //   지금은 RuleBasedBargainModel(배율 규칙)을 쓰고,
    //   나중에 FCM 군집 + RBFN 예측 모델을 이 클래스를 상속해서 만들면
    //   코드 수정 없이 에셋만 ShopSellPanelUI 인스펙터의 Bargain Model 칸에 바꿔 끼우면 됨
    //
    // FCM/RBFN 모델 만드는 예:
    //   [CreateAssetMenu(menuName = "FarmingSystem/Economy/FCM RBFN Bargain")]
    //   public class FcmRbfnBargainModel : BargainModel
    //   {
    //       public override float AcceptChance(BargainContext ctx)
    //       {
    //           // ctx.Features (0~1 값 8개) → FCM 소속도 → RBFN 출력 → 0~1
    //       }
    //       public override void OnResult(BargainContext ctx, bool accepted) { /* 학습하거나 기록 */ }
    //   }
    public abstract class BargainModel : ScriptableObject
    {
        /// <summary>제안을 받아들일 확률 (0 = 절대 안 받음, 1 = 무조건 받음)</summary>
        public abstract float AcceptChance(BargainContext ctx);

        /// <summary>흥정이 끝난 뒤 결과를 알려줌 (학습·기록용, 안 써도 됨)</summary>
        public virtual void OnResult(BargainContext ctx, bool accepted) { }
    }
}