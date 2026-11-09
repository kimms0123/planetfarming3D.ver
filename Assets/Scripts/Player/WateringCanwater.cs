using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Sprays water particles out of the watering can's spout.
/// While autoByTilt is on, the flow rate follows how far the spout tips down,
/// so it works in any animation clip (or during blending) without animation events.
/// Usage: add this component to the character root (or to WateringCan).
///        SpoutTip / SpoutAim are found automatically by name from the children.
/// </summary>
[DisallowMultipleComponent]
public class WateringCanWater : MonoBehaviour
{
    [Header("Spout (found by name when left empty)")]
    public Transform spoutTip;
    public Transform spoutAim;

    [Header("Flow control")]
    [Tooltip("When on, the flow follows the can's tilt.")]
    public bool autoByTilt = true;
    [Tooltip("Water starts at this world-up component of the spray direction (0.35 = sprinkler head pointing ~20 deg up).")]
    public float startUp = 0.35f;
    [Tooltip("Flow is at 100% at or below this value.")]
    public float fullUp = 0.12f;
    [Range(0f, 1f), Tooltip("Flow used when autoByTilt is off.")]
    public float manualFlow = 0f;

    [Header("Look")]
    public float maxRate = 140f;          // droplets per second
    public float speed = 1.6f;            // m/s
    public float spreadAngle = 12f;       // cone angle
    public float dropletSize = 0.028f;    // m
    public float lifetime = 0.8f;         // s
    public Color waterColor = new Color(0.55f, 0.82f, 1f, 0.85f);
    [Tooltip("Leave empty to create a default material at runtime. Assigning one is recommended for builds.")]
    public Material dropletMaterial;

    [Header("Collision / splash")]
    public bool splashes = true;
    public LayerMask collideWith = ~0;

    [Header("Events (e.g. water sound)")]
    public UnityEvent onPourStart;
    public UnityEvent onPourEnd;

    public bool IsPouring { get; private set; }
    public float Flow { get; private set; }

    ParticleSystem _spray;
    ParticleSystem _splash;
    Transform _sprayTf;
    static Material s_defaultMat;

    void Awake()
    {
        if (spoutTip == null) spoutTip = FindChild("SpoutTip");
        if (spoutAim == null) spoutAim = FindChild("SpoutAim");
        if (spoutTip == null || spoutAim == null)
        {
            Debug.LogWarning("[WateringCanWater] Couldn't find SpoutTip/SpoutAim. Assign them in the Inspector.", this);
            enabled = false;
            return;
        }
        Build();
    }

    Transform FindChild(string n)
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name == n) return t;
        return null;
    }

    void Build()
    {
        var go = new GameObject("WaterSpray");
        _sprayTf = go.transform;
        _sprayTf.SetParent(transform, false);
        _spray = go.AddComponent<ParticleSystem>();
        _spray.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = _spray.main;
        main.playOnAwake = false;
        main.loop = true;
        main.startLifetime = lifetime;
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.85f, speed * 1.15f);
        main.startSize = new ParticleSystem.MinMaxCurve(dropletSize * 0.7f, dropletSize * 1.2f);
        main.startColor = waterColor;
        main.gravityModifier = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World; // droplets don't follow the can
        main.maxParticles = 1000;

        var em = _spray.emission;
        em.rateOverTime = 0f;

        var sh = _spray.shape;                 // cone emits along local +Z = spray direction
        sh.shapeType = ParticleSystemShapeType.Cone;
        sh.angle = spreadAngle;
        sh.radius = 0.02f;

        var sol = _spray.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.6f));

        var col = _spray.collision;
        col.enabled = true;
        col.type = ParticleSystemCollisionType.World;
        col.mode = ParticleSystemCollisionMode.Collision3D;
        col.collidesWith = collideWith;
        col.quality = ParticleSystemCollisionQuality.Medium;
        col.bounce = 0f;
        col.lifetimeLoss = 1f;                 // droplet disappears when it hits the ground

        var mat = dropletMaterial != null ? dropletMaterial : DefaultMaterial();
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sharedMaterial = mat;

        if (splashes) BuildSplash(go.transform, mat);
        _spray.Play();
    }

    void BuildSplash(Transform parent, Material mat)
    {
        var go = new GameObject("WaterSplash");
        go.transform.SetParent(parent, false);
        _splash = go.AddComponent<ParticleSystem>();
        _splash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = _splash.main;
        main.playOnAwake = false;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.25f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(dropletSize * 0.35f, dropletSize * 0.6f);
        main.startColor = waterColor;
        main.gravityModifier = 0.5f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 500;

        var em = _splash.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 1, 3) });

        var sh = _splash.shape;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius = 0.005f;

        var vol = _splash.velocityOverLifetime;  // small upward bounce
        vol.enabled = true;
        vol.space = ParticleSystemSimulationSpace.World;
        vol.x = new ParticleSystem.MinMaxCurve(0f);
        vol.y = new ParticleSystem.MinMaxCurve(0.7f);
        vol.z = new ParticleSystem.MinMaxCurve(0f);

        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;

        var se = _spray.subEmitters;
        se.enabled = true;
        se.AddSubEmitter(_splash, ParticleSystemSubEmitterType.Collision, ParticleSystemSubEmitterProperties.InheritNothing);
    }

    void LateUpdate()   // after the Animator has posed the bones
    {
        Vector3 tip = spoutTip.position;
        Vector3 dir = spoutAim.position - tip;
        // The combined clip FBX hides the can (scale ~0) outside the Watering clip: no water then.
        bool hidden = spoutTip.lossyScale.x < 0.01f || dir.sqrMagnitude < 1e-8f;
        if (!hidden)
        {
            dir.Normalize();
            _sprayTf.SetPositionAndRotation(tip, Quaternion.LookRotation(dir));
        }

        Flow = hidden ? 0f : (autoByTilt ? Mathf.InverseLerp(startUp, fullUp, dir.y) : manualFlow);
        var em = _spray.emission;
        em.rateOverTime = maxRate * Flow;

        bool pouring = Flow > 0.01f;
        if (pouring != IsPouring)
        {
            IsPouring = pouring;
            if (pouring) onPourStart?.Invoke(); else onPourEnd?.Invoke();
        }
    }

    void OnDisable()
    {
        if (_spray != null) _spray.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (IsPouring) { IsPouring = false; onPourEnd?.Invoke(); }
    }

    void OnEnable()
    {
        if (_spray != null && !_spray.isPlaying) _spray.Play();
    }

    // Sprites/Default is a built-in shader: alpha blended, uses vertex color, renders in both Built-in and URP.
    static Material DefaultMaterial()
    {
        if (s_defaultMat != null) return s_defaultMat;
        var shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        s_defaultMat = new Material(shader) { name = "WaterDroplet (runtime)" };
        s_defaultMat.mainTexture = MakeDot(32);
        return s_defaultMat;
    }

    static Texture2D MakeDot(int n)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "WaterDot" };
        float c = (n - 1) * 0.5f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                float a = Mathf.Clamp01(1f - Mathf.SmoothStep(0.55f, 1f, d));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return tex;
    }
}