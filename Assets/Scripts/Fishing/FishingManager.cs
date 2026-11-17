using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using FarmingSystem.Building;
using FarmingSystem.Core;
using FarmingSystem.Inventory;
using FarmingSystem.Player;
using FarmingSystem.Save;
using FarmingSystem.UI;

namespace FarmingSystem.Fishing
{
    // 낚시 (모동숲 방식)
    //  1. 물가에 물고기 그림자가 헤엄침 (플레이어 주변 물가에 몇 마리씩, 지금 계절·시간·장소에 맞는 물고기)
    //  2. 낚싯대를 들고 물 쪽을 보며 좌클릭 → 찌를 던짐 (십자선이 가리키는 물, 너무 멀면 앞쪽으로)
    //  3. 찌 근처(4m 안)의 물고기가 다가와서 몇 번 툭툭 건드림 (이때 누르면 놀라서 도망)
    //  4. 찌가 '쑥' 들어가면 그때 좌클릭 → 잡음! (물고기마다 누를 수 있는 시간이 다름)
    //  5. 잡으면 그림·이름·설명 창 → 가방에 들어가고 도감에 등록
    //  - 우클릭이나 이동 키 = 찌를 거둠 / 낚싯대 내구도는 물고기가 물었을 때(잡든 놓치든) 1 줄어듦
    // 설치: Tools → 행성농사일지 → 물고기 → 낚시 준비하기 (씬에 이 컴포넌트 + 낚싯대 + 상점)
    //       씬에 없으면 게임이 시작될 때 기본 설정으로 저절로 생김 (그림자는 검은 타원)
    // 플레이어 애니메이터에 아래 이름의 파라미터가 있으면 같이 움직임 (없으면 무시)
    //  Trigger "FishCast"(던지기), "FishReel"(거두기), "FishCatch"(잡음) / Bool "Fishing"(낚시 중)
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
        [Tooltip("찌 근처 이 거리 안의 물고기가 다가옴 (m)")]
        [SerializeField] private float noticeRange = 4f;

        private enum State { Idle, Casting, Waiting, Approaching, Nibbling, Biting, Reeling }
        private State state = State.Idle;
        private float stateTimer;

        private float waterY;
        private bool waterReady;
        private readonly List<FishShadow> shadows = new List<FishShadow>();
        private float spawnTimer;
        private List<FishData> allFish;

        private PlayerController player;
        private Animator playerAnim;
        private bool playerWasEnabled;
        private FarmActionController farmActions;

        private Transform bobber;
        private LineRenderer line;
        private Vector3 castFrom, castTo;
        private FishShadow hooked;
        private int nibblesLeft;
        private float dip;           // 찌가 물에 잠긴 정도 (연출)
        private float noFishTimer;
        private Material shadowMat;

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
            if (player == null) { player = FindAnyObjectByType<PlayerController>(); if (player != null) playerAnim = player.GetComponentInChildren<Animator>(); }
            if (farmActions == null) farmActions = FindAnyObjectByType<FarmActionController>();
            if (player == null || !waterReady) return;

            UpdateShadows(Time.deltaTime);
            if (TitleScreen.IsShowing) { if (state != State.Idle) EndFishing(false); return; }

            switch (state)
            {
                case State.Idle: UpdateIdle(); break;
                case State.Casting: UpdateCasting(); break;
                case State.Waiting: UpdateWaiting(); break;
                case State.Approaching: UpdateApproach(); break;
                case State.Nibbling: UpdateNibble(); break;
                case State.Biting: UpdateBite(); break;
                case State.Reeling: UpdateReel(); break;
            }
            UpdateBobberAndLine();
        }

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
            FishShadow s = go.AddComponent<FishShadow>();
            s.Init(f, p, length);
            return s;
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

        // ───────────────────────── 던지기 ─────────────────────────

        private bool CanStart()
        {
            InventoryManager inv = InventoryManager.Instance;
            if (inv == null || inv.CurrentTool != ToolType.FishingRod) return false;
            if (UIInputBlocker.IsBlocked || PhoneMenu.IsOpen || inv.IsInventoryOpen || ContainerWindow.IsOpen || BuildingPlacer.BlocksFarming) return false;
            if (!player.enabled || player.IsBusy) return false;
            if (farmActions != null && farmActions.HoveredAction != FarmActionType.None) return false; // 수확 등 농사 행동이 먼저
            return true;
        }

        private void UpdateIdle()
        {
            if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame || !CanStart()) return;
            if (!FindCastPoint(out Vector3 target)) { FishCatchPopup.Hint("물가를 바라보고 던져 주세요"); return; }

            player.FaceTowards(target);
            playerWasEnabled = player.enabled;
            player.enabled = false; // 낚시 중엔 제자리
            SetAnim("FishCast", true);
            EnsureGear();
            castFrom = RodTip();
            castTo = new Vector3(target.x, waterY, target.z);
            bobber.gameObject.SetActive(true);
            line.enabled = true;
            hooked = null;
            dip = 0f;
            state = State.Casting;
            stateTimer = 0f;
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

        private void UpdateCasting()
        {
            stateTimer += Time.deltaTime;
            float k = Mathf.Clamp01(stateTimer / 0.45f);
            Vector3 p = Vector3.Lerp(castFrom, castTo, k);
            p.y += Mathf.Sin(k * Mathf.PI) * 1.2f;
            bobber.position = p;
            if (k < 1f) return;
            Ripple.Spawn(castTo, 0.5f);
            state = State.Waiting;
            stateTimer = Random.Range(1f, 2.5f); // 물고기가 알아채기까지
            noFishTimer = 0f;
        }

        private bool CancelPressed()
        {
            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame) return true;
            Keyboard kb = Keyboard.current;
            return kb != null && (kb.wKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame);
        }

        private bool ClickPressed() => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && !PhoneMenu.IsOpen && !UIInputBlocker.IsBlocked;

        private void UpdateWaiting()
        {
            if (CancelPressed() || ClickPressed()) { StartReel(); return; }
            stateTimer -= Time.deltaTime;
            noFishTimer += Time.deltaTime;
            if (noFishTimer > 6f && noFishTimer - Time.deltaTime <= 6f) FishCatchPopup.Hint("물고기 그림자 가까이에 던져 보세요");
            if (stateTimer > 0f) return;

            // 찌 근처에서 가장 가까운 물고기
            FishShadow best = null;
            float bestD = noticeRange;
            foreach (FishShadow s in shadows)
            {
                if (s == null || s.Fleeing) continue;
                float d = Vector3.Distance(Flat(s.transform.position), Flat(castTo));
                if (d < bestD) { bestD = d; best = s; }
            }
            if (best == null) { stateTimer = 1f; return; }
            hooked = best;
            state = State.Approaching;
        }

        private void UpdateApproach()
        {
            if (hooked == null) { state = State.Waiting; stateTimer = 1f; return; }
            if (CancelPressed()) { StartReel(); return; }
            if (ClickPressed()) { Scare("너무 빨랐어요… 물고기가 도망갔어요"); return; }
            Vector3 front = castTo + (Flat(hooked.transform.position) - Flat(castTo)).normalized * hooked.Length * 0.5f;
            if (hooked.SwimTo(front, castTo, 1.1f, Time.deltaTime, waterY - swimDepth))
            {
                state = State.Nibbling;
                nibblesLeft = hooked.Data.rarity >= FishRarity.Rare ? Random.Range(2, 6) : Random.Range(1, 4);
                stateTimer = Random.Range(0.8f, 1.6f);
            }
        }

        private void UpdateNibble()
        {
            if (hooked == null) { StartReel(); return; }
            if (CancelPressed()) { StartReel(); return; }
            if (ClickPressed()) { Scare("너무 빨랐어요… 물고기가 도망갔어요"); return; }
            stateTimer -= Time.deltaTime;
            if (stateTimer > 0f) return;
            if (nibblesLeft > 0)
            {
                nibblesLeft--;
                dip = 0.07f; // 툭
                hooked.Lunge();
                stateTimer = Random.Range(0.9f, 1.8f);
                return;
            }
            // 쑥!
            state = State.Biting;
            stateTimer = hooked.Data.biteWindow;
            dip = 0.32f;
            hooked.Lunge();
            Ripple.Spawn(castTo, 0.9f);
        }

        private void UpdateBite()
        {
            if (hooked == null) { StartReel(); return; }
            dip = 0.32f;
            if (ClickPressed()) { Catch(); return; }
            stateTimer -= Time.deltaTime;
            if (CancelPressed() || stateTimer <= 0f)
            {
                SpendRod();
                Scare("놓쳤어요… 물고기가 도망갔어요");
            }
        }

        private void Catch()
        {
            FishData fish = hooked.Data;
            shadows.Remove(hooked);
            Destroy(hooked.gameObject);
            hooked = null;
            SetAnim("FishCatch", false);
            Ripple.Spawn(castTo, 1.1f);
            SpendRod();

            bool isNew = FishCollection.Register(fish);
            int notAdded = InventoryManager.Instance != null ? InventoryManager.Instance.AddItem(fish, 1) : 1;
            FishCatchPopup.Show(fish, isNew, notAdded > 0);
            Debug.Log($"[낚시] {fish.itemName} 잡음{(notAdded > 0 ? " (가방이 가득 차서 놓아줌)" : "")}");
            StartReel();
        }

        private void Scare(string message)
        {
            if (hooked != null) hooked.Flee();
            hooked = null;
            FishCatchPopup.Hint(message);
            StartReel();
        }

        private void SpendRod()
        {
            if (InventoryManager.Instance != null && InventoryManager.Instance.CurrentTool == ToolType.FishingRod)
                InventoryManager.Instance.UseCurrentTool();
        }

        private void StartReel()
        {
            if (hooked != null) { hooked.Flee(); hooked = null; }
            SetAnim("FishReel", false);
            state = State.Reeling;
            stateTimer = 0f;
            castFrom = bobber != null ? bobber.position : castTo;
        }

        private void UpdateReel()
        {
            stateTimer += Time.deltaTime;
            float k = Mathf.Clamp01(stateTimer / 0.3f);
            bobber.position = Vector3.Lerp(castFrom, RodTip(), k);
            if (k >= 1f) EndFishing(true);
        }

        private void EndFishing(bool restore)
        {
            state = State.Idle;
            if (bobber != null) bobber.gameObject.SetActive(false);
            if (line != null) line.enabled = false;
            RestorePlayer();
        }

        private void RestorePlayer()
        {
            if (player != null && playerWasEnabled) player.enabled = true;
            playerWasEnabled = false;
            SetBool("Fishing", false);
        }

        // ───────────────────────── 찌·낚싯줄 ─────────────────────────

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

        private void UpdateBobberAndLine()
        {
            if (bobber == null || !bobber.gameObject.activeSelf) return;
            if (state == State.Waiting || state == State.Approaching || state == State.Nibbling || state == State.Biting)
            {
                dip = Mathf.MoveTowards(dip, state == State.Biting ? 0.32f : 0f, Time.deltaTime * 0.6f);
                float bob = Mathf.Sin(Time.time * 2.4f) * 0.02f;
                bobber.position = new Vector3(castTo.x, waterY + 0.03f + bob - dip, castTo.z);
            }
            Vector3 a = RodTip(), b = bobber.position;
            float sag = state == State.Casting ? 0.1f : Mathf.Min(0.6f, Vector3.Distance(a, b) * 0.08f);
            for (int i = 0; i < line.positionCount; i++)
            {
                float t = i / (float)(line.positionCount - 1);
                Vector3 p = Vector3.Lerp(a, b, t);
                p.y -= Mathf.Sin(t * Mathf.PI) * sag;
                line.SetPosition(i, p);
            }
        }

        // ───────────────────────── 애니메이션 (있으면) ─────────────────────────

        private void SetAnim(string trigger, bool fishing)
        {
            SetBool("Fishing", fishing || state != State.Idle);
            if (playerAnim == null || !HasParam(trigger, AnimatorControllerParameterType.Trigger)) return;
            playerAnim.SetTrigger(trigger);
        }

        private void SetBool(string name, bool v)
        {
            if (playerAnim != null && HasParam(name, AnimatorControllerParameterType.Bool)) playerAnim.SetBool(name, v);
        }

        private bool HasParam(string name, AnimatorControllerParameterType type)
        {
            if (playerAnim == null || playerAnim.runtimeAnimatorController == null) return false;
            foreach (AnimatorControllerParameter p in playerAnim.parameters) if (p.name == name && p.type == type) return true;
            return false;
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