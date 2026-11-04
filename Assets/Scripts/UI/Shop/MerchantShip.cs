using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FarmingSystem.Building;
using FarmingSystem.Player;

namespace FarmingSystem.Economy
{
    // 상인 우주선 (상인이 타고 온 배)
    // - 상인이 오는 날(상점이 열린 날)에만 상인과 우주선이 나타남. 상인이 떠나면 둘 다 사라짐
    //   (상인 오브젝트 자체는 꺼지지 않고 모델·충돌체만 안 보이게 함 → 이 스크립트가 계속 날짜를 확인할 수 있음)
    // - 게임 중에 상인이 도착/출발하면 하늘에서 내려앉고 / 하늘로 떠오르는 연출 (끌 수 있음)
    // - 땅 가까이 오면 헬기 착륙처럼 바람에 먼지가 사방으로 퍼짐 (가까울수록 진하게, 닿는 순간 한 번 크게 퍼짐). 떠날 때도 똑같이
    // - 우주선은 상인 등 뒤(상인이 바라보는 반대쪽)에 상인과 같은 방향으로 놓이고, 땅 높이에 맞춰 바닥에 붙음
    // - 걸어서 통과하지 못하게 우주선 크기의 충돌 상자를 붙임
    // 붙이는 곳: 상인 오브젝트(NPC_Merchant, ShopInteractable이 붙은 오브젝트)
    public class MerchantShip : MonoBehaviour
    {
        [Header("우주선")]
        [Tooltip("상인 우주선 모델 (지금은 fox_spaceship 프리팹)")]
        [SerializeField] private GameObject shipPrefab;
        [Tooltip("우주선 가로 길이(m). 모델 크기와 상관없이 이 길이에 맞춰 크기를 맞춤 (칸 1개 ≈ 1.94m)")]
        [SerializeField] private float shipWidth = 10f;
        [Tooltip("우주선 방향 보정 (도). 우주선 정면이 이상한 쪽을 보면 90/180/270")]
        [SerializeField] private float shipYaw = 0f;

        [Header("위치 (상인 기준)")]
        [Tooltip("비우면 이 오브젝트(상인)")]
        [SerializeField] private Transform merchant;
        [Tooltip("상인 등과 우주선 사이 거리(m)")]
        [SerializeField] private float gapBehind = 1.0f;
        [Tooltip("옆으로 비켜 놓기(m). +는 상인 기준 오른쪽, -는 왼쪽")]
        [SerializeField] private float sideOffset = 0f;
        [Tooltip("땅에서 살짝 띄우기(m). 착륙 다리가 땅에 파묻히면 올리기")]
        [SerializeField] private float heightOffset = 0f;

        [Header("연출")]
        [Tooltip("게임 중에 도착/출발할 때 하늘에서 내려오고 올라가는 연출")]
        [SerializeField] private bool animate = true;
        [SerializeField] private float flyHeight = 25f;
        [SerializeField] private float flyDuration = 3f;

        [Header("착륙 먼지 (헬기 착륙처럼)")]
        [Tooltip("내려오고 떠날 때 땅 먼지가 사방으로 퍼짐")]
        [SerializeField] private bool dust = true;
        [Tooltip("먼지 재질. 날씨 도구가 만든 Assets/Weather/StormPuff 재질을 넣으면 됨 (비우면 자동으로 만들어 씀)")]
        [SerializeField] private Material dustMaterial;
        [Tooltip("먼지 색 (흙·모래색). 알파 = 진하기")]
        [SerializeField] private Color dustColor = new Color(0.8f, 0.72f, 0.6f, 0.7f);
        [Tooltip("땅에서 이 높이(m)보다 가까워지면 먼지가 일기 시작")]
        [SerializeField] private float dustStartHeight = 14f;
        [Tooltip("가장 진할 때 1초에 나오는 먼지 개수")]
        [SerializeField] private float dustRate = 80f;
        [Tooltip("먼지가 바깥으로 퍼지는 빠르기 (m/s)")]
        [SerializeField] private float dustSpeed = 10f;
        [Tooltip("먼지 덩어리 크기 (m)")]
        [SerializeField] private float dustSize = 2.4f;
        [Tooltip("땅에 닿는 순간 한 번에 퍼지는 먼지 개수")]
        [SerializeField] private int touchdownBurst = 60;

        [Header("상인도 같이 등장·퇴장")]
        [Tooltip("켜면 상인이 오는 날에만 상인이 보임. 도착: 우주선이 내려앉은 뒤 상인 등장 / 출발: 상인이 먼저 사라지고 우주선이 떠남")]
        [SerializeField] private bool hideMerchantWhenAway = true;

        private GameObject shipRoot;   // 위치·연출용
        private Vector3 landedPos;
        private bool shown;
        private bool initialized;
        private Coroutine flying;
        private BoxCollider blocker;
        private ParticleSystem dustPS;
        private Vector3 dustCenter;     // 우주선 아래 땅 가운데
        private float dustRadius = 4f;  // 우주선 크기 반지름
        private float dustCarry;        // 이번 프레임에 못 낸 먼지 개수(소수점) 이어 붙이기

        private void Awake()
        {
            if (merchant == null) merchant = transform;
        }

        private void Start()
        {
            // 처음 상태는 연출 없이 바로 맞춤 (불러오자마자 상인 날이면 이미 서 있게)
            Apply(IsVisitDay, false);
            initialized = true;
        }

        // 상점 열림 여부를 매 프레임 확인 (이벤트 구독 순서 문제 없음)
        private void Update()
        {
            if (!initialized) return;
            bool open = IsVisitDay;
            if (open != shown) Apply(open, animate);
        }

        private static bool IsVisitDay => ShopManager.Instance != null && ShopManager.Instance.IsShopOpen;

        private void Apply(bool show, bool withAnimation)
        {
            shown = show;
            // 상인 보이기: 도착 연출 중이면 착륙한 뒤에 보임 (Fly 끝에서), 떠날 땐 바로 사라짐
            if (hideMerchantWhenAway) SetMerchantVisible(show && !withAnimation);
            if (flying != null) { StopCoroutine(flying); flying = null; }

            if (show)
            {
                if (shipRoot == null) CreateShip();
                if (shipRoot == null) { if (hideMerchantWhenAway) SetMerchantVisible(true); return; }
                shipRoot.SetActive(true); // 꺼진 상태에선 크기를 못 재서 먼저 켬
                PlaceShip();
                if (withAnimation) flying = StartCoroutine(Fly(landedPos + Vector3.up * flyHeight, landedPos, true));
                else shipRoot.transform.position = landedPos;
                Debug.Log("[상인 우주선] 도착");
            }
            else if (shipRoot != null && shipRoot.activeSelf)
            {
                if (withAnimation)
                {
                    if (dust) DustBurst(touchdownBurst / 2, 0.9f); // 시동 걸 때 한 번
                    flying = StartCoroutine(Fly(shipRoot.transform.position, landedPos + Vector3.up * flyHeight, false));
                }
                else shipRoot.SetActive(false);
                Debug.Log("[상인 우주선] 출발");
            }
        }

        // ---------- 만들기·배치 ----------

        private void CreateShip()
        {
            if (shipPrefab == null)
            {
                Debug.LogWarning("[상인 우주선] Ship Prefab이 비어 있어요. 상인 오브젝트의 MerchantShip에 우주선 프리팹을 넣어 주세요");
                return;
            }
            // 상인의 자식으로 두지 않음 (상인 크기·숨기기에 같이 영향받지 않게)
            shipRoot = new GameObject("MerchantShip");
            GameObject model = Instantiate(shipPrefab, shipRoot.transform, false);
            model.name = shipPrefab.name;

            // 우주선 안의 스크립트가 있으면 끔 (모델만 보여 주기)
            foreach (MonoBehaviour mb in model.GetComponentsInChildren<MonoBehaviour>()) mb.enabled = false;

            blocker = shipRoot.AddComponent<BoxCollider>();
            int layer = LayerMask.NameToLayer("FarmingBlocker");
            if (layer >= 0) shipRoot.layer = layer;
        }

        private void PlaceShip()
        {
            Transform t = shipRoot.transform;
            Vector3 fwd = Flat(merchant.forward);
            Vector3 right = Vector3.Cross(Vector3.up, fwd);

            // 1. 방향·크기 맞추기 (상인 위치에서 먼저 맞춘 뒤 뒤로 옮김)
            t.position = merchant.position;
            t.rotation = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(0f, shipYaw, 0f);
            t.localScale = Vector3.one;
            Transform model = t.GetChild(0);
            model.localPosition = Vector3.zero;
            model.localScale = shipPrefab.transform.localScale;

            if (!BuildingSystem.TryGetBounds(model, out Bounds b)) return;
            // 가로 길이 = 상인 기준 좌우 방향 길이
            float widthNow = Mathf.Abs(right.x) * b.size.x + Mathf.Abs(right.z) * b.size.z;
            if (widthNow > 0.001f) model.localScale *= shipWidth / widthNow;

            // 2. 다시 재서 우주선 앞뒤 길이만큼 뒤로 + 가운데 맞추기
            BuildingSystem.TryGetBounds(model, out b);
            float depth = Mathf.Abs(fwd.x) * b.size.x + Mathf.Abs(fwd.z) * b.size.z;
            Vector3 target = merchant.position - fwd * (gapBehind + depth * 0.5f) + right * sideOffset;
            model.position += new Vector3(target.x - b.center.x, 0f, target.z - b.center.z);

            // 3. 땅 높이 찾아서 바닥 붙이기 (모델의 맨 아래 = 땅)
            float groundY = FindGroundY(target, b.size);
            dustCenter = new Vector3(target.x, groundY, target.z);
            dustRadius = Mathf.Max(b.size.x, b.size.z) * 0.5f;
            BuildingSystem.TryGetBounds(model, out b);
            model.position += Vector3.up * (groundY + heightOffset - b.min.y);

            // 4. 충돌 상자 = 모델 크기 (shipRoot 기준 좌표로)
            BuildingSystem.TryGetBounds(model, out b);
            if (blocker != null)
            {
                blocker.center = t.InverseTransformPoint(b.center);
                Vector3 local = Quaternion.Inverse(t.rotation) * b.size;
                blocker.size = new Vector3(Mathf.Abs(local.x), b.size.y, Mathf.Abs(local.z)) * 0.9f;
            }
            landedPos = t.position;
        }

        // 우주선 자리 아래 땅 높이 (여러 곳을 찍어서 가장 높은 땅 = 파묻히지 않게)
        private float FindGroundY(Vector3 center, Vector3 size)
        {
            float best = float.NegativeInfinity;
            float hx = size.x * 0.35f, hz = size.z * 0.35f;
            Vector3[] pts =
            {
                center, center + new Vector3(hx, 0, hz), center + new Vector3(-hx, 0, hz),
                center + new Vector3(hx, 0, -hz), center + new Vector3(-hx, 0, -hz),
            };
            foreach (Vector3 p in pts)
            {
                RaycastHit[] hits = Physics.RaycastAll(p + Vector3.up * 40f, Vector3.down, 80f, ~0, QueryTriggerInteraction.Ignore);
                System.Array.Sort(hits, (a, c) => a.distance.CompareTo(c.distance));
                foreach (RaycastHit h in hits)
                {
                    if (h.transform.IsChildOf(shipRoot.transform) || h.transform.IsChildOf(merchant)) continue;
                    if (h.collider.GetComponentInParent<PlayerController>() != null) continue;
                    if (h.point.y > best) best = h.point.y;
                    break;
                }
            }
            return float.IsNegativeInfinity(best) ? merchant.position.y : best;
        }

        // ---------- 연출 ----------

        private IEnumerator Fly(Vector3 from, Vector3 to, bool landing)
        {
            Transform t = shipRoot.transform;
            if (blocker != null) blocker.enabled = false;
            float time = 0f;
            while (time < flyDuration)
            {
                time += Time.deltaTime;
                float k = Mathf.Clamp01(time / flyDuration);
                float e = landing ? 1f - (1f - k) * (1f - k) * (1f - k)  // 내려올 땐 점점 느리게
                                  : k * k * k;                          // 떠날 땐 점점 빠르게
                t.position = Vector3.LerpUnclamped(from, to, e);
                if (dust) EmitDust(t.position.y - landedPos.y);
                yield return null;
            }
            t.position = to;
            if (landing)
            {
                if (dust) DustBurst(touchdownBurst, 1.3f); // 닿는 순간 크게 한 번
                if (blocker != null) blocker.enabled = true;
                if (hideMerchantWhenAway) SetMerchantVisible(true); // 착륙 후 상인 등장
            }
            else shipRoot.SetActive(false);
            flying = null;
        }

        // ---------- 착륙 먼지 ----------

        // 높이에 따라 먼지 양 조절 (땅에 가까울수록 많이·빠르게)
        private void EmitDust(float height)
        {
            float k = 1f - Mathf.Clamp01(height / Mathf.Max(0.1f, dustStartHeight));
            if (k <= 0f) return;
            k *= k; // 가까워질수록 급격히 진해짐
            dustCarry += dustRate * k * Time.deltaTime;
            int n = Mathf.FloorToInt(dustCarry);
            if (n <= 0) return;
            dustCarry -= n;
            Emit(n, 0.5f + 0.5f * k, 0.35f + 0.65f * k);
        }

        private void DustBurst(int count, float power) => Emit(count, power, 1f);

        // 우주선 아래 땅 둘레에서 바깥쪽으로 퍼지는 먼지 count개
        private void Emit(int count, float speedScale, float alphaScale)
        {
            ParticleSystem ps = GetDust();
            if (ps == null) return;

            // 밤에는 먼지도 어둡게 (빛을 안 받는 재질이라 그대로면 밤에 빛나 보임)
            Color amb = RenderSettings.ambientSkyColor;
            float light = Mathf.Clamp(0.25f + 1.2f * (0.3f * amb.r + 0.59f * amb.g + 0.11f * amb.b), 0.3f, 1f);
            Color c = new Color(dustColor.r * light, dustColor.g * light, dustColor.b * light, dustColor.a * alphaScale);

            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                float ang = Random.Range(0f, Mathf.PI * 2f);
                Vector3 dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                float r = dustRadius * Random.Range(0.25f, 0.9f);
                ep.position = dustCenter + dir * r + Vector3.up * Random.Range(0.2f, 0.6f);
                ep.velocity = dir * dustSpeed * speedScale * Random.Range(0.6f, 1.2f) + Vector3.up * Random.Range(0.3f, 1.6f);
                ep.startSize = dustSize * Random.Range(0.6f, 1.3f);
                ep.startLifetime = Random.Range(1.6f, 2.8f);
                ep.rotation = Random.Range(0f, 360f);
                ep.startColor = c;
                ps.Emit(ep, 1);
            }
        }

        // 먼지 파티클 (처음 쓸 때 한 번 만듦. 우주선과 따로 둬서 우주선이 사라져도 먼지는 마저 퍼짐)
        private ParticleSystem GetDust()
        {
            if (dustPS != null) return dustPS;
            Material mat = dustMaterial != null ? dustMaterial : MakeDustMaterial();
            if (mat == null) return null;

            var go = new GameObject("MerchantShipDust");
            dustPS = go.AddComponent<ParticleSystem>();
            dustPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = dustPS.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 1500;
            main.startSpeed = 0f;
            main.gravityModifier = -0.02f; // 아주 살짝 떠오름

            var emission = dustPS.emission;
            emission.enabled = false; // 코드에서 직접 뿌림
            var shape = dustPS.shape;
            shape.enabled = false;

            // 퍼지다가 공기 저항으로 점점 느려짐
            var limit = dustPS.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 100f;   // 속도 제한은 안 쓰고
            limit.dampen = 0f;
            limit.drag = 1.2f;    // 공기 저항만 (퍼지는 거리 ≈ 속도 ÷ 1.2)
            limit.multiplyDragByParticleSize = false;
            limit.multiplyDragByParticleVelocity = false;

            // 점점 커지면서
            var size = dustPS.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1.8f)));

            // 빨리 나타났다가 천천히 사라짐
            var col = dustPS.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            // 천천히 빙글
            var rot = dustPS.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);

            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.renderMode = ParticleSystemRenderMode.Billboard;
            rend.sharedMaterial = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;

            dustPS.Play();
            return dustPS;
        }

        // 재질을 안 넣었을 때: 부드러운 동그라미 그림으로 반투명 파티클 재질을 만듦
        private Material MakeDustMaterial()
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) { Debug.LogWarning("[상인 우주선] 먼지 재질을 만들 셰이더가 없어요. Dust Material에 Assets/Weather/StormPuff를 넣어 주세요"); return null; }

            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * (3f - 2f * a); // 가장자리 부드럽게
                    a *= 0.75f + 0.25f * Mathf.PerlinNoise(x * 0.15f, y * 0.15f); // 살짝 뭉게뭉게
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();

            var m = new Material(sh) { name = "MerchantShipDust (자동)" };
            m.SetTexture("_BaseMap", tex);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = 3000;
            dustMaterial = m;
            return m;
        }

        // ---------- 상인 숨기기 ----------

        private void SetMerchantVisible(bool v)
        {
            foreach (Renderer r in merchant.GetComponentsInChildren<Renderer>(true)) r.enabled = v;
            foreach (Collider c in merchant.GetComponentsInChildren<Collider>(true)) c.enabled = v;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude < 0.0001f ? Vector3.forward : v.normalized;
        }

        // 인스펙터에서 값을 바꾼 뒤 플레이 중에 바로 확인
        [ContextMenu("디버그: 우주선 위치 다시 맞추기")]
        private void DebugReplace()
        {
            if (shipRoot == null || !shown) { Debug.Log("[상인 우주선] 상인이 와 있을 때만 맞출 수 있어요"); return; }
            if (flying != null) { StopCoroutine(flying); flying = null; }
            PlaceShip();
            shipRoot.transform.position = landedPos;
            if (blocker != null) blocker.enabled = true;
            if (hideMerchantWhenAway) SetMerchantVisible(true);
        }

        private void OnDestroy()
        {
            if (shipRoot != null) Destroy(shipRoot);
            if (dustPS != null) Destroy(dustPS.gameObject);
        }

        [ContextMenu("디버그: 착륙 먼지만 보기")]
        private void DebugDust()
        {
            if (shipRoot == null) { Debug.Log("[상인 우주선] 상인이 와 있을 때 눌러 주세요 (먼지 위치를 우주선 자리로 잡음)"); return; }
            DustBurst(touchdownBurst, 1.3f);
        }
    }
}