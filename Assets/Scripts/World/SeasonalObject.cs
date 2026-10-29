using UnityEngine;
using FarmingSystem.Core;

namespace FarmingSystem.World
{
    // 계절마다 모델이 통째로 바뀌는 오브젝트 (나무 등)
    // 봄·여름·가을·겨울 모델을 모두 자식으로 갖고 있다가, 지금 계절 것만 켜고 나머지는 끈다
    // (블록처럼 색만 바뀌는 게 아니라 겨울 나무는 잎이 없는 등 모양 자체가 달라서 모델을 바꿔 끼움)
    // - GameClock의 계절 변경 이벤트를 받아 자동으로 바뀜. 게임 시작 시에도 지금 계절로 맞춤
    // - 비어 있는 계절은 봄 모델로 대신
    // IslandMapGenerator가 나무를 만들 때 자동으로 붙이고 설정한다
    public class SeasonalObject : MonoBehaviour
    {
        [SerializeField] private GameObject spring;
        [SerializeField] private GameObject summer;
        [SerializeField] private GameObject autumn;
        [SerializeField] private GameObject winter;

        private bool subscribed;

        public void Configure(GameObject springModel, GameObject summerModel, GameObject autumnModel, GameObject winterModel)
        {
            spring = springModel;
            summer = summerModel;
            autumn = autumnModel;
            winter = winterModel;
            Apply(Season.Spring); // 에디터에서는 봄 모습으로
        }

        private void OnEnable() => TrySubscribe();

        private void Start()
        {
            TrySubscribe();
            Apply(GameClock.Instance != null ? GameClock.Instance.CurrentSeason : Season.Spring);
        }

        private void OnDisable()
        {
            if (subscribed && GameClock.Instance != null) GameClock.Instance.OnSeasonChanged -= Apply;
            subscribed = false;
        }

        private void TrySubscribe()
        {
            if (subscribed || GameClock.Instance == null) return;
            GameClock.Instance.OnSeasonChanged += Apply;
            subscribed = true;
        }

        // 그 계절 모델만 켜기
        public void Apply(Season season)
        {
            GameObject target = season == Season.Summer ? summer
                              : season == Season.Autumn ? autumn
                              : season == Season.Winter ? winter
                              : spring;
            if (target == null) target = spring;

            SetActive(spring, spring == target);
            SetActive(summer, summer == target);
            SetActive(autumn, autumn == target);
            SetActive(winter, winter == target);
        }

        private static void SetActive(GameObject g, bool on)
        {
            if (g != null && g.activeSelf != on) g.SetActive(on);
        }
    }
}