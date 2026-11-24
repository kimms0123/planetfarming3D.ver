using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using FarmingSystem.Audio;
using FarmingSystem.Building;
using FarmingSystem.Core;
using FarmingSystem.Farming;
using FarmingSystem.Inventory;
using FarmingSystem.NPC;
using FarmingSystem.Player;
using FarmingSystem.Save;
using FarmingSystem.UI;

namespace FarmingSystem.Gathering
{
    // 나무 베기 (밭 넓히기용)
    // - 도끼(AxeData)를 들고 나무를 좌클릭 → 몇 번 내리치면 쓰러지고 목재를 얻음
    // - 평평한 땅의 나무만 (언덕 나무는 어차피 밭을 못 가니까) - Flat Trees Only로 끄고 켬
    // - 벤 자리는 바로 밭으로 갈 수 있음 (나무 충돌체가 사라지니까)
    // - 벤 나무는 저장됨. Regrow Days일이 지나면 아침에 그 자리에 다시 자람
    //   (그 칸을 갈았거나 작물·잡초·건물이 있으면 자라지 않고 기다림)
    // - 저장은 세이브 파일 옆 trees_N.json에 따로 함 → SaveManager·SaveData는 안 건드림
    // 팀 파일(InventoryManager·FarmActionController·PlayerController·IslandMapGenerator)은 안 건드림:
    //   나무는 맵 생성기가 만든 "Tree_x_z" 이름으로 찾고, 도끼는 Tool Type = None인 도구라서 농사 동작과 안 겹침
    // 붙이는 곳: 씬의 빈 오브젝트 (Tools → 행성농사일지 → 나무 베기 → 도끼·목재 만들기가 만들어 줌)
    [DefaultExecutionOrder(-90)] // 농사 클릭보다 먼저 → 나무를 친 클릭이 밭 동작으로 같이 안 들어가게
    public class TreeChopper : MonoBehaviour
    {
        public static TreeChopper Instance { get; private set; }

        [Header("목재")]
        [Tooltip("나무를 베면 얻는 아이템 (목재)")]
        [SerializeField] private ItemData wood;
        [Tooltip("나무 한 그루에서 나오는 목재 수 (최소~최대)")]
        [SerializeField] private Vector2Int woodPerTree = new Vector2Int(2, 4);

        [Header("베기")]
        [Tooltip("플레이어에서 나무 밑동까지 이 거리(m) 안이면 벨 수 있음")]
        [SerializeField] private float reach = 2.6f;
        [Tooltip("한 번 휘두르고 다음 휘두르기까지 최소 간격 (초)")]
        [SerializeField] private float swingInterval = 0.45f;
        [Tooltip("이 시간(초) 동안 안 치면 친 횟수가 처음부터 다시")]
        [SerializeField] private float hitResetSeconds = 6f;
        [Tooltip("켜면 평평한 땅의 나무만 벨 수 있음 (언덕 나무는 안 됨)")]
        [SerializeField] private bool flatTreesOnly = true;
        [Tooltip("맵 생성기가 나무에 붙이는 이름 앞부분")]
        [SerializeField] private string treeNamePrefix = "Tree_";

        [Header("다시 자라기")]
        [Tooltip("벤 뒤 며칠이 지나면 아침에 다시 자라는지")]
        [SerializeField, Min(1)] private int regrowDays = 5;

        [Header("디버그")]
        [Tooltip("도끼를 휘둘렀는데 안 베일 때 Console에 이유 남기기")]
        [SerializeField] private bool logWhyNot = true;

        [Serializable] private class ChoppedTree { public string name; public int day; }
        [Serializable] private class TreeSaveFile { public List<ChoppedTree> chopped = new List<ChoppedTree>(); }

        private readonly Dictionary<string, ChoppedTree> chopped = new Dictionary<string, ChoppedTree>();
        private readonly Dictionary<string, Transform> treeByName = new Dictionary<string, Transform>();
        private readonly List<string> pendingHide = new List<string>();
        private readonly HashSet<Transform> animating = new HashSet<Transform>();
        private float pendingUntil;
        private float nextIndexScan;

        private PlayerController player;
        private Transform hitTree;
        private int hits;
        private float lastHitTime, nextSwing, nextHint;

        private SaveManager savedSub;
        private GameClock clockSub;
        private int lastSlot = -1;

        /// <summary>지금 벤 상태인 나무 수</summary>
        public int ChoppedCount => chopped.Count;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (savedSub != null) savedSub.OnSaved -= WriteFile;
            if (clockSub != null) clockSub.OnDayChanged -= HandleDayChanged;
            UIInputBlocker.Unblock(this);
        }

        private void OnDisable() => UIInputBlocker.Unblock(this);

        private void Update()
        {
            Subscribe();
            CheckSlotLoaded();
            ResolvePending();
            TryChop();
        }

        private void Subscribe()
        {
            if (SaveManager.Instance != null && savedSub != SaveManager.Instance)
            {
                if (savedSub != null) savedSub.OnSaved -= WriteFile;
                savedSub = SaveManager.Instance;
                savedSub.OnSaved += WriteFile;
            }
            if (GameClock.Instance != null && clockSub != GameClock.Instance)
            {
                if (clockSub != null) clockSub.OnDayChanged -= HandleDayChanged;
                clockSub = GameClock.Instance;
                clockSub.OnDayChanged += HandleDayChanged;
            }
            if (player == null) player = FindAnyObjectByType<PlayerController>();
        }

        private static int Today => GameClock.Instance != null ? GameClock.Instance.CurrentDay : 0;

        // ───────────────────────── 베기 ─────────────────────────

        private void TryChop()
        {
            if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
            InventoryManager inv = InventoryManager.Instance;
            AxeData axe = inv != null ? inv.CurrentToolData as AxeData : null;
            if (axe == null) return;

            string why = BlockReason(inv);
            if (why != null) { if (logWhyNot) Debug.Log($"[나무] 휘둘렀지만 안 벰 - 이유: {why}"); return; }
            if (Time.time < nextSwing) return;

            Transform tree = AimedTree(out float dist);
            if (tree == null) { Hint("나무를 가리키고 휘둘러 주세요"); return; }
            if (dist > reach) { Hint("나무에 조금 더 가까이 가 주세요"); return; }
            if (flatTreesOnly && FarmTile.IsHillHeight(tree.position.y + 0.05f))
            {
                Hint("언덕 위 나무는 벨 수 없어요");
                if (logWhyNot) Debug.Log($"[나무] {tree.name}: 언덕 나무 (밑동 높이 {tree.position.y:0.0}m, 평지 {FarmTile.BaseGroundHeight:0.0}m)");
                return;
            }

            if (tree != hitTree || Time.time - lastHitTime > hitResetSeconds) { hitTree = tree; hits = 0; }
            int need = Mathf.Max(1, axe.hitsPerTree);
            bool last = hits + 1 >= need;
            int amount = wood != null ? UnityEngine.Random.Range(Mathf.Min(woodPerTree.x, woodPerTree.y), Mathf.Max(woodPerTree.x, woodPerTree.y) + 1) : 0;
            if (last && wood != null && amount > 0 && inv.GetAddableAmount(wood) < amount)
            {
                Hint("가방이 꽉 차서 목재를 담을 수 없어요");
                return;
            }

            // 이번 클릭이 밭 동작(수확·잡초 등)으로 같이 들어가지 않게 이 프레임만 입력을 막음
            UIInputBlocker.Block(this);
            StartCoroutine(UnblockAtEndOfFrame());

            hits++;
            lastHitTime = Time.time;
            nextSwing = Time.time + swingInterval;
            Vector3 dir = tree.position - (player != null ? player.transform.position : tree.position - Vector3.forward);
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.forward;
            if (player != null) player.FaceTowards(tree.position);
            PlaySwing(); // 내리치는 동작 (도끼 동작이 없으면 밭 갈기 동작을 같이 씀)
            GameAudio.PlayAt(SoundId.Till, tree.position);

            if (!last)
            {
                if (!animating.Contains(tree)) StartCoroutine(Shake(tree, dir));
                return;
            }

            hitTree = null;
            hits = 0;
            chopped[tree.name] = new ChoppedTree { name = tree.name, day = Today };
            treeByName[tree.name] = tree;
            if (wood != null && amount > 0) inv.AddItem(wood, amount);
            inv.UseCurrentTool(); // 내구도가 있는 도끼면 닳음 (0이면 안 닳음)
            Debug.Log($"[나무] {tree.name} 벰 → 목재 {amount}개 (Day {Today}, {regrowDays}일 뒤부터 다시 자람)");
            StartCoroutine(FallAndHide(tree, dir));
        }

        private string BlockReason(InventoryManager inv)
        {
            if (UIInputBlocker.IsBlocked) return "다른 UI가 입력을 막고 있음";
            if (PhoneMenu.IsOpen) return "폰 메뉴가 열려 있음";
            if (inv.IsInventoryOpen) return "인벤토리가 열려 있음";
            if (ContainerWindow.IsOpen) return "상자 창이 열려 있음";
            if (BuildingPlacer.BlocksFarming) return "건축 모드 중";
            if (player == null) return "플레이어를 못 찾음";
            if (!player.enabled) return "플레이어 조작이 꺼져 있음 (연출·대화·낚시 중?)";
            if (player.IsBusy) return "다른 동작 중";
            if (Cursor.lockState != CursorLockMode.Locked && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return "마우스가 UI 위에 있음";
            return null;
        }

        // 십자선(또는 마우스)이 가리키는 나무. 땅을 가리키면 그 근처 나무, 그것도 없으면 바로 앞 나무
        private Transform AimedTree(out float dist)
        {
            dist = float.MaxValue;
            if (player == null) return null;
            Vector3 pp = player.transform.position;
            Transform found = null;

            Camera cam = Camera.main;
            if (cam != null)
            {
                Ray ray = Cursor.lockState == CursorLockMode.Locked || Mouse.current == null
                    ? cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
                    : cam.ScreenPointToRay(Mouse.current.position.ReadValue());
                RaycastHit[] hitsArr = Physics.RaycastAll(ray, 60f, ~0, QueryTriggerInteraction.Ignore);
                Array.Sort(hitsArr, (a, b) => a.distance.CompareTo(b.distance));
                foreach (RaycastHit h in hitsArr)
                {
                    if (h.collider.transform.IsChildOf(player.transform)) continue;
                    found = FindTree(h.collider.transform);
                    if (found == null) found = NearestTree(h.point, 1.3f, Vector3.zero);
                    break; // 맨 처음 맞은 것만 (나무 뒤의 나무는 안 됨)
                }
            }
            if (found == null) found = NearestTree(pp, reach + 0.6f, player.transform.forward);
            if (found == null) return null;

            Vector3 d = found.position - pp;
            d.y = 0f;
            dist = Mathf.Max(0f, d.magnitude - 0.5f); // 밑동 굵기만큼 빼 줌
            return found;
        }

        private Transform FindTree(Transform t)
        {
            for (; t != null; t = t.parent)
            {
                if (!t.name.StartsWith(treeNamePrefix, StringComparison.Ordinal)) continue;
                treeByName[t.name] = t;
                return t;
            }
            return null;
        }

        // point 근처(수평 radius m 안)의 서 있는 나무 중 가장 가까운 것. forward가 있으면 그쪽(앞쪽)에 있는 것만
        private Transform NearestTree(Vector3 point, float radius, Vector3 forward)
        {
            RefreshTreeIndex(false);
            Transform best = null;
            float bestD = radius;
            forward.y = 0f;
            bool needFront = forward.sqrMagnitude > 0.001f;
            if (needFront) forward.Normalize();
            foreach (Transform t in treeByName.Values)
            {
                if (t == null || !t.gameObject.activeInHierarchy || animating.Contains(t)) continue;
                Vector3 d = t.position - point;
                d.y = 0f;
                float m = d.magnitude;
                if (m >= bestD) continue;
                if (needFront && m > 0.01f && Vector3.Dot(d / m, forward) < 0.5f) continue;
                best = t;
                bestD = m;
            }
            return best;
        }

        // 씬의 나무(Tree_로 시작하는 오브젝트)를 한 번 훑어서 이름 → 나무 표를 만듦
        private void RefreshTreeIndex(bool force)
        {
            if (!force && Time.unscaledTime < nextIndexScan) return;
            nextIndexScan = Time.unscaledTime + 3f;
            foreach (Transform t in FindObjectsByType<Transform>(FindObjectsInactive.Include))
            {
                if (!t.name.StartsWith(treeNamePrefix, StringComparison.Ordinal)) continue;
                if (t.parent != null && t.parent.name.StartsWith(treeNamePrefix, StringComparison.Ordinal)) continue; // 나무 안의 자식은 건너뜀
                if (t.GetComponentInChildren<Collider>(true) == null) continue; // 충돌체 없는 건 나무가 아님
                if (!treeByName.ContainsKey(t.name) || treeByName[t.name] == null) treeByName[t.name] = t;
            }
        }

        // 캐릭터 Animator에 있는 트리거로 휘두르기 (도끼 동작 → 없으면 밭 갈기 동작). 아무것도 없으면 그냥 넘어감
        private static readonly string[] SwingTriggers = { "Chop", "Axe", "Till", "Hoe", "Dig" };
        private Animator playerAnim;
        private string swingTrigger;
        private bool swingChecked;

        private void PlaySwing()
        {
            if (player == null) return;
            if (playerAnim == null) { playerAnim = player.GetComponentInChildren<Animator>(); swingChecked = false; }
            if (playerAnim == null || playerAnim.runtimeAnimatorController == null) return;
            if (!swingChecked)
            {
                swingChecked = true;
                swingTrigger = null;
                foreach (string want in SwingTriggers)
                {
                    foreach (AnimatorControllerParameter p in playerAnim.parameters)
                        if (p.type == AnimatorControllerParameterType.Trigger && p.name == want) { swingTrigger = want; break; }
                    if (swingTrigger != null) break;
                }
                Debug.Log(swingTrigger != null
                    ? $"[나무] 휘두르기 동작: Animator 트리거 \"{swingTrigger}\""
                    : "[나무] 캐릭터 Animator에 Chop·Axe·Till·Hoe·Dig 트리거가 없어서 휘두르기 동작 없이 벰");
            }
            if (swingTrigger == null) return;
            playerAnim.ResetTrigger(swingTrigger);
            playerAnim.SetTrigger(swingTrigger);
        }

        private void Hint(string text)
        {
            if (Time.unscaledTime < nextHint) return;
            nextHint = Time.unscaledTime + 1.5f;
            IslandNotice.Show(text, this);
        }

        private IEnumerator UnblockAtEndOfFrame()
        {
            yield return new WaitForEndOfFrame();
            UIInputBlocker.Unblock(this);
        }

        // ───────────────────────── 연출 ─────────────────────────

        private static Vector3 BasePoint(Transform tree)
        {
            float minY = tree.position.y;
            bool any = false;
            foreach (Renderer r in tree.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                minY = any ? Mathf.Min(minY, r.bounds.min.y) : r.bounds.min.y;
                any = true;
            }
            return new Vector3(tree.position.x, Mathf.Max(minY, tree.position.y - 1f), tree.position.z);
        }

        // 한 번 칠 때: 맞은 쪽 반대로 살짝 기울었다 돌아옴
        private IEnumerator Shake(Transform tree, Vector3 dir)
        {
            animating.Add(tree);
            Vector3 pos0 = tree.position;
            Quaternion rot0 = tree.rotation;
            Vector3 pivot = BasePoint(tree);
            Vector3 axis = Vector3.Cross(Vector3.up, dir);
            const float dur = 0.28f;
            for (float t = 0f; t < dur && tree != null; t += Time.deltaTime)
            {
                float a = Mathf.Sin(t / dur * Mathf.PI) * 5f * (1f - t / dur * 0.5f);
                tree.SetPositionAndRotation(pos0, rot0);
                tree.RotateAround(pivot, axis, a);
                yield return null;
            }
            if (tree != null) tree.SetPositionAndRotation(pos0, rot0);
            animating.Remove(tree);
        }

        // 마지막 한 방: 맞은 반대쪽으로 쓰러지고 → 작아지며 사라짐 → 원래 모습으로 되돌려서 꺼 둠 (다시 자랄 때 켬)
        private IEnumerator FallAndHide(Transform tree, Vector3 dir)
        {
            animating.Add(tree);
            Collider[] cols = tree.GetComponentsInChildren<Collider>();
            foreach (Collider c in cols) c.enabled = false; // 바로 지나가고 밭을 갈 수 있게
            Vector3 pos0 = tree.position;
            Quaternion rot0 = tree.rotation;
            Vector3 scale0 = tree.localScale;
            Vector3 pivot = BasePoint(tree);
            Vector3 axis = Vector3.Cross(Vector3.up, dir);

            const float fall = 0.75f;
            for (float t = 0f; t < fall && tree != null; t += Time.deltaTime)
            {
                float k = t / fall;
                tree.SetPositionAndRotation(pos0, rot0);
                tree.RotateAround(pivot, axis, 85f * k * k); // 점점 빨라지며 쓰러짐
                yield return null;
            }
            if (tree == null) yield break;
            tree.SetPositionAndRotation(pos0, rot0);
            tree.RotateAround(pivot, axis, 85f);
            GameAudio.PlayAt(SoundId.ClearCrop, tree.position);
            yield return new WaitForSeconds(0.25f);

            Vector3 fallenPos = tree.position;
            const float shrink = 0.35f;
            for (float t = 0f; t < shrink && tree != null; t += Time.deltaTime)
            {
                float k = 1f - t / shrink;
                tree.localScale = scale0 * k;
                tree.position = Vector3.Lerp(pivot, fallenPos, k);
                yield return null;
            }
            if (tree == null) yield break;
            tree.SetPositionAndRotation(pos0, rot0);
            tree.localScale = scale0;
            foreach (Collider c in cols) if (c != null) c.enabled = true;
            animating.Remove(tree);
            // 그 사이 다시 자라기로 했으면 (거의 없음) 그대로 둠
            if (chopped.ContainsKey(tree.name)) tree.gameObject.SetActive(false);
        }

        // 다시 자랄 때: 작은 묘목에서 커짐
        private IEnumerator GrowIn(Transform tree)
        {
            animating.Add(tree);
            Vector3 scale0 = tree.localScale;
            const float dur = 1.2f;
            for (float t = 0f; t < dur && tree != null; t += Time.unscaledDeltaTime)
            {
                float k = t / dur;
                float s = Mathf.Lerp(0.15f, 1f, 1f - (1f - k) * (1f - k));
                tree.localScale = scale0 * s;
                yield return null;
            }
            if (tree != null) tree.localScale = scale0;
            animating.Remove(tree);
        }

        // ───────────────────────── 다시 자라기 ─────────────────────────

        private void HandleDayChanged(int day, Season season)
        {
            if (chopped.Count == 0) return;
            RefreshTreeIndex(true);
            var ready = new List<ChoppedTree>();
            foreach (ChoppedTree c in chopped.Values)
                if (day - c.day >= regrowDays) ready.Add(c);

            int grown = 0, waiting = 0;
            foreach (ChoppedTree c in ready)
            {
                if (!treeByName.TryGetValue(c.name, out Transform t) || t == null) { chopped.Remove(c.name); continue; }
                string why = RegrowBlockReason(t);
                if (why != null) { waiting++; if (logWhyNot) Debug.Log($"[나무] {c.name}: 다시 자랄 때가 됐지만 기다림 - {why}"); continue; }
                chopped.Remove(c.name);
                t.gameObject.SetActive(true);
                StartCoroutine(GrowIn(t));
                grown++;
            }
            if (grown > 0 || waiting > 0) Debug.Log($"[나무] Day {day}: {grown}그루 다시 자람, {waiting}그루는 자리가 비기를 기다림");
        }

        // 그 자리에 다시 자라면 안 되는 이유 (자라도 되면 null)
        private string RegrowBlockReason(Transform tree)
        {
            Vector3 p = tree.position;
            if (player != null)
            {
                Vector3 d = player.transform.position - p;
                d.y = 0f;
                if (d.magnitude < 1.5f) return "플레이어가 그 자리에 서 있음";
            }
            foreach (RaycastHit h in Physics.RaycastAll(p + Vector3.up * 2f, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore))
            {
                FarmTile tile = h.collider.GetComponentInParent<FarmTile>();
                if (tile == null) continue;
                if (tile.State != TileState.Natural) return "그 칸을 갈아 놓음";
                if (tile.CurrentCrop != null) return "그 칸에 작물이 있음";
                if (WeedManager.HasWeed(tile)) return "그 칸에 잡초가 있음";
                break;
            }
            foreach (Collider c in Physics.OverlapSphere(p + Vector3.up * 1f, 0.3f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c.GetComponentInParent<FarmTile>() != null) continue;
                if (player != null && c.transform.IsChildOf(player.transform)) continue;
                if (c.transform.IsChildOf(tree)) continue;
                return $"그 자리에 다른 물건이 있음 ({c.name})";
            }
            return null;
        }

        // ───────────────────────── 저장 ─────────────────────────

        private static string FilePath(int slot) => Path.Combine(Application.persistentDataPath, $"trees_{slot + 1}.json");

        // SaveManager가 저장할 때마다 같이 저장 (새 게임 시작할 때도 저장이 한 번 되므로 예전 기록이 지워짐)
        private void WriteFile(int slot)
        {
            try
            {
                var f = new TreeSaveFile();
                f.chopped.AddRange(chopped.Values);
                File.WriteAllText(FilePath(slot), JsonUtility.ToJson(f));
            }
            catch (Exception e) { Debug.LogWarning("[나무] 저장 실패: " + e.Message); }
        }

        // 칸을 고르면(이어하기·새 게임) 그 칸의 나무 기록을 읽음
        private void CheckSlotLoaded()
        {
            int slot = SaveManager.Instance != null ? SaveManager.Instance.CurrentSlot : -1;
            if (slot == lastSlot) return;
            lastSlot = slot;
            if (slot >= 0) ReadFile(slot);
        }

        private void ReadFile(int slot)
        {
            chopped.Clear();
            pendingHide.Clear();
            string path = FilePath(slot);
            try
            {
                if (!File.Exists(path)) return;
                // 세이브 파일과 같이 저장된 기록만 믿음 (세이브를 지우고 새로 시작했는데 예전 기록이 남은 경우 무시)
                string savePath = SaveManager.SlotPath(slot);
                if (File.Exists(savePath) && Math.Abs((File.GetLastWriteTimeUtc(savePath) - File.GetLastWriteTimeUtc(path)).TotalSeconds) > 30)
                {
                    Debug.Log($"[나무] {slot + 1}번 칸: 세이브와 저장 시각이 안 맞는 예전 나무 기록이라 무시");
                    return;
                }
                TreeSaveFile f = JsonUtility.FromJson<TreeSaveFile>(File.ReadAllText(path));
                if (f == null || f.chopped == null) return;
                foreach (ChoppedTree c in f.chopped)
                {
                    if (c == null || string.IsNullOrEmpty(c.name)) continue;
                    chopped[c.name] = c;
                    pendingHide.Add(c.name);
                }
                pendingUntil = Time.unscaledTime + 15f;
                if (chopped.Count > 0) Debug.Log($"[나무] {slot + 1}번 칸: 벤 나무 {chopped.Count}그루 불러옴");
            }
            catch (Exception e) { Debug.LogWarning("[나무] 불러오기 실패: " + e.Message); }
        }

        // 불러온 벤 나무를 씬에서 찾아 꺼 둠 (맵이 늦게 만들어져도 잠깐 동안 계속 찾음)
        private void ResolvePending()
        {
            if (pendingHide.Count == 0) return;
            RefreshTreeIndex(false);
            for (int i = pendingHide.Count - 1; i >= 0; i--)
            {
                if (!treeByName.TryGetValue(pendingHide[i], out Transform t) || t == null) continue;
                t.gameObject.SetActive(false);
                pendingHide.RemoveAt(i);
            }
            if (pendingHide.Count > 0 && Time.unscaledTime > pendingUntil)
            {
                Debug.LogWarning($"[나무] 저장된 벤 나무 {pendingHide.Count}그루를 씬에서 못 찾음 (맵이 바뀌었나요?) - 예: {pendingHide[0]}");
                foreach (string n in pendingHide) chopped.Remove(n);
                pendingHide.Clear();
            }
        }

#if !NO_EXHIBITION_CHEATS
        /// <summary>전시회용: 벤 나무를 전부 지금 바로 다시 자라게</summary>
        public void DebugRegrowAll()
        {
            RefreshTreeIndex(true);
            foreach (ChoppedTree c in new List<ChoppedTree>(chopped.Values))
                if (treeByName.TryGetValue(c.name, out Transform t) && t != null) { t.gameObject.SetActive(true); StartCoroutine(GrowIn(t)); }
            chopped.Clear();
        }
#endif
    }
}