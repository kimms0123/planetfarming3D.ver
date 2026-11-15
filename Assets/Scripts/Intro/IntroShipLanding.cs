using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FarmingSystem.Building;
using FarmingSystem.Farming;
using FarmingSystem.Player;
using FarmingSystem.Save;

namespace FarmingSystem.Intro
{
    // 새 게임 오프닝: 플레이어 우주선이 하늘에서 날아와 착륙 → 먼지·화면 흔들림 → 캐릭터 등장 - PV의 PVShipLanding을 그대로 가져옴
    // - 이 오브젝트 위치 = 착륙 자리 (Scene 창에서 옮겨서 정함). 우주선은 이 오브젝트의 뒤쪽(-Z, 파란 화살표 반대) 하늘에서 비스듬히 내려옴
    //   → Scene 창의 주황 선 = 날아오는 길. 산을 지나가지 않게 이 오브젝트를 돌려서(Y 회전) 바다 쪽에서 오게 맞추기
    // - 자식 "ExitPoint"(없으면 자동 생성) = 캐릭터가 나타날 자리 (초록 상자)
    // - 시작: 카메라 경로 1번 점의 On Arrive에 Play()가 연결되어 있음 (자동 배치가 연결)
    // 게임에서 달라진 점 (PV와 차이):
    //  - 우주선이 '내 우주선' 건물(Building_player_spaceship)로 땅 칸에 맞춰 놓이고 저장됨 (공사 없이 바로 완성, 크기 = 건물의 Footprint)
    //    → 착륙 자리는 가장 가까운 칸에, 우주선 모델 방향은 90도 단위로 맞춰짐 (날아오는 길은 이 오브젝트 방향 그대로)
    //  - 새 게임마다 한 번만 놓임 (테스트로 여러 번 재생해도 우주선은 하나)
    [ExecuteAlways] // 에디터에서 ExitPoint를 옮기면 캐릭터를 비추는 카메라 점도 같이 따라가게
    public class IntroShipLanding : MonoBehaviour
    {
        [Header("우주선")]
        [Tooltip("내 우주선 건물 데이터 (Assets/Buildings/Data/Building_player_spaceship). 비우면 저장 목록에서 찾음")]
        [SerializeField] private BuildingData shipBuilding;
        [Tooltip("우주선 방향 (90도씩 돌림). 0 = PV 영상과 같은 방향, 2 = PV 영상의 정반대 (기본)")]
        [SerializeField, Range(0, 3)] private int shipFacing = 2;

        [Header("비행")]
        [Tooltip("하늘에서 출발하는 높이 (m)")]
        [SerializeField] private float startHeight = 40f;
        [Tooltip("착륙 자리에서 수평으로 얼마나 떨어진 곳에서 출발할지 (m, 이 오브젝트의 뒤쪽 = -Z 방향)")]
        [SerializeField] private float startDistance = 60f;
        [Tooltip("날아오는 시간 (초)")]
        [SerializeField] private float flyTime = 5f;
        [Tooltip("마지막에 땅 위에서 잠깐 멈칫하며 내려앉는 시간 (초)")]
        [SerializeField] private float settleTime = 1.2f;

        [Header("착륙 연출")]
        [SerializeField] private float landShake = 0.25f;
        [SerializeField] private float shakeTime = 0.8f;
        [SerializeField] private Color dustColor = new Color(0.8f, 0.72f, 0.6f, 0.75f);
        [SerializeField] private Material dustMaterial;

        [Header("캐릭터 등장")]
        [Tooltip("착륙 뒤 캐릭터가 나타날 때까지 (초)")]
        [SerializeField] private float playerDelay = 1.2f;

        /// <summary>캐릭터가 나타나는 순간 (시작 화면이 숨겨 둔 캐릭터도 같이 보이게)</summary>
        public static event Action OnPlayerAppear;

        /// <summary>착륙 연출(날아옴 → 착륙 → 캐릭터 등장) 중인지. 카메라 경로가 이게 끝날 때까지 기다릴 수 있음</summary>
        public bool IsLanding { get; private set; }

        private const float DefaultCell = 1.94f; // 에디터 미리보기용 칸 크기 (게임에서는 실제 칸 크기)
        private PlacedBuilding ship;
        private Vector3 landPos;
        private float shipRadius = 4f;
        private Transform exitPoint;
        private readonly List<Renderer> hiddenPlayerRenderers = new List<Renderer>();
        private ParticleSystem dust;

        [SerializeField, HideInInspector] private Vector3 syncedExitLocal;   // 카메라 점을 마지막으로 맞춘 캐릭터 자리 (이 오브젝트 기준)
        [SerializeField, HideInInspector] private bool syncedExitSet;

        private void Start()
        {
            if (!Application.isPlaying) return;
            exitPoint = EnsureExitPoint();
        }

        /// <summary>우주선이 날아가는 방향 = 이 오브젝트 정면 (PV와 같음, 날아오는 길은 이 방향 그대로)</summary>
        public Vector3 Forward
        {
            get
            {
                Vector3 f = transform.forward;
                f.y = 0f;
                return f.sqrMagnitude < 0.0001f ? Vector3.forward : f.normalized;
            }
        }
        private int Steps => Mathf.RoundToInt(transform.eulerAngles.y / 90f) & 3; // 건물(우주선 모델)은 90도 단위로만 놓임

        /// <summary>우주선 가로 길이 (m, 건물 칸 수 × 칸 크기)</summary>
        public float ShipWidth
        {
            get
            {
                BuildingData d = Data;
                float cell = BuildingSystem.Instance != null && Application.isPlaying ? BuildingSystem.Instance.CellSize : DefaultCell;
                return d != null ? Mathf.Max(d.footprint.x, d.footprint.y) * cell * d.fill : 9f;
            }
        }

        private BuildingData Data
        {
            get
            {
                if (shipBuilding == null && Application.isPlaying && SaveManager.Instance != null)
                    shipBuilding = SaveManager.Instance.FindBuildingData("Building_player_spaceship");
                return shipBuilding;
            }
        }

        public bool IsShip(Transform t) => ship != null && t.IsChildOf(ship.transform);

        [ContextMenu("디버그: 지금 착륙")]
        public void Play()
        {
            if (!Prepare()) return;
            StopAllCoroutines();
            IsLanding = true;
            StartCoroutine(Land());
        }

        // ───────────────────────── 에디터: 캐릭터 자리를 옮기면 카메라도 따라감 ─────────────────────────
        // ExitPoint(초록 상자)를 옮기면, 착륙 이후 캐릭터를 비추는 카메라 점들(착륙을 기다리는 점 다음부터 끝까지)이
        // 캐릭터와의 거리·각도를 그대로 유지한 채 같이 옮겨지고 돌아감 (우주선 반대편으로 옮겨도 캐릭터 정면을 비춤)
        // 실행 취소(Ctrl+Z)로 되돌릴 수 있음

        /// <summary>지금 캐릭터 자리를 기준으로 기억 (가져오기·자동 배치 직후 부름 - 이후 옮긴 만큼만 카메라가 따라감)</summary>
        public void SyncExitReference()
        {
            Transform e = transform.Find("ExitPoint");
            if (e == null) return;
            syncedExitLocal = transform.InverseTransformPoint(e.position);
            syncedExitSet = true;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

#if UNITY_EDITOR
        private void Update()
        {
            if (Application.isPlaying) return;
            Transform e = transform.Find("ExitPoint");
            if (e == null) return;
            Vector3 local = transform.InverseTransformPoint(e.position);
            if (!syncedExitSet) { SyncExitReference(); return; }
            if ((local - syncedExitLocal).sqrMagnitude < 0.000001f) return;

            Vector3 oldExit = transform.TransformPoint(syncedExitLocal);
            Vector3 newExit = e.position;
            FollowCameraPoints(oldExit, newExit);
            syncedExitLocal = local;
            UnityEditor.EditorUtility.SetDirty(this);
        }

        private void FollowCameraPoints(Vector3 oldExit, Vector3 newExit)
        {
            IntroCameraPath path = FindAnyObjectByType<IntroCameraPath>(FindObjectsInactive.Include);
            if (path == null) return;
            var pts = new List<Transform>();
            int landingShot = -1;
            foreach (Transform c in path.transform)
            {
                IntroCamPoint p = c.GetComponent<IntroCamPoint>();
                if (landingShot < 0 && p != null && p.waitForLanding) landingShot = pts.Count;
                pts.Add(c);
            }
            int from = landingShot >= 0 ? landingShot + 1 : Mathf.Max(0, pts.Count - 2); // 착륙 장면 다음부터 (없으면 마지막 2개)
            if (from >= pts.Count) return;

            // 우주선 가운데 기준으로 캐릭터가 돌아간 각도만큼 카메라도 돌림
            Vector3 c0 = transform.position;
            Vector3 a = oldExit - c0, b = newExit - c0;
            a.y = 0f; b.y = 0f;
            Quaternion turn = a.sqrMagnitude > 0.01f && b.sqrMagnitude > 0.01f
                ? Quaternion.Euler(0f, Vector3.SignedAngle(a, b, Vector3.up), 0f) : Quaternion.identity;

            for (int i = from; i < pts.Count; i++)
            {
                Transform t = pts[i];
                UnityEditor.Undo.RecordObject(t, "카메라 점 따라 옮기기");
                t.SetPositionAndRotation(newExit + turn * (t.position - oldExit), turn * t.rotation);
            }
        }
#endif

        /// <summary>캐릭터가 나타날 자리 (없으면 우주선 뒤쪽에 만듦 - 에디터에서도 저장됨)</summary>
        public Transform EnsureExitPoint()
        {
            Transform e = transform.Find("ExitPoint");
            if (e != null) return e;
            e = new GameObject("ExitPoint").transform;
            e.SetParent(transform, false);
            e.localPosition = new Vector3(0f, 0f, -ShipWidth * 0.5f - 1f);
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.Undo.RegisterCreatedObjectUndo(e.gameObject, "ExitPoint");
#endif
            return e;
        }

        // ───────────────────────── 우주선 놓기 (건물) ─────────────────────────

        /// <summary>우주선을 건물로 놓고 하늘(출발 자리)에 올려 둠 + 캐릭터 숨김. 이미 놓였으면 하늘로만 옮김</summary>
        public bool Prepare()
        {
            if (exitPoint == null) exitPoint = EnsureExitPoint();
            if (ship == null)
            {
                BuildingSystem sys = BuildingSystem.Instance;
                BuildingData data = Data;
                if (sys == null || data == null)
                {
                    Debug.LogWarning($"[오프닝 우주선] {(sys == null ? "씬에 BuildingSystem이 없어요" : "Ship Building 칸이 비어 있어요 (Building_player_spaceship을 넣어 주세요)")}");
                    return false;
                }
                int rot = (Steps + shipFacing) & 3;
                Vector2Int fp = BuildingSystem.RotatedFootprint(data, rot);
                if (!sys.TryWorldToGrid(transform.position, out Vector2Int mid)) mid = Vector2Int.zero;
                Vector2Int start = new Vector2Int(mid.x - (fp.x - 1) / 2, mid.y - (fp.y - 1) / 2);

                var cells = new List<Vector2Int>();
                Vector3 sum = Vector3.zero;
                int tiles = 0, bad = 0;
                float top = float.MinValue;
                for (int x = 0; x < fp.x; x++)
                    for (int z = 0; z < fp.y; z++)
                    {
                        var g = new Vector2Int(start.x + x, start.y + z);
                        cells.Add(g);
                        if (sys.TryGetTile(g, out FarmTile t) && !sys.IsOccupied(g))
                        {
                            sum += t.Center; tiles++;
                            top = Mathf.Max(top, t.Center.y);
                        }
                        else bad++;
                    }
                if (bad > 0) Debug.LogWarning($"[오프닝 우주선] 착륙 자리 {fp.x}x{fp.y}칸 중 {bad}칸이 땅이 아니거나 다른 건물과 겹쳐요. NewGame_ShipLanding을 평평한 빈 땅으로 옮겨 주세요");
                Vector3 center = tiles > 0 ? sum / tiles : transform.position;
                center.y = tiles > 0 ? top : transform.position.y;

                ship = sys.Place(data, cells, center, rot);
                if (ship.State != PlacedBuilding.BuildState.Complete) ship.Complete(); // 공사 없이 바로
                landPos = center;
                if (BuildingSystem.TryGetBounds(ship.transform, out Bounds b)) shipRadius = Mathf.Max(b.size.x, b.size.z) * 0.5f;
            }
            ship.transform.SetPositionAndRotation(StartPos(), Quaternion.identity);
            SetPlayerVisible(false);
            return true;
        }

        private Vector3 StartPos() => landPos - Forward * startDistance + Vector3.up * startHeight;

        // ───────────────────────── 착륙 (PV와 같음) ─────────────────────────

        private IEnumerator Land()
        {
            Transform t = ship.transform;
            Vector3 fwd = Forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 from = StartPos();
            Vector3 hover = landPos + Vector3.up * 2f;

            // 1) 비스듬히 날아옴 (점점 느려짐) - 기울었다가 수평으로
            float time = 0f;
            while (time < flyTime)
            {
                time += Time.deltaTime;
                float k = Mathf.Clamp01(time / flyTime);
                float e = 1f - Mathf.Pow(1f - k, 3f);
                t.position = Vector3.Lerp(from, hover, e);
                t.rotation = Quaternion.AngleAxis(Mathf.Lerp(12f, 0f, e), right) * Quaternion.AngleAxis(Mathf.Sin(k * Mathf.PI) * 6f, fwd);
                EmitDust(t.position.y - landPos.y, 0.6f);
                yield return null;
            }

            // 2) 땅 위에서 내려앉음
            time = 0f;
            while (time < settleTime)
            {
                time += Time.deltaTime;
                float k = Mathf.Clamp01(time / settleTime);
                t.position = Vector3.Lerp(hover, landPos, k * k * (3f - 2f * k));
                EmitDust(t.position.y - landPos.y, 1f);
                yield return null;
            }
            t.SetPositionAndRotation(landPos, Quaternion.identity);

            // 3) 닿는 순간: 먼지 크게 + 화면 흔들림
            Burst(70);
            StartCoroutine(ShakeRoutine());

            // 4) 캐릭터 등장
            yield return new WaitForSeconds(playerDelay);
            PlayerController pc = FindAnyObjectByType<PlayerController>();
            if (pc != null && exitPoint != null)
            {
                // 우주선에서 밖으로 나오는 방향을 보게
                Vector3 outward = exitPoint.position - landPos;
                outward.y = 0f;
                Quaternion face = outward.sqrMagnitude > 0.01f ? Quaternion.LookRotation(outward.normalized, Vector3.up) : exitPoint.rotation;
                IntroCameraPath.PlacePlayerOnGround(pc.transform, exitPoint.position, face); // 발이 땅에 닿게
            }
            SetPlayerVisible(true);
            OnPlayerAppear?.Invoke();
            IsLanding = false;
            if (SaveManager.Instance != null && SaveManager.Instance.IsPlaying) SaveManager.Instance.Save(); // 우주선 자리 바로 저장
        }

        private IEnumerator ShakeRoutine()
        {
            float time = 0f;
            while (time < shakeTime)
            {
                time += Time.deltaTime;
                IntroCameraPath.ExternalShake = landShake * (1f - time / shakeTime);
                yield return null;
            }
            IntroCameraPath.ExternalShake = 0f;
        }

        // 숨길 때 켜져 있던 것만 기억했다가 그것만 다시 켬
        private void SetPlayerVisible(bool v)
        {
            PlayerController pc = FindAnyObjectByType<PlayerController>();
            if (!v)
            {
                if (pc == null) return;
                foreach (Renderer r in pc.GetComponentsInChildren<Renderer>())
                {
                    if (r == null || !r.enabled) continue;
                    r.enabled = false;
                    if (!hiddenPlayerRenderers.Contains(r)) hiddenPlayerRenderers.Add(r);
                }
            }
            else
            {
                foreach (Renderer r in hiddenPlayerRenderers) if (r != null) r.enabled = true;
                hiddenPlayerRenderers.Clear();
            }
        }

        // ───────────────────────── 먼지 (PV와 같음) ─────────────────────────

        private float dustCarry;

        private void EmitDust(float height, float strength)
        {
            float k = 1f - Mathf.Clamp01(height / 14f);
            if (k <= 0f) return;
            dustCarry += 90f * k * k * strength * Time.deltaTime;
            int n = Mathf.FloorToInt(dustCarry);
            if (n <= 0) return;
            dustCarry -= n;
            Emit(n, 0.5f + 0.5f * k, 0.4f + 0.6f * k);
        }

        private void Burst(int count) => Emit(count, 1.3f, 1f);

        private void Emit(int count, float speedScale, float alpha)
        {
            ParticleSystem ps = GetDust();
            if (ps == null) return;
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                float a = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                ep.position = landPos + dir * shipRadius * UnityEngine.Random.Range(0.25f, 0.9f) + Vector3.up * UnityEngine.Random.Range(0.2f, 0.6f);
                ep.velocity = dir * 11f * speedScale * UnityEngine.Random.Range(0.6f, 1.2f) + Vector3.up * UnityEngine.Random.Range(0.3f, 1.6f);
                ep.startSize = 2.6f * UnityEngine.Random.Range(0.6f, 1.3f);
                ep.startLifetime = UnityEngine.Random.Range(1.6f, 2.8f);
                ep.rotation = UnityEngine.Random.Range(0f, 360f);
                ep.startColor = new Color(dustColor.r, dustColor.g, dustColor.b, dustColor.a * alpha);
                ps.Emit(ep, 1);
            }
        }

        private ParticleSystem GetDust()
        {
            if (dust != null) return dust;
            Material mat = dustMaterial;
            if (mat == null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (sh == null) return null;
                const int N = 64;
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                        float v = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, v * v * (3f - 2f * v)));
                    }
                tex.Apply();
                mat = new Material(sh);
                mat.SetTexture("_BaseMap", tex);
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.renderQueue = 3000;
            }
            var go = new GameObject("IntroLandingDust");
            dust = go.AddComponent<ParticleSystem>();
            dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = dust.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 2000;
            main.startSpeed = 0f;
            main.gravityModifier = -0.02f;
            var em = dust.emission; em.enabled = false;
            var sp = dust.shape; sp.enabled = false;
            var lim = dust.limitVelocityOverLifetime;
            lim.enabled = true; lim.limit = 100f; lim.dampen = 0f; lim.drag = 1.2f;
            lim.multiplyDragByParticleSize = false; lim.multiplyDragByParticleVelocity = false;
            var size = dust.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1.8f)));
            var col = dust.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.sharedMaterial = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            dust.Play();
            return dust;
        }

        // ───────────────────────── Scene 창 미리보기 ─────────────────────────

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.9f);
            Vector3 start = transform.position - Forward * startDistance + Vector3.up * startHeight;
            Gizmos.DrawWireSphere(transform.position, ShipWidth * 0.5f);
            Gizmos.DrawLine(transform.position + Vector3.up * 2f, start); // 날아오는 길
        }

        // 플레이 안 해도 항상 보임: 착륙한 우주선 모습(반투명) + 날아오는 길 + 캐릭터가 나타날 자리
        private void OnDrawGizmos()
        {
            if (Application.isPlaying) return;

            // 날아오는 길 (주황 선) - 산·나무를 지나가지 않는지 확인
            Gizmos.color = new Color(1f, 0.55f, 0.15f, 0.8f);
            Gizmos.DrawLine(transform.position + Vector3.up * 2f, transform.position - Forward * startDistance + Vector3.up * startHeight);

            // 캐릭터 나타날 자리
            Transform exit = transform.Find("ExitPoint");
            Vector3 ep = exit != null ? exit.position : transform.TransformPoint(new Vector3(0f, 0f, -ShipWidth * 0.5f - 1f));
            Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.9f);
            Gizmos.DrawWireCube(ep + Vector3.up * 0.9f, new Vector3(0.7f, 1.8f, 0.7f));
            Gizmos.DrawWireSphere(ep + Vector3.up * 2f, 0.3f);

            BuildingData d = shipBuilding;
            if (d == null) return;
            int rot = (Steps + shipFacing) & 3;
            Vector2Int fp = BuildingSystem.RotatedFootprint(d, rot);

            // 차지하는 칸 (흰 테두리)
            Gizmos.color = new Color(1f, 1f, 1f, 0.6f);
            Gizmos.DrawWireCube(transform.position + Vector3.up * 0.05f, new Vector3(fp.x * DefaultCell, 0.1f, fp.y * DefaultCell));

            if (d.prefab == null) return;
            Transform root = d.prefab.transform;
            Matrix4x4 rootInv = Matrix4x4.Translate(-root.position);
            Quaternion yaw = Quaternion.Euler(0f, rot * 90f + d.modelYaw, 0f);

            bool found = false;
            Bounds b = default;
            foreach (MeshFilter mf in d.prefab.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                Matrix4x4 m = Matrix4x4.Rotate(yaw) * rootInv * mf.transform.localToWorldMatrix;
                Bounds mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 w = m.MultiplyPoint3x4(c);
                    if (!found) { b = new Bounds(w, Vector3.zero); found = true; } else b.Encapsulate(w);
                }
            }
            if (!found) return;
            // 건물과 같은 맞춤: 칸 영역(fill) 안에 들어가게
            float sizeX = fp.x * DefaultCell * d.fill, sizeZ = fp.y * DefaultCell * d.fill;
            float sc = d.manualScale > 0f ? d.manualScale : Mathf.Min(sizeX / Mathf.Max(b.size.x, 0.001f), sizeZ / Mathf.Max(b.size.z, 0.001f));
            Vector3 offset = new Vector3(-b.center.x * sc, -b.min.y * sc, -b.center.z * sc);
            Matrix4x4 place = Matrix4x4.Translate(transform.position + offset) * Matrix4x4.Scale(Vector3.one * sc) * Matrix4x4.Rotate(yaw);

            Gizmos.color = new Color(1f, 0.75f, 0.4f, 0.45f);
            foreach (MeshFilter mf in d.prefab.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                Gizmos.matrix = place * (rootInv * mf.transform.localToWorldMatrix);
                Gizmos.DrawMesh(mf.sharedMesh);
            }
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}