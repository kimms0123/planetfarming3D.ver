#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Linq;
using System.IO;

public static class MeshyDockNPCSetup
{
    [MenuItem("Tools/Dock NPC Meshy/Create animated prefab")]
    public static void Create()
    {
        var script = AssetDatabase.FindAssets("MeshyDockNPCSetup t:MonoScript")
            .Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => p.EndsWith("/MeshyDockNPCSetup.cs"));
        if (script == null) throw new System.Exception("MeshyDockNPCSetup.cs could not be located.");
        var folder = Path.GetDirectoryName(Path.GetDirectoryName(script)).Replace('\\', '/');
        var modelPath = folder + "/FishNPC_Meshy_Swagger.fbx";
        var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        if (importer == null) throw new System.Exception("FishNPC_Meshy_Swagger.fbx must be in the DockNPC folder.");
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = true;
        importer.optimizeGameObjects = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.SaveAndReimport();
        var clips = importer.defaultClipAnimations;
        foreach (var clip in clips) { clip.loopTime = true; clip.loopPose = true; clip.lockRootRotation = true; clip.lockRootPositionXZ = true; }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
        var imported = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
        var walk = imported.FirstOrDefault(c => c.name.Contains("Swagger_Walk"));
        var run = imported.FirstOrDefault(c => c.name.Contains("Swagger_Run"));
        if (walk == null || run == null) throw new System.Exception("Both walk and run animation takes are required. Check the FBX Animation tab.");
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) throw new System.Exception("This setup requires an active URP project.");
        string bodyPath = folder + "/DockNPC_Body.mat";
        var body = AssetDatabase.LoadAssetAtPath<Material>(bodyPath);
        if (body == null) { body = new Material(shader); AssetDatabase.CreateAsset(body, bodyPath); }
        body.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/fish_npc.png"));
        body.SetColor("_BaseColor", Color.white); body.SetFloat("_Smoothness", 0.23f);
        string glassPath = folder + "/DockNPC_Glass.mat";
        var glass = AssetDatabase.LoadAssetAtPath<Material>(glassPath);
        if (glass == null) { glass = new Material(shader); AssetDatabase.CreateAsset(glass, glassPath); }
        glass.SetColor("_BaseColor", new Color(0.55f, 0.85f, 0.9f, 0.07f));
        glass.SetFloat("_Surface", 1); glass.SetFloat("_Blend", 0);
        glass.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        glass.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        glass.SetFloat("_ZWrite", 0); glass.SetFloat("_Cull", 2); glass.SetFloat("_Smoothness", 0.7f);
        glass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); glass.SetOverrideTag("RenderType", "Transparent"); glass.renderQueue = 3000;
        string controllerPath = folder + "/DockNPC.controller";
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        if (!controller.parameters.Any(p => p.name == "IsRunning")) controller.AddParameter("IsRunning", AnimatorControllerParameterType.Bool);
        var sm = controller.layers[0].stateMachine;
        var walkState = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == "Walk") ?? sm.AddState("Walk", new Vector3(240, 90, 0));
        var runState = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == "Run") ?? sm.AddState("Run", new Vector3(490, 90, 0));
        walkState.motion = walk; runState.motion = run; sm.defaultState = walkState;
        foreach (var t in walkState.transitions) walkState.RemoveTransition(t);
        foreach (var t in runState.transitions) runState.RemoveTransition(t);
        var toRun = walkState.AddTransition(runState); toRun.hasExitTime = false; toRun.duration = 0.18f; toRun.AddCondition(AnimatorConditionMode.If, 0, "IsRunning");
        var toWalk = runState.AddTransition(walkState); toWalk.hasExitTime = false; toWalk.duration = 0.18f; toWalk.AddCondition(AnimatorConditionMode.IfNot, 0, "IsRunning");
        var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
        instance.name = "DockNPC_Swagger";
        foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true)) renderer.sharedMaterials = renderer.sharedMaterials.Length > 1 ? new Material[] { body, glass } : new Material[] { body };
        var animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller; animator.applyRootMotion = false;
        PrefabUtility.SaveAsPrefabAsset(instance, folder + "/DockNPC_Swagger.prefab");
        Object.DestroyImmediate(instance); EditorUtility.SetDirty(body); EditorUtility.SetDirty(glass); EditorUtility.SetDirty(controller); AssetDatabase.SaveAssets();
        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(folder + "/DockNPC_Swagger.prefab"));
        Debug.Log("Dock NPC ready: use Animator bool IsRunning to switch Walk/Run. Move the NPC using your own movement code.");
    }
}
#endif
