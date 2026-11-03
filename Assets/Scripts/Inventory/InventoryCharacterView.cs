using UnityEngine;
using UnityEngine.EventSystems;
using FarmingSystem.Player;

namespace FarmingSystem.UI
{
    // 인벤토리 창의 캐릭터 칸 (실제 플레이어 3D 모델을 비추는 화면)
    // - 전용 카메라가 플레이어를 정면에서 찍어 RenderTexture로 보내고, 이 칸(RawImage)이 그걸 보여줌
    // - 칸을 마우스로 좌우로 끌면 캐릭터를 돌려 볼 수 있음
    // - 창이 열려 있을 때만 카메라를 켬 (닫혀 있으면 성능 영향 없음)
    // - 플레이어 크기(렌더러 범위)를 재서 몸 전체가 들어오게 거리를 자동으로 맞춤
    // 카메라의 Culling Mask는 플레이어 레이어만 → 배경 없이 캐릭터만 보임 (플레이어가 Default 레이어면 땅도 같이 찍힘)
    // - 전용 조명: 카메라 자리에서 캐릭터만 비추는 작은 스포트라이트를 창이 열려 있을 때만 켬
    //   → 밤이나 흐린 날에도 캐릭터 칸의 플레이어가 까맣게 보이지 않음 (조명은 없으면 자동으로 만듦)
    public class InventoryCharacterView : MonoBehaviour, IDragHandler
    {
        [Tooltip("캐릭터를 찍는 전용 카메라 (Target Texture에 RenderTexture가 들어 있어야 함)")]
        [SerializeField] private Camera portraitCamera;
        [Tooltip("찍을 대상. 비우면 씬의 플레이어(PlayerController)")]
        [SerializeField] private Transform target;

        [Header("구도")]
        [Tooltip("화면 세로에 캐릭터 키의 몇 배가 들어오게 할지 (클수록 작게 보임)")]
        [SerializeField, Range(1f, 2f)] private float fitMargin = 1.2f;
        [Tooltip("카메라 높이를 캐릭터 가운데보다 얼마나 위로 (캐릭터 키 대비, 0.1 = 살짝 내려다봄)")]
        [SerializeField, Range(-0.3f, 0.5f)] private float cameraHeight = 0.1f;
        [Tooltip("돌려 보기 속도")]
        [SerializeField] private float rotateSpeed = 0.4f;

        [Header("전용 조명 (시간·날씨와 상관없이 밝게)")]
        [SerializeField] private bool useOwnLight = true;
        [Tooltip("비워 두면 자동으로 카메라에 스포트라이트를 붙여서 씀")]
        [SerializeField] private Light portraitLight;
        [Tooltip("캐릭터가 받는 밝기 (거리와 상관없이 일정하게 맞춰 줌)")]
        [SerializeField, Range(0f, 3f)] private float lightBrightness = 1.2f;
        [SerializeField] private Color lightColor = new Color(1f, 0.96f, 0.9f, 1f);

        private float yaw;           // 끌어서 돌린 각도
        private Bounds bounds;

        private void OnEnable()
        {
            if (target == null)
            {
                PlayerController p = FindAnyObjectByType<PlayerController>();
                if (p != null) target = p.transform;
            }
            yaw = 0f;
            MeasureTarget();
            if (portraitCamera != null) portraitCamera.enabled = true;
            if (useOwnLight) EnsureLight();
            if (portraitLight != null) portraitLight.enabled = useOwnLight;
            UpdateCamera();
        }

        private void OnDisable()
        {
            if (portraitCamera != null) portraitCamera.enabled = false;
            if (portraitLight != null) portraitLight.enabled = false; // 창을 닫으면 조명도 끔 (게임 화면에 영향 없게)
        }

        // 카메라 자리에서 캐릭터 쪽을 비추는 스포트라이트 (없으면 만듦)
        private void EnsureLight()
        {
            if (portraitLight != null || portraitCamera == null) return;
            var go = new GameObject("PortraitLight");
            go.transform.SetParent(portraitCamera.transform, false);
            portraitLight = go.AddComponent<Light>();
            portraitLight.type = LightType.Spot;
            portraitLight.shadows = LightShadows.None;
            portraitLight.renderMode = LightRenderMode.ForcePixel;
        }

        private void LateUpdate() => UpdateCamera();

        public void OnDrag(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            yaw += e.delta.x * rotateSpeed;
        }

        // 플레이어의 보이는 범위 재기 (몸 전체가 화면에 들어오게)
        private void MeasureTarget()
        {
            if (target == null) return;
            bool found = false;
            foreach (Renderer r in target.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (!found) { bounds = r.bounds; found = true; }
                else bounds.Encapsulate(r.bounds);
            }
            if (!found) bounds = new Bounds(target.position + Vector3.up, new Vector3(1f, 2f, 1f));
        }

        private void UpdateCamera()
        {
            if (portraitCamera == null || target == null) return;

            // 캐릭터 기준 위치 (플레이어가 움직여도 같은 구도) - 몸 가운데
            Vector3 offsetFromRoot = bounds.center - target.position;
            Vector3 center = target.position + offsetFromRoot;
            float height = Mathf.Max(bounds.size.y, 0.5f);

            // 캐릭터 정면에서, 끌어서 돌린 만큼 회전
            Vector3 front = Quaternion.Euler(0f, yaw, 0f) * target.forward;
            float fov = portraitCamera.fieldOfView;
            float distance = (height * fitMargin * 0.5f) / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            Vector3 pos = center + front * distance + Vector3.up * (height * cameraHeight);
            portraitCamera.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(center - pos, Vector3.up));

            // 조명: 카메라 자리에서 캐릭터만 덮는 좁은 원뿔. 빛은 거리²만큼 약해지므로 거리에 맞춰 세기를 올림
            if (portraitLight != null && useOwnLight)
            {
                float d = Vector3.Distance(pos, center);
                portraitLight.transform.localPosition = Vector3.zero;
                portraitLight.transform.localRotation = Quaternion.identity;
                portraitLight.color = lightColor;
                portraitLight.range = d * 1.6f;
                portraitLight.spotAngle = Mathf.Clamp(Mathf.Atan2(height * 0.7f, d) * Mathf.Rad2Deg * 2f + 8f, 10f, 120f);
                portraitLight.intensity = lightBrightness * d * d;
            }
        }
    }
}