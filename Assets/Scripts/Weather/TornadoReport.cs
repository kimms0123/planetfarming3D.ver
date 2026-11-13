using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using FarmingSystem.Farming;

namespace FarmingSystem.Weather
{
    // 태풍 피해 알림 (PV 컷 10~12 + 실제 게임에서도 사용)
    // - 태풍이 시작되면: 화면 위에 "태풍 발생!" 경보
    // - 태풍이 끝나면(다음 날): "농작물 N% 손실" (숫자가 0부터 올라감) → "남은 작물 품질 하락" 카드
    //   (태풍 시작 때 작물 수와 끝났을 때 작물 수를 비교. 회오리가 지나간 만큼 손실)
    // - PV 촬영용 T키: 태풍을 부르고 → 하늘이 어두워진 뒤 → 작물 70%가 바람에 날아가고 → 남은 작물은 피해 표시 → 결과 알림
    //   (회오리가 무작위로 지나가서 찍기 어려울 때, 매번 같은 장면을 만들 수 있음)
    // 씬에 빈 오브젝트 하나 만들고 이 스크립트만 붙이면 됨 (알림 화면은 코드가 만듦)
    public class TyphoonReport : MonoBehaviour
    {
        [Header("PV 촬영")]
        [Tooltip("이 키를 누르면 태풍 피해 장면 연출 (None = 끔). 에디터·Development Build에서만 작동하고 출시 빌드에선 꺼짐")]
        [SerializeField] private Key pvKey = Key.T;
        [Tooltip("날아갈 작물 비율 (0.7 = 70%)")]
        [SerializeField, Range(0f, 1f)] private float pvLossRatio = 0.7f;
        [Tooltip("태풍을 부른 뒤 작물이 날아가기 시작할 때까지 (초) - 하늘이 어두워지는 시간")]
        [SerializeField] private float pvWindUpTime = 2.5f;
        [Tooltip("작물이 하나둘 날아가는 전체 시간 (초)")]
        [SerializeField] private float pvBlowTime = 2.5f;
        [Tooltip("캐릭터에게서 이 거리(m) 안의 작물만 대상 (화면에 보이는 밭). 근처에 없으면 맵 전체")]
        [SerializeField] private float pvRadius = 25f;
        [Tooltip("회오리를 밭 한가운데로 지나가게 함 (작물은 회오리가 지나가는 순간 날아감)")]
        [SerializeField] private bool pvMoveTornado = true;
        [Tooltip("회오리가 밭을 가로지르는 시간 (초)")]
        [SerializeField] private float pvPassTime = 6f;
        [Tooltip("회오리가 밭 가장자리에서 이만큼(m) 떨어진 곳에서 출발")]
        [SerializeField] private float pvTornadoLead = 10f;

        [Header("실제 게임")]
        [Tooltip("태풍이 시작될 때 경보 표시")]
        [SerializeField] private bool showWarning = true;
        [Tooltip("태풍이 끝날 때 피해 결과 표시")]
        [SerializeField] private bool showReport = true;

        [Header("문구")]
        [SerializeField] private string warningTitle = "태풍 발생!";
        [SerializeField] private string warningSub = "오늘은 밭일을 쉬어야 해요";
        [Tooltip("{0} = 손실 퍼센트")]
        [SerializeField] private string lossTitle = "농작물 {0}% 손실";
        [SerializeField] private string damagedTitle = "남은 작물 품질 하락";
        [Tooltip("{0} = 피해 입은 작물 수")]
        [SerializeField] private string damagedSub = "피해 작물 {0}개 · 최대 '기본' 등급";
        [SerializeField] private string noDamageTitle = "다행히 피해가 없어요!";

        [Header("모양")]
        [Tooltip("알림 판 이미지 (비우면 반투명 검은 판). 그래픽 팀 Panel 이미지를 넣어도 됨")]
        [SerializeField] private Sprite panelSprite;
        [SerializeField] private Color panelColor = new Color(0.08f, 0.06f, 0.12f, 0.72f);
        [SerializeField] private Color dangerColor = new Color(1f, 0.42f, 0.32f, 1f);
        [SerializeField] private Color damagedColor = new Color(1f, 0.82f, 0.4f, 1f);
        [SerializeField] private float showSeconds = 3.5f;

        private int cropsAtStart = -1;
        private WeatherManager subscribed;
        private bool pvRunning;

        private Canvas canvas;
        private RectTransform mainCard, subCard;
        private TextMeshProUGUI mainTitle, mainSub, subTitle, subSub;
        private CanvasGroup mainGroup, subGroup;
        private Coroutine showRoutine;

        private void Awake() => BuildUI();

        private void Update()
        {
            if (subscribed == null && WeatherManager.Instance != null)
            {
                subscribed = WeatherManager.Instance;
                subscribed.OnWeatherChanged += HandleWeatherChanged;
                if (subscribed.IsTyphoon) cropsAtStart = CountCrops();
            }
            if (Debug.isDebugBuild && pvKey != Key.None && Keyboard.current != null && Keyboard.current[pvKey].wasPressedThisFrame && !pvRunning)
                StartCoroutine(PVSequence());
        }

        /// <summary>PV 자동 촬영용: 태풍 피해 연출 시작 (T 키와 같음). windUp ≥ 0이면 하늘이 어두워지는 시간을 그 값으로</summary>
        public void PlayPV(float windUp = -1f)
        {
            if (pvRunning) return;
            if (windUp >= 0f) pvWindUpTime = windUp;
            StartCoroutine(PVSequence());
        }

        /// <summary>태풍 피해 연출이 진행 중인지</summary>
        public bool IsPVRunning => pvRunning;

        private void OnDestroy()
        {
            if (subscribed != null) subscribed.OnWeatherChanged -= HandleWeatherChanged;
        }

        // ---------- 실제 게임 ----------

        private void HandleWeatherChanged(WeatherType type)
        {
            if (pvRunning) return;
            if (type == WeatherType.Typhoon)
            {
                cropsAtStart = CountCrops();
                if (showWarning) ShowWarning();
            }
            else if (cropsAtStart >= 0)
            {
                int lost = Mathf.Max(0, cropsAtStart - CountCrops());
                if (showReport && cropsAtStart > 0) ShowReport(lost, cropsAtStart, CountDamaged());
                cropsAtStart = -1;
            }
        }

        private static int CountCrops()
        {
            int n = 0;
            foreach (CropInstance c in FindObjectsByType<CropInstance>(FindObjectsInactive.Exclude))
                if (c != null && !c.IsWithered) n++;
            return n;
        }

        private static int CountDamaged()
        {
            int n = 0;
            foreach (CropInstance c in FindObjectsByType<CropInstance>(FindObjectsInactive.Exclude))
                if (c != null && c.IsStormDamaged && !c.IsWithered) n++;
            return n;
        }

        // ---------- PV 연출 ----------

        private IEnumerator PVSequence()
        {
            pvRunning = true;

            // 1) 화면에 보이는 밭(캐릭터·카메라 근처)의 살아 있는 작물
            Vector3 focus = FocusPoint();
            var tiles = new List<FarmTile>();
            var all = new List<FarmTile>();
            foreach (FarmTile t in FindObjectsByType<FarmTile>(FindObjectsInactive.Exclude))
            {
                if (t.CurrentCrop == null || t.CurrentCrop.IsWithered) continue;
                all.Add(t);
                if (Flat(t.Center - focus).magnitude <= pvRadius) tiles.Add(t);
            }
            if (tiles.Count == 0) tiles = all; // 근처에 없으면 맵 전체
            int total = tiles.Count;
            if (total == 0)
            {
                Debug.LogWarning("[태풍 피해 연출] 밭에 살아 있는 작물이 없어요. 작물을 심고 다시 T를 눌러 주세요");
                pvRunning = false;
                yield break;
            }

            // 2) 태풍 시작 + 경보
            WeatherManager wm = WeatherManager.Instance;
            if (wm != null && !wm.IsTyphoon) wm.SetWeatherToday(WeatherType.Typhoon);
            ShowWarning();

            // 3) 회오리가 밭 한가운데를 바람 방향으로 가로지르는 길
            Vector3 center = Vector3.zero;
            foreach (FarmTile t in tiles) center += t.Center;
            center /= total;
            float spread = 0f;
            foreach (FarmTile t in tiles) spread = Mathf.Max(spread, Flat(t.Center - center).magnitude);
            Vector2 w = WeatherVisuals.WindDirection.sqrMagnitude > 0.01f ? WeatherVisuals.WindDirection.normalized : Vector2.right;
            Vector3 wind = new Vector3(w.x, 0f, w.y);
            Vector3 side = Vector3.Cross(Vector3.up, wind);
            float half = spread + pvTornadoLead;
            Vector3 from = center - wind * half;
            Vector3 to = center + wind * half;
            float pass = Mathf.Max(1f, pvPassTime);

            yield return new WaitForSeconds(pvWindUpTime); // 하늘이 어두워지는 동안

            WeatherTornado tornado = pvMoveTornado ? FindAnyObjectByType<WeatherTornado>() : null;
            if (tornado != null) tornado.PVPlace(from, to, half * 2f / pass, false); // 작물은 아래에서 직접 날림 (회오리 자체 피해는 끔)
            else if (pvMoveTornado) Debug.Log("[태풍 피해 연출] 씬에 회오리(WeatherTornado)가 없어서 작물만 날아가요");

            // 4) 길에 가까운 작물부터 날아감 (회오리가 지나간 자리가 휑하게), 나머지는 피해 표시
            tiles.Sort((a, b) => Mathf.Abs(Vector3.Dot(a.Center - center, side)).CompareTo(Mathf.Abs(Vector3.Dot(b.Center - center, side))));
            int loseCount = Mathf.RoundToInt(total * pvLossRatio);
            Debug.Log($"[태풍 피해 연출] 시작 - 작물 {total}개 중 {loseCount}개 날아감, 회오리 {(tornado != null ? "있음" : "없음")}");
            float lastBlow = 0f;
            for (int i = 0; i < total; i++)
            {
                FarmTile t = tiles[i];
                // 회오리가 그 작물 옆을 지나는 순간 (회오리가 없으면 고르게 흩어서)
                float along = Mathf.Clamp01(Vector3.Dot(t.Center - from, wind) / (half * 2f));
                float when = tornado != null ? along * pass - 0.3f : Random.Range(0f, pvBlowTime);
                when = Mathf.Max(0f, when + Random.Range(-0.15f, 0.15f));
                if (i < loseCount)
                {
                    StartCoroutine(BlowAway(t, wind, when, tornado));
                    lastBlow = Mathf.Max(lastBlow, when + 1.7f);
                }
                else t.CurrentCrop.MarkStormDamaged();
            }

            // 5) 다 날아가면 결과 카드
            yield return new WaitForSeconds(Mathf.Max(lastBlow, 1f) + 0.8f);
            ShowReport(loseCount, total, total - loseCount);
            yield return new WaitForSeconds(showSeconds + 3f);
            if (tornado != null) tornado.PVRelease();
            pvRunning = false;
        }

        // 캐릭터 위치 (없으면 카메라가 보는 곳)
        private static Vector3 FocusPoint()
        {
            var player = FindAnyObjectByType<FarmingSystem.Player.PlayerController>();
            if (player != null) return player.transform.position;
            Camera cam = Camera.main;
            return cam != null ? cam.transform.position + cam.transform.forward * 15f : Vector3.zero;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private void OnDisable() => pvRunning = false; // 중간에 꺼져도 다음에 T가 다시 되게

        // 작물 하나가 바람 방향으로 빙글빙글 날아가며 작아짐 → 밭에서 사라짐
        private IEnumerator BlowAway(FarmTile tile, Vector3 wind, float delay, WeatherTornado tornado = null)
        {
            yield return new WaitForSeconds(delay);
            if (tile == null || tile.CurrentCrop == null) yield break;
            if (tornado != null) { tornado.SuckUp(tile); yield break; } // 회오리가 있으면 회오리 속으로 빨려 올라감
            Transform t = tile.CurrentCrop.transform;
            Vector3 start = t.position;
            Vector3 startScale = t.localScale;
            Quaternion startRot = t.rotation;
            Vector3 spinAxis = Vector3.Cross(Vector3.up, wind).normalized;
            if (spinAxis.sqrMagnitude < 0.01f) spinAxis = Vector3.right;
            float dur = Random.Range(0.7f, 1.1f);
            float side = Random.Range(-1.5f, 1.5f);
            float time = 0f;
            while (time < dur && t != null)
            {
                time += Time.deltaTime;
                float k = Mathf.Clamp01(time / dur);
                // 처음엔 살짝 흔들리다가 확 뽑혀 날아감
                float lift = k < 0.2f ? 0f : (k - 0.2f) / 0.8f;
                Vector3 off = wind * (lift * lift * 9f) + Vector3.up * (Mathf.Sin(lift * Mathf.PI * 0.8f) * 3f) + Vector3.Cross(Vector3.up, wind) * side * lift;
                t.position = start + off;
                float wobble = k < 0.2f ? Mathf.Sin(time * 60f) * 12f : 0f;
                t.rotation = Quaternion.AngleAxis(wobble + lift * 720f, spinAxis) * startRot;
                t.localScale = startScale * Mathf.Lerp(1f, 0.2f, lift);
                yield return null;
            }
            if (tile != null) tile.DestroyCrop("태풍");
        }

        // ---------- 알림 화면 ----------

        private void ShowWarning()
        {
            if (showRoutine != null) StopCoroutine(showRoutine);
            showRoutine = StartCoroutine(ShowCards(warningTitle, warningSub, dangerColor, null, null, -1, 0));
        }

        private void ShowReport(int lost, int total, int damaged)
        {
            if (showRoutine != null) StopCoroutine(showRoutine);
            int percent = total > 0 ? Mathf.RoundToInt(lost * 100f / total) : 0;
            if (percent <= 0 && damaged <= 0)
            {
                showRoutine = StartCoroutine(ShowCards(noDamageTitle, null, damagedColor, null, null, -1, 0));
                return;
            }
            string sub2 = damaged > 0 ? string.Format(damagedSub, damaged) : null;
            showRoutine = StartCoroutine(ShowCards(warningTitle, null, dangerColor,
                damaged > 0 ? damagedTitle : null, sub2, percent, 0));
        }

        // 위 카드(큰 제목 + 손실 % 숫자 올라감) → 잠시 뒤 아래 카드(품질 하락)
        private IEnumerator ShowCards(string title, string sub, Color color, string title2, string sub2, int percent, int _)
        {
            canvas.gameObject.SetActive(true);
            canvas.enabled = true; // F2(UI 숨기기)로 꺼졌던 경우에도 보이게
            mainTitle.color = color;
            mainTitle.text = title;
            bool hasLoss = percent >= 0;
            mainSub.gameObject.SetActive(hasLoss || !string.IsNullOrEmpty(sub));
            mainSub.text = hasLoss ? string.Format(lossTitle, 0) : sub;
            subCard.gameObject.SetActive(false);

            // 위 카드: 위에서 툭 떨어지며 흔들림
            float t = 0f;
            Vector2 home = new Vector2(0f, -120f);
            while (t < 0.45f)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / 0.45f);
                float e = 1f - Mathf.Pow(1f - k, 3f);
                mainGroup.alpha = k;
                mainCard.anchoredPosition = home + new Vector2(Mathf.Sin(t * 45f) * 10f * (1f - k), 80f * (1f - e));
                yield return null;
            }
            mainCard.anchoredPosition = home;

            // 손실 %: 0부터 올라감
            if (hasLoss)
            {
                t = 0f;
                while (t < 1f)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.Clamp01(t);
                    int shown = Mathf.RoundToInt(percent * (1f - (1f - k) * (1f - k)));
                    mainSub.text = string.Format(lossTitle, shown);
                    mainSub.rectTransform.localScale = Vector3.one * (1f + 0.15f * Mathf.Sin(k * Mathf.PI));
                    yield return null;
                }
                mainSub.text = string.Format(lossTitle, percent);
                mainSub.rectTransform.localScale = Vector3.one;
            }

            // 아래 카드: 품질 하락
            if (!string.IsNullOrEmpty(title2))
            {
                yield return new WaitForSecondsRealtime(0.6f);
                subCard.gameObject.SetActive(true);
                subTitle.text = title2;
                subTitle.color = damagedColor;
                subSub.gameObject.SetActive(!string.IsNullOrEmpty(sub2));
                subSub.text = sub2;
                t = 0f;
                while (t < 0.35f)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.Clamp01(t / 0.35f);
                    subGroup.alpha = k;
                    subCard.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, 1f - (1f - k) * (1f - k));
                    yield return null;
                }
            }

            yield return new WaitForSecondsRealtime(showSeconds);

            // 사라짐
            t = 0f;
            while (t < 0.5f)
            {
                t += Time.unscaledDeltaTime;
                float a = 1f - Mathf.Clamp01(t / 0.5f);
                mainGroup.alpha = a;
                subGroup.alpha = Mathf.Min(subGroup.alpha, a);
                yield return null;
            }
            canvas.gameObject.SetActive(false);
            showRoutine = null;
        }

        private void BuildUI()
        {
            var go = new GameObject("TyphoonReportCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 600;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            TMP_FontAsset font = null;
            foreach (TextMeshProUGUI tx in FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include))
                if (tx.font != null && !tx.font.name.StartsWith("LiberationSans")) { font = tx.font; break; }

            mainCard = MakeCard("MainCard", go.transform, new Vector2(760f, 190f), new Vector2(0f, -120f), out mainGroup);
            mainTitle = MakeText("Title", mainCard, font, 64f, new Vector2(0f, 30f));
            mainSub = MakeText("Sub", mainCard, font, 44f, new Vector2(0f, -45f));

            subCard = MakeCard("SubCard", go.transform, new Vector2(600f, 130f), new Vector2(0f, -330f), out subGroup);
            subTitle = MakeText("Title", subCard, font, 44f, new Vector2(0f, 18f));
            subSub = MakeText("Sub", subCard, font, 28f, new Vector2(0f, -32f));
            subSub.color = new Color(1f, 1f, 1f, 0.85f);

            go.SetActive(false);
        }

        private RectTransform MakeCard(string name, Transform parent, Vector2 size, Vector2 pos, out CanvasGroup group)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            Image img = go.GetComponent<Image>();
            img.raycastTarget = false;
            if (panelSprite != null)
            {
                img.sprite = panelSprite;
                img.type = panelSprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
                img.color = Color.white;
            }
            else img.color = panelColor;
            group = go.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            return rt;
        }

        private static TextMeshProUGUI MakeText(string name, RectTransform parent, TMP_FontAsset font, float size, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(parent.sizeDelta.x - 40f, size * 1.4f);
            rt.anchoredPosition = pos;
            var t = go.GetComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.fontSize = size;
            t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Color.white;
            t.outlineWidth = 0.18f;
            t.outlineColor = new Color32(30, 20, 30, 255);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            return t;
        }
    }
}