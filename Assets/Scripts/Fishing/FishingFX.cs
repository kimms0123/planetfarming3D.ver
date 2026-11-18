using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 낚시 클립용 연출 (낚싯줄 · 찌 · 물보라 · 낚싯대 등장/퇴장 반짝임).
/// 캐릭터의 Animator와 같은 오브젝트(SpaceFarmer_AllClips)에 붙인다.
/// 낚싯대 자체가 뿅 커졌다 작아지는 것은 FBX 애니메이션(RodSocket 크기)에 들어 있고,
/// 이 스크립트는 애니메이션 이벤트를 받아 나머지 효과만 만든다.
/// 이벤트는 SpaceFarmerAllClipsImporter가 클립에 자동으로 넣는다.
///   Fishing_Equip       RodSpawn     (낚싯대가 나타나는 순간 반짝임)
///   Fishing_Cast        CastRelease  (찌가 앞으로 날아가 물에 떨어짐)
///   Fishing_Bite        Bite         (찌가 쏙 들어가며 물보라)
///   Fishing_Reel_Success Hook / FishOut (당김 물보라 → 물고기 튀어오름)
///   Fishing_Fail        LineSlack    (줄이 풀리며 찌가 돌아옴)
///   Fishing_Unequip     RodDespawn   (낚싯대가 사라지는 순간 반짝임)
/// </summary>
[DisallowMultipleComponent]
public class FishingFX : MonoBehaviour
{
    [Header("낚싯대 (비우면 이름으로 찾음)")]
    public Transform rodTip;            // "RodTip"
    public Transform rod;               // "FishingRod"

    [Header("찌 · 줄")]
    [Tooltip("캐릭터 앞쪽으로 찌가 떨어지는 거리 (m)")]
    public float castDistance = 4.5f;
    [Tooltip("찌가 날아가는 시간 (초)")]
    public float castFlightTime = 0.55f;
    [Tooltip("물/땅 높이를 찾을 레이어. 물 콜라이더가 없으면 캐릭터 발 높이에 떨어짐")]
    public LayerMask waterMask = ~0;
    public float waterLevelOffset = 0f;
    public Color lineColor = new Color(0.95f, 0.95f, 0.95f, 0.9f);
    public float lineWidth = 0.012f;
    public Color bobberTop = new Color(0.93f, 0.25f, 0.2f);
    public Color bobberBottom = Color.white;
    public float bobberSize = 0.16f;
    [Tooltip("넣으면 기본 구 대신 사용 (Props/Bobber.fbx). 피벗이 위쪽 끝")]
    public GameObject bobberPrefab;

    [Header("물고기 (선택)")]
    [Tooltip("넣으면 FishOut 때 물에서 튀어올라 낚싯대 끝에 매달림 (Props/CatchFish.fbx, 피벗이 입). 비우면 물보라만")]
    public GameObject fishPrefab;
    [Tooltip("낚싯대 끝에서 물고기 입까지 줄 길이 (m)")]
    public float fishHangLength = 0.35f;

    [Header("색")]
    public Color sparkleColor = new Color(1f, 0.93f, 0.55f, 1f);
    public Color splashColor = new Color(0.75f, 0.92f, 1f, 0.9f);

    [Header("이벤트 (사운드 등)")]
    public UnityEvent onRodSpawn, onCast, onBobberLand, onBite, onHook, onFishOut, onLineSlack, onRodDespawn;

    // ---- 게임 연동 (행성농사일지 FishingManager) ----
    bool _useCastTarget;
    Vector3 _castTarget;

    /// <summary>다음 CastRelease 때 찌가 떨어질 자리 (게임이 고른 물 위). 안 부르면 원래대로 앞쪽 castDistance</summary>
    public void SetCastTarget(Vector3 p) { _castTarget = p; _useCastTarget = true; }
    /// <summary>찌가 물에 떠 있는지</summary>
    public bool BobberInWater => _bobberInWater;
    /// <summary>물고기가 툭 건드림 (찌가 살짝 잠김)</summary>
    public void Nibble() { if (_bobberInWater) StartCoroutine(SmallDip()); }
    /// <summary>찌·줄·물고기 모두 정리 (낚시가 끝나거나 중간에 끊길 때)</summary>
    public void ResetAll()
    {
        StopAllCoroutines();
        _bobberCo = null;
        HideBobberAndLine();
        if (_fish != null) Destroy(_fish);
        _fishOn = false;
        _useCastTarget = false;
    }

    IEnumerator SmallDip()
    {
        Vector3 p = _landPoint;
        float[] d = { 0f, -0.05f, 0f };
        for (int i = 1; i < d.Length; i++)
            for (float t = 0; t < 0.08f; t += Time.deltaTime)
            {
                _landPoint = p + Vector3.up * Mathf.Lerp(d[i - 1], d[i], t / 0.08f);
                yield return null;
            }
        _landPoint = p;
    }

    LineRenderer _line;
    Transform _bobber;
    Vector3 _landPoint;
    bool _lineOn, _bobberInWater, _fishOn;
    float _bobT;
    Coroutine _bobberCo;
    GameObject _fish;
    ParticleSystem _stars, _splash, _ring;
    static Texture2D s_dot, s_star, s_ring;

    void Awake()
    {
        if (rodTip == null) rodTip = FindDeep(transform, "RodTip");
        if (rod == null) rod = FindDeep(transform, "FishingRod");
        BuildLine();
        BuildBobber();
        _stars = MakePS("FishingSparkle", Star(), 0.12f, 0.45f, sparkleColor, 0.6f);
        _splash = MakePS("FishingSplash", Dot(), 0.08f, 0.7f, splashColor, 3.5f);
        _ring = MakePS("FishingRing", Ring(), 0.35f, 0.6f, splashColor, 0f);
    }

    void LateUpdate()
    {
        if (_bobberInWater && !_fishOn)
        {
            _bobT += Time.deltaTime;
            _bobber.position = _landPoint + Vector3.up * (Mathf.Sin(_bobT * 2.6f) * 0.025f);
        }
        if (_lineOn && rodTip != null)
        {
            Vector3 a = rodTip.position, b = _fishOn && _fish != null ? _fish.transform.position : _bobber.position;
            float sag = _bobberInWater ? 0.35f : 0.12f;
            for (int i = 0; i < _line.positionCount; i++)
            {
                float t = i / (float)(_line.positionCount - 1);
                Vector3 p = Vector3.Lerp(a, b, t) + Vector3.down * sag * 4f * t * (1f - t) * Mathf.Min(1f, Vector3.Distance(a, b) / 3f);
                _line.SetPosition(i, p);
            }
        }
        _line.enabled = _lineOn;
    }

    // ---------------- animation events ----------------
    public void RodSpawn() { Sparkle(RodCenter(), 12); onRodSpawn?.Invoke(); }

    public void RodDespawn()
    {
        Sparkle(RodCenter(), 10);
        HideBobberAndLine();
        if (_fish != null) Destroy(_fish);
        _fishOn = false;
        onRodDespawn?.Invoke();
    }

    public void CastRelease()
    {
        if (rodTip == null) return;
        Vector3 fwd = transform.forward; fwd.y = 0f; fwd.Normalize();
        _landPoint = _useCastTarget ? _castTarget : FindWater(transform.position + fwd * castDistance);
        _useCastTarget = false;
        if (_bobberCo != null) StopCoroutine(_bobberCo);
        _bobberCo = StartCoroutine(FlyBobber(rodTip.position, _landPoint, castFlightTime, 1.6f, true));
        onCast?.Invoke();
    }

    public void Bite()
    {
        if (!_bobberInWater) return;
        StartCoroutine(Dip());
        Splash(_landPoint, 10, 1.6f);
        onBite?.Invoke();
    }

    public void Hook()
    {
        if (!_bobberInWater) return;
        Splash(_landPoint, 14, 2.2f);
        onHook?.Invoke();
    }

    public void FishOut()
    {
        Splash(_landPoint, 26, 3.2f);
        if (fishPrefab != null && rodTip != null)
        {
            if (_fish != null) Destroy(_fish);
            _fish = Instantiate(fishPrefab, _landPoint, Quaternion.identity);
            StartCoroutine(FlyFish(_fish.transform, _landPoint, 0.45f));
        }
        else if (rodTip != null)
        {
            if (_bobberCo != null) StopCoroutine(_bobberCo);
            _bobberCo = StartCoroutine(FlyBobber(_bobber.position, rodTip.position + Vector3.down * 0.6f, 0.45f, 1.2f, false));
        }
        onFishOut?.Invoke();
    }

    public void LineSlack()
    {
        if (!_bobberInWater || rodTip == null) return;
        Splash(_landPoint, 6, 1.0f);
        if (_bobberCo != null) StopCoroutine(_bobberCo);
        _bobberCo = StartCoroutine(FlyBobber(_bobber.position, rodTip.position + Vector3.down * 0.5f, 0.6f, 0.5f, false));
        onLineSlack?.Invoke();
    }

    // ---------------- motion helpers ----------------
    IEnumerator FlyBobber(Vector3 from, Vector3 to, float time, float arc, bool landInWater)
    {
        _bobberInWater = false; _lineOn = true; _bobber.gameObject.SetActive(true);
        for (float t = 0; t < time; t += Time.deltaTime)
        {
            float k = t / time;
            _bobber.position = Vector3.Lerp(from, to, k) + Vector3.up * arc * 4f * k * (1f - k);
            yield return null;
        }
        _bobber.position = to;
        if (landInWater)
        {
            _bobberInWater = true; _bobT = 0f;
            Splash(to, 12, 1.8f); Ring(to);
            onBobberLand?.Invoke();
        }
        else
        {
            // reeled back in: hide shortly after reaching the rod
            yield return new WaitForSeconds(0.15f);
            HideBobberAndLine();
        }
        _bobberCo = null;
    }

    IEnumerator Dip()
    {
        Vector3 p = _landPoint;
        float[] d = { 0f, -0.12f, -0.05f, -0.10f, 0f };
        for (int i = 1; i < d.Length; i++)
            for (float t = 0; t < 0.08f; t += Time.deltaTime)
            {
                _landPoint = p + Vector3.up * Mathf.Lerp(d[i - 1], d[i], t / 0.08f);
                yield return null;
            }
        _landPoint = p;
    }

    IEnumerator FlyFish(Transform fish, Vector3 from, float time)
    {
        _bobberInWater = false; _fishOn = true; _bobber.gameObject.SetActive(false);
        Quaternion face = Quaternion.LookRotation(Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized, Vector3.up);
        for (float t = 0; t < time; t += Time.deltaTime)
        {
            float k = t / time;
            Vector3 to = rodTip.position + Vector3.down * fishHangLength;
            fish.position = Vector3.Lerp(from, to, k) + Vector3.up * 1.4f * 4f * k * (1f - k);
            fish.rotation = face * Quaternion.Euler(0f, 0f, 540f * k);          // 공중제비 (arc)
            yield return null;
        }
        float w = 0f;
        while (fish != null && _fishOn)          // 낚싯대 끝에 매달려 파닥파닥 (follow-through)
        {
            w += Time.deltaTime;
            fish.position = Vector3.Lerp(fish.position, rodTip.position + Vector3.down * fishHangLength, 0.35f);
            float flap = Mathf.Sin(w * 18f) * 25f * Mathf.Exp(-w * 0.8f) + Mathf.Sin(w * 5f) * 6f;
            fish.rotation = face * Quaternion.Euler(0f, flap, Mathf.Sin(w * 3f) * 8f);
            yield return null;
        }
    }

    void HideBobberAndLine() { _lineOn = false; _bobberInWater = false; if (_bobber != null) _bobber.gameObject.SetActive(false); }

    Vector3 FindWater(Vector3 p)
    {
        if (Physics.Raycast(p + Vector3.up * 5f, Vector3.down, out RaycastHit h, 20f, waterMask, QueryTriggerInteraction.Collide))
            return h.point + Vector3.up * waterLevelOffset;
        return new Vector3(p.x, transform.position.y + waterLevelOffset, p.z);
    }

    Vector3 RodCenter() => rod != null ? rod.position : (rodTip != null ? rodTip.position : transform.position + Vector3.up);

    // ---------------- builders ----------------
    void BuildLine()
    {
        var go = new GameObject("FishingLine");
        go.transform.SetParent(transform, false);
        _line = go.AddComponent<LineRenderer>();
        _line.positionCount = 16; _line.widthMultiplier = lineWidth; _line.useWorldSpace = true;
        _line.material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
        _line.material.color = lineColor; _line.startColor = _line.endColor = lineColor;
        _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _line.enabled = false;
    }

    void BuildBobber()
    {
        var root = new GameObject("FishingBobber").transform;
        if (bobberPrefab != null)
        {
            var b = Instantiate(bobberPrefab, root, false);
            b.transform.localPosition = Vector3.up * 0.09f;
            foreach (var c in b.GetComponentsInChildren<Collider>()) Destroy(c);
            _bobber = root; root.gameObject.SetActive(false);
            return;
        }
        var top = GameObject.CreatePrimitive(PrimitiveType.Sphere); var bot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        foreach (var g in new[] { top, bot }) { Destroy(g.GetComponent<Collider>()); g.transform.SetParent(root, false); }
        top.transform.localScale = new Vector3(1f, 0.6f, 1f) * bobberSize; top.transform.localPosition = Vector3.up * bobberSize * 0.18f;
        bot.transform.localScale = new Vector3(0.95f, 0.6f, 0.95f) * bobberSize; bot.transform.localPosition = Vector3.down * bobberSize * 0.12f;
        Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        top.GetComponent<Renderer>().material = new Material(lit) { color = bobberTop };
        bot.GetComponent<Renderer>().material = new Material(lit) { color = bobberBottom };
        _bobber = root; root.gameObject.SetActive(false);
    }

    void Sparkle(Vector3 p, int n)
    {
        for (int i = 0; i < n; i++)
        {
            var ep = new ParticleSystem.EmitParams { position = p + Random.insideUnitSphere * 0.25f, velocity = Random.onUnitSphere * Random.Range(0.4f, 1.1f), startSize = Random.Range(0.08f, 0.16f), startLifetime = Random.Range(0.35f, 0.6f) };
            _stars.Emit(ep, 1);
        }
    }

    void Splash(Vector3 p, int n, float speed)
    {
        for (int i = 0; i < n; i++)
        {
            Vector2 c = Random.insideUnitCircle * 0.6f;
            var ep = new ParticleSystem.EmitParams { position = p + new Vector3(c.x * 0.2f, 0.02f, c.y * 0.2f), velocity = new Vector3(c.x, 1.2f + Random.value, c.y) * speed * 0.6f, startSize = Random.Range(0.05f, 0.11f), startLifetime = Random.Range(0.4f, 0.75f) };
            _splash.Emit(ep, 1);
        }
    }

    void Ring(Vector3 p)
    {
        var ep = new ParticleSystem.EmitParams { position = p + Vector3.up * 0.02f, velocity = Vector3.zero, startSize = 0.3f, startLifetime = 0.6f, rotation3D = new Vector3(90f, 0f, 0f) };
        _ring.Emit(ep, 1);
    }

    ParticleSystem MakePS(string name, Texture2D tex, float size, float life, Color col, float gravity)
    {
        var go = new GameObject(name);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.loop = false; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSize = size; main.startLifetime = life; main.startColor = col; main.gravityModifier = gravity; main.maxParticles = 300;
        var em = ps.emission; em.enabled = false;
        var sh = ps.shape; sh.enabled = false;
        var sz = ps.sizeOverLifetime; sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, name == "FishingRing" ? AnimationCurve.Linear(0, 0.3f, 1, 2.5f) : AnimationCurve.EaseInOut(0, 1, 1, 0));
        var co = ps.colorOverLifetime; co.enabled = true;
        var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 0.6f), new GradientAlphaKey(0, 1) });
        co.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = name == "FishingRing" ? ParticleSystemRenderMode.HorizontalBillboard : ParticleSystemRenderMode.Billboard;
        var sh2 = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
        var m = new Material(sh2); m.mainTexture = tex;
        if (sh2.name.Contains("Universal"))
        {
            m.SetTexture("_BaseMap", tex); m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = 3000;
        }
        r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
        return ps;
    }

    static Texture2D Dot() { if (s_dot == null) s_dot = MakeTex((x, y) => Mathf.Clamp01(1f - Mathf.Sqrt(x * x + y * y))); return s_dot; }
    static Texture2D Ring() { if (s_ring == null) s_ring = MakeTex((x, y) => { float d = Mathf.Sqrt(x * x + y * y); return Mathf.Clamp01(1f - Mathf.Abs(d - 0.75f) * 8f); }); return s_ring; }
    static Texture2D Star() { if (s_star == null) s_star = MakeTex((x, y) => { float a = Mathf.Clamp01(1f - (Mathf.Abs(x) * 6f + Mathf.Abs(y))) + Mathf.Clamp01(1f - (Mathf.Abs(y) * 6f + Mathf.Abs(x))); return Mathf.Clamp01(a); }); return s_star; }
    static Texture2D MakeTex(System.Func<float, float, float> f)
    {
        const int N = 64; var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
        { float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f; t.SetPixel(x, y, new Color(1, 1, 1, f(u, v))); }
        t.Apply(); return t;
    }

    static Transform FindDeep(Transform t, string n)
    {
        if (t.name == n) return t;
        foreach (Transform c in t) { var r = FindDeep(c, n); if (r != null) return r; }
        return null;
    }
}
