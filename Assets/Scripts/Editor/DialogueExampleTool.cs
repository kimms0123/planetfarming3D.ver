#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FarmingSystem.Dialogue;
using FarmingSystem.Farming;
using FarmingSystem.NPC;

namespace FarmingSystem.EditorTools
{
    // 물고기 NPC 예시 대사·퀘스트 만들기 (대사 모음 만드는 법 참고용 + 바로 써 볼 수 있게)
    // 메뉴: Tools → 행성농사일지 → 대화 → 물고기 NPC 예시 대사·퀘스트 만들기
    //  - 씬에 QuestManager가 없으면 만듦 (퀘스트 진행·대화 기억, 저장에 같이 들어감)
    //  - Assets/Dialogue/Fish_Dialogue.asset (대사 모음) + Assets/Dialogue/Quests/Fish_Quest_Hungry.asset (작물 가져다주기 퀘스트)
    //  - Hierarchy에서 물고기 NPC를 선택하고 누르면 그 NPC에 바로 연결 (선택 안 했으면 이름에 fish가 들어간 NPC를 찾음)
    // 이미 있으면 덮어쓰지 않음 (고친 대사가 날아가지 않게). 다시 만들고 싶으면 에셋을 지우고 누르기
    // 메뉴: 대화 → 퀘스트 목록 새로 채우기 (새 퀘스트 에셋을 만들었을 때)
    public static class DialogueExampleTool
    {
        private const string Folder = "Assets/Dialogue";
        private const string QuestFolder = Folder + "/Quests";

        [MenuItem("Tools/행성농사일지/대화/물고기 NPC 예시 대사·퀘스트 만들기")]
        private static void Create()
        {
            EnsureFolder(Folder);
            EnsureFolder(QuestFolder);

            QuestData quest = AssetDatabase.LoadAssetAtPath<QuestData>(QuestFolder + "/Fish_Quest_Hungry.asset");
            bool newQuest = quest == null;
            if (newQuest)
            {
                quest = ScriptableObject.CreateInstance<QuestData>();
                quest.title = "배고픈 낚시꾼";
                quest.description = "요즘 바다에 먹을 게 없다는 낚시꾼에게 작물을 가져다주자.";
                quest.goal = QuestGoal.DeliverItem;
                quest.item = FirstCrop();
                quest.amount = 3;
                quest.rewardBells = 500;
                AssetDatabase.CreateAsset(quest, QuestFolder + "/Fish_Quest_Hungry.asset");
            }

            DialogueSet set = AssetDatabase.LoadAssetAtPath<DialogueSet>(Folder + "/Fish_Dialogue.asset");
            bool newSet = set == null;
            if (newSet)
            {
                set = ScriptableObject.CreateInstance<DialogueSet>();
                set.npcName = "낚시꾼";
                set.entries = BuildFishEntries(quest);
                AssetDatabase.CreateAsset(set, Folder + "/Fish_Dialogue.asset");
            }
            AssetDatabase.SaveAssets();

            // QuestManager
            QuestManager qm = Object.FindAnyObjectByType<QuestManager>(FindObjectsInactive.Include);
            if (qm == null)
            {
                var go = new GameObject("QuestManager");
                Undo.RegisterCreatedObjectUndo(go, "대화·퀘스트 만들기");
                qm = go.AddComponent<QuestManager>();
            }
            qm.FillQuests();

            // 물고기 NPC에 연결
            NPCDialogue fish = FindFishNpc();
            string linked = "물고기 NPC를 못 찾았어요 → NPC의 NPC Dialogue → Dialogue 칸에 Fish_Dialogue를 직접 넣어 주세요";
            if (fish != null)
            {
                var so = new SerializedObject(fish);
                so.FindProperty("dialogue").objectReferenceValue = set;
                so.ApplyModifiedProperties();
                linked = $"'{fish.name}'에 Fish_Dialogue 연결";
            }

            EditorSceneManager.MarkSceneDirty(qm.gameObject.scene);
            Selection.activeObject = set;
            string itemName = quest.item != null ? quest.item.name : "(없음 - 퀘스트 에셋에서 Item을 넣어 주세요)";
            string msg = $"완료!\n\n· 대사 모음: {Folder}/Fish_Dialogue.asset {(newSet ? "(새로 만듦)" : "(이미 있어서 그대로)")}\n" +
                         $"· 퀘스트: Fish_Quest_Hungry - {itemName} {quest.amount}개 가져다주기, 보상 {quest.rewardBells}벨 {(newQuest ? "(새로 만듦)" : "(그대로)")}\n" +
                         $"· QuestManager: 퀘스트 {qm.QuestCount}개 등록\n· {linked}\n\nCtrl+S로 씬 저장";
            Debug.Log("[대화] " + msg);
            EditorUtility.DisplayDialog("물고기 NPC 대사·퀘스트", msg, "확인");
        }

        // 여우 상인 예시 대사 (퀘스트 없음 - 인사말만 상황마다 바뀜. 마지막 줄 다음에 "사기/팔기" 선택지가 나옴)
        [MenuItem("Tools/행성농사일지/대화/여우 상인 예시 대사 만들기")]
        private static void CreateMerchant()
        {
            EnsureFolder(Folder);
            string path = Folder + "/Merchant_Dialogue.asset";
            DialogueSet set = AssetDatabase.LoadAssetAtPath<DialogueSet>(path);
            bool newSet = set == null;
            if (newSet)
            {
                set = ScriptableObject.CreateInstance<DialogueSet>();
                set.npcName = ""; // 비워 두면 ShopDialogueUI의 Merchant Name을 그대로 씀
                set.entries = BuildMerchantEntries();
                AssetDatabase.CreateAsset(set, path);
                AssetDatabase.SaveAssets();
            }

            // 첫 만남·오늘 또 왔는지 기억하려면 QuestManager가 필요
            QuestManager qm = Object.FindAnyObjectByType<QuestManager>(FindObjectsInactive.Include);
            if (qm == null)
            {
                var go = new GameObject("QuestManager");
                Undo.RegisterCreatedObjectUndo(go, "상인 대사 만들기");
                qm = go.AddComponent<QuestManager>();
                qm.FillQuests();
            }

            var ui = Object.FindAnyObjectByType<FarmingSystem.UI.Shop.ShopDialogueUI>(FindObjectsInactive.Include);
            string linked = "ShopDialogueUI를 못 찾았어요 → ShopDialogueUI의 Merchant Dialogue 칸에 Merchant_Dialogue를 직접 넣어 주세요";
            if (ui != null)
            {
                var so = new SerializedObject(ui);
                so.FindProperty("merchantDialogue").objectReferenceValue = set;
                so.ApplyModifiedProperties();
                linked = $"'{ui.name}'의 Merchant Dialogue에 연결";
                EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
            }
            Selection.activeObject = set;
            string msg = $"완료!\n\n· 대사 모음: {path} {(newSet ? "(새로 만듦)" : "(이미 있어서 그대로)")}\n· {linked}\n\nCtrl+S로 씬 저장";
            Debug.Log("[대화] " + msg);
            EditorUtility.DisplayDialog("여우 상인 대사", msg, "확인");
        }

        private static List<DialogueEntry> BuildMerchantEntries()
        {
            var list = new List<DialogueEntry>();
            list.Add(new DialogueEntry
            {
                id = "첫만남",
                priority = 100,
                onceOnly = true,
                talk = TalkCondition.FirstMeeting,
                lines = new[] { "어머, 처음 보는 얼굴이네? 난 별과 별 사이를 떠도는 여우 상인이야.", "씨앗이든 집 키트든, 필요한 건 다 있어. 천천히 둘러봐!" },
            });
            list.Add(new DialogueEntry { id = "태풍", weather = WeatherCondition.Typhoon, priority = 30, lines = new[] { "이런 날씨에도 왔어? 배가 날아갈까 봐 조마조마해… 빨리 골라!" } });
            list.Add(new DialogueEntry { id = "또옴", talk = TalkCondition.AlreadyToday, priority = 20, lines = new[] { "또 왔네? 뭐 잊은 거라도 있어?" } });
            list.Add(new DialogueEntry { id = "기본", lines = new[] { "어서 와! 오늘은 뭘 찾아?" } });
            list.Add(new DialogueEntry { id = "기본2", lines = new[] { "좋은 물건 들여왔어. 구경하고 가!" } });
            list.Add(new DialogueEntry { id = "아침", time = TimeCondition.Morning, weight = 2f, lines = new[] { "아침부터 부지런하네! 첫 손님이니까 잘해 줄게." } });
            list.Add(new DialogueEntry { id = "밤", time = TimeCondition.Night, weight = 2f, lines = new[] { "이 시간까지 안 자고 뭐 해? 얼른 사고 들어가 쉬어." } });
            list.Add(new DialogueEntry { id = "비", weather = WeatherCondition.Rain, weight = 3f, lines = new[] { "비 오는 날엔 손님이 없어서 심심했는데, 잘 왔어!" } });
            return list;
        }

        [MenuItem("Tools/행성농사일지/대화/퀘스트 목록 새로 채우기")]
        private static void FillQuests()
        {
            QuestManager qm = Object.FindAnyObjectByType<QuestManager>(FindObjectsInactive.Include);
            if (qm == null) { EditorUtility.DisplayDialog("퀘스트 목록", "씬에 QuestManager가 없어요. '물고기 NPC 예시 대사·퀘스트 만들기'를 먼저 눌러 주세요.", "확인"); return; }
            qm.FillQuests();
            EditorSceneManager.MarkSceneDirty(qm.gameObject.scene);
            EditorUtility.DisplayDialog("퀘스트 목록", $"퀘스트 {qm.QuestCount}개 등록. Ctrl+S로 씬 저장", "확인");
        }

        private static List<DialogueEntry> BuildFishEntries(QuestData q)
        {
            var list = new List<DialogueEntry>();

            // 1. 처음 만났을 때 (한 번만)
            list.Add(new DialogueEntry
            {
                id = "첫만남",
                priority = 100,
                onceOnly = true,
                talk = TalkCondition.FirstMeeting,
                lines = new[] { "오, 이 섬에도 사람이 살았네? 소문 듣고 와 봤지.", "난 바다를 떠도는 낚시꾼이야. 앞으로 잘 부탁해!" },
            });

            // 2. 퀘스트: 끝낼 수 있을 때 (가장 먼저)
            list.Add(new DialogueEntry
            {
                id = "퀘스트_건네기",
                priority = 60,
                quest = q,
                questState = QuestCondition.ReadyToTurnIn,
                lines = new[] { "오! 그거 {item} 아니야? {have}개나 있네!" },
                choices = new List<DialogueChoice>
                {
                    new DialogueChoice { text = "건네주기", reply = "정말 고마워! 이건 내 마음이야. ({reward}벨)", action = ChoiceAction.TurnInQuest },
                    new DialogueChoice { text = "아직이요", reply = "그래, 준비되면 줘." },
                },
            });

            // 3. 퀘스트: 아직 안 받았을 때 (첫 만남 다음 대화부터)
            list.Add(new DialogueEntry
            {
                id = "퀘스트_제안",
                priority = 50,
                quest = q,
                questState = QuestCondition.NotStarted,
                lines = new[] { "요즘 바다에 먹을 게 없어서 배가 고프네…", "혹시 {item} {amount}개만 나눠 줄 수 있어?" },
                choices = new List<DialogueChoice>
                {
                    new DialogueChoice { text = "좋아요!", reply = "고마워! 천천히 가져와도 돼.", action = ChoiceAction.StartQuest },
                    new DialogueChoice { text = "다음에요", reply = "그래, 생각나면 말해 줘." },
                },
            });

            // 4. 퀘스트: 진행 중 (아직 덜 모음)
            list.Add(new DialogueEntry
            {
                id = "퀘스트_진행중",
                priority = 40,
                quest = q,
                questState = QuestCondition.InProgress,
                lines = new[] { "{item} {amount}개, 잊지 않았지? 지금 {have}개 있네!" },
            });

            // 5. 평소 잡담 (우선순위 0끼리 무작위, 시간·날씨에 따라)
            list.Add(new DialogueEntry { id = "잡담_기본", lines = new[] { "오늘도 바다는 넓구나~" } });
            list.Add(new DialogueEntry { id = "잡담_기본2", lines = new[] { "물결 소리 들려? 이 섬은 바다가 참 좋아." } });
            list.Add(new DialogueEntry { id = "잡담_아침", time = TimeCondition.Morning, weight = 2f, lines = new[] { "아침 바다 냄새 좋지? 이때 물고기가 제일 잘 잡혀." } });
            list.Add(new DialogueEntry { id = "잡담_밤", time = TimeCondition.Night, weight = 2f, lines = new[] { "밤바다엔 별이 비쳐서 예뻐. 졸리면 들어가 자고." } });
            list.Add(new DialogueEntry { id = "잡담_비", weather = WeatherCondition.Rain, weight = 3f, lines = new[] { "비 오는 날엔 물고기가 수면 가까이 올라와!" } });
            list.Add(new DialogueEntry { id = "잡담_태풍", weather = WeatherCondition.Typhoon, priority = 30, lines = new[] { "이런 날엔 절대 바다에 나가면 안 돼! 밭도 조심하고." } });
            list.Add(new DialogueEntry { id = "잡담_또옴", talk = TalkCondition.AlreadyToday, lines = new[] { "또 왔어? 심심했구나!" } });
            list.Add(new DialogueEntry { id = "잡담_완료후", quest = q, questState = QuestCondition.Completed, weight = 2f, lines = new[] { "덕분에 배불러! 언젠가 낚싯대를 만들어 줄게." } });
            return list;
        }

        private static CropData FirstCrop()
        {
            foreach (string g in AssetDatabase.FindAssets("t:CropData", new[] { "Assets" }))
            {
                var c = AssetDatabase.LoadAssetAtPath<CropData>(AssetDatabase.GUIDToAssetPath(g));
                if (c != null) return c;
            }
            return null;
        }

        private static NPCDialogue FindFishNpc()
        {
            if (Selection.activeGameObject != null && Selection.activeGameObject.TryGetComponent(out NPCDialogue sel)) return sel;
            foreach (NPCDialogue n in Object.FindObjectsByType<NPCDialogue>(FindObjectsInactive.Include))
            {
                string lower = n.name.ToLowerInvariant();
                if (lower.Contains("fish") || n.name.Contains("물고기") || n.name.Contains("낚시")) return n;
            }
            return null;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
#endif