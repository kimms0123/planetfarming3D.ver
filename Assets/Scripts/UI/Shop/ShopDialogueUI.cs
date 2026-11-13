using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using FarmingSystem.Core;
using FarmingSystem.Economy;
using FarmingSystem.Player;

namespace FarmingSystem.UI.Shop
{
    /// <summary>
    /// 상인과의 거래 한 번(세션)을 관리한다.
    /// 대화 선택지(구매/판매/아무것도 아니다) -> 구매/판매 창 -> [취소]/Esc 시 선택지로 복귀.
    /// 세션이 열려 있는 동안 UIInputBlocker로 이동·농사·핫바·인벤토리 입력을 막는다.
    /// 선택지 버튼의 OnClick()은 인스펙터에서 OnChooseBuy / OnChooseSell / OnChooseNothing에 연결한다.
    /// </summary>
    public class ShopDialogueUI : MonoBehaviour
    {
        [Tooltip("예전 대화창 (새 대화창을 쓰면 자동으로 꺼둠)")]
        [SerializeField] private GameObject dialogueRoot;
        [SerializeField] private ShopBuyPanelUI buyPanel;
        [SerializeField] private ShopSellPanelUI sellPanel;

        [Header("새 대화창 (포켓몬·동숲 느낌 - 아래 대화 상자 + 오른쪽 선택지)")]
        [Tooltip("끄면 예전 대화창(Dialogue Root)을 그대로 씀")]
        [SerializeField] private bool useConversationView = true;
        [Tooltip("비우면 자동으로 만들어 붙임")]
        [SerializeField] private ShopConversationUI conversation;
        [SerializeField] private string merchantName = "상인";
        [Tooltip("처음 말 걸었을 때 대사 (여러 개면 무작위)")]
        [SerializeField, TextArea] private string[] greetingLines = { "어서와! 오늘은 무엇을 도와줄까?", "왔구나! 좋은 물건 많이 들어왔어." };
        [Tooltip("구매·판매 창을 닫고 돌아왔을 때 대사")]
        [SerializeField, TextArea] private string againLine = "또 필요한 게 있어?";
        [SerializeField] private string[] choiceLabels = { "구매하기", "판매하기", "괜찮아요" };

        [Header("상인 클로즈업 카메라 (1인칭 느낌)")]
        [SerializeField] private bool useCloseup = true;
        [Tooltip("상인 모델 (비우면 E로 말을 건 상인을 자동으로 씀)")]
        [SerializeField] private Transform merchant;
        [Tooltip("카메라를 놓을 자리를 직접 정하고 싶으면 상인 앞에 빈 오브젝트를 만들어 넣기 (파란 Z축이 상인 얼굴을 보게). 비우면 자동 계산")]
        [SerializeField] private Transform closeupPoint;
        [Tooltip("상인 얼굴에서 카메라까지 최소 거리 (m)")]
        [SerializeField] private float closeupDistance = 3f;
        [Tooltip("화면에 상인 키의 몇 %가 보이게 할지 (1 = 머리부터 발끝까지, 0.6 = 허리 위). 상인 크기를 재서 거리를 자동으로 맞춤")]
        [SerializeField, Range(0.3f, 1.5f)] private float visibleHeightRatio = 0.75f;
        [Tooltip("얼굴 높이 = 상인 머리 꼭대기에서 키의 몇 %만큼 아래")]
        [SerializeField, Range(0f, 0.6f)] private float faceFromTop = 0.2f;
        [Tooltip("화면 구도 (화면 반쪽 크기 대비 비율): x = 상인을 왼쪽으로 미는 정도, y = 상인을 위로 올리는 정도 (아래 대화 상자에 얼굴이 가리지 않게)")]
        [SerializeField] private Vector2 framing = new Vector2(0.2f, 0.3f);
        [Tooltip("클로즈업 중 화각 (작을수록 망원 - 얼굴이 크게). 0이면 그대로")]
        [SerializeField, Range(0f, 90f)] private float closeupFov = 38f;
        [Tooltip("카메라 높이 각도 (도). 0 = 상인과 눈높이 (얼굴 정면), 10~15 = 살짝 위에서 내려다봄")]
        [SerializeField, Range(0f, 45f)] private float closeupPitch = 0f;
        [Tooltip("눈 높이 = 머리 뼈(Head)에서 머리 꼭대기(head_end)까지의 몇 % 위치. 모델에 머리 뼈가 있을 때만 씀 (없으면 Face From Top 사용)")]
        [SerializeField, Range(0f, 1f)] private float eyeInHead = 0.35f;
        [Tooltip("대화할 때 아래 대화창이 가리는 높이 (화면 세로 비율). 대화창 높이 250 + 아래 여백 → 1080 기준 약 0.29. 상인 턱이 항상 이 위에 오도록 자동으로 올림")]
        [SerializeField, Range(0f, 0.6f)] private float closeupBoxCover = 0.29f;
        [Tooltip("구매·판매 창에서 왼쪽 아래 상인 대사 상자가 가리는 높이 (화면 세로 비율). 상자 160 + 여백 40 → 1080 기준 약 0.2")]
        [SerializeField, Range(0f, 0.6f)] private float panelBoxCover = 0.2f;
        [Tooltip("대화 중 플레이어 모델 숨기기 (1인칭 느낌)")]
        [SerializeField] private bool hidePlayer = true;
        [Tooltip("대화 중 핫바·십자선 숨기기")]
        [SerializeField] private bool hideHud = true;

        [Header("구매·판매 창 연출 (전체 화면 대신 실제 상인 옆에 품목 창)")]
        [Tooltip("켜면 구매·판매 창을 열 때 카메라가 옆으로 비켜서 상인을 화면 왼쪽에 두고, 오른쪽에 품목 창이 보이게 함")]
        [SerializeField] private bool worldViewPanels = true;
        [Tooltip("구매·판매 창이 열렸을 때 구도 (x = 상인을 왼쪽으로 미는 정도, y = 위로 올리는 정도). 클로즈업 Framing과 같은 방식")]
        [SerializeField] private Vector2 panelFraming = new Vector2(0.55f, 0.1f);
        [Tooltip("판매 창 구도 (판매 창이 더 넓어서 상인을 더 왼쪽으로)")]
        [SerializeField] private Vector2 sellPanelFraming = new Vector2(0.7f, 0.1f);
        [Tooltip("구매·판매 창이 열렸을 때 화면에 보이는 상인 키 비율 (클수록 멀리서 - 몸 전체가 보임)")]
        [SerializeField, Range(0.3f, 2f)] private float panelVisibleHeightRatio = 1.2f;
        [Tooltip("구매·판매 창을 열 때 숨길 오브젝트 (전체 화면 배경, 왼쪽 상인 초상화 등 - 실제 상인이 보이므로 필요 없는 것)")]
        [SerializeField] private GameObject[] hideDuringPanels;

        public bool IsSessionOpen { get; private set; }

        private bool isSubscribedToShop = false;
        private bool greeted;
        private Transform playerTransform;
        private readonly List<Renderer> hiddenPlayerRenderers = new List<Renderer>();
        private readonly List<CanvasGroup> hiddenGroups = new List<CanvasGroup>();
        private readonly List<float> hiddenAlphas = new List<float>();

        private void Awake()
        {
            if (useConversationView && conversation == null)
            {
                conversation = GetComponent<ShopConversationUI>();
                if (conversation == null) conversation = gameObject.AddComponent<ShopConversationUI>();
            }
            if (buyPanel != null) buyPanel.Closed += ShowChoices;
            if (sellPanel != null) sellPanel.Closed += ShowChoices;
        }

        private void OnEnable() => TrySubscribeToShop();
        private void Start() => TrySubscribeToShop();

        private void OnDestroy()
        {
            if (buyPanel != null) buyPanel.Closed -= ShowChoices;
            if (sellPanel != null) sellPanel.Closed -= ShowChoices;
            if (ShopManager.Instance != null) ShopManager.Instance.OnShopAvailabilityChanged -= HandleShopAvailabilityChanged;
            UIInputBlocker.Unblock(this);
        }

        private void TrySubscribeToShop()
        {
            if (isSubscribedToShop || ShopManager.Instance == null) return;
            ShopManager.Instance.OnShopAvailabilityChanged += HandleShopAvailabilityChanged;
            isSubscribedToShop = true;
        }

        // 거래 도중 상인이 떠나면 세션 강제 종료
        private void HandleShopAvailabilityChanged(bool isOpen)
        {
            if (!isOpen && IsSessionOpen) EndSession();
        }

        private void Update()
        {
            if (!IsSessionOpen || Keyboard.current == null) return;
            if (!Keyboard.current.escapeKey.wasPressedThisFrame) return;

            if (buyPanel != null && buyPanel.IsOpen) buyPanel.Cancel();
            else if (sellPanel != null && sellPanel.IsOpen) sellPanel.Cancel();
            else if (UsingConversation && conversation.IsVisible) conversation.Cancel(); // "괜찮아요"를 고른 것과 같음
            else EndSession();
        }

        private bool UsingConversation => useConversationView && conversation != null;

        /// <summary>ShopInteractable이 E키로 호출 (말을 건 상인과 플레이어를 넘겨주면 클로즈업 카메라가 그 상인을 비춤)</summary>
        public void OpenDialogue(Transform merchantTransform, Transform player)
        {
            if (merchantTransform != null && merchant == null) merchant = merchantTransform;
            if (player != null) playerTransform = player;
            OpenDialogue();
        }

        /// <summary>ShopInteractable이 E키로 호출</summary>
        public void OpenDialogue()
        {
            if (IsSessionOpen) return;

            IsSessionOpen = true;
            greeted = false;
            UIInputBlocker.Block(this);
            BeginCloseup();
            ShowChoices();
            Debug.Log("[상점] 대화 시작");
        }

        public void CloseDialogue()
        {
            if (dialogueRoot != null) dialogueRoot.SetActive(false);
            if (conversation != null) conversation.Hide();
        }

        private void ShowChoices()
        {
            if (!IsSessionOpen) return;
            SetPanelHiddenObjects(false);
            if (greeted) MoveCameraForCloseup(); // 구매·판매 창에서 돌아오면 다시 얼굴 쪽으로
            if (UsingConversation)
            {
                if (dialogueRoot != null) dialogueRoot.SetActive(false);
                string line = !greeted && greetingLines != null && greetingLines.Length > 0
                    ? greetingLines[Random.Range(0, greetingLines.Length)]
                    : againLine;
                greeted = true;
                conversation.Show(merchantName, line, choiceLabels, HandleChoice, choiceLabels.Length - 1);
                return;
            }
            if (dialogueRoot != null) dialogueRoot.SetActive(true);
        }

        // 새 대화창에서 고른 선택지 (0 = 구매, 1 = 판매, 그 외 = 괜찮아요)
        private void HandleChoice(int index)
        {
            if (index == 0) OnChooseBuy();
            else if (index == 1) OnChooseSell();
            else OnChooseNothing();
        }

        // ---------- 상인 클로즈업 ----------

        private void BeginCloseup()
        {
            if (hideHud) HideHud();
            if (!useCloseup) return;

            ThirdPersonCameraRig rig = ThirdPersonCameraRig.Instance;
            if (playerTransform == null)
            {
                PlayerController pc = FindAnyObjectByType<PlayerController>();
                if (pc != null) playerTransform = pc.transform;
            }

            if (rig != null && merchant != null)
            {
                if (closeupPoint != null) rig.BeginShot(closeupPoint.position, closeupPoint.rotation, closeupFov);
                else
                {
                    CalculateShot(framing, visibleHeightRatio, closeupBoxCover, out Vector3 pos, out Quaternion rot);
                    rig.BeginShot(pos, rot, closeupFov);
                }
            }
            else if (merchant == null) Debug.LogWarning("[상점] 클로즈업할 상인을 몰라요 - ShopDialogueUI의 Merchant 칸에 상인을 넣어주세요");

            if (hidePlayer && playerTransform != null)
            {
                hiddenPlayerRenderers.Clear();
                foreach (Renderer r in playerTransform.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled) continue;
                    r.enabled = false;
                    hiddenPlayerRenderers.Add(r);
                }
            }
        }

        private void EndCloseup()
        {
            if (ThirdPersonCameraRig.Instance != null) ThirdPersonCameraRig.Instance.EndShot();
            foreach (Renderer r in hiddenPlayerRenderers) if (r != null) r.enabled = true;
            hiddenPlayerRenderers.Clear();
            ShowHud();
        }

        // 상인 얼굴 앞, 플레이어가 서 있던 쪽에서 얼굴을 바라보는 위치 (상인 모델 크기에 맞춰 자동 계산)
        // 구매·판매 창 구도로 카메라 이동 (상인 왼쪽, 오른쪽에 품목 창 자리)
        private void MoveCameraForPanel(bool sell)
        {
            if (!useCloseup || !worldViewPanels || merchant == null || ThirdPersonCameraRig.Instance == null) return;
            CalculateShot(sell ? sellPanelFraming : panelFraming, panelVisibleHeightRatio, panelBoxCover, out Vector3 pos, out Quaternion rot);
            ThirdPersonCameraRig.Instance.BeginShot(pos, rot, closeupFov);
        }

        // 선택지로 돌아올 때 다시 얼굴 클로즈업
        private void MoveCameraForCloseup()
        {
            if (!useCloseup || merchant == null || ThirdPersonCameraRig.Instance == null) return;
            if (closeupPoint != null) { ThirdPersonCameraRig.Instance.BeginShot(closeupPoint.position, closeupPoint.rotation, closeupFov); return; }
            CalculateShot(framing, visibleHeightRatio, closeupBoxCover, out Vector3 pos, out Quaternion rot);
            ThirdPersonCameraRig.Instance.BeginShot(pos, rot, closeupFov);
        }

        private void SetPanelHiddenObjects(bool hidden)
        {
            if (!worldViewPanels || hideDuringPanels == null) return;
            foreach (GameObject g in hideDuringPanels)
                if (g != null) g.SetActive(!hidden);
        }

        private void CalculateShot(Vector2 framing, float visibleHeightRatio, float boxCover, out Vector3 position, out Quaternion rotation)
        {
            // 상인 몸 크기: 캐릭터 모델(뼈대 있는 SkinnedMeshRenderer)만 잼
            // → 상인 밑에 가판대·소품 같은 큰 물체가 있어도 얼굴 위치가 엉뚱하게 잡히지 않게
            Renderer[] all = merchant.GetComponentsInChildren<Renderer>();
            bool hasSkinned = false;
            foreach (Renderer r in all) if (r is SkinnedMeshRenderer) { hasSkinned = true; break; }
            Bounds b = new Bounds(merchant.position, Vector3.one * 1.5f);
            bool found = false;
            foreach (Renderer r in all)
            {
                if (r is ParticleSystemRenderer) continue;
                if (hasSkinned && !(r is SkinnedMeshRenderer)) continue;
                // 상인에게서 3m 넘게 떨어진 물체도 뺌
                Vector3 off = r.bounds.center - merchant.position;
                off.y = 0f;
                if (off.magnitude > 3f) continue;
                if (!found) { b = r.bounds; found = true; }
                else b.Encapsulate(r.bounds);
            }
            Vector3 face = new Vector3(b.center.x, b.max.y - b.size.y * faceFromTop, b.center.z);
            float chinY = face.y - b.size.y * 0.15f;
            // 머리 뼈(Head)가 있으면 그걸로 눈·턱 높이를 잼 (귀·후드가 큰 모델도 정확하게)
            FindHeadBones(merchant, out Transform head, out Transform headEnd);
            if (head != null)
            {
                float topY = headEnd != null && headEnd.position.y > head.position.y ? headEnd.position.y : b.max.y;
                face = new Vector3(head.position.x, Mathf.Lerp(head.position.y, topY, eyeInHead), head.position.z);
                chinY = head.position.y;
            }

            // 플레이어 쪽에서 바라봄 (플레이어가 없으면 상인 정면)
            Vector3 dir = playerTransform != null ? playerTransform.position - merchant.position : merchant.forward;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = merchant.forward;
            dir.Normalize();

            // 거리: 상인 키의 visibleHeightRatio만큼이 화면 세로에 들어오게 (화각 기준), 최소 closeupDistance
            float fov = closeupFov > 0f ? closeupFov : (Camera.main != null ? Camera.main.fieldOfView : 60f);
            float fitDistance = (b.size.y * visibleHeightRatio * 0.5f) / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            float distance = Mathf.Max(closeupDistance, fitDistance);
            // 턱(대사 상자 위) ~ 머리 꼭대기·귀(화면 위 끝)가 한 화면에 들어오도록 필요하면 조금 더 뒤로
            float boxTopY = boxCover * 2f - 1f + 0.06f;
            float headSpan = Mathf.Max(0.01f, b.max.y - chinY);
            float headFit = headSpan / Mathf.Max(0.2f, 0.95f - boxTopY) / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            distance = Mathf.Max(distance, headFit);
            float pitch = closeupPitch * Mathf.Deg2Rad;
            position = face + (dir * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch)) * distance;
            Vector3 forward = (face - position).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            // 바라보는 점을 오른쪽·아래로 옮기면 화면에서 상인이 왼쪽·위로 감 (아래 대화 상자, 오른쪽 선택지 자리 비우기)
            float halfH = distance * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            float aspect = Camera.main != null ? Camera.main.aspect : 16f / 9f;
            // 턱이 대사 상자 위에 오도록 필요한 만큼 상인을 더 위로 (화면 세로 -1 ~ 1 기준)
            float lift = framing.y;
            float chinOnScreen = (chinY - face.y) / halfH;
            lift = Mathf.Max(lift, boxTopY - chinOnScreen);
            lift = Mathf.Min(lift, 0.9f);
            Vector3 look = face + right * (framing.x * halfH * aspect) - Vector3.up * (lift * halfH);
            rotation = Quaternion.LookRotation(look - position, Vector3.up);
        }

        // 머리 뼈 찾기: Head (Meshy·Mixamo 이름), 머리 꼭대기 head_end / HeadTop_End
        private static void FindHeadBones(Transform root, out Transform head, out Transform headEnd)
        {
            head = headEnd = null;
            foreach (Transform t in root.GetComponentsInChildren<Transform>())
            {
                string n = t.name.ToLowerInvariant();
                int colon = n.LastIndexOf(':');
                if (colon >= 0) n = n.Substring(colon + 1);
                if (head == null && (n == "head" || n.EndsWith("_head") || n == "bip001 head")) head = t;
                else if (headEnd == null && n.Contains("head") && (n.Contains("end") || n.Contains("top"))) headEnd = t;
            }
        }

        // 핫바·십자선 숨기기 (투명도만 0 - 스크립트 동작에는 영향 없음)
        private void HideHud()
        {
            ShowHud();
            var targets = new List<GameObject>();
            foreach (HotbarUI h in FindObjectsByType<HotbarUI>(FindObjectsInactive.Exclude)) targets.Add(h.gameObject);
            foreach (CrosshairUI c in FindObjectsByType<CrosshairUI>(FindObjectsInactive.Exclude)) targets.Add(c.gameObject);
            foreach (GameObject g in targets)
            {
                CanvasGroup cg = g.GetComponent<CanvasGroup>();
                if (cg == null) cg = g.AddComponent<CanvasGroup>();
                if (hiddenGroups.Contains(cg)) continue;
                hiddenGroups.Add(cg);
                hiddenAlphas.Add(cg.alpha);
                cg.alpha = 0f;
                cg.blocksRaycasts = false;
            }
        }

        private void ShowHud()
        {
            for (int i = 0; i < hiddenGroups.Count; i++)
            {
                if (hiddenGroups[i] == null) continue;
                hiddenGroups[i].alpha = hiddenAlphas[i];
                hiddenGroups[i].blocksRaycasts = true;
            }
            hiddenGroups.Clear();
            hiddenAlphas.Clear();
        }

        /// <summary>"구매" 버튼 OnClick에 연결</summary>
        public void OnChooseBuy()
        {
            CloseDialogue();
            SetPanelHiddenObjects(true);
            MoveCameraForPanel(false);
            if (buyPanel != null) buyPanel.Open();
            Debug.Log("[상점] 구매 선택");
        }

        /// <summary>"판매" 버튼 OnClick에 연결</summary>
        public void OnChooseSell()
        {
            CloseDialogue();
            SetPanelHiddenObjects(true);
            MoveCameraForPanel(true);
            if (sellPanel != null) sellPanel.Open();
            Debug.Log("[상점] 판매 선택");
        }

        /// <summary>"아무것도 아니다" 버튼 OnClick에 연결 - 플레이 화면으로 복귀</summary>
        public void OnChooseNothing()
        {
            EndSession();
            Debug.Log("[상점] 대화 종료 (아무것도 안 함)");
        }

        private void EndSession()
        {
            CloseDialogue();
            if (buyPanel != null) buyPanel.Close();
            if (sellPanel != null) sellPanel.Close();

            IsSessionOpen = false;
            SetPanelHiddenObjects(false);
            EndCloseup();
            UIInputBlocker.Unblock(this);
        }
    }
}