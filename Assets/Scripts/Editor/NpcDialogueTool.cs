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
    // 주민 NPC 대사 모음 한 번에 만들기 (상인 제외) - 모동숲 주민처럼 상황마다 다른 말 + 가끔 질문하고 대답에 반응
    // 메뉴: Tools → 행성농사일지 → 대화 → 주민 NPC 대사 모두 만들기 (상인 제외)
    //
    //  NPC        대화창 이름   성격
    //  Mushroom   버섯돌이     쪼꼬미 아기 버섯. 말끝 "~뽀요". 비·그늘 좋아함, 햇볕 무서움
    //  Shark      샤크         덩치 크고 무섭게 생긴 츤데레. 말끝 "~샥". 퉁명스럽지만 몰래 챙겨 줌, 이빨이 자랑
    //  Fish       뻐끔이       우주선을 타고 별 바다를 여행하는 수다쟁이 물고기. 말끝 "~뻐끔". 배고픈 부탁(퀘스트) 있음
    //
    //  - Assets/Dialogue/ 에 Mushroom_Dialogue / Shark_Dialogue / Fish_Dialogue 를 만들고
    //    씬에서 이름이 Mushroom / Shark / Fish 인 NPC의 NPC Dialogue → Dialogue 칸에 연결
    //  - 이미 있는 대사 모음은 물어보고 바꿈 (바꾸면 Inspector에서 고친 대사는 사라짐, 연결은 그대로)
    //  - 물고기 퀘스트는 Assets/Dialogue/Quests/Fish_Quest_Hungry 가 있으면 그대로 씀 (없으면 만듦)
    //  - 씬에 QuestManager가 없으면 만듦 (첫 만남·오늘 말 걸었는지·퀘스트 기억)
    //  대사·이름을 바꾸려면: Project 창에서 대사 모음 에셋을 클릭 → Inspector에서 고치기
    //  우선순위: 첫 만남 100 > 퀘스트 60~40 > 태풍 30 > 오늘 첫 인사 20 > 오늘 또 옴 10 > 평소·날씨·계절 0 (같은 우선순위끼리는 Weight대로 무작위)
    public static class NpcDialogueTool
    {
        private const string Folder = "Assets/Dialogue";
        private const string QuestFolder = Folder + "/Quests";

        private struct Npc { public string objectName, displayName, asset; }

        [MenuItem("Tools/행성농사일지/대화/주민 NPC 대사 모두 만들기 (상인 제외)")]
        private static void CreateAll()
        {
            EnsureFolder(Folder);
            EnsureFolder(QuestFolder);

            var npcs = new[]
            {
                new Npc { objectName = "Mushroom", displayName = "버섯돌이", asset = "Mushroom_Dialogue" },
                new Npc { objectName = "Shark", displayName = "샤크", asset = "Shark_Dialogue" },
                new Npc { objectName = "Fish", displayName = "뻐끔이", asset = "Fish_Dialogue" },
            };

            bool anyExists = false;
            foreach (Npc n in npcs) if (AssetDatabase.LoadAssetAtPath<DialogueSet>($"{Folder}/{n.asset}.asset") != null) anyExists = true;
            bool overwrite = anyExists && EditorUtility.DisplayDialog("주민 NPC 대사",
                "이미 있는 대사 모음이 있어요.\n새 대사로 바꿀까요?\n\n(바꾸면 Inspector에서 직접 고친 대사는 사라져요. NPC 연결은 그대로)",
                "새 대사로 바꾸기", "있는 건 그대로 두기");

            QuestData fishQuest = FishQuest();
            var report = new List<string>();
            foreach (Npc n in npcs)
            {
                List<DialogueEntry> entries = n.objectName == "Mushroom" ? Mushroom()
                                            : n.objectName == "Shark" ? Shark()
                                            : Fish(fishQuest);
                string path = $"{Folder}/{n.asset}.asset";
                DialogueSet set = AssetDatabase.LoadAssetAtPath<DialogueSet>(path);
                string state;
                if (set == null)
                {
                    set = ScriptableObject.CreateInstance<DialogueSet>();
                    set.npcName = n.displayName;
                    set.entries = entries;
                    AssetDatabase.CreateAsset(set, path);
                    state = "새로 만듦";
                }
                else if (overwrite)
                {
                    Undo.RecordObject(set, "주민 NPC 대사 바꾸기");
                    set.npcName = n.displayName;
                    set.entries = entries;
                    EditorUtility.SetDirty(set);
                    state = "새 대사로 바꿈";
                }
                else state = "그대로 둠";

                NPCDialogue npc = FindNpc(n.objectName);
                string link = "씬에서 못 찾음 → Dialogue 칸에 직접 넣기";
                if (npc != null)
                {
                    var so = new SerializedObject(npc);
                    so.FindProperty("dialogue").objectReferenceValue = set;
                    so.ApplyModifiedProperties();
                    EditorSceneManager.MarkSceneDirty(npc.gameObject.scene);
                    link = $"'{npc.name}'에 연결";
                }
                report.Add($"· {n.displayName} ({n.asset}): {state}, {link}");
            }
            AssetDatabase.SaveAssets();

            QuestManager qm = Object.FindAnyObjectByType<QuestManager>(FindObjectsInactive.Include);
            if (qm == null)
            {
                var go = new GameObject("QuestManager");
                Undo.RegisterCreatedObjectUndo(go, "주민 NPC 대사 만들기");
                qm = go.AddComponent<QuestManager>();
            }
            qm.FillQuests();
            EditorSceneManager.MarkSceneDirty(qm.gameObject.scene);

            string msg = "완료!\n\n" + string.Join("\n", report) + $"\n· QuestManager: 퀘스트 {qm.QuestCount}개 등록\n\nCtrl+S로 씬 저장";
            Debug.Log("[대화] " + msg);
            EditorUtility.DisplayDialog("주민 NPC 대사", msg, "확인");
        }

        // ───────────────────────── 버섯돌이 ─────────────────────────

        private static List<DialogueEntry> Mushroom()
        {
            var l = new List<DialogueEntry>();
            l.Add(new DialogueEntry
            {
                id = "첫만남", priority = 100, onceOnly = true, talk = TalkCondition.FirstMeeting,
                lines = new[]
                {
                    "앗! 바, 밟지 마요! 여기 아래예요, 아래!",
                    "…휴, 큰일 날 뻔했뽀요. 나는 버섯돌이예요!",
                    "우주를 둥둥 떠다니다가 여기 쏙 내려앉았어요. 이 행성 흙, 촉촉하고 폭신해서 마음에 들어요.",
                    "오늘부터 여기가 우리 집이뽀요! 우리 친하게 지내요?",
                },
                choices = Choices("좋아!", "와아! 포자가 막 퐁퐁 나와요! 잘 부탁해요뽀요!",
                                  "조금 생각해 볼게", "히잉… 그래도 기다릴게요. 나는 원래 못 움직이뽀요."),
            });
            l.Add(new DialogueEntry { id = "태풍", priority = 30, weather = WeatherCondition.Typhoon,
                lines = new[] { "으앗, 바람에 날아갈 것 같아요!", "내 줄기 꼭 잡아 줘요뽀요! 놓으면 안 돼요!" } });

            l.Add(Greet("인사_아침", TimeCondition.Morning, "으음… 아직 졸려요…", "이슬 한 방울만 더 마시고 일어날래뽀요…"));
            var noon = Greet("인사_낮", TimeCondition.Afternoon, "햇볕이 너무 세요!", "그쪽 그림자 밑에 잠깐 숨어도 돼뽀요?");
            noon.choices = Choices("들어와", "헤헤, 시원하다~ 그쪽 그림자 최고예요뽀요!", "나도 더워", "그럼 우리 둘 다 나무 밑으로 가요! 같이 숨어요뽀요!");
            l.Add(noon);
            l.Add(Greet("인사_저녁", TimeCondition.Evening, "노을이 지면 내 갓이 노랗게 물들어요.", "예쁘죠? 헤헤뽀요."));
            l.Add(Greet("인사_밤", TimeCondition.Night, "밤이 제일 좋아요.", "별빛 받으면 쑥쑥 자라는 기분이뽀요!"));

            l.Add(Again("또옴", "또 왔어요? 그쪽 발소리는 이제 알아요. 쿵, 쿵, 하면 아, 왔구나 해요!"));
            l.Add(Again("또옴2", "헤헤, 나 보고 싶었어요? 나도 그쪽 기다리고 있었뽀요."));

            l.Add(Chat("잡담_물밭", "물 준 밭 옆에 앉아 있으면 발이 시원해요.", "…아, 나 발 없지뽀요."));
            l.Add(Chat("잡담_노래", "작물 친구들은 말이 없어요.", "그래도 내가 노래 불러 주면 쑥쑥 자라는 것 같아요!"));
            l.Add(Chat("잡담_리듬", "리듬에 맞춰 수확하는 거 봤어요!", "나도 갓으로 박자 맞춰 봤는데… 어지러웠뽀요."));
            l.Add(Ask("질문_날씨", "있잖아요, 그쪽은 어떤 날씨가 제일 좋아요?",
                "비 오는 날", "꺄아! 나랑 똑같아요! 우리 비 오면 같이 놀아요뽀요!",
                "맑은 날", "으으… 그쪽은 햇볕이 안 무섭구나. 부럽기도 하고 무섭기도 해뽀요."));
            l.Add(Ask("질문_키", "나 어제보다 조금 컸죠? 그렇죠?",
                "응, 컸어!", "역시! 밤새 별빛 많이 먹었거든요. 헤헤뽀요!",
                "똑같은데?", "히잉… 갓이 조금 커진 것 같았는데… 내일 다시 봐 줘요뽀요."));

            l.Add(WeatherLine("비", WeatherCondition.Rain, "비다아아! 갓 위로 톡톡 떨어지는 거 간지러워요!", "오늘은 최고의 날이뽀요!"));
            var sunny = WeatherLine("맑음", WeatherCondition.Clear, "오늘은 그늘에서 꼼짝 안 할 거예요.", "버섯은 원래 그런 거뽀요.");
            sunny.time = TimeCondition.Afternoon; sunny.weight = 1.5f;
            l.Add(sunny);

            l.Add(SeasonLine("봄", SeasonCondition.Spring, "새싹 친구들이 잔뜩 생겼어요!", "다들 나보다 키가 커서 조금 샘나뽀요."));
            l.Add(SeasonLine("여름", SeasonCondition.Summer, "여름은 무서워요…", "햇볕에 바싹 말라서 버섯 과자가 될지도 몰라요…"));
            var fall = SeasonLine("가을", SeasonCondition.Autumn, "가을은 버섯의 계절이에요!", "오늘따라 내가 더 반짝반짝해 보이지 않아요?");
            fall.choices = Choices("반짝반짝해!", "헤헤, 갓 닦느라 아침부터 바빴거든요뽀요!", "잘 모르겠어", "에잇, 자세히 봐요! 여기, 여기 반짝이잖아요뽀요!");
            l.Add(fall);
            l.Add(SeasonLine("겨울", SeasonCondition.Winter, "차가워요… 갓 위에 눈이 쌓이면 나 눈사람 버섯 같아요?", "겨울엔 꾸벅꾸벅 졸려요. 깨우면 조금 칭얼댈지도 몰라뽀요."));
            return l;
        }

        // ───────────────────────── 샤크 (츤데레 상어) ─────────────────────────

        private static List<DialogueEntry> Shark()
        {
            var l = new List<DialogueEntry>();
            l.Add(new DialogueEntry
            {
                id = "첫만남", priority = 100, onceOnly = true, talk = TalkCondition.FirstMeeting,
                lines = new[]
                {
                    "…뭘 그렇게 쳐다봐. 이빨 처음 보냐?",
                    "난 샤크다. 이 근처 물가는 내 구역이니까 함부로 첨벙대지 마라샥.",
                    "…그래도 길 잃으면 불러. 딱히 걱정돼서 그러는 건 아니고샥.",
                },
                choices = Choices("고마워!", "고, 고맙긴 뭘! 그냥 하는 말이라고샥!",
                                  "이빨 멋있다", "…흥. 보는 눈은 있네. 매일 아침 300번씩 닦는다샥."),
            });
            l.Add(new DialogueEntry { id = "태풍", priority = 30, weather = WeatherCondition.Typhoon,
                lines = new[] { "이런 날 밖에 왜 돌아다녀! 날아가면 어쩌려고!", "…빨리 집에 들어가. 밭은 내가 대충 봐 줄 테니까샥." } });

            l.Add(Greet("인사_아침", TimeCondition.Morning, "아침부터 부지런 떨긴.", "…나도 방금 일어난 거 아니다. 한참 전에 일어났다샥."));
            l.Add(Greet("인사_낮", TimeCondition.Afternoon, "덥다. 이럴 땐 시원한 바닷물에 쏙 들어가 있는 게 최고야.", "너도 같이… 아, 아니다. 혼자 들어갈 거다샥."));
            l.Add(Greet("인사_저녁", TimeCondition.Evening, "해 질 때 바다가 주황색이 되는 거 알아?", "…딱히 예쁘다는 건 아니고, 그냥 그렇다고샥."));
            var night = Greet("인사_밤", TimeCondition.Night, "이 시간에 뭐 해. 어두운 데서 돌아다니면 위험하다.", "…혹시 무서워서 못 자는 거냐?");
            night.choices = Choices("조금 무서워", "칫, 어쩔 수 없지. 내가 여기 있을 테니까 안심하고 가라샥.", "아니거든!", "흥, 누가 걱정했대? 얼른 자라샥.");
            l.Add(night);

            l.Add(Again("또옴", "또 왔냐? …할 일 없으면 여기 좀 앉아 있다 가든가샥."));
            l.Add(Again("또옴2", "자꾸 오네. 내가 그렇게 좋냐? …아, 아니면 됐다샥."));

            l.Add(Chat("잡담_이빨", "이빨이 빠져도 새로 난다. 평생 3만 개쯤?", "…자랑하는 거 맞다샥."));
            l.Add(Chat("잡담_밭", "네 밭 말이야. 어제 지나가다 봤는데…", "…꽤 잘 키우더라. 아, 우연히 본 거다샥."));
            l.Add(Chat("잡담_버섯", "저기 쪼그만 버섯 녀석 말이야. 자꾸 내 그림자에 숨어.", "…뭐, 비켜 줄 생각은 없다샥."));
            l.Add(Ask("질문_무서워", "솔직히 말해. 나 처음 봤을 때 무서웠지?",
                "응, 조금", "…그렇구나. 웃는 연습 좀 해야겠다샥.",
                "전혀!", "거, 거짓말! …그래도 고맙다샥."));
            l.Add(Ask("질문_생선", "물어볼 게 있는데… 너 생선 좋아하냐?",
                "좋아해", "…그래? 다음에 잡으면 하나 줄게. 많이 잡혀서 그러는 거다샥.",
                "별로", "흥, 맛을 모르는구먼. …그럼 작물이라도 키워서 먹어라샥."));

            l.Add(WeatherLine("비", WeatherCondition.Rain, "비 오는 날이 좋다. 땅 위에서도 물속 같거든.", "…오늘은 기분이 좋으니까 말 좀 걸어도 된다샥."));
            l.Add(WeatherLine("맑음", WeatherCondition.Clear, "쨍쨍하네. 지느러미가 바삭해질 것 같다.", "…물 좀 뿌려 줄 거면 뿌려 주든가샥."));

            l.Add(SeasonLine("봄", SeasonCondition.Spring, "봄이라고 다들 들떠 있네.", "…나도 꽃 하나쯤은 봐 줄 수 있다샥."));
            l.Add(SeasonLine("여름", SeasonCondition.Summer, "여름은 내 계절이다!", "바다가 따뜻해서 하루 종일 헤엄칠 수 있다샥!"));
            l.Add(SeasonLine("가을", SeasonCondition.Autumn, "가을엔 작물이 잔뜩 익지.", "…남는 거 있으면 나눠 줘도 된다. 딱히 달라는 건 아니고샥."));
            l.Add(SeasonLine("겨울", SeasonCondition.Winter, "추, 추운 거 아니다. 이빨이 딱딱 부딪히는 건 원래 그런 거다샥.", "…목도리 같은 거 있으면 빌려주든가샥."));
            return l;
        }

        // ───────────────────────── 뻐끔이 (여행자 물고기, 퀘스트 있음) ─────────────────────────

        private static List<DialogueEntry> Fish(QuestData q)
        {
            var l = new List<DialogueEntry>();
            l.Add(new DialogueEntry
            {
                id = "첫만남", priority = 100, onceOnly = true, talk = TalkCondition.FirstMeeting,
                lines = new[]
                {
                    "뻐끔! 뻐끔! 안녕! 이 행성에도 누가 살고 있었구나!",
                    "나는 뻐끔이! 우주선을 타고 별 바다를 여행하고 있어뻐끔.",
                    "벌써 행성을 스무 개도 넘게 다녔는데, 여기처럼 바다가 반짝이는 곳은 처음이야!",
                    "한동안 여기 머물러도 될까뻐끔?",
                },
                choices = Choices("물론이지!", "야호! 오늘부터 여기가 내 정거장이야뻐끔!",
                                  "얼마나 있을 건데?", "음… 마음에 들 때까지? 그러니까 아주 오래뻐끔!"),
            });

            if (q != null)
            {
                l.Add(new DialogueEntry
                {
                    id = "퀘스트_건네기", priority = 60, quest = q, questState = QuestCondition.ReadyToTurnIn,
                    lines = new[] { "뻐끔?! 그거 {item} 아니야? {have}개나 있네!" },
                    choices = new List<DialogueChoice>
                    {
                        new DialogueChoice { text = "건네주기", reply = "와아, 고마워! 이건 여행하면서 모은 거야. ({reward}벨)", action = ChoiceAction.TurnInQuest },
                        new DialogueChoice { text = "아직이야", reply = "괜찮아, 준비되면 줘뻐끔!" },
                    },
                });
                l.Add(new DialogueEntry
                {
                    id = "퀘스트_제안", priority = 50, quest = q, questState = QuestCondition.NotStarted,
                    lines = new[] { "있잖아… 우주선 냉장고가 텅 비었어.", "혹시 {item} {amount}개만 나눠 줄 수 있어뻐끔?" },
                    choices = new List<DialogueChoice>
                    {
                        new DialogueChoice { text = "좋아!", reply = "고마워! 천천히 가져와도 돼뻐끔!", action = ChoiceAction.StartQuest },
                        new DialogueChoice { text = "다음에", reply = "응… 배 꼬르륵 소리 들려도 모른 척해 줘뻐끔." },
                    },
                });
                l.Add(new DialogueEntry { id = "퀘스트_진행중", priority = 40, quest = q, questState = QuestCondition.InProgress,
                    lines = new[] { "{item} {amount}개, 기억하지? 지금 {have}개 있네! 조금만 더뻐끔!" } });
                l.Add(new DialogueEntry { id = "퀘스트_완료후", quest = q, questState = QuestCondition.Completed, weight = 2f,
                    lines = new[] { "네가 준 {item}, 정말 맛있었어!", "다음 행성 가면 너 자랑할 거야뻐끔!" } });
            }

            l.Add(new DialogueEntry { id = "태풍", priority = 30, weather = WeatherCondition.Typhoon,
                lines = new[] { "우와아, 우주 폭풍보다 세다!", "우주선 날아갈까 봐 꽉 묶어 뒀어. 너도 조심해뻐끔!" } });

            l.Add(Greet("인사_아침", TimeCondition.Morning, "좋은 아침! 우주선 창문으로 해 뜨는 거 봤어?", "이 행성 해는 꼭 달걀 노른자 같아뻐끔!"));
            l.Add(Greet("인사_낮", TimeCondition.Afternoon, "점심은 먹었어? 나는 아직!", "여행하다 보면 밥때를 자꾸 까먹어뻐끔."));
            l.Add(Greet("인사_저녁", TimeCondition.Evening, "저녁 하늘에 별이 하나씩 켜지고 있어!", "저 중에 내가 갔던 별도 있을걸뻐끔?"));
            var night = Greet("인사_밤", TimeCondition.Night, "밤엔 우주선 지붕에 누워서 별을 세.", "너도 별 세는 거 좋아해뻐끔?");
            night.choices = Choices("좋아해", "그럼 다음에 같이 세자! 내가 별 이름 다 알려 줄게뻐끔!", "금방 졸려", "헤헤, 나도 백 개쯤 세면 잠들어뻐끔.");
            l.Add(night);

            l.Add(Again("또옴", "또 왔네! 할 얘기가 아직 산더미야뻐끔!"));
            l.Add(Again("또옴2", "뻐끔! 혹시 내 여행 얘기 또 듣고 싶어서 왔어?"));

            l.Add(Chat("잡담_젤리별", "전에 갔던 행성은 바다가 전부 젤리였어!", "헤엄치다가 그대로 굳어 버릴 뻔했지뻐끔."));
            l.Add(Chat("잡담_상어", "저기 상어 아저씨, 무섭게 생겼지?", "근데 어제 나 우주선 고치는 거 몰래 도와줬어. 비밀이야뻐끔!"));
            l.Add(Chat("잡담_우주선", "내 우주선 이름은 '뽀글호'야.", "엔진에서 거품이 뽀글뽀글 나와서 그렇게 지었어뻐끔!"));
            l.Add(Ask("질문_여행", "너도 언젠가 다른 행성에 가 보고 싶어?",
                "가 보고 싶어!", "그럼 그때 내 우주선 태워 줄게! 창가 자리로뻐끔!",
                "여기가 좋아", "헤헤, 그 마음 알 것 같아. 여기 진짜 좋은 곳이야뻐끔."));
            l.Add(Ask("질문_기념품", "다음 행성 가면 기념품 사 올게! 뭐가 좋아?",
                "반짝이는 돌", "좋아! 제일 반짝이는 걸로 골라 올게뻐끔!",
                "맛있는 거", "역시! 나랑 생각이 똑같아뻐끔!"));

            l.Add(WeatherLine("비", WeatherCondition.Rain, "비다! 지느러미가 촉촉해서 기분 최고야!", "물고기한테 비는 공짜 샤워야뻐끔!"));
            l.Add(WeatherLine("맑음", WeatherCondition.Clear, "날씨 좋다! 우주선 빨래 널기 딱 좋은 날이야.", "…물고기도 빨래 한다고뻐끔."));

            l.Add(SeasonLine("봄", SeasonCondition.Spring, "봄바람에서 꽃 냄새가 나!", "이 행성은 계절이 있어서 좋아뻐끔."));
            l.Add(SeasonLine("여름", SeasonCondition.Summer, "여름 바다는 따뜻해서 헤엄치기 딱이야!", "상어 아저씨도 오늘은 기분 좋아 보이더라뻐끔."));
            l.Add(SeasonLine("가을", SeasonCondition.Autumn, "가을엔 밭이 알록달록해지네!", "우주에서 보면 이 행성 꼭 사탕 같을 거야뻐끔."));
            l.Add(SeasonLine("겨울", SeasonCondition.Winter, "추워! 우주선 난로 앞에서 안 나오고 싶어.", "그래도 눈 내리는 바다는 꼭 봐야 해. 진짜 예뻐뻐끔."));
            return l;
        }

        // ───────────────────────── 대사 만들기 도우미 ─────────────────────────

        private static List<DialogueChoice> Choices(string a, string aReply, string b, string bReply) =>
            new List<DialogueChoice> { new DialogueChoice { text = a, reply = aReply }, new DialogueChoice { text = b, reply = bReply } };

        private static DialogueEntry Greet(string id, TimeCondition time, params string[] lines) =>
            new DialogueEntry { id = id, priority = 20, talk = TalkCondition.FirstToday, time = time, lines = lines };

        private static DialogueEntry Again(string id, params string[] lines) =>
            new DialogueEntry { id = id, priority = 10, talk = TalkCondition.AlreadyToday, lines = lines };

        private static DialogueEntry Chat(string id, params string[] lines) => new DialogueEntry { id = id, lines = lines };

        private static DialogueEntry Ask(string id, string line, string a, string aReply, string b, string bReply) =>
            new DialogueEntry { id = id, weight = 1.5f, lines = new[] { line }, choices = Choices(a, aReply, b, bReply) };

        private static DialogueEntry WeatherLine(string id, WeatherCondition w, params string[] lines) =>
            new DialogueEntry { id = id, weather = w, weight = w == WeatherCondition.Rain ? 3f : 1f, lines = lines };

        private static DialogueEntry SeasonLine(string id, SeasonCondition s, params string[] lines) =>
            new DialogueEntry { id = id, season = s, weight = 1.5f, lines = lines };

        // ───────────────────────── 그 밖 ─────────────────────────

        private static QuestData FishQuest()
        {
            string path = QuestFolder + "/Fish_Quest_Hungry.asset";
            QuestData q = AssetDatabase.LoadAssetAtPath<QuestData>(path);
            if (q != null) return q;
            q = ScriptableObject.CreateInstance<QuestData>();
            q.title = "배고픈 여행자";
            q.description = "우주선 냉장고가 텅 빈 뻐끔이에게 작물을 가져다주자.";
            q.goal = QuestGoal.DeliverItem;
            q.item = FirstCrop();
            q.amount = 3;
            q.rewardBells = 500;
            AssetDatabase.CreateAsset(q, path);
            return q;
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

        // 오브젝트 이름이 정확히 같은 것 먼저, 없으면 이름에 들어간 것
        private static NPCDialogue FindNpc(string objectName)
        {
            NPCDialogue partial = null;
            foreach (NPCDialogue n in Object.FindObjectsByType<NPCDialogue>(FindObjectsInactive.Include))
            {
                if (n.name == objectName) return n;
                if (partial == null && n.name.ToLowerInvariant().Contains(objectName.ToLowerInvariant())) partial = n;
            }
            return partial;
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
