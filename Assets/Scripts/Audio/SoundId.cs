namespace FarmingSystem.Audio
{
    // 효과음 종류. 파일 이름 = "SFX_" + 이름 (여러 개면 뒤에 _1, _2 …) → 설정 메뉴가 자동으로 연결
    // ※ 숫자는 저장된 연결 정보라서 바꾸지 말 것. 새 소리는 맨 뒤에 새 번호로 추가
    public enum SoundId
    {
        // UI
        UIClick = 0,
        UIOpen = 1,
        UIClose = 2,
        PhoneOpen = 3,
        PhoneClose = 4,
        InventoryOpen = 5,
        InventoryClose = 6,
        ToolSwitch = 7,
        ItemPickup = 8,
        Coin = 9,             // 사기
        Sell = 10,            // 팔기
        BargainAccept = 11,
        BargainReject = 12,
        Notice = 13,          // 섬 알림·힌트
        QuestAccept = 14,
        QuestComplete = 15,
        CollectionNew = 16,   // 도감 새로 등록 (작물)
        DialoguePop = 17,     // 대화 시작

        // 농사
        Till = 20,            // 밭 갈기 (땅 파기)
        Water = 21,
        Plant = 22,
        Fertilize = 23,
        ClearCrop = 24,       // 잡초 뽑기·시든 작물 치우기
        ToolBreak = 25,

        // 발걸음
        FootstepGrass = 30,   // 봄·여름·가을
        FootstepSnow = 31,    // 겨울

        // 낚시
        FishCast = 40,        // 던지기 (휙)
        BobberPlop = 41,      // 찌가 물에 떨어짐 (퐁)
        FishNibble = 42,      // 툭툭
        FishBite = 43,        // 쑥! (물었다)
        FishSplash = 44,      // 잡아 올림 (첨벙)
        FishEscape = 45,      // 놓침

        // 결과 징글 (따단)
        RhythmPerfect = 50,   // 리듬게임 ALL PERFECT·최상급
        RhythmGood = 51,      // 상급
        RhythmNormal = 52,    // 기본
        RhythmBad = 53,       // 쓰레기
        FishResult = 54,      // 물고기 잡음 (흔함·보통)
        FishResultRare = 55,  // 물고기 잡음 (희귀·전설)

        // 세상
        ChestOpen = 60,
        ChestClose = 61,
        PortalOpen = 62,
        BuildPlace = 63,
        BuildComplete = 64,
        ShipEngine = 65,      // 우주선 날아옴·떠남
        ShipThud = 66,        // 우주선 착륙 쿵
        SleepJingle = 67,
        MorningChime = 68,
        TyphoonAlert = 69,
        Thunder = 70,         // 천둥 (날씨 연출이 직접 씀)
    }
}
