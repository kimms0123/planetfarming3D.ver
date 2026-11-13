using System;
using System.Collections.Generic;

namespace FarmingSystem.Save
{
    // 저장 파일 한 칸의 내용 (JSON으로 저장됨)
    // 새 항목을 추가할 땐 맨 아래에 필드를 추가하면 됨 → 예전 저장 파일도 그대로 열림 (없는 값은 기본값)
    // 아이템·작물·건물은 에셋 이름(ScriptableObject 파일 이름)으로 저장 → 에셋 이름을 바꾸면 예전 저장에서 그 아이템이 사라지니 주의
    [Serializable]
    public class SaveData
    {
        public int version = 1;

        // 칸 고르는 화면에 보여 줄 요약
        public string farmerName = "";
        public string savedAt = "";        // "2026-11-13 14:22"
        public float playSeconds;

        // 시간
        public int day = 1;
        public int hour = 6;
        public int minute;

        // 돈
        public int bells;

        // 플레이어
        public float[] playerPos;          // x, y, z
        public float playerYaw;

        // 가방·도구칸
        public List<SlotSave> bag = new List<SlotSave>();
        public List<SlotSave> tools = new List<SlotSave>();
        public int selectedHotbar;
        public int selectedTool;

        // 밭 (갈린 칸·작물 있는 칸만)
        public List<TileSave> tiles = new List<TileSave>();

        // 건물
        public List<BuildingSave> buildings = new List<BuildingSave>();

        // 날씨 (오늘 날씨를 직접 바꿨을 때만 의미 있음, -1 = 자동)
        public int todayWeather = -1;

        // 상점 재고 (재고가 정해진 물건만)
        public List<StockSave> shopStock = new List<StockSave>();

        // 흥정 누적 (FCM/RBFN 특징값 "누적 성공률"에 쓰임)
        public int bargainAttempts;
        public int bargainSuccesses;

        // 주민: 한 집에 한 명, 한 번 정해지면 바뀌지 않음 (모동숲처럼) → 누가 어느 집에 사는지 저장
        public List<ResidentSave> residents = new List<ResidentSave>();
        // 한 번 온 손님 (계속 섬에 있음)
        public List<string> visitorsArrived = new List<string>();

        // 퀘스트 진행 · NPC와 나눈 대화 기억 (처음 만남·오늘 말 걸었는지·한 번만 하는 말)
        public List<QuestSave> quests = new List<QuestSave>();
        public List<TalkSave> talks = new List<TalkSave>();
    }

    [Serializable]
    public class QuestSave
    {
        public string id;       // 퀘스트 에셋 이름
        public int state;       // 0 안 받음, 1 진행 중, 2 완료
        public int progress;    // 돈 벌기: 받은 뒤 번 벨
    }

    [Serializable]
    public class TalkSave
    {
        public string npc;
        public int lastDay;
        public List<string> seen = new List<string>();
    }

    [Serializable]
    public class ResidentSave
    {
        public string npc;          // NPC 오브젝트 이름
        public int houseX, houseY;  // 집의 첫 칸
    }

    [Serializable]
    public class SlotSave
    {
        public int index;
        public string item;     // 에셋 이름
        public int quantity;
        public int quality;     // ItemQuality
    }

    [Serializable]
    public class TileSave
    {
        public int x, y;        // FarmTile.GridPosition
        public int state;       // TileState
        public bool watered;
        public string crop;     // CropData 에셋 이름 (없으면 빈칸)
        public int growthDay;
        public bool withered;
        public bool storm;
        public int daysSinceReady;
    }

    [Serializable]
    public class BuildingSave
    {
        public string data;     // BuildingData 에셋 이름
        public List<int> cells = new List<int>(); // x, y, x, y ...
        public float[] center;
        public int rotation;
        public bool complete;
        public int completeDay;
    }

    [Serializable]
    public class StockSave
    {
        public string item;
        public int stock;
    }

    /// <summary>칸 고르는 화면용 요약 (파일 전체를 읽지 않고 이것만 보여 줌)</summary>
    public class SlotSummary
    {
        public int slot;
        public bool exists;
        public string farmerName;
        public int day;
        public int bells;
        public float playSeconds;
        public string savedAt;
    }
}