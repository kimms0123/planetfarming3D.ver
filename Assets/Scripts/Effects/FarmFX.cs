using System.Collections;
using UnityEngine;

namespace FarmingSystem.Effects
{
    // 농사 반짝이 연출 (씬에 따로 놓을 필요 없음 - 처음 쓸 때 자동으로 만들어짐)
    // - 작물이 한 단계 자랄 때: 새 모습이 톡 튀어나오며(작게 → 살짝 크게 → 원래 크기) 반짝이가 퍼짐
    // - 수확 리듬게임 PERFECT: 작물 위에 작은 반짝이
    // - 리듬게임 마지막 뽑히는 순간: 등급에 따라 반짝이 (기본 흰색 / 상급 금색 / 최상급 무지개, 많이)
    // 게임 시간이 멈춰 있어도(리듬게임 중) 실제 시간으로 재생됨
    // 끄고 싶으면 FarmFX.Enabled = false
    public class FarmFX : MonoBehaviour
    {
        public static bool Enabled = true;

        private static FarmFX instance;
        private ParticleSystem sparkles;
        private Material material;
        private Texture2D texture;

        private static FarmFX Get()
        {
            if (!Enabled) return null;
            if (instance == null)
            {
                var go = new GameObject("FarmFX");
                instance = go.AddComponent<FarmFX>();
                instance.Build();
            }
            return instance.sparkles != null ? instance : null;
        }

        // ---------- 쓰는 곳에서 부르는 함수 ----------

        /// <summary>작물이 자랐을 때: 새 모델 톡 튀어나옴 + 반짝이</summary>
        public static void CropGrew(GameObject stageObject, bool fullyGrown)
        {
            FarmFX fx = Get();
            if (fx == null || stageObject == null) return;
            fx.StartCoroutine(fx.PopScale(stageObject.transform, fullyGrown ? 1.18f : 1.1f));
            Vector3 c = Center(stageObject);
            fx.Burst(c, fullyGrown ? 26 : 14, fullyGrown ? 0.35f : 0.28f, new Color(1f, 0.95f, 0.6f), false, fullyGrown ? 2.2f : 1.6f);
        }

        /// <summary>PERFECT 판정: 작물 위에 작은 반짝이</summary>
        public static void Perfect(Vector3 pos)
        {
            FarmFX fx = Get();
            if (fx == null) return;
            fx.Burst(pos, 8, 0.22f, new Color(1f, 0.9f, 0.45f), false, 1.4f);
        }

        /// <summary>뽑히는 순간 등급 반짝이 (tier: 0 쓰레기, 1 기본, 2 상급, 3 최상급). delay초 뒤에 터짐</summary>
        public static void HarvestBurst(Vector3 pos, int tier, float delay)
        {
            FarmFX fx = Get();
            if (fx == null || tier <= 0) return;
            fx.StartCoroutine(fx.Delayed(delay, () =>
            {
                if (tier >= 3) fx.Burst(pos, 60, 0.42f, Color.white, true, 3.2f);
                else if (tier == 2) fx.Burst(pos, 36, 0.36f, new Color(1f, 0.8f, 0.25f), false, 2.6f);
                else fx.Burst(pos, 18, 0.3f, Color.white, false, 2f);
            }));
        }

        // ---------- 내부 ----------

        private IEnumerator Delayed(float t, System.Action a)
        {
            if (t > 0f) yield return new WaitForSecondsRealtime(t);
            a();
        }

        // 톡 튀어나오기: 0.35 → 목표 크기(살짝 크게) → 1
        private IEnumerator PopScale(Transform t, float overshoot)
        {
            const float up = 0.18f, settle = 0.16f;
            Vector3 baseScale = t.localScale;
            float time = 0f;
            while (time < up + settle)
            {
                if (t == null) yield break;
                time += Time.unscaledDeltaTime;
                float s;
                if (time < up)
                {
                    float k = time / up;
                    s = Mathf.Lerp(0.35f, overshoot, 1f - (1f - k) * (1f - k));
                }
                else
                {
                    float k = Mathf.Clamp01((time - up) / settle);
                    s = Mathf.Lerp(overshoot, 1f, k * k * (3f - 2f * k));
                }
                t.localScale = baseScale * s;
                yield return null;
            }
            if (t != null) t.localScale = baseScale;
        }

        private void Burst(Vector3 pos, int count, float size, Color color, bool rainbow, float speed)
        {
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                Vector3 dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) * 0.8f + 0.25f; // 주로 위·옆으로
                ep.position = pos + dir * Random.Range(0.05f, 0.25f);
                ep.velocity = dir.normalized * speed * Random.Range(0.4f, 1f);
                ep.startSize = size * Random.Range(0.6f, 1.3f);
                ep.startLifetime = Random.Range(0.55f, 1.0f);
                ep.rotation = Random.Range(0f, 360f);
                Color c = rainbow ? Color.HSVToRGB(Random.value, 0.55f, 1f) : color;
                ep.startColor = c;
                sparkles.Emit(ep, 1);
            }
        }

        private static Vector3 Center(GameObject go)
        {
            bool found = false;
            Bounds b = new Bounds(go.transform.position, Vector3.zero);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                if (!found) { b = r.bounds; found = true; } else b.Encapsulate(r.bounds);
            }
            return found ? b.center + Vector3.up * b.extents.y * 0.3f : go.transform.position + Vector3.up * 0.4f;
        }

        private void Build()
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) { Debug.LogWarning("[FarmFX] URP 파티클 셰이더를 못 찾아서 반짝이를 끔"); return; }

            // 반짝이 그림: 가운데 밝은 점 + 십자 빛줄기
            const int N = 64;
            texture = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "FarmFX_Sparkle" };
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float core = Mathf.Clamp01(1f - r / 0.35f); core *= core;
                    float cross = Mathf.Max(Mathf.Clamp01(1f - Mathf.Abs(u) / 0.06f) * Mathf.Clamp01(1f - Mathf.Abs(v)),
                                            Mathf.Clamp01(1f - Mathf.Abs(v) / 0.06f) * Mathf.Clamp01(1f - Mathf.Abs(u)));
                    float a = Mathf.Clamp01(core + cross * cross * 0.9f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            texture.Apply();

            material = new Material(sh) { name = "FarmFX_Sparkle" };
            material.SetTexture("_BaseMap", texture);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 2f); // Additive (빛나게)
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_BLENDMODE_ADD");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = 3100;

            sparkles = gameObject.AddComponent<ParticleSystem>();
            sparkles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = sparkles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.useUnscaledTime = true; // 리듬게임 중(게임 시간 멈춤)에도 움직임
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;
            main.startSpeed = 0f;
            main.gravityModifier = 0.35f;
            var emission = sparkles.emission; emission.enabled = false;
            var shape = sparkles.shape; shape.enabled = false;

            var limit = sparkles.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 100f;
            limit.dampen = 0f;
            limit.drag = 2.5f;
            limit.multiplyDragByParticleSize = false;
            limit.multiplyDragByParticleVelocity = false;

            var size = sparkles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.2f, 1f), new Keyframe(1f, 0f)));

            var rot = sparkles.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-3f, 3f);

            var rend = GetComponent<ParticleSystemRenderer>();
            rend.renderMode = ParticleSystemRenderMode.Billboard;
            rend.sharedMaterial = material;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;

            sparkles.Play();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            if (material != null) Destroy(material);
            if (texture != null) Destroy(texture);
        }
    }
}