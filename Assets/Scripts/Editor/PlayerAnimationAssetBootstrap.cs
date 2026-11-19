using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Non-destructive Editor bootstrapper creating the AnimatorController and 5 AnimationClip assets
/// for the Player (<c>Idle</c>, <c>Movement</c>, <c>Start</c>, <c>End</c>, <c>Death</c>) under
/// <c>Assets/Animation/Player/</c>. Never modifies Scenes or Prefabs (<c>AGENTS.md §1.1</c>).
/// </summary>
public static class PlayerAnimationAssetBootstrap
{
    public const string AnimationFolder = "Assets/Animation/Player";
    public const string ControllerAssetPath = AnimationFolder + "/PlayerAnimator.controller";
    public const string IdleClipPath = AnimationFolder + "/Player_Idle.anim";
    public const string MovementClipPath = AnimationFolder + "/Player_Movement.anim";
    public const string StartClipPath = AnimationFolder + "/Player_Start.anim";
    public const string EndClipPath = AnimationFolder + "/Player_End.anim";
    public const string DeathClipPath = AnimationFolder + "/Player_Death.anim";

    private const float ClipFrameRate = 12f;

    [MenuItem("Tools/Gravita/Bootstrap Player Animation Assets")]
    public static void BootstrapMenuCommand()
    {
        EnsurePlayerAnimationAssetsExist(forceRebuildController: true);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[PlayerAnimationAssetBootstrap] Bootstrapped PlayerAnimator.controller and 5 Player animation clips.");
    }

    public static AnimatorController EnsurePlayerAnimationAssetsExist(bool forceRebuildController = false)
    {
        EnsureFolderHierarchy(AnimationFolder);

        AnimationClip idleClip = EnsureClip(IdleClipPath, loopTime: true, durationSeconds: 0.50f);
        AnimationClip movementClip = EnsureClip(MovementClipPath, loopTime: true, durationSeconds: 0.25f);
        AnimationClip startClip = EnsureClip(StartClipPath, loopTime: false, durationSeconds: 0.35f);
        AnimationClip endClip = EnsureClip(EndClipPath, loopTime: false, durationSeconds: 0.45f);
        AnimationClip deathClip = EnsureClip(DeathClipPath, loopTime: false, durationSeconds: 0.45f);

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerAssetPath);
        if (controller != null && !forceRebuildController)
            return controller;

        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerAssetPath);

        RebuildControllerGraph(controller, idleClip, movementClip, startClip, endClip, deathClip);
        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static AnimationClip EnsureClip(string assetPath, bool loopTime, float durationSeconds)
    {
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
        bool isNew = clip == null;

        if (isNew)
        {
            clip = new AnimationClip
            {
                name = Path.GetFileNameWithoutExtension(assetPath),
                frameRate = ClipFrameRate
            };
        }

        // Preserve user-authored sprite/animation curves completely once artwork is dropped in.
        bool hasObjectCurves = AnimationUtility.GetObjectReferenceCurveBindings(clip).Length > 0;
        if (!isNew && hasObjectCurves)
            return clip;

        clip.frameRate = ClipFrameRate;

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loopTime;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        // Minimal SpriteRenderer.m_Enabled = 1 curve gives non-zero clip duration without
        // touching Transform or overwriting SpriteRenderer.sprite used by PlayerCosmeticApplier.
        bool hasCustomFloatCurves = HasNonPlaceholderFloatCurve(clip);
        if (isNew || !hasCustomFloatCurves)
        {
            var binding = EditorCurveBinding.FloatCurve(string.Empty, typeof(SpriteRenderer), "m_Enabled");
            var curve = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(durationSeconds, 1f));
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        if (isNew)
            AssetDatabase.CreateAsset(clip, assetPath);
        else
            EditorUtility.SetDirty(clip);

        return clip;
    }

    private static bool HasNonPlaceholderFloatCurve(AnimationClip clip)
    {
        EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
        for (int i = 0; i < bindings.Length; i++)
        {
            EditorCurveBinding b = bindings[i];
            if (b.type != typeof(SpriteRenderer) || b.propertyName != "m_Enabled")
                return true;
        }
        return false;
    }

    private static void RebuildControllerGraph(
        AnimatorController controller,
        AnimationClip idleClip,
        AnimationClip movementClip,
        AnimationClip startClip,
        AnimationClip endClip,
        AnimationClip deathClip)
    {
        for (int i = controller.layers.Length - 1; i >= 1; i--)
            controller.RemoveLayer(i);

        for (int i = controller.parameters.Length - 1; i >= 0; i--)
            controller.RemoveParameter(i);

        controller.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Start", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("End", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Death", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Reset", AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        ClearStateMachine(sm);

        AnimatorState idleState = sm.AddState("Idle", new Vector3(260f, 0f, 0f));
        idleState.motion = idleClip;

        AnimatorState movementState = sm.AddState("Movement", new Vector3(540f, 0f, 0f));
        movementState.motion = movementClip;

        AnimatorState startState = sm.AddState("Start", new Vector3(260f, -120f, 0f));
        startState.motion = startClip;

        AnimatorState endState = sm.AddState("End", new Vector3(540f, -120f, 0f));
        endState.motion = endClip;

        AnimatorState deathState = sm.AddState("Death", new Vector3(400f, 120f, 0f));
        deathState.motion = deathClip;

        sm.defaultState = idleState;

        AnimatorStateTransition idleToMove = idleState.AddTransition(movementState);
        ConfigureCrispTransition(idleToMove, hasExitTime: false);
        idleToMove.AddCondition(AnimatorConditionMode.If, 0f, "IsMoving");

        AnimatorStateTransition moveToIdle = movementState.AddTransition(idleState);
        ConfigureCrispTransition(moveToIdle, hasExitTime: false);
        moveToIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsMoving");

        AnimatorStateTransition startToMove = startState.AddTransition(movementState);
        ConfigureCrispTransition(startToMove, hasExitTime: false);
        startToMove.AddCondition(AnimatorConditionMode.If, 0f, "IsMoving");

        AnimatorStateTransition startToIdle = startState.AddTransition(idleState);
        ConfigureCrispTransition(startToIdle, hasExitTime: true, exitTime: 1f);

        AddAnyStateTriggerTransition(sm, startState, "Start", canTransitionToSelf: false);
        AddAnyStateTriggerTransition(sm, endState, "End", canTransitionToSelf: false);
        AddAnyStateTriggerTransition(sm, deathState, "Death", canTransitionToSelf: false);
        AddAnyStateTriggerTransition(sm, idleState, "Reset", canTransitionToSelf: true);
    }

    private static void ClearStateMachine(AnimatorStateMachine sm)
    {
        AnimatorStateTransition[] anyTransitions = sm.anyStateTransitions;
        for (int i = anyTransitions.Length - 1; i >= 0; i--)
            sm.RemoveAnyStateTransition(anyTransitions[i]);

        ChildAnimatorState[] states = sm.states;
        for (int i = states.Length - 1; i >= 0; i--)
            sm.RemoveState(states[i].state);

        ChildAnimatorStateMachine[] subMachines = sm.stateMachines;
        for (int i = subMachines.Length - 1; i >= 0; i--)
            sm.RemoveStateMachine(subMachines[i].stateMachine);
    }

    private static void AddAnyStateTriggerTransition(
        AnimatorStateMachine sm,
        AnimatorState destination,
        string triggerParam,
        bool canTransitionToSelf)
    {
        AnimatorStateTransition t = sm.AddAnyStateTransition(destination);
        ConfigureCrispTransition(t, hasExitTime: false, canTransitionToSelf: canTransitionToSelf);
        t.AddCondition(AnimatorConditionMode.If, 0f, triggerParam);
    }

    private static void ConfigureCrispTransition(
        AnimatorStateTransition transition,
        bool hasExitTime,
        float exitTime = 1f,
        bool canTransitionToSelf = false)
    {
        transition.hasExitTime = hasExitTime;
        transition.exitTime = exitTime;
        transition.hasFixedDuration = true;
        transition.duration = 0f;
        transition.offset = 0f;
        transition.interruptionSource = TransitionInterruptionSource.None;
        transition.canTransitionToSelf = canTransitionToSelf;
    }

    private static void EnsureFolderHierarchy(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        string[] parts = folderPath.Replace('\\', '/').Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
