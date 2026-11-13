using UnityEngine;
using FarmingSystem.Inventory;

namespace FarmingSystem.Dialogue
{
    public enum QuestGoal
    {
        [InspectorName("물건 가져다주기")] DeliverItem,
        [InspectorName("돈 벌기 (받은 뒤부터)")] EarnBells,
    }

    // 퀘스트(부탁) 하나
    // Project 창 우클릭 → Create → FarmingSystem → Dialogue → Quest
    // 대사 묶음(Dialogue Set)의 Quest 칸에 연결해서 "받기·진행 중·끝낼 수 있음·완료" 상태별로 다른 말을 하게 함
    // 나중에 낚시·광산이 생기면 QuestGoal에 "물고기 잡기" 같은 종류만 추가하면 됨
    [CreateAssetMenu(fileName = "Quest", menuName = "FarmingSystem/Dialogue/Quest")]
    public class QuestData : ScriptableObject
    {
        [Tooltip("알림에 뜨는 이름 (예: 배고픈 낚시꾼)")]
        public string title = "새 부탁";
        [TextArea(2, 3)] public string description;

        [Header("목표")]
        public QuestGoal goal = QuestGoal.DeliverItem;
        [Tooltip("가져다줄 물건 (물건 가져다주기일 때)")]
        public ItemData item;
        [Tooltip("개수 또는 벌어야 할 벨")]
        [Min(1)] public int amount = 3;

        [Header("보상")]
        public int rewardBells = 300;
        public ItemData rewardItem;
        [Min(1)] public int rewardItemAmount = 1;

        [Header("이어지는 퀘스트")]
        [Tooltip("이 퀘스트를 먼저 끝내야 받을 수 있음 (비우면 언제든)")]
        public QuestData requires;
    }
}
