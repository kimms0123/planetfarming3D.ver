using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using FarmingSystem.Core;
using FarmingSystem.Player;

namespace FarmingSystem.Intro
{
    // 새 게임 오프닝 카메라 경로 - PV의 PVCameraPath를 그대로 가져옴
    // - 자식으로 둔 "카메라 점"들을 차례로 부드럽게 지나감 (점마다 이동 시간·멈춤 시간·화각·흔들림)
    // - 카메라 점 만들기: Scene 창에서 원하는 구도로 화면을 맞춘 뒤 → 이 컴포넌트 ⋮ → "Scene 뷰 시점으로 카메라 점 추가"
    // - ⋮ → "자동 배치: 오프닝 (우주선 착륙)" = PV와 같은 점 4개를 착륙 자리 기준으로 만듦
    // - 점에 도착할 때 이벤트(On Arrive)로 우주선 착륙을 시작 (자동 배치가 1번 점에 연결해 둠)
    // 게임에서는: 시작 화면에서 "새 게임"을 고르면 TitleScreen이 재생 → 끝나면 마지막 구도에서 평소 카메라로 부드럽게 이어짐
    // 테스트: Test Key를 정해 두면 플레이 중 그 키로 재생 (우주선은 처음 한 번만 건물로 놓임)
    public class IntroCameraPath : MonoBehaviour
    {
        [Header("재생")]
        [Tooltip("플레이 중 이 키를 누르면 재생 (테스트용, None = 끔). 게임에서는 새 게임을 고를 때 자동 재생")]
        [SerializeField] private Key testKey = Key.None;
        [Tooltip("점 사이를 얼마나 곡선으로 지나갈지 (0 = 직선, 1 = 크게 휘는 곡선). 곡선이 크면 중간에 땅 쪽으로 처질 수 있음")]
        [SerializeField, Range(0f, 1f)] private float curveAmount = 0.4f;
        [Tooltip("이동 중 카메라가 땅·나무·산보다 최소 이만큼(m) 위에 있게 (맵 속으로 파고들지 않게)")]
        [SerializeField] private float minGroundClearance = 1.2f;
        [Tooltip("장애물을 넘을 때 몇 m 전부터 천천히 올라갈지 (클수록 더 완만하게)")]
        [SerializeField] private float liftRamp = 12f;
        [Tooltip("나무도 피해서 넘어감 (끄면 땅·산만 피하고 나무는 그냥 통과 → 높이 변화가 가장 적음)")]
        [SerializeField] private bool avoidTrees = true;

        [Header("안내음성에 길이 맞추기")]
        [Tooltip("켜면 카메라 이동·멈춤 시간을 같은 비율로 늘리거나 줄여서, 인트로가 안내음성(오프닝 소리)과 같이 끝나게 함\n" +
                 "각 점의 Move Time·Hold Time 비율(어느 장면이 길고 짧은지)은 그대로")]
        [SerializeField] private bool fitToVoice = true;
        [Tooltip("맞출 소리. 비우면 소리 설정(SoundLibrary)의 Intro 소리(BGM_Intro)")]
        [SerializeField] private AudioClip voiceClip;
        [Tooltip("소리가 끝난 뒤 카메라가 조금 더 머무는 시간 (초). 음성 끝에 여운을 주고 싶으면 늘리기, 소리보다 먼저 끝내려면 음수")]
        [SerializeField] private float voiceTail = 0.3f;
        private float pace = 1f; // 1보다 작으면 천천히 (음성이 더 길 때)

        [Header("이벤트")]
        public UnityEvent onStart = new UnityEvent();
        public UnityEvent onFinish = new UnityEvent();

        /// <summary>다른 연출(우주선 착륙 등)이 넣는 화면 흔들림 세기 (재생 중에만 적용)</summary>
        public static float ExternalShake;

        public bool IsPlaying { get; private set; }
        public bool HasPoints => Points().Count > 0;

        private Coroutine routine;
        private ThirdPersonCameraRig rig;
        private PlayerController player;
        private bool rigWasEnabled, playerWasEnabled;
        private Camera cam;

        private void Update()
        {
            if (testKey == Key.None || Keyboard.current == null || !Keyboard.current[testKey].wasPressedThisFrame) return;
            if (IsPlaying) return;
            Play();
        }

        /// <summary>테스트 재생 (게임에서는 TitleScreen이 Run()을 직접 기다림)</summary>
        public void Play()
        {
            if (!HasPoints) { Debug.LogWarning($"[오프닝 카메라] '{name}'에 카메라 점이 없어요. ⋮ 메뉴 → 자동 배치: 오프닝"); return; }
            if (routine != null) StopCoroutine(routine);
            routine = StartCoroutine(Run());
        }

        private List<IntroCamPoint> Points()
        {
            var list = new List<IntroCamPoint>();
            foreach (Transform c in transform)
            {
                if (!c.gameObject.activeSelf) continue;
                IntroCamPoint p = c.GetComponent<IntroCamPoint>();
                if (p == null) p = c.gameObject.AddComponent<IntroCamPoint>();
                list.Add(p);
            }
            return list;
        }

        private void Grab()
        {
            rig = FindAnyObjectByType<ThirdPersonCameraRig>();
            player = FindAnyObjectByType<PlayerController>();
            rigWasEnabled = rig != null && rig.enabled;
            playerWasEnabled = player != null && player.enabled;
            if (rig != null) rig.enabled = false;     // PV처럼 카메라를 직접 움직임
            if (player != null) player.enabled = false;
            UIInputBlocker.Block(this);
            IsPlaying = true;
        }

        private void Release()
        {
            if (!IsPlaying) return;
            if (rig != null && rigWasEnabled)
            {
                HandBackToRig(rig, cam);
                rig.enabled = true;
                rig.EndShot(); // 마지막 구도 → 평소 카메라로 부드럽게
            }
            if (player != null && playerWasEnabled) player.enabled = true;
            UIInputBlocker.Unblock(this);
            IsPlaying = false;
            ExternalShake = 0f;
        }

        // 안내음성 길이 ÷ 카메라 전체 시간 → 재생 속도 (예: 음성 15초, 카메라 12.3초 → 0.82배속으로 천천히)
        private float FitPace(List<IntroCamPoint> pts)
        {
            if (!fitToVoice) return 1f;
            AudioClip clip = voiceClip;
            if (clip == null)
            {
                var lib = Resources.Load<FarmingSystem.Audio.SoundLibrary>("SoundLibrary");
                if (lib != null) clip = lib.intro;
            }
            if (clip == null) return 1f;
            float total = 0f;
            for (int i = 0; i < pts.Count; i++) total += (i == 0 ? 0f : pts[i].moveTime) + pts[i].holdTime;
            // 검은 화면 문구가 먼저 지나간 만큼은 빼고, 남은 음성 길이에 맞춤
            float elapsed = FarmingSystem.Save.TitleScreen.IntroVoiceStartTime >= 0f
                ? Time.unscaledTime - FarmingSystem.Save.TitleScreen.IntroVoiceStartTime : 0f;
            float target = clip.length - elapsed + voiceTail;
            if (total <= 0.01f || target <= 0.5f)
            {
                Debug.Log($"[인트로] 안내음성 '{clip.name}' {clip.length:0.0}초가 검은 화면 문구({elapsed:0.0}초) 동안 거의 다 끝나서 카메라는 원래 속도로 재생");
                return 1f;
            }
            float p = Mathf.Clamp(total / target, 0.4f, 2.5f);
            Debug.Log($"[인트로] 안내음성 '{clip.name}' {clip.length:0.0}초 중 검은 화면 문구 {elapsed:0.0}초 → 남은 {target:0.0}초에 카메라 {total:0.0}초를 맞춰 {1f / p:0.00}배 길이로 재생");
            return p;
        }

        /// <summary>카메라 점을 차례로 지나감 (끝나면 평소 카메라로 넘겨줌)</summary>
        public IEnumerator Run()
        {
            List<IntroCamPoint> pts = Points();
            cam = Camera.main;
            if (pts.Count == 0 || cam == null) yield break;
            Grab();
            onStart?.Invoke();
            Transform ct = cam.transform;

            Vector3 prevPos = ct.position;
            Quaternion prevRot = ct.rotation;
            float prevFov = cam.fieldOfView;
            pace = FitPace(pts);

            for (int i = 0; i < pts.Count; i++)
            {
                IntroCamPoint p = pts[i];
                float targetFov = p.fov > 0f ? p.fov : prevFov;
                float move = i == 0 ? 0f : p.moveTime;

                // 이전 점 → 이 점 (곡선으로 부드럽게: 앞뒤 점을 참고)
                Vector3 p0 = i >= 2 ? pts[i - 2].transform.position : prevPos;
                Vector3 p3 = i + 1 < pts.Count ? pts[i + 1].transform.position : p.transform.position;
                float t = 0f;
                Vector3 a = prevPos, b = p.transform.position;
                float[] lift = move > 0f ? BuildLift(p0, a, b, p3) : null; // 이 구간을 미리 훑어서 땅·나무를 부드럽게 넘는 높이
                while (t < move)
                {
                    t += Dt * pace;
                    float k = Ease(Mathf.Clamp01(t / move), p.ease);
                    Vector3 pos = PathPos(p0, a, b, p3, k);
                    pos.y += SampleLift(lift, k);
                    ct.position = pos + Shake(Mathf.Lerp(PrevShake(pts, i), p.shake, k));
                    ct.rotation = Quaternion.Slerp(prevRot, p.transform.rotation, k);
                    cam.fieldOfView = Mathf.Lerp(prevFov, targetFov, k);
                    yield return null;
                }
                ct.position = p.transform.position;
                ct.rotation = p.transform.rotation;
                cam.fieldOfView = targetFov;
                p.onArrive?.Invoke();

                // 멈춤 (흔들림은 계속)
                t = 0f;
                while (t < p.holdTime)
                {
                    t += Dt * pace;
                    ct.position = p.transform.position + Shake(p.shake);
                    yield return null;
                }

                // 우주선 착륙이 끝날 때까지 기다림 (착륙 중에 카메라가 먼저 떠나지 않게)
                if (p.waitForLanding)
                {
                    IntroShipLanding landing = FindAnyObjectByType<IntroShipLanding>();
                    float safety = 0f;
                    while (landing != null && landing.IsLanding && safety < 30f)
                    {
                        safety += Dt;
                        ct.position = p.transform.position + Shake(p.shake);
                        yield return null;
                    }
                    t = 0f;
                    while (t < p.afterLandingHold)
                    {
                        t += Dt;
                        ct.position = p.transform.position + Shake(p.shake);
                        yield return null;
                    }
                }

                prevPos = p.transform.position;
                prevRot = p.transform.rotation;
                prevFov = targetFov;
            }

            ct.position = prevPos;
            onFinish?.Invoke();
            Release();
            routine = null;
        }

        // 평소 카메라(ThirdPersonCameraRig)가 마지막 구도에서 이어받게:
        // 좌우·상하 각도를 마지막 구도와 맞추고, "연출 시점"을 지금 카메라 자리로 맞춘 뒤 EndShot → 튀지 않고 부드럽게 돌아감
        // (팀 카메라 스크립트는 고치지 않고 값만 맞춤. 이름이 바뀌어 못 찾으면 그냥 넘어감)
        private static void HandBackToRig(ThirdPersonCameraRig r, Camera c)
        {
            if (r == null || c == null) return;
            const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
            System.Type type = typeof(ThirdPersonCameraRig);
            Vector3 e = c.transform.eulerAngles;
            float pitch = Mathf.DeltaAngle(0f, e.x);
            SetField(r, "yaw", e.y);
            SetField(r, "targetYaw", e.y);
            FieldInfo minP = type.GetField("minPitch", F), maxP = type.GetField("maxPitch", F);
            if (minP != null && maxP != null) pitch = Mathf.Clamp(pitch, (float)minP.GetValue(r), (float)maxP.GetValue(r));
            SetField(r, "pitch", pitch);
            SetField(r, "targetPitch", pitch);
            r.BeginShot(c.transform.position, c.transform.rotation, c.fieldOfView);
            SetField(r, "shotCurrentPosition", c.transform.position);
            SetField(r, "shotCurrentRotation", c.transform.rotation);
            SetField(r, "shotCurrentFov", c.fieldOfView);
            SetField(r, "shotWeight", 1f);
        }

        private static void SetField(ThirdPersonCameraRig r, string field, object v)
        {
            FieldInfo f = typeof(ThirdPersonCameraRig).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null && f.FieldType == v.GetType()) f.SetValue(r, v);
        }

        // 녹화용 시간 (PV와 같음): 일정 프레임 녹화 중이면 녹화 간격, 아니면 실제 시간
        public static float Dt => Time.captureFramerate > 0 ? Time.captureDeltaTime : Time.unscaledDeltaTime;
        private static float shakeClock;
        private static int shakeFrame = -1;

        /// <summary>캐릭터를 그 자리 땅 위에 세움 (발이 땅에 닿게)</summary>
        public static void PlacePlayerOnGround(Transform player, Vector3 spot, Quaternion rotation)
        {
            float groundY = spot.y;
            RaycastHit[] hits = Physics.RaycastAll(spot + Vector3.up * 5f, Vector3.down, 15f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit h in hits)
            {
                if (h.transform.IsChildOf(player)) continue;
                groundY = h.point.y;
                break;
            }

            float foot = 0f;
            CharacterController cc = player.GetComponent<CharacterController>();
            if (cc != null)
            {
                float sy = player.lossyScale.y;
                foot = (cc.height * 0.5f - cc.center.y) * sy + cc.skinWidth;
            }
            else
            {
                Collider col = player.GetComponentInChildren<Collider>();
                if (col != null) foot = player.position.y - col.bounds.min.y;
            }

            if (cc != null) cc.enabled = false; // 순간이동할 때는 꺼 둬야 위치가 바뀜
            player.SetPositionAndRotation(new Vector3(spot.x, groundY + foot, spot.z), rotation);
            if (cc != null) cc.enabled = true;
        }

        private static float PrevShake(List<IntroCamPoint> pts, int i) => i > 0 ? pts[i - 1].shake : 0f;

        private static Vector3 Shake(float amount)
        {
            if (Time.frameCount != shakeFrame) { shakeFrame = Time.frameCount; shakeClock += Dt; }
            float a = amount + ExternalShake;
            if (a <= 0f) return Vector3.zero;
            float t = shakeClock * 25f;
            return new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, t) - 0.5f, Mathf.PerlinNoise(t, t * 0.5f) - 0.5f) * a;
        }

        private static float Ease(float k, IntroCamPoint.EaseType e)
        {
            switch (e)
            {
                case IntroCamPoint.EaseType.Linear: return k;
                case IntroCamPoint.EaseType.EaseIn: return k * k;
                case IntroCamPoint.EaseType.EaseOut: return 1f - (1f - k) * (1f - k);
                default: return k * k * (3f - 2f * k);
            }
        }

        private Vector3 PathPos(Vector3 p0, Vector3 a, Vector3 b, Vector3 p3, float k)
        {
            Vector3 straight = Vector3.Lerp(a, b, k);
            Vector3 curved = CatmullRom(p0, a, b, p3, k);
            return Vector3.Lerp(straight, curved, curveAmount);
        }

        // 맵 속으로 파고들지 않게 (PV와 같음): 구간을 64칸으로 나눠 아래 땅·나무·산 높이를 재고 미리·천천히 올라갔다 내려옴
        private const int LiftSamples = 64;
        private float[] BuildLift(Vector3 p0, Vector3 a, Vector3 b, Vector3 p3)
        {
            var need = new float[LiftSamples + 1];
            float length = 0f;
            Vector3 prev = PathPos(p0, a, b, p3, 0f);
            for (int i = 0; i <= LiftSamples; i++)
            {
                Vector3 pos = PathPos(p0, a, b, p3, i / (float)LiftSamples);
                length += Vector3.Distance(prev, pos);
                prev = pos;
                float top = TopBelow(pos);
                need[i] = top > float.MinValue ? Mathf.Max(0f, top + minGroundClearance - pos.y) : 0f;
            }
            float metersPerSample = Mathf.Max(0.01f, length / LiftSamples);
            int spread = Mathf.Clamp(Mathf.CeilToInt(liftRamp / metersPerSample), 1, LiftSamples);

            float[] wide = new float[need.Length];
            for (int i = 0; i < need.Length; i++)
            {
                float m = 0f;
                for (int j = Mathf.Max(0, i - spread); j <= Mathf.Min(need.Length - 1, i + spread); j++) m = Mathf.Max(m, need[j]);
                wide[i] = m;
            }
            float[] smooth = Blur(Blur(Blur(wide, spread), spread), spread);
            for (int i = 0; i < smooth.Length; i++)
            {
                float k = i / (float)LiftSamples;
                float w = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k / 0.3f)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - k) / 0.3f));
                smooth[i] *= w;
            }
            return smooth;
        }

        private static float[] Blur(float[] src, int r)
        {
            var dst = new float[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                float sum = 0f; int n = 0;
                for (int j = Mathf.Max(0, i - r); j <= Mathf.Min(src.Length - 1, i + r); j++) { sum += src[j]; n++; }
                dst[i] = sum / n;
            }
            return dst;
        }

        private static float SampleLift(float[] lift, float k)
        {
            if (lift == null) return 0f;
            float f = Mathf.Clamp01(k) * LiftSamples;
            int i = Mathf.Min(LiftSamples - 1, Mathf.FloorToInt(f));
            return Mathf.Lerp(lift[i], lift[i + 1], f - i);
        }

        // 그 자리 아래에서 가장 높은 것 (땅·나무·산, 캐릭터·날아오는 우주선 제외)
        private float TopBelow(Vector3 pos)
        {
            float best = float.MinValue;
            IntroShipLanding landing = FindAnyObjectByType<IntroShipLanding>();
            foreach (RaycastHit h in Physics.RaycastAll(pos + Vector3.up * 60f, Vector3.down, 120f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (player != null && h.transform.IsChildOf(player.transform)) continue;
                if (landing != null && landing.IsShip(h.transform)) continue;
                if (!avoidTrees && h.collider.GetComponentInParent<FarmingSystem.Farming.FarmTile>() == null) continue;
                if (h.point.y > best) best = h.point.y;
            }
            return best;
        }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        private void OnDisable()
        {
            if (routine != null) StopCoroutine(routine);
            routine = null;
            Release();
        }

#if UNITY_EDITOR
        // ---------- 에디터: 자동 배치 (PV와 같은 점 4개) ----------

        [ContextMenu("자동 배치: 오프닝 (우주선 착륙)")]
        public void AutoOpening()
        {
            IntroShipLanding landing = FindAnyObjectByType<IntroShipLanding>(FindObjectsInactive.Include);
            if (landing == null)
            {
                UnityEditor.EditorUtility.DisplayDialog("자동 배치", "씬에 NewGame_ShipLanding(IntroShipLanding)이 없어요. 착륙 자리에 먼저 놓아 주세요.", "확인");
                return;
            }
            if (!ConfirmReplace()) return;

            Transform lt = landing.transform;
            float w = landing.ShipWidth;
            Vector3 L = lt.position;
            Vector3 F = landing.Forward;              // 우주선은 -F 쪽 하늘에서 날아옴
            Vector3 R = Vector3.Cross(Vector3.up, F);
            Vector3 E = landing.EnsureExitPoint().position; // 캐릭터 나타날 자리 (우주선 뒤쪽)
            GetIslandBounds(L, out Vector3 C, out float radius);

            // 1) 섬 반대편 바다 위 멀리·높이: 날아오는 길(배경)과 섬이 함께 보이게
            Vector3 p1 = C + F * (radius + 45f) + Vector3.up * 20f;
            AddPoint("Point 1 · 우주선 접근", p1, L - F * 25f + Vector3.up * 12f, 0f, 0.5f, 50f, 0f, IntroCamPoint.EaseType.Smooth,
                     landing.Play, false); // 도착하자마자 우주선 출발
            // 2) 착륙 자리 옆 낮은 곳에서 살짝 올려다봄 (우주선이 내려앉는 시간에 맞춰 도착)
            AddPoint("Point 2 · 착륙", L + R * (w * 1.3f) + F * (w * 0.7f) + Vector3.up * 1.2f, L + Vector3.up * 3f, 5f, 1.8f, 45f, 0.03f, IntroCamPoint.EaseType.Smooth, null, true);
            // 3) 캐릭터 앞에서 캐릭터 + 뒤의 우주선
            AddPoint("Point 3 · 캐릭터 등장", E - F * 3.2f + R * 1.8f + Vector3.up * 1.6f, E + F * 1.5f + Vector3.up * 1.2f, 2f, 2f, 40f, 0f, IntroCamPoint.EaseType.Smooth, null, false);
            // 4) 뒤로·위로 빠지며 주변 땅까지 → 여기서 평소 카메라로 이어짐
            AddPoint("Point 4 · 새로운 시작", E - F * 13f + R * 5f + Vector3.up * 7f, E + F * 2f + Vector3.up * 1f, 3.5f, 1f, 45f, 0f, IntroCamPoint.EaseType.EaseOut, null, false);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
            landing.SyncExitReference(); // 이후 캐릭터 자리를 옮기면 3·4번 점이 따라감
            Debug.Log("[오프닝 카메라] 점 4개 자동 배치 완료 (Point 1 도착 → 우주선 착륙 연결됨)");
        }

        private bool ConfirmReplace()
        {
            if (transform.childCount == 0) return true;
            if (!UnityEditor.EditorUtility.DisplayDialog("자동 배치", $"'{name}' 아래 카메라 점 {transform.childCount}개를 지우고 새로 만들까요?", "새로 만들기", "취소"))
                return false;
            for (int i = transform.childCount - 1; i >= 0; i--)
                UnityEditor.Undo.DestroyObjectImmediate(transform.GetChild(i).gameObject);
            return true;
        }

        private void AddPoint(string pointName, Vector3 pos, Vector3 look, float move, float hold, float fov, float shake,
                              IntroCamPoint.EaseType ease, UnityAction onArrive, bool waitForLanding)
        {
            pos = AboveGround(pos, 1.0f);
            var go = new GameObject(pointName);
            UnityEditor.Undo.RegisterCreatedObjectUndo(go, "카메라 점 자동 배치");
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
            IntroCamPoint pt = go.AddComponent<IntroCamPoint>();
            pt.moveTime = move;
            pt.holdTime = hold;
            pt.fov = fov;
            pt.shake = shake;
            pt.ease = ease;
            pt.waitForLanding = waitForLanding;
            pt.afterLandingHold = 0.3f;
            if (pt.onArrive == null) pt.onArrive = new UnityEvent();
            if (onArrive != null) UnityEditor.Events.UnityEventTools.AddPersistentListener(pt.onArrive, onArrive);
            UnityEditor.EditorUtility.SetDirty(pt);
        }

        private static Vector3 AboveGround(Vector3 p, float minHeight)
        {
            if (Physics.Raycast(p + Vector3.up * 200f, Vector3.down, out RaycastHit hit, 400f, ~0, QueryTriggerInteraction.Ignore))
                if (p.y < hit.point.y + minHeight) p.y = hit.point.y + minHeight;
            return p;
        }

        // 섬 가운데·반지름 (밭 칸 기준, 없으면 착륙 자리 기준)
        private static void GetIslandBounds(Vector3 fallback, out Vector3 center, out float radius)
        {
            bool found = false;
            Bounds b = new Bounds(fallback, Vector3.zero);
            foreach (FarmingSystem.Farming.FarmTile t in FindObjectsByType<FarmingSystem.Farming.FarmTile>(FindObjectsInactive.Include))
            {
                if (!found) { b = new Bounds(t.transform.position, Vector3.zero); found = true; }
                else b.Encapsulate(t.transform.position);
            }
            center = found ? new Vector3(b.center.x, fallback.y, b.center.z) : fallback;
            radius = found ? Mathf.Max(b.extents.x, b.extents.z) : 40f;
        }

        // ---------- 에디터: 카메라 점 만들기 ----------

        [ContextMenu("Scene 뷰 시점으로 카메라 점 추가")]
        private void AddPointFromSceneView()
        {
            UnityEditor.SceneView sv = UnityEditor.SceneView.lastActiveSceneView;
            if (sv == null || sv.camera == null) { Debug.LogWarning("[오프닝 카메라] Scene 창을 한 번 클릭한 뒤 다시 실행해 주세요"); return; }
            var go = new GameObject($"Point {transform.childCount + 1}");
            UnityEditor.Undo.RegisterCreatedObjectUndo(go, "카메라 점 추가");
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(sv.camera.transform.position, sv.camera.transform.rotation);
            go.AddComponent<IntroCamPoint>();
            UnityEditor.Selection.activeObject = go;
        }

        [ContextMenu("선택한 카메라 점을 Scene 뷰 시점으로 옮기기")]
        private void MoveSelectedToSceneView()
        {
            UnityEditor.SceneView sv = UnityEditor.SceneView.lastActiveSceneView;
            GameObject sel = UnityEditor.Selection.activeGameObject;
            if (sv == null || sel == null || sel.transform.parent != transform) { Debug.LogWarning("[오프닝 카메라] Hierarchy에서 이 경로의 카메라 점 하나를 선택한 뒤, 이 컴포넌트를 다시 열어 실행하세요"); return; }
            UnityEditor.Undo.RecordObject(sel.transform, "카메라 점 옮기기");
            sel.transform.SetPositionAndRotation(sv.camera.transform.position, sv.camera.transform.rotation);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.9f);
            foreach (Transform c in transform)
            {
                Gizmos.DrawWireSphere(c.position, 0.25f);
                Gizmos.DrawLine(c.position, c.position + c.forward * 1.2f); // 바라보는 방향
            }
        }

        // 선택했을 때: 카메라가 실제로 지나갈 길 (노란 선, 장애물 넘는 높이 포함)
        private void OnDrawGizmosSelected()
        {
            var pts = new List<Transform>();
            foreach (Transform c in transform) if (c.gameObject.activeSelf) pts.Add(c);
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 1f);
            for (int i = 1; i < pts.Count; i++)
            {
                Vector3 a = pts[i - 1].position, b = pts[i].position;
                Vector3 p0 = i >= 2 ? pts[i - 2].position : a;
                Vector3 p3 = i + 1 < pts.Count ? pts[i + 1].position : b;
                float[] lift = BuildLift(p0, a, b, p3);
                Vector3 prev = a;
                for (int s = 1; s <= 32; s++)
                {
                    float k = s / 32f;
                    Vector3 pos = PathPos(p0, a, b, p3, k);
                    pos.y += SampleLift(lift, k);
                    Gizmos.DrawLine(prev, pos);
                    prev = pos;
                }
            }
        }
#endif
    }
}