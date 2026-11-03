using UnityEngine;

namespace FarmingSystem.Weather
{
    // 화면 흔들림 (천둥·태풍 바람) - 날씨 연출이 메인 카메라에 자동으로 붙임
    // - 카메라 조작 스크립트(ThirdPersonCameraRig 등)가 위치를 정한 "뒤에" 살짝 흔들고,
    //   다음 프레임 시작 때 흔든 만큼 되돌려서 카메라 조작에는 영향을 주지 않음
    // - Shake(세기, 시간): 천둥처럼 한 번 쿵 / SetWobble(세기): 태풍 동안 계속 살짝 흔들림
    [DefaultExecutionOrder(10000)] // 다른 카메라 스크립트보다 늦게 실행
    public class WeatherCameraShake : MonoBehaviour
    {
        private float shakeAmount, shakeDuration, shakeTime;
        private float wobble;
        private Vector3 applied;

        public void Shake(float amount, float duration)
        {
            shakeAmount = Mathf.Max(shakeAmount * (1f - shakeTime / Mathf.Max(shakeDuration, 0.01f)), amount);
            shakeDuration = Mathf.Max(0.01f, duration);
            shakeTime = 0f;
        }

        public void SetWobble(float amount) => wobble = Mathf.Max(0f, amount);

        // 지난 프레임에 흔든 만큼 먼저 되돌림
        private void Update()
        {
            if (applied != Vector3.zero)
            {
                transform.position -= applied;
                applied = Vector3.zero;
            }
        }

        private void LateUpdate()
        {
            Vector3 offset = Vector3.zero;
            float t = Time.time;

            if (shakeTime < shakeDuration)
            {
                shakeTime += Time.deltaTime;
                float fade = 1f - Mathf.Clamp01(shakeTime / shakeDuration);
                float a = shakeAmount * fade * fade;
                offset += new Vector3(Mathf.PerlinNoise(t * 25f, 0f) - 0.5f, Mathf.PerlinNoise(0f, t * 25f) - 0.5f, 0f) * (2f * a);
            }
            if (wobble > 0f)
                offset += new Vector3(Mathf.PerlinNoise(t * 1.3f, 5f) - 0.5f, Mathf.PerlinNoise(7f, t * 1.1f) - 0.5f, 0f) * (2f * wobble);

            if (offset == Vector3.zero) return;
            applied = transform.rotation * offset; // 카메라 기준 좌우·위아래로
            transform.position += applied;
        }

        private void OnDisable()
        {
            if (applied != Vector3.zero) { transform.position -= applied; applied = Vector3.zero; }
        }
    }
}