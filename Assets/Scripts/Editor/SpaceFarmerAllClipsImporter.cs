#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Splits SpaceFarmer_AllClips.fbx (one baked timeline) into named clips on import and adds the
/// HoeImpact animation event to Hoe_Till_Once. Put this file in an Editor folder.
/// The tools (watering can, hoe) are shown/hidden by the animation itself (socket scale), so each
/// clip only shows the tool it uses. Reimport the FBX after changing this file.
/// Remove the older HoeTillEventsImporter.cs from the project: it would rename this single take.
/// </summary>
public class SpaceFarmerAllClipsImporter : AssetPostprocessor
{
    const string FileKey = "SpaceFarmer_AllClips";

    struct Clip
    {
        public string name; public int first, last; public bool loop;
        public Clip(string n, int f, int l, bool lp) { name = n; first = f; last = l; loop = lp; }
    }

    // Frame ranges @ 30 fps on the baked timeline (10 empty frames between clips).
    static readonly Clip[] Clips =
    {
        new Clip("Idle", 0, 72, true),
        new Clip("Walk", 83, 113, true),
        new Clip("Run", 124, 144, true),
        new Clip("Plant_Seed", 155, 251, false),
        new Clip("Jump", 262, 310, false),
        new Clip("Jump_InPlace", 321, 369, false),
        new Clip("Watering", 380, 452, false),
        new Clip("Hoe_Till_Once", 463, 514, false),
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
            if (c.name == "Hoe_Till_Once")
            {
                float len = (c.last - c.first) / 30f;          // 1.7 s
                clip.events = new[] { new AnimationEvent { time = 1.0f / len, functionName = "HoeImpact" } }; // frame 30 of the clip
            }
            result[i] = clip;
        }
        importer.clipAnimations = result;
    }
}
#endif