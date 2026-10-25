using UnityEngine;

namespace FarmingSystem.Rhythm
{
    // 수확 리듬게임 판정에 맞춰 실제 3D 작물을 움직이는 연출 (기획서 10~13장)
    // - 성공할 때마다 왼쪽 → 오른쪽 → 왼쪽… 번갈아 흔들림 (Perfect 8도 / Good 4도 / Miss 1도, 작물별 배율 적용)
    // - 3번째 성공부터 작물이 조금씩 땅에서 올라옴, 4번째 성공에 흙이 갈라짐(흙 파티클)
    // - 마지막: 좌우로 크게 흔들림 → 위로 튕기듯 뽑힘 + 흙 파티클 + 반짝임 → 작게 줄어들며 사라짐
    // - 흔들림 방향은 카메라 기준 좌우 (화면에서 보이는 그대로 좌우로 기울어짐)
    // - 리듬게임 중엔 게임 시간이 멈춰 있어서 모든 움직임은 실제 시간(unscaled)으로 계산
    // - 애니메이션 클립 없이 코드로 회전·이동하므로 작물 모델이 늘어나도 따로 작업할 필요 없음
    // 리듬게임이 수확 시작할 때 작물 오브젝트에 자동으로 붙이고, 끝나면 떼어냄 (직접 붙일 필요 없음)
    public class CropReactionController : MonoBehaviour
    {
        // 판정별 흔들림 각도 (기획서 11장)
        public float perfectAngle = 8f;
        public float goodAngle = 4f;
        public float missAngle = 1f;
        public float shakeScale = 1f;          // 작물별 배율 (CropData.harvestShakeScale)
        public float shakeFrequency = 14f;     // 흔들림 진동 속도
        public float shakeDecay = 7f;          // 흔들림이 잦아드는 속도

        private Vector3 basePosition;
        private Quaternion baseRotation;
        private Vector3 baseScale;
        private Vector3 tiltAxis;              // 이 축을 중심으로 기울이면 화면에서 좌우로 보임
        private float height = 1f;             // 작물 모델 높이 (이동량·파티클 크기 기준)

        private float shakeAmp, shakeTime;     // 현재 흔들림 (감쇠 진동)
        private float lift, liftTarget;        // 땅에서 올라온 정도
        private int successCount;
        private int nextSide = -1;             // -1 = 왼쪽부터

        private enum PullState { None, Wobble, Pop, Vanish, Done }
        private PullState pull = PullState.None;
        private float pullTimer;
        private const float WobbleTime = 0.35f, PopTime = 0.3f, VanishTime = 0.25f;

        public bool IsPullFinished => pull == PullState.Done;
        public float PullDuration => WobbleTime + PopTime + VanishTime;

        public static CropReactionController Attach(GameObject crop, float shakeScale)
        {
            if (crop == null) return null;
            var c = crop.GetComponent<CropReactionController>();
            if (c == null) c = crop.AddComponent<CropReactionController>();
            c.shakeScale = shakeScale;
            c.Init();
            return c;
        }

        private void Init()
        {
            basePosition = transform.localPosition;
            baseRotation = transform.localRotation;
            baseScale = transform.localScale;

            Camera cam = Camera.main;
            Vector3 fwd = cam != null ? cam.transform.forward : Vector3.forward;
            fwd.y = 0f;
            tiltAxis = fwd.sqrMagnitude > 0.0001f ? fwd.normalized : Vector3.forward;

            Bounds b = GetBounds();
            height = Mathf.Max(0.2f, b.size.y);
        }

        // 판정 결과 반응. 0 = Perfect, 1 = Good, 2 = Miss
        public void React(int judgement)
        {
            if (pull != PullState.None) return;

            float angle = judgement == 0 ? perfectAngle : judgement == 1 ? goodAngle : missAngle;
            if (judgement < 2)
            {
                successCount++;
                Kick(nextSide * angle * shakeScale);
                nextSide = -nextSide;

                // 누적 연출 (기획서 12장): 3번째 성공부터 조금씩 올라오고, 4번째에 흙이 갈라짐
                if (successCount >= 3) liftTarget = Mathf.Min(0.12f, 0.03f * (successCount - 2)) * height;
                if (successCount == 4) SpawnDirt(10, 0.6f);
            }
            else
            {
                // Miss: 살짝 덜컹 (방향 유지)
                Kick(nextSide * angle * shakeScale);
            }
        }

        // 모든 노트가 끝났을 때 뽑히는 연출 시작
        public void Pull()
        {
            if (pull != PullState.None) return;
            pull = PullState.Wobble;
            pullTimer = 0f;
        }

        // 포기했을 때 원래 모습으로 되돌림
        public void Restore()
        {
            transform.localPosition = basePosition;
            transform.localRotation = baseRotation;
            transform.localScale = baseScale;
            Destroy(this);
        }

        private void Kick(float angle)
        {
            shakeAmp = angle;
            shakeTime = 0f;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            shakeTime += dt;
            lift = Mathf.Lerp(lift, liftTarget, 1f - Mathf.Exp(-12f * dt));

            // 감쇠 진동: 처음 kick 방향으로 기울었다가 반대로 조금 넘어가며 잦아듦
            float angle = shakeAmp * Mathf.Exp(-shakeDecay * shakeTime) * Mathf.Cos(shakeFrequency * shakeTime);
            float extraLift = 0f;
            float scale = 1f;

            if (pull != PullState.None && pull != PullState.Done)
            {
                pullTimer += dt;
                switch (pull)
                {
                    case PullState.Wobble:
                        // 좌우 크게 흔들림
                        angle = Mathf.Sin(pullTimer * 40f) * 14f * shakeScale;
                        if (pullTimer >= WobbleTime)
                        {
                            pull = PullState.Pop;
                            pullTimer = 0f;
                            SpawnDirt(22, 1f);
                        }
                        break;

                    case PullState.Pop:
                        {
                            // 위로 튕기듯 뽑힘 (빠르게 올라갔다가 살짝 멈칫)
                            float t = Mathf.Clamp01(pullTimer / PopTime);
                            float ease = 1f - (1f - t) * (1f - t);
                            extraLift = ease * height * 0.8f;
                            angle = Mathf.Sin(t * Mathf.PI) * 6f * shakeScale;
                            if (pullTimer >= PopTime)
                            {
                                pull = PullState.Vanish;
                                pullTimer = 0f;
                                SpawnSparkle();
                            }
                            break;
                        }

                    case PullState.Vanish:
                        {
                            // 작아지며 사라짐 (곧 수확 처리로 오브젝트가 지워짐)
                            float t = Mathf.Clamp01(pullTimer / VanishTime);
                            extraLift = height * (0.8f + 0.15f * t);
                            scale = 1f - t;
                            if (pullTimer >= VanishTime) pull = PullState.Done;
                            break;
                        }
                }
            }
            else if (pull == PullState.Done)
            {
                extraLift = height * 0.95f;
                scale = 0f;
                angle = 0f;
            }

            transform.localRotation = baseRotation;
            transform.Rotate(tiltAxis, angle, Space.World);
            transform.localPosition = basePosition;
            transform.position += Vector3.up * (lift + extraLift);
            transform.localScale = baseScale * scale;
        }

        private Bounds GetBounds()
        {
            Bounds b = new Bounds(transform.position, Vector3.one * 0.5f);
            bool found = false;
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
            {
                if (!found) { b = r.bounds; found = true; }
                else b.Encapsulate(r.bounds);
            }
            return b;
        }

        // ---------- 파티클 (코드로 생성, 끝나면 스스로 삭제) ----------

        private void SpawnDirt(int count, float strength)
        {
            SpawnBurst("DirtBurst", count, new Color(0.45f, 0.3f, 0.18f), new Color(0.6f, 0.42f, 0.25f),
                       height * 0.08f, 2.5f * strength, 2f, 0.7f, false);
        }

        private void SpawnSparkle()
        {
            SpawnBurst("Sparkle", 16, new Color(1f, 0.95f, 0.6f), Color.white,
                       height * 0.06f, 1.5f, -0.1f, 0.6f, true);
        }

        private void SpawnBurst(string name, int count, Color a, Color b, float size, float speed, float gravity, float life, bool atTop)
        {
            var go = new GameObject(name);
            Vector3 pos = basePosition;
            if (transform.parent != null) pos = transform.parent.TransformPoint(basePosition);
            go.transform.position = pos + (atTop ? Vector3.up * height * 1.2f : Vector3.up * 0.05f);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.2f;
            main.loop = false;
            main.useUnscaledTime = true; // 게임 시간이 멈춰 있어도 재생
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.5f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size * 1.4f);
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            var shape = ps.shape;
            shape.shapeType = atTop ? ParticleSystemShapeType.Sphere : ParticleSystemShapeType.Hemisphere;
            shape.radius = height * (atTop ? 0.3f : 0.2f);
            shape.rotation = atTop ? Vector3.zero : new Vector3(-90f, 0f, 0f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = GetParticleMaterial();

            ps.Play();
        }

        private static Material particleMaterial;

        private static Material GetParticleMaterial()
        {
            if (particleMaterial != null) return particleMaterial;
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            particleMaterial = new Material(shader) { name = "HarvestParticle (generated)" };
            return particleMaterial;
        }
    }
}