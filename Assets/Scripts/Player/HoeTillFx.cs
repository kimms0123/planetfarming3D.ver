using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Tool spawn/despawn + particles + soil furrow for the Hoe_Till_Once clip.
/// Add it to the same GameObject as the Animator (the character root).
/// The FBX clip only contains bone motion. This script handles everything else,
/// driven by Animation Events. The events are added automatically by the
/// HoeTillEventsImporter editor script; their times are listed below.
///   1.00s HoeImpact (vertical chop clip, 1.7s). ToolSpawn/ScrapeStart/ScrapeEnd/ToolDespawn
///   are kept for other clips and can be called from your own events if needed.
/// </summary>
[DisallowMultipleComponent]
public class HoeTillFX : MonoBehaviour
{
    [Header("Tool (found by name when left empty)")]
    public Transform tool;              // "Hoe"  (child of HoeSocket under RightHand)
    public Transform bladeTip;          // "BladeTip"
    [Tooltip("Hide the tool (scale 0) at start; it only appears on ToolSpawn.")]
    public bool hideToolOnStart = false;

    [Header("Ground")]
    public LayerMask groundMask = ~0;
    public float groundRayLength = 3f;

    [Header("Colors")]
    public Color sparkleColor = new Color(1f, 0.93f, 0.55f, 1f);
    public Color dustColor = new Color(0.66f, 0.52f, 0.36f, 0.75f);
    public Color clodColor = new Color(0.38f, 0.25f, 0.15f, 1f);
    public Color furrowColor = new Color(0.30f, 0.20f, 0.12f, 1f);

    [Header("Furrow")]
    public float furrowWidth = 0.22f;
    [Tooltip("Seconds before the furrow fades. 0 = keep it.")]
    public float furrowLifetime = 10f;

    [Header("Optional materials (defaults are created at runtime)")]
    public Material particleMaterial;
    public Material furrowMaterial;

    [Header("Events (camera shake, sound, ...)")]
    public UnityEvent onToolSpawn;
    public UnityEvent onImpact;
    public UnityEvent onScrapeStart;
    public UnityEvent onScrapeEnd;
    public UnityEvent onToolDespawn;

    Vector3 _toolScale = Vector3.one;
    Coroutine _scaleCo;
    ParticleSystem _stars, _ring, _flash, _dust, _clods, _lines, _scrape;
    bool _scraping;
    Vector3 _lastBlade;
    readonly List<Vector3> _furrow = new List<Vector3>();
    Mesh _furrowMesh;
    GameObject _furrowGO;
    Transform _fxRoot;
    static Texture2D s_dot, s_star, s_ring;

    // ------------------------------------------------------------------ setup
    void Awake()
    {
        if (tool == null) tool = FindChild("Hoe");
        if (bladeTip == null) bladeTip = FindChild("BladeTip");
        if (tool != null)
        {
            _toolScale = tool.localScale;
            if (hideToolOnStart) tool.localScale = Vector3.zero;
        }
        else Debug.LogWarning("[HoeTillFX] Couldn't find the 'Hoe' object. Assign it to 'tool'.", this);

        _fxRoot = new GameObject("HoeTillFX_Particles").transform;   // not parented: particles live in world space
        var pm = particleMaterial != null ? particleMaterial : MakeMat(Dot());
        var sm = particleMaterial != null ? particleMaterial : MakeMat(Star());
        var rm = particleMaterial != null ? particleMaterial : MakeMat(Ring());

        _stars = MakePS("Stars", sm, 0.45f, 0.05f, 0f, 60);
        {
            var m = _stars.main; m.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.1f); m.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f); m.startColor = sparkleColor;
            var sol = _stars.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 0.3f, 0.25f, 1.2f, 1f, 0f));
        }
        _ring = MakePS("Ring", rm, 0.3f, 0f, 0f, 5);
        {
            var m = _ring.main; m.startSpeed = 0f; m.startSize = 0.25f; m.startColor = sparkleColor;
            var sol = _ring.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 0.2f, 1f, 1.6f, 1f, 1.8f));
            var col = _ring.colorOverLifetime; col.enabled = true; col.color = Fade(Color.white);
        }
        _flash = MakePS("Pop", pm, 0.12f, 0f, 0f, 5);
        {
            var m = _flash.main; m.startSpeed = 0f; m.startSize = 0.35f; m.startColor = new Color(1f, 1f, 1f, 0.9f);
            var col = _flash.colorOverLifetime; col.enabled = true; col.color = Fade(Color.white);
        }

        _dust = MakePS("Dust", pm, 1.1f, -0.05f, 0f, 200);
        {
            var m = _dust.main; m.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.0f); m.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.26f);
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f); m.startColor = dustColor;
            var sol = _dust.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 0.5f, 0.4f, 1.1f, 1f, 1.6f));
            var col = _dust.colorOverLifetime; col.enabled = true; col.color = Fade(Color.white);
            var lim = _dust.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 0.15f; lim.dampen = 0.08f;
        }
        _clods = MakePS("Clods", pm, 0.9f, 1.6f, 0.25f, 200);
        {
            var m = _clods.main; m.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.4f); m.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.065f);
            m.startColor = clodColor;
        }
        _lines = MakePS("ImpactLines", pm, 0.16f, 0f, 0f, 40);
        {
            var m = _lines.main; m.startSpeed = new ParticleSystem.MinMaxCurve(4f, 6f); m.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.04f);
            m.startColor = new Color(1f, 0.97f, 0.85f, 0.9f);
            var r = _lines.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.06f; r.lengthScale = 2f;
            var col = _lines.colorOverLifetime; col.enabled = true; col.color = Fade(Color.white);
        }
        _scrape = MakePS("ScrapeDirt", pm, 0.7f, 1.4f, 0.2f, 300);
        { var m = _scrape.main; m.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f); m.startColor = clodColor; }
    }

    Transform FindChild(string n)
    {
        foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == n) return t;
        return null;
    }

    ParticleSystem MakePS(string name, Material mat, float life, float gravity, float bounce, int max)
    {
        var go = new GameObject(name); go.transform.SetParent(_fxRoot, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main;
        m.playOnAwake = false; m.loop = false;
        m.startLifetime = life; m.gravityModifier = gravity; m.maxParticles = max;
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.enabled = false;           // everything is emitted manually with Emit()
        var sh = ps.shape; sh.enabled = false;
        if (bounce > 0f)
        {
            var c = ps.collision; c.enabled = true; c.type = ParticleSystemCollisionType.World;
            c.mode = ParticleSystemCollisionMode.Collision3D; c.collidesWith = groundMask;
            c.bounce = bounce; c.dampen = 0.5f; c.lifetimeLoss = 0.35f; c.quality = ParticleSystemCollisionQuality.Medium;
        }
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
        ps.Play();
        return ps;
    }

    // ------------------------------------------------------------------ animation events
    public void ToolSpawn()
    {
        Vector3 p = tool != null ? tool.position : transform.position + Vector3.up;
        Burst(_flash, p, 1, Vector3.zero, 0f);
        Burst(_ring, p, 1, Vector3.zero, 0f);
        BurstSphere(_stars, p, 10, 0.9f);
        ScaleTo(new[] { 0f, 0.06f, 0.13f, 0.2f }, new[] { 0f, 1.15f, 0.95f, 1f });
        onToolSpawn?.Invoke();
    }

    public void HoeImpact()
    {
        Vector3 g = GroundPoint(BladePos());
        Vector3 back = Flat(transform.position - g).normalized;       // toward the character
        // dust ring + clods + impact lines
        for (int i = 0; i < 14; i++)
        {
            Vector3 d = Quaternion.Euler(0, i * 360f / 14f, 0) * Vector3.forward;
            Emit(_dust, g + d * 0.05f + Vector3.up * 0.03f, (d * Random.Range(0.5f, 1.1f) + Vector3.up * Random.Range(0.2f, 0.6f)));
        }
        for (int i = 0; i < 16; i++)
        {
            Vector3 d = (Random.insideUnitSphere * 0.6f + Vector3.up * 1.2f + back * 0.35f).normalized;
            Emit(_clods, g + Vector3.up * 0.02f, d * Random.Range(1.4f, 2.6f));
        }
        for (int i = 0; i < 10; i++)
        {
            Vector3 d = Quaternion.Euler(0, i * 36f + Random.Range(-8f, 8f), 0) * Vector3.forward;
            Emit(_lines, g + Vector3.up * 0.06f + d * 0.1f, (d + Vector3.up * 0.15f).normalized * Random.Range(4f, 6f));
        }
        onImpact?.Invoke();
    }

    public void ScrapeStart()
    {
        _scraping = true; _furrow.Clear(); _lastBlade = BladePos();
        AddFurrowPoint(GroundPoint(_lastBlade));
        onScrapeStart?.Invoke();
    }

    public void ScrapeEnd()
    {
        _scraping = false;
        Vector3 g = GroundPoint(BladePos());
        for (int i = 0; i < 10; i++)   // a small soil pile at the end of the stroke
            Emit(_clods, g + Vector3.up * 0.02f, (Random.insideUnitSphere * 0.5f + Vector3.up).normalized * Random.Range(0.6f, 1.2f));
        if (furrowLifetime > 0f && _furrowGO != null) StartCoroutine(FadeFurrow(_furrowGO, _furrowMesh, furrowLifetime));
        _furrowGO = null; _furrowMesh = null;
        onScrapeEnd?.Invoke();
    }

    public void ToolDespawn()
    {
        Vector3 p = tool != null ? tool.position : transform.position + Vector3.up;
        BurstSphere(_stars, p, 8, 0.7f);
        Burst(_ring, p, 1, Vector3.zero, 0f);
        ScaleTo(new[] { 0f, 0.07f, 0.17f }, new[] { 1f, 1.1f, 0f });
        onToolDespawn?.Invoke();
    }

    // ------------------------------------------------------------------ scraping (every frame)
    void LateUpdate()
    {
        if (!_scraping) return;
        Vector3 b = BladePos();
        Vector3 g = GroundPoint(b);
        Vector3 move = Flat(b - _lastBlade);
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        Vector3 dir = move.sqrMagnitude > 1e-8f ? move.normalized : Flat(transform.position - g).normalized;
        int n = Mathf.Clamp(Mathf.RoundToInt(70f * dt + Random.value), 0, 4);
        for (int i = 0; i < n; i++)   // soil is pushed in the scraping direction and flicks up
        {
            Vector3 v = dir * Random.Range(0.6f, 1.4f) + Vector3.up * Random.Range(0.6f, 1.3f) + Random.insideUnitSphere * 0.25f;
            Emit(_scrape, g + Vector3.up * 0.02f + Random.insideUnitSphere * 0.03f, v);
        }
        if (Random.value < 0.35f) Emit(_dust, g + Vector3.up * 0.03f, dir * 0.3f + Vector3.up * 0.25f);
        if ((_furrow.Count == 0) || Flat(g - _furrow[_furrow.Count - 1]).magnitude > 0.02f) AddFurrowPoint(g);
        _lastBlade = b;
    }

    // ------------------------------------------------------------------ furrow mesh
    void AddFurrowPoint(Vector3 g)
    {
        _furrow.Add(g);
        if (_furrowGO == null)
        {
            _furrowGO = new GameObject("Furrow");
            _furrowMesh = new Mesh { name = "FurrowMesh" };
            _furrowGO.AddComponent<MeshFilter>().sharedMesh = _furrowMesh;
            var mr = _furrowGO.AddComponent<MeshRenderer>();
            mr.sharedMaterial = furrowMaterial != null ? furrowMaterial : MakeMat(Dot());
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        if (_furrow.Count < 2) return;
        int n = _furrow.Count;
        var verts = new Vector3[n * 3]; var cols = new Color[n * 3]; var uvs = new Vector2[n * 3];
        var tris = new int[(n - 1) * 12];
        float len = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector3 a = _furrow[Mathf.Max(0, i - 1)], b = _furrow[Mathf.Min(n - 1, i + 1)];
            Vector3 fwd = Flat(b - a); if (fwd.sqrMagnitude < 1e-8f) fwd = Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, fwd.normalized) * (furrowWidth * 0.5f);
            if (i > 0) len += Vector3.Distance(_furrow[i], _furrow[i - 1]);
            float taper = Mathf.Clamp01(Mathf.Min(i, n - 1 - i) / 2f + 0.35f);
            Vector3 c = _furrow[i] + Vector3.up * 0.004f;
            verts[i * 3 + 0] = c - side * taper; verts[i * 3 + 1] = c; verts[i * 3 + 2] = c + side * taper;
            Color edge = furrowColor; edge.a = 0f; Color mid = furrowColor; mid.a = 1f;
            cols[i * 3 + 0] = edge; cols[i * 3 + 1] = mid; cols[i * 3 + 2] = edge;
            uvs[i * 3 + 0] = new Vector2(0.5f, 0.5f); uvs[i * 3 + 1] = new Vector2(0.5f, 0.5f); uvs[i * 3 + 2] = new Vector2(0.5f, 0.5f);
        }
        for (int i = 0; i < n - 1; i++)
        {
            int o = i * 12, a = i * 3, b = (i + 1) * 3;
            tris[o + 0] = a; tris[o + 1] = b; tris[o + 2] = a + 1;
            tris[o + 3] = a + 1; tris[o + 4] = b; tris[o + 5] = b + 1;
            tris[o + 6] = a + 1; tris[o + 7] = b + 1; tris[o + 8] = a + 2;
            tris[o + 9] = a + 2; tris[o + 10] = b + 1; tris[o + 11] = b + 2;
        }
        _furrowMesh.Clear();
        _furrowMesh.vertices = verts; _furrowMesh.colors = cols; _furrowMesh.uv = uvs; _furrowMesh.triangles = tris;
        _furrowMesh.RecalculateBounds();
    }

    IEnumerator FadeFurrow(GameObject go, Mesh mesh, float life)
    {
        yield return new WaitForSeconds(life * 0.7f);
        float t = 0f, dur = life * 0.3f;
        var src = mesh.colors;
        var cols = new Color[src.Length];
        while (t < dur && go != null)
        {
            t += Time.deltaTime; float k = 1f - Mathf.Clamp01(t / dur);
            for (int i = 0; i < src.Length; i++) { var c = src[i]; c.a *= k; cols[i] = c; }
            mesh.colors = cols;
            yield return null;
        }
        if (go != null) Destroy(go);
    }

    // ------------------------------------------------------------------ helpers
    void ScaleTo(float[] times, float[] values)
    {
        if (tool == null) return;
        if (_scaleCo != null) StopCoroutine(_scaleCo);
        _scaleCo = StartCoroutine(ScaleRoutine(times, values));
    }

    IEnumerator ScaleRoutine(float[] times, float[] values)
    {
        float t = 0f, end = times[times.Length - 1];
        while (t < end)
        {
            int k = 1; while (k < times.Length - 1 && t > times[k]) k++;
            float u = Mathf.InverseLerp(times[k - 1], times[k], t);
            u = u * u * (3f - 2f * u);
            tool.localScale = _toolScale * Mathf.LerpUnclamped(values[k - 1], values[k], u);
            t += Time.deltaTime;
            yield return null;
        }
        tool.localScale = _toolScale * values[values.Length - 1];
    }

    Vector3 BladePos() { return bladeTip != null ? bladeTip.position : transform.position + transform.forward * 0.6f; }

    Vector3 GroundPoint(Vector3 p)
    {
        var hits = Physics.RaycastAll(p + Vector3.up * 0.6f, Vector3.down, groundRayLength, groundMask, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue; Vector3 res = new Vector3(p.x, transform.position.y, p.z);
        foreach (var h in hits)
        {
            if (h.collider.transform.IsChildOf(transform)) continue;    // ignore the character's own colliders
            if (h.distance < best) { best = h.distance; res = h.point; }
        }
        return res;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    static void Emit(ParticleSystem ps, Vector3 pos, Vector3 vel)
    {
        var ep = new ParticleSystem.EmitParams { position = pos, velocity = vel, applyShapeToPosition = false };
        ps.Emit(ep, 1);
    }
    static void Burst(ParticleSystem ps, Vector3 pos, int n, Vector3 vel, float spread)
    {
        for (int i = 0; i < n; i++) Emit(ps, pos, vel + Random.insideUnitSphere * spread);
    }
    static void BurstSphere(ParticleSystem ps, Vector3 pos, int n, float speed)
    {
        for (int i = 0; i < n; i++)
        {
            Vector3 d = Random.onUnitSphere; d.y = Mathf.Abs(d.y) * 0.8f + 0.2f;
            Emit(ps, pos + d * 0.05f, d.normalized * speed * Random.Range(0.6f, 1.2f));
        }
    }

    static AnimationCurve Curve(float t0, float v0, float t1, float v1, float t2, float v2)
    {
        return new AnimationCurve(new Keyframe(t0, v0), new Keyframe(t1, v1), new Keyframe(t2, v2));
    }
    static ParticleSystem.MinMaxGradient Fade(Color c)
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
        return new ParticleSystem.MinMaxGradient(g);
    }

    // Sprites/Default: built-in, alpha blended, uses vertex color, renders in Built-in and URP.
    static Material MakeMat(Texture2D tex)
    {
        var sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        return new Material(sh) { mainTexture = tex, name = "HoeFX (runtime)" };
    }
    static Texture2D Dot() { return s_dot != null ? s_dot : (s_dot = Procedural(64, 0)); }
    static Texture2D Star() { return s_star != null ? s_star : (s_star = Procedural(64, 1)); }
    static Texture2D Ring() { return s_ring != null ? s_ring : (s_ring = Procedural(64, 2)); }
    static Texture2D Procedural(int n, int kind)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float c = (n - 1) * 0.5f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x - c) / c, dy = (y - c) / c, r = Mathf.Sqrt(dx * dx + dy * dy), a;
                if (kind == 0) a = Mathf.Clamp01(1f - Mathf.SmoothStep(0.5f, 1f, r));
                else if (kind == 1)
                {   // 4-point star
                    float s = Mathf.Min(Mathf.Abs(dx) * Mathf.Abs(dy) * 14f + r * 0.9f, 1.5f);
                    a = Mathf.Clamp01(1.15f - s) + Mathf.Clamp01(1f - r * 4f);
                }
                else a = Mathf.Clamp01(1f - Mathf.Abs(r - 0.8f) * 9f);   // ring
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
            }
        tex.Apply();
        return tex;
    }
}