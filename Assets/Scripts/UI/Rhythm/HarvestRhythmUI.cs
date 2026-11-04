using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace FarmingSystem.Rhythm
{
    // 씬에 놓인 수확 리듬게임 UI의 연결표
    // - "리듬게임 UI 씬에 만들기" 메뉴가 HarvestRhythmCanvas에 붙이고 칸을 전부 채워 줌
    // - HarvestRhythmGame이 플레이할 때 UI를 새로 만들지 않고 이 연결표에 적힌 UI를 그대로 씀
    //   → 씬에서 위치·크기·색·글자 크기·이미지를 바꾸면 게임에 그대로 나옴
    // 주의: UI 오브젝트를 지우면 연결이 끊김 → 안 쓰는 건 지우지 말고 체크를 꺼서 숨기기
    public class HarvestRhythmUI : MonoBehaviour
    {
        [Header("전체")]
        public Canvas canvas;
        public GameObject hudRoot;
        public GameObject resultPanel;

        [Header("판정 원 (작물 위를 따라다님)")]
        public RectTransform judgeRoot;
        public Image judgeRing;
        public Image judgeGlow;
        public Image arrowImage;
        public Image arrowShadow;
        public Image nextArrow;
        [Tooltip("줄어드는 접근 원 (여러 개를 돌려씀)")]
        public Image[] approachRings = new Image[4];
        public TextMeshProUGUI popupText;
        public TextMeshProUGUI countdownText;
        [Tooltip("작물 강조 모서리 (없으면 비워 둬도 됨)")]
        public Image[] focusCorners = new Image[4];

        [Header("위쪽")]
        public Image goalIcon;
        public TextMeshProUGUI goalText;
        public TextMeshProUGUI comboText;
        public TextMeshProUGUI remainText;
        public Image progressFill;
        [Tooltip("점수 게이지 별 (기본 / 상급 / 최상급). 위치는 등급 기준값에 맞춰 코드가 옮김")]
        public Image[] progressStars = new Image[3];

        [Header("아래쪽")]
        [Tooltip("방향키 순서: 위 / 아래 / 왼쪽 / 오른쪽")]
        public Image[] keyBacks = new Image[4];
        public Image[] keyArrows = new Image[4];
        public Image[] keyShadows = new Image[4];
        public TextMeshProUGUI guideText;

        [Header("결과창")]
        public TextMeshProUGUI resultTitle;
        public TextMeshProUGUI resultBody;
        public Image[] resultStars = new Image[3];

        [Header("기본 화살표 그림 (디자이너 화살표가 없을 때 돌려서 씀)")]
        public Sprite arrowSprite;
    }
}