using System.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 낚시 동작 재생기 (테스트/기본 흐름용). Animator Controller를 고치지 않고 클립을 직접 재생한다.
/// 낚시하는 동안만 Animator 출력을 잠시 넘겨받고, 끝나면 원래 Animator Controller(Idle/Walk...)로 돌아간다.
///
/// 흐름: [F] Equip → Cast → Wait_Loop(찌 대기) → 랜덤 시간 뒤 Bite
///        → 입질 후 reactionWindow 안에 [F]: Reel_Success → Catch_Celebrate → Unequip
///        → 못 누르면: Fail → Unequip
///        Wait 중에 [F]를 다시 누르면 낚시 취소 (Unequip)
///
/// 붙이는 곳: Animator가 있는 캐릭터 모델 오브젝트 (FishingFX와 같은 오브젝트).
/// Inspector에서 FBX 안의 Fishing_* 클립 8개를 각각 넣는다.
/// </summary>
[DisallowMultipleComponent]
public class FishingController : MonoBehaviour
{
    [Header("클립 (SpaceFarmer_AllClips.fbx 안의 Fishing_*)")]
    public AnimationClip equip;
    public AnimationClip cast;
    public AnimationClip waitLoop;
    public AnimationClip bite;
    public AnimationClip reelSuccess;
    public AnimationClip celebrate;
    public AnimationClip fail;
    public AnimationClip unequip;

    [Header("참조 (비우면 자동)")]
    public Animator animator;
    [Tooltip("낚시 중 꺼둘 이동 스크립트 (비우면 부모에서 PlayerController를 찾음)")]
    public Behaviour movementToDisable;

    [Header("규칙")]
    public float biteDelayMin = 2.5f;
    public float biteDelayMax = 6f;
    [Tooltip("입질 후 이 시간 안에 누르면 성공")]
    public float reactionWindow = 0.9f;
    [Tooltip("켜면 좌클릭으로도 챔질 (농사 좌클릭과 겹치면 끄기)")]
    public bool mouseClickHooks = false;
    [Tooltip("클립 사이 섞는 시간")]
    public float blendTime = 0.08f;

    public bool IsFishing { get; private set; }

    // ---- 게임 연동 (행성농사일지 FishingManager) ----
    [Tooltip("게임의 FishingManager가 낚시를 진행할 때 켜짐 → [F] 테스트 흐름은 꺼지고 아래 함수로 클립만 재생")]
    public bool externalControl = false;

    /// <summary>클립 재생 준비 (Animator 출력을 넘겨받음)</summary>
    public void BeginExternal() { if (!_graph.IsValid()) BuildGraph(); }
    /// <summary>끝 (원래 Animator Controller로 돌아감)</summary>
    public void EndExternal() { if (_fade != null) StopCoroutine(_fade); _fade = null; StopGraph(); }
    /// <summary>지금 재생 중인 클립이 끝났는지 (반복 클립은 한 바퀴)</summary>
    public bool CurrentClipDone(AnimationClip clip) => clip == null || !_cur.IsValid() || _cur.GetTime() >= clip.length;

    PlayableGraph _graph;
    AnimationMixerPlayable _mixer;
    AnimationClipPlayable _cur;
    int _curSlot;
    Coroutine _flow, _fade;

    void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (movementToDisable == null)
        {
            Transform t = transform;
            while (t != null && movementToDisable == null) { movementToDisable = t.GetComponent("PlayerController") as Behaviour; t = t.parent; }
        }
    }

    void OnDisable() { StopGraph(); if (!externalControl) RestoreMovement(); IsFishing = false; }

    void Update()
    {
        if (externalControl) return; // 게임이 진행 중
        if (!IsFishing && PressedStart()) StartFishing();
    }

    public void StartFishing()
    {
        if (IsFishing || animator == null || equip == null) return;
        _flow = StartCoroutine(Flow());
    }

    IEnumerator Flow()
    {
        IsFishing = true;
        if (movementToDisable != null) movementToDisable.enabled = false;
        BuildGraph();

        yield return PlayOnce(equip);
        yield return PlayOnce(cast);

        Play(waitLoop);
        float biteAt = Time.time + Random.Range(biteDelayMin, biteDelayMax);
        bool cancelled = false;
        yield return null;
        while (Time.time < biteAt)
        {
            if (PressedAction()) { cancelled = true; break; }
            yield return null;
        }

        if (!cancelled)
        {
            Play(bite);
            float until = Time.time + reactionWindow;
            bool hooked = false;
            yield return null;
            while (Time.time < until)
            {
                if (PressedAction()) { hooked = true; break; }
                yield return null;
            }
            if (hooked)
            {
                yield return PlayOnce(reelSuccess);
                yield return PlayOnce(celebrate);
            }
            else
            {
                // let the bite reaction finish before the fail motion
                while (_cur.IsValid() && _cur.GetTime() < bite.length) yield return null;
                yield return PlayOnce(fail);
            }
        }
        yield return PlayOnce(unequip);

        StopGraph();
        RestoreMovement();
        IsFishing = false;
    }

    // ---------------- playables ----------------
    void BuildGraph()
    {
        _graph = PlayableGraph.Create("Fishing");
        _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        var output = AnimationPlayableOutput.Create(_graph, "Fishing", animator);
        _mixer = AnimationMixerPlayable.Create(_graph, 2);
        output.SetSourcePlayable(_mixer);
        _curSlot = -1;
        _graph.Play();
    }

    public void Play(AnimationClip clip)
    {
        if (clip == null) return;
        int slot = _curSlot == 0 ? 1 : 0;
        var input = _mixer.GetInput(slot);
        if (input.IsValid()) { _graph.Disconnect(_mixer, slot); input.Destroy(); }
        var p = AnimationClipPlayable.Create(_graph, clip);
        p.SetApplyFootIK(false);
        _graph.Connect(p, 0, _mixer, slot);
        int prev = _curSlot; _curSlot = slot; _cur = p;
        if (_fade != null) StopCoroutine(_fade);
        if (prev >= 0) { _mixer.SetInputWeight(prev, 1f); _mixer.SetInputWeight(slot, 0f); }
        if (prev < 0) { _mixer.SetInputWeight(slot, 1f); _fade = null; }
        else _fade = StartCoroutine(Fade(prev, slot));
    }

    IEnumerator Fade(int from, int to)
    {
        for (float t = 0; t < blendTime; t += Time.deltaTime)
        {
            float k = t / blendTime;
            _mixer.SetInputWeight(from, 1f - k); _mixer.SetInputWeight(to, k);
            yield return null;
        }
        _mixer.SetInputWeight(from, 0f); _mixer.SetInputWeight(to, 1f);
    }

    public IEnumerator PlayOnce(AnimationClip clip)
    {
        if (clip == null) yield break;
        Play(clip);
        yield return null;
        while (_cur.IsValid() && _cur.GetTime() < clip.length) yield return null;
    }

    void StopGraph()
    {
        if (_graph.IsValid()) _graph.Destroy();
    }

    void RestoreMovement()
    {
        if (movementToDisable != null) movementToDisable.enabled = true;
    }

    // ---------------- input ----------------
    static bool PressedStart()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.F);
#endif
    }

    bool PressedAction()
    {
#if ENABLE_INPUT_SYSTEM
        return (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame) ||
               (mouseClickHooks && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
#else
        return Input.GetKeyDown(KeyCode.F) || (mouseClickHooks && Input.GetMouseButtonDown(0));
#endif
    }
}
