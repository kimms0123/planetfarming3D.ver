#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Splits SpaceFarmer_AllClips.fbx (one baked timeline) into named clips on import and adds animation events.
/// Put this file in an Editor folder (it replaces the previous SpaceFarmerAllClipsImporter.cs - keep only one copy).
/// Tools (watering can, hoe, fishing rod) are shown/hidden by the animation itself (socket scale),
/// so each clip only shows the tool it uses. Reimport the FBX after changing this file.
/// Frames 0-514 are the original clips (unchanged). Fishing clips were appended from frame 525.
/// </summary>
public class SpaceFarmerAllClipsImporter : AssetPostprocessor
{
    const string FileKey = "SpaceFarmer_AllClips";
    const float FPS = 30f;

    struct Ev { public int frame; public string fn; public Ev(int f, string n) { frame = f; fn = n; } }
    struct Clip
    {
        public string name; public int first, last; public bool loop; public Ev[] events;
        public Clip(string n, int f, int l, bool lp, params Ev[] e) { name = n; first = f; last = l; loop = lp; events = e; }
    }

    // Frame ranges @ 30 fps on the baked timeline (10 empty frames between clips). Event frames are absolute.
    static readonly Clip[] Clips =
    {
        // ---- original clips (unchanged)
        new Clip("Idle", 0, 72, true),
        new Clip("Walk", 83, 113, true),
        new Clip("Run", 124, 144, true),
        new Clip("Plant_Seed", 155, 251, false),
        new Clip("Jump", 262, 310, false),
        new Clip("Jump_InPlace", 321, 369, false),
        new Clip("Watering", 380, 452, false),
        new Clip("Hoe_Till_Once", 463, 514, false, new Ev(493, "HoeImpact")),
        // ---- fishing (appended)
        new Clip("Fishing_Equip", 525, 540, false, new Ev(532, "RodSpawn")),
        new Clip("Fishing_Cast", 541, 576, false, new Ev(556, "CastRelease")),
        new Clip("Fishing_Wait_Loop", 577, 637, true),
        new Clip("Fishing_Bite", 638, 650, false, new Ev(640, "Bite")),
        new Clip("Fishing_Reel_Success", 651, 695, false, new Ev(653, "Hook"), new Ev(680, "FishOut")),
        new Clip("Fishing_Catch_Celebrate", 696, 731, false),
        new Clip("Fishing_Fail", 732, 758, false, new Ev(736, "LineSlack")),
        new Clip("Fishing_Unequip", 759, 774, false, new Ev(765, "RodDespawn")),
        // whole sequence (success path then fail path) for previewing only
        new Clip("Fishing_FullPreview", 525, 774, false,
            new Ev(532, "RodSpawn"), new Ev(556, "CastRelease"), new Ev(640, "Bite"), new Ev(653, "Hook"),
            new Ev(680, "FishOut"), new Ev(736, "LineSlack"), new Ev(765, "RodDespawn")),
    };

    void OnPreprocessAnimation()
    {
        if (!assetPath.Contains(FileKey)) return;
        var importer = assetImporter as ModelImporter;
        if (importer == null) return;
        var defaults = importer.defaultClipAnimations;
        if (defaults == null || defaults.Length == 0) return;
        string take = defaults[0].takeName;

        var result = new ModelImporterClipAnimation[Clips.Length];
        for (int i = 0; i < Clips.Length; i++)
        {
            var c = Clips[i];
            var clip = new ModelImporterClipAnimation
            {
                name = c.name,
                takeName = take,
                firstFrame = c.first,
                lastFrame = c.last,
                loopTime = c.loop,
                loopPose = false,
            };
            if (c.events != null && c.events.Length > 0)
            {
                var evs = new List<AnimationEvent>();
                float span = c.last - c.first;                       // events use normalized time (0..1)
                foreach (var e in c.events)
                    evs.Add(new AnimationEvent { time = (e.frame - c.first) / span, functionName = e.fn });
                clip.events = evs.ToArray();
            }
            result[i] = clip;
        }
        importer.clipAnimations = result;
    }
}
#endif