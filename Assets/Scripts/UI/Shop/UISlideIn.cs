using UnityEngine;

namespace FarmingSystem.UI
{
    // 창이 켜질 때(SetActive true) 옆에서 미끄러져 들어오며 서서히 나타나게 하는 연출
    // 구매·판매 창의 품목 패널 등에 붙이면 됨 (게임 시간이 멈춰 있어도 실제 시간으로 움직임)
    // 붙인 오브젝트의 원래 위치가 도착 위치 → 씬에서 정한 자리 그대로 들어옴
    [RequireComponent(typeof(RectTransform))]
    public class UISlideIn : MonoBehaviour
    {
        [Tooltip("시작 위치 (도착 위치 기준 이만큼 떨어진 곳에서 출발). 오른쪽에서 들어오면 (300, 0)")]
        [SerializeField] private Vector2 fromOffset = new Vector2(300f, 0f);
        [Tooltip("들어오는 시간 (초)")]
        [SerializeField] private float duration = 0.35f;
        [Tooltip("시작 전 기다리는 시간 (초) - 카메라가 먼저 움직이고 창이 따라 들어오게")]
        [SerializeField] private float delay = 0.1f;

        private RectTransform rect;
        private CanvasGroup group;
        private Vector2 restPosition;
        private bool hasRest;
        private float elapsed;
        private bool playing;

        private void Awake()
        {
            rect = (RectTransform)transform;
            group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        }

        private void OnEnable()
        {
            if (rect == null) Awake();
            if (!hasRest) { restPosition = rect.anchoredPosition; hasRest = true; }
            elapsed = -delay;
            playing = true;
            Apply(0f);
        }

        private void OnDisable()
        {
            // 다음에 켤 때를 위해 제자리로 (씬 편집 중 위치가 틀어지지 않게)
            if (hasRest && rect != null) rect.anchoredPosition = restPosition;
            if (group != null) group.alpha = 1f;
            playing = false;
        }

        private void Update()
        {
            if (!playing) return;
            elapsed += Time.unscaledDeltaTime;
            float t = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
            Apply(t);
            if (t >= 1f) playing = false;
        }

        private void Apply(float t)
        {
            // 처음엔 빠르고 끝에서 살짝 느려지는 움직임
            float e = 1f - Mathf.Pow(1f - t, 3f);
            rect.anchoredPosition = restPosition + fromOffset * (1f - e);
            group.alpha = e;
            group.interactable = t >= 1f;
            group.blocksRaycasts = t >= 1f;
        }
    }
}