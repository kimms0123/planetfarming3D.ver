using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;
using FarmingSystem.Core;
using FarmingSystem.Economy;
using FarmingSystem.Farming;
using FarmingSystem.Inventory;
using FarmingSystem.Player;

namespace FarmingSystem.Building
{
    // 건물 설치 모드 (동숲에서 집 키트를 들고 자리를 고르는 방식)
    // 1. 핫바에서 건물 키트를 고르고 좌클릭 → 설치 모드 시작
    // 2. 바라보는 칸(십자선, 커서가 보이면 마우스)에 반투명 건물과 칸 표시가 따라다님
    //    - 초록: 지을 수 있음 / 빨강: 못 지음 (아래 안내줄에 이유)
    //    - R: 90도 회전 (건물 설정에서 허용한 경우)
    // 3. 좌클릭: 확정 → 키트 1개 사용, 그 자리에 공사장 생성 (다음 날 완성)
    //    Esc 또는 우클릭: 취소
    // 지을 수 없는 곳: 땅이 아닌 곳(물), 높이가 다른 칸이 섞인 곳, 다른 건물·작물·갈아 둔 밭, 나무·바위·절벽 같은 장애물, 플레이어가 서 있는 곳
    public class BuildingPlacer : MonoBehaviour
    {
        public static BuildingPlacer Instance { get; private set; }

        /// <summary>설치 모드 중이거나 핫바에 건물 키트를 들고 있으면 true → 농사 클릭을 막음</summary>
        public static bool BlocksFarming => Instance != null && (Instance.placing || Instance.HoldingKit(out _, out _));
        public bool IsPlacing => placing;

        [Header("안내")]
        [Tooltip("화면에 조작법·못 짓는 이유를 보여줄 글자 (건축 시스템 만들기 도구가 만들어 줌)")]
        [SerializeField] private TMP_Text hintText;

        [Header("설치 규칙")]
        [Tooltip("플레이어에게서 이 거리(m)보다 먼 곳에는 못 지음")]
        [SerializeField] private float maxDistance = 25f;
        [Tooltip("갈아 둔 밭(작물 없음) 위에도 지을 수 있게")]
        [SerializeField] private bool allowOnTilled = false;
        [Tooltip("칸 높이 차이 허용 (m)")]
        [SerializeField] private float heightTolerance = 0.25f;
        [Tooltip("장애물(나무·바위·절벽·플레이어)로 볼 레이어")]
        [SerializeField] private LayerMask obstacleMask = ~0;

        [Header("미리보기 색")]
        [SerializeField] private Color validColor = new Color(0.4f, 1f, 0.55f, 0.45f);
        [SerializeField] private Color invalidColor = new Color(1f, 0.35f, 0.35f, 0.45f);

        private bool placing;
        private BuildingKitData kit;
        private int kitSlot;
        private int rotation;
        private int startFrame;
        private Vector2 rightDownPos = new Vector2(-9999f, -9999f);

        private GameObject ghostRoot;
        private GameObject ghostModel;
        private int ghostModelRotation = -1;
        private readonly List<GameObject> cellMarks = new List<GameObject>();
        private Material ghostMat, cellOkMat, cellBadMat;

        private readonly List<Vector2Int> cells = new List<Vector2Int>();
        private bool lastValid;
        private Vector3 lastCenter;
        private PlayerController player;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (hintText != null) hintText.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (!placing)
            {
                // 키트를 든 채 좌클릭 → 설치 모드
                if (!UIInputBlocker.IsBlocked && !PointerOverUI() && LeftClick() && HoldingKit(out BuildingKitData k, out int slot))
                    Begin(k, slot);
                return;
            }

            // 인벤토리 등 다른 창이 열리면 잠깐 숨김
            if (UIInputBlocker.IsBlocked) { SetGhostVisible(false); return; }

            // 키트를 내려놓으면(핫바 칸 바꾸기 등) 취소
            if (!HoldingKit(out BuildingKitData now, out int nowSlot) || now != kit || nowSlot != kitSlot) { End("취소"); return; }

            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            // 우클릭: 눌렀다 바로 뗐을 때만 취소 (우클릭 끌기로 화면을 돌리는 카메라 모드와 안 겹치게)
            if (mouse != null && mouse.rightButton.wasPressedThisFrame) rightDownPos = mouse.position.ReadValue();
            bool rightTap = mouse != null && mouse.rightButton.wasReleasedThisFrame
                            && (mouse.position.ReadValue() - rightDownPos).sqrMagnitude < 64f;
            if ((kb != null && kb.escapeKey.wasPressedThisFrame) || rightTap) { End("취소"); return; }
            if (kit.building.allowRotate && kb != null && kb.rKey.wasPressedThisFrame) rotation = (rotation + 1) % 4;

            string reason = UpdatePreview();
            ShowHint(reason);

            if (Time.frameCount > startFrame && LeftClick() && !PointerOverUI())
            {
                if (reason == null) Confirm();
                else Debug.Log($"[건축] 여기엔 지을 수 없어요 - {reason}");
            }
        }

        // ---------- 시작·끝 ----------

        /// <summary>코드에서 바로 설치 모드 시작 (스토리 이벤트·디버그용 - 키트 없이)</summary>
        public void Begin(BuildingKitData k, int slot)
        {
            if (k == null || k.building == null || k.building.prefab == null)
            {
                Debug.LogWarning("[건축] 키트에 건물(Building Data)이나 모델(Prefab)이 연결되어 있지 않아요");
                return;
            }
            if (BuildingSystem.Instance == null) { Debug.LogWarning("[건축] 씬에 BuildingSystem이 없어요"); return; }

            placing = true;
            kit = k;
            kitSlot = slot;
            rotation = 0;
            startFrame = Time.frameCount;
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            CreateGhost();
            Debug.Log($"[건축] {k.building.displayName} 설치 모드 - 좌클릭: 설치 / R: 회전 / Esc·우클릭: 취소");
        }

        private void End(string why)
        {
            placing = false;
            kit = null;
            DestroyGhost();
            if (hintText != null) hintText.gameObject.SetActive(false);
            if (why != null) Debug.Log($"[건축] 설치 모드 끝 ({why})");
        }

        private void Confirm()
        {
            BuildingData b = kit.building;
            if (b.cost > 0)
            {
                if (CurrencyManager.Instance == null || !CurrencyManager.Instance.TrySpendBells(b.cost))
                {
                    ShowHint($"돈이 부족해요 ({b.cost}벨 필요)");
                    return;
                }
            }
            InventoryManager.Instance.RemoveFromSlot(kitSlot, 1);
            BuildingSystem.Instance.Place(b, new List<Vector2Int>(cells), lastCenter, rotation);
            End(null);
        }

        // ---------- 미리보기 ----------

        // 바라보는 칸을 중심으로 칸을 고르고, 지을 수 있는지 확인. 못 지으면 이유를 돌려줌 (지을 수 있으면 null)
        private string UpdatePreview()
        {
            BuildingSystem sys = BuildingSystem.Instance;
            BuildingData b = kit.building;
            if (!TryGetTargetGrid(out Vector2Int target, out float baseY))
            {
                SetGhostVisible(false);
                return "지을 자리를 바라봐 주세요";
            }
            SetGhostVisible(true);

            Vector2Int fp = BuildingSystem.RotatedFootprint(b, rotation);
            Vector2Int start = new Vector2Int(target.x - (fp.x - 1) / 2, target.y - (fp.y - 1) / 2);

            cells.Clear();
            string reason = null;
            var bad = new List<bool>();
            Vector3 sum = Vector3.zero;
            int found = 0;
            for (int x = 0; x < fp.x; x++)
                for (int z = 0; z < fp.y; z++)
                {
                    var g = new Vector2Int(start.x + x, start.y + z);
                    cells.Add(g);
                    string cellReason = CheckCell(sys, g, baseY, out Vector3 center);
                    bad.Add(cellReason != null);
                    if (cellReason != null && reason == null) reason = cellReason;
                    if (center != Vector3.zero) { sum += center; found++; }
                    else sum += GridGuess(sys, g, baseY);
                }
            lastCenter = sum / cells.Count;
            lastCenter.y = baseY;

            // 장애물 (나무·바위·절벽·플레이어)
            if (reason == null) reason = CheckObstacles(sys, fp);
            // 거리
            if (reason == null && player != null && Vector3.Distance(player.transform.position, lastCenter) > maxDistance)
                reason = "너무 멀어요. 조금 더 가까이 가 주세요";

            PlaceGhost(fp, bad, reason == null);
            lastValid = reason == null;
            return reason;
        }

        private string CheckCell(BuildingSystem sys, Vector2Int g, float baseY, out Vector3 center)
        {
            center = Vector3.zero;
            if (!sys.TryGetTile(g, out FarmTile t)) return "땅이 아닌 곳이 있어요 (물·길·바위 땅)";
            center = t.Center;
            if (Mathf.Abs(t.Center.y - baseY) > heightTolerance) return "높이가 다른 땅이 섞여 있어요";
            if (sys.IsOccupied(g)) return "다른 건물이 있어요";
            if (t.CurrentCrop != null) return "작물이 심어져 있어요";
            if (!allowOnTilled && t.State != TileState.Natural) return "갈아 둔 밭이 있어요";
            return null;
        }

        private string CheckObstacles(BuildingSystem sys, Vector2Int fp)
        {
            float c = sys.CellSize;
            Vector3 half = new Vector3(fp.x * c * 0.47f, 1.2f, fp.y * c * 0.47f);
            Vector3 center = lastCenter + Vector3.up * (0.3f + half.y);
            Collider[] hits = Physics.OverlapBox(center, half, Quaternion.identity, obstacleMask, QueryTriggerInteraction.Ignore);
            foreach (Collider h in hits)
            {
                if (ghostRoot != null && h.transform.IsChildOf(ghostRoot.transform)) continue;
                if (h.GetComponentInParent<FarmTile>() != null)
                {
                    if (h.bounds.max.y <= lastCenter.y + 0.35f) continue; // 발밑 땅
                    return "높은 땅(언덕)에 걸려요";
                }
                if (h.GetComponentInParent<PlayerController>() != null) return "플레이어가 그 자리에 서 있어요";
                if (h.GetComponentInParent<PlacedBuilding>() != null) return "다른 건물이 있어요";
                return $"장애물이 있어요 ({h.transform.root.name})";
            }
            return null;
        }

        // 칸 정보가 없는 자리(물 등)의 위치 추정 - 표시용
        private static Vector3 GridGuess(BuildingSystem sys, Vector2Int g, float y)
        {
            if (sys.TryGetTile(g, out FarmTile t)) return t.Center;
            // 이웃 칸에서 거리 추정
            foreach (var d in new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down })
                if (sys.TryGetTile(g + d, out FarmTile n))
                    return n.Center - new Vector3(d.x * sys.CellSize, 0f, d.y * sys.CellSize);
            return Vector3.zero;
        }

        // 십자선(커서가 보이면 마우스)이 가리키는 칸
        private bool TryGetTargetGrid(out Vector2Int g, out float baseY)
        {
            g = default;
            baseY = 0f;
            Camera cam = Camera.main;
            BuildingSystem sys = BuildingSystem.Instance;
            if (cam == null || sys == null) return false;

            Ray ray = Cursor.lockState == CursorLockMode.Locked || Mouse.current == null
                ? cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
                : cam.ScreenPointToRay(Mouse.current.position.ReadValue());

            RaycastHit[] hits = Physics.RaycastAll(ray, 80f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit h in hits)
            {
                if (h.collider.GetComponentInParent<PlayerController>() != null) continue;
                if (ghostRoot != null && h.transform.IsChildOf(ghostRoot.transform)) continue;

                FarmTile t = h.collider.GetComponentInParent<FarmTile>();
                if (t != null) { g = t.GridPosition; baseY = t.Center.y; return true; }
                if (sys.TryWorldToGrid(h.point, out g))
                {
                    baseY = sys.TryGetTile(g, out FarmTile near) ? near.Center.y : h.point.y;
                    return true;
                }
                return false;
            }
            return false;
        }

        // ---------- 반투명 건물 + 칸 표시 ----------

        private void CreateGhost()
        {
            DestroyGhost();
            BuildingSystem sys = BuildingSystem.Instance;
            ghostRoot = new GameObject("BuildingGhost");
            Material baseMat = sys.ghostMaterial;
            ghostMat = baseMat != null ? new Material(baseMat) : null;
            cellOkMat = baseMat != null ? new Material(baseMat) : null;
            cellBadMat = baseMat != null ? new Material(baseMat) : null;
            SetColor(cellOkMat, validColor);
            SetColor(cellBadMat, invalidColor);
            ghostModelRotation = -1;
        }

        private void DestroyGhost()
        {
            if (ghostRoot != null) Destroy(ghostRoot);
            ghostRoot = null;
            ghostModel = null;
            cellMarks.Clear();
            if (ghostMat != null) Destroy(ghostMat);
            if (cellOkMat != null) Destroy(cellOkMat);
            if (cellBadMat != null) Destroy(cellBadMat);
            ghostMat = cellOkMat = cellBadMat = null;
        }

        private void SetGhostVisible(bool v)
        {
            if (ghostRoot != null && ghostRoot.activeSelf != v) ghostRoot.SetActive(v);
        }

        private void PlaceGhost(Vector2Int fp, List<bool> bad, bool valid)
        {
            BuildingSystem sys = BuildingSystem.Instance;
            ghostRoot.transform.position = lastCenter;

            // 회전이 바뀌면 모델을 다시 만듦
            if (ghostModel == null || ghostModelRotation != rotation)
            {
                if (ghostModel != null) Destroy(ghostModel);
                ghostModel = BuildingSystem.SpawnFitted(kit.building, ghostRoot.transform, sys.CellSize, rotation);
                ghostModelRotation = rotation;
                if (ghostModel != null) MakeGhostLook(ghostModel);
            }
            SetColor(ghostMat, valid ? validColor : invalidColor);

            // 칸 표시 (칸마다 초록/빨강 판)
            while (cellMarks.Count < cells.Count) cellMarks.Add(MakeCellMark());
            for (int i = 0; i < cellMarks.Count; i++)
            {
                bool on = i < cells.Count;
                cellMarks[i].SetActive(on);
                if (!on) continue;
                Vector3 p = GridGuess(sys, cells[i], lastCenter.y);
                if (p == Vector3.zero) p = lastCenter;
                cellMarks[i].transform.position = new Vector3(p.x, lastCenter.y + 0.04f, p.z);
                cellMarks[i].transform.localScale = Vector3.one * sys.CellSize * 0.92f;
                cellMarks[i].GetComponent<Renderer>().sharedMaterial = bad[i] || !valid ? cellBadMat : cellOkMat;
            }
        }

        private GameObject MakeCellMark()
        {
            GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Collider col = q.GetComponent<Collider>();
            if (col != null) Destroy(col);
            q.name = "CellMark";
            q.transform.SetParent(ghostRoot.transform, true);
            q.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // 바닥에 눕힘
            Renderer r = q.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return q;
        }

        // 미리보기 모델: 충돌체·스크립트 끄고 반투명 재질로
        private void MakeGhostLook(GameObject model)
        {
            foreach (Collider c in model.GetComponentsInChildren<Collider>()) Destroy(c);
            foreach (MonoBehaviour mb in model.GetComponentsInChildren<MonoBehaviour>()) mb.enabled = false;
            if (ghostMat == null) return;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = ghostMat;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        private static void SetColor(Material m, Color c)
        {
            if (m == null) return;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        // ---------- 안내·입력 ----------

        private void ShowHint(string reason)
        {
            if (hintText == null) return;
            hintText.gameObject.SetActive(true);
            string rot = kit != null && kit.building.allowRotate ? " · <b>R</b> 회전" : "";
            string head = $"<b>{kit?.building.displayName}</b> 설치  —  <b>좌클릭</b> 설치{rot} · <b>Esc</b>/<b>우클릭</b> 취소";
            hintText.text = reason == null ? head : $"{head}\n<color=#D65A5A>{reason}</color>";
        }

        /// <summary>지금 핫바에서 고른 칸이 건물 키트인지</summary>
        public bool HoldingKit(out BuildingKitData k, out int slot)
        {
            k = null;
            slot = -1;
            InventoryManager inv = InventoryManager.Instance;
            if (inv == null) return false;
            slot = inv.SelectedHotbarIndex;
            InventorySlotData s = inv.GetSlot(slot);
            k = s != null && !s.IsEmpty ? s.item as BuildingKitData : null;
            return k != null;
        }

        private static bool LeftClick() => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;

        private static bool PointerOverUI()
        {
            if (Cursor.lockState == CursorLockMode.Locked) return false;
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }
    }
}