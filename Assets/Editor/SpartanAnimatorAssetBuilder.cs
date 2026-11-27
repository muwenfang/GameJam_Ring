#if UNITY_EDITOR
using System;
using System.Linq;
using Ring.Characters;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>在 Unity 导入分镜 Sprite 后，自动生成斯巴达动画剪辑及 Animator Controller。</summary>
internal static class SpartanAnimatorAssetBuilder
{
    private const string Folder = "Assets/Resources/Spartan";
    private const string ControllerPath = Folder + "/Spartan.controller";

    [InitializeOnLoadMethod]
    private static void BuildOnEditorLoad() => EditorApplication.delayCall += Build;

    [MenuItem("钟声/角色动画/生成斯巴达 Animator 资源")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) return;
        string[] names = { "Idle", "Run", "Thrust", "Sweep", "Shield" };
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null
            && names.All(n => AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/" + n + ".anim") != null)) return;
        var clips = names.ToDictionary(n => n, CreateClip);
        if (clips.Any(pair => pair.Value == null)) return;

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller != null) AssetDatabase.DeleteAsset(ControllerPath);
        controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Moving", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Thrust", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Sweep", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Shield", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Shielding", AnimatorControllerParameterType.Bool);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AnimatorState idle = machine.AddState("Idle"); idle.motion = clips["Idle"];
        machine.defaultState = idle;
        AnimatorState run = machine.AddState("Run"); run.motion = clips["Run"];
        AnimatorState thrust = AddAction(machine, "Thrust", clips["Thrust"], idle, "Thrust");
        AnimatorState sweep = AddAction(machine, "Sweep", clips["Sweep"], idle, "Sweep");
        AnimatorState shield = machine.AddState("Shield"); shield.motion = clips["Shield"];
        AnimatorStateTransition shieldEnter = machine.AddAnyStateTransition(shield);
        shieldEnter.hasExitTime = false; shieldEnter.duration = 0.05f; shieldEnter.AddCondition(AnimatorConditionMode.If, 0, "Shield");
        AnimatorStateTransition shieldExit = shield.AddTransition(idle);
        shieldExit.hasExitTime = false; shieldExit.duration = 0.05f; shieldExit.AddCondition(AnimatorConditionMode.IfNot, 0, "Shielding");
        AnimatorStateTransition toRun = idle.AddTransition(run); toRun.hasExitTime = false; toRun.AddCondition(AnimatorConditionMode.If, 0, "Moving");
        AnimatorStateTransition toIdle = run.AddTransition(idle); toIdle.hasExitTime = false; toIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "Moving");
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
    }

    private static AnimatorState AddAction(AnimatorStateMachine machine, string name, AnimationClip clip, AnimatorState idle, string trigger)
    {
        AnimatorState state = machine.AddState(name); state.motion = clip;
        AnimatorStateTransition enter = machine.AddAnyStateTransition(state);
        enter.hasExitTime = false; enter.duration = 0.05f; enter.AddCondition(AnimatorConditionMode.If, 0, trigger);
        AnimatorStateTransition exit = state.AddTransition(idle); exit.hasExitTime = true; exit.exitTime = 1f; exit.duration = 0.05f;
        return state;
    }

    private static AnimationClip CreateClip(string name)
    {
        string path = Folder + "/" + name + ".anim";
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = new AnimationClip { name = "Spartan_" + name }; AssetDatabase.CreateAsset(clip, path); }
        string framePrefix = name == "Idle" ? "Thrust" : name;
        Sprite[] frames = AssetDatabase.FindAssets("t:Sprite " + framePrefix, new[] { Folder })
            .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<Sprite>)
            .Where(s => s != null && s.name.StartsWith(framePrefix + "_", StringComparison.Ordinal))
            .OrderBy(s => s.name, StringComparer.Ordinal).ToArray();
        if (frames.Length == 0) return null;
        if (name == "Idle") frames = new[] { frames[0] };
        var keys = frames.Select((sprite, i) => new ObjectReferenceKeyframe { time = i / 12f, value = sprite }).ToArray();
        var binding = EditorCurveBinding.PPtrCurve(string.Empty, typeof(SpriteRenderer), "m_Sprite");
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
        clip.frameRate = 12f;
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = name == "Run" || name == "Idle";
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }
}
#endif
