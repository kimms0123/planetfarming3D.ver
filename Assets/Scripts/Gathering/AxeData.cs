using UnityEngine;

namespace FarmingSystem.Inventory
{
    // 도끼 (나무 베기용 도구) - 도구칸에 넣고 들면 나무를 좌클릭으로 벨 수 있음 (TreeChopper)
    // Tool Type은 None으로 둬야 함 → 밭 갈기·물 주기 같은 농사 동작과 겹치지 않음
    // 만들기: Tools → 행성농사일지 → 나무 베기 → 도끼·목재 만들기 (또는 Create → FarmingSystem → Axe Data)
    [CreateAssetMenu(fileName = "NewAxeData", menuName = "FarmingSystem/Axe Data")]
    public class AxeData : ToolData
    {
        [Header("도끼 전용")]
        [Tooltip("나무 한 그루를 쓰러뜨리는 데 필요한 횟수 (좋은 도끼일수록 적게)")]
        [Min(1)] public int hitsPerTree = 3;
    }
}
