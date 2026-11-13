using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FarmingSystem.Effects
{
    // 게임 화면 보정 (PV의 PVPostFX를 게임용으로 옮긴 것)
    // "Tools → 행성농사일지 → 화면 보정 만들기 (게임용)" 메뉴가 만든 Game_PostProcess 오브젝트에 붙어 있음
    //
    // PV와 다른 점
    //  - 단축키 없음 (V는 게임에서 이미 쓰는 키라서) → 전부 Inspector에서 조절
    //  - 초점(피사계 심도)은 기본 끔. 켜면 "캐릭터에 초점" → 캐릭터는 선명하고 먼 배경만 살짝 흐림
    //    (화면 가운데 초점은 십자선이 하늘을 볼 때마다 초점이 튀어서 게임에선 비추천)
    //  - 색감·빛 번짐·가장자리 어둡게는 PV와 같은 기본값
    [RequireComponent(typeof(Volume))]
    public class GamePostFX : MonoBehaviour
    {
        public enum FocusMode
        {
            [InspectorName("끔")] Off,
            [InspectorName("캐릭터에 초점 (추천)")] Player,
            [InspectorName("화면 가운데에 초점")] Center,
        }

        [Tooltip("보정 전체 켜기/끄기")]
        [SerializeField] private bool fxOn = true;

        [Header("초점")]
        [SerializeField] private FocusMode focusMode = FocusMode.Off;
        [Tooltip("흐림 정도 (조리개 값, 작을수록 많이 흐림. 게임은 8~12 무난)")]
        [SerializeField, Range(1f, 16f)] private float aperture = 10f;
        [SerializeField] private float focusSpeed = 4f;
        [SerializeField] private float fallbackDistance = 20f;

        [Header("색감")]
        [SerializeField, Range(-2f, 2f)] private float exposure = 0f;
        [SerializeField, Range(-50f, 50f)] private float contrast = 8f;
        [SerializeField, Range(-50f, 50f)] private float saturation = 12f;
        [SerializeField] private Color colorFilter = Color.white;

        [Header("빛 번짐 (Bloom)")]
        [SerializeField, Range(0f, 3f)] private float bloomIntensity = 0.6f;
        [SerializeField, Range(0f, 2f)] private float bloomThreshold = 0.9f;

        [Header("화면 가장자리 어둡게 (Vignette)")]
        [SerializeField, Range(0f, 0.6f)] private float vignette = 0.22f;

        private Volume volume;
        private DepthOfField dof;
        private ColorAdjustments color;
        private Bloom bloom;
        private Vignette vig;
        private Transform player;
        private float focusDistance = 10f;

        private void Awake()
        {
            volume = GetComponent<Volume>();
            VolumeProfile p = volume.profile; // 플레이 중엔 복사본 (원본 에셋은 안 바뀜)
            p.TryGet(out dof);
            p.TryGet(out color);
            p.TryGet(out bloom);
            p.TryGet(out vig);
        }

        private void Update()
        {
            volume.weight = fxOn ? 1f : 0f;
            ApplyLook();
            UpdateFocus(Time.unscaledDeltaTime);
        }

        private void ApplyLook()
        {
            if (color != null)
            {
                color.active = true;
                color.postExposure.Override(exposure);
                color.contrast.Override(contrast);
                color.saturation.Override(saturation);
                color.colorFilter.Override(colorFilter);
            }
            if (bloom != null)
            {
                bloom.active = bloomIntensity > 0f;
                bloom.intensity.Override(bloomIntensity);
                bloom.threshold.Override(bloomThreshold);
            }
            if (vig != null)
            {
                vig.active = vignette > 0f;
                vig.intensity.Override(vignette);
            }
        }

        private void UpdateFocus(float dt)
        {
            if (dof == null) return;
            if (focusMode == FocusMode.Off) { dof.active = false; return; }
            dof.active = true;
            dof.mode.Override(DepthOfFieldMode.Bokeh);

            Camera cam = Camera.main;
            if (cam != null)
            {
                float target = fallbackDistance;
                if (focusMode == FocusMode.Player)
                {
                    if (player == null)
                    {
                        var pc = FindAnyObjectByType<FarmingSystem.Player.PlayerController>();
                        if (pc != null) player = pc.transform;
                    }
                    if (player != null) target = Vector3.Distance(cam.transform.position, player.position + Vector3.up);
                }
                else
                {
                    Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                    if (Physics.Raycast(ray, out RaycastHit hit, 500f, ~0, QueryTriggerInteraction.Ignore)) target = hit.distance;
                }
                focusDistance = Mathf.Lerp(focusDistance, target, 1f - Mathf.Exp(-focusSpeed * dt));
            }
            dof.focusDistance.Override(Mathf.Max(0.1f, focusDistance));
            dof.aperture.Override(aperture);
            dof.focalLength.Override(50f);
        }
    }
}