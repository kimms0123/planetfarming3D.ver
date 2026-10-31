using UnityEngine;

namespace FarmingSystem.World
{
    // 곡면 월드 조절기 (씬에 하나)
    // 동숲·포코피아처럼 멀리 있는 땅이 아래로 둥글게 휘어 보이게 하는 값을 모든 곡면 셰이더에 한꺼번에 알려줌
    // - 기준점(보통 카메라)에서 멀어질수록 거리² 만큼 아래로 내려감 → 지평선이 둥글게 휨
    // - Side Amount 0 = 앞뒤로만 휨 (동숲의 굴러가는 통나무 느낌) / 1 = 사방으로 휨 (작은 행성 느낌)
    // - 눈에 보이는 모습만 휨. 충돌·밭 판정·조준은 원래 평평한 위치 그대로 (플레이어 근처는 거의 안 휘어서 차이 없음)
    // - 에디터에서도 바로 보이게 실행 안 해도 동작 (Preview In Edit Mode)
    [ExecuteAlways]
    public class CurvedWorldController : MonoBehaviour
    {
        [Tooltip("곡면 효과 켜기/끄기")]
        [SerializeField] private bool curveEnabled = true;
        [Tooltip("휘는 세기. 0.001 = 살짝, 0.003 = 동숲 정도, 0.006 = 아주 둥근 행성")]
        [SerializeField, Range(0f, 0.02f)] private float strength = 0.002f;
        [Tooltip("옆으로도 휘는 정도 (1 = 사방으로 똑같이 둥근 행성 - 카메라를 돌려도 산 모양이 안 변함 / 0 = 보는 방향 앞뒤로만 휨 - 카메라가 고정된 게임용)\n" +
                 "우리 게임은 카메라를 마우스로 돌리므로 1 권장")]
        [SerializeField, Range(0f, 1f)] private float sideAmount = 1f;
        [Tooltip("기준점에서 이 거리(m)까지는 전혀 안 휨. 가까운 산·건물은 원근감 그대로 서 있고, 그 너머 먼 지평선부터 휨")]
        [SerializeField, Range(0f, 100f)] private float flatRadius = 25f;
        [Tooltip("휘는 기준점. 비우면 플레이어 (플레이어가 없으면 메인 카메라)\n플레이어 기준이면 카메라를 돌려도 세상이 휘는 모양이 그대로")]
        [SerializeField] private Transform origin;
        [Tooltip("기준점을 보는 방향 앞쪽으로 옮기는 거리 (m). 0 권장 - 0이 아니면 카메라를 돌릴 때 먼 산이 오르내림")]
        [SerializeField] private float originForwardOffset = 0f;
        [Tooltip("실행하지 않아도 Scene/Game 화면에서 휜 모습 미리보기")]
        [SerializeField] private bool previewInEditMode = true;

        private FarmingSystem.Player.PlayerController cachedPlayer;

        private static readonly int CurveOriginId = Shader.PropertyToID("_CurveOrigin");
        private static readonly int CurveForwardId = Shader.PropertyToID("_CurveForward");
        private static readonly int CurveStrengthId = Shader.PropertyToID("_CurveStrength");
        private static readonly int CurveSideId = Shader.PropertyToID("_CurveSide");
        private static readonly int CurveFlatId = Shader.PropertyToID("_CurveFlat");

        public float Strength
        {
            get => strength;
            set => strength = Mathf.Max(0f, value);
        }

        public bool CurveEnabled
        {
            get => curveEnabled;
            set => curveEnabled = value;
        }

        private void OnEnable() => Push();
        private void LateUpdate() => Push();
        private void OnValidate() => Push();

        private void OnDisable()
        {
            Shader.SetGlobalFloat(CurveStrengthId, 0f); // 끄면 평평하게
        }

        private void Push()
        {
            bool active = curveEnabled && (Application.isPlaying || previewInEditMode);
            Transform t = origin;
            if (t == null)
            {
                if (cachedPlayer == null) cachedPlayer = FindAnyObjectByType<FarmingSystem.Player.PlayerController>();
                if (cachedPlayer != null) t = cachedPlayer.transform;
            }
            if (t == null && Camera.main != null) t = Camera.main.transform;
            Transform view = Camera.main != null ? Camera.main.transform : t; // 앞 방향은 카메라 기준
            if (t == null || !active)
            {
                Shader.SetGlobalFloat(CurveStrengthId, 0f);
                return;
            }

            Vector3 forward = view != null ? view.forward : t.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            Vector3 o = t.position + forward * originForwardOffset;
            Shader.SetGlobalVector(CurveOriginId, new Vector4(o.x, o.y, o.z, 0f));
            Shader.SetGlobalVector(CurveForwardId, new Vector4(forward.x, 0f, forward.z, 0f));
            Shader.SetGlobalFloat(CurveStrengthId, strength);
            Shader.SetGlobalFloat(CurveSideId, sideAmount);
            Shader.SetGlobalFloat(CurveFlatId, flatRadius);
        }
    }
}