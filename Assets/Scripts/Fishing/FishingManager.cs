using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using FarmingSystem.Building;
using FarmingSystem.Core;
using FarmingSystem.Inventory;
using FarmingSystem.Player;
using FarmingSystem.Save;
using FarmingSystem.UI;
using FarmingSystem.Audio;

namespace FarmingSystem.Fishing
{
    // 낚시 (모동숲 방식)
    //  1. 물가에 물고기 그림자가 헤엄침 (플레이어 주변 물가에 몇 마리씩, 지금 계절·시간·장소에 맞는 물고기)
    //  2. 낚싯대를 들고 물 쪽을 보며 좌클릭 → 낚싯대를 꺼내 찌를 던짐 (십자선이 가리키는 물, 너무 멀면 앞쪽으로)
    //  3. 찌 근처(4m 안)의 물고기가 다가와서 몇 번 툭툭 건드림 (이때 누르면 놀라서 도망)
    //  4. 찌가 '쑥' 들어가면 그때 좌클릭 → 잡음! (물고기마다 누를 수 있는 시간이 다름)
    //  5. 잡으면 낚아 올리는 동작 → 그림·이름·설명 창 → 가방에 들어가고 도감에 등록
    //  - 우클릭이나 이동 키 = 찌를 거둠 / 낚싯대 내구도는 물고기가 물었을 때(잡든 놓치든) 1 줄어듦
    // 플레이어 동작: 캐릭터 모델에 FishingController(클립 8개) + FishingFX가 붙어 있으면 그 동작·찌·줄·물보라를 씀
    //  (FishingController의 [F] 테스트 흐름은 꺼지고 이 스크립트가 진행)
    //  없으면 동작 없이 간단한 찌·줄로 진행
    // 설치: Tools → 행성농사일지 → 물고기 → 낚시 준비하기. 씬에 없으면 게임이 시작될 때 기본 설정으로 저절로 생김
    // 농사 행동(FarmActionController)보다 먼저 실행 → 낚싯대를 들고 클릭하면 낚시가 먼저 처리되고, 그 프레임엔 농사 행동이 안 일어남
    [DefaultExecutionOrder(-100)]
    public class FishingManager : MonoBehaviour
    {
        public static FishingManager Instance { get; private set; }

        [Header("물고기 그림자")]
        [Tooltip("물속을 헤엄치는 물고기 모델 (black_fish). 비우면 검은 타원")]
        [SerializeField] private GameObject fishModel;
        [Tooltip("모델 앞쪽이 헤엄치는 방향과 안 맞으면 90 / 180 / 270")]
        [SerializeField] private float fishModelYaw = 0f;
        [Tooltip("물고기 등(맨 위)이 물 표면에서 얼마나 아래에 오는지 (m). 물이 불투명해서 안 보이면 0이나 음수로")]
        [SerializeField] private float swimDepth = 0.05f;
        [Tooltip("물 표면에 검은 그림자를 그림 (모동숲처럼). 물이 불투명해도 물고기 위치가 보임")]
        [SerializeField] private bool surfaceShadow = true;
        [Tooltip("물 표면 그림자 진하기 (0 = 안 보임 ~ 1 = 새까맘)")]
        [SerializeField, Range(0f, 1f)] private float shadowOpacity = 0.6f;
        [Tooltip("물 표면 그림자 크기 (물고기 길이에 곱함)")]
        [SerializeField] private float shadowScale = 1.15f;
        [Tooltip("플레이어 주변에 동시에 보이는 물고기 수")]
        [SerializeField] private int maxShadows = 5;
        [Tooltip("플레이어에서 이 거리 안의 물가에 생김 (m)")]
        [SerializeField] private float spawnRadius = 16f;

        [Header("물")]
        [Tooltip("물 표면 높이. -999 = 섬 생성기(IslandMapGenerator) 설정에서 자동으로 읽음")]
        [SerializeField] private float waterSurfaceY = -999f;

        [Header("던지기")]
        [SerializeField] private float minCast = 1.5f;
        [SerializeField] private float maxCast = 7f;
        [Tooltip("낚싯대를 들고 클릭했는데 안 던져지면 Console에 이유를 출력 (테스트용)")]
        [SerializeField] private bool logWhyNotCasting = true;
        [Tooltip("찌 근처 이 거리 안의 물고기가 다가옴 (m)")]
        [SerializeField] private float noticeRange = 4f;

        private enum Outcome { Cancel, Early, Missed, Caught }

        private float waterY;
        private bool waterReady;
        private readonly List<FishShadow> shadows = new List<FishShadow>();
        private float spawnTimer;
        private List<FishData> allFish;

        private PlayerController player;
        private bool playerWasEnabled;
        private FarmActionController farmActions;
        private FishingController anim; // 팀 낚시 동작 (캐릭터 모델에 붙어 있으면)
        private FishingFX fx;           // 팀 찌·줄·물보라

        private bool fishing;
        private Coroutine flow;
        private Vector3 castTo;
        private FishShadow hooked;
        private Material shadowMat, surfaceMat;

        // 동작이 없을 때 쓰는 간단한 찌·줄
        private Transform bobber;
        private LineRenderer line;
        private float dip;
        private bool simpleBobberFloating;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindAnyObjectByType<FishingManager>() != null) return;
            new GameObject("FishingManager").AddComponent<FishingManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            RestorePlayer();
        }

        private void Update()
        {
            if (!waterReady) DetectWater();
            if (player == null) FindPlayer();
            if (farmActions == null) farmActions = FindAnyObjectByType<FarmActionController>();
            if (player == null || !waterReady) return;

            UpdateShadows(Time.deltaTime);
            if (TitleScreen.IsShowing) { if (fishing) Abort(); return; }
            if (!fishing) TryStart();
            UpdateSimpleBobber();
        }

        private void FindPlayer()
        {
            player = FindAnyObjectByType<PlayerController>();
            if (player == null) return;
            anim = player.GetComponentInChildren<FishingController>(true);
            fx = player.GetComponentInChildren<FishingFX>(true);
            if (anim != null) anim.externalControl = true; // [F] 테스트 흐름 끄기
        }

        // 팀 동작을 쓸 수 있는지 (클립이 다 들어 있어야)
        private bool Animated => anim != null && fx != null && anim.animator != null
                                 && anim.equip != null && anim.cast != null && anim.waitLoop != null && anim.bite != null;

        // ───────────────────────── 물 ─────────────────────────

        private void DetectWater()
        {
            if (waterSurfaceY > -900f) { waterY = waterSurfaceY; waterReady = true; return; }
            var gen = FindAnyObjectByType<FarmingSystem.World.IslandMapGenerator>();
            if (gen == null) return;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var fy = typeof(FarmingSystem.World.IslandMapGenerator).GetField("waterSurfaceY", flags);
            var fs = typeof(FarmingSystem.World.IslandMapGenerator).GetField("worldScale", flags);
            float y = fy != null ? (float)fy.GetValue(gen) : 0.85f;
            float s = fs != null ? (float)fs.GetValue(gen) : 2f;
            waterY = gen.transform.position.y + y * s;
            waterReady = true;
            Debug.Log($"[낚시] 물 표면 높이 {waterY:0.00}");
        }

        /// <summary>그 자리가 물인지 (위에서 아래로 쏴서 물 높이보다 높은 땅이 없으면 물)</summary>
        public bool IsWater(Vector3 p)
        {
            Vector3 from = new Vector3(p.x, waterY + 40f, p.z);
            RaycastHit[] hits = Physics.RaycastAll(from, Vector3.down, 80f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float top = float.MinValue;
            foreach (RaycastHit h in hits)
            {
                if (player != null && h.collider.transform.IsChildOf(player.transform)) continue;
                if (h.collider.GetComponentInParent<FishShadow>() != null) continue;
                if (h.collider is CharacterController) continue;
                if (h.point.y > top) top = h.point.y;
            }
            return top < waterY + 0.1f;
        }

        // 물이면서 4m 안에 땅이 있는 곳 (물가)
        private bool IsShoreWater(Vector3 p)
        {
            if (!IsWater(p)) return false;
            for (int i = 0; i < 8; i++)
            {
                Vector3 d = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
                if (!IsWater(p + d * 2f) || !IsWater(p + d * 4f)) return true;
            }
            return false;
        }

        // 주변이 거의 다 물이면 바다, 아니면 강
        private FishHabitat HabitatAt(Vector3 p)
        {
            int water = 0;
            for (int i = 0; i < 12; i++)
                if (IsWater(p + Quaternion.Euler(0f, i * 30f, 0f) * Vector3.forward * 6f)) water++;
            return water >= 9 ? FishHabitat.Sea : FishHabitat.River;
        }

        // ───────────────────────── 그림자 (물고기) ─────────────────────────

        private List<FishData> AllFish()
        {
            if (allFish != null && allFish.Count > 0) return allFish;
            allFish = new List<FishData>();
            if (SaveManager.Instance != null)
                foreach (ItemData it in SaveManager.Instance.Items) if (it is FishData f && !allFish.Contains(f)) allFish.Add(f);
            if (allFish.Count == 0)
                foreach (FishData f in Resources.FindObjectsOfTypeAll<FishData>()) if (!allFish.Contains(f)) allFish.Add(f);
            return allFish;
        }

        private FishData PickFish(FishHabitat where)
        {
            GameClock clock = GameClock.Instance;
            float total = 0f;
            var pool = new List<FishData>();
            foreach (FishData f in AllFish())
            {
                if (f.habitat != FishHabitat.Any && f.habitat != where) continue;
                if (clock != null && (!f.InSeason(clock.CurrentSeason) || !f.InTime(clock.CurrentHour))) continue;
                pool.Add(f);
                total += f.Weight;
            }
            float roll = Random.value * total;
            foreach (FishData f in pool) { roll -= f.Weight; if (roll <= 0f) return f; }
            return pool.Count > 0 ? pool[pool.Count - 1] : null;
        }

        private void UpdateShadows(float dt)
        {
            Vector3 pp = player.transform.position;
            for (int i = shadows.Count - 1; i >= 0; i--)
            {
                FishShadow s = shadows[i];
                if (s == null) { shadows.RemoveAt(i); continue; }
                Vector3 d = s.transform.position - pp;
                d.y = 0f;
                if (s.Gone || (d.magnitude > spawnRadius + 10f && s != hooked)) { Destroy(s.gameObject); shadows.RemoveAt(i); continue; }
                if (s != hooked) s.Wander(dt, this);
            }

            spawnTimer -= dt;
            if (spawnTimer > 0f || shadows.Count >= maxShadows) return;
            spawnTimer = 1.5f;
            for (int tries = 0; tries < 6; tries++)
            {
                Vector2 r = Random.insideUnitCircle.normalized * Random.Range(4f, spawnRadius);
                Vector3 p = new Vector3(pp.x + r.x, waterY, pp.z + r.y);
                if (!IsShoreWater(p) || TooClose(p)) continue;
                FishData f = PickFish(HabitatAt(p));
                if (f == null) return;
                shadows.Add(SpawnShadow(f, p));
                return;
            }
        }

        private bool TooClose(Vector3 p)
        {
            foreach (FishShadow s in shadows) if (s != null && (s.transform.position - p).sqrMagnitude < 9f) return true;
            return false;
        }

        private FishShadow SpawnShadow(FishData f, Vector3 p)
        {
            var go = new GameObject("Fish_" + f.itemName);
            go.transform.position = new Vector3(p.x, waterY - swimDepth, p.z);
            go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            float length = 0.35f + 0.3f * f.shadowSize; // 1 → 0.65m ~ 6 → 2.15m

            var model = new GameObject("Model").transform;
            model.SetParent(go.transform, false);
            model.localRotation = Quaternion.Euler(0f, fishModelYaw, 0f);
            if (fishModel != null)
            {
                GameObject m = Instantiate(fishModel, model, false);
                foreach (Collider c in m.GetComponentsInChildren<Collider>()) Destroy(c);
                // 헤엄치는 애니메이션 (black_fish의 'fish' 클립). 낚시 준비하기 메뉴가 모델을 Legacy 애니메이션으로 바꿔 둠
                Animation anim = m.GetComponentInChildren<Animation>();
                if (anim != null && anim.clip != null)
                {
                    anim.wrapMode = WrapMode.Loop;
                    anim.Play();
                    foreach (AnimationState st in anim) st.speed = Random.Range(0.8f, 1.2f);
                }
                if (BuildingSystem.TryGetBounds(model, out Bounds b))
                {
                    float longest = Mathf.Max(b.size.x, b.size.z, 0.001f);
                    model.localScale *= length / longest;
                    BuildingSystem.TryGetBounds(model, out b);
                    // 가운데를 맞추고, 등(맨 위)이 물 표면에서 swimDepth 아래에 오게
                    model.position += new Vector3(go.transform.position.x - b.center.x, go.transform.position.y - b.max.y, go.transform.position.z - b.center.z);
                }
            }
            else
            {
                GameObject blob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(blob.GetComponent<Collider>());
                blob.transform.SetParent(model, false);
                blob.transform.localScale = new Vector3(length * 0.38f, 0.05f, length);
                blob.GetComponent<Renderer>().sharedMaterial = ShadowMaterial();
            }
            // 물 표면 그림자 (Model 다음 두 번째 자식 - 꼬리 흔들기는 Model만)
            if (surfaceShadow)
            {
                GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "SurfaceShadow";
                Destroy(quad.GetComponent<Collider>());
                quad.transform.SetParent(go.transform, false);
                quad.transform.localPosition = new Vector3(0f, swimDepth + 0.03f, 0f); // 물 표면 바로 위
                quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                quad.transform.localScale = new Vector3(length * 0.45f * shadowScale, length * shadowScale, 1f);
                Renderer r = quad.GetComponent<Renderer>();
                r.sharedMaterial = SurfaceMaterial();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }

            FishShadow s = go.AddComponent<FishShadow>();
            s.Init(f, p, length);
            return s;
        }

        // 가장자리가 부드러운 검은 타원 (투명)
        private Material SurfaceMaterial()
        {
            if (surfaceMat != null) return surfaceMat;
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                    float d = Mathf.Sqrt(u * u + v * v);
                    tex.SetPixel(x, y, new Color(0f, 0f, 0f, Mathf.Clamp01((1f - d) * 2.2f)));
                }
            tex.Apply();
            surfaceMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex };
            surfaceMat.color = new Color(0.02f, 0.04f, 0.08f, shadowOpacity);
            surfaceMat.renderQueue = 3100; // 물 위에 그려지게
            return surfaceMat;
        }

        private Material ShadowMaterial()
        {
            if (shadowMat != null) return shadowMat;
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            shadowMat = new Material(sh != null ? sh : Shader.Find("Unlit/Color"));
            Color c = new Color(0.03f, 0.06f, 0.1f, 0.8f);
            if (shadowMat.HasProperty("_BaseColor")) shadowMat.SetColor("_BaseColor", c);
            shadowMat.color = c;
            return shadowMat;
        }

        public float WaterY => waterY;
        /// <summary>물고기가 찌를 문 순간 (지금 클릭하면 잡힘). 캐릭터 머리 위 "!" 표시(FishBiteMark)가 읽음</summary>
        public bool IsBiting { get; private set; }

        // ───────────────────────── 낚시 시작 ─────────────────────────

        private bool CanStart() => CastBlockReason() == null;

        // 지금 던질 수 없는 이유 (던질 수 있으면 null). 조건은 예전 CanStart와 똑같음
        private string CastBlockReason()
        {
            InventoryManager inv = InventoryManager.Instance;
            if (inv == null) return "InventoryManager 없음";
            if (inv.CurrentTool != ToolType.FishingRod) return $"낚싯대를 안 들고 있음 (지금 도구: {inv.CurrentTool})";
            if (UIInputBlocker.IsBlocked) return "다른 UI가 입력을 막고 있음 (UIInputBlocker)";
            if (PhoneMenu.IsOpen) return "폰 메뉴가 열려 있음";
            if (inv.IsInventoryOpen) return "인벤토리가 열려 있음";
            if (ContainerWindow.IsOpen) return "상자 창이 열려 있음";
            if (BuildingPlacer.BlocksFarming) return "건축 모드 중";
            if (!player.enabled) return "플레이어 조작이 꺼져 있음 (연출·대화 중?)";
            if (player.IsBusy) return "플레이어가 다른 행동 중 (IsBusy)";
            // 낚싯대를 들고 있을 때는 낚시가 우선 (예전엔 칸을 가리키면 농사 행동이 먼저라 낚시가 안 던져졌음)
            // 수확은 E키로 그대로 가능
            return null;
        }

        private void TryStart()
        {
            if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
            string reason = CastBlockReason();
            if (reason != null)
            {
                if (logWhyNotCasting && InventoryManager.Instance != null && InventoryManager.Instance.CurrentTool == ToolType.FishingRod)
                    Debug.Log($"[낚시] 클릭했지만 안 던짐 - 이유: {reason}");
                return;
            }
            if (!FindCastPoint(out Vector3 target))
            {
                if (logWhyNotCasting) Debug.Log($"[낚시] 클릭했지만 안 던짐 - 이유: {minCast}~{maxCast}m 안에 물을 못 찾음 (물 높이 {waterY:0.00}, 물 감지 {(waterReady ? "됨" : "안 됨")})");
                FishCatchPopup.Hint("물가를 바라보고 던져 주세요");
                return;
            }
            // 이번 클릭으로 농사 행동(잡초 뽑기·수확 등)이 같이 실행되지 않게 이 프레임만 입력을 막음
            UIInputBlocker.Block(this);
            StartCoroutine(UnblockAtEndOfFrame());
            flow = StartCoroutine(Flow(target));
        }

        private void OnDisable() => UIInputBlocker.Unblock(this); // 꺼질 때 막아 둔 입력이 남지 않게

        private IEnumerator UnblockAtEndOfFrame()
        {
            yield return new WaitForEndOfFrame();
            UIInputBlocker.Unblock(this);
        }

        // 십자선(또는 마우스)이 가리키는 물. 너무 멀거나 땅이면 바라보는 쪽 물을 찾음
        private bool FindCastPoint(out Vector3 target)
        {
            target = Vector3.zero;
            Camera cam = Camera.main;
            Vector3 pp = player.transform.position;
            Vector3 fwd = player.transform.forward;
            if (cam != null)
            {
                Ray ray = Cursor.lockState == CursorLockMode.Locked || Mouse.current == null
                    ? cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
                    : cam.ScreenPointToRay(Mouse.current.position.ReadValue());
                var plane = new Plane(Vector3.up, new Vector3(0f, waterY, 0f));
                if (plane.Raycast(ray, out float enter))
                {
                    Vector3 hit = ray.GetPoint(enter);
                    Vector3 d = hit - pp;
                    d.y = 0f;
                    if (d.sqrMagnitude > 0.01f) fwd = d.normalized;
                    if (d.magnitude >= minCast && d.magnitude <= maxCast && IsWater(hit)) { target = hit; return true; }
                }
            }
            fwd.y = 0f;
            fwd.Normalize();
            for (float dist = maxCast; dist >= minCast; dist -= 0.5f)
            {
                Vector3 p = pp + fwd * dist;
                if (IsWater(p)) { target = p; return true; }
            }
            return false;
        }

        // ───────────────────────── 낚시 흐름 ─────────────────────────

        private IEnumerator Flow(Vector3 target)
        {
            fishing = true;
            hooked = null;
            castTo = new Vector3(target.x, waterY, target.z);
            player.FaceTowards(target);
            playerWasEnabled = player.enabled;
            player.enabled = false; // 낚시 중엔 제자리
            bool animated = Animated;

            // 1. 꺼내서 던지기
            if (animated)
            {
                anim.BeginExternal();
                yield return anim.PlayOnce(anim.equip);
                fx.SetCastTarget(castTo + Vector3.up * 0.02f);
                anim.Play(anim.cast);
                GameAudio.PlayAt(SoundId.FishCast, player.transform.position);
                float until = Time.time + anim.cast.length + fx.castFlightTime + 0.5f;
                while (!fx.BobberInWater && Time.time < until) yield return null; // CastRelease 이벤트 → 찌가 날아가 물에 떨어짐
                if (fx.BobberInWater) GameAudio.PlayAt(SoundId.BobberPlop, castTo);
                while (!anim.CurrentClipDone(anim.cast)) yield return null;
                anim.Play(anim.waitLoop);
            }
            else
            {
                EnsureGear();
                bobber.gameObject.SetActive(true);
                line.enabled = true;
                Vector3 from = RodTip();
                GameAudio.PlayAt(SoundId.FishCast, player.transform.position);
                for (float t = 0f; t < 0.45f; t += Time.deltaTime)
                {
                    float k = t / 0.45f;
                    Vector3 p = Vector3.Lerp(from, castTo, k);
                    p.y += Mathf.Sin(k * Mathf.PI) * 1.2f;
                    bobber.position = p;
                    yield return null;
                }
                Ripple.Spawn(castTo, 0.5f);
                GameAudio.PlayAt(SoundId.BobberPlop, castTo);
                dip = 0f;
                simpleBobberFloating = true;
            }

            // 2. 기다리기 → 다가옴 → 툭툭 → 쑥!
            Outcome result = Outcome.Cancel;
            int phase = 0;              // 0 = 물고기 찾는 중, 1 = 다가오는 중, 2 = 툭툭 건드리는 중
            float wait = Random.Range(1f, 2.5f), noFish = 0f, nibbleTimer = 0f;
            int nibbles = 0;
            bool hinted = false;
            while (true)
            {
                yield return null;
                float dt = Time.deltaTime;
                if (CancelPressed()) { result = Outcome.Cancel; break; }
                bool click = ClickPressed();

                if (phase == 0)
                {
                    if (click) { result = Outcome.Cancel; break; } // 아무것도 없을 때 누르면 그냥 거둠
                    noFish += dt;
                    if (!hinted && noFish > 6f) { hinted = true; FishCatchPopup.Hint("물고기 그림자 가까이에 던져 보세요"); }
                    wait -= dt;
                    if (wait > 0f) continue;
                    hooked = NearestShadow();
                    if (hooked == null) { wait = 1f; continue; }
                    phase = 1;
                }
                else if (phase == 1)
                {
                    if (hooked == null) { phase = 0; wait = 1f; continue; }
                    if (click) { result = Outcome.Early; break; }
                    Vector3 front = castTo + (Flat(hooked.transform.position) - Flat(castTo)).normalized * hooked.Length * 0.5f;
                    if (hooked.SwimTo(front, castTo, 1.1f, dt, waterY - swimDepth))
                    {
                        phase = 2;
                        nibbles = hooked.Data.rarity >= FishRarity.Rare ? Random.Range(2, 6) : Random.Range(1, 4);
                        nibbleTimer = Random.Range(0.8f, 1.6f);
                    }
                }
                else
                {
                    if (hooked == null) { result = Outcome.Cancel; break; }
                    if (click) { result = Outcome.Early; break; }
                    nibbleTimer -= dt;
                    if (nibbleTimer > 0f) continue;
                    if (nibbles > 0)
                    {
                        nibbles--;
                        hooked.Lunge();
                        if (animated) fx.Nibble(); else dip = 0.07f; // 툭
                        GameAudio.PlayAt(SoundId.FishNibble, castTo);
                        nibbleTimer = Random.Range(0.9f, 1.8f);
                        continue;
                    }

                    // 쑥! → 이 시간 안에 눌러야 함
                    hooked.Lunge();
                    if (animated) anim.Play(anim.bite); // Bite 이벤트 → 찌가 쏙 + 물보라
                    else { dip = 0.32f; Ripple.Spawn(castTo, 0.9f); }
                    GameAudio.PlayAt(SoundId.FishBite, castTo);
                    float window = hooked.Data.biteWindow;
                    result = Outcome.Missed;
                    IsBiting = true; // 머리 위 "!"
                    while (window > 0f)
                    {
                        yield return null;
                        if (ClickPressed()) { result = Outcome.Caught; break; }
                        if (CancelPressed()) break;
                        window -= Time.deltaTime;
                    }
                    IsBiting = false;
                    break;
                }
            }

            // 3. 결과
            if (result == Outcome.Caught && hooked != null)
            {
                FishData fish = hooked.Data;
                shadows.Remove(hooked);
                Destroy(hooked.gameObject);
                hooked = null;
                SpendRod();
                GameAudio.PlayAt(SoundId.FishSplash, castTo);
                bool isNew = FishCollection.Register(fish);
                int notAdded = InventoryManager.Instance != null ? InventoryManager.Instance.AddItem(fish, 1) : 1;
                Debug.Log($"[낚시] {fish.itemName} 잡음{(notAdded > 0 ? " (가방이 가득 차서 놓아줌)" : "")}");
                if (animated)
                {
                    if (anim.reelSuccess != null) yield return anim.PlayOnce(anim.reelSuccess); // Hook·FishOut → 물고기가 튀어 올라 매달림
                    FishCatchPopup.Show(fish, isNew, notAdded > 0);
                    if (anim.celebrate != null) yield return anim.PlayOnce(anim.celebrate);
                }
                else
                {
                    Ripple.Spawn(castTo, 1.1f);
                    FishCatchPopup.Show(fish, isNew, notAdded > 0);
                }
            }
            else
            {
                if (hooked != null) { hooked.Flee(); hooked = null; }
                if (result == Outcome.Missed || result == Outcome.Early) GameAudio.PlayAt(SoundId.FishEscape, castTo);
                if (result == Outcome.Missed) { SpendRod(); FishCatchPopup.Hint("놓쳤어요… 물고기가 도망갔어요"); }
                else if (result == Outcome.Early) FishCatchPopup.Hint("너무 빨랐어요… 물고기가 도망갔어요");
                if (animated && (result == Outcome.Missed || result == Outcome.Early) && anim.fail != null)
                {
                    if (result == Outcome.Missed) while (!anim.CurrentClipDone(anim.bite)) yield return null;
                    yield return anim.PlayOnce(anim.fail); // LineSlack → 줄이 풀리며 찌가 돌아옴
                }
            }

            // 4. 거두고 낚싯대 넣기
            if (animated)
            {
                if (anim.unequip != null) yield return anim.PlayOnce(anim.unequip); // RodDespawn → 찌·줄 숨김
                fx.ResetAll();
                anim.EndExternal();
            }
            else
            {
                simpleBobberFloating = false;
                Vector3 from = bobber.position;
                for (float t = 0f; t < 0.3f; t += Time.deltaTime)
                {
                    bobber.position = Vector3.Lerp(from, RodTip(), t / 0.3f);
                    yield return null;
                }
                bobber.gameObject.SetActive(false);
                line.enabled = false;
            }
            RestorePlayer();
            fishing = false;
            flow = null;
        }

        // 시작 화면으로 돌아갈 때 등 강제로 끝내기
        private void Abort()
        {
            if (flow != null) StopCoroutine(flow);
            flow = null;
            if (hooked != null) { hooked.Flee(); hooked = null; }
            if (anim != null) anim.EndExternal();
            if (fx != null) fx.ResetAll();
            simpleBobberFloating = false;
            if (bobber != null) bobber.gameObject.SetActive(false);
            if (line != null) line.enabled = false;
            RestorePlayer();
            fishing = false;
        }

        private FishShadow NearestShadow()
        {
            FishShadow best = null;
            float bestD = noticeRange;
            foreach (FishShadow s in shadows)
            {
                if (s == null || s.Fleeing) continue;
                float d = Vector3.Distance(Flat(s.transform.position), Flat(castTo));
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        private bool CancelPressed()
        {
            if (PhoneMenu.IsOpen) return false;
            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame) return true;
            Keyboard kb = Keyboard.current;
            return kb != null && (kb.wKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame);
        }

        private bool ClickPressed() => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && !PhoneMenu.IsOpen && !UIInputBlocker.IsBlocked;

        private void SpendRod()
        {
            if (InventoryManager.Instance != null && InventoryManager.Instance.CurrentTool == ToolType.FishingRod)
                InventoryManager.Instance.UseCurrentTool();
        }

        private void RestorePlayer()
        {
            if (player != null && playerWasEnabled) player.enabled = true;
            playerWasEnabled = false;
        }

        // ───────────────────────── 간단한 찌·줄 (동작이 없을 때) ─────────────────────────

        private Vector3 RodTip() => player.transform.position + Vector3.up * 1.5f + player.transform.forward * 0.7f;

        private void EnsureGear()
        {
            if (bobber != null) return;
            bobber = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
            bobber.name = "FishingBobber";
            Destroy(bobber.GetComponent<Collider>());
            bobber.localScale = Vector3.one * 0.14f;
            bobber.GetComponent<Renderer>().sharedMaterial = SolidMaterial(new Color(0.9f, 0.2f, 0.2f));
            Transform bottom = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
            Destroy(bottom.GetComponent<Collider>());
            bottom.SetParent(bobber, false);
            bottom.localPosition = new Vector3(0f, -0.35f, 0f);
            bottom.localScale = new Vector3(0.9f, 0.6f, 0.9f);
            bottom.GetComponent<Renderer>().sharedMaterial = SolidMaterial(Color.white);
            bobber.gameObject.SetActive(false);

            line = new GameObject("FishingLine").AddComponent<LineRenderer>();
            line.positionCount = 12;
            line.widthMultiplier = 0.012f;
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.startColor = line.endColor = new Color(1f, 1f, 1f, 0.85f);
            line.enabled = false;
        }

        private static Material SolidMaterial(Color c)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            var m = new Material(sh != null ? sh : Shader.Find("Standard"));
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            m.color = c;
            return m;
        }

        private void UpdateSimpleBobber()
        {
            if (bobber == null || !bobber.gameObject.activeSelf) return;
            if (simpleBobberFloating)
            {
                dip = Mathf.MoveTowards(dip, 0f, Time.deltaTime * 0.6f);
                float bob = Mathf.Sin(Time.time * 2.4f) * 0.02f;
                bobber.position = new Vector3(castTo.x, waterY + 0.03f + bob - dip, castTo.z);
            }
            Vector3 a = RodTip(), b = bobber.position;
            float sag = Mathf.Min(0.6f, Vector3.Distance(a, b) * 0.08f);
            for (int i = 0; i < line.positionCount; i++)
            {
                float t = i / (float)(line.positionCount - 1);
                Vector3 p = Vector3.Lerp(a, b, t);
                p.y -= Mathf.Sin(t * Mathf.PI) * sag;
                line.SetPosition(i, p);
            }
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }

    // 물고기 그림자 하나 (헤엄치기·다가오기·툭 건드리기·도망)
    public class FishShadow : MonoBehaviour
    {
        public FishData Data { get; private set; }
        public float Length { get; private set; }
        public bool Fleeing { get; private set; }
        public bool Gone { get; private set; }

        private Vector3 home, target;
        private float wait, speed, lunge, fleeTime;

        public void Init(FishData data, Vector3 homePos, float length)
        {
            Data = data;
            Length = length;
            home = homePos;
            target = homePos;
            speed = Random.Range(0.35f, 0.7f);
            wait = Random.Range(0f, 1.5f);
            phase = Random.Range(0f, 10f);
        }

        public void Wander(float dt, FishingManager m)
        {
            if (Fleeing)
            {
                fleeTime += dt;
                transform.position += transform.forward * 3f * dt;
                if (fleeTime > 1.5f) Gone = true;
                return;
            }
            if (wait > 0f) { wait -= dt; Wiggle(dt, 0.3f); return; }
            if (SwimTo(target, target, speed, dt, transform.position.y))
            {
                wait = Random.Range(0.6f, 2.2f);
                for (int i = 0; i < 6; i++)
                {
                    Vector2 r = Random.insideUnitCircle * 2.5f;
                    Vector3 p = new Vector3(home.x + r.x, m.WaterY, home.z + r.y);
                    if (m.IsWater(p)) { target = p; break; }
                }
            }
        }

        /// <summary>to 쪽으로 헤엄치고 lookAt을 바라봄. 도착하면 true</summary>
        public bool SwimTo(Vector3 to, Vector3 lookAt, float spd, float dt, float y)
        {
            Vector3 pos = transform.position;
            Vector3 d = new Vector3(to.x - pos.x, 0f, to.z - pos.z);
            float dist = d.magnitude;
            Vector3 look = new Vector3(lookAt.x - pos.x, 0f, lookAt.z - pos.z);
            if (look.sqrMagnitude > 0.0004f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look), 1f - Mathf.Exp(-5f * dt));
            if (dist < 0.08f) { Wiggle(dt, 0.5f); return true; }
            transform.position = new Vector3(pos.x, y, pos.z) + d / dist * Mathf.Min(dist, spd * dt);
            Wiggle(dt, 1f);
            return false;
        }

        /// <summary>찌를 툭 건드림 (앞으로 살짝 튀었다 돌아옴)</summary>
        public void Lunge() => lunge = 1f;

        public void Flee()
        {
            Fleeing = true;
            transform.rotation = Quaternion.LookRotation(-transform.forward);
        }

        // 꼬리 흔들기 + 툭 튀기 (모델의 처음 자세를 기준으로)
        private bool baseKnown;
        private float phase; // 물고기마다 꼬리 흔드는 박자를 다르게 (Init에서 정함)
        private float baseYaw;
        private Vector3 basePos;

        private void Wiggle(float dt, float amount)
        {
            if (transform.childCount == 0) return;
            Transform model = transform.GetChild(0);
            if (!baseKnown) { baseKnown = true; baseYaw = model.localEulerAngles.y; basePos = model.localPosition; }
            float yaw = Mathf.Sin(Time.time * 7f + phase) * 6f * amount;
            model.localRotation = Quaternion.Euler(0f, baseYaw + yaw, 0f);
            lunge = Mathf.MoveTowards(lunge, 0f, dt * 4f);
            model.localPosition = basePos + Vector3.forward * (Mathf.Sin(lunge * Mathf.PI) * 0.15f);
        }
    }

    // 물 위에 퍼지는 동그란 물결 (던질 때·물었을 때)
    public class Ripple : MonoBehaviour
    {
        private LineRenderer lr;
        private float t, size;

        public static void Spawn(Vector3 at, float size)
        {
            var go = new GameObject("Ripple");
            go.transform.position = at + Vector3.up * 0.02f;
            Ripple r = go.AddComponent<Ripple>();
            r.size = size;
            r.lr = go.AddComponent<LineRenderer>();
            r.lr.positionCount = 32;
            r.lr.loop = true;
            r.lr.useWorldSpace = false;
            r.lr.widthMultiplier = 0.04f;
            r.lr.material = new Material(Shader.Find("Sprites/Default"));
        }

        private void Update()
        {
            t += Time.deltaTime / 0.8f;
            if (t >= 1f) { Destroy(gameObject); return; }
            float rad = Mathf.Lerp(0.1f, size, t);
            for (int i = 0; i < 32; i++)
            {
                float a = i / 32f * Mathf.PI * 2f;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a) * rad, 0f, Mathf.Sin(a) * rad));
            }
            Color c = new Color(1f, 1f, 1f, 0.8f * (1f - t));
            lr.startColor = lr.endColor = c;
        }
    }
}