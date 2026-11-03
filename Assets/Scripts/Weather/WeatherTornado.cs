using System.Collections.Generic;
using UnityEngine;
using FarmingSystem.Farming;

namespace FarmingSystem.Weather
{
    // 회오리 바람 기둥 (태풍 연출)
    // - 태풍이 오면 플레이어에게서 조금 떨어진 곳에 서서히 나타나 맵 위를 천천히 떠돌고, 태풍이 그치면 서서히 사라짐
    // - 기둥(Funnel): 아래는 좁고 위로 갈수록 넓어지며 빙글빙글 도는 구름 / 바닥(Dust): 땅에서 흙먼지가 휘몰아침
    // - 땅 높이에 맞춰 바닥이 붙어 다니고, 곡면 월드로 땅이 휘어 보이는 만큼 같이 내려감 (공중에 뜨지 않게)
    // - 가까이 오면 화면이 더 흔들림
    // - 밭을 지나가면 반경 안의 작물이 휩쓸림: 30% 확률로 날아가 사라지고, 70% 확률로 살아남지만 품질이 나빠짐
    //   (살아남은 작물은 수확할 때 등급이 한 단계 낮아지고 최대 '기본' - 색도 살짝 누렇게 변함)
    public class WeatherTornado : MonoBehaviour
    {
        [Header("연결 (허리케인 연출 추가 도구가 만들어 줌)")]
        [SerializeField] private ParticleSystem funnel;
        [SerializeField] private ParticleSystem dust;

        [Header("떠도는 범위")]
        [Tooltip("이 지점을 중심으로 떠돎. 비우면 이 오브젝트가 처음 놓인 위치")]
        [SerializeField] private Transform roamCenter;
        [Tooltip("중심에서 이 반경(m) 안에서만 움직임 (섬 크기에 맞추기)")]
        [SerializeField] private float roamRadius = 60f;
        [Tooltip("움직이는 속도 (m/s)")]
        [SerializeField] private float moveSpeed = 3f;
        [Tooltip("나타날 때 플레이어(카메라)에게서 떨어진 거리 (m)")]
        [SerializeField] private Vector2 spawnDistance = new Vector2(35f, 55f);

        [Header("땅")]
        [Tooltip("바닥 높이를 찾을 때 쓰는 레이어 (비우면 전부)")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Header("가까울 때 화면 흔들림")]
        [Tooltip("이 거리(m)보다 가까우면 화면이 흔들리기 시작")]
        [SerializeField] private float shakeDistance = 30f;
        [SerializeField] private float shakeStrength = 0.06f;

        [Header("작물 피해")]
        [Tooltip("회오리 중심에서 이 반경(m) 안의 작물이 휩쓸림")]
        [SerializeField] private float damageRadius = 4.5f;
        [Tooltip("휩쓸린 작물이 살아남을 확률 (살아남아도 품질이 나빠짐). 나머지는 날아가 사라짐")]
        [SerializeField, Range(0f, 1f)] private float surviveChance = 0.7f;
        [Tooltip("시든 작물도 날려 버릴지")]
        [SerializeField] private bool blowAwayWithered = true;

        [Header("나타나고 사라지는 빠르기")]
        [SerializeField] private float fadeSpeed = 0.4f;

        private float presence;       // 0 = 없음, 1 = 완전히 나타남
        private float funnelRate, dustRate;
        private Vector3 groundPos;    // 실제(평평한) 땅 위치
        private Vector3 target;
        private Vector3 home;
        private bool placed;
        private WeatherCameraShake shake;
        private FarmTile[] tiles;
        private readonly HashSet<FarmTile> hitTiles = new HashSet<FarmTile>(); // 한 번 나타났을 때 같은 칸은 한 번만
        private float damageTimer;

        private static readonly int CurveOriginId = Shader.PropertyToID("_CurveOrigin");
        private static readonly int CurveStrengthId = Shader.PropertyToID("_CurveStrength");
        private static readonly int CurveSideId = Shader.PropertyToID("_CurveSide");
        private static readonly int CurveFlatId = Shader.PropertyToID("_CurveFlat");
        private static readonly int CurveForwardId = Shader.PropertyToID("_CurveForward");

        private void Awake()
        {
            home = roamCenter != null ? roamCenter.position : transform.position;
            if (funnel != null) funnelRate = funnel.emission.rateOverTime.constant;
            if (dust != null) dustRate = dust.emission.rateOverTime.constant;
            SetEmission(0f);
        }

        private void Update()
        {
            float storm = WeatherVisuals.Storm;
            bool active = storm > 0.5f;

            // 태풍이 시작되면 카메라 근처 바람 위쪽에 자리를 잡음
            if (active && !placed) { PlaceNearCamera(); hitTiles.Clear(); tiles = null; }
            if (!active && presence <= 0.01f) placed = false;

            presence = Mathf.MoveTowards(presence, active ? 1f : 0f, fadeSpeed * Time.deltaTime);
            SetEmission(presence);
            if (!placed) return;

            // 천천히 목표 지점으로 이동, 도착하면 새 목표
            Vector3 flatPos = new Vector3(groundPos.x, 0f, groundPos.z);
            Vector3 flatTarget = new Vector3(target.x, 0f, target.z);
            if (Vector3.Distance(flatPos, flatTarget) < 2f) target = RandomPointInRange();
            flatPos = Vector3.MoveTowards(flatPos, flatTarget, moveSpeed * Time.deltaTime);
            groundPos = new Vector3(flatPos.x, GroundHeight(flatPos), flatPos.z);

            // 곡면 월드로 땅이 휘어 보이는 만큼 같이 내려감
            transform.position = groundPos + Vector3.down * CurveDrop(groundPos);

            // 다 나타난 상태에서만 작물 피해 (0.2초마다 확인)
            if (presence > 0.6f)
            {
                damageTimer -= Time.deltaTime;
                if (damageTimer <= 0f) { damageTimer = 0.2f; DamageCrops(flatPos); }
            }

            // 가까우면 화면 흔들림
            Camera cam = Camera.main;
            if (cam != null && shakeStrength > 0f)
            {
                float dist = Vector3.Distance(new Vector3(cam.transform.position.x, 0f, cam.transform.position.z), flatPos);
                float near = Mathf.Clamp01(1f - dist / Mathf.Max(shakeDistance, 0.1f)) * presence;
                if (near > 0f)
                {
                    if (shake == null) shake = cam.GetComponent<WeatherCameraShake>();
                    if (shake != null) shake.Shake(shakeStrength * near, 0.2f);
                }
            }
        }

        private void DamageCrops(Vector3 flatPos)
        {
            if (damageRadius <= 0f) return;
            if (tiles == null) tiles = FindObjectsByType<FarmTile>(FindObjectsInactive.Exclude);
            float r2 = damageRadius * damageRadius;
            foreach (FarmTile t in tiles)
            {
                if (t == null || t.CurrentCrop == null || hitTiles.Contains(t)) continue;
                Vector3 c = t.Center;
                float dx = c.x - flatPos.x, dz = c.z - flatPos.z;
                if (dx * dx + dz * dz > r2) continue;

                hitTiles.Add(t);
                CropInstance crop = t.CurrentCrop;
                if (crop.IsWithered)
                {
                    if (blowAwayWithered) t.DestroyCrop("회오리");
                    continue;
                }
                if (Random.value < surviveChance) crop.MarkStormDamaged();
                else t.DestroyCrop("회오리");
            }
        }

        private void PlaceNearCamera()
        {
            Camera cam = Camera.main;
            Vector3 c = cam != null ? cam.transform.position : home;
            Vector2 wind = WeatherVisuals.WindDirection;
            // 바람이 불어오는 쪽 + 약간 옆으로 → 화면 앞쪽에서 보이는 경우가 많게
            Vector3 dir = new Vector3(-wind.x, 0f, -wind.y);
            dir = Quaternion.Euler(0f, Random.Range(-60f, 60f), 0f) * dir;
            Vector3 p = c + dir.normalized * Random.Range(spawnDistance.x, spawnDistance.y);
            p = ClampToRange(p);
            groundPos = new Vector3(p.x, GroundHeight(p), p.z);
            target = RandomPointInRange();
            placed = true;
        }

        private Vector3 RandomPointInRange()
        {
            Vector2 r = Random.insideUnitCircle * roamRadius;
            return new Vector3(home.x + r.x, 0f, home.z + r.y);
        }

        private Vector3 ClampToRange(Vector3 p)
        {
            Vector3 d = new Vector3(p.x - home.x, 0f, p.z - home.z);
            if (d.magnitude > roamRadius) d = d.normalized * roamRadius;
            return new Vector3(home.x + d.x, p.y, home.z + d.z);
        }

        // 위에서 아래로 광선을 쏴서 땅 높이 찾기 (못 찾으면 중심 높이)
        private float GroundHeight(Vector3 p)
        {
            if (Physics.Raycast(new Vector3(p.x, home.y + 200f, p.z), Vector3.down, out RaycastHit hit, 400f, groundMask, QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return home.y;
        }

        // 곡면 셰이더와 같은 계산 (CurvedWorldController가 보내는 값을 읽음)
        private static float CurveDrop(Vector3 p)
        {
            float k = Shader.GetGlobalFloat(CurveStrengthId);
            if (k <= 0f) return 0f;
            Vector4 o = Shader.GetGlobalVector(CurveOriginId);
            Vector4 f = Shader.GetGlobalVector(CurveForwardId);
            float side = Shader.GetGlobalFloat(CurveSideId);
            float flat = Shader.GetGlobalFloat(CurveFlatId);
            float dx = p.x - o.x, dz = p.z - o.z;
            float z = dx * f.x + dz * f.z;
            float x = dx * f.z - dz * f.x;
            float r = Mathf.Max(Mathf.Sqrt(z * z + side * x * x) - flat, 0f);
            return r * r * k;
        }

        private void SetEmission(float amount)
        {
            if (funnel != null)
            {
                var e = funnel.emission;
                e.rateOverTime = funnelRate * amount;
                if (amount > 0.01f && !funnel.isPlaying) funnel.Play();
            }
            if (dust != null)
            {
                var e = dust.emission;
                e.rateOverTime = dustRate * amount;
                if (amount > 0.01f && !dust.isPlaying) dust.Play();
            }
        }
    }
}